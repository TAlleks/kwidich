using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class VRMainMenu : MonoBehaviour
{
    [Header("Настройки VR")]
    [SerializeField] private string targetScene = "qwidspeed";
    [SerializeField] private XRBaseInteractable startButtonInteractable;

    private bool isLoading = false;

    void Start()
    {
        // Подписываемся на события VR кнопки
        if (startButtonInteractable != null)
        {
            startButtonInteractable.selectEntered.AddListener(OnVRButtonPressed);
        }
        else
        {
            Debug.LogError("VR кнопка не назначена!");
        }
    }

    private void OnVRButtonPressed(SelectEnterEventArgs args)
    {
        if (!isLoading)
        {
            LoadQwidspeedScene();
        }
    }

    public void LoadQwidspeedScene()
    {
        if (isLoading) return;

        isLoading = true;
        Debug.Log("Переход в сцену: qwidspeed");

        SceneManager.LoadScene(targetScene);
    }

    void OnDestroy()
    {
        if (startButtonInteractable != null)
        {
            startButtonInteractable.selectEntered.RemoveListener(OnVRButtonPressed);
        }
    }
}