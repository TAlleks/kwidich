using LogitechG29.Sample.Input;
using UnityEngine;

public class BroomController : MonoBehaviour
{
    #region Inspector Fields & Serialized Fields

    [Header("Input")]
    [SerializeField] private InputControllerReader inputControllerReader;

    [Header("Broom Physics")]
    public float baseMaxSpeed = 15f;
    public float acceleration = 8f;
    public float steeringSensitivity = 3f;
    public float liftForce = 20f;
    public float hoverHeight = 2f;
    public float verticalAcceleration = 10f;
    public float maxVerticalSpeed = 12f;

    [Header("Steering")]
    [SerializeField] private float steeringSpeed = 6f;

    [Header("Gearbox - 7 Gears")]
    public int currentGear = 0;
    public float gearShiftCooldown = 0.3f;

    [Header("Team")]
    public Team team = Team.Player;

    [Header("References")]
    public Transform broomModel;
    public Rigidbody rb;
    private Camera _mainCamera;
    #endregion

    #region Private Fields

    // Steering
    private float currentSteeringAngle;
    private float targetSteeringAngle;

    // Movement & Physics
    private float lastGearShiftTime;
    private float currentSpeed;
    private bool isGrounded;
    private float stableHeight;
    private Vector3 flightDirection;
    public Quaffle quaffle;

    // Collision recovery
    private bool isRecoveringFromCollision;
    private float collisionRecoveryTimer;

    // Gear System
    private readonly GearSettings[] gearSettings = new GearSettings[]
    {
        new GearSettings { type = MovementType.Vertical,   description = "Вверх",       speedMultiplier = 1f },
        new GearSettings { type = MovementType.Vertical,   description = "Вниз",        speedMultiplier = 1f },
        new GearSettings { type = MovementType.Horizontal, description = "Медленно",    speedMultiplier = 0.5f },
        new GearSettings { type = MovementType.Horizontal, description = "Средне",      speedMultiplier = 1.0f },
        new GearSettings { type = MovementType.Horizontal, description = "Быстро",      speedMultiplier = 1.5f },
        new GearSettings { type = MovementType.Horizontal, description = "Очень быстро",speedMultiplier = 2.0f },
        new GearSettings { type = MovementType.Horizontal, description = "Назад",       speedMultiplier = -1.0f }
    };

    #endregion

    #region Unity Lifecycle Methods

    private void Start()
    {
        InitializeBroom();
        _mainCamera = Camera.main;
    }

    void Update()
    {
        HandleInput();
        CheckGround();
        HandleGearShifting();
        UpdateSteering();
        HandleCollisionRecovery();
        ThrowBallInput();
    }

    void FixedUpdate()
    {
        ApplyMovement();
        ApplyHeightControl(); // ← единый контроллер высоты
    }

    void OnCollisionEnter(Collision col)
    {
        HandleCollision(col);
    }

    #endregion

    #region Initialization & Setup

