using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AIPlayer : MonoBehaviour
{
    #region Enums
    
    public enum BotRole
    {
        Attacker,    // Агрессивно идет за мячом
        Defender,    // Защищает свои ворота
        Support      // Поддержка, занимает позицию
    }
    
    #endregion
    
    #region Inspector Fields
    
    [Header("Debug")]
    public bool debugLogs = true;
    public bool showGizmos = true;

    [Header("Team Role")]
    public BotRole role = BotRole.Attacker;
    public float roleChangeInterval = 15f;
    private float nextRoleChangeTime = 0f;

    [Header("Goal Approach")]
    public float stopDistanceFromGoal = 12f;

    [Header("Steal Settings")]
    public float stealCooldown = 2f;
    private static float lastStealTime = -999f;
    
    [Header("Push Settings")]
    public float pushForce = 15f;              // Сила толчка (сильный)
    public float pushUpwardForce = 5f;         // Вертикальная составляющая
    public bool canPushPlayer = false;         // Может ли толкать игрока (для безопасности в капсуле)

    [Header("Settings")]
    public Team team = Team.Enemy;
    public float moveSpeed = 15f;
    public float turnSpeed = 5f;
    public float scoringDistance = 20f;
    public float minThrowDistance = 5f;
    public float throwChance = 0.85f;

    [Header("Avoidance")]
    public float avoidanceRadius = 5f;         // Радиус обнаружения других ботов
    public float avoidanceForce = 3f;          // Сила избегания
    public float separationWeight = 1.5f;      // Вес разделения
    public LayerMask botLayer;                 // Слой ботов для обнаружения

    [Header("Target Offset")]
    public float targetOffsetRadius = 3f;      // Радиус разброса вокруг цели
    private Vector3 targetOffset;              // Персональный offset

    [Header("Trajectory Prediction")]
    public bool usePrediction = true;          // Использовать предсказание траектории
    public float predictionTime = 1.5f;        // Время предсказания (секунды)

    [Header("Pickup Settings")]
    public float pickupCooldown = 2f;
    private float lastThrowTime = -999f;

    [Header("References")]
    public Transform model;

    #endregion

    #region Private Fields

    internal Rigidbody rb;
    public bool hasBall = false;
    private Quaffle currentQuaffle;
    private Transform currentTarget;
    private float nextDecisionTime = 0f;
    private float decisionInterval = 0.3f;
    
    #endregion

    #region Unity Lifecycle

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 1f;
        rb.angularDamping = 3f;
        
        // Регистрируем бота в менеджере
        GameObjectManager.Instance.RegisterBot(this);
        
        // Инициализируем случайный offset
        GenerateNewTargetOffset();
        
        // Устанавливаем время смены роли
        nextRoleChangeTime = Time.time + roleChangeInterval;
    }

    void OnDestroy()
    {
        if (GameObjectManager.Instance != null)
        {
            GameObjectManager.Instance.UnregisterBot(this);
        }
    }

    void Update()
    {
        // Оптимизация: принимаем решения не каждый кадр, а с интервалом
        if (Time.time >= nextDecisionTime)
        {
            MakeDecision();
            nextDecisionTime = Time.time + decisionInterval;
        }
        
        // Периодическая смена роли (опционально)
        if (Time.time >= nextRoleChangeTime)
        {
            ChangeRoleRandomly();
            nextRoleChangeTime = Time.time + roleChangeInterval;
        }
    }

    void FixedUpdate()
    {
        MoveToTarget();
        CheckActions();
        RotateModel();
    }

    #endregion

    #region Decision Making

    void MakeDecision()
    {
        if (hasBall)
        {
            currentTarget = FindBestGoal();
            return;
        }

        // Логика в зависимости от роли
        switch (role)
        {
            case BotRole.Attacker:
                MakeAttackerDecision();
                break;
            case BotRole.Defender:
                MakeDefenderDecision();
                break;
            case BotRole.Support:
                MakeSupportDecision();
                break;
        }
    }

    void MakeAttackerDecision()
    {
        // Агрессивно идет за мячом
        if (Time.time >= lastThrowTime + pickupCooldown)
        {
            currentTarget = FindNearestFreeQuaffle();
            
            // Генерируем новый offset при выборе новой цели
            if (currentTarget != null)
            {
                GenerateNewTargetOffset();
            }
        }

        if (currentTarget != null) return;

        // Если нет свободного мяча, идем красть
        currentTarget = FindNearestBotWithBall();

        if (currentTarget == null)
        {
            currentTarget = FindPlayerWithBall();
        }
    }

    void MakeDefenderDecision()
    {
        // Защищает свои ворота, перехватывает противников
        Transform enemyWithBall = FindNearestBotWithBall();
        
        if (enemyWithBall != null)
        {
            currentTarget = enemyWithBall;
            return;
        }

        // Если нет угрозы, ищем свободный мяч
        if (Time.time >= lastThrowTime + pickupCooldown)
        {
            currentTarget = FindNearestFreeQuaffle();
            if (currentTarget != null)
            {
                GenerateNewTargetOffset();
            }
        }
    }

    void MakeSupportDecision()
    {
        // Поддержка, занимает стратегическую позицию
        if (Time.time >= lastThrowTime + pickupCooldown)
        {
            currentTarget = FindNearestFreeQuaffle();
            if (currentTarget != null)
            {
                GenerateNewTargetOffset();
            }
        }

        if (currentTarget != null) return;

        // Если нет мяча, следим за игроком
        currentTarget = FindPlayerWithBall();
    }

    void ChangeRoleRandomly()
    {
        // Случайная смена роли для разнообразия
        int randomRole = Random.Range(0, 3);
        role = (BotRole)randomRole;
        Log($"Сменил роль на {role}");
    }

    #endregion

    #region Movement & Avoidance

    void MoveToTarget()
    {
        if (currentTarget == null)
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, Time.fixedDeltaTime * 3f);
            return;
        }

        // Целевая позиция с учетом предсказания и offset
        Vector3 targetPosition = CalculateTargetPosition();
        
        // Направление к цели
        Vector3 dirToTarget = (targetPosition - transform.position).normalized;
        
        // Избегание других ботов
        Vector3 avoidance = CalculateAvoidance();
        
        // Комбинированное направление
        Vector3 finalDir = (dirToTarget + avoidance).normalized;
        
        // Динамическая скорость
        float dynamicSpeed = CalculateDynamicSpeed();
        
        // Проверка близости к цели (замедление)
        float distanceToTarget = Vector3.Distance(transform.position, targetPosition);
        
        // Особая логика для ворот
        if (hasBall && currentTarget.GetComponent<GoalRing>() != null)
        {
            if (distanceToTarget <= stopDistanceFromGoal)
            {
                rb.linearVelocity = Vector3.Lerp(
                    rb.linearVelocity,
                    Vector3.zero,
                    Time.fixedDeltaTime * 5f
                );
                return;
            }
        }
        
        // Замедление при приближении к цели
        if (distanceToTarget < 3f)
        {
            float speedMultiplier = Mathf.Clamp01(distanceToTarget / 3f);
            rb.linearVelocity = finalDir * dynamicSpeed * speedMultiplier;
        }
        else
        {
            rb.linearVelocity = finalDir * dynamicSpeed;
        }
    }

    Vector3 CalculateTargetPosition()
    {
        Vector3 basePosition = currentTarget.position;
        
        // Если цель - мяч и используется предсказание
        Quaffle quaffle = currentTarget.GetComponent<Quaffle>();
        if (quaffle != null && usePrediction && !quaffle.isHeld)
        {
            basePosition = PredictBallPosition(quaffle, predictionTime);
        }
        
        // Добавляем offset (кроме ворот)
        if (currentTarget.GetComponent<GoalRing>() == null)
        {
            basePosition += targetOffset;
        }
        
        return basePosition;
    }

    Vector3 CalculateAvoidance()
    {
        Vector3 avoidanceVector = Vector3.zero;
        
        // Используем OverlapSphere для поиска ботов рядом
        Collider[] nearbyBots = Physics.OverlapSphere(
            transform.position, 
            avoidanceRadius, 
            botLayer
        );
        
        int count = 0;
        foreach (var botCollider in nearbyBots)
        {
            if (botCollider.transform == transform) continue;
            
            Vector3 diff = transform.position - botCollider.transform.position;
            float distance = diff.magnitude;
            
            if (distance < avoidanceRadius && distance > 0.1f)
            {
                // Чем ближе бот, тем сильнее отталкивание
                avoidanceVector += (diff.normalized / distance) * avoidanceForce;
                count++;
            }
        }
        
        if (count > 0)
        {
            avoidanceVector /= count;
            avoidanceVector *= separationWeight;
        }
        
        return avoidanceVector;
    }

    float CalculateDynamicSpeed()
    {
        float speed = moveSpeed;
        
        // Ускоряемся, если цель далеко
        if (currentTarget != null)
        {
            float distance = Vector3.Distance(transform.position, currentTarget.position);
            if (distance > 20f)
            {
                speed *= 1.3f; // Буст скорости на дальних дистанциях
            }
        }
        
        // Замедляемся в толпе
        Collider[] nearbyBots = Physics.OverlapSphere(transform.position, 5f, botLayer);
        int botCount = nearbyBots.Length - 1; // Минус сам бот
        
        if (botCount > 3)
        {
            speed *= 0.7f; // Замедление в толпе
        }
        
        return speed;
    }

    Vector3 PredictBallPosition(Quaffle ball, float timeAhead)
    {
        if (ball.rb == null) return ball.transform.position;
        
        Vector3 velocity = ball.rb.linearVelocity;
        Vector3 gravity = Physics.gravity;
        
        // Простое физическое предсказание
        Vector3 predictedPos = ball.transform.position 
            + velocity * timeAhead 
            + 0.5f * gravity * timeAhead * timeAhead;
        
        return predictedPos;
    }

    void GenerateNewTargetOffset()
    {
        // Генерируем случайный offset вокруг цели
        targetOffset = Random.insideUnitSphere * targetOffsetRadius;
        targetOffset.y = 0; // Только горизонтальный offset
    }

    void RotateModel()
    {
        if (currentTarget != null)
        {
            Vector3 lookDirection = currentTarget.transform.position - transform.position;

            if (lookDirection != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(lookDirection);
            }
        }
    }

    #endregion

    #region Actions & Stealing

    void CheckActions()
    {
        if (currentTarget == null) return;
        
        AIPlayer targetBot = currentTarget.GetComponent<AIPlayer>();

        // Кража у другого бота
        if (!hasBall && targetBot != null && targetBot.hasBall)
        {
            float sqrDist = (transform.position - currentTarget.position).sqrMagnitude;
            if (sqrDist <= 9f && Time.time >= lastStealTime + stealCooldown)
            {
                StealBallFromBot(targetBot);
                lastStealTime = Time.time;
            }
        }
        
        // Кража у игрока
        IPlayerController player = currentTarget.GetComponent<IPlayerController>();
        if (!hasBall && player != null && player.HasBall)
        {
            float sqrDist = (transform.position - currentTarget.position).sqrMagnitude;
            if (sqrDist <= 9f && Time.time >= lastStealTime + stealCooldown)
            {
                StealBallFromPlayer(player);
                lastStealTime = Time.time;
            }
        }

        // Бросок в ворота
        if (hasBall && currentTarget.GetComponent<GoalRing>() != null)
        {
            float dist = Vector3.Distance(transform.position, currentTarget.position);
            if (dist <= scoringDistance && dist >= minThrowDistance)
            {
                ThrowBall(currentTarget.position);
            }
        }
    }

    void StealBallFromBot(AIPlayer targetBot)
    {
        if (targetBot == null || !targetBot.hasBall) return;

        Quaffle q = targetBot.currentQuaffle;
        if (q != null)
        {
            // ТОЛЧОК БОТА (всегда активен)
            Vector3 pushDirection = (targetBot.transform.position - transform.position).normalized;
            pushDirection.y = pushUpwardForce / pushForce;
            
            if (targetBot.rb != null)
            {
                targetBot.rb.AddForce(pushDirection * pushForce, ForceMode.Impulse);
                Log($"ТОЛКНУЛ бота {targetBot.name} с силой {pushForce}");
            }
            
            // Забираем мяч
            targetBot.SetHasBall(false, null);
            SetHasBall(true, q);
            Log("УКРАЛ мяч у бота");
        }
    }

    void StealBallFromPlayer(IPlayerController player)
    {
        Quaffle q = player.CurrentQuaffle;
        if (q != null)
        {
            // ТОЛЧОК ИГРОКА (только если разрешено)
            if (canPushPlayer)
            {
                Vector3 pushDirection = (player.Transform.position - transform.position).normalized;
                pushDirection.y = pushUpwardForce / pushForce;
                
                Rigidbody playerRb = player.Transform.GetComponent<Rigidbody>();
                if (playerRb != null)
                {
                    playerRb.AddForce(pushDirection * pushForce, ForceMode.Impulse);
                    Log($"ТОЛКНУЛ игрока с силой {pushForce}");
                }
            }
            else
            {
                Log("Украл мяч у игрока БЕЗ толчка (безопасный режим)");
            }
            
            // Забираем мяч
            player.SetHasBall(false, null);
            q.TryPickup(transform);
            Log("УКРАЛ мяч у игрока");
        }
    }

    #endregion

    #region Ball Management

    public Quaffle GetCurrentQuaffle()
    {
        return currentQuaffle;
    }

    public void SetHasBall(bool value, Quaffle quaffle)
    {
        if (value == true)
        {
            if (Time.time < lastThrowTime + pickupCooldown)
            {
                return;
            }
        }
        else
        {
            Log("Потерял мяч");
        }

        hasBall = value;
        currentQuaffle = value ? quaffle : null;
    }

    void ThrowBall(Vector3 goalPos)
    {
        if (!hasBall || currentQuaffle == null) return;

        Vector3 dir = (goalPos - transform.position).normalized;
        dir.y += 0.2f;

        if (Random.value > throwChance)
        {
            dir += Random.insideUnitSphere * 0.5f;
            dir.Normalize();
        }

        currentQuaffle.Throw(dir);
        SetHasBall(false, null);

        lastThrowTime = Time.time;
        Log("Бросил мяч в ворота");
    }

    #endregion

    #region Finding Targets

    Transform FindBestGoal()
    {
        GoalRing best = GameObjectManager.Instance.FindBestGoal(transform.position, team);
        return best != null ? best.transform : null;
    }

    Transform FindNearestFreeQuaffle()
    {
        Quaffle nearest = GameObjectManager.Instance.FindNearestFreeQuaffle(transform.position);
        return nearest != null ? nearest.transform : null;
    }

    Transform FindNearestBotWithBall()
    {
        AIPlayer nearest = GameObjectManager.Instance.FindNearestBotWithBall(transform.position, this);
        return nearest != null ? nearest.transform : null;
    }

    Transform FindPlayerWithBall()
    {
        IPlayerController player = GameObjectManager.Instance.GetPlayer();
        
        if (player != null && player.HasBall)
            return player.Transform;

        return null;
    }

    #endregion

    #region Debug & Gizmos

    void Log(string msg)
    {
        if (debugLogs)
            Debug.Log($"[AIPlayer:{name}] {msg}");
    }

    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        
        // Радиус избегания
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, avoidanceRadius);
        
        // Линия к цели
        if (currentTarget != null)
        {
            Gizmos.color = hasBall ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position, currentTarget.position);
            
            // Offset позиция
            if (currentTarget.GetComponent<GoalRing>() == null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(currentTarget.position + targetOffset, 0.5f);
            }
        }
        
        // Предсказанная позиция мяча
        if (currentTarget != null && usePrediction)
        {
            Quaffle quaffle = currentTarget.GetComponent<Quaffle>();
            if (quaffle != null && !quaffle.isHeld)
            {
                Vector3 predicted = PredictBallPosition(quaffle, predictionTime);
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(predicted, 0.8f);
                Gizmos.DrawLine(currentTarget.position, predicted);
            }
        }
        
        // Индикатор роли
        Gizmos.color = role == BotRole.Attacker ? Color.red : 
                       role == BotRole.Defender ? Color.blue : Color.yellow;
        Gizmos.DrawWireCube(transform.position + Vector3.up * 3f, Vector3.one * 0.5f);
    }

    #endregion
}
