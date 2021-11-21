using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PlcCommunication;

namespace ControlPanel
{
    /// <summary>
    /// Helper class.
    /// </summary>
    public class SignalGroup
    {
        /// <summary>Name of the group.</summary>
        public string Name { get; set; }
        /// <summary>List of signals.</summary>
        public List<KeyValuePair<string, IOSignal>> Signals = new List<KeyValuePair<string, IOSignal>>();
        /// <summary>List of subgroups.</summary>
        public List<SignalGroup> Groups = new List<SignalGroup>();
        /// <summary>List of devices, if any.</summary>
        public List<string> Devices = new List<string>();
    }
}
