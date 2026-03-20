using System;

namespace Futurift.Options
{
    [Serializable]
    public class FutuRiftOptions
    {
        /// <summary>
        /// Interval for sending commands, in milliseconds
        /// </summary>
        public double interval = 100;
    }
}
