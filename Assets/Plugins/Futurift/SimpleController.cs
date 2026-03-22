using Futurift.DataSenders;
using Futurift.Options;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Futurift
{
    public class SimpleController : MonoBehaviour
    {
        [SerializeField] private string ipAddress = "127.0.0.1";
        [SerializeField] private int port = 6065;

        [Header("XR Input")]
        [SerializeField] private InputActionProperty rightThumbstick; // движение + поворот

        private FutuRiftController _controller;

        private void Awake()
        {
            var udpOptions = new UdpOptions
            {
                ip = ipAddress,
                port = port
            };

            _controller = new FutuRiftController(new UdpPortSender(udpOptions));

            rightThumbstick.action.Enable();
        }
        
        private void Update()
        {
            Vector2 rightStick = rightThumbstick.action.ReadValue<Vector2>();
            float steering = rightStick.x;
            float input = rightStick.y;
            input *= 15f;
            steering *= 15f;
            var euler = transform.eulerAngles;
            _controller.Pitch = -input;
            _controller.Roll = steering ;
        }

        private void OnEnable()
        {
            _controller?.Start();
        }

        private void OnDisable()
        {
            _controller?.Stop();
        }
    }
}