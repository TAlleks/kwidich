using LogitechG29.Sample.Input;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [SerializeField] AudioSource audioSource;
    [SerializeField] private InputControllerReader inputControllerReader;

    private void Update()
    {
        if (inputControllerReader.NorthButton)
        {
            PlayButton();
        }
        if (inputControllerReader.SouthButton)
        {
            ExitButton();
        }
    }
    public void PlayButton()
    {
        audioSource.Play();
        SceneManager.LoadScene("QwidSpeed");
    }

    public void ExitButton()
    {
        audioSource.Play();
        Application.Quit();
    }
}
