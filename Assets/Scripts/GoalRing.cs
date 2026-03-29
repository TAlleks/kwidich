using UnityEngine;

public class GoalRing : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip goalSound;
    [SerializeField] private AudioSource audioSource;
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
        GameScoreManager.Instance?.AddGoal(scoredTeam);
    }

    public Team GetScoredTeam() => scoredTeam;
}
