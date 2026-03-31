using UnityEngine;
using System.Collections;

public class GoalRing : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip goalSound;
    [SerializeField] private AudioSource audioSource;
    
    [Header("Visual Effects")]
    [SerializeField] private GameObject Fire;
    [SerializeField] private float fireActiveDuration = 2.5f;

    GameScoreManager scoreManager;
    [Header("Goal Settings")]
    [SerializeField] private Team scoredTeam = Team.Enemy;

    private void Awake()
    {
        scoreManager = FindAnyObjectByType<GameScoreManager>();
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        //audioSource.spatialBlend = 1f;
        
        // Отключаем Fire при старте
        if (Fire != null)
            Fire.SetActive(false);
        
        // Регистрируем ворота в менеджере
        GameObjectManager.Instance.RegisterGoal(this);
    }

    private void OnDestroy()
    {
        // Удаляем ворота из менеджера при уничтожении
        if (GameObjectManager.Instance != null)
        {
            GameObjectManager.Instance.UnregisterGoal(this);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // ������� ������ �����������, ����� ����� ��������� ���� ������ � ������� ������.
        if (!other.CompareTag("Quaffle")) return;

        Quaffle quaffle = other.GetComponent<Quaffle>();
        if (quaffle == null) return;

        // ��� ������������� ������ ���� ��� � ��������� ��������� (�� � �����).
        if (quaffle.isHeld) return;

        if (goalSound != null && audioSource != null && scoreManager.playerScore != scoreManager.maxScore)
        {
            audioSource.PlayOneShot(goalSound);
        }

        Debug.Log($"[GoalRing] ���! �������: {scoredTeam}", this);
        
        // Включаем эффект огня
        if (Fire != null)
        {
            StartCoroutine(ActivateFireEffect());
        }
        
        // Находим бота, который забил гол
        AIPlayer scorer = null;
        if (quaffle.GetCurrentHolder() != null)
        {
            scorer = quaffle.GetCurrentHolder().GetComponent<AIPlayer>();
        }
        
        // Передаем информацию о забившем боте
        GameScoreManager.Instance?.AddGoal(scoredTeam, scorer);
    }

    /// <summary>
    /// Включает эффект огня на заданное время
    /// </summary>
    private IEnumerator ActivateFireEffect()
    {
        Fire.SetActive(true);
        Debug.Log($"[GoalRing] Эффект огня включен на {fireActiveDuration} секунд");
        
        yield return new WaitForSeconds(fireActiveDuration);
        
        Fire.SetActive(false);
        Debug.Log("[GoalRing] Эффект огня выключен");
    }

    public Team GetScoredTeam() => scoredTeam;
}
