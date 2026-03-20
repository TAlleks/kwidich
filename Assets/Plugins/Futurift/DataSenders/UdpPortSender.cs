using System.Net;
using System.Net.Sockets;
using Futurift.Options;

namespace Futurift.DataSenders
{
    public class UdpPortSender : IDataSender
    {
        public bool IsConnected => true;

        private readonly UdpClient _udpClient;
        private readonly IPEndPoint _endPoint;

        public UdpPortSender(UdpOptions options)
        {
            _udpClient = new UdpClient();
            _endPoint = new IPEndPoint(IPAddress.Parse(options.ip), options.port);
        }


        public void SendData(byte[] data)
        {
            _udpClient.Send(data, data.Length, _endPoint);
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }
    }
}