using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace ControlPanel
{
    public class WorkingTimeCell : DependencyObject
    {
        /// <summary>Name (of the signal to watch).</summary>
        public string DisplayName { get; set; }

        /// <summary>Time spent working on this stuff :)</summary>
        public TimeSpan WorkingTime
        {
            get { return (TimeSpan)GetValue(WorkingTimeProperty); }
            set { SetValue(WorkingTimeProperty, value); }
        }
        public static readonly DependencyProperty WorkingTimeProperty =
            DependencyProperty.Register("WorkingTime", typeof(TimeSpan), typeof(WorkingTimeCell), new PropertyMetadata(TimeSpan.Zero));

        /// <summary>Time of the next overhaul, if any.</summary>
        public TimeSpan OverhaulTime
        {
            get { return (TimeSpan)GetValue(OverhaulTimeProperty); }
            set { SetValue(OverhaulTimeProperty, value); }
        }
        public static readonly DependencyProperty OverhaulTimeProperty =
            DependencyProperty.Register("OverhaulTime", typeof(TimeSpan), typeof(WorkingTimeCell), new UIPropertyMetadata(TimeSpan.Zero));

        #region BEHIND THE SCENES
        /// <summary>Last known value of the signal.</summary>
        public Tuple<bool, int> LastKnownValue;
        /// <summary>Index into the signals.</summary>
        public int SignalIndex;
        /// <summary>Working seconds, if any.</summary>
        public long WorkingTicks;
        #endregion // BEHIND THE SCENES.
    } // class WorkHours


}
