using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ControlPanel
{
    /// <summary>
    /// Interaction logic for TimespanPanelControl.xaml
    /// </summary>
    public partial class TimespanPanelControl : UserControl
    {
        public enum TIMESPAN
        {
            RUNNING,
            START_FROM,
        };

        /// <summary>Type of timespan, if one wants to know.</summary>
        public TIMESPAN TimespanType
        {
            get { return (TIMESPAN)GetValue(TimespanTypeProperty); }
            set { SetValue(TimespanTypeProperty, value); }
        }
        public static readonly DependencyProperty TimespanTypeProperty =
            DependencyProperty.Register("TimespanType", typeof(TIMESPAN), typeof(TimespanPanelControl), new PropertyMetadata(TIMESPAN.RUNNING));

        /// <summary>Datetime this thing starts from.</summary>
        public DateTime FromDatetime
        {
            get { return (DateTime)GetValue(FromDatetimeProperty); }
            set { SetValue(FromDatetimeProperty, value); }
        }
        public static readonly DependencyProperty FromDatetimeProperty =
            DependencyProperty.Register("FromDatetime", typeof(DateTime), typeof(TimespanPanelControl), new PropertyMetadata(DateTime.Now));

        /// <summary>Selected timespan.</summary>
        public TimeSpan TimeSpan
        {
            get { return (TimeSpan)GetValue(TimeSpanProperty); }
            set { SetValue(TimeSpanProperty, value); }
        }
        public static readonly DependencyProperty TimeSpanProperty =
            DependencyProperty.Register("TimeSpan", typeof(TimeSpan), typeof(TimespanPanelControl), new PropertyMetadata(TimeSpan.Zero));

        
        /// <summary>
        /// Event called when anything changes in the timespan selection.
        /// </summary>
        public event RoutedEventHandler OnTimespanChanged
        {
            add { base.AddHandler(TimespanChangedEvent, value); }
            remove { base.RemoveHandler(TimespanChangedEvent, value); }
        }
        public static readonly RoutedEvent TimespanChangedEvent =
            EventManager.RegisterRoutedEvent("OnTimespanChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TimespanPanelControl));


        /// <summary>
        /// Semicolon-separated list of timespans.
        /// </summary>
        public string Timespans
        {
            get { return (string)GetValue(TimespansProperty); }
            set { SetValue(TimespansProperty, value); }
        }
        public static readonly DependencyProperty TimespansProperty =
            DependencyProperty.Register("Timespans", typeof(string), typeof(TimespanPanelControl), new PropertyMetadata("30;60;120;300;600;1800;3600;7200;14400;21600;43200;86400;86400;172800;259200;604800"));

        /// <summary>
        /// Default timespan, in seconds.
        /// </summary>
        public int DefaultTimespan
        {
            get { return (int)GetValue(DefaultTimespanProperty); }
            set { SetValue(DefaultTimespanProperty, value); }
        }
        public static readonly DependencyProperty DefaultTimespanProperty =
            DependencyProperty.Register("DefaultTimespan", typeof(int), typeof(TimespanPanelControl), new PropertyMetadata(3600));


        public Tuple<DateTime, DateTime> GetTimespan()
        {
            switch (TimespanType)
            {
                case TIMESPAN.RUNNING:
                    {
                        var now = DateTime.Now;
                        return new Tuple<DateTime, DateTime>(now.Subtract(TimeSpan), now);
                    }
                case TIMESPAN.START_FROM:
                    return new Tuple<DateTime, DateTime>(FromDatetime, FromDatetime.Add(TimeSpan));
            }
            throw new ApplicationException("TimespanPanelControl: shouldn't get here!");
        }

        public TimespanPanelControl()
        {
            InitializeComponent();
        }

        bool _FirstLoad = true;
        void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_FirstLoad)
            {
                var new_from = DateTime.Now.Subtract(timespanpickerPeriod.SelectedTimeSpan);
                FromDatetime = new_from;
                TimeSpan = System.TimeSpan.FromSeconds(timespanpickerPeriod.DefaultTimeSpan);
                datetimepickerFrom.Value = new_from;

                _FirstLoad = false;
            }
        }

        void timespanpickerPeriod_OnTimeSpanChanged(object sender, HandyBox.TimeSpanPicker.TimeSpanChangedEventArgs e)
        {
            // TODO: is the check really needed?
            if (TimeSpan != timespanpickerPeriod.SelectedTimeSpan)
            {
                TimeSpan = timespanpickerPeriod.SelectedTimeSpan;
                // Re-bubble the event.
                _RaiseTimespanChanged();
            }
        }

        void datetimepickerFrom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // TODO: is the check really needed?
            if (FromDatetime != datetimepickerFrom.Value.Value)
            {
                FromDatetime = datetimepickerFrom.Value.Value;
                _RaiseTimespanChanged();
            }
        }

        void radiobuttonRunning_Click(object sender, RoutedEventArgs e)
        {
            // TODO: is the check really needed?
            if (TimespanType != TIMESPAN.RUNNING)
            {
                TimespanType = TIMESPAN.RUNNING;
                _RaiseTimespanChanged();
            }
        }

        void radiobuttonFrom_Click(object sender, RoutedEventArgs e)
        {
            // TODO: is the check really needed?
            if (TimespanType != TIMESPAN.START_FROM)
            {
                TimespanType = TIMESPAN.START_FROM;
                _RaiseTimespanChanged();
            }
        }

        void _RaiseTimespanChanged()
        {
            var args = new RoutedEventArgs(TimespanChangedEvent, this);
            base.RaiseEvent(args);
        }
    }
}
