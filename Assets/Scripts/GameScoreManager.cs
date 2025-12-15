using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class GameScoreManager : MonoBehaviour
{
    public static GameScoreManager Instance { get; private set; }

    [Header("Score Settings")]
    public int maxScore = 6; // При каком счёте завершать игру

    [Header("UI References")]
    public TMP_Text playerScoreText;

    [Header("Scene Settings")]
    public string winSceneName = "WinScene";
    public string loseSceneName = "LoseScene"; // Можно использовать одну сцену

    private int playerScore = 0;
    private int enemyScore = 0;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Обновляем UI при старте
        UpdateScoreUI();
    }

    // Вызывается из GoalRing или другого скрипта
    public void AddGoal(Team team)
    {
        if (team == Team.Player)
        {
            playerScore++;
            Debug.Log($"[Счёт] Игрок забил! Счёт: {playerScore} - {enemyScore}");
        }
        else
        {
            enemyScore++;
            Debug.Log($"[Счёт] Противник забил! Счёт: {playerScore} - {enemyScore}");
        }

        UpdateScoreUI();
        CheckGameOver();
    }

    private void UpdateScoreUI()
    {
        if (playerScoreText != null)
            playerScoreText.text = "Your " + (playerScore/2f).ToString() + " : " + (enemyScore / 2f).ToString() + " Enemy";
    }

    private void CheckGameOver()
    {
        if (playerScore >= maxScore)
        {
            Debug.Log("Игрок выиграл!");
            SceneManager.LoadScene(winSceneName);
        }
        else if (enemyScore >= maxScore)
        {
            Debug.Log("Противник выиграл!");
            SceneManager.LoadScene(loseSceneName);
        }
    }

    // Опционально: сброс счёта
    public void ResetScore()
    {
        playerScore = 0;
        enemyScore = 0;
        UpdateScoreUI();
    }
}