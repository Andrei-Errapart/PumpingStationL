using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ControlPanel
{
    /// <summary>
    /// 
    /// </summary>
    public class SignalValuesType
    {
        /// <summary>
        /// Row Id in the database.
        /// </summary>
        public int Id;
        /// <summary>
        /// Database version, if anyone is interested in that.
        /// </summary>
        public int Version;
        /// <summary>
        /// Timestamp, in .NET Ticks.
        /// </summary>
        public long Timestamp;
        /// <summary>
        /// Raw signal values.
        /// </summary>
        public byte[] Values;
    }
}
