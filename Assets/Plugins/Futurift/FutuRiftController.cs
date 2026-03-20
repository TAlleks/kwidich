using System;
using System.Timers;
using Futurift.Data;
using Futurift.DataSenders;
using Futurift.Extensions;
using Futurift.Options;

namespace Futurift
{
    public class FutuRiftController
    {
        private readonly IDataSender _dataSender;
        private readonly byte[] _buffer = new byte[33];
        private readonly Timer _timer;

        private float _pitch;
        private float _roll;

        public bool IsConnected => _dataSender.IsConnected;

        public float Pitch
        {
            get => _pitch;
            set => _pitch = value.Clamp(-15, 21);
        }

        public float Roll
        {
            get => _roll;
            set => _roll = value.Clamp(-18, 18);
        }

        public FutuRiftController(IDataSender dataSender, FutuRiftOptions futuRiftOptions = null)
        {
            _dataSender = dataSender;

            _buffer[0] = MSG.SOM;
            _buffer[1] = 33;
            _buffer[2] = 12;
            _buffer[3] = (byte)Flag.OneBlock;

            futuRiftOptions ??= new FutuRiftOptions();
            _timer = new Timer(futuRiftOptions.interval);
            _timer.Elapsed += Timer_Elapsed;
        }

        public void Start()
        {
            _dataSender.Start();
            _timer.Start();
        }

        public void Stop()
        {
            _dataSender.Stop();
            _timer.Stop();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            byte index = 4;

            Fill(ref index, _pitch);
            Fill(ref index, _roll);

            _buffer[index++] = 0;
            _buffer[index++] = 0;
            _buffer[index++] = 0;
            _buffer[index++] = 0;

            Fill(ref index, FullCRC(_buffer, 1, index));

            _buffer[index++] = MSG.EOM;

            _dataSender.SendData(_buffer);
        }

        private void Fill(ref byte index, float value)
        {
            var arr = BitConverter.GetBytes(value);
            foreach (var b in arr)
            {
                AddByte(ref index, b);
            }
        }

        private void Fill(ref byte index, ushort value)
        {
            var arr = BitConverter.GetBytes(value);
            foreach (var b in arr)
            {
                AddByte(ref index, b);
            }
        }

        private void AddByte(ref byte index, byte value)
        {
            if (value >= MSG.ESC)
            {
                _buffer[index++] = MSG.ESC;
                _buffer[index++] = (byte)(value - MSG.ESC);
            }
            else
            {
                _buffer[index++] = value;
            }
        }

        private static ushort FullCRC(byte[] p, int start, int end)
        {
            ushort crc = 58005;
            for (var i = start; i < end; i++)
            {
                if (p[i] == MSG.ESC)
                {
                    i++;
                    crc = CRC16(crc, (byte)(p[i] + MSG.ESC));
                }
                else
                {
                    crc = CRC16(crc, p[i]);
                }
            }

            return crc;
        }

        private static ushort CRC16(ushort crc, byte b)
        {
            var num1 = (ushort)(byte.MaxValue & (crc >> 8 ^ b));
            var num2 = (ushort)(num1 ^ (uint)num1 >> 4);
            return (ushort)((crc ^ num2 << 4 ^ num2 >> 3) << 8 ^ (num2 ^ num2 << 5) & byte.MaxValue);
        }
    }
}