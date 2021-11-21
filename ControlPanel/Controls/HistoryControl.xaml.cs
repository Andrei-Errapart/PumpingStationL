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

using Microsoft.Win32;

using PlcCommunication;

namespace ControlPanel
{
    /// <summary>
    /// Interaction logic for HistoryControl.xaml
    /// </summary>
    public partial class HistoryControl : UserControl
    {
        public class EventLine
        {
            /// <summary>Timestamp in ticks. There can be several lines with equal timestamps.</summary>
            public long Timestamp;
            /// <summary>Signals of the given line. Lines with equal timestamps share signals.</summary>
            public Tuple<bool, int>[] Values;
            /// <summary>Timestamp to be displayed.</summary>
            public string DisplayTimestamp { get; set; }
            public string Device { get; set; }
            public string SignalName { get; set; }
            /// <summary>Name of the signal to be displayed.</summary>
            public string SignalDescription { get; set; }
            /// <summary>Message conveyed: value of the signal, most probably.</summary>
            public string Message { get; set; }

            public static EventLine Create(
                IOSignal Signal,
                SignalValuesType Values,
                Tuple<bool,int>[] ExtractedValues,
                string Message)
            {
                var r = new EventLine()
                {
                    Timestamp = Values.Timestamp,
                    Values = ExtractedValues,
                    DisplayTimestamp = (new DateTime(Values.Timestamp)).ToString(App.TimestampFormatString),
                    Device = _StringOfDevice(Signal.Device),
                    SignalName = Signal.Name=="" ? Signal.DisplayDevicePin : Signal.Name,
                    SignalDescription = Signal.Description,
                    Message = Message,
                };
                return r;
            }

            public static EventLine CreateConnectivityEvent(
                    IOSignal Signal,
                    SignalValuesType Values,
                    Tuple<bool,int>[] ExtractedValues,
                    string Message)
            {
                var r = new EventLine()
                {
                    Timestamp = Values.Timestamp,
                    Values = ExtractedValues,
                    DisplayTimestamp = (new DateTime(Values.Timestamp)).ToString(App.TimestampFormatString),
                    Device = _StringOfDevice(Signal.Device),
                    SignalName = "",
                    SignalDescription = "",
                    Message = Message,
                };
                return r;
            }

            /// <summary>Dechipher the name of the device correctly.</summary>
            /// <param name="Device">Name of the device.</param>
            /// <returns>CPU for device "0", "MEM" for device "" (variable), and R+Device otherwise.</returns>
            static string _StringOfDevice(string Device)
            {
                return Device == "" ? "MEM" : (Device == "0" ? "CPU" : ("R" + Device));
            }
        }

        /// <summary>Events, if any.</summary>
        public ObservableCollection<EventLine> EventLines
        {
            get { return (ObservableCollection<EventLine>)GetValue(EventLinesProperty); }
            set { SetValue(EventLinesProperty, value); }
        }
        public static readonly DependencyProperty EventLinesProperty =
            DependencyProperty.Register("EventLines", typeof(ObservableCollection<EventLine>), typeof(HistoryControl), new PropertyMetadata(null));

        /// <summary>Viewmodel of the Control Panel.</summary>
        VisualizationControl _ViewModel { get { return DataContext as VisualizationControl; } }
        /// <summary></summary>
        bool _FirstLoad = true;

        /// <summary></summary>
        Tuple<bool, int>[] _ResultsOK = null;

        public HistoryControl()
        {
            // but no events, yet.
            EventLines = new ObservableCollection<EventLine>();
            InitializeComponent();
        }

        void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Can't do ReloadHistory() here, the LocalSignalDB has not been set (yet).
            _FirstLoad = false;
        }

        public void ReloadHistory()
        {
#if (false)
            // TODO: optimize the history.
            var interval = timespanpanel.GetTimespan();
            var results = _ViewModel.LocalSignalDB.FetchByTimeRangeIncluding(interval.Item1, interval.Item2);
            var timespan_end = interval.Item2.Ticks;
            int nresults = results.Count;

            // FIXME: do what?
            EventLines.Clear();
            var signals = _ViewModel.PhysicalSignals;
            if (results.Count > 0 && signals.Count>0)
            {
                var nsignals = signals.Count;
                var results0 = new Tuple<bool, int>[nsignals];
                var results1 = new Tuple<bool, int>[nsignals];
                var connectivity_changes = new Dictionary<string, bool>();
                _ResultsOK = new Tuple<bool, int>[nsignals];
                _ViewModel.PlcConnection.ExtractSignals(results0, results[0].Values);
                Array.Copy(results0, _ResultsOK, nsignals);

                for (int result_index = 1; result_index < nresults; ++result_index)
                {
                    var ri = results[result_index];
                    if (ri.Timestamp > timespan_end)
                    {
                        break;
                    }
                    _ProcessLine(ri, results0, results1, connectivity_changes);
                }
            }
#endif
        }

