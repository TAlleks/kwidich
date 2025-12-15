using UnityEngine;

public class GoalRing : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioClip goalSound;
    [SerializeField] private AudioSource audioSource;

    [Header("Goal Settings")]
    [SerializeField] private Team scoredTeam = Team.Enemy;

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Триггер должен срабатывать, когда любой коллайдер мяча входит в триггер кольца.
        if (!other.CompareTag("Quaffle")) return;

        Quaffle quaffle = other.GetComponent<Quaffle>();
        if (quaffle == null) return;

        // Гол засчитывается только если мяч в свободном состоянии (не в руках).
        if (quaffle.isHeld) return;

        if (goalSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(goalSound);
        }

        Debug.Log($"[GoalRing] Гол! Команда: {scoredTeam}", this);
        GameScoreManager.Instance?.AddGoal(scoredTeam);
    }

    public Team GetScoredTeam() => scoredTeam;
}
