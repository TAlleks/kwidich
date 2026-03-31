using System.Collections;
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
    private float defenderMaxDistance = 50f;     // Половина поля (100/2) - граница, за которую не может заходить
    private float defenderPredictionRange = 30f; // Дистанция предиктивного перехвата
    private Vector3 homeGoalPosition;           // Позиция своих ворот
    private Vector3 fieldCenter;                // Центр поля (граница для визуализации)

    [Header("Steal Settings")]
    public float stealCooldown = 2f;                    // Базовый cooldown (для Attacker)
    public float stealFromPlayerCooldown = 5f;          // Отдельный cooldown для игрока
    private static float lastStealTime = -999f;
    private static float lastStealFromPlayerTime = -999f;      // Отдельный таймер для игрока
    
    /// <summary>
    /// Устанавливает глобальный cooldown для всех ботов на кражу у игрока
    /// Вызывается когда игрок крадет мяч у бота
    /// </summary>
    public static void SetGlobalStealFromPlayerCooldown()
    {
        lastStealFromPlayerTime = Time.time;
        Debug.Log("[AIPlayer] Глобальный cooldown на кражу у игрока установлен");
    }
    
    [Header("Push Settings")]
    public float pushForce = 25f;              // Сила толчка (увеличено с 15 до 25)
    public float pushUpwardForce = 8f;         // Вертикальная составляющая (увеличено с 5 до 8)
    public bool canPushPlayer = true;          // Может ли толкать игрока (включено по умолчанию)

    [Header("Settings")]
    public Team team = Team.Player;  // Изменено с Enemy на Player (ваша команда по умолчанию)
    public float moveSpeed = 15f;
    public float turnSpeed = 5f;
    public float scoringDistance = 20f;
    public float minThrowDistance = 5f;
    public float throwChance = 0.85f;

    [Header("Role Characteristics")]
    private float roleSpeedMultiplier = 1f;        // Множитель скорости для роли
    private float roleAggressionLevel = 1f;        // Уровень агрессивности (влияет на частоту смены цели)

    [Header("Avoidance")]
    public float avoidanceRadius = 3f;         // Радиус обнаружения других ботов (МАКСИМУМ 3!)
    public float avoidanceForce = 2f;          // Сила избегания
    public float separationWeight = 1.0f;      // Вес разделения
    public LayerMask botLayer;                 // Слой ботов для обнаружения

    [Header("Target Offset")]
    public float targetOffsetRadius = 1f;      // Радиус разброса вокруг цели (уменьшено с 3 до 1)
    private Vector3 targetOffset;              // Персональный offset

    [Header("Pickup Settings")]
    public float pickupCooldown = 3f;
    private float lastThrowTime = -999f;

    [Header("Pass Settings")]
    public float passRange = 80f;              // Максимальная дистанция передачи
    public float passAccuracy = 0.9f;          // Точность передачи
    public float passCheckRadius = 5f;         // Радиус проверки блокировки
    public float passLeadTime = 0.8f;          // Упреждение для движущихся целей
    public LayerMask passBlockLayer;           // Слой для проверки блокировки (боты + игрок)
    private AIPlayer currentPassTarget;        // Текущая цель для паса

    [Header("Celebration Settings")]
    public float celebrationDuration = 2.5f;   // Длительность празднования
    public float celebrationHeight = 3f;       // Высота подпрыгивания
    public float celebrationSpinSpeed = 360f;  // Скорость вращения (градусы/сек)
    
    [Header("References")]
    public Transform model;

    #endregion

    #region Private Fields

    public enum BotState
    {
        Normal,      // Обычное поведение
        Celebrating, // Празднование гола
        Returning    // Возврат на стартовую позицию
    }

    internal Rigidbody rb;
    public bool hasBall = false;
    private Quaffle currentQuaffle;
    private Transform currentTarget;
    private float nextDecisionTime = 0f;
    private float decisionInterval = 0.3f;
    
    // Состояние бота
    public BotState currentState = BotState.Normal;
    private float celebrationStartTime = 0f;
    private Vector3 startPosition;             // Стартовая позиция для возврата
    private Quaternion startRotation;          // Стартовая ротация
    
    // Система оглушения
    private bool isStunned = false;            // Флаг оглушения
    private float stunEndTime = 0f;            // Время окончания оглушения
    public float stunDuration = 0.7f;          // Длительность оглушения (настраиваемая)
    
    #endregion

    #region Unity Lifecycle

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 1f;
        rb.angularDamping = 3f;
        
        // Сохраняем стартовую позицию и ротацию
        startPosition = transform.position;
        startRotation = transform.rotation;
        
        // Регистрируем бота в менеджере
        GameObjectManager.Instance.RegisterBot(this);
        
        // Инициализируем случайный offset
        GenerateNewTargetOffset();
        
        // Устанавливаем время смены роли
        nextRoleChangeTime = Time.time + roleChangeInterval;
        
        // НОВОЕ: Инициализируем характеристики роли
        UpdateRoleCharacteristics();
        
        // Найти свои ворота для Defender
        if (role == BotRole.Defender)
        {
            GoalRing[] goals = FindObjectsByType<GoalRing>(FindObjectsSortMode.None);
            foreach (var goal in goals)
            {
                if (goal.GetScoredTeam() == team) // Наши ворота (которые мы защищаем)
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
        // Обработка состояний бота
        switch (currentState)
        {
            case BotState.Celebrating:
                UpdateCelebrationLogic();  // Только логика и вращение
                return; // Не выполняем обычную логику во время празднования
                
            case BotState.Returning:
                UpdateReturningLogic();    // Только логика проверки
                return; // Не выполняем обычную логику во время возврата
                
            case BotState.Normal:
                // Обычное поведение
                break;
        }
        
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
        
        // НОВОЕ: Проверка синхронизации состояния мяча
        if (hasBall && currentQuaffle != null)
        {
            // Проверяем, что мяч действительно принадлежит нам
            if (!currentQuaffle.IsHeldBy(transform))
            {
                Log("РАССИНХРОНИЗАЦИЯ: Мяч не принадлежит мне, исправляю!");
                SetHasBall(false, null);
            }
        }
        else if (!hasBall && currentQuaffle != null)
        {
            // Если у нас нет флага, но есть ссылка на мяч - очищаем
            currentQuaffle = null;
        }
    }

    void FixedUpdate()
    {
        // Обработка состояний бота (физика)
        switch (currentState)
        {
            case BotState.Celebrating:
                UpdateCelebrationPhysics();  // Только физика (подпрыгивание)
                return;
                
            case BotState.Returning:
                UpdateReturningPhysics();    // Только физика (движение)
                return;
                
            case BotState.Normal:
                // Проверка оглушения
                if (isStunned)
                {
                    if (Time.time >= stunEndTime)
                    {
                        isStunned = false;
                        Log("Оглушение закончилось");
                    }
                    else
                    {
                        // Применяем плавное замедление во время оглушения (только горизонтальное)
                        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                        horizontalVelocity = Vector3.Lerp(horizontalVelocity, Vector3.zero, Time.fixedDeltaTime * 2f);
                        rb.linearVelocity = new Vector3(horizontalVelocity.x, rb.linearVelocity.y, horizontalVelocity.z);
                        return; // Не выполняем обычную логику движения
                    }
                }
                
                MoveToTarget();
                CheckActions();
                RotateModel();
                break;
        }
    }

    #endregion

    #region Decision Making

    void MakeDecision()
    {
        if (hasBall)
        {
            // НОВОЕ: Для Defender - проверяем возможность передачи
            if (role == BotRole.Defender)
            {
                // Ищем лучшего союзника для паса
                AIPlayer bestTeammate = GameObjectManager.Instance.FindBestTeammateForPass(
                    transform.position, team, this, passRange);
                
                if (bestTeammate != null)
                {
                    // Проверяем, не заблокирован ли пас
                    currentPassTarget = bestTeammate;
                    
                    if (!IsPassBlocked(transform.position, bestTeammate.transform.position))
                    {
                        // Пас открыт - передаем мяч
                        PassToTeammate(bestTeammate);
                        currentPassTarget = null;
                        return;
                    }
                    else
                    {
                        Log("Пас заблокирован, пытаюсь вынести мяч сам");
                        currentPassTarget = null;
                    }
                }
            }
            
            // Если не Defender или нет открытых союзников - идем к воротам
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
        // Проверяем cooldown перед поиском свободного мяча
        if (Time.time >= lastThrowTime + pickupCooldown)
        {
            currentTarget = FindNearestFreeQuaffle();
            
            if (currentTarget != null)
            {
                GenerateNewTargetOffset();
                return;
            }
        }
        
        // Если cooldown активен или нет свободного мяча - идем красть
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
        
        // 4. НОВОЕ: Если нет угрозы и cooldown прошел - подбираем свободный мяч в зоне защиты
        if (Time.time >= lastThrowTime + pickupCooldown)
        {
            Transform nearestQuaffle = FindNearestFreeQuaffle();
            
            if (nearestQuaffle != null)
            {
                // Проверяем, что мяч находится в нашей зоне защиты (100% от defenderMaxDistance)
                float quaffleDistanceFromGoal = Vector3.Distance(homeGoalPosition, nearestQuaffle.position);
                
                if (quaffleDistanceFromGoal < defenderMaxDistance)
                {
                    currentTarget = nearestQuaffle;
                    GenerateNewTargetOffset();
                    Log($"Defender подбирает свободный мяч в зоне защиты (расстояние от ворот: {quaffleDistanceFromGoal:F1}м)");
                    return;
                }
            }
        }
        
        // 5. Если нет угрозы - патрулируем около ворот
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
        
        // В крайнем случае - свободный мяч (с проверкой cooldown)
        if (currentTarget == null && Time.time >= lastThrowTime + pickupCooldown)
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
        
        // НОВОЕ: Обновляем характеристики при смене роли
        UpdateRoleCharacteristics();
        
        Log($"Сменил роль на {role}");
    }
    
    // НОВЫЙ МЕТОД: Обновить характеристики в зависимости от роли
    void UpdateRoleCharacteristics()
    {
        roleSpeedMultiplier = GetRoleSpeedMultiplier();
        roleAggressionLevel = GetRoleAggressionLevel();
        
        // Обновляем интервал принятия решений в зависимости от агрессивности
        decisionInterval = 0.3f / roleAggressionLevel;
        
        Log($"Роль {role}: Скорость x{roleSpeedMultiplier:F1}, Агрессия x{roleAggressionLevel:F1}, Интервал решений {decisionInterval:F2}с");
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
    
    // Получить множитель скорости в зависимости от роли
    float GetRoleSpeedMultiplier()
    {
        switch (role)
        {
            case BotRole.Attacker:
                return 1.1f;    // ИЗМЕНЕНО: с 1.2x на 1.1x (чуть медленнее)
            case BotRole.Support:
                return 1.0f;    // Средняя скорость
            case BotRole.Defender:
                return 0.9f;    // Самый медленный
            default:
                return 1.0f;
        }
    }

    // Получить уровень агрессивности в зависимости от роли
    float GetRoleAggressionLevel()
    {
        switch (role)
        {
            case BotRole.Attacker:
                return 1.4f;    // Очень агрессивный (чаще меняет цель)
            case BotRole.Support:
                return 1.0f;    // Средняя агрессивность
            case BotRole.Defender:
                return 0.8f;    // Низкая агрессивность (более осторожный)
            default:
                return 1.0f;
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
                    rb.linearVelocity = dirToHome * (moveSpeed * roleSpeedMultiplier * 0.5f); // Медленно возвращаемся с учетом роли
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
        
        // Применяем множитель скорости роли
        float effectiveSpeed = moveSpeed * roleSpeedMultiplier;
        
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
            rb.linearVelocity = finalDir * effectiveSpeed * speedMultiplier;
        }
        else
        {
            rb.linearVelocity = finalDir * effectiveSpeed;
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
            // НОВОЕ: Проверяем, что мяч действительно у цели
            if (!q.IsHeldBy(targetBot.transform))
            {
                Log($"Мяч не принадлежит {targetBot.name}, рассинхронизация исправлена");
                targetBot.SetHasBall(false, null);
                return;
            }
            
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
                
                // НОВОЕ: Применяем оглушение к боту
                targetBot.ApplyStun(targetBot.stunDuration);
            }
            
            // НОВОЕ: Используем централизованный метод смены владельца
            bool success = q.TryChangeOwner(transform, forceSteal: true);
            
            if (success)
            {
                Log($"Украл мяч у {targetBot.name}");
            }
        }
    }

    void StealBallFromPlayer(IPlayerController player)
    {
        Quaffle q = player.CurrentQuaffle;
        if (q != null)
        {
            // НОВОЕ: Проверяем, что мяч действительно у игрока
            if (!q.IsHeldBy(player.Transform))
            {
                Log("Мяч не принадлежит игроку, рассинхронизация исправлена");
                player.SetHasBall(false, null);
                return;
            }
            
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
                }
            }
            
            // НОВОЕ: Используем централизованный метод смены владельца
            bool success = q.TryChangeOwner(transform, forceSteal: true);
            
            if (success)
            {
                Log("Украл мяч у игрока");
            }
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
        
        // НОВОЕ: Проверяем, что мяч действительно у нас
        if (!currentQuaffle.IsHeldBy(transform))
        {
            Log("Пытаюсь бросить мяч, который мне не принадлежит, рассинхронизация исправлена");
            SetHasBall(false, null);
            return;
        }

        Vector3 dir = (goalPos - transform.position).normalized;
        dir.y += 0.2f;

        if (Random.value > throwChance)
        {
            dir += Random.insideUnitSphere * 0.5f;
            dir.Normalize();
        }

        currentQuaffle.Throw(dir);
        // SetHasBall вызовется автоматически в Quaffle.Throw()

        lastThrowTime = Time.time;
        Log("Бросил мяч в ворота");
    }

    #endregion

    #region Pass System (Goalkeeper)

    /// <summary>
    /// Применить оглушение к боту (вызывается при получении толчка)
    /// </summary>
    public void ApplyStun(float duration)
    {
        isStunned = true;
        stunEndTime = Time.time + duration;
        Log($"Оглушен на {duration:F1} секунд");
    }

    /// <summary>
    /// Проверяет, заблокирован ли путь передачи врагами
    /// </summary>
    bool IsPassBlocked(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        float distance = direction.magnitude;
        
        // Используем SphereCast для проверки препятствий
        RaycastHit[] hits = Physics.SphereCastAll(from, passCheckRadius, direction.normalized, distance, passBlockLayer);
        
        foreach (var hit in hits)
        {
            // Игнорируем себя и цель
            if (hit.transform == transform || hit.transform == currentPassTarget.transform)
                continue;
                
            // Проверяем, это враг?
            AIPlayer bot = hit.transform.GetComponent<AIPlayer>();
            if (bot != null && bot.team != team)
            {
                Log($"Пас заблокирован ботом {bot.name}");
                return true;
            }
            
            // Проверяем игрока
            IPlayerController player = hit.transform.GetComponent<IPlayerController>();
            if (player != null && player.Team != team)
            {
                Log("Пас заблокирован игроком");
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Рассчитывает целевую позицию для паса с упреждением
    /// </summary>
    Vector3 CalculatePassTarget(AIPlayer teammate)
    {
        if (teammate == null) return Vector3.zero;
        
        // Получаем скорость союзника
        Rigidbody teammateRb = teammate.rb;
        Vector3 teammateVelocity = teammateRb != null ? teammateRb.linearVelocity : Vector3.zero;
        
        // Если союзник движется - добавляем упреждение
        if (teammateVelocity.magnitude > 1f)
        {
            Vector3 predictedPosition = teammate.transform.position + teammateVelocity * passLeadTime;
            Log($"Пас с упреждением к {teammate.name}");
            return predictedPosition;
        }
        
        return teammate.transform.position;
    }

    /// <summary>
    /// Передает мяч союзнику
    /// </summary>
    void PassToTeammate(AIPlayer teammate)
    {
        if (!hasBall || currentQuaffle == null || teammate == null) return;
        
        // Проверяем, что мяч действительно у нас
        if (!currentQuaffle.IsHeldBy(transform))
        {
            Log("Пытаюсь передать мяч, который мне не принадлежит");
            SetHasBall(false, null);
            return;
        }

        Vector3 targetPos = CalculatePassTarget(teammate);
        Vector3 dir = (targetPos - transform.position).normalized;
        dir.y += 0.15f; // Небольшой подъем для паса
        
        // Добавляем небольшую неточность в зависимости от passAccuracy
        if (Random.value > passAccuracy)
        {
            dir += Random.insideUnitSphere * 0.3f;
            dir.Normalize();
        }

        currentQuaffle.Throw(dir);
        lastThrowTime = Time.time;
        
        Log($"Передал мяч союзнику {teammate.name}");
    }

    #endregion

    #region Celebration System

    /// <summary>
    /// Начинает празднование гола
    /// </summary>
    public void StartCelebration()
    {
        if (currentState != BotState.Normal) return;
        
        currentState = BotState.Celebrating;
        celebrationStartTime = Time.time;
        
        Log("Начинаю празднование!");
    }

    /// <summary>
    /// Обновление логики празднования (вызывается в Update)
    /// </summary>
    void UpdateCelebrationLogic()
    {
        float elapsed = Time.time - celebrationStartTime;
        
        if (elapsed >= celebrationDuration)
        {
            // Празднование закончилось
            currentState = BotState.Normal;
            Log("Празднование завершено");
            return;
        }
        
        // Вращение (не физика, поэтому в Update)
        if (model != null)
        {
            model.Rotate(Vector3.up, celebrationSpinSpeed * Time.deltaTime);
        }
        else
        {
            transform.Rotate(Vector3.up, celebrationSpinSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Обновление физики празднования (вызывается в FixedUpdate)
    /// </summary>
    void UpdateCelebrationPhysics()
    {
        float elapsed = Time.time - celebrationStartTime;
        float progress = elapsed / celebrationDuration;
        
        // Подпрыгивание (синусоида) - физика, поэтому в FixedUpdate
        float bounce = Mathf.Sin(progress * Mathf.PI * 4f) * celebrationHeight;
        Vector3 targetVelocity = Vector3.up * bounce;
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, targetVelocity, Time.fixedDeltaTime * 5f);
    }

    /// <summary>
    /// Начинает возврат на стартовую позицию (УСТАРЕЛО - используйте TeleportToStartPosition)
    /// </summary>
    public void StartReturning()
    {
        // Сбрасываем мяч если есть
        if (hasBall && currentQuaffle != null)
        {
            SetHasBall(false, null);
        }
        
        currentState = BotState.Returning;
        Log("Возвращаюсь на стартовую позицию");
    }

    /// <summary>
    /// Обновление логики возврата на позицию (вызывается в Update)
    /// </summary>
    void UpdateReturningLogic()
    {
        float distanceToStart = Vector3.Distance(transform.position, startPosition);
        
        // Если достигли стартовой позиции
        if (distanceToStart < 2f)
        {
            currentState = BotState.Normal;
            rb.linearVelocity = Vector3.zero;
            Log("Вернулся на стартовую позицию");
        }
    }

    /// <summary>
    /// Обновление физики возврата на позицию (вызывается в FixedUpdate)
    /// </summary>
    void UpdateReturningPhysics()
    {
        // Движение к стартовой позиции
        Vector3 dirToStart = (startPosition - transform.position).normalized;
        rb.linearVelocity = dirToStart * (moveSpeed * roleSpeedMultiplier * 0.7f);
    }

    /// <summary>
    /// Плавное замедление бота (БЕЗ телепортации)
    /// Используется для синхронной телепортации всех ботов
    /// </summary>
    public IEnumerator SlowdownSequence()
    {
        // Плавное замедление (0.5 секунды)
        float slowdownDuration = 0.5f;
        float elapsed = 0f;
        Vector3 initialVelocity = rb.linearVelocity;
        Vector3 initialAngularVelocity = rb.angularVelocity;
        
        while (elapsed < slowdownDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / slowdownDuration;
            
            // Плавное замедление (ease-out)
            rb.linearVelocity = Vector3.Lerp(initialVelocity, Vector3.zero, t);
            rb.angularVelocity = Vector3.Lerp(initialAngularVelocity, Vector3.zero, t);
            
            yield return null;
        }
        
        // Полностью останавливаем
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        
        Log("Замедление завершено");
    }

    /// <summary>
    /// Телепортирует бота на стартовую позицию (мгновенно)
    /// </summary>
    public void TeleportToStartPosition()
    {
        // Сбрасываем мяч если есть
        if (hasBall && currentQuaffle != null)
        {
            SetHasBall(false, null);
        }
        
        // Телепортируем
        transform.SetPositionAndRotation(startPosition, startRotation);
        
        // Обнуляем физику
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        
        // Сбрасываем состояние
        currentState = BotState.Normal;
        
        Log("Телепортирован на стартовую позицию");
    }

    /// <summary>
    /// Сброс бота в нормальное состояние (вызывается из GameScoreManager)
    /// </summary>
    public void ResetToNormal()
    {
        currentState = BotState.Normal;
        rb.linearVelocity = Vector3.zero;
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
        
        // НОВОЕ: Визуализация передачи для Defender
        if (role == BotRole.Defender && hasBall && currentPassTarget != null)
        {
            // Линия передачи
            bool blocked = IsPassBlocked(transform.position, currentPassTarget.transform.position);
            Gizmos.color = blocked ? Color.red : Color.green;
            Gizmos.DrawLine(transform.position, currentPassTarget.transform.position);
            
            // Целевая позиция с упреждением
            Vector3 passTarget = CalculatePassTarget(currentPassTarget);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(passTarget, 1f);
            
            // Радиус проверки блокировки
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            Vector3 midPoint = (transform.position + currentPassTarget.transform.position) / 2f;
            Gizmos.DrawWireSphere(midPoint, passCheckRadius);
        }
        
        // Индикатор роли
        Gizmos.color = role == BotRole.Attacker ? Color.red : 
                       role == BotRole.Defender ? Color.blue : Color.yellow;
        Gizmos.DrawWireCube(transform.position + Vector3.up * 3f, Vector3.one * 0.5f);
        
        // Индикатор состояния
        if (currentState == BotState.Celebrating)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 5f, 2f);
        }
        else if (currentState == BotState.Returning)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(transform.position, startPosition);
            Gizmos.DrawWireSphere(startPosition, 1f);
        }
        
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
