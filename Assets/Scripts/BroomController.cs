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
    [Header("Steal Settings")]
    public float stealDistance = 3f;
    public float stealCooldown = 5f;

    private float lastStealTime = -999f;
    private float lastLostBallTime = -999f;

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
        UpdateSteering();
        HandleCollisionRecovery();
        ThrowBallInput();
        TryStealBall();
    }


    void FixedUpdate()
    {
        ApplyMovement();
        ApplyHeightControl();
    }

    //void OnCollisionEnter(Collision col)
    //{
    //    HandleCollision(col);
    //}

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
    void TryStealBall()
    {
        if (hasBall) return;
        if (Time.time < lastStealTime + stealCooldown) return;

        AIPlayer[] bots = FindObjectsByType<AIPlayer>(FindObjectsSortMode.None);

        foreach (var bot in bots)
        {
            if (!bot.hasBall) continue;

            float dist = Vector3.Distance(transform.position, bot.transform.position);

            if (dist < stealDistance)
            {
                StealBall(bot);
                lastStealTime = Time.time;
                return;
            }
        }
    }

    void StealBall(AIPlayer targetBot)
    {
        if (targetBot == null || !targetBot.hasBall) return;

        Quaffle q = targetBot.GetCurrentQuaffle();
        if (q != null)
        {
            targetBot.SetHasBall(false, null);
            SetHasBall(true, q);
            quaffle.holder = gameObject.transform;
            Debug.Log("[Broom] 💥 Украл мяч у AI");
        }
    }

    void HandleInput()
    {
        if (inputControllerReader == null)
            return;

        float steering = inputControllerReader.Steering;
        float throttle = inputControllerReader.Throttle;
        float brake = inputControllerReader.Brake;

        targetSteeringAngle = steering * 40f;

        float input = throttle - brake;

        float targetSpeed = input * baseMaxSpeed;
        currentSpeed = Mathf.Lerp(
            currentSpeed,
            targetSpeed,
            acceleration * Time.deltaTime
        );
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
                //Log("❌ Не могу взять мяч — кулдаун");
                return;
            }
            //Log("✅ Взял мяч");
        }
        else
        {
            lastLostBallTime = Time.time;
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
        currentSteeringAngle = Mathf.Lerp(
            currentSteeringAngle,
            targetSteeringAngle,
            steeringSpeed * Time.deltaTime
        );

        if (isRecoveringFromCollision)
            return;

        ApplyBroomTilt();
    }

    private void ApplyBroomTilt()
    {
        if (broomModel == null)
            return;

        Quaternion targetTilt;

        if (Mathf.Abs(currentSpeed) < 0.1f)
        {
            // 🟢 Медленно возвращаемся в нейтраль
            targetTilt = Quaternion.identity;
        }
        else
        {
            float speedFactor = Mathf.Clamp01(Mathf.Abs(currentSpeed) / baseMaxSpeed);

            float tiltZ = -currentSteeringAngle * 0.8f;
            float tiltX = Mathf.Sign(currentSpeed) * speedFactor * 8f;

            targetTilt = Quaternion.Euler(tiltX, 0f, tiltZ);
        }

        broomModel.localRotation = Quaternion.Slerp(
            broomModel.localRotation,
            targetTilt,
            Time.deltaTime * 8f
        );
    }

    void ApplyMovement()
    {
        if (isRecoveringFromCollision)
            return;

        if (Mathf.Abs(currentSpeed) < 0.1f)
            return;

        float speedSign = Mathf.Sign(currentSpeed);

        // Руль: при движении назад инвертируем поворот (как у машины)
        float turnRate =
            currentSteeringAngle *
            steeringSensitivity *
            speedSign *
            Time.fixedDeltaTime;

        Quaternion turnRot = Quaternion.Euler(0f, turnRate, 0f);

        // Поворачиваем направление носа
        flightDirection = turnRot * flightDirection;
        flightDirection.Normalize();

        // ВАЖНО: скорость может быть отрицательной — это и есть движение назад
        Vector3 velocity = flightDirection * currentSpeed;

        rb.linearVelocity = new Vector3(
            velocity.x,
            rb.linearVelocity.y,
            velocity.z
        );

        // ❗ ВСЕГДА смотрим вперёд, НИКОГДА не разворачиваем при заднем ходе
        Quaternion targetRot = Quaternion.LookRotation(flightDirection, Vector3.up);

        rb.MoveRotation(
            Quaternion.Slerp(rb.rotation, targetRot, Time.fixedDeltaTime * 6f)
        );
    }



    void ApplyHeightControl()
    {
        float targetY = stableHeight;

        if (inputControllerReader != null)
        {
            if (inputControllerReader.Shifter3) // вверх
                targetY += verticalAcceleration * Time.fixedDeltaTime;
            else if (inputControllerReader.Shifter4) // вниз
                targetY -= verticalAcceleration * Time.fixedDeltaTime;
        }

        targetY = Mathf.Clamp(targetY, 1f, 50f);
        stableHeight = targetY;

        float heightError = targetY - transform.position.y;
        float desiredVerticalSpeed = Mathf.Clamp(heightError * 6f, -maxVerticalSpeed, maxVerticalSpeed);

        float currentVertVel = rb.linearVelocity.y;
        float newVertVel = Mathf.Lerp(currentVertVel, desiredVerticalSpeed, Time.fixedDeltaTime * 8f);

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

    //public void BoostSpeed(float multiplier, float duration)
    //{
    //    StartCoroutine(SpeedBoostRoutine(multiplier, duration));
    //}

    //private System.Collections.IEnumerator SpeedBoostRoutine(float multiplier, float duration)
    //{
    //    float originalSpeed = baseMaxSpeed;
    //    baseMaxSpeed *= multiplier;
    //    Debug.Log($"[Broom] Буст скорости ×{multiplier} на {duration} сек", this);

    //    yield return new WaitForSeconds(duration);

    //    baseMaxSpeed = originalSpeed;
    //    Debug.Log("[Broom] Буст окончен", this);
    //}

    public void SetInputController(InputControllerReader controller)
    {
        inputControllerReader = controller;
    }

    public float GetCurrentSpeed() => currentSpeed;
    public int GetCurrentGear() => currentGear;

    #endregion
}
