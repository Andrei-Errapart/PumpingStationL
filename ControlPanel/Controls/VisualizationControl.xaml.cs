using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using System.Xml.Linq;

using PlcCommunication;

namespace ControlPanel
{
    /// <summary>
    /// Interaction logic for VisualizationControl.xaml
    /// </summary>
    public partial class VisualizationControl : UserControl
    {
        #region GUI PROPERTIES
        public MainWindow MainWindow { get; set; }

        /// <summary>TODO: better name.</summary>
        public string DisplayName { get; set; }
       
        /// <summary>Title in the tabcontrol.</summary>
        public string DisplayTitle { get; set; }

        public int PlcId { get; set; }

        /// <summary>Signals to be displayed.</summary>
        public ObservableCollection<IOSignal> Signals { get; set; }

        /// <summary>Layers on the main canvas (scheme).</summary>
        public Dictionary<string, SchemeLayer> Layers;

        /// <summary>Table of all signals.</summary>
        public Dictionary<int, IOSignal> SignalTable = new Dictionary<int, IOSignal>();
        /// <summary>Dictionary of all signals.</summary>
        public Dictionary<string, IOSignal> SignalDict = new Dictionary<string, IOSignal>();
        /// <summary>List of physical signals.</summary>
        public List<IOSignal> PhysicalSignals = new List<IOSignal>();
        /// <summary>List of computed signals.</summary>
        public List<ComputedSignal> ComputedSignals = new List<ComputedSignal>();

        /// <summary>
        /// Signal groups displayed.
        /// </summary>
        public List<SignalGroupToplevel> Signal_Groups = new List<SignalGroupToplevel>();
        /// <summary>Chart groups ()default values), if any.</summary>
        public List<SignalGroup> Chart_Groups = new List<SignalGroup>();

        /// <summary>Buttons on the scheme for the activation of tab items.</summary>
        List<Button> SchemeButtons = new List<Button>();

        /// <summary>Header for the tabitem; to be set and used in the main program.</summary>
        public TextBlock TabItemHeader;
        #endregion // GUI PROPERTIES

        /// <summary>
        /// Scheme program statements.
        /// </summary>
        public List<SchemeStatement> SchemeStatements = new List<SchemeStatement>();
        /// <summary>
        /// Last signal update time.
        /// </summary>
        public DateTime? LastUpdateTime = null;

        /// <summary>
        /// Version of configuration.
        /// </summary>
        int ConfigurationVersion = -1;

