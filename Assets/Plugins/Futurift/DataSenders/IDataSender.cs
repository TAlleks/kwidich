namespace Futurift.DataSenders
{
    public interface IDataSender
    {
        bool IsConnected { get; }

        void SendData(byte[] data);
        void Start();
        void Stop();
    }
}