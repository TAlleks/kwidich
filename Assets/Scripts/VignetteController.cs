using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System;
using System.Collections;

/// <summary>
/// Контроллер для управления Vignette эффектом при телепортации
/// Использует URP (Universal Render Pipeline)
/// </summary>
public class VignetteController : MonoBehaviour
{
    [Header("Post-Processing")]
    [Tooltip("Global Volume с Vignette Override")]
    public Volume postProcessVolume;
    
    [Header("Vignette Settings")]
    [Tooltip("Длительность fade-in/fade-out эффекта")]
    public float fadeDuration = 0.3f;
    
    [Tooltip("Максимальная интенсивность затемнения (0-1)")]
    [Range(0f, 1f)]
    public float maxIntensity = 0.8f;
    
    private Vignette vignette;
    
    void Awake()
    {
        // Получаем Vignette из Volume Profile
        if (postProcessVolume != null && postProcessVolume.profile != null)
        {
            if (postProcessVolume.profile.TryGet(out vignette))
            {
                // Устанавливаем начальную интенсивность в 0
                vignette.intensity.value = 0f;
                Debug.Log("[VignetteController] Vignette инициализирован");
            }
            else
            {
                Debug.LogError("[VignetteController] Vignette Override не найден в Volume Profile! Добавьте Vignette в Volume.");
            }
        }
        else
        {
            Debug.LogError("[VignetteController] Post Process Volume не назначен!");
        }
    }
    
    /// <summary>
    /// Воспроизводит эффект телепортации с Vignette
    /// </summary>
    /// <param name="onTeleport">Callback, вызываемый в момент телепортации (между fade-out и fade-in)</param>
    public IEnumerator PlayTeleportEffect(Action onTeleport)
    {
        if (vignette == null)
        {
            Debug.LogWarning("[VignetteController] Vignette не инициализирован, пропускаем эффект");
            onTeleport?.Invoke();
            yield break;
        }
        
        // Fade-out (затемнение краев экрана)
        yield return FadeVignette(0f, maxIntensity, fadeDuration);
        
        // Телепортация (вызываем callback)
        onTeleport?.Invoke();
        Debug.Log("[VignetteController] Телепортация выполнена");
        
        // Fade-in (осветление краев экрана)
        yield return FadeVignette(maxIntensity, 0f, fadeDuration);
    }
    
    /// <summary>
    /// Плавное изменение интенсивности Vignette
    /// </summary>
    private IEnumerator FadeVignette(float from, float to, float duration)
    {
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            
            // Плавная интерполяция
            vignette.intensity.value = Mathf.Lerp(from, to, t);
            
            yield return null;
        }
        
        // Устанавливаем финальное значение
        vignette.intensity.value = to;
    }
    
    /// <summary>
    /// Сброс Vignette в начальное состояние (интенсивность = 0)
    /// </summary>
    public void ResetVignette()
    {
        if (vignette != null)
        {
            vignette.intensity.value = 0f;
        }
    }
}