        /// <summary>
        /// Append signal values to the present history log, if needed.
        /// </summary>
        /// <param name="SignalValues"></param>
        /// <param name="Timestamp"></param>
        public void AppendSignalValues(SignalValuesType SignalValues)
        {
            // 1. Append to the end.
            if (!_FirstLoad && timespanpanel.TimespanType == TimespanPanelControl.TIMESPAN.RUNNING && _ResultsOK!=null)
            {
                // Remove exess at the beginning, if needed.
                _RemoveExcessAtBeginning();

                // Append first
                var signals = _ViewModel.Signals;
                var nsignals = signals.Count;
                var results0 = EventLines.Count==0 ? _ResultsOK : EventLines.First().Values;
                var results1 = new Tuple<bool, int>[nsignals];
                var connectivity_changes = new Dictionary<string, bool>();
                _ProcessLine(SignalValues, results0, results1, connectivity_changes);
            }
        }

        public void TimerTick()
        {
            if (!_FirstLoad && timespanpanel.TimespanType == TimespanPanelControl.TIMESPAN.RUNNING && _ResultsOK != null)
            {
                _RemoveExcessAtBeginning();
            }
        }

        public void Print(PrintDialog pd)
        {
            // 1. Create the flow document.
            // TODO: use xaml template for this one!!!
            var interval = timespanpanel.GetTimespan();
            int nlines = EventLines.Count;
            var fd = new FlowDocument();
            fd.FontSize = 10.0;
            var ph_header = new Paragraph();
            ph_header.Inlines.Add(new Run("Algus:\t" + interval.Item1.ToString(App.TimestampFormatString)));
            ph_header.Inlines.Add(new LineBreak());
            ph_header.Inlines.Add(new Run("Lõpp:\t" + interval.Item2.ToString(App.TimestampFormatString)));
            ph_header.Inlines.Add(new LineBreak());
            ph_header.Inlines.Add(new Run("Kokku:\t" + nlines + " sündmust."));
            fd.Blocks.Add(ph_header);

            var table = new Table();
            var thickness = 0.3;
            table.CellSpacing = 0.0;
            var tleft = new Thickness(thickness, thickness, thickness, thickness);
            var tcenter = new Thickness(thickness, thickness, thickness, thickness);
            var tright = new Thickness(thickness, thickness, thickness, thickness);
            // 5 rows.
            table.Columns.Add(new TableColumn() { Width = new GridLength(1.5, GridUnitType.Star) });
            table.Columns.Add(new TableColumn() { Width = new GridLength(0.6, GridUnitType.Star) });
            table.Columns.Add(new TableColumn() { Width = new GridLength(2.0, GridUnitType.Star) });
            table.Columns.Add(new TableColumn() { Width = new GridLength(3.0, GridUnitType.Star) });
            table.Columns.Add(new TableColumn() { Width = new GridLength(3.0, GridUnitType.Star) });


            // Header.
            var tgr = new TableRowGroup();
            var header = new TableRow();
            var bbrush = Brushes.Gray;
            var cpadding = new Thickness(1.5, 1.5, 1.5, 1.5);
            header.Cells.Add(new TableCell(new Paragraph(new Run("Kellaaeg")) { FontWeight = FontWeights.Bold }) { BorderThickness = tleft, BorderBrush = bbrush, Padding = cpadding });
            header.Cells.Add(new TableCell(new Paragraph(new Run("Seade")) { FontWeight = FontWeights.Bold }) { BorderThickness = tcenter, BorderBrush = bbrush, Padding = cpadding });
            header.Cells.Add(new TableCell(new Paragraph(new Run("Signaal")) { FontWeight = FontWeights.Bold }) { BorderThickness = tcenter, BorderBrush = bbrush, Padding = cpadding });
            header.Cells.Add(new TableCell(new Paragraph(new Run("Kirjeldus")) { FontWeight = FontWeights.Bold }) { BorderThickness = tcenter, BorderBrush = bbrush, Padding = cpadding });
            header.Cells.Add(new TableCell(new Paragraph(new Run("Teade")) { FontWeight = FontWeights.Bold }) { BorderThickness = tright, BorderBrush = bbrush, Padding = cpadding });
            tgr.Rows.Add(header);

            // Content!
            for (int i = nlines - 1; i >= 0; --i)
            {
                var el = EventLines[i];
                var content = new TableRow();
                // OutputLine(el.DisplayTimestamp + "\t" + el.Device + "\t" + el.SignalName + "\t" + el.SignalDescription + "\t" + el.Message);
                content.Cells.Add(new TableCell(new Paragraph(new Run(el.DisplayTimestamp))) { BorderThickness = tleft, BorderBrush = bbrush, Padding = cpadding });
                content.Cells.Add(new TableCell(new Paragraph(new Run(el.Device))) { BorderThickness = tcenter, BorderBrush = bbrush, Padding = cpadding });
                content.Cells.Add(new TableCell(new Paragraph(new Run(el.SignalName))) { BorderThickness = tcenter, BorderBrush = bbrush, Padding = cpadding });
                content.Cells.Add(new TableCell(new Paragraph(new Run(el.SignalDescription))) { BorderThickness = tcenter, BorderBrush = bbrush, Padding = cpadding });
                content.Cells.Add(new TableCell(new Paragraph(new Run(el.Message))) { BorderThickness = tright, BorderBrush = bbrush, Padding = cpadding });
                tgr.Rows.Add(content);
            }
            table.RowGroups.Add(tgr);
            fd.Blocks.Add(table);

            // 2. Print the flow document!
            fd.PageHeight = pd.PrintableAreaHeight;
            fd.PageWidth = pd.PrintableAreaWidth;
            fd.PagePadding = new Thickness(50);
            fd.ColumnGap = 0;
            fd.ColumnWidth = pd.PrintableAreaWidth;

            IDocumentPaginatorSource dps = fd;
            pd.PrintDocument(dps.DocumentPaginator, "Ajalugu");            
        }

