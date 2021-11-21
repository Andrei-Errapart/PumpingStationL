using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;

using System.ComponentModel;

using CSUtils;
using System.IO;

namespace ControlPanel
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public const string Title = "Pumpla Juhtpaneel";
        public const string TitleOK = "OK - " + Title;
        public const string TitleError = "Viga - " + Title;
        public static string ApplicationDataDirectory = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Exe-Project"), "PumpingStationL");
        public static string DataDirectory = ".";
        public static string ConfigurationFilename = Path.Combine(DataDirectory, "ControlPanel.ini");
        public static string ApplicationDataConfigurationFilename = Path.Combine(DataDirectory, "ControlPanel.ini");
        public static string SchemeProgramFilename = "ControlPanel-Scheme.txt";

        public const string TimestampFormatString = "yyyy'-'MM'-'dd' 'HH':'mm':'ss";

        public static new App Current
        {
            get { return Application.Current as App; }
        }

        public Configuration Configuration;
        FileConfig _FileConfig;
        const string _SectionName = "ControlPanel";

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 1. Process the command line.
            bool is_in_debug_mode = true;
            foreach (var s in e.Args)
            {
                if (s == "--debug")
                {
                    is_in_debug_mode = true;
                }
            }

            // 2. Create application data directory, if it doesn't exist.
            if (!Directory.Exists(DataDirectory))
            {
                try
                {
                    Directory.CreateDirectory(DataDirectory);
                }
                catch (Exception)
                {
                    // FIXME: what to do?
                }
            }

            // 3. Ensure that there is a configuration, at least some sort.
            // 3a. Try application data directory first.
            _FileConfig = new FileConfig(ApplicationDataConfigurationFilename);
            try
            {
                // avoid exceptions.
                if (File.Exists(ApplicationDataConfigurationFilename))
                {
                    _FileConfig.Load();
                    Configuration = StringDictionary.ToObject<Configuration>(_FileConfig[_SectionName]);
                }
            }
            catch (Exception)
            {
                // pass.
            }

            // 3b. Try local directory, then.
            if (Configuration == null)
            {
                try
                {
                    var cfg = new FileConfig(ConfigurationFilename);
                    cfg.Load();
                    Configuration = StringDictionary.ToObject<Configuration>(cfg[_SectionName]);
                }
                catch (Exception)
                {
                }
            }

            // 3c. Give up.
            if (Configuration == null)
            {
                Configuration = StringDictionary.CreateWithDefaults<Configuration>();
            }

            Configuration.IsDebug = is_in_debug_mode;
        }

        public void Store_Configuration()
        {
            if (_FileConfig == null)
            {
                _FileConfig = new FileConfig(ApplicationDataConfigurationFilename);
            }

            _FileConfig[_SectionName] = StringDictionary.Create(Configuration);
            _FileConfig.Store();
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            CSUtils.ErrorDialog.Show(e.Exception);
        }
    }
}
