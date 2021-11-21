using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Xml.Linq;
using System.Text;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Markup;
using System.Windows.Threading;

using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.IO.Ports;
using System.Reflection;

namespace ControlPanel
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 1-second timer for updating the title and driving the blinking.
        /// </summary>
        DispatcherTimer _Timer = null;

        /// <summary>
        /// Local configuration settings.
        /// </summary>
        public Configuration LocalConfiguration { get; set; }

        public DateTime NextDisplayChangeTime = DateTime.Now.AddMinutes(1.0);

        /// <summary>
        /// Are we connected?
        /// </summary>
        public bool IsConnected
        {
            get { return (bool)GetValue(IsConnectedProperty); }
            set { SetValue(IsConnectedProperty, value); }
        }
        public static readonly DependencyProperty IsConnectedProperty = DependencyProperty.Register("IsConnected", typeof(bool), typeof(MainWindow), new PropertyMetadata(false, _IsConnectedChanged));

        public Dictionary<string, VisualizationControl> SelectedPlcs = new Dictionary<string, VisualizationControl>();

        public List<VisualizationControl> StationVisuals = new List<VisualizationControl>();

        /// <summary>
        /// Log changes to the system log, as necessary :)
        /// </summary>
        /// <param name="d"></param>
        /// <param name="e"></param>
        private static void _IsConnectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var vm = d as MainWindow;
            var before = (bool)e.OldValue;
            var after = (bool)e.NewValue;
            if (before)
            {
                if (!after)
                {
                    vm.textboxLog.AppendLine("Disconnected!");
                }
            }
            else
            {
                if (after)
                {
                    vm.textboxLog.AppendLine("Connected!");
                }
            }
        }

        public MainWindow()
        {
            LocalConfiguration = App.Current.Configuration;
            CopyOfConfiguration = new Configuration();
            CopyOfConfiguration.CopyFrom(LocalConfiguration);

            NextDisplayChangeTime = DateTime.Now.AddSeconds(LocalConfiguration.DisplayChangePeriod);
            InitializeComponent();
        }

        void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var dirlist = System.IO.Directory.EnumerateDirectories(App.DataDirectory);
            foreach (var base_dir in dirlist)
            {
                try
                {
                    var name = System.IO.Path.GetFileName(base_dir);
                    var vc = new VisualizationControl(this, base_dir, name);
                    vc.TabItemHeader = new TextBlock() { Text=vc.DisplayTitle };
                    StationVisuals.Add(vc);

                    var ti = new TabItem();
                    ti.Header = vc.TabItemHeader;
                    ti.Content = vc;
                    tabcontrolMain.Items.Insert(0, ti);

                    SelectedPlcs.Add(name, vc);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, App.TitleError);
                }
            }

            // 6. Start the update timer.
            _Timer = new DispatcherTimer(TimeSpan.FromSeconds(1.0), DispatcherPriority.Normal, _Timer_Tick, Dispatcher);
            _Timer.IsEnabled = true;

            PostponeReconnect();
            // 5. Connect to the PLC, if possible.
            ConnectToPlc();

            tabcontrolMain.SelectedIndex = 0;
        }

        public void PostponeDisplayChange()
        {
            this.NextDisplayChangeTime = DateTime.Now.AddSeconds(this.LocalConfiguration.DisplayChangePeriod);
        }

        /// <summary>
        /// one-second timer for:
        /// 1. Blinking.
        /// 2. Timeouts.
        /// 3. Chart scrolls.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void _Timer_Tick(object sender, EventArgs e)
        {
            bool sound_a_beep = false;
            foreach (var sv in StationVisuals)
            {
                bool this_round = false;
                sv.TimerTick(ref this_round);
                Brush new_brush = this_round ? Brushes.Red : tabcontrolMain.Foreground;
                // optimized assignment :)
                if (sv.TabItemHeader.Foreground != new_brush)
                {
                    sv.TabItemHeader.Foreground = new_brush;
                }
                sound_a_beep = sound_a_beep || this_round;
            }

            if (sound_a_beep && App.Current.Configuration.IsAudibleAlarmEnabled)
            {
                System.Media.SystemSounds.Beep.Play();
            }

            // Is it time for a reconnect?
            var t1 = DateTime.Now;
            var plc_connection = PlcConnection; // avoid deep water.
            if (t1 > NextReconnectTime && plc_connection != null && plc_connection.IsConnected)
            {
                LogLine("Main", "Data receive timeout, reconnecting!");
                try
                {
                    ConnectToPlc();
                }
                catch (Exception ex)
                {
                    LogLine("Main", "Reconnect failed: " + ex.Message);
                }
            }

            // Is it time to change the display?
            if (t1 > this.NextDisplayChangeTime)
            {
                // 1. Postpone the inevitable.
                PostponeDisplayChange();

                // 2. Choose next display; assuming first ones are the stationvisuals.
                int count = StationVisuals.Count;
                int index = tabcontrolMain.SelectedIndex;
                if (index < count)
                {
                    VisualizationControl vc = tabcontrolMain.SelectedContent as VisualizationControl;
                    int subitem_index = vc.tabcontrolMain.SelectedIndex;
                    int subitem_count = 2;
                    // Over the edge in the subitem?
                    if (subitem_index + 1 >= subitem_count)
                    {
                        vc.tabcontrolMain.SelectedIndex = 0;
                        tabcontrolMain.SelectedIndex = index >= count ? 0 : ((index + 1) % count);
                    }
                    else
                    {
                        vc.tabcontrolMain.SelectedIndex = (subitem_index + 1) % subitem_count;
                    }
                }
            }
        }

        /// <summary>
        /// Open a connection to the PLC and return whether it succeeded.
        /// </summary>
        /// <param name="PlcConnection">Connection string in the form ip:port.</param>
        /// <returns>true iff connection succeeded, false otherwise.</returns>
        static bool _TestConnectToPlc(string PlcConnection)
        {
            try
            {
                var v = PlcConnection.Split(new char[] { ':' });
                using (var c = new TcpClient(v[0], int.Parse(v[1])))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, App.TitleError);
                return false;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 1. Signal the thread to close.
            var plc_connection = PlcConnection;
            PlcConnection = null;
            if (plc_connection != null)
            {
                try
                {
                    plc_connection.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, App.TitleError);
                }
            }

            e.Cancel = false;
        }

        #region Configuration

        /// <summary>
        /// Has the configuration been tested yes/no?
        /// </summary>
        public bool IsConfigurationTested
        {
            get { return (bool)GetValue(IsConfigurationTestedProperty); }
            set { SetValue(IsConfigurationTestedProperty, value); }
        }
        public static readonly DependencyProperty IsConfigurationTestedProperty =
            DependencyProperty.Register("IsConfigurationTested", typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

        /// <summary>
        /// Our new configuration, if any.
        /// </summary>
        public Configuration CopyOfConfiguration
        {
            get { return (Configuration)GetValue(CopyOfConfigurationProperty); }
            set { SetValue(CopyOfConfigurationProperty, value); }
        }
        public static readonly DependencyProperty CopyOfConfigurationProperty =
            DependencyProperty.Register("CopyOfConfiguration", typeof(Configuration), typeof(MainWindow), new PropertyMetadata(null));

        static void _SettingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var self = d as MainWindow;
            if (self!=null)
            {
                self.IsConfigurationTested = false;
            }
        }

        private void Button_Test_Configuration(object sender, RoutedEventArgs e)
        {
            // 1. Have to test the beep :)
            System.Media.SystemSounds.Beep.Play();
            // 2. Try to connect.
            if (_TestConnectToPlc(CopyOfConfiguration.ServerConnection))
            {
                IsConfigurationTested = true;
                MessageBox.Show("Settings are correct!", App.TitleOK);
            }
        }

        private void Button_Apply_Configuration(object sender, RoutedEventArgs e)
        {
            if (IsConfigurationTested)
            {
                try {
                    // 1. Store the configuration.
                    var ap = App.Current;
                    ap.Configuration.CopyFrom(CopyOfConfiguration);
                    ap.Store_Configuration();

                    // 2. Maybe we need faster response.
                    PostponeDisplayChange();

                    // 3. Use the configuration.
                    ConnectToPlc();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, App.TitleError);
                }
            }
            else
            {
                MessageBox.Show("Test the configuration first!", App.TitleError);
            }
        }
        #endregion Configuration

        #region PUBLIC INTERFACE
        // public delegate void LogLineHandler(string Text);

        public void LogLine(string SourceName, string Text)
        {
            this.textboxLog.AppendLine(SourceName + ":" + Text);
        }

        public ServerConnection PlcConnection;

        /// <summary>
        /// Time for the next connection.
        /// </summary>
        public DateTime NextReconnectTime = DateTime.Now;

        public void PostponeReconnect()
        {
            NextReconnectTime = DateTime.Now.AddSeconds(LocalConfiguration.PlcDataTimeout);
        }

        /// <summary>
        /// Connect to the PLC using current settings.
        /// </summary>
        public void ConnectToPlc()
        {
            PostponeReconnect();

            var v = LocalConfiguration.ServerConnection.Split(new char[] { ':' });
            var connect_to = new IPEndPoint(IPAddress.Parse(v[0]), int.Parse(v[1]));
            var current_connection = PlcConnection;
            if (current_connection != null)
            {
                current_connection.Close();
                this.PlcConnection = null;
            }
            this.PlcConnection = new ServerConnection(
                this, connect_to,
                LocalConfiguration.ServerUsername,
                LocalConfiguration.ServerPassword);
        }
        #endregion // PUBLIC INTERFACE

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            PostponeDisplayChange();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            PostponeDisplayChange();
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            PostponeDisplayChange();
        }
    }
}
