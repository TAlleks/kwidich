using Futurift.DataSenders;
using Futurift.Options;
using UnityEngine;

namespace Futurift
{
    public class SimpleController : MonoBehaviour
    {
        [SerializeField] private string ipAddress = "127.0.0.1";
        [SerializeField] private int port = 6065;

        private FutuRiftController _controller;

        private void Awake()
        {
            var udpOptions = new UdpOptions
            {
                ip = ipAddress,
                port = port
            };

            _controller = new FutuRiftController(new UdpPortSender(udpOptions));
        }
        
        private void Update()
        {
            var euler = transform.eulerAngles;
            _controller.Pitch = (euler.x > 180 ? euler.x - 360 : euler.x);
            _controller.Roll = (euler.z > 180 ? euler.z - 360 : euler.z);
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