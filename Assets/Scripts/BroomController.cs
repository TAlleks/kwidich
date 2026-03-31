using LogitechG29.Sample.Input;
using UnityEngine;

public class BroomController : MonoBehaviour, IPlayerController
{
    #region Inspector Fields & Serialized Fields

    [Header("Input")]
    [SerializeField] private InputControllerReader inputControllerReader;

    [Header("Broom Physics")]
    public float baseMaxSpeed = 15f;
    public float acceleration = 8f;
    public float steeringSensitivity = 3f;
    //public float liftForce = 20f;
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
    
    [Header("Push Settings (Player)")]
    public float pushForce = 25f;              // Сила толчка бота (увеличено с 15 до 25)
    public float pushUpwardForce = 8f;         // Вертикальная составляющая (увеличено с 5 до 8)

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

    // Respawn
    private Vector3 startPosition;
    private Quaternion startRotation;
    private bool isInputDisabled = false;  // Флаг блокировки управления

    #endregion

    #region Unity Lifecycle Methods

    private void Start()
    {
        InitializeBroom();
        _mainCamera = Camera.main;
        
        // Сохраняем стартовую позицию
        SaveStartPosition();
        
        // Регистрируем игрока в менеджере
        GameObjectManager.Instance.RegisterPlayer(this);
    }

    private void OnDestroy()
    {
        // Удаляем игрока из менеджера при уничтожении
        if (GameObjectManager.Instance != null)
        {
            GameObjectManager.Instance.UnregisterPlayer();
        }
    }

    void Update()
    {
        // Если управление заблокировано - не обрабатываем input
        if (!isInputDisabled)
        {
            HandleInput();
            CheckGround();
            UpdateSteering();
            HandleCollisionRecovery();
            ThrowBallInput();
            TryStealBall();
        }
        
        // НОВОЕ: Проверка синхронизации состояния мяча
        if (hasBall && quaffle != null)
        {
            if (!quaffle.IsHeldBy(transform))
            {
                Debug.Log("[BroomController] РАССИНХРОНИЗАЦИЯ: Мяч не принадлежит мне, исправляю!");
                SetHasBall(false, null);
            }
        }
        else if (!hasBall && quaffle != null)
        {
            quaffle = null;
        }
    }


    void FixedUpdate()
    {
        // Если управление заблокировано - не применяем движение
        if (!isInputDisabled)
        {
            ApplyMovement();
            ApplyHeightControl();
        }
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

        var bots = GameObjectManager.Instance.GetAllBots();

        foreach (var bot in bots)
        {
            if (!bot.hasBall) continue;

            float sqrDist = (transform.position - bot.transform.position).sqrMagnitude;

            if (sqrDist < stealDistance * stealDistance) // Используем sqrMagnitude для оптимизации
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
            // НОВОЕ: Проверяем, что мяч действительно у бота
            if (!q.IsHeldBy(targetBot.transform))
            {
                Debug.Log($"[BroomController] Мяч не принадлежит {targetBot.name}, рассинхронизация исправлена");
                targetBot.SetHasBall(false, null);
                return;
            }
            
            // УЛУЧШЕННАЯ ФОРМУЛА ТОЛЧКА
            Vector3 pushDirection = (targetBot.transform.position - transform.position).normalized;
            pushDirection.y = 0.4f;
            pushDirection.Normalize();
            
            if (targetBot.rb != null && !targetBot.rb.isKinematic)
            {
                targetBot.rb.AddForce(pushDirection * pushForce, ForceMode.VelocityChange);
                targetBot.rb.AddForce(Vector3.up * pushUpwardForce, ForceMode.Impulse);
            }
            
            // НОВОЕ: Используем централизованный метод смены владельца
            bool success = q.TryChangeOwner(transform, forceSteal: true);
            
            if (success)
            {
                Debug.Log("[BroomController] Украл мяч у бота");
            }
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

        // Упрощенная проверка: полагаемся только на hasBall и quaffle != null
        // Убрана проверка quaffle.isHeld для избежания рассинхронизации состояний
        if (inputControllerReader.Clutch >= 0.8f && hasBall && quaffle != null)
        {
            ThrowBall();
        }
    }

    void ThrowBall()
    {
        if (quaffle == null || !quaffle.isHeld)
        {
            Debug.LogWarning($"[BroomController] Не могу бросить: quaffle={quaffle}, isHeld={quaffle?.isHeld}", this);
            return;
        }

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
        
        Debug.Log("[BroomController] Мяч брошен успешно", this);
    }

    public void SetHasBall(bool value, Quaffle incomingQuaffle)
    {
        if (value == true)
        {
            if (Time.time < lastThrowTime + pickupCooldown)
            {
                Debug.LogWarning($"[BroomController] Cooldown активен: {Time.time - lastThrowTime:F2}s < {pickupCooldown}s", this);
                return;
            }
            Debug.Log($"[BroomController] Взял мяч: {incomingQuaffle?.name}", this);
        }
        else
        {
            lastLostBallTime = Time.time;
            Log("Бросил / Потерял мяч");
        }

        hasBall = value;
        quaffle = value ? incomingQuaffle : null;
    }

    private void Log(string message)
    {
        Debug.Log($"[BroomLogic] {message}", this);
    }

    #endregion

    #region IPlayerController Implementation

    public bool HasBall => hasBall;
    public Quaffle CurrentQuaffle => quaffle;
    public Team Team => team;
    public Transform Transform => transform;

    /// <summary>
    /// Сохранить стартовую позицию (вызывается в начале игры)
    /// </summary>
    public void SaveStartPosition()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;
        Debug.Log($"[BroomController] Стартовая позиция сохранена: {startPosition}");
    }

    /// <summary>
    /// Респавн на стартовую позицию (мгновенная телепортация)
    /// </summary>
    public void RespawnToStartPosition()
    {
        // Блокируем управление
        isInputDisabled = true;
        
        // Сбрасываем мяч если есть
        if (hasBall && quaffle != null)
        {
            SetHasBall(false, null);
        }
        
        // Телепортируем на стартовую позицию
        transform.SetPositionAndRotation(startPosition, startRotation);
        
        // Обнуляем физику
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        
        // Сбрасываем скорость и передачу
        currentSpeed = 0f;
        currentGear = 0;
        
        // Сбрасываем направление полета
        flightDirection = transform.forward;
        
        // Разблокируем управление через небольшую задержку
        StartCoroutine(EnableInputAfterDelay(0.1f));
        
        Debug.Log("[BroomController] Респавн на стартовую позицию");
    }
    
    /// <summary>
    /// Разблокировка управления после задержки
    /// </summary>
    private System.Collections.IEnumerator EnableInputAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        isInputDisabled = false;
        Debug.Log("[BroomController] Управление разблокировано");
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
