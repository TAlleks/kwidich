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
    private float defenderMaxDistance = 30f;     // Зона защиты - граница, за которую не может заходить
    private float defenderPredictionRange = 40f; // Зона угрозы - враг близко к воротам (увеличено для раннего перехвата)
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
    
    [Header("Team Play Settings")]
    public float pressureRadius = 5f;          // Радиус проверки давления врагов
    public int minEnemiesForPressure = 1;      // Мин. кол-во врагов для давления
    public float supportDistance = 12f;        // Дистанция следования за союзником
    public float openForPassRadius = 6f;       // Радиус проверки "открытости" (увеличено с 3 до 6)
    public float minPassDistance = 5f;         // Минимальная дистанция для паса
    public float maxPassDistance = 30f;        // Максимальная дистанция для паса
    public float attackerOffsetForward = 20f;  // Attacker впереди союзника (увеличено с 12 до 20)
    public float supportOffsetSide = 15f;      // Support сбоку от союзника (увеличено с 10 до 15)
    public float defenderOffsetBack = 12f;     // Defender сзади союзника (увеличено с 10 до 12)
    public float arrivalThreshold = 3f;        // Порог прибытия к позиции поддержки
    
    // Флаги состояния командной игры
    private bool isUnderPressure = false;      // Под давлением врагов
    public bool isOpenForPass = false;         // Открыт для паса (PUBLIC для других ботов)
    private AIPlayer supportingTeammate = null; // Союзник, которого поддерживаем

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
                
                // НОВОЕ: Проверка поддержки союзника
                if (!hasBall && supportingTeammate != null)
                {
                    // Поддерживаем союзника - специальная логика движения
                    SupportTeammateWithBall();
                    RotateModel();
                }
                else
                {
                    // Обычная логика движения
                    MoveToTarget();
                    CheckActions();
                    RotateModel();
                }
                break;
        }
    }

    #endregion

    #region Decision Making

    void MakeDecision()
    {
        if (hasBall)
        {
            // Логика с мячом
            MakeDecisionWithBall();
        }
        else
        {
            // Логика без мяча - ЗАВИСИТ ОТ РОЛИ!
            switch (role)
            {
                case BotRole.Defender:
                    MakeDefenderDecision();
                    break;
                case BotRole.Attacker:
                    MakeAttackerDecision();
                    break;
                case BotRole.Support:
                    MakeSupportDecision();
                    break;
            }
        }
    }

    /// <summary>
    /// Логика принятия решений когда у бота ЕСТЬ мяч
    /// ПРОСТАЯ И РАБОЧАЯ ВЕРСИЯ
    /// </summary>
    void MakeDecisionWithBall()
    {
        // Проверяем давление врагов
        isUnderPressure = IsUnderPressure();
        
        // ПРОСТАЯ ЛОГИКА: Если под давлением - пытаемся пас, иначе - несем сами
        if (isUnderPressure)
        {
            // Ищем ЛЮБОГО открытого союзника впереди
            AIPlayer bestTeammate = FindOpenTeammateAhead();
            
            if (bestTeammate != null)
            {
                // Нашли открытого союзника - пасуем!
                PassToTeammate(bestTeammate);
                Log($"ПАС! {name} → {bestTeammate.name} (команда: {bestTeammate.team})");
                return;
            }
            else
            {
                Log("Под давлением, но нет открытых союзников - несу сам");
            }
        }
        
        // Несем к воротам
        currentTarget = FindBestGoal();
    }
    
    /// <summary>
    /// ПРОСТОЙ МЕТОД: Находит ЛЮБОГО открытого союзника впереди
    /// </summary>
    AIPlayer FindOpenTeammateAhead()
    {
        Transform enemyGoal = FindBestGoal();
        if (enemyGoal == null) return null;
        
        float myDistToGoal = Vector3.Distance(transform.position, enemyGoal.position);
        var allBots = GameObjectManager.Instance.GetAllBots();
        
        foreach (var bot in allBots)
        {
            // Пропускаем себя
            if (bot == null || bot == this) continue;
            
            // КРИТИЧНО: Только союзники!
            if (bot.team != team)
            {
                Log($"Пропускаю {bot.name} - враг (его команда: {bot.team}, моя: {team})");
                continue;
            }
            
            // Проверка дистанции
            float distance = Vector3.Distance(transform.position, bot.transform.position);
            if (distance < 5f || distance > 30f) continue;
            
            // Проверка: союзник ВПЕРЕДИ (ближе к воротам)
            float botDistToGoal = Vector3.Distance(bot.transform.position, enemyGoal.position);
            if (botDistToGoal >= myDistToGoal)
            {
                Log($"Пропускаю {bot.name} - сзади меня");
                continue;
            }
            
            // Проверка: союзник открыт (мало врагов рядом)
            if (!bot.isOpenForPass)
            {
                Log($"Пропускаю {bot.name} - закрыт врагами");
                continue;
            }
            
            // Нашли подходящего!
            Log($"НАШЕЛ СОЮЗНИКА ДЛЯ ПАСА: {bot.name} (команда: {bot.team}, открыт: {bot.isOpenForPass})");
            return bot;
        }
        
        return null;
    }
    
    /// <summary>
    /// НОВЫЙ МЕТОД: Находит лучшего союзника для паса с учетом открытости
    /// ИСПРАВЛЕННАЯ ВЕРСИЯ: строгая проверка команды и позиции
    /// </summary>
    AIPlayer FindBestTeammateForPassImproved()
    {
        var allBots = GameObjectManager.Instance.GetAllBots();
        AIPlayer bestTeammate = null;
        float bestScore = -Mathf.Infinity;
        
        foreach (var bot in allBots)
        {
            // КРИТИЧНО: Пропускаем себя и ВРАГОВ
            if (bot == null || bot == this) continue;
            if (bot.team != team) continue; // СТРОГАЯ ПРОВЕРКА КОМАНДЫ!
            
            float distance = Vector3.Distance(transform.position, bot.transform.position);
            
            // Пропускаем слишком близких или далеких
            if (distance < minPassDistance || distance > maxPassDistance) continue;
            
            // НОВОЕ: Проверяем, что союзник впереди нас (ближе к воротам)
            Transform enemyGoal = FindBestGoal();
            if (enemyGoal != null)
            {
                float myDistToGoal = Vector3.Distance(transform.position, enemyGoal.position);
                float botDistToGoal = Vector3.Distance(bot.transform.position, enemyGoal.position);
                
                // Пропускаем союзников, которые ДАЛЬШЕ от ворот (пас назад)
                if (botDistToGoal >= myDistToGoal)
                {
                    continue; // Не пасуем назад!
                }
            }
            
            // Оценка союзника
            float score = 0f;
            
            // 1. Огромный приоритет открытым ботам
            if (bot.isOpenForPass)
            {
                score += 100f;
            }
            else
            {
                // Если не открыт - сильный штраф
                score -= 50f;
            }
            
            // 2. Бонус за близость к воротам противника
            if (enemyGoal != null)
            {
                float distToGoal = Vector3.Distance(bot.transform.position, enemyGoal.position);
                score += (100f - distToGoal) * 0.5f;
            }
            
            // 3. Бонус за роль
            if (bot.role == BotRole.Attacker) score += 30f;
            else if (bot.role == BotRole.Support) score += 15f;
            
            // 4. Штраф за дистанцию до союзника
            score -= distance * 0.3f;
            
            // 5. НОВОЕ: Проверяем, нет ли врагов между нами
            if (IsPassBlocked(transform.position, bot.transform.position))
            {
                score -= 100f; // Огромный штраф за заблокированный пас
            }
            
            if (score > bestScore)
            {
                bestScore = score;
                bestTeammate = bot;
            }
        }
        
        // НОВОЕ: Возвращаем только если score положительный
        if (bestScore > 0f)
        {
            return bestTeammate;
        }
        
        return null; // Нет подходящих союзников
    }

    /// <summary>
    /// Логика принятия решений когда у бота НЕТ мяча
    /// ИСПРАВЛЕНО: Правильные приоритеты - враги важнее союзников!
    /// </summary>
    void MakeDecisionWithoutBall()
    {
        // ПРИОРИТЕТ 1: Свободный мяч (если cooldown прошел)
        if (Time.time >= lastThrowTime + pickupCooldown)
        {
            Transform freeQuaffle = FindNearestFreeQuaffle();
            if (freeQuaffle != null)
            {
                currentTarget = freeQuaffle;
                supportingTeammate = null;
                isOpenForPass = false;
                GenerateNewTargetOffset();
                Log("Цель: свободный мяч");
                return;
            }
        }
        
        // ПРИОРИТЕТ 2: Враг с мячом (ВСЕГДА преследуем!)
        Transform enemyWithBall = FindNearestBotWithBall();
        if (enemyWithBall != null)
        {
            currentTarget = enemyWithBall;
            supportingTeammate = null;
            isOpenForPass = false;
            Log($"Цель: враг с мячом {enemyWithBall.name}");
            return;
        }
        
        // ПРИОРИТЕТ 3: Игрок с мячом
        Transform playerWithBall = FindPlayerWithBall();
        if (playerWithBall != null)
        {
            currentTarget = playerWithBall;
            supportingTeammate = null;
            isOpenForPass = false;
            Log("Цель: игрок с мячом");
            return;
        }
        
        // ПРИОРИТЕТ 4: Поддержка союзника (ТОЛЬКО если нет врагов с мячом!)
        AIPlayer teammate = GameObjectManager.Instance.FindTeammateWithBall(team, this);
        if (teammate != null)
        {
            supportingTeammate = teammate;
            currentTarget = null; // Движение через SupportTeammateWithBall
            CheckIfOpenForPass();
            Log($"Поддерживаю союзника {teammate.name}");
            return;
        }
        
        // Нет целей - сбрасываем всё
        supportingTeammate = null;
        isOpenForPass = false;
        currentTarget = null;
        Log("Нет целей");
    }

    void MakeAttackerDecision()
    {
        // ПРИОРИТЕТ 1: Свободный мяч (если cooldown прошел)
        if (Time.time >= lastThrowTime + pickupCooldown)
        {
            Transform freeQuaffle = FindNearestFreeQuaffle();
            if (freeQuaffle != null)
            {
                currentTarget = freeQuaffle;
                supportingTeammate = null;
                isOpenForPass = false;
                GenerateNewTargetOffset();
                Log("Attacker: цель - свободный мяч");
                return;
            }
        }
        
        // ПРИОРИТЕТ 2: Враг с мячом (ВСЕГДА преследуем!)
        Transform enemyWithBall = FindNearestBotWithBall();
        if (enemyWithBall != null)
        {
            currentTarget = enemyWithBall;
            supportingTeammate = null;
            isOpenForPass = false;
            Log($"Attacker: цель - враг с мячом {enemyWithBall.name}");
            return;
        }
        
        // ПРИОРИТЕТ 3: Игрок с мячом
        Transform playerWithBall = FindPlayerWithBall();
        if (playerWithBall != null)
        {
            currentTarget = playerWithBall;
            supportingTeammate = null;
            isOpenForPass = false;
            Log("Attacker: цель - игрок с мячом");
            return;
        }
        
        // ПРИОРИТЕТ 4: Поддержка союзника (ТОЛЬКО если нет врагов с мячом!)
        AIPlayer teammate = GameObjectManager.Instance.FindTeammateWithBall(team, this);
        if (teammate != null)
        {
            supportingTeammate = teammate;
            currentTarget = null;
            CheckIfOpenForPass();
            Log($"Attacker: поддерживаю союзника {teammate.name}");
            return;
        }
        
        // Нет целей
        supportingTeammate = null;
        isOpenForPass = false;
        currentTarget = null;
        Log("Attacker: нет целей");
    }

    void MakeDefenderDecision()
    {
        // ЛОГИКА ЗАЩИТНИКА: АГРЕССИВНЫЙ перехват врагов, летящих к воротам
        float distanceFromHome = Vector3.Distance(transform.position, homeGoalPosition);
        
        // Если слишком далеко - БЫСТРО возвращаемся к воротам
        if (distanceFromHome > defenderMaxDistance)
        {
            currentTarget = null;
            Log("Defender возвращается к воротам (слишком далеко)");
            return;
        }
        
        // ПРИОРИТЕТ 1: АГРЕССИВНЫЙ перехват врагов с мячом, летящих к воротам
        Transform enemyWithBall = FindNearestBotWithBall();
        if (enemyWithBall != null)
        {
            float enemyDistFromGoal = Vector3.Distance(homeGoalPosition, enemyWithBall.position);
            
            // Если враг в зоне угрозы (40м)
            if (enemyDistFromGoal < defenderPredictionRange)
            {
                // Проверяем, летит ли враг к воротам
                Rigidbody enemyRb = enemyWithBall.GetComponent<Rigidbody>();
                if (enemyRb != null)
                {
                    Vector3 enemyVelocity = enemyRb.linearVelocity;
                    Vector3 dirToGoal = (homeGoalPosition - enemyWithBall.position).normalized;
                    float dotToGoal = Vector3.Dot(enemyVelocity.normalized, dirToGoal);
                    
                    // Если враг летит к воротам (dot > 0.3) ИЛИ медленно движется - ПЕРЕХВАТЫВАЕМ!
                    if (dotToGoal > 0.3f || enemyVelocity.magnitude < 2f)
                    {
                        currentTarget = enemyWithBall;
                        supportingTeammate = null;
                        isOpenForPass = false;
                        Log($"⚠️ ПЕРЕХВАТ! Враг {enemyWithBall.name} в {enemyDistFromGoal:F1}м летит к воротам (dot: {dotToGoal:F2})!");
                        return;
                    }
                    else
                    {
                        Log($"Враг {enemyWithBall.name} в зоне, но НЕ летит к воротам (dot: {dotToGoal:F2}) - игнорирую");
                    }
                }
                else
                {
                    // Нет Rigidbody - перехватываем на всякий случай
                    currentTarget = enemyWithBall;
                    supportingTeammate = null;
                    isOpenForPass = false;
                    Log($"⚠️ ПЕРЕХВАТ! Враг {enemyWithBall.name} в {enemyDistFromGoal:F1}м (нет Rigidbody)!");
                    return;
                }
            }
        }
        
        // ПРИОРИТЕТ 2: Проверяем игрока с мячом
        Transform playerWithBall = FindPlayerWithBall();
        if (playerWithBall != null)
        {
            float playerDistFromGoal = Vector3.Distance(homeGoalPosition, playerWithBall.position);
            
            if (playerDistFromGoal < defenderPredictionRange)
            {
                // Проверяем направление игрока
                Rigidbody playerRb = playerWithBall.GetComponent<Rigidbody>();
                if (playerRb != null)
                {
                    Vector3 playerVelocity = playerRb.linearVelocity;
                    Vector3 dirToGoal = (homeGoalPosition - playerWithBall.position).normalized;
                    float dotToGoal = Vector3.Dot(playerVelocity.normalized, dirToGoal);
                    
                    // Если игрок летит к воротам ИЛИ медленно движется - ПЕРЕХВАТЫВАЕМ!
                    if (dotToGoal > 0.3f || playerVelocity.magnitude < 2f)
                    {
                        currentTarget = playerWithBall;
                        supportingTeammate = null;
                        isOpenForPass = false;
                        Log($"⚠️ ПЕРЕХВАТ! Игрок в {playerDistFromGoal:F1}м летит к воротам (dot: {dotToGoal:F2})!");
                        return;
                    }
                    else
                    {
                        Log($"Игрок в зоне, но НЕ летит к воротам (dot: {dotToGoal:F2}) - игнорирую");
                    }
                }
                else
                {
                    // Нет Rigidbody - перехватываем на всякий случай
                    currentTarget = playerWithBall;
                    supportingTeammate = null;
                    isOpenForPass = false;
                    Log($"⚠️ ПЕРЕХВАТ! Игрок в {playerDistFromGoal:F1}м (нет Rigidbody)!");
                    return;
                }
            }
        }
        
        // ПРИОРИТЕТ 3: Патрулирование около ворот (нет угрозы)
        currentTarget = null;
        supportingTeammate = null;
        isOpenForPass = false;
        Log("Defender патрулирует около ворот (нет угрозы)");
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
        // ПРИОРИТЕТ 1: Свободный мяч (если cooldown прошел)
        if (Time.time >= lastThrowTime + pickupCooldown)
        {
            Transform freeQuaffle = FindNearestFreeQuaffle();
            if (freeQuaffle != null)
            {
                currentTarget = freeQuaffle;
                supportingTeammate = null;
                isOpenForPass = false;
                GenerateNewTargetOffset();
                Log("Support: цель - свободный мяч");
                return;
            }
        }
        
        // ПРИОРИТЕТ 2: Враг с мячом
        Transform enemyWithBall = FindNearestBotWithBall();
        if (enemyWithBall != null)
        {
            currentTarget = enemyWithBall;
            supportingTeammate = null;
            isOpenForPass = false;
            Log($"Support: цель - враг с мячом {enemyWithBall.name}");
            return;
        }
        
        // ПРИОРИТЕТ 3: Игрок с мячом
        Transform playerWithBall = FindPlayerWithBall();
        if (playerWithBall != null)
        {
            currentTarget = playerWithBall;
            supportingTeammate = null;
            isOpenForPass = false;
            Log("Support: цель - игрок с мячом");
            return;
        }
        
        // ПРИОРИТЕТ 4: Поддержка союзника
        AIPlayer teammate = GameObjectManager.Instance.FindTeammateWithBall(team, this);
        if (teammate != null)
        {
            supportingTeammate = teammate;
            currentTarget = null;
            CheckIfOpenForPass();
            Log($"Support: поддерживаю союзника {teammate.name}");
            return;
        }
        
        // Нет целей
        supportingTeammate = null;
        isOpenForPass = false;
        currentTarget = null;
        Log("Support: нет целей");
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
                return 2f;      // Самый осторожный
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
                return 4.5f;      // Часто атакует игрока
            case BotRole.Support:
                return 4.5f;      // Средняя частота
            case BotRole.Defender:
                return 4.5f;      // Редко атакует игрока
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
                return 1.05f;    // ИЗМЕНЕНО: с 1.2x на 1.1x (чуть медленнее)
            case BotRole.Support:
                return 1.0f;    // Средняя скорость
            case BotRole.Defender:
                return 1.2f;    // ИЗМЕНЕНО: с 1.05f на 1.2f (быстрый для перехвата)
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
                return 1.2f;    // Очень агрессивный (чаще меняет цель)
            case BotRole.Support:
                return 1.0f;    // Средняя агрессивность
            case BotRole.Defender:
                return 1.5f;    // ИЗМЕНЕНО: с 1.0f на 1.5f (агрессивный перехват)
            default:
                return 1.0f;
        }
    }

    #endregion

    #region Team Play Methods

    /// <summary>
    /// Проверяет, находится ли бот под давлением врагов
    /// </summary>
    bool IsUnderPressure()
    {
        int enemyCount = 0;
        var allBots = GameObjectManager.Instance.GetAllBots();
        
        foreach (var bot in allBots)
        {
            if (bot == null || bot.team == team) continue; // Пропускаем союзников
            
            float dist = Vector3.Distance(transform.position, bot.transform.position);
            if (dist < pressureRadius)
            {
                enemyCount++;
            }
        }
        
        return enemyCount >= minEnemiesForPressure;
    }

    /// <summary>
    /// Проверяет, открыт ли бот для получения паса
    /// ПРОСТАЯ ВЕРСИЯ: просто проверяем врагов рядом
    /// </summary>
    void CheckIfOpenForPass()
    {
        if (supportingTeammate == null)
        {
            isOpenForPass = false;
            return;
        }
        
        // Просто считаем врагов в радиусе 6 метров
        int nearbyEnemies = 0;
        var allBots = GameObjectManager.Instance.GetAllBots();
        
        foreach (var bot in allBots)
        {
            if (bot == null || bot.team == team) continue; // Пропускаем союзников
            
            float dist = Vector3.Distance(transform.position, bot.transform.position);
            if (dist < 6f) // Простая проверка
            {
                nearbyEnemies++;
            }
        }
        
        // Открыт если врагов не больше 1
        isOpenForPass = (nearbyEnemies <= 1);
    }

    /// <summary>
    /// Рассчитывает позицию для поддержки союзника с мячом
    /// ИСПРАВЛЕНО: Убраны случайные отклонения - стабильная позиция
    /// </summary>
    Vector3 CalculateSupportPosition(AIPlayer teammate)
    {
        if (teammate == null) return transform.position;
        
        Vector3 teammatePos = teammate.transform.position;
        Transform goalTransform = FindBestGoal();
        
        if (goalTransform == null) return teammatePos;
        
        Vector3 toGoal = (goalTransform.position - teammatePos).normalized;
        
        switch (role)
        {
            case BotRole.Attacker:
                // Впереди союзника (ближе к воротам)
                return teammatePos + toGoal * attackerOffsetForward;
                
            case BotRole.Support:
                // Сбоку от союзника
                Vector3 sideOffset = Vector3.Cross(toGoal, Vector3.up).normalized * supportOffsetSide;
                return teammatePos + sideOffset;
                
            case BotRole.Defender:
                // Сзади союзника (страховка)
                return teammatePos - toGoal * defenderOffsetBack;
                
            default:
                return teammatePos;
        }
    }

    /// <summary>
    /// Логика поддержки союзника с мячом (следование и позиционирование)
    /// УЛУЧШЕННАЯ ВЕРСИЯ: боты отходят дальше, замедление только у цели
    /// </summary>
    void SupportTeammateWithBall()
    {
        if (supportingTeammate == null) return;
        
        Vector3 targetPosition = CalculateSupportPosition(supportingTeammate);
        
        // Направление к позиции поддержки
        Vector3 direction = (targetPosition - transform.position).normalized;
        float distance = Vector3.Distance(transform.position, targetPosition);
        
        // НОВОЕ: Замедляемся только очень близко к цели (в пределах arrivalThreshold)
        float speedMultiplier = 1f;
        if (distance < arrivalThreshold)
        {
            // Плавное замедление только в последних 3 метрах
            speedMultiplier = Mathf.Clamp01(distance / arrivalThreshold);
        }
        
        // Применяем движение с учетом роли
        rb.linearVelocity = direction * moveSpeed * roleSpeedMultiplier * speedMultiplier;
        
        // Избегание других ботов
        Vector3 avoidance = CalculateAvoidance();
        if (avoidance.magnitude > 0.1f)
        {
            Vector3 avoidanceLimited = Vector3.ClampMagnitude(avoidance, 0.3f);
            rb.linearVelocity += avoidanceLimited * moveSpeed * roleSpeedMultiplier;
        }
        
        Log($"Поддерживаю {supportingTeammate.name}, дистанция: {distance:F1}м, открыт: {isOpenForPass}");
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
                    rb.linearVelocity = dirToHome * (moveSpeed * roleSpeedMultiplier * 1.0f); // ИЗМЕНЕНО: с 0.5f на 1.0f (быстрый возврат)
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
        // ИСПРАВЛЕНО: Проверяем, что это ВРАГ, а не союзник!
        if (!hasBall && targetBot != null && targetBot.hasBall && targetBot.team != team)
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
        // ПРОВЕРКА: Разрешена ли кража мяча у игрока?
        if (!BallStealToggle.canStealFromPlayer)
        {
            Log("Кража мяча у игрока отключена (BallStealToggle)");
            return;
        }
        
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
        
        // КРИТИЧЕСКАЯ ПРОВЕРКА: Это союзник, а не враг?
        if (teammate.team != team)
        {
            Log($"ОШИБКА! Попытка паса врагу {teammate.name} (его команда: {teammate.team}, моя: {team})");
            // Не пасуем врагу - несем мяч к воротам
            currentTarget = FindBestGoal();
            return;
        }
        
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
        
        Log($"Передал мяч союзнику {teammate.name} (команда: {teammate.team})");
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
        
        // ========== НОВОЕ: ВИЗУАЛИЗАЦИЯ КОМАНДНОЙ ИГРЫ ==========
        
        // 1. Радиус давления (красная сфера если под давлением)
        if (hasBall && isUnderPressure)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, pressureRadius);
            
            // Текст "UNDER PRESSURE"
            Gizmos.color = Color.red;
            Gizmos.DrawCube(transform.position + Vector3.up * 5f, Vector3.one * 0.5f);
        }
        
        // 2. Линия к союзнику, которого поддерживаем (зеленая)
        if (!hasBall && supportingTeammate != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, supportingTeammate.transform.position);
            
            // Целевая позиция поддержки (зеленая сфера)
            Vector3 supportPos = CalculateSupportPosition(supportingTeammate);
            Gizmos.color = new Color(0f, 1f, 0f, 0.5f);
            Gizmos.DrawWireSphere(supportPos, 1.5f);
            
            // Линия к целевой позиции (пунктирная)
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Gizmos.DrawLine(transform.position, supportPos);
        }
        
        // 3. Индикатор "открыт для паса" (зеленый куб над головой)
        if (!hasBall && isOpenForPass)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawCube(transform.position + Vector3.up * 4f, Vector3.one * 0.8f);
            
            // Радиус "открытости"
            Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, openForPassRadius);
        }
        
        // 4. Линия паса (если есть цель для паса) - для ВСЕХ ролей
        if (hasBall && currentPassTarget != null)
        {
            bool blocked = IsPassBlocked(transform.position, currentPassTarget.transform.position);
            Gizmos.color = blocked ? Color.red : Color.green;
            Gizmos.DrawLine(transform.position, currentPassTarget.transform.position);
            
            // Целевая позиция паса
            Vector3 passTarget = CalculatePassTarget(currentPassTarget);
            Gizmos.color = blocked ? new Color(1f, 0f, 0f, 0.5f) : new Color(0f, 1f, 0f, 0.5f);
            Gizmos.DrawWireSphere(passTarget, 1f);
        }
    }

    #endregion
}
