using System.IO.Ports;
using Futurift.Options;

namespace Futurift.DataSenders
{
    public class ComPortSender : IDataSender
    {
        public bool IsConnected => _port.IsOpen;

        public SerialPort Port => _port;

        private readonly SerialPort _port;

        public ComPortSender(ComPortOptions options) => _port = new SerialPort
        {
            BaudRate = 115200,
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            ReadBufferSize = 4096,
            WriteBufferSize = 4096,
            ReadTimeout = 500,
            PortName = $"COM{options.comPort}",
        };

        public void SendData(byte[] data) => _port.Write(data, 0, data.Length);

        public void Start() => _port.Open();
        public void Stop() => _port.Close();
    }
}