using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DumpSignalValues
{
    public static class ExtensionMethods
    {
        /// <summary>
        /// Find one IOSignal by either name or by Id.
        /// </summary>
        /// <param name="Signals">List of signals to search from.</param>
        /// <param name="NameOrId">Name, or Id to search by.</param>
        /// <returns></returns>
        public static IOSignal SingleOrDefaultByNameOrId(this IEnumerable<IOSignal> Signals, string NameOrId)
        {
            IOSignal r;
            int id;

            r = (from ios in Signals where ios.Name == NameOrId select ios).SingleOrDefault();
            if (r == null && int.TryParse(NameOrId, out id))
            {
                r = (from ios in Signals where ios.Id == id select ios).SingleOrDefault();
            }
            return r;
        }

        /// <summary>
        /// Datetime corresponding to Java's System.currentTimeMillis(). 
        /// </summary>
        /// <param name="SignalValues"></param>
        /// <returns></returns>
        public static DateTime GetTimestamp(this PlcCommunication.DatabaseRow SignalValues)
        {
            return _DateTimeOfJavaMilliseconds(SignalValues.TimeMs);
        }

        static DateTime _RefTimeJava = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static DateTime _DateTimeOfJavaMilliseconds(long ms)
        {
            // this two-step process should preserve precision :)
            long minutes = ms / 60000;
            long milliseconds = ms - minutes * 60000;
            var r = _RefTimeJava.AddMinutes(minutes).AddMilliseconds(milliseconds).ToLocalTime();
            return r;
        }

        static long _TicksOfJavaMilliseconds(long ms)
        {
            var r = _DateTimeOfJavaMilliseconds(ms);
            return r.Ticks;
        }

        /// <summary>
        /// Count the bytes needed for the packet buffer.
        /// </summary>
        public static int BytesInPacket(this List<IOSignal> PhysicalSignals)
        {
            int bits = 0;
            foreach (var ios in PhysicalSignals)
            {
                bits += 1 + ios.BitCount;
            }
            return (bits + 7) / 8;
        }

        static int _ExtractBit(byte[] Packet, ref int ByteIndex, ref int BitIndex)
        {
            int r = (Packet[ByteIndex] >> BitIndex) & 1;
            --BitIndex;
            if (BitIndex < 0)
            {
                BitIndex = 7;
                ++ByteIndex;
            }
            return r;
        }

        /// <summary>
        /// Extract signals from the packet.
        /// </summary>
        /// <param name="Buffer"></param>
        /// <param name="PackedSignals"></param>
        public static void ExtractSignals(this List<IOSignal> PhysicalSignals, Tuple<bool, int>[] Buffer, byte[] Packet)
        {
            int byte_index = 0;
            int bit_index = 7;
            int signal_index = 0;
            foreach (var ios in PhysicalSignals)
            {
                bool is_connected = _ExtractBit(Packet, ref byte_index, ref bit_index) != 0;
                int value = 0;
                for (int i = 0; i < ios.BitCount; ++i)
                {
                    value = (value << 1) | _ExtractBit(Packet, ref byte_index, ref bit_index);
                }
                Buffer[signal_index] = new Tuple<bool, int>(is_connected, value);

                ++signal_index;
            }
        }
    }
}
