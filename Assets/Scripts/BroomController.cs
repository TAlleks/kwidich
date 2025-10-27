using System;
using System.Collections.Generic;
using LogitechG29.Sample.Input;
using UnityEngine;

public class BroomController : MonoBehaviour
{
    [SerializeField] private InputControllerReader inputControllerReader;

    [Header("Broom Physics")]
    public float maxSpeed = 20f;
    public float acceleration = 5f;
    public float steeringSensitivity = 2f;
    public float liftForce = 15f;
    public float hoverHeight = 2f;
    public float verticalAcceleration = 8f;
    public float maxVerticalSpeed = 10f;
    public float heightStabilizationForce = 20f;

    [Header("Levitation Settings")]
    public float levitationFrequency = 1f;
    public float levitationAmplitude = 0.2f;
    public float movementLevitationReduction = 0.5f;
    public float levitationSmoothness = 2f;

    [Header("Gearbox Settings")]
    public int currentGear = 1;
    public int maxGear = 4;
    public float gearShiftCooldown = 0.5f;

    [Header("Height Settings")]
    public float maxHeight = 50f;
    public float minHeight = 1f;

    [Header("References")]
    public Transform broomModel;

    private Rigidbody rb;
    private float currentSpeed;
    private float currentVerticalSpeed;
    private bool isGrounded;
    private float lastGearShiftTime;
    private float targetHeight;
    private bool isHeightLocked = false;
    private float stableHeight;
    private float levitationTimer;
    private float currentLevitationOffset;
    private float targetLevitationOffset;
    private Vector3 lastPosition;
    private bool isMoving;