        // label -> id
        Dictionary<string, SchemeLayer> _FetchLayerTable(string SvgFilename, DrawingCollection layers)
        {
            using (var stream = new System.IO.FileStream(SvgFilename, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            {
                var r = new Dictionary<string, SchemeLayer>();
                var svg = XDocument.Load(stream);

                var lst = from item in svg.Descendants()
                          let label = (from at1 in item.Attributes() where at1.Name.LocalName == "label" select at1).SingleOrDefault()
                          let id = (from at2 in item.Attributes() where at2.Name.LocalName == "id" select at2).SingleOrDefault()
                          where item.Name.LocalName == "g" && label != null && id != null
                          select label.Value; //  new KeyValuePair<string, string>(label.Value, id.Value);
                int index = 0;

                foreach (var id in lst)
                {
                    if (index < layers.Count)
                    {
                        r[id] = new SchemeLayer(id, layers[index] as DrawingGroup);
                    }
                    ++index;
                }
                return r;
            }
        }

        DrawingGroup _AddDrawingToCanvas(string Filename)
        {
            using (var stream = new System.IO.FileStream(Filename, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            {
                var xmlr = System.Xml.XmlReader.Create(stream);
                var g = XamlReader.Load(xmlr) as DrawingGroup;
                drawingimageScada.Drawing = g;
                var dg = g.Children[0] as DrawingGroup;
                var rc = (dg.ClipGeometry as RectangleGeometry).Rect;
                mainCanvas.Width = rc.Width;
                mainCanvas.Height = rc.Height;
                imageSCADA.Width = rc.Width;
                imageSCADA.Height = rc.Height;
                return g;
            }
        }

        static Tuple<DependencyObject, MethodInfo> _FindChildWithPrintMethod(DependencyObject control)
        {
            int n = VisualTreeHelper.GetChildrenCount(control);
            // 1. Try to get the method from children.
            for (int child_index = 0; child_index < n; ++child_index)
            {
                var ch = VisualTreeHelper.GetChild(control, child_index);
                var mtc = ch.GetType().GetMethod("Print");
                if (mtc != null)
                {
                    return new Tuple<DependencyObject, MethodInfo>(ch, mtc);
                }
            }

            // 2. Try to get the print method from grandchildren.
            for (int child_index = 0; child_index < n; ++child_index)
            {
                var ch = VisualTreeHelper.GetChild(control, child_index);
                var r = _FindChildWithPrintMethod(ch);
                if (r != null)
                {
                    return r;
                }
            }

            // 3. Fail.
            return null;
        }

        static IOSignal _FetchByKey(IEnumerable<KeyValuePair<string, IOSignal>> UsedSignals, string Key)
        {
            return (from kv in UsedSignals where kv.Key == Key select kv.Value).SingleOrDefault();
        }

        static int _FetchIntegerAttribute(XmlTextReader reader, string attribute_name)
        {
            return int.Parse(reader.GetAttribute(attribute_name));
        }

        /// <summary>
        /// Parse PLC configuration.
        /// </summary>
        /// <param name="NewSignals"></param>
        /// <param name="Input">Configuration to be parsed.</param>
        static void _ParsePlcConfiguration(
            ref string NewDisplayTitle,
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
                    // 0. DisplayName, if any.
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "plcmaster")
                    {
                        string display_title = reader.GetAttribute("title");
                        if (display_title != null && display_title.Length > 0)
                        {
                            NewDisplayTitle = display_title;
                        }
                    }
                    // 1. Devices.
                    else if (reader.NodeType == XmlNodeType.Element && reader.Name == "device")
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

        // Detect all-zero configurations and replace them with custom resource.
        System.IO.Stream _GetConfigurationStream(byte[] ConfigurationFile)
        {
            if ((from x in ConfigurationFile where x != 0 select x).Count() > 0)
            {
                return new System.IO.MemoryStream(ConfigurationFile);
            }
            else
            {
                var names = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames();
                var ri = Application.GetResourceStream(new Uri("/ControlPanel;component/Resources/setup.xml", UriKind.Relative));
                return ri.Stream;
            }
        }

        Tuple<bool, int>[] _UnpackBuffer = null;
        /// <summary>
        /// Update the _Signals.
        /// </summary>
        /// <param name="Packet">Source packet.</param>
        /// <param name="Offset">Offset to start with.</param>
        /// <param name="Length">Length of payload, in bytes.</param>
        void _Update_Signals(ServerConnection PlcConnection, byte[] Packet, int Offset, int Length)
        {
            if (_UnpackBuffer == null || _UnpackBuffer.Length < Signals.Count)
            {
                _UnpackBuffer = new Tuple<bool, int>[Signals.Count];
            }
            this.PhysicalSignals.ExtractSignals(_UnpackBuffer, Packet);
            int signal_index = 0;

            // Physical signals first.
            foreach (var ios in PhysicalSignals)
            {
                var up = _UnpackBuffer[signal_index];
                ios.Update(up.Item1, up.Item2);

                ++signal_index;
            }
            // Computed signals later.
            foreach (var cios in ComputedSignals)
            {
                cios.Update();
            }
        }

        void _SchemeButton_OnClick(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.Tag != null)
            {
                var index = (int)btn.Tag;
                tabGroupDetails.SelectedIndex = index;
            }
        }

        /// <summary>
        /// Set visibility on the layers in SchemeContext whose name matches 'ti.Header + ".Selected"', where ti is an TabItem in "items".
        /// </summary>
        /// <param name="items"></param>
        /// <param name="action"></param>
        void _SetVisibilityOnCorrespondingLayers(System.Collections.IList items, bool IsVisible)
        {
            foreach (var i in items)
            {
                var ti = i as TabItem;
                if (ti != null)
                {
                    string header = ti.Header as string;
                    SchemeLayer layer = null;
                    if (header != null && Layers.TryGetValue(header + ".Selected", out layer))
                    {
                        layer.IsVisible = IsVisible;
                    }
                }
            }
        }

        #region PUBLIC INTERFACE
        public void LogLine(string Text)
        {
            var mw = this.MainWindow;
            if (mw != null)
            {
                mw.LogLine(DisplayName ?? "VisualizationControl", Text);
            }
        }

        int _Timer_Tick_Count = 0;
        bool _Timer_LastAnyBlinks = false;
        bool _Timer_LastIsConnected = false;
        /// <summary>
        /// Process the 
        /// Called once per second.
        /// </summary>
        public void TimerTick(ref bool sound_a_beep)
        {
            bool any_blinks = false;
            string blinking_layer_name = "";
            foreach (var st in SchemeStatements)
            {
                var layer = st.Layer;
                if (st.Type == SchemeStatement.TYPE.BLINK && layer.IsBlinking)
                {
                    layer.IsVisible = (_Timer_Tick_Count & 1) != 0;
                    blinking_layer_name = layer.Name;
                    any_blinks = true;
                    if (st.MuteButton == null || !st.MuteButton.IsMuted)
                    {
                        sound_a_beep = true;
                    }
                }
            }
            ++_Timer_Tick_Count;

            // If there are blinking layers, attention should be drawn on the corresponding detailed info panel.
            if (any_blinks && !_Timer_LastAnyBlinks)
            {
                var v = blinking_layer_name.Split(new char[] { '.' }, 2);
                var group_name = v[0];
                for (int sg_index = 0; sg_index < Signal_Groups.Count; ++sg_index)
                {
                    if (Signal_Groups[sg_index].GroupName == group_name)
                    {
                        tabGroupDetails.SelectedIndex = sg_index;
                    }
                }
            }
            _Timer_LastAnyBlinks = any_blinks;

            // We have to keep the connection open...
            // Sometimes the connection drops without the socket noticing it; this is detected by using timeouts.
            var t0 = LastUpdateTime;
            var t1 = DateTime.Now;
            var is_connected = true;
            if (t0.HasValue)
            {
                TimeSpan diff = t1.Subtract(t0.Value);
                int total_seconds = (int)Math.Round(diff.TotalSeconds);
                is_connected = total_seconds < MainWindow.LocalConfiguration.PlcDataTimeout;
                this.textblockLastUpdated.Text = total_seconds.ToString() + " sekundit tagasi (" + t0.Value.ToString() + ")";
            }
            else
            {
                this.textblockLastUpdated.Text = "Ei";
                is_connected = false;
            }
            this.textblockLastUpdated.Foreground = is_connected ? MainWindow.Foreground : Brushes.Red;
            this.textblockLastUpdatedLabel.Foreground = is_connected ? MainWindow.Foreground : Brushes.Red;
            this.buttonMute.Visibility = is_connected ? Visibility.Collapsed : Visibility.Visible;
            this.borderDisconnected.BorderBrush = is_connected ? Brushes.Transparent : Brushes.Red;
            if (!is_connected && _Timer_LastIsConnected)
            {
                buttonMute.IsMuted = false;
            }
            _Timer_LastIsConnected = is_connected;
            if (!is_connected && !buttonMute.IsMuted)
            {
                sound_a_beep = true;
            }

            historycontrol.TimerTick();
            chartspanel.TimerTick();
        }

        public void HandleConfigurationFromPlc(int Version, byte[] ConfigurationFile)
        {
            this.ConfigurationVersion = Version;
            using (var ms = _GetConfigurationStream(ConfigurationFile))
            {
                List<IOSignal> new_signals = new List<IOSignal>();
                var infopanel_groups = new List<SignalGroup>();
                var workhour_groups = new List<SignalGroup>();
                var station_groups = new List<SignalGroup>();
                var new_computed_signals = new List<ComputedSignal>();
                string new_display_title = "";
                Chart_Groups.Clear();
                _ParsePlcConfiguration(ref new_display_title, new_signals, new_computed_signals, infopanel_groups, Chart_Groups, workhour_groups, station_groups, ms);
                if (new_display_title != null && new_display_title.Length > 0)
                {
                    DisplayTitle = new_display_title;
                }
                // Tables in _SchemeContext need updating.
                PhysicalSignals.Clear();
                Signals.Clear();
                SignalTable.Clear();
                SignalDict.Clear();
                ComputedSignals.Clear();
                foreach (var ios in new_signals)
                {
                    Signals.Add(ios);
                    PhysicalSignals.Add(ios);
                    if (ios.Name.Length > 0)
                    {
                        SignalDict.Add(ios.Name, ios);
                    }
                    if (ios.Id >= 0)
                    {
                        SignalTable.Add(ios.Id, ios);
                    }
                }
                foreach (var cs in new_computed_signals)
                {
                    cs.ConnectWithSourceSignals(SignalDict);
                    SignalDict.Add(cs.Name, cs);
                    Signals.Add(cs);
                    ComputedSignals.Add(cs);
                }

                // 2. Update the info panel tabGroupDetails.
                tabGroupDetails.Items.Clear();
                Signal_Groups.Clear();

                foreach (var g in infopanel_groups)
                {
                    // 1. Toplevel.
                    var gt = new SignalGroupToplevel();
                    gt.GroupName = g.Name;
                    foreach (var s in g.Signals)
                    {
                        gt.DisplaySignals.Add(s.Value);
                    }
                    // 3. Add it to the tab control and to the list, too.
                    tabGroupDetails.Items.Add(new TabItem() { Header = g.Name, Content = gt });
                    Signal_Groups.Add(gt);
                }

                // Clean up the selection mess of the details panels TabControl.
                _SetVisibilityOnCorrespondingLayers(tabGroupDetails.Items, false);
                _SetVisibilityOnCorrespondingLayers(new object[] { tabGroupDetails.SelectedItem }, true);

                // New buttons on the scheme are needed.
                for (int button_index = SchemeButtons.Count - 1; button_index >= 0; --button_index)
                {
                    mainCanvas.Children.Remove(SchemeButtons[button_index]);
                    SchemeButtons.RemoveAt(button_index);
                }

                // Find the insert position such that selection buttons will be placed below mute buttons.
                int insert_position = 0;
                for (int ch_index = mainCanvas.Children.Count - 1; ch_index >= 0; --ch_index)
                {
                    var mb = mainCanvas.Children[ch_index] as MuteButton;
                    if (mb == null)
                    {
                        insert_position = ch_index + 1;
                        break;
                    }
                }

                // foreach (var si in _Signal_Groups)
                for (int sg_index = 0; sg_index < Signal_Groups.Count; ++sg_index)
                {
                    var si = Signal_Groups[sg_index];
                    var layer_name = si.GroupName + ".Selected";
                    SchemeLayer scheme_layer;
                    if (Layers.TryGetValue(layer_name, out scheme_layer))
                    {
                        // ok, new button!
                        var bounds = scheme_layer.Layer.Bounds;
                        var b = new Button()
                        {
                            Content = "Push ME!",
                            FontSize = 30,
                            Foreground = System.Windows.Media.Brushes.Red,
                            Opacity = 0.0,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch,
                            Width = bounds.Width,
                            Height = bounds.Height,
                        };
                        b.SetValue(Canvas.LeftProperty, bounds.Left);
                        b.SetValue(Canvas.TopProperty, bounds.Top);
                        b.Tag = sg_index;
                        b.Click += _SchemeButton_OnClick;
                        mainCanvas.Children.Insert(insert_position, b);
                        SchemeButtons.Add(b);
                    }
                }

                // 4. Working log update.
                var new_working_times = new ObservableCollection<WorkingTimeCell>();
                foreach (var wg in workhour_groups)
                {
                    foreach (var ks in wg.Signals)
                    {
                        var wt = new WorkingTimeCell()
                        {
                            DisplayName = ks.Key,
                            LastKnownValue = new Tuple<bool, int>(false, 0),
                            SignalIndex = Signals.IndexOf(ks.Value),
                            WorkingTicks = 0,
                        };
                        new_working_times.Add(wt);
                    }
                }

                // For now, that's all.
                // LogLine(string.Format("Received configuration, {0} signals in total.", new_signals.Count));
            }
            // this.LastUpdateTime = DateTime.Now;
        }

        public void HandleMessageFromPlc(ServerConnection sender, PlcCommunication.MessageFromPlc message)
        {
            try
            {
                if (message.HasId)
                {
                    // 1. Any configuration?
                    if (message.HasOOBConfiguration)
                    {
                        var cfg = message.OOBConfiguration;
                        if (cfg.HasVersion && cfg.HasConfigurationFile)
                        {
                            LogLine("Received configuration from the server, ignoring it.");
                            /*
                            HandleConfigurationFromPlc(cfg.Version, cfg.ConfigurationFile.ToByteArray());
                             */
                        }
                    } // message.HasOOBConfiguration

                    // 2. Any signals?
                    if (message.HasOOBDatabaseRow)
                    {
                        var oob_signal = message.OOBDatabaseRow;
                        if (oob_signal.HasVersion && oob_signal.HasTimeMs && oob_signal.HasSignalValues)
                        {
                            // TODO: handle version and time.
                            var encoded_signals = oob_signal.SignalValues.ToByteArray();
                            _Update_Signals(sender, encoded_signals, 0, encoded_signals.Length);
                            this.LastUpdateTime = DateTime.Now;

                            foreach (var st in SchemeStatements)
                            {
                                st.Execute();
                            }

                            // Charts!
                            var signals = new SignalValuesType()
                            {
                                Id = oob_signal.RowId,
                                Version = oob_signal.Version,
                                Timestamp = oob_signal.GetTimestamp().Ticks,
                                Values = encoded_signals,
                            };
                            historycontrol.AppendSignalValues(signals);
                            this.chartspanel.AppendSignalValues(signals);
                        }
                    } // message.HasOOBSignalValues

                    chartspanel.HandleMessageFromPlc(sender, message);
                } // message.HasId
            }
            catch (Exception ex)
            {
                // MessageBox.Show(App.TitleError, ex.Message, MessageBoxButton.OK);
                LogLine("_PacketHandler error: " + ex.Message);
                LogLine("Stack trace: " + ex.StackTrace);
            }
        }
        #endregion // PUBLIC INTERFACE

        #region GUI METHODS/CALLBACKS
        public VisualizationControl(MainWindow MainWindow, string BaseDir, string DisplayName)
        {
            this.Signals = new ObservableCollection<IOSignal>();
            this.MainWindow = MainWindow;
            this.DisplayName = DisplayName;
            this.DisplayTitle = DisplayName;

            this.DataContext = this;

            InitializeComponent();

            var bgd = _AddDrawingToCanvas(System.IO.Path.Combine(BaseDir, "Scheme.xaml"));
            Layers = _FetchLayerTable(System.IO.Path.Combine(BaseDir, "Scheme.svg"), (bgd.Children[0] as DrawingGroup).Children);

            // Binding as follows:
            // bg.Opacity <= IsConnected ? 1.0 : 0.0;
            var binding = new Binding()
            {
                Source = MainWindow,
                Path = new PropertyPath("IsConnected"),
                Mode = BindingMode.OneWay,
                Converter = new CSUtils.SelectOneOfTwo() { ValueWhenTrue = 1.0, ValueWhenFalse = 0.1, },
            };
            BindingOperations.SetBinding(bgd, DrawingGroup.OpacityProperty, binding);

            // 2. Load the image.

            // Load the configuration.
            var bytes = System.IO.File.ReadAllBytes(System.IO.Path.Combine(BaseDir, "setup.xml"));
            HandleConfigurationFromPlc(1, bytes);

            // _SchemeStatements
            var scheme_filename = System.IO.Path.Combine(BaseDir, App.SchemeProgramFilename);
            // LogLine("Loading scheme program file " + scheme_filename);
            var scanner = new Scanner(scheme_filename);
            var parser = new Parser(scanner);
            var sb = new StringBuilder();
            using (var error_stream = new System.IO.StringWriter(sb))
            {
                parser.Context = this;
                parser.Result = SchemeStatements;
                parser.errors.errorStream = error_stream;
                parser.Parse();
            }
            var lines = sb.ToString().Split(new string[] { "\r\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (line.Length > 0)
                {
                    LogLine(line);
                }
            }

            // Initialize charts.
            var defaults = new List<IOSignal>();
            var defs = Chart_Groups.FirstOrDefault();
            if (defs!=null)
            {
                foreach (var kv in defs.Signals)
                {
                    defaults.Add(kv.Value);
                }
            }
            chartspanel.Init(this, defaults);

            // LogLine("Scheme program loaded, " + SchemeStatements.Count + " statements in total.");
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void TabControl_InfoPanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _SetVisibilityOnCorrespondingLayers(e.AddedItems, true);
            _SetVisibilityOnCorrespondingLayers(e.RemovedItems, false);
        }

        PrintDialog _PrintDialog = new PrintDialog()
        {
        };

        private void Button_SetTo1_Click(object sender, RoutedEventArgs e)
        {
            _WriteSignalToPlcByButton(sender, 1);
        }
        private void Button_SetTo0_Click(object sender, RoutedEventArgs e)
        {
            _WriteSignalToPlcByButton(sender, 0);
        }

        private void _WriteSignalToPlcByButton(object Sender, int Value)
        {
            var button = Sender as Button;
            var signal = button.Tag as IOSignal;
            var new_kv = new KeyValuePair<string, int>(signal.Name.Length == 0 ? signal.Id.ToString() : signal.Name, Value);
            MainWindow.PlcConnection.WriteSignals(this, new KeyValuePair<string, int>[] { new_kv });
        }

        private void ButtonPrint_Click(object sender, RoutedEventArgs e)
        {
            var ti = tabcontrolMain.SelectedItem as TabItem;
            var tc = ti.Content as UIElement;
            var special_print = _FindChildWithPrintMethod(tc);
            if (_PrintDialog.ShowDialog() == true)
            {
                if (special_print == null)
                {
                    _PrintDialog.PrintVisual(tc, ti.Header.ToString());
                    // MessageBox.Show("Visuaali '" + ti.Header + "' ei saa trükkida!");
                }
                else
                {
                    special_print.Item2.Invoke(special_print.Item1, new object[] { _PrintDialog });
                }
            }
        }
        #endregion // GUI METHODS/CALLBACKS
    }
}