        SaveFileDialog _SaveFileDialog = new SaveFileDialog()
        {
            Filter = "Tab-separated text files (*.txt)|*.txt|All files (*.*)|*.*",
        };
        void ButtonSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_SaveFileDialog.ShowDialog() == true)
                {
                    var filename = _SaveFileDialog.FileName;
                    // 3. Query the stuff!
                    using (var tw = new System.IO.StreamWriter(filename))
                    {
                        _ProduceOutput((s) => tw.WriteLine(s));
                    }

                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, App.TitleError);
            }
        }

        void ButtonCopy_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            _ProduceOutput((s) => sb.AppendLine(s));
            System.Windows.Clipboard.SetText(sb.ToString());
        }

        void TimespanPanelControl_OnTimespanChanged(object sender, RoutedEventArgs e)
        {
#if (false)
            if (_ViewModel.LocalSignalDB != null)
            {
                ReloadHistory();
            }
#endif
        }

        void _ProduceOutput(Action<string> OutputLine)
        {
            var interval = timespanpanel.GetTimespan();
            int nlines = EventLines.Count;
            OutputLine("Algus:\t" + interval.Item1.ToString(App.TimestampFormatString));
            OutputLine("Lõpp:\t" + interval.Item2.ToString(App.TimestampFormatString));
            OutputLine("Kokku:\t" + nlines + " sündmust.");

            for (int i = nlines - 1; i >= 0; --i)
            {
                var el = EventLines[i];
                OutputLine(el.DisplayTimestamp + "\t" + el.Device + "\t" + el.SignalName + "\t" + el.SignalDescription + "\t" + el.Message);
            }
        }

        void _ProcessLine(SignalValuesType StoredSignals,
            Tuple<bool, int>[] ResultsPrevious,
            Tuple<bool, int>[] ResultsCurrent,
            Dictionary<string, bool> ConnectivityChanges)
        {
            var signals = _ViewModel.PhysicalSignals;
            int nsignals = signals.Count;
            _ViewModel.PhysicalSignals.ExtractSignals(ResultsCurrent, StoredSignals.Values);
            // 1. Connectivity changes go first!
            ConnectivityChanges.Clear();
            for (int signal_index = 0; signal_index < nsignals; ++signal_index)
            {
                var c0 = ResultsPrevious[signal_index].Item1;
                var c1 = ResultsCurrent[signal_index].Item1;
                if (c0 != c1)
                {
                    var signal = signals[signal_index];
                    var device = signal.Device;
                    if (!ConnectivityChanges.ContainsKey(device))
                    {
                        ConnectivityChanges.Add(device, c1);
                        var el = EventLine.CreateConnectivityEvent(signal, StoredSignals, ResultsCurrent,
                            c1 ? "Ühendus taastus!" : "Ühendus kadunud!");
                        EventLines.Insert(0, el);
                    }
                }
            }

            for (int signal_index = 0; signal_index < nsignals; ++signal_index)
            {
                var si = signals[signal_index];
                // Note: if one is not interested in variables, it can be check for with si.IsVariable.
                var bit0 = ResultsPrevious[signal_index];
                var bit1 = ResultsCurrent[signal_index];
                if (bit1.Item1)
                {
                    // Found something!
                    // Gotta compare with the last known good value.
                    var bitx = _ResultsOK[signal_index];
                    if (bit1.Item2 != bitx.Item2)
                    {
                        var el = EventLine.Create(si, StoredSignals, ResultsCurrent, bit1.Item2 == 0 ? si.Text0 : si.Text1);
                        EventLines.Insert(0, el);
                    }
                }
            }

            // Ok, have it!
            Array.Copy(ResultsCurrent, ResultsPrevious, nsignals);

            // Copy the connected stuff bits.
            for (int signal_index = 0; signal_index < nsignals; ++signal_index)
            {
                var bit = ResultsCurrent[signal_index];
                if (bit.Item1)
                {
                    _ResultsOK[signal_index] = bit;
                }
            }
        }

        void _RemoveExcessAtBeginning()
        {
            var timestamp_begin = timespanpanel.GetTimespan().Item1.Ticks;
            while (EventLines.Count > 0 && EventLines.Last().Timestamp < timestamp_begin)
            {
                EventLines.RemoveAt(EventLines.Count-1);
            }
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ReloadHistory();
        }
    }
}
