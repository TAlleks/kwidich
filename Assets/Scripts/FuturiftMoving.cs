using UnityEngine.XR;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using LogitechG29.Sample.Input;

public class FuturiftMoving : MonoBehaviour, IPlayerController
{
    #region Inspector Fields & Serialized Fields

    [Header("XR Input")]
    [SerializeField] private InputActionProperty rightThumbstick; // �������� + �������
    [SerializeField] private InputActionProperty leftThumbstick;  // �����/����
    [SerializeField] private InputActionProperty rightTrigger;    // ������ 
    float triggerValue = 0f;
    [Header("Broom Physics")]
    public float baseMaxSpeed = 15f;
    public float acceleration = 8f;
    public float steeringSensitivity = 3f;
    public float hoverHeight = 2f;
    public float verticalAcceleration = 10f;
    public float maxVerticalSpeed = 12f;

    [Header("Steering")]
    [SerializeField] private float steeringSpeed = 6f;

    [Header("Team")]
    public Team team = Team.Player;

    [Header("References")]
    public Transform broomModel;
    public Rigidbody rb;
    private Camera _mainCamera;

    [Header("Ball Interaction")]
    public Quaffle quaffle; // ������� ��� � �����
    public bool hasBall = false; // ����: ���� �� ���
    public float pickupCooldown = 1.0f; // �������� ����� ��������� ��������
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
    private float lastThrowTime; // ����� ���������� ������
    // Steering
    private float currentSteeringAngle;
    private float targetSteeringAngle;

    // Movement & Physics
    private float currentSpeed;
    private bool isGrounded;
    private float stableHeight;
    private Vector3 flightDirection;
    // Collision recovery
    private bool isRecoveringFromCollision;
    private float collisionRecoveryTimer;

    #endregion

    #region Unity Lifecycle Methods
    private void Awake()
    {
        rightThumbstick.action.Enable();
        leftThumbstick.action.Enable();
        rightTrigger.action.Enable();
    }
    private void Start()
    {
        InitializeBroom();
        _mainCamera = Camera.main;
        
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
        HandleInput();
        CheckGround();
        UpdateSteering();
        ThrowBallInput();
        TryStealBall();
    }

    void FixedUpdate()
    {
        ApplyMovement();
        ApplyHeightControl();
    }

    #endregion

    #region Initialization & Setup

    private void InitializeBroom()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            Debug.LogWarning("Rigidbody �������� ����������� � " + name);
        }

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.useGravity = false;

        flightDirection = transform.forward;

        // ������������� ��������� ������
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
            // УЛУЧШЕННАЯ ФОРМУЛА ТОЛЧКА
            Vector3 pushDirection = (targetBot.transform.position - transform.position).normalized;
            pushDirection.y = 0.4f;
            pushDirection.Normalize();
            
            if (targetBot.rb != null && !targetBot.rb.isKinematic)
            {
                targetBot.rb.AddForce(pushDirection * pushForce, ForceMode.VelocityChange);
                targetBot.rb.AddForce(Vector3.up * pushUpwardForce, ForceMode.Impulse);
                Debug.Log($"[FuturiftMoving] ТОЛКНУЛ бота {targetBot.name} с силой {pushForce}");
            }
            
            // Забираем мяч
            targetBot.SetHasBall(false, null);
            SetHasBall(true, q);
            quaffle.holder = gameObject.transform;
            Debug.Log("[FuturiftMoving] Украл мяч у бота");
        }
    }

    void HandleInput()
    {
        Vector2 rightStick = rightThumbstick.action.ReadValue<Vector2>();
        Vector2 leftStick = leftThumbstick.action.ReadValue<Vector2>();
        triggerValue = rightTrigger.action.ReadValue<float>();
        //Debug.Log(triggerValue);
        //Debug.Log(rightThumbstick.action);  
        //Debug.Log(rightThumbstick.action.phase);
        //Debug.Log(rightStick);
        // DEADZONE
        if (rightStick.magnitude < 0.1f) rightStick = Vector2.zero;
        if (leftStick.magnitude < 0.1f) leftStick = Vector2.zero;

        // === ������ ���� (�����������) ===
        float steering = rightStick.x;
        float input = rightStick.y;

        targetSteeringAngle = steering * 40f;

        float targetSpeed = input * baseMaxSpeed;

        currentSpeed = Mathf.Lerp(
            currentSpeed,
            targetSpeed,
            acceleration * Time.deltaTime
        );
    }

    void ThrowBallInput()
    {
        // Упрощенная проверка: полагаемся только на hasBall и quaffle != null
        // Убрана проверка quaffle.isHeld для избежания рассинхронизации состояний
        if (!hasBall || quaffle == null)
        {
            return;
        }

        bool isPressed = triggerValue > 0.8f;

        if (isPressed)
        {
            ThrowBall();
        }
    }

    void ThrowBall()
    {
        if (quaffle == null || !quaffle.isHeld)
        {
            Debug.LogWarning($"[FuturiftMoving] Не могу бросить: quaffle={quaffle}, isHeld={quaffle?.isHeld}", this);
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
        
        Debug.Log("[FuturiftMoving] Мяч брошен успешно", this);
    }

    public void SetHasBall(bool value, Quaffle incomingQuaffle)
    {
        if (value == true)
        {
            if (Time.time < lastThrowTime + pickupCooldown)
            {
                Debug.LogWarning($"[FuturiftMoving] Cooldown активен: {Time.time - lastThrowTime:F2}s < {pickupCooldown}s", this);
                return;
            }
            Debug.Log($"[FuturiftMoving] Взял мяч: {incomingQuaffle?.name}", this);
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
            // �������� ������������ � ��������
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

        // ����: ��� �������� ����� ����������� ������� (��� � ������)
        float turnRate =
            currentSteeringAngle *
            steeringSensitivity *
            speedSign *
            Time.fixedDeltaTime;

        Quaternion turnRot = Quaternion.Euler(0f, turnRate, 0f);

        // ������������ ����������� ����
        flightDirection = turnRot * flightDirection;
        flightDirection.Normalize();

        // �����: �������� ����� ���� ������������� � ��� � ���� �������� �����
        Vector3 velocity = flightDirection * currentSpeed;

        rb.linearVelocity = new Vector3(
            velocity.x,
            rb.linearVelocity.y,
            velocity.z
        );

        // ������ ������� �����, ������� �� ������������� ��� ������ ����
        Quaternion targetRot = Quaternion.LookRotation(flightDirection, Vector3.up);

        rb.MoveRotation(
            Quaternion.Slerp(rb.rotation, targetRot, Time.fixedDeltaTime * 6f)
        );
    }

    void ApplyHeightControl()
    {
        float targetY = stableHeight;

        float verticalInput = leftThumbstick.action.ReadValue<Vector2>().y;

        targetY += verticalInput * verticalAcceleration * Time.fixedDeltaTime;

        targetY = Mathf.Clamp(targetY, 1f, 50f);
        stableHeight = targetY;

        float heightError = targetY - transform.position.y;
        float desiredVerticalSpeed = Mathf.Clamp(heightError * 6f, -maxVerticalSpeed, maxVerticalSpeed);

        float currentVertVel = rb.linearVelocity.y;
        float newVertVel = Mathf.Lerp(currentVertVel, desiredVerticalSpeed, Time.fixedDeltaTime * 8f);

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, newVertVel, rb.linearVelocity.z);
    }
    #endregion
}
