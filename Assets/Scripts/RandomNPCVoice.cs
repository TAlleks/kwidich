using UnityEngine;
using System.Collections;

[RequireComponent(typeof(AudioSource))]
public class RandomAudioPlayer : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Список фраз для воспроизведения")]
    public AudioClip[] voiceClips;

    [Tooltip("Минимальное время тишины (сек)")]
    public float minDelay = 10f;

    [Tooltip("Максимальное время тишины (сек)")]
    public float maxDelay = 30f;

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        // Отключаем Loop, чтобы клип не зацикливался сам по себе
        audioSource.loop = false;

        // Проверка на ошибки
        if (voiceClips == null || voiceClips.Length == 0)
        {
            Debug.LogWarning($"[RandomAudioPlayer] Нет аудиоклипов в объекте {name}!");
            enabled = false; // Отключаем скрипт, если нет звуков
        }
    }

    private void Start()
    {
        // Запускаем бесконечный цикл воспроизведения
        StartCoroutine(PlayRandomVoiceRoutine());
    }

    private IEnumerator PlayRandomVoiceRoutine()
    {
        while (true)
        {
            // 1. Ждем случайное время
            float waitTime = Random.Range(minDelay, maxDelay);
            yield return new WaitForSeconds(waitTime);

            // 2. Если аудио еще играет (предыдущая фраза длинная) — ждем, пока она закончится
            while (audioSource.isPlaying)
            {
                yield return null;
            }

            // 3. Выбираем и играем случайный звук
            PlayRandomClip();
        }
    }

    private void PlayRandomClip()
    {
        if (voiceClips.Length == 0) return;

        // Выбираем случайный индекс
        int randomIndex = Random.Range(0, voiceClips.Length);
        AudioClip clipToPlay = voiceClips[randomIndex];

        // Играем (PlayOneShot позволяет играть поверх, если нужно, но мы уже сделали паузу выше)
        audioSource.PlayOneShot(clipToPlay);
    }
}
