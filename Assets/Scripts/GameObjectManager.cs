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
