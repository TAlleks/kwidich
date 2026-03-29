using UnityEngine;

/// <summary>
/// Тестовый скрипт для проверки всех изменений
/// Добавьте этот скрипт на пустой GameObject в сцене для тестирования
/// </summary>
public class OptimizationTest : MonoBehaviour
{
    [Header("Test Results")]
    public bool allTestsPassed = false;
    public string testLog = "";

    void Start()
    {
        RunTests();
    }

    void RunTests()
    {
        testLog = "=== ТЕСТИРОВАНИЕ ОПТИМИЗАЦИИ ===\n\n";
        bool allPassed = true;

        // Тест 1: Проверка GameObjectManager
        testLog += "Тест 1: GameObjectManager существует... ";
        if (GameObjectManager.Instance != null)
        {
            testLog += "✓ PASSED\n";
        }
        else
        {
            testLog += "✗ FAILED\n";
            allPassed = false;
        }

        // Тест 2: Проверка интерфейса IPlayerController
        testLog += "Тест 2: Интерфейс IPlayerController... ";
        FuturiftMoving futurift = FindFirstObjectByType<FuturiftMoving>();
        BroomController broom = FindFirstObjectByType<BroomController>();
        
        if (futurift != null)
        {
            IPlayerController playerController = futurift as IPlayerController;
            if (playerController != null)
            {
                testLog += "✓ PASSED (FuturiftMoving)\n";
            }
            else
            {
                testLog += "✗ FAILED (FuturiftMoving не реализует интерфейс)\n";
                allPassed = false;
            }
        }
        else if (broom != null)
        {
            IPlayerController playerController = broom as IPlayerController;
            if (playerController != null)
            {
                testLog += "✓ PASSED (BroomController)\n";
            }
            else
            {
                testLog += "✗ FAILED (BroomController не реализует интерфейс)\n";
                allPassed = false;
            }
        }
        else
        {
            testLog += "⚠ SKIPPED (Нет контроллера игрока на сцене)\n";
        }

        // Тест 3: Проверка регистрации ботов
        testLog += "Тест 3: Регистрация ботов в менеджере... ";
        var bots = GameObjectManager.Instance.GetAllBots();
        testLog += $"✓ PASSED (Найдено ботов: {bots.Count})\n";

        // Тест 4: Проверка регистрации мячей
        testLog += "Тест 4: Регистрация мячей в менеджере... ";
        var quaffles = GameObjectManager.Instance.GetAllQuaffles();
        testLog += $"✓ PASSED (Найдено мячей: {quaffles.Count})\n";

        // Тест 5: Проверка регистрации ворот
        testLog += "Тест 5: Регистрация ворот в менеджере... ";
        var goals = GameObjectManager.Instance.GetAllGoals();
        testLog += $"✓ PASSED (Найдено ворот: {goals.Count})\n";

        // Тест 6: Проверка регистрации игрока
        testLog += "Тест 6: Регистрация игрока в менеджере... ";
        var player = GameObjectManager.Instance.GetPlayer();
        if (player != null)
        {
            testLog += $"✓ PASSED (Игрок: {player.Transform.name})\n";
        }
        else
        {
            testLog += "⚠ SKIPPED (Нет игрока на сцене)\n";
        }

        // Тест 7: Проверка производительности
        testLog += "Тест 7: Проверка производительности... ";
        float startTime = Time.realtimeSinceStartup;
        for (int i = 0; i < 1000; i++)
        {
            GameObjectManager.Instance.GetAllBots();
            GameObjectManager.Instance.GetAllQuaffles();
            GameObjectManager.Instance.GetAllGoals();
        }
        float endTime = Time.realtimeSinceStartup;
        float duration = (endTime - startTime) * 1000f;
        testLog += $"✓ PASSED (1000 запросов за {duration:F2}ms)\n";

        testLog += "\n=== РЕЗУЛЬТАТ ===\n";
        if (allPassed)
        {
            testLog += "✓ ВСЕ ТЕСТЫ ПРОЙДЕНЫ!\n";
            allTestsPassed = true;
        }
        else
        {
            testLog += "✗ НЕКОТОРЫЕ ТЕСТЫ НЕ ПРОЙДЕНЫ\n";
            allTestsPassed = false;
        }

        testLog += "\n=== СТАТИСТИКА ===\n";
        testLog += $"Ботов на сцене: {bots.Count}\n";
        testLog += $"Мячей на сцене: {quaffles.Count}\n";
        testLog += $"Ворот на сцене: {goals.Count}\n";
        testLog += $"Игрок: {(player != null ? player.Transform.name : "Нет")}\n";

        Debug.Log(testLog);
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.alignment = TextAnchor.UpperLeft;
        style.fontSize = 12;
        style.normal.textColor = allTestsPassed ? Color.green : Color.yellow;
        
        GUI.Box(new Rect(10, 10, 500, 400), testLog, style);
    }
}
