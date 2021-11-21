// Dump the [TimeStamp] and signal values from the PLC database.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Data;
using System.Data.SQLite;

namespace DumpSignalValues
{
    class Program
    {
        // ==================================================================
        static IOSignal _FetchByKey(IEnumerable<KeyValuePair<string, IOSignal>> UsedSignals, string Key)
        {
            return (from kv in UsedSignals where kv.Key == Key select kv.Value).SingleOrDefault();
        }

        static int _FetchIntegerAttribute(XmlTextReader reader, string attribute_name)
        {
            return int.Parse(reader.GetAttribute(attribute_name));
        }

        // ==================================================================
        /// <summary>
        /// Parse PLC configuration.
        /// </summary>
        /// <param name="NewSignals"></param>
        /// <param name="Input">Configuration to be parsed.</param>
        static void _ParsePlcConfiguration(
            List<IOSignal> NewSignals,
            List<ComputedSignal> NewComputedSignals,
            List<SignalGroup> InfopanelGroups,
            List<SignalGroup> ChartGroups,
            List<SignalGroup> WorkHoursGroups,
            List<SignalGroup> StationGroups,
            System.IO.Stream Input)
        {
            // List of top-level groups fetched.
            List<SignalGroup> groups = null;
            // Stack of groups during parsing.
            List<SignalGroup> groupstack = new List<SignalGroup>();

            // 1. Parse the input.
            using (var reader = new XmlTextReader(Input))
            {
                string ios_device = "";
                while (reader.Read())
                {
                    // 1. Devices.
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "device")
                    {
                        ios_device = reader.GetAttribute("address") ?? "";
                    }
                    // 2. Signals.
                    else if (reader.NodeType == XmlNodeType.Element && reader.Name == "signal")
                    {
                        IOSignal ios;
                        int ios_id = _FetchIntegerAttribute(reader, "id");
                        string ios_name = reader.GetAttribute("name") ?? "";
                        string ios_type_name = reader.GetAttribute("type");
                        string ios_ioindex = reader.GetAttribute("ioindex") ?? "";
                        string ios_description = reader.GetAttribute("description") ?? "";
                        string ios_text0 = reader.GetAttribute("text0") ?? "0";
                        string ios_text1 = reader.GetAttribute("text1") ?? "1";
                        IOSignal.TYPE? ios_type = IOSignal.TypeOfString(ios_type_name);
                        if (ios_type.HasValue)
                        {
                            ios = new IOSignal(ios_id, ios_name, false, ios_type.Value, ios_device, ios_ioindex, ios_description, ios_text0, ios_text1);
                        }
                        else
                        {
                            throw new ApplicationException("PlcConfiguration.startElement: Invalid value for signal type: '" + ios_type_name + "'");
                        }
                        NewSignals.Add(ios);
                    }
                    else if (reader.NodeType == XmlNodeType.Element && reader.Name == "computedsignal")
                    {
                        string cs_name = reader.GetAttribute("name");
                        string s_type_name = reader.GetAttribute("type");
                        var cs_type_name = ComputedSignal.ComputationTypeOf(s_type_name);
                        string s_source_signals = reader.GetAttribute("sources");
                        string[] cs_source_signals = s_source_signals == null || s_source_signals.Length == 0 ? new string[] { } : s_source_signals.Split(new char[] { ';' });
                        string cs_parameters = reader.GetAttribute("params") ?? "";
                        string cs_formatstring = reader.GetAttribute("formatstring") ?? "0.0";
                        string cs_unit = reader.GetAttribute("unit") ?? "";
                        string cs_description = reader.GetAttribute("description");
                        var new_computed_signal = new ComputedSignal(
                            cs_name, cs_type_name,
                            cs_source_signals, cs_parameters,
                            cs_formatstring, cs_unit, cs_description);
                        NewComputedSignals.Add(new_computed_signal);
#if (false)
                        NewComputedSignals.Add(new ComputedSignal(
                            "LIA1.READING",
                            ComputedSignal.COMPUTATION_TYPE.ANALOG_SENSOR,
                            new string[] { "LIA1" }, "4;20;0;50", "0.0", "m", "Puurkaevu nivoo"));
#endif
                    }
                    // 2. Variables
                    else if (reader.NodeType == XmlNodeType.Element && reader.Name == "variable")
                    {
                        // FIXME: do what?
                        IOSignal ios;
                        string ios_name = reader.GetAttribute("name");
                        string ios_type_name = reader.GetAttribute("type");
                        IOSignal.TYPE? ios_type = IOSignal.TypeOfString(ios_type_name);
                        string ios_description = reader.GetAttribute("description") ?? "";
                        string ios_text0 = reader.GetAttribute("text0") ?? "0";
                        string ios_text1 = reader.GetAttribute("text1") ?? "1";
                        if (ios_name == null)
                        {
                            throw new ApplicationException("PlcConfiguration.startElement: Variable without a name detected!");
                        }
                        if (ios_type.HasValue)
                        {
                            ios = new IOSignal(-1, ios_name, true, ios_type.Value, "", "", ios_description, ios_text0, ios_text1);
                        }
                        else
                        {
                            throw new ApplicationException("PlcConfiguration.startElement: Invalid value for variable type: '" + ios_type_name + "'");
                        }
                        NewSignals.Add(ios);
                    }
                    // 3. Groups
                    else if (reader.Name == "group")
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            var g = new SignalGroup() { Name = reader.GetAttribute("name"), };
                            var lg = groupstack.LastOrDefault();
                            (lg == null ? groups : lg.Groups).Add(g);
                            groupstack.Add(g);
                        }
                        else if (reader.NodeType == XmlNodeType.EndElement)
                        {
                            // one off from the stack...
                            groupstack.RemoveAt(groupstack.Count - 1);
                        }
                    }
                    else if (reader.Name == "scheme")
                    {
                        switch (reader.NodeType)
                        {
                            case XmlNodeType.Element:
                                {
                                    string stype = reader.GetAttribute("type");
                                    if (stype == "charts")
                                    {
                                        groups = ChartGroups;
                                    }
                                    else if (stype == "infopanel")
                                    {
                                        groups = InfopanelGroups;
                                    }
                                    else if (stype == "workhours")
                                    {
                                        groups = WorkHoursGroups;
                                    }
                                    else if (stype == "stations")
                                    {
                                        groups = StationGroups;
                                    }
                                    else
                                    {
                                        // just don't use it, but don't crack either.
                                        // LogLine("_ParsePlcConfiguration: unknown scheme type '" + stype + "'.");
                                        groups = new List<SignalGroup>();
                                    }
                                }
                                break;
                            case XmlNodeType.EndElement:
                                break;
                        }
                    }
                    // 4. UsedSignals.
                    else if (reader.Name == "usesignal" && reader.NodeType == XmlNodeType.Element)
                    {
                        string ios_key = reader.GetAttribute("key") ?? "";
                        string ios_name = reader.GetAttribute("signal") ?? "";
                        // It is either a physical signal or computed signal.
                        IOSignal ios = NewSignals.SingleOrDefaultByNameOrId(ios_name);
                        if (ios != null)
                        {
                            groupstack.Last().Signals.Add(new KeyValuePair<string, IOSignal>(ios_key, ios));
                        }
                        else
                        {
                            // linq version threw exceptions (ios was null). TODO: why it didn't work???
                            // ComputedSignal cs = (from cios in NewComputedSignals where ios.Name == ios_name select cios).SingleOrDefault();
                            ComputedSignal cs = NewComputedSignals.FirstOrDefault(cios => cios.Name == ios_name);
                            // (from cios in NewComputedSignals where ios.Name == ios_name select cios).SingleOrDefault();
                            if (cs != null)
                            {
                                groupstack.Last().Signals.Add(new KeyValuePair<string, IOSignal>(ios_key, cs));
                            }
                        }
                    }
                    // 5. usedevice
                    else if (reader.Name == "usedevice" && reader.NodeType == XmlNodeType.Element)
                    {
                        string device = reader.GetAttribute("device");
                        if (device != null)
                        {
                            groupstack.Last().Devices.Add(device);
                        }
                    }
                }
            }
        }

        /*
        let opendb_and_fetchenv (db_filename:string) =
            let db = new SQLiteConnection("Data Source=\"" + db_filename + "\"")
            db.Open()
            let env = new Dictionary<string,string>()
            squery db "SELECT Id, Name, Value FROM Environment" (fun (r:SQLiteDataReader) -> env.Add(r.GetString(1), r.GetString(2)) )
            db, env
         */
        // ==================================================================
        static void squery (SQLiteConnection db, string query_string, SQLiteDataReader row_fun)
        {
            // use cmd = new SQLiteCommand(query_string, db)
            // rquery cmd row_fun
        }

        // ==================================================================
        static byte[] reader_getbytes (SQLiteDataReader reader, int index)
        {
            const int CHUNK_SIZE = 2 * 1024;
            var buffer = new byte[CHUNK_SIZE];
            int bytes_read = 1;
            int field_offset = 0;
            using (var stream = new System.IO.MemoryStream()) {
                while (bytes_read>0) {
                    bytes_read = (int)reader.GetBytes(index, field_offset, buffer, 0, buffer.Length);
                    stream.Write(buffer, 0, bytes_read);
                    if (bytes_read>0) {
                        field_offset = field_offset + bytes_read;
                    }
                }            
                return stream.ToArray();
            }
        }

        // ==================================================================
        static byte[] FetchSetup(SQLiteConnection db)
        {
            byte[] setup_xml = null;
            using (var cmd = new SQLiteCommand("SELECT Value FROM ConfigurationItem WHERE Name='Setup' ORDER BY ConfigurationId DESC LIMIT 1", db))
            {
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        setup_xml = reader_getbytes(r, 0);
                    }
                }
            }
            if (setup_xml == null)
            {
                throw new System.ApplicationException("No setup found in the database!");
            }
            return setup_xml;
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

        // ==================================================================
        /// <summary>
        /// Extract signals from the packet.
        /// </summary>
        /// <param name="Buffer"></param>
        /// <param name="PackedSignals"></param>
        public static void ExtractSignals(List<IOSignal> PhysicalSignals, Tuple<bool, int>[] Buffer, byte[] Packet)
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

        // ==================================================================
        static DateTime _RefTimeJava = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static long JavaMillisecondsOfDateTime (DateTime dt)
        {
            var utc = dt.ToUniversalTime();
            var ts = utc.Subtract(_RefTimeJava);
            var minutes = (long) ts.TotalMinutes;
            var milliseconds = ts.Seconds * 1000 + ts.Milliseconds;
            return minutes * 60000L + ((long)milliseconds);
        }

        // ==================================================================
        static int Main(string[] args)
        {
            var db_filename = args.Length > 0 ? args[0] : "Ranna VTJ.plc";
            var cout = System.Console.Out;
            try
            {
                // 1. Open the db...
                var db = new SQLiteConnection("Data Source=\"" + db_filename + "\"");
                db.Open();
                byte[] setup_xml = FetchSetup(db);

                // 2. Parse the configuration from the DB.
                var NewSignals = new List<IOSignal>();
                var NewComputedSignals = new List<ComputedSignal>();
                var InfopanelGroups = new List<SignalGroup>();
                var ChartGroups = new List<SignalGroup>();
                var WorkHoursGroups = new List<SignalGroup>();
                var StationGroups = new List<SignalGroup>();
                using (var fr = new System.IO.MemoryStream(setup_xml))
                {
                    _ParsePlcConfiguration(NewSignals, NewComputedSignals, InfopanelGroups, ChartGroups, WorkHoursGroups, StationGroups, fr);
                }
                cout.WriteLine("Read {0} signals, {1} computed signals.", NewSignals.Count, NewComputedSignals.Count);

                // 3. Find our signals.
                int ntotal = NewSignals.Count;
                string[] signal_names = { "FQI1", "FQI2", "FQI3" };
                int[] signal_indices = { -1, -1, -1 };

                for (int signal_index = 0; signal_index < ntotal; ++signal_index)
                {
                    var ios = NewSignals[signal_index];
                    for (int name_index = 0; name_index < signal_names.Length; ++name_index)
                    {
                        if (signal_names[name_index] == ios.Name)
                        {
                            signal_indices[name_index] = signal_index;
                        }
                    }
                }
                // Got all?
                for (int name_index = 0; name_index < signal_names.Length; ++name_index)
                {
                    if (signal_indices[name_index] < 0)
                    {
                        cout.WriteLine("Signal {0} not found!", signal_names[name_index]);
                        return 1;
                    }
                }
                
                // 4. Scan the database.
                using (var cmd = new SQLiteCommand("SELECT OriginalId, [TimeStamp], SignalValues FROM SignalValue ORDER BY OriginalId ASC", db))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        var Buffer = new Tuple<bool, int>[ntotal];
                        var prev_ts = 0L;
                        var prev_id = 0;
                        int[] prev_values = new int[signal_names.Length];
                        while (reader.Read())
                        {
                            var original_id = reader.GetInt32(0);
                            var timestamp = reader.GetDateTime(1);
                            var java_timestamp = JavaMillisecondsOfDateTime(timestamp);
                            var signals_packet = reader_getbytes(reader, 2);

                            ExtractSignals(NewSignals, Buffer, signals_packet);

                            cout.Write("{0}\t", timestamp);
                            cout.Write("{0}\t{1}", original_id, original_id - prev_id);
                            prev_id = original_id;

                            cout.Write("\t{0}\t{1}", java_timestamp, java_timestamp - prev_ts);
                            prev_ts = java_timestamp;
                            for (int name_index = 0; name_index < signal_names.Length; ++name_index)
                            {
                                int signal_index = signal_indices[name_index];
                                var count = Buffer[signal_index].Item2;
                                cout.Write("\t{0}\t{1}\t{2}", Buffer[signal_index].Item1, count, count - prev_values[name_index]);
                                prev_values[name_index] = count;
                            }
                            cout.WriteLine();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                cout.WriteLine("Error: " + ex.Message);
                return 1;
            }
            return 0;
        }
    }
}