    private void InitializeBroom()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            Debug.LogWarning("Rigidbody добавлен динамически к " + name);
        }

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.useGravity = false;

        currentGear = 0;
        flightDirection = transform.forward;

        // Устанавливаем начальную высоту
        SetInitialHeight();
        stableHeight = transform.position.y;
    }

    private void SetInitialHeight()
    {
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 10f))
        {
            transform.position = new Vector3(transform.position.x, hit.point.y + hoverHeight, transform.position.z);
        }
        else
        {
            transform.position = new Vector3(transform.position.x, hoverHeight, transform.position.z);
        }
    }

    #endregion

    #region Input Handling

    void HandleInput()
    {
        if (inputControllerReader == null)
        {
            Debug.LogError("InputControllerReader не назначен на " + name, this);
            return;
        }

        float steering = inputControllerReader.Steering;
        float throttle = inputControllerReader.Throttle;
        float brake = inputControllerReader.Brake;

        targetSteeringAngle = steering * 45f;

        if (gearSettings[currentGear].type == MovementType.Horizontal)
        {
            float maxSpd = baseMaxSpeed * Mathf.Abs(gearSettings[currentGear].speedMultiplier);
            float targetSpd = throttle * maxSpd;

            if (brake > 0.1f) targetSpd = 0f;

            // Плавное ускорение/торможение
            currentSpeed = Mathf.Lerp(currentSpeed, targetSpd, acceleration * Time.deltaTime);
        }
    }

    void HandleGearShifting()
    {
        if (inputControllerReader == null) return;

        bool canShift = (Time.time - lastGearShiftTime) > gearShiftCooldown;
        //bool shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.JoystickButton4);

        if (canShift)
        {
            if (inputControllerReader.Shifter1) ShiftGear(0);
            else if (inputControllerReader.Shifter2) ShiftGear(1);
            else if (inputControllerReader.Shifter3) ShiftGear(2);
            else if (inputControllerReader.Shifter4) ShiftGear(3);
            else if (inputControllerReader.Shifter5) ShiftGear(4);
            else if (inputControllerReader.Shifter6) ShiftGear(5);
            else if (inputControllerReader.Shifter7) ShiftGear(6);
        }
    }

    void ShiftGear(int gear)
    {
        if (gear >= 0 && gear <= 6 && gear != currentGear)
        {
            currentGear = gear;
            Debug.Log($"[Broom] Передача {currentGear}: {gearSettings[gear].description}", this);
        }
    }
    void ThrowBallInput()
    {
        if (inputControllerReader == null) return;

        // Бросок по нажатию сцепления (Clutch >= 0.8f)
        if (inputControllerReader.Clutch >= 0.8f && quaffle != null && quaffle.isHeld)
        {
            ThrowBall();
        }
    }

    void ThrowBall()
    {
        if (quaffle == null || !quaffle.isHeld) return;

        // === ОПРЕДЕЛЯЕМ НАПРАВЛЕНИЕ БРОСКА ===
        Vector3 throwDirection;

        if (_mainCamera != null)
        {
            // Бросок туда, куда смотрит камера (как в шутерах)
            throwDirection = _mainCamera.transform.forward;
        }
        else
        {
            // Если камеры нет — бросаем вперёд по направлению полёта
            throwDirection = flightDirection;
        }

        // Убираем вертикальную компоненту, если хотите "горизонтальный" бросок
        // throwDirection.y = 0f;

        // Нормализуем и добавляем лёгкий подъём (как в квиддиче)
        throwDirection = throwDirection.normalized;
        throwDirection.y = Mathf.Max(throwDirection.y, 0.1f); // минимальный подъём

        // Передаём направление в общий метод броска
        quaffle.Throw(throwDirection);
        Debug.Log("Бросил мяч");

        // Опционально: сброс ссылки на мяч у игрока
        quaffle = null;
    }


    #endregion

    #region Physics & Movement

    void CheckGround()
    {
        // Расширяем проверку до 1.5 * hoverHeight — надёжнее
        isGrounded = Physics.Raycast(transform.position, Vector3.down, hoverHeight * 1.5f);
    }

    void UpdateSteering()
    {
        currentSteeringAngle = Mathf.Lerp(currentSteeringAngle, targetSteeringAngle, steeringSpeed * Time.deltaTime);

        if (gearSettings[currentGear].type == MovementType.Horizontal && !isRecoveringFromCollision)
        {
            ApplyBroomTilt();
        }
    }

    private void ApplyBroomTilt()
    {
        if (broomModel != null)
        {
            float tiltZ = -currentSteeringAngle * 0.8f; // крен в поворот
            float tiltX = Mathf.Sign(currentSpeed) * Mathf.Clamp(Mathf.Abs(currentSpeed) / baseMaxSpeed, 0f, 1f) * 8f; // наклон вперёд/назад

            Quaternion targetTilt = Quaternion.Euler(tiltX, 0f, tiltZ);
            broomModel.localRotation = Quaternion.Slerp(broomModel.localRotation, targetTilt, Time.deltaTime * 10f);
        }
    }

    void ApplyMovement()
    {
        if (gearSettings[currentGear].type != MovementType.Horizontal || isRecoveringFromCollision)
            return;

        if (currentSpeed < 0.01f)
            return;

        float direction = Mathf.Sign(gearSettings[currentGear].speedMultiplier);

        // 1. ПОВОРОТ НАПРАВЛЕНИЯ (а не transform)
        float turnRate = currentSteeringAngle * steeringSensitivity * Time.fixedDeltaTime;
        Quaternion turnRot = Quaternion.Euler(0f, turnRate, 0f);
        flightDirection = turnRot * flightDirection;
        flightDirection.Normalize();

        // 2. ЗАДАЁМ СКОРОСТЬ
        Vector3 velocity = flightDirection * currentSpeed * direction;
        rb.linearVelocity = new Vector3(
            velocity.x,
            rb.linearVelocity.y,
            velocity.z
        );

        // 3. ПОВОРАЧИВАЕМ МЕТЛУ В НАПРАВЛЕНИЕ ПОЛЁТА
        Quaternion targetRot = Quaternion.LookRotation(flightDirection, Vector3.up);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, Time.fixedDeltaTime * 6f));
    }



    void ApplyHeightControl()
    {
        // 1. Целевая высота
        float targetY = stableHeight;

        if (currentGear == 0) // Вверх
            targetY += verticalAcceleration * Time.fixedDeltaTime;
        else if (currentGear == 1) // Вниз
            targetY -= verticalAcceleration * Time.fixedDeltaTime;

        targetY = Mathf.Clamp(targetY, 1f, 50f);
        stableHeight = targetY;

        // 2. Желаемая вертикальная скорость (П-регулятор)
        float heightError = targetY - transform.position.y;
        float desiredVerticalSpeed = Mathf.Clamp(heightError * 6f, -maxVerticalSpeed, maxVerticalSpeed);

        // 3. Плавный переход
        float currentVertVel = rb.linearVelocity.y;
        float newVertVel = Mathf.Lerp(currentVertVel, desiredVerticalSpeed, Time.fixedDeltaTime * 8f);

        // 4. "Подушка" при близости к земле
        if (isGrounded && gearSettings[currentGear].type == MovementType.Horizontal)
        {
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, hoverHeight * 2f))
            {
                float cushion = Mathf.Max(0f, 1f - hit.distance / hoverHeight);
                newVertVel += liftForce * cushion * Time.fixedDeltaTime;
            }
        }

        // 5. Устанавливаем итоговую скорость
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, newVertVel, rb.linearVelocity.z);
    }

    #endregion

    #region Collision & Recovery

    void HandleCollision(Collision col)
    {
        if (col.relativeVelocity.magnitude > 1.5f)
        {
            ApplyCollisionResponse(col);
            StartCollisionRecovery();
        }
    }

    private void ApplyCollisionResponse(Collision col)
    {
        Vector3 impactPoint = rb.worldCenterOfMass;

        // Мягкий отскок вверх + гашение
        Vector3 bounce = Vector3.up * 3f + (-rb.linearVelocity.normalized) * 1f;
        rb.AddForceAtPosition(bounce * 2f, impactPoint, ForceMode.Impulse);

        // Демпфирование
        rb.linearVelocity *= 0.3f;
        rb.angularVelocity *= 0.1f;
    }

    private void StartCollisionRecovery()
    {
        isRecoveringFromCollision = true;
        collisionRecoveryTimer = 1f;
    }

    void HandleCollisionRecovery()
    {
        if (isRecoveringFromCollision)
        {
            collisionRecoveryTimer -= Time.deltaTime;
            if (collisionRecoveryTimer <= 0f)
                isRecoveringFromCollision = false;
        }
    }

    #endregion

    #region Helper Classes & Enums

    [System.Serializable]
    public class GearSettings
    {
        public MovementType type;
        public string description;
        public float speedMultiplier = 1f;
    }

    public enum MovementType { Horizontal, Vertical }

    #endregion

    #region Public Methods

    public void ResetBroom()
    {
        currentGear = 0;
        currentSpeed = 0f;
        isRecoveringFromCollision = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        SetInitialHeight();
        stableHeight = transform.position.y;
    }

    public void BoostSpeed(float multiplier, float duration)
    {
        StartCoroutine(SpeedBoostRoutine(multiplier, duration));
    }

    private System.Collections.IEnumerator SpeedBoostRoutine(float multiplier, float duration)
    {
        float originalSpeed = baseMaxSpeed;
        baseMaxSpeed *= multiplier;
        Debug.Log($"[Broom] Буст скорости ×{multiplier} на {duration} сек", this);

        yield return new WaitForSeconds(duration);

        baseMaxSpeed = originalSpeed;
        Debug.Log("[Broom] Буст окончен", this);
    }

    public void SetInputController(InputControllerReader controller)
    {
        inputControllerReader = controller;
    }

    public float GetCurrentSpeed() => currentSpeed;
    public int GetCurrentGear() => currentGear;

    #endregion
}