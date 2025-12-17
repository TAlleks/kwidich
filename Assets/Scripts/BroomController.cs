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

    [Header("Ball Interaction")]
    public Quaffle quaffle; // Текущий мяч в руках
    public bool hasBall = false; // Флаг: есть ли мяч
    public float pickupCooldown = 1.0f; // Задержка перед повторным подбором
    #endregion

    #region Private Fields
    // Ball Logic
    private float lastThrowTime; // Время последнего броска
    // Steering
    private float currentSteeringAngle;
    private float targetSteeringAngle;

    // Movement & Physics
    private float lastGearShiftTime;
    private float currentSpeed;
    private bool isGrounded;
    private float stableHeight;
    private Vector3 flightDirection;
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

        // === ИСПРАВЛЕНИЕ: Обработка нейтрали ===
        if (currentGear == -1)
        {
            // На нейтрали просто плавно сбрасываем скорость (имитация сопротивления воздуха)
            currentSpeed = Mathf.Lerp(currentSpeed, 0f, 2f * Time.deltaTime);

            // Руль все равно считываем, чтобы поворачивать по инерции
            float steeringNeutral = inputControllerReader.Steering;
            targetSteeringAngle = steeringNeutral * 45f;
            return;
        }

        float steering = inputControllerReader.Steering;
        float throttle = inputControllerReader.Throttle;
        float brake = inputControllerReader.Brake;

        targetSteeringAngle = steering * 45f;

        // Безопасная проверка массива
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

        // === ИСПРАВЛЕНИЕ: Логика нейтрали ===

        // 1. По умолчанию считаем, что ручка в нейтрали (-1)
        int targetGear = -1;

        // 2. Если нажата хоть одна кнопка — запоминаем передачу
        if (inputControllerReader.Shifter1) targetGear = 0;
        else if (inputControllerReader.Shifter2) targetGear = 1;
        else if (inputControllerReader.Shifter3) targetGear = 2;
        else if (inputControllerReader.Shifter4) targetGear = 3;
        else if (inputControllerReader.Shifter5) targetGear = 4;
        else if (inputControllerReader.Shifter6) targetGear = 5;
        else if (inputControllerReader.Shifter7) targetGear = 6;

        // 3. Если целевая передача отличается от текущей — переключаем
        if (targetGear != currentGear)
        {
            // Проверка кулдауна
            if ((Time.time - lastGearShiftTime) > gearShiftCooldown)
            {
                ShiftGear(targetGear);
                lastGearShiftTime = Time.time;
            }
        }
    }

    void ShiftGear(int gear)
    {
        currentGear = gear;

        // === ИСПРАВЛЕНИЕ: Защита от вылета массива при -1 ===
        if (currentGear == -1)
        {
            Debug.Log("[Broom] ⚪ Нейтраль", this);
        }
        else if (gear >= 0 && gear < gearSettings.Length)
        {
            Debug.Log($"[Broom] ⚙ Передача {currentGear}: {gearSettings[gear].description}", this);
        }
    }

    void ThrowBallInput()
    {
        if (inputControllerReader == null) return;

        if (inputControllerReader.Clutch >= 0.8f && hasBall && quaffle != null && quaffle.isHeld)
        {
            ThrowBall();
        }
    }

    void ThrowBall()
    {
        if (quaffle == null || !quaffle.isHeld) return;

        Vector3 throwDirection;

        if (_mainCamera != null)
        {
            throwDirection = _mainCamera.transform.forward;
        }
        else
        {
            throwDirection = flightDirection;
        }

        throwDirection = throwDirection.normalized;
        throwDirection.y = Mathf.Max(throwDirection.y, 0.1f);

        quaffle.rb.mass = 1;
        quaffle.Throw(throwDirection);

        lastThrowTime = Time.time;
        SetHasBall(false, null);
    }

    public void SetHasBall(bool value, Quaffle incomingQuaffle)
    {
        if (value == true)
        {
            if (Time.time < lastThrowTime + pickupCooldown)
            {
                Log("❌ Не могу взять мяч — кулдаун");
                return;
            }
            Log("✅ Взял мяч");
        }
        else
        {
            Log("❌ Бросил / Потерял мяч");
        }

        hasBall = value;
        quaffle = value ? incomingQuaffle : null;
    }

    private void Log(string message)
    {
        Debug.Log($"[BroomLogic] {message}", this);
    }

    #endregion

    #region Physics & Movement

    void CheckGround()
    {
        isGrounded = Physics.Raycast(transform.position, Vector3.down, hoverHeight * 1.5f);
    }

    void UpdateSteering()
    {
        currentSteeringAngle = Mathf.Lerp(currentSteeringAngle, targetSteeringAngle, steeringSpeed * Time.deltaTime);

        // === ИСПРАВЛЕНИЕ: Проверка на нейтраль перед наклоном ===
        if (currentGear != -1 && !isRecoveringFromCollision)
        {
            // Наклоняем только если это горизонтальная передача
            if (gearSettings[currentGear].type == MovementType.Horizontal)
            {
                ApplyBroomTilt();
            }
        }
    }

    private void ApplyBroomTilt()
    {
        if (broomModel != null)
        {
            float tiltZ = -currentSteeringAngle * 0.8f;
            float tiltX = Mathf.Sign(currentSpeed) * Mathf.Clamp(Mathf.Abs(currentSpeed) / baseMaxSpeed, 0f, 1f) * 8f;

            Quaternion targetTilt = Quaternion.Euler(tiltX, 0f, tiltZ);
            broomModel.localRotation = Quaternion.Slerp(broomModel.localRotation, targetTilt, Time.deltaTime * 10f);
        }
    }

    void ApplyMovement()
    {
        // === ИСПРАВЛЕНИЕ: Если нейтраль — не применяем тягу ===
        if (currentGear == -1) return;

        if (gearSettings[currentGear].type != MovementType.Horizontal || isRecoveringFromCollision)
            return;

        if (currentSpeed < 0.01f)
            return;

        float direction = Mathf.Sign(gearSettings[currentGear].speedMultiplier);

        float turnRate = currentSteeringAngle * steeringSensitivity * Time.fixedDeltaTime;
        Quaternion turnRot = Quaternion.Euler(0f, turnRate, 0f);
        flightDirection = turnRot * flightDirection;
        flightDirection.Normalize();

        Vector3 velocity = flightDirection * currentSpeed * direction;
        rb.linearVelocity = new Vector3(
            velocity.x,
            rb.linearVelocity.y,
            velocity.z
        );

        Quaternion targetRot = Quaternion.LookRotation(flightDirection, Vector3.up);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, Time.fixedDeltaTime * 6f));
    }

    void ApplyHeightControl()
    {
        float targetY = stableHeight;

        // Если нейтраль, эти условия просто не выполнятся (false), высота не изменится
        if (currentGear == 0) // Вверх
            targetY += verticalAcceleration * Time.fixedDeltaTime;
        else if (currentGear == 1) // Вниз
            targetY -= verticalAcceleration * Time.fixedDeltaTime;

        targetY = Mathf.Clamp(targetY, 1f, 50f);
        stableHeight = targetY;

        float heightError = targetY - transform.position.y;
        float desiredVerticalSpeed = Mathf.Clamp(heightError * 6f, -maxVerticalSpeed, maxVerticalSpeed);

        float currentVertVel = rb.linearVelocity.y;
        float newVertVel = Mathf.Lerp(currentVertVel, desiredVerticalSpeed, Time.fixedDeltaTime * 8f);

        // === ИСПРАВЛЕНИЕ: Проверка на нейтраль перед доступом к gearSettings ===
        if (isGrounded && currentGear != -1)
        {
            if (gearSettings[currentGear].type == MovementType.Horizontal)
            {
                if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, hoverHeight * 2f))
                {
                    float cushion = Mathf.Max(0f, 1f - hit.distance / hoverHeight);
                    newVertVel += liftForce * cushion * Time.fixedDeltaTime;
                }
            }
        }

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
        Vector3 bounce = Vector3.up * 3f + (-rb.linearVelocity.normalized) * 1f;
        rb.AddForceAtPosition(bounce * 2f, impactPoint, ForceMode.Impulse);
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

        if (hasBall && quaffle != null)
        {
            SetHasBall(false, quaffle);
        }
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
