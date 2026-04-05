using UnityEngine;

/// <summary>
/// Управление возможностью ботов красть мяч у игрока
/// Клавиша (-) - отключить кражу
/// Клавиша (=) - включить кражу
/// </summary>
public class BallStealToggle : MonoBehaviour
{
    // Статическое поле - доступно из любого места
    public static bool canStealFromPlayer = true;
    
    [Header("Debug")]
    public bool showDebugMessages = true;
    
    void Update()
    {
        // Клавиша (-) - отключить кражу мяча у игрока
        if (Input.GetKeyDown(KeyCode.Minus))
        {
            canStealFromPlayer = false;
            if (showDebugMessages)
            {
                Debug.Log("[BallStealToggle] ❌ Кража мяча у игрока ОТКЛЮЧЕНА");
            }
        }
        
        // Клавиша (=) - включить кражу мяча у игрока
        if (Input.GetKeyDown(KeyCode.Equals))
        {
            canStealFromPlayer = true;
            if (showDebugMessages)
            {
                Debug.Log("[BallStealToggle] ✅ Кража мяча у игрока ВКЛЮЧЕНА");
            }
        }
    }
    
    void OnGUI()
    {
        // Показываем ТОЛЬКО зеленый индикатор когда кража ОТКЛЮЧЕНА
        if (!canStealFromPlayer)
        {
            // Правый нижний угол
            float size = 20f;
            float margin = 20f;
            Rect indicatorRect = new Rect(Screen.width - size - margin, Screen.height - size - margin, size, size);
            
            // Зеленый круг
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.green);
            texture.Apply();
            
            GUI.DrawTexture(indicatorRect, texture);
        }
    }
}
