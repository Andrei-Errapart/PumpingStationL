using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

using PlcCommunication;

namespace ControlPanel
{
    /// <summary>
    /// Top-level group information.
    /// </summary>
    public partial class SignalGroupToplevel : UserControl
    {
        /// <summary>
        /// Name of the group.
        /// </summary>
        public string GroupName { get; set; }

        public ObservableCollection<IOSignal> DisplaySignals
        {
            get { return (ObservableCollection<IOSignal>)GetValue(DisplaySignalsProperty); }
            set { SetValue(DisplaySignalsProperty, value); }
        }
        public static readonly DependencyProperty DisplaySignalsProperty =
            DependencyProperty.Register("DisplaySignals", typeof(ObservableCollection<IOSignal>), typeof(SignalGroupToplevel), new PropertyMetadata(null));

        public SignalGroupToplevel()
        {
            DisplaySignals = new ObservableCollection<IOSignal>();
            InitializeComponent();
        }
    }
}
