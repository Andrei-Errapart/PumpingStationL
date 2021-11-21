using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Windows;
using System.Xml;
using System.Windows.Controls;

namespace ControlPanel
{
    /// <summary>
    /// Configuration as can be found in the "ControlPanel.ini" file.
    /// </summary>
    public class Configuration
    {
        /// <summary>
        /// Server connection string.
        /// </summary>
        [DefaultValue("127.0.0.5:1503")]
        public string ServerConnection { get; set; }

        [DefaultValue("")]
        public string ServerUsername { get; set; }

        [DefaultValue("")]
        public string ServerPassword { get; set; }

        /// <summary>
        /// Enable audible alarm.
        /// </summary>
        [DefaultValue(true)]
        public bool IsAudibleAlarmEnabled { get; set; }

        /// <summary>
        /// PLC data timeout, seconds.
        /// </summary>
        [DefaultValue(120)]
        public int PlcDataTimeout { get; set; }

        /// <summary>
        /// Are we running a debug version?
        /// </summary>
        [DefaultValue(false)]
        public bool IsDebug { get; set; }

        /// <summary>
        /// Timespan values for the timespan picker, in seconds, separate by semicolons.
        /// </summary>
        [DefaultValue("30;60;120;300;600;1800;3600;7200;14400;21600;43200;86400;172800;259200;604800")]
        public string TimeSpanPickerTimes { get; set; }

        /// <summary>
        /// Default timespan picker value.
        /// </summary>
        [DefaultValue(300)]
        public int TimeSpanPickerDefault { get; set; }

        /// <summary>
        /// Timespan values for the timespan picker, in seconds, separate by semicolons.
        /// </summary>
        [DefaultValue("3600;7200;14400;21600;43200;86400;172800;259200;604800;2419200;2505600;2592000;2678400")]
        public string HistoryTimeSpanPickerTimes { get; set; }

        /// <summary>
        /// Default timespan picker value.
        /// </summary>
        [DefaultValue(86400)]
        public int HistoryTimeSpanPickerDefault { get; set; }

        /// <summary>Period of the display change, in seconds.</summary>
        [DefaultValue(300)]
        public int DisplayChangePeriod { get; set; }

        /// <summary>
        /// Copy configuration over from the existing one.
        /// </summary>
        /// <param name="src"></param>
        public void CopyFrom(Configuration src)
        {
            this.ServerConnection = src.ServerConnection;
            this.ServerUsername = src.ServerUsername;
            this.ServerPassword = src.ServerPassword;
            this.IsAudibleAlarmEnabled = src.IsAudibleAlarmEnabled;
            this.IsDebug = src.IsDebug;
            this.TimeSpanPickerTimes = src.TimeSpanPickerTimes;
            this.TimeSpanPickerDefault = src.TimeSpanPickerDefault;
            this.DisplayChangePeriod = src.DisplayChangePeriod;
        }
    } // class Configuration
} // namespace ControlPanel
