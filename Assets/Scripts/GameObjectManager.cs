using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton менеджер для кэширования и быстрого доступа к игровым объектам
/// Убирает необходимость использовать FindObjectsByType каждый кадр
/// </summary>
public class GameObjectManager : MonoBehaviour
{
    private static GameObjectManager instance;
    public static GameObjectManager Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("GameObjectManager");
                instance = go.AddComponent<GameObjectManager>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

    #region Cached Lists

    private List<AIPlayer> allBots = new List<AIPlayer>();
    private List<Quaffle> allQuaffles = new List<Quaffle>();
    private List<GoalRing> allGoals = new List<GoalRing>();
    private IPlayerController player;

    #endregion

    #region Registration Methods

    public void RegisterBot(AIPlayer bot)
    {
        if (bot != null && !allBots.Contains(bot))
        {
            allBots.Add(bot);
            Debug.Log($"[GameObjectManager] Зарегистрирован бот: {bot.name}");
        }
    }

    public void UnregisterBot(AIPlayer bot)
    {
        if (bot != null && allBots.Contains(bot))
        {
            allBots.Remove(bot);
            Debug.Log($"[GameObjectManager] Удален бот: {bot.name}");
        }
    }

    public void RegisterQuaffle(Quaffle quaffle)
    {
        if (quaffle != null && !allQuaffles.Contains(quaffle))
        {
            allQuaffles.Add(quaffle);
            Debug.Log($"[GameObjectManager] Зарегистрирован мяч: {quaffle.name}");
        }
    }

    public void UnregisterQuaffle(Quaffle quaffle)
    {
        if (quaffle != null && allQuaffles.Contains(quaffle))
        {
            allQuaffles.Remove(quaffle);
            Debug.Log($"[GameObjectManager] Удален мяч: {quaffle.name}");
        }
    }

    public void RegisterGoal(GoalRing goal)
    {
        if (goal != null && !allGoals.Contains(goal))
        {
            allGoals.Add(goal);
            Debug.Log($"[GameObjectManager] Зарегистрированы ворота: {goal.name}");
        }
    }

    public void UnregisterGoal(GoalRing goal)
    {
        if (goal != null && allGoals.Contains(goal))
        {
            allGoals.Remove(goal);
            Debug.Log($"[GameObjectManager] Удалены ворота: {goal.name}");
        }
    }

    public void RegisterPlayer(IPlayerController playerController)
    {
        player = playerController;
        Debug.Log($"[GameObjectManager] Зарегистрирован игрок: {playerController.Transform.name}");
    }

    public void UnregisterPlayer()
    {
        if (player != null)
        {
            Debug.Log($"[GameObjectManager] Удален игрок: {player.Transform.name}");
            player = null;
        }
    }

    #endregion

    #region Access Methods

    /// <summary>
    /// Получить всех ботов в игре
    /// </summary>
    public List<AIPlayer> GetAllBots()
    {
        // Удаляем null объекты (если были уничтожены)
        allBots.RemoveAll(bot => bot == null);
        return allBots;
    }

    /// <summary>
    /// Получить все мячи в игре
    /// </summary>
    public List<Quaffle> GetAllQuaffles()
    {
        allQuaffles.RemoveAll(q => q == null);
        return allQuaffles;
    }

    /// <summary>
    /// Получить все ворота в игре
    /// </summary>
    public List<GoalRing> GetAllGoals()
    {
        allGoals.RemoveAll(g => g == null);
        return allGoals;
    }

    /// <summary>
    /// Получить игрока
    /// </summary>
    public IPlayerController GetPlayer()
    {
        return player;
    }

    /// <summary>
    /// Найти ближайший свободный мяч
    /// </summary>
    public Quaffle FindNearestFreeQuaffle(Vector3 position)
    {
        Quaffle nearest = null;
        float minDist = Mathf.Infinity;

        foreach (var q in allQuaffles)
        {
            if (q == null || q.isHeld) continue;

            float dist = Vector3.Distance(position, q.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = q;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Найти ближайшего бота с мячом
    /// </summary>
    public AIPlayer FindNearestBotWithBall(Vector3 position, AIPlayer excludeBot = null)
    {
        AIPlayer nearest = null;
        float minDist = Mathf.Infinity;

        foreach (var bot in allBots)
        {
            if (bot == null || bot == excludeBot || !bot.hasBall) continue;

            float dist = Vector3.Distance(position, bot.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = bot;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Найти лучшие ворота для команды
    /// </summary>
    public GoalRing FindBestGoal(Vector3 position, Team team)
    {
        GoalRing best = null;
        float minDist = Mathf.Infinity;

        foreach (var goal in allGoals)
        {
            if (goal == null) continue;
            // ИСПРАВЛЕНО: Ищем ворота, где scoredTeam == team (ворота противника, в которые нужно забивать)
            if (goal.GetScoredTeam() == team) continue;

            float dist = Vector3.Distance(position, goal.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                best = goal;
            }
        }

        return best;
    }

    /// <summary>
    /// Найти лучшего союзника для передачи мяча
    /// </summary>
    /// <param name="fromPosition">Позиция передающего</param>
    /// <param name="team">Команда передающего</param>
    /// <param name="excludeBot">Исключить этого бота из поиска (обычно сам передающий)</param>
    /// <param name="maxPassDistance">Максимальная дистанция передачи</param>
    /// <returns>Лучший союзник для паса или null</returns>
    public AIPlayer FindBestTeammateForPass(Vector3 fromPosition, Team team, AIPlayer excludeBot, float maxPassDistance = 80f)
    {
        AIPlayer bestTeammate = null;
        float bestScore = -Mathf.Infinity;

        // Находим ворота противника для оценки позиций
        GoalRing enemyGoal = FindBestGoal(fromPosition, team);
        if (enemyGoal == null) return null;

        Vector3 enemyGoalPos = enemyGoal.transform.position;

        foreach (var bot in allBots)
        {
            // Пропускаем null, себя, врагов и ботов не своей команды
            if (bot == null || bot == excludeBot || bot.team != team) continue;

            float distanceToBot = Vector3.Distance(fromPosition, bot.transform.position);
            
            // Пропускаем слишком далеких союзников
            if (distanceToBot > maxPassDistance) continue;

            // Оценка позиции союзника
            float distanceToEnemyGoal = Vector3.Distance(bot.transform.position, enemyGoalPos);
            
            // Чем ближе к воротам противника - тем лучше
            // Чем ближе к нам - тем хуже (не хотим пасовать назад)
            float positionScore = (maxPassDistance - distanceToEnemyGoal) / maxPassDistance;
            
            // Бонус для атакующих
            if (bot.role == AIPlayer.BotRole.Attacker)
            {
                positionScore += 0.3f;
            }
            else if (bot.role == AIPlayer.BotRole.Support)
            {
                positionScore += 0.15f;
            }
            
            // Штраф за слишком близкую дистанцию (не хотим пасовать рядом стоящим)
            if (distanceToBot < 10f)
            {
                positionScore -= 0.5f;
            }

            if (positionScore > bestScore)
            {
                bestScore = positionScore;
                bestTeammate = bot;
            }
        }

        return bestTeammate;
    }

    /// <summary>
    /// Получить всех союзников определенной команды
    /// </summary>
    public List<AIPlayer> GetTeammates(Team team, AIPlayer excludeBot = null)
    {
        List<AIPlayer> teammates = new List<AIPlayer>();
        
        foreach (var bot in allBots)
        {
            if (bot == null || bot == excludeBot) continue;
            if (bot.team == team)
            {
                teammates.Add(bot);
            }
        }
        
        return teammates;
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    #endregion
}
