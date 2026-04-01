using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

public class GameScoreManager : MonoBehaviour
{
    public static GameScoreManager Instance { get; private set; }

    [Header("Score Settings")]
    public int maxScore = 6; // ��� ����� ����� ��������� ����

    [Header("UI References")]
    public TMP_Text playerScoreText;

    [Header("Scene Settings")]
    public string winSceneName = "WinScene";
    public string loseSceneName = "LoseScene"; // ����� ������������ ���� �����

    [Header("Goal Celebration Settings")]
    public float celebrationDuration = 2.5f;   // Длительность празднования
    public Vector3 ballRespawnPosition = Vector3.zero; // Позиция респавна мяча (центр поля)
    
    [Header("Respawn Audio")]
    public AudioClip respawnSound;             // Звук респавна
    public AudioSource audioSource;            // Аудио источник
    
    [Header("Visual Effects")]
    public VignetteController vignetteController; // Контроллер Vignette эффекта

    internal int playerScore = 0;
    private int enemyScore = 0;
    private bool isHandlingGoal = false;       // Флаг обработки гола

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

         //��������� UI ��� ������
        UpdateScoreUI();
    }

    // ���������� �� GoalRing ��� ������� �������
    public void AddGoal(Team team, AIPlayer scorer = null)
    {
        // Предотвращаем множественные вызовы
        if (isHandlingGoal) return;

        if (team != Team.Player)
        {
            playerScore += 1;
            //Debug.Log($"[����] ����� �����! ����: {playerScore} - {enemyScore}");
        }
        else
        {
            enemyScore += 1;
            //Debug.Log($"[����] ��������� �����! ����: {playerScore} - {enemyScore}");
        }

        UpdateScoreUI();
        
        // Запускаем корутину обработки гола
        StartCoroutine(HandleGoalSequence(team, scorer));
    }

    /// <summary>
    /// Корутина обработки последовательности после гола
    /// </summary>
    private IEnumerator HandleGoalSequence(Team scoringTeam, AIPlayer scorer)
    {
        isHandlingGoal = true;

        // 1. Начинаем празднование для забившего бота
        if (scorer != null)
        {
            scorer.StartCelebration();
            Debug.Log($"[GameScoreManager] {scorer.name} празднует гол!");
        }

        // 2. Останавливаем всех остальных ботов (замедляем их)
        AIPlayer[] allBots = FindObjectsByType<AIPlayer>(FindObjectsSortMode.None);
        foreach (var bot in allBots)
        {
            if (bot != scorer && bot.currentState == AIPlayer.BotState.Normal)
            {
                // Замедляем ботов (они продолжат двигаться, но медленнее)
                bot.rb.linearVelocity *= 0.3f;
            }
        }

        // 3. Ждем окончания празднования
        yield return new WaitForSeconds(celebrationDuration);

        // 4. Проверяем окончание игры
        if (CheckGameOverCondition())
        {
            isHandlingGoal = false;
            yield break; // Игра окончена, выходим
        }

        // 5. Респавним мяч в центре поля
        Quaffle[] quaffles = FindObjectsByType<Quaffle>(FindObjectsSortMode.None);
        foreach (var quaffle in quaffles)
        {
            quaffle.RespawnAt(ballRespawnPosition);
            Debug.Log($"[GameScoreManager] Мяч респавнен в позиции {ballRespawnPosition}");
        }

        // 6. НОВОЕ: Запускаем замедление ВСЕХ (игрок + боты)
        IPlayerController player = GameObjectManager.Instance.GetPlayer();
        
        // Запускаем замедление игрока
        if (player != null)
        {
            StartCoroutine(player.SlowdownSequence());
        }
        
        // Запускаем замедление всех ботов
        foreach (var bot in allBots)
        {
            StartCoroutine(bot.SlowdownSequence());
        }
        
        // 7. Ждем окончания замедления (0.5 секунды)
        yield return new WaitForSeconds(0.5f);

        // 8. НОВОЕ: Vignette эффект + одновременная телепортация ВСЕХ
        if (vignetteController != null)
        {
            yield return vignetteController.PlayTeleportEffect(() =>
            {
                // Телепортируем игрока
                if (player != null)
                {
                    player.TeleportToStart();
                    Debug.Log("[GameScoreManager] Игрок телепортирован");
                }
                
                // Телепортируем всех ботов
                foreach (var bot in allBots)
                {
                    bot.TeleportToStartPosition();
                }
                
                Debug.Log("[GameScoreManager] Все телепортированы одновременно!");
            });
        }
        else
        {
            // Если VignetteController не настроен - телепортируем без эффекта
            Debug.LogWarning("[GameScoreManager] VignetteController не назначен! Телепортация без эффекта.");
            
            if (player != null)
                player.TeleportToStart();
                
            foreach (var bot in allBots)
                bot.TeleportToStartPosition();
        }
        
        // 9. Воспроизводим звук респавна (если есть)
        if (respawnSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(respawnSound);
        }
        
        // 10. Разблокируем управление игрока
        if (player != null)
        {
            player.EnableInput();
        }

        //Debug.Log("[GameScoreManager] Все респавнены! Игра продолжается!");
        isHandlingGoal = false;
    }

    private void UpdateScoreUI()
    {
        if (playerScoreText != null)
            playerScoreText.text = (playerScore).ToString() + "                            " + (enemyScore).ToString();
    }

    /// <summary>
    /// Проверяет условие окончания игры (не загружает сцену сразу)
    /// </summary>
    private bool CheckGameOverCondition()
    {
        if (playerScore >= maxScore)
        {
            //Debug.Log("����� �������!");
            SceneManager.LoadScene(winSceneName);
            return true;
        }
        else if (enemyScore >= maxScore)
        {
            //Debug.Log("��������� �������!");
            SceneManager.LoadScene(loseSceneName);
            return true;
        }
        return false;
    }

    // �����������: ����� �����
    public void ResetScore()
    {
        playerScore = 0;
        enemyScore = 0;
        UpdateScoreUI();
    }
}