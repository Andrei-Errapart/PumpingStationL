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
    /// Interaction logic for MuteControl.xaml
    /// </summary>
    public partial class MuteButton : UserControl
    {
        public bool IsMuted
        {
            get { return (bool)GetValue(IsMutedProperty); }
            set { SetValue(IsMutedProperty, value); }
        }
        public static readonly DependencyProperty IsMutedProperty =
            DependencyProperty.Register("IsMuted", typeof(bool), typeof(MuteButton), new PropertyMetadata(false));

        public MuteButton()
        {
            InitializeComponent();

            DrawingLayerMuteOn.Opacity = 0.0;
            DrawingLayerMuteOff.Opacity = 1.0;
        }

        bool? _AlarmState = null;
        public void SetAlarmState(bool NewAlarmState)
        {
            if (NewAlarmState==true && (_AlarmState==null || _AlarmState==false))
            {
                // Alarm On.
                IsMuted = false;
                Visibility = System.Windows.Visibility.Visible;
                DrawingLayerMuteOn.Opacity = 0.0;
                DrawingLayerMuteOff.Opacity = 1.0;
            }
            else if (NewAlarmState == false && (_AlarmState==null || _AlarmState == true))
            {
                // Alarm off
                IsMuted = false;
                Visibility = System.Windows.Visibility.Collapsed;
            }
            _AlarmState = NewAlarmState;
        }

        void ButtonMute_Click(object sender, RoutedEventArgs e)
        {
            IsMuted = !IsMuted;
            if (IsMuted)
            {
                DrawingLayerMuteOn.Opacity = 1.0;
                DrawingLayerMuteOff.Opacity = 0.0;
            }
            else
            {
                DrawingLayerMuteOn.Opacity = 0.0;
                DrawingLayerMuteOff.Opacity = 1.0;
            }
        }
    }
}
