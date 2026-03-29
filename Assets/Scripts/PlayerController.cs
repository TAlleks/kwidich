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
    public float roleChangeInterval = 999999f; // Отключить автосмену ролей
    private float nextRoleChangeTime = 0f;

    [Header("Goal Approach")]
    public float stopDistanceFromGoal = 12f;

    [Header("Defender Settings")]
    public float defenderMaxDistance = 50f;     // Половина поля (100/2) - граница, за которую не может заходить
    public float defenderPredictionRange = 30f; // Дистанция предиктивного перехвата
    private Vector3 homeGoalPosition;           // Позиция своих ворот
    private Vector3 fieldCenter;                // Центр поля (граница для визуализации)

    [Header("Steal Settings")]
    public float stealCooldown = 2f;                    // Базовый cooldown (для Attacker)
    public float stealFromPlayerCooldown = 5f;          // Отдельный cooldown для игрока
    private static float lastStealTime = -999f;
    private float lastStealFromPlayerTime = -999f;      // Отдельный таймер для игрока
    
    [Header("Push Settings")]
    public float pushForce = 25f;              // Сила толчка (увеличено с 15 до 25)
    public float pushUpwardForce = 8f;         // Вертикальная составляющая (увеличено с 5 до 8)
    public bool canPushPlayer = true;          // Может ли толкать игрока (включено по умолчанию)

    [Header("Settings")]
    public Team team = Team.Enemy;
    public float moveSpeed = 15f;
    public float turnSpeed = 5f;
    public float scoringDistance = 20f;
    public float minThrowDistance = 5f;
    public float throwChance = 0.85f;

    [Header("Avoidance")]
    public float avoidanceRadius = 3f;         // Радиус обнаружения других ботов (МАКСИМУМ 3!)
    public float avoidanceForce = 2f;          // Сила избегания
    public float separationWeight = 1.0f;      // Вес разделения
    public LayerMask botLayer;                 // Слой ботов для обнаружения

    [Header("Target Offset")]
    public float targetOffsetRadius = 1f;      // Радиус разброса вокруг цели (уменьшено с 3 до 1)
    private Vector3 targetOffset;              // Персональный offset

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
        
        // Найти свои ворота для Defender
        if (role == BotRole.Defender)
        {
            GoalRing[] goals = FindObjectsByType<GoalRing>(FindObjectsSortMode.None);
            foreach (var goal in goals)
            {
                if (goal.GetScoredTeam() != team) // Наши ворота (которые мы защищаем)
                {
                    homeGoalPosition = goal.transform.position;
                    
                    // Рассчитываем центр поля (граница для Defender)
                    // Находим противоположные ворота
                    foreach (var enemyGoal in goals)
                    {
                        if (enemyGoal.GetScoredTeam() == team) // Ворота противника
                        {
                            // Центр поля = середина между воротами
                            fieldCenter = (homeGoalPosition + enemyGoal.transform.position) / 2f;
                            break;
                        }
                    }
                    
                    Log($"Defender инициализирован. Домашние ворота: {homeGoalPosition}, Центр поля: {fieldCenter}");
                    break;
                }
            }
        }
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
        // ВСЕГДА ищем мяч (свободный или у кого-то)
        // Убрана проверка pickupCooldown - атакуем постоянно!
        
        currentTarget = FindNearestFreeQuaffle();
        
        if (currentTarget != null)
        {
            GenerateNewTargetOffset();
            return;
        }
        
        // Если нет свободного - идем красть
        currentTarget = FindNearestBotWithBall();
        
        if (currentTarget == null)
        {
            currentTarget = FindPlayerWithBall();
        }
    }

    void MakeDefenderDecision()
    {
        // 1. Проверяем, не слишком ли далеко мы от ворот
        float distanceFromHome = Vector3.Distance(transform.position, homeGoalPosition);
        
        // Если слишком далеко - возвращаемся к воротам (не преследуем цели)
        if (distanceFromHome > defenderMaxDistance)
        {
            currentTarget = null; // Вернемся к воротам через MoveToTarget
            Log("Defender слишком далеко от ворот - возвращаюсь");
            return;
        }
        
        // 2. ПРЕДИКТИВНЫЙ ПЕРЕХВАТ: Ищем врагов с мячом, движущихся к нашим воротам
        Transform enemyWithBall = FindNearestBotWithBall();
        
        if (enemyWithBall != null)
        {
            // Проверяем расстояние врага от наших ворот
            float enemyDistanceFromGoal = Vector3.Distance(homeGoalPosition, enemyWithBall.position);
            
            // Если враг в зоне угрозы (в пределах defenderPredictionRange от ворот)
            if (enemyDistanceFromGoal < defenderPredictionRange)
            {
                // ПРЕДИКТ: Рассчитываем точку перехвата
                Vector3 interceptPoint = CalculateInterceptPoint(enemyWithBall);
                
                // Проверяем, не выходит ли точка перехвата за границу
                float interceptDistance = Vector3.Distance(homeGoalPosition, interceptPoint);
                
                if (interceptDistance < defenderMaxDistance)
                {
                    currentTarget = enemyWithBall;
                    Log($"Defender перехватывает врага на расстоянии {enemyDistanceFromGoal:F1}м от ворот");
                    return;
                }
            }
        }
        
        // 3. Ищем игрока с мячом (если он угроза)
        Transform playerWithBall = FindPlayerWithBall();
        if (playerWithBall != null)
        {
            float playerDistanceFromGoal = Vector3.Distance(homeGoalPosition, playerWithBall.position);
            
            if (playerDistanceFromGoal < defenderPredictionRange)
            {
                float interceptDistance = Vector3.Distance(homeGoalPosition, 
                    CalculateInterceptPoint(playerWithBall));
                
                if (interceptDistance < defenderMaxDistance)
                {
                    currentTarget = playerWithBall;
                    Log($"Defender перехватывает игрока на расстоянии {playerDistanceFromGoal:F1}м от ворот");
                    return;
                }
            }
        }
        
        // 4. Если нет угрозы - патрулируем около ворот (остаемся на месте)
        currentTarget = null;
        Log("Defender патрулирует около ворот");
    }
    
    // НОВЫЙ МЕТОД: Расчет точки перехвата
    Vector3 CalculateInterceptPoint(Transform target)
    {
        if (target == null) return transform.position;
        
        // Получаем скорость цели
        Rigidbody targetRb = target.GetComponent<Rigidbody>();
        Vector3 targetVelocity = targetRb != null ? targetRb.linearVelocity : Vector3.zero;
        
        // Если цель не движется - возвращаем её текущую позицию
        if (targetVelocity.magnitude < 0.5f)
        {
            return target.position;
        }
        
        // Простой предикт: позиция цели через 1 секунду
        Vector3 predictedPosition = target.position + targetVelocity * 1f;
        
        return predictedPosition;
    }

    void MakeSupportDecision()
    {
        // Приоритет: красть у противников
        currentTarget = FindNearestBotWithBall();
        
        if (currentTarget != null)
        {
            return;
        }
        
        // Если некого грабить - идем за игроком
        currentTarget = FindPlayerWithBall();
        
        // В крайнем случае - свободный мяч
        if (currentTarget == null)
        {
            currentTarget = FindNearestFreeQuaffle();
            if (currentTarget != null)
            {
                GenerateNewTargetOffset();
            }
        }
    }

    void ChangeRoleRandomly()
    {
        // Случайная смена роли для разнообразия
        int randomRole = Random.Range(0, 3);
        role = (BotRole)randomRole;
        Log($"Сменил роль на {role}");
    }
    
    // Получить cooldown кражи у ботов в зависимости от роли
    float GetStealCooldownForRole()
    {
        switch (role)
        {
            case BotRole.Attacker:
                return 2f;      // Самый агрессивный
            case BotRole.Support:
                return 2.5f;    // Средний
            case BotRole.Defender:
                return 3f;      // Самый осторожный
            default:
                return stealCooldown;
        }
    }

    // Получить cooldown кражи у игрока в зависимости от роли
    float GetStealFromPlayerCooldownForRole()
    {
        switch (role)
        {
            case BotRole.Attacker:
                return 5f;      // Часто атакует игрока
            case BotRole.Support:
                return 4f;      // Средняя частота
            case BotRole.Defender:
                return 6f;      // Редко атакует игрока
            default:
                return stealFromPlayerCooldown;
        }
    }

    #endregion

    #region Movement & Avoidance

    void MoveToTarget()
    {
        if (currentTarget == null)
        {
            // Для Defender - возвращаемся к воротам
            if (role == BotRole.Defender)
            {
                float distanceFromHome = Vector3.Distance(transform.position, homeGoalPosition);
                
                if (distanceFromHome > 5f) // Если далеко от ворот
                {
                    Vector3 dirToHome = (homeGoalPosition - transform.position).normalized;
                    rb.linearVelocity = dirToHome * (moveSpeed * 0.5f); // Медленно возвращаемся
                    Log($"Defender возвращается к воротам (расстояние: {distanceFromHome:F1}м)");
                    return;
                }
            }
            
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, Time.fixedDeltaTime * 3f);
            return;
        }

        // Целевая позиция с учетом offset
        Vector3 targetPosition = CalculateTargetPosition();
        
        // Направление к цели
        Vector3 dirToTarget = (targetPosition - transform.position).normalized;
        
        // Избегание других ботов
        Vector3 avoidance = CalculateAvoidance();
        
        // Комбинированное направление с ограничением силы avoidance
        Vector3 avoidanceLimited = Vector3.ClampMagnitude(avoidance, 0.3f);
        Vector3 finalDir = (dirToTarget + avoidanceLimited).normalized;
        
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
            rb.linearVelocity = finalDir * moveSpeed * speedMultiplier;
        }
        else
        {
            rb.linearVelocity = finalDir * moveSpeed;
        }
    }

    Vector3 CalculateTargetPosition()
    {
        Vector3 basePosition = currentTarget.position;
        
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

        // Кража у другого бота (cooldown зависит от роли)
        if (!hasBall && targetBot != null && targetBot.hasBall)
        {
            float sqrDist = (transform.position - currentTarget.position).sqrMagnitude;
            float roleCooldown = GetStealCooldownForRole(); // НОВОЕ: cooldown по роли
            
            if (sqrDist <= 9f && Time.time >= lastStealTime + roleCooldown)
            {
                StealBallFromBot(targetBot);
                lastStealTime = Time.time;
            }
        }
        
        // Кража у игрока (ОТДЕЛЬНЫЙ cooldown по роли)
        IPlayerController player = currentTarget.GetComponent<IPlayerController>();
        if (!hasBall && player != null && player.HasBall)
        {
            float sqrDist = (transform.position - currentTarget.position).sqrMagnitude;
            float rolePlayerCooldown = GetStealFromPlayerCooldownForRole(); // НОВОЕ
            
            if (sqrDist <= 9f && Time.time >= lastStealFromPlayerTime + rolePlayerCooldown)
            {
                StealBallFromPlayer(player);
                lastStealFromPlayerTime = Time.time;  // Используем отдельный таймер!
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
            // УЛУЧШЕННАЯ ФОРМУЛА ТОЛЧКА
            Vector3 pushDirection = (targetBot.transform.position - transform.position).normalized;
            
            // Добавляем вертикальную составляющую (фиксированная)
            pushDirection.y = 0.4f; // Фиксированное значение вместо деления
            pushDirection.Normalize();
            
            if (targetBot.rb != null && !targetBot.rb.isKinematic)
            {
                // Используем VelocityChange для более предсказуемого результата
                targetBot.rb.AddForce(pushDirection * pushForce, ForceMode.VelocityChange);
                
                // Дополнительно: добавляем импульс вверх
                targetBot.rb.AddForce(Vector3.up * pushUpwardForce, ForceMode.Impulse);
                
                Log($"ТОЛКНУЛ бота {targetBot.name} с силой {pushForce} (VelocityChange + Impulse)");
            }
            else
            {
                Log($"ОШИБКА: rb бота {targetBot.name} null или kinematic!");
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
                pushDirection.y = 0.4f;
                pushDirection.Normalize();
                
                Rigidbody playerRb = player.Transform.GetComponent<Rigidbody>();
                if (playerRb != null && !playerRb.isKinematic)
                {
                    playerRb.AddForce(pushDirection * pushForce, ForceMode.VelocityChange);
                    playerRb.AddForce(Vector3.up * pushUpwardForce, ForceMode.Impulse);
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
        
        // Индикатор роли
        Gizmos.color = role == BotRole.Attacker ? Color.red : 
                       role == BotRole.Defender ? Color.blue : Color.yellow;
        Gizmos.DrawWireCube(transform.position + Vector3.up * 3f, Vector3.one * 0.5f);
        
        // НОВОЕ: Визуализация для Defender
        if (role == BotRole.Defender)
        {
            // 1. Домашние ворота (зеленая сфера)
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(homeGoalPosition, 3f);
            
            // 2. Центр поля (ГРАНИЦА) - красная линия
            Gizmos.color = Color.red;
            // Вертикальная линия через центр поля (граница, за которую не может заходить Defender)
            Gizmos.DrawLine(fieldCenter + Vector3.up * 20f, fieldCenter - Vector3.up * 5f);
            Gizmos.DrawLine(fieldCenter + Vector3.right * 30f, fieldCenter - Vector3.right * 30f);
            Gizmos.DrawLine(fieldCenter + Vector3.forward * 30f, fieldCenter - Vector3.forward * 30f);
            
            // 3. Максимальная дистанция от ворот (синяя сфера)
            Gizmos.color = new Color(0, 0.5f, 1f, 0.3f); // Полупрозрачный синий
            Gizmos.DrawWireSphere(homeGoalPosition, defenderMaxDistance);
            
            // 4. Зона предиктивного перехвата (желтая сфера)
            Gizmos.color = new Color(1f, 1f, 0f, 0.2f); // Полупрозрачный желтый
            Gizmos.DrawWireSphere(homeGoalPosition, defenderPredictionRange);
            
            // 5. Линия от Defender к домашним воротам
            Gizmos.color = Color.white;
            Gizmos.DrawLine(transform.position, homeGoalPosition);
        }
    }

    #endregion
}