    // Настройки для каждой передачи
    private readonly Dictionary<int, GearSettings> gearSettings = new Dictionary<int, GearSettings>()
    {
        { 1, new GearSettings { movementType = MovementType.Horizontal, description = "Вперед" } },
        { 2, new GearSettings { movementType = MovementType.Vertical, description = "Вверх" } },
        { 3, new GearSettings { movementType = MovementType.Vertical, description = "Вниз" } },
        { 4, new GearSettings { movementType = MovementType.Horizontal, description = "Назад" } }
    };

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        currentGear = 1;
        targetHeight = transform.position.y;
        stableHeight = transform.position.y;
        lastPosition = transform.position;
    }

    void Update()
    {
        HandleInput();
        CheckGround();
        HandleGearShifting();
        UpdateHeightLock();
        UpdateStableHeight();
        CheckMovement();
        UpdateLevitation();
    }

    void FixedUpdate()
    {
        ApplyHover();
        ApplyMovement();
        ApplyVerticalMovement();
        StabilizeHeight();
        ApplyLevitation();
    }

    private void HandleInput()
    {
        float steering = inputControllerReader.Steering;
        float throttle = inputControllerReader.Throttle;
        float brake = inputControllerReader.Brake;

        ApplySteering(steering);
        ApplyThrottle(throttle, brake);
        HandleSpecialControls();
    }

    private void HandleGearShifting()
    {

        if (inputControllerReader.Shifter1 &&
            Time.time - lastGearShiftTime > gearShiftCooldown)
        {
            ShiftGear(1);
        }

        if (inputControllerReader.Shifter2 &&
     Time.time - lastGearShiftTime > gearShiftCooldown)
        {
            ShiftGear(2);
        }

        if (inputControllerReader.Shifter3 &&
     Time.time - lastGearShiftTime > gearShiftCooldown)
        {
            ShiftGear(3);
        }

        if (inputControllerReader.Shifter4 &&
     Time.time - lastGearShiftTime > gearShiftCooldown)
        {
            ShiftGear(4);
        }

    }

    private void CheckMovement()
    {
        // Проверяем, движется ли метла
        Vector3 positionDelta = transform.position - lastPosition;
        isMoving = positionDelta.magnitude > 0.01f || Mathf.Abs(currentSpeed) > 0.1f;
        lastPosition = transform.position;
    }

    private void UpdateLevitation()
    {
        // Обновляем таймер левитации
        levitationTimer += Time.deltaTime * levitationFrequency;

        // Вычисляем целевое смещение левитации
        float baseLevitation = Mathf.Sin(levitationTimer) * levitationAmplitude;

        // Уменьшаем амплитуду при движении
        float movementReduction = isMoving ? movementLevitationReduction : 1f;
        targetLevitationOffset = baseLevitation * movementReduction;

        // Плавно интерполируем к целевому смещению
        currentLevitationOffset = Mathf.Lerp(currentLevitationOffset, targetLevitationOffset,
                                           Time.deltaTime * levitationSmoothness);
    }

    private void ApplyLevitation()
    {
        // Применяем левитацию только когда метла не на земле и на горизонтальных передачах
        if (!isGrounded && (currentGear == 1 || currentGear == 4))
        {
            // Добавляем небольшую силу левитации
            Vector3 levitationForce = Vector3.up * currentLevitationOffset * 2f;
            rb.AddForce(levitationForce, ForceMode.Acceleration);
        }
    }

    private void ShiftGear(int direction)
    {
        int newGear = direction;
        SetGear(newGear);
    }

    private void SetGear(int gear)
    {
        if (gear >= 1 && gear <= maxGear && gear != currentGear)
        {
            currentGear = gear;
            lastGearShiftTime = Time.time;
            Debug.Log($"Переключение на передачу: {currentGear} - {gearSettings[currentGear].description}");

            // Сброс скорости при переключении передачи
            currentSpeed = 0f;

            // При переключении на вертикальные передачи фиксируем текущую высоту
            if (gear == 2 || gear == 3)
            {
                LockCurrentHeight();
            }
            else
            {
                isHeightLocked = false;
                // При переключении на горизонтальные передачи запоминаем текущую высоту как стабильную
                stableHeight = transform.position.y;
            }

            Debug.Log($"Переключение на передачу: {currentGear} - {gearSettings[currentGear].description}");
        }
    }

    private void UpdateHeightLock()
    {
        // Для вертикальных передач обновляем целевую высоту в зависимости от педалей
        if (currentGear == 2 || currentGear == 3)
        {
            float throttle = inputControllerReader.Throttle;
            float brake = inputControllerReader.Brake;

            if (currentGear == 2) // Подъем
            {
                if (throttle > 0.1f)
                {
                    targetHeight += throttle * verticalAcceleration * Time.deltaTime;
                }
                if (brake > 0.1f)
                {
                    targetHeight -= brake * verticalAcceleration * Time.deltaTime;
                }
            }
            else if (currentGear == 3) // Спуск
            {
                if (throttle > 0.1f)
                {
                    targetHeight -= throttle * verticalAcceleration * Time.deltaTime;
                }
                if (brake > 0.1f)
                {
                    targetHeight += brake * verticalAcceleration * Time.deltaTime;
                }
            }

            // Ограничиваем высоту
            targetHeight = Mathf.Clamp(targetHeight, minHeight, maxHeight);
            stableHeight = targetHeight; // Обновляем стабильную высоту
        }
    }

    private void UpdateStableHeight()
    {
        // На горизонтальных передачах стабильная высота - текущая высота + левитация
        if (currentGear == 1 || currentGear == 4)
        {
            stableHeight = transform.position.y - currentLevitationOffset;
        }
    }

    private void LockCurrentHeight()
    {
        targetHeight = transform.position.y;
        stableHeight = targetHeight;
        isHeightLocked = true;
    }

    private void ApplySteering(float steeringInput)
    {
        // Поворот работает только на горизонтальных передачах
        if (gearSettings[currentGear].movementType == MovementType.Horizontal)
        {
            float turnAmount = steeringInput * steeringSensitivity * Time.deltaTime;
            transform.Rotate(0, turnAmount, 0);
        }

        // Наклон метлы с учетом левитации
        if (broomModel != null)
        {
            float targetTilt = steeringInput * 30f;

            // Добавляем небольшой наклон от левитации
            float levitationTilt = Mathf.Sin(levitationTimer * 1.5f) * 5f;

            Quaternion targetRotation = Quaternion.Euler(levitationTilt, 0, -targetTilt);
            broomModel.localRotation = Quaternion.Lerp(broomModel.localRotation, targetRotation, Time.deltaTime * 5f);
        }
    }

    private void ApplyThrottle(float throttle, float brake)
    {
        // Горизонтальное ускорение только для горизонтальных передач
        if (gearSettings[currentGear].movementType == MovementType.Horizontal)
        {
            float targetSpeed = throttle * maxSpeed;

            // Торможение работает для всех передач
            if (brake > 0.1f)
            {
                targetSpeed = 0f;
                currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, brake * 2f * Time.deltaTime);
            }
            else
            {
                currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, acceleration * Time.deltaTime);
            }
        }
        else
        {
            // Для вертикальных передач сбрасываем горизонтальную скорость
            currentSpeed = 0f;
        }
    }

    private void ApplyMovement()
    {
        // Горизонтальное движение только для передач 1 и 4
        if (gearSettings[currentGear].movementType == MovementType.Horizontal)
        {
            Vector3 moveDirection = transform.forward * currentSpeed;

            // Определяем направление для передачи 4 (назад)
            if (currentGear == 4)
            {
                moveDirection = -moveDirection;
            }

            // Сохраняем текущую Y-скорость чтобы не терять высоту
            rb.linearVelocity = new Vector3(moveDirection.x, rb.linearVelocity.y, moveDirection.z);
        }
        else
        {
            // Для вертикальных передач обнуляем горизонтальную скорость
            rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0);
        }
    }

    private void ApplyVerticalMovement()
    {
        // Вертикальное движение для передач 2 и 3
        if (currentGear == 2 || currentGear == 3)
        {
            float heightDifference = targetHeight - transform.position.y;
            float verticalForce = heightDifference * verticalAcceleration;

            // Ограничиваем максимальную вертикальную скорость
            verticalForce = Mathf.Clamp(verticalForce, -maxVerticalSpeed, maxVerticalSpeed);

            rb.linearVelocity = new Vector3(rb.linearVelocity.x, verticalForce, rb.linearVelocity.z);
        }
    }

    private void StabilizeHeight()
    {
        // Стабилизация высоты для горизонтальных передач
        if ((currentGear == 1 || currentGear == 4) && !isGrounded)
        {
            float heightDifference = stableHeight - transform.position.y;
            if (Mathf.Abs(heightDifference) > 0.1f)
            {
                float stabilizationForce = heightDifference * heightStabilizationForce;
                rb.AddForce(Vector3.up * stabilizationForce, ForceMode.Acceleration);
            }
        }
    }

    private void ApplyHover()
    {
        // Hover работает только на горизонтальных передачах и когда мы близко к земле
        if (gearSettings[currentGear].movementType == MovementType.Horizontal && isGrounded)
        {
            RaycastHit hit;
            if (Physics.Raycast(transform.position, Vector3.down, out hit, hoverHeight * 2f))
            {
                float hoverForce = liftForce * (1f - (hit.distance / hoverHeight));
                rb.AddForce(Vector3.up * hoverForce, ForceMode.Acceleration);
            }
        }
    }

    private void HandleSpecialControls()
    {
        // Экстренный подъем (работает на любой передаче)
        if (Input.GetKey(KeyCode.JoystickButton0) || Input.GetKey(KeyCode.Space))
        {
            rb.AddForce(Vector3.up * liftForce * 2f, ForceMode.Impulse);
        }

        // Принудительная фиксация высоты (например, кнопка X)
        if (Input.GetKeyDown(KeyCode.JoystickButton2) || Input.GetKeyDown(KeyCode.X))
        {
            LockCurrentHeight();
            Debug.Log("Высота зафиксирована: " + targetHeight);
        }
    }

    private void CheckGround()
    {
        RaycastHit hit;
        isGrounded = Physics.Raycast(transform.position, Vector3.down, out hit, hoverHeight + 0.2f);
    }

    // Вспомогательные классы и enum
    [System.Serializable]
    public class GearSettings
    {
        public MovementType movementType;
        public string description;
    }

    public enum MovementType
    {
        Horizontal,
        Vertical
    }

    public float GetCurrentSpeed()
    {
        return currentSpeed;
    }

    public bool IsGrounded()
    {
        return isGrounded;
    }

    public int GetCurrentGear()
    {
        return currentGear;
    }

    public string GetGearDescription()
    {
        return gearSettings.ContainsKey(currentGear) ? gearSettings[currentGear].description : "Unknown";
    }

    public float GetCurrentHeight()
    {
        return transform.position.y;
    }

    public float GetTargetHeight()
    {
        return targetHeight;
    }

    public bool IsMoving()
    {
        return isMoving;
    }

    void OnDrawGizmosSelected()
    {
        // Визуализация hover высоты
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * hoverHeight);

        // Визуализация целевой высоты для вертикальных передач
        if (currentGear == 2 || currentGear == 3)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(new Vector3(transform.position.x - 1f, targetHeight, transform.position.z),
                           new Vector3(transform.position.x + 1f, targetHeight, transform.position.z));
        }

        // Визуализация стабильной высоты для горизонтальных передач
        if (currentGear == 1 || currentGear == 4)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(new Vector3(transform.position.x - 1f, stableHeight, transform.position.z),
                           new Vector3(transform.position.x + 1f, stableHeight, transform.position.z));
        }

        // Визуализация левитации
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * currentLevitationOffset);

        // Визуализация направления движения
        Gizmos.color = Color.red;
        if (gearSettings.ContainsKey(currentGear) && gearSettings[currentGear].movementType == MovementType.Horizontal)
        {
            Vector3 direction = currentGear == 1 ? transform.forward : -transform.forward;
            Gizmos.DrawLine(transform.position, transform.position + direction * 2f);
        }

        // Отображение текущей передачи и высоты
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.white;
#if UNITY_EDITOR
        string info = $"Gear: {currentGear} - {GetGearDescription()}\nHeight: {transform.position.y:F1}";
        if (currentGear == 2 || currentGear == 3)
        {
            info += $"\nTarget: {targetHeight:F1}";
        }
        else
        {
            info += $"\nStable: {stableHeight:F1}";
        }
        info += $"\nLevitation: {currentLevitationOffset:F2}";
        info += $"\nMoving: {isMoving}";
        UnityEditor.Handles.Label(transform.position + Vector3.up * 3f, info, style);
#endif
    }
}