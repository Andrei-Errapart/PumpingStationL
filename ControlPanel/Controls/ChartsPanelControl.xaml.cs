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
using HandyBox;
using YY;
using PlcCommunication;

namespace ControlPanel
{
    /// <summary>
    /// Interaction logic for ChartsPanel.xaml
    /// </summary>
    public partial class ChartsPanelControl : UserControl
    {
        public class BrushAndName
        {
            public Brush Brush { get; set; }
            public string Name { get; set; }
            public BrushAndName()
            {
            }
            public BrushAndName(Brush brush, string name)
            {
                this.Brush = brush;
                this.Name = name;
            }
        }

        public class SelectedSignal
        {
            /// <summary>Personal brush collection for every signal. How cool is that? :)</summary>
            public ObservableCollection<BrushAndName> BrushSelection { get; set; }
            public IOSignal Signal { get; set; }
            public BrushAndName Brush { get; set; }
            public bool IsBrushAvailable
            {
                get
                {
                    var ios = Signal;
                    if (ios == null)
                    {
                        return false;
                    }
                    var cs = ios as ComputedSignal;
                    return cs != null || (ios.BitCount > 1);
                }
            }
        } // class SelectedSignal

        enum DRAWING_STEP
        {
            NONE,
            SEND_DB_QUERY,
        };

        /// <summary>Selected signals.</summary>
        public ObservableCollection<SelectedSignal> SelectedSignals { get; set; }

        /// <summary>All signals.</summary>
        public ObservableCollection<IOSignal> AllSignals { get; set; }

        /// <summary>Viewmodel of the Control Panel.</summary>
        VisualizationControl _ViewModel { get { return DataContext as VisualizationControl; } }
        public bool IsRunningSelected { get { return timespanpanel.radiobuttonRunning.IsChecked == true; } }
        public TimeSpan TimeSpan { get { return timespanpanel.timespanpickerPeriod.SelectedTimeSpan; } }

        /// <summary>Is it first load?</summary>
        bool _FirstLoad = true;
        /// <summary>Indices into _ViewModel.Signals</summary>
        List<int> ChartSignalIndices = new List<int>();

        /// <summary>Timestamped clone of _ViewModel.ComputedSignals</summary>
        List<IOSignal> TS_Signals = new List<IOSignal>();
        /// <summary>Physical signals of TS_Signals</summary>
        List<IOSignal> TS_PhysicalSignals = new List<IOSignal>();
        /// <summary>Computed signals of TS_Signals</summary>
        List<ComputedSignal> TS_ComputedSignals = new List<ComputedSignal>();
        /// <summary>Add this to the PLC times to obtain correct time.</summary>
        TimeSpan _PLC_Offset = TimeSpan.Zero;

        Tuple<bool, int>[] _BufferBits = null;
        Tuple<bool, int>[] _GetBufferBits()
        {
            if (_BufferBits == null || _BufferBits.Length != _ViewModel.PhysicalSignals.Count)
            {
                _BufferBits = new Tuple<bool, int>[_ViewModel.PhysicalSignals.Count];
            }
            return _BufferBits;
        }
        // Inclusing tail id of the chart. NB! Update them whenever you update the chart.
        int _ChartTailId = 0;
        // Exclusive head id of the chart. NB! Update them whenever you update the chart.
        int _ChartHeadId = 0;


        /// <summary>Every</summary>
        /// <returns></returns>
        static ObservableCollection<BrushAndName> CreateBrushSelection()
        {
            var r = new ObservableCollection<BrushAndName>();
            r.Add(new BrushAndName(Brushes.Black, "Must"));
            r.Add(new BrushAndName(Brushes.Blue, "Sinine"));
            r.Add(new BrushAndName(Brushes.Red, "Punane"));
            r.Add(new BrushAndName(Brushes.Green, "Roheline"));
            r.Add(new BrushAndName(Brushes.Gray, "Hall"));
            // r.Add(new BrushAndName(Brushes.Yellow, "Kollane"));
            r.Add(new BrushAndName(Brushes.Orange, "Oranzh"));
            r.Add(new BrushAndName(Brushes.Violet, "Lilla"));
            r.Add(new BrushAndName(Brushes.Cyan, "Tsüaniin"));
            // r.Add(new BrushAndName(Brushes.Pink, "Roosa"));
            r.Add(new BrushAndName(Brushes.Brown, "Pruun"));
            return r;
        }

        static ObservableCollection<BrushAndName> BrushSelection = CreateBrushSelection();

        delegate T CreateFunction<T>();

        static List<T> _NewList<T>(int Size) where T : struct
        {
            var r = new List<T>(Size);
            for (int i = 0; i < Size; ++i)
            {
                r.Add(new T());
            }
            return r;
        }

        // ==================================================================
        static void _ResizeList<T>(IList<T> a, int NewSize, CreateFunction<T> NewFunc)
        {
            int ndiff = a.Count - NewSize;
            if (ndiff > 0)
            {
                // too big
                for (int i = 0; i < ndiff; ++i)
                {
                    a.RemoveAt(a.Count - 1);
                }
            }
            else if (ndiff < 0)
            {
                // Too small
                for (int i = ndiff; i < 0; ++i)
                {
                    a.Add(NewFunc());
                }
            }
        }

        // ==================================================================
        void _ProcessValidSignalValues(
            IList<SignalValuesType> SignalValues,
            DateTime TimestampBegin,
            DateTime TimestampEnd)
        {
            int ncols = SignalValues.Count;

            // 1. Prepare xy arrays.
            _ResizeList(plcchart.XValues as IList<Tuple<Int64, bool>>, ncols, () => new Tuple<Int64, bool>(0, false));
            foreach (var lg in plcchart.LineGroups)
            {
                foreach (var ln in lg.Lines)
                {
                    _ResizeList(ln.Points as IList<double>, ncols, () => 0.0);
                }
            }

            // for every column
            var xvalues = plcchart.XValues as IList<Tuple<Int64,bool>>;
            for (int i = 0; i < ncols; ++i)
            {
                _UpdateSignalCopiesAndSetPlcChartColumns(i, _GetBufferBits(), SignalValues[i]);
            }

            // 3. Let the bitchart do the hard work! :D
            plcchart.TimeBegin = TimestampBegin;
            plcchart.TimeEnd = TimestampEnd;

            // 4. Id-s.
            if (SignalValues.Count > 0)
            {
                _ChartTailId = SignalValues.First().Id;
                _ChartHeadId = SignalValues.Last().Id + 1;
            }
            else
            {
                _ChartTailId = 0;
                _ChartHeadId = 0;
            }

            // 5. PlcChart will work now!
            plcchart.Redraw();
        }

        // ==================================================================
        void _AddSignal(IOSignal ios)
        {
            var bs = CreateBrushSelection();
            var new_ss = new SelectedSignal() { Signal = ios, BrushSelection=bs };
            if (new_ss.IsBrushAvailable)
            {
                // Avoid insertion of duplicates.
                var iset = new HashSet<int>();
                for (int index = 0; index < bs.Count; ++index)
                {
                    iset.Add(index);
                }
                foreach (var x in SelectedSignals)
                {
                    var idx = -1;
                    for (int bs_i = 0; bs_i < bs.Count; ++bs_i)
                    {
                        if (bs[bs_i].Brush == x.Brush.Brush)
                        {
                            idx = bs_i;
                            break;
                        }
                    }
                    if (iset.Contains(idx))
                    {
                        iset.Remove(idx);
                    }
                }
                var sb = bs[iset.Count > 0 ? iset.First() : 0];
                new_ss.Brush = bs[iset.Count > 0 ? iset.First() : 0];
            }
            else
            {
                new_ss.Brush = bs[0];
            }
            SelectedSignals.Add(new_ss);
            AllSignals.Remove(ios);
        }

        // ==================================================================
        /// <summary>
        /// Fill the bit column and update signals, too.
        /// </summary>
        /// <param name="ColumnIndex">Column index</param>
        /// <param name="b"></param>
        /// <param name="BufferBits"></param>
        /// <param name="SignalValues"></param>
        void _UpdateSignalCopiesAndSetPlcChartColumns(int ColumnIndex, Tuple<bool, int>[] BufferBits, SignalValuesType SignalValues)
        {
            // 1. Update signal copies.
            _ViewModel.PhysicalSignals.ExtractSignals(BufferBits, SignalValues.Values);
            for (int i = 0; i < TS_PhysicalSignals.Count; ++i)
            {
                TS_PhysicalSignals[i].UpdateValueOnly(BufferBits[i]);
            }
            foreach (var cs in TS_ComputedSignals)
            {
                cs.UpdateValueOnly();
            }

            // 2. Update given column in PlcChart.
            var xvalues = plcchart.XValues as List<Tuple<Int64, bool>>;
            xvalues[ColumnIndex] = new Tuple<long, bool>(SignalValues.Timestamp + _PLC_Offset.Ticks, true);

            int index_index = 0;
            for (int lg_index = 0; lg_index < plcchart.LineGroups.Count; ++lg_index)
            {
                var lg = plcchart.LineGroups[lg_index];
                for (int ln_index = 0; ln_index < lg.Lines.Count; ++ln_index)
                {
                    var ln = lg.Lines[ln_index];
                    var index = ChartSignalIndices[index_index];
                    var ios = TS_Signals[index];
                    if (ios.BitCount == 1)
                    {
                        (ln.Points as IList<double>)[ColumnIndex] = ios.Value == 0 ? PlcChart.LOGICAL_ZERO : PlcChart.LOGICAL_ONE;
                    }
                    else
                    {
                        (ln.Points as IList<double>)[ColumnIndex] = ios.RealValue;
                    }
                    ++index_index;
                }
            }
        }

        // ==================================================================
        void _EmptyPlcChart(int TailId, int HeadId)
        {
            int ncols = HeadId - TailId;

            // 1. Prepare xy arrays.
            _ResizeList(plcchart.XValues as IList<Tuple<Int64, bool>>, ncols, () => new Tuple<Int64, bool>(0, false));
            foreach (var lg in plcchart.LineGroups)
            {
                foreach (var ln in lg.Lines)
                {
                    var lst = ln.Points as IList<double>;
                    _ResizeList(lst, ncols, () => 0.0);
                    for (int i = 0; i < ncols; ++i)
                    {
                        lst[i] = 0.0;
                    }
                }
            }

            // for every column
            var xvalues = plcchart.XValues as IList<Tuple<Int64, bool>>;
            var timespan = _DrawingStep_EndTime.Subtract(_DrawingStep_BeginTime);
            for (int i = 0; i < ncols; ++i)
            {
                xvalues[i] = new Tuple<long, bool>(timespan.Ticks * i / ncols + _DrawingStep_BeginTime.Ticks, false);
            }

            // 3. Let the bitchart do the hard work! :D
            plcchart.TimeBegin = _DrawingStep_BeginTime;
            plcchart.TimeEnd = _DrawingStep_EndTime;

            _ChartTailId = TailId;
            _ChartHeadId = HeadId;

            // 5. PlcChart will work now!
            plcchart.Redraw();
        }

        // ==================================================================
        static void _Copy<T>(IList<T> dst, int dst_offset, IList<T> src, int src_offset, int count)
        {
            if (src_offset > dst_offset)
            {
                for (int i = 0; i < count; ++i)
                {
                    dst[dst_offset + i] = src[src_offset + i];
                }
            }
            else if (src_offset < dst_offset)
            {
                for (int i = count - 1; i >= 0; --i)
                {
                    dst[dst_offset + i] = src[src_offset + i];
                }
            }
        }

        /// <summary>
        /// Enlarge array by incr elements. New elements are left empty.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <param name="incr"></param>
        /// <returns></returns>
        static void _EnlargeBy<T>(IList<T> a, int incr, CreateFunction<T> NewFunc)
        {
            for (int i = 0; i < incr; ++i)
            {
                a.Add(NewFunc());
            }
        }

        /// <summary>
        /// Decrease array size by decr elements.
        /// 1. Front /decr/ elements are deleted.
        /// 2. Result is shifted to left by 1.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="src"></param>
        /// <param name="incr"></param>
        /// <returns></returns>
        static List<T> _DecreaseByNAndShiftBy1<T>(IList<T> src, int decr, CreateFunction<T> NewFunc)
        {
            var r = new List<T>(src.Count - decr);
            for (int i = decr + 1; i < src.Count; ++i)
            {
                r.Add(src[i]);
            }
            r.Add(NewFunc());
            return r;
        }

        static List<T> _DecreaseByN<T>(IList<T> src, int decr)
        {
            var r = new List<T>(src.Count - decr);
            for (int i = decr; i < src.Count; ++i)
            {
                r.Add(src[i]);
            }
            return r;
        }

        static void _ShiftLeftBy1<T>(IList<T> a)
        {
            _Copy(a, 0, a, 1, a.Count - 1);
        }

        private void _RefreshStructure()
        {
            // 1. Set up new fields.
            ChartSignalIndices.Clear();
            plcchart.LineGroups.Clear();
            foreach (var ss in SelectedSignals)
            {
                var ios = ss.Signal;
                var cs = ss.Signal as ComputedSignal;
                var index = _ViewModel.Signals.IndexOf(ios);
                ChartSignalIndices.Add(index);
            }

            // 2. Clone the signals.
            TS_Signals.Clear();
            TS_PhysicalSignals.Clear();
            TS_ComputedSignals.Clear();
            var signal_dict = new Dictionary<string, IOSignal>();
            foreach (var ios in _ViewModel.PhysicalSignals)
            {
                var c = ios.CloneForChart();
                TS_Signals.Add(c);
                TS_PhysicalSignals.Add(c);
                if (c.Name.Length > 0)
                {
                    signal_dict.Add(c.Name, c);
                }
            }
            foreach (var cs in _ViewModel.ComputedSignals)
            {
                var c = cs.CloneForChart();
                var cs2 = c as ComputedSignal;
                cs2.ConnectWithSourceSignals(signal_dict);
                TS_Signals.Add(cs2);
                TS_ComputedSignals.Add(cs2);
            }

            plcchart.XValues = new List<Tuple<Int64, bool>>();
            for (int ss_index = 0; ss_index < SelectedSignals.Count; ++ss_index)
            {
                var ss = SelectedSignals[ss_index];
                var signal_index = ChartSignalIndices[ss_index];
                var ios = TS_Signals[signal_index];
                var cs = ios as ComputedSignal;

                // ln
                var ln = new PlcLine();
                ln.Name = ios.Description;
                ln.Pen = new Pen(ss.Brush.Brush, 1.0);
                ln.Points = new List<double>();
                // lg
                var lg = new PlcLineGroup();
                if (cs == null && ios.BitCount == 1)
                {
                    lg.Height = 40;
                }
                lg.FormatString = "F1";
                lg.Lines.Add(ln);
                // plcchart
                plcchart.LineGroups.Add(lg);
            }
        }

        // =====================================================================
        void _QueryDbRangeIfConnected()
        {
            var server = _ViewModel.MainWindow.PlcConnection;
            if (server != null && server.IsConnected)
            {
                // 1.Fetch the rows.
                DateTime timestamp_end;
                DateTime timestamp_begin;
                if (IsRunningSelected)
                {
                    timestamp_end = DateTime.Now;
                    timestamp_begin = timestamp_end.Subtract(TimeSpan);
                }
                else
                {
                    timestamp_begin = timespanpanel.datetimepickerFrom.Value.Value;
                    timestamp_end = timestamp_begin.Add(TimeSpan);
                }

                _DrawingStep_BeginTime = timestamp_begin;
                _DrawingStep_EndTime = timestamp_end;
                if (IsRunningSelected)
                {
                    _DrawingStep_QueryId = server.QueryDbRange(_ViewModel, timestamp_begin.Subtract(_PLC_Offset), null);
                }
                else
                {
                    _DrawingStep_QueryId = server.QueryDbRange(_ViewModel, timestamp_begin.Subtract(_PLC_Offset), timestamp_end.Subtract(_PLC_Offset));
                }

                // _ViewModel.LogLine("Requested db range from the server.");
                this._DrawingStep = DRAWING_STEP.NONE;
            }
        }

        // =====================================================================
        private DRAWING_STEP _DrawingStep = DRAWING_STEP.NONE;
        private int _DrawingStep_QueryId = -1;
        private DateTime _DrawingStep_BeginTime;
        private DateTime _DrawingStep_EndTime;

        // Database row queries to be performed...
        private Queue<Tuple<int, int>> _DrawingStep_RowQueries = new Queue<Tuple<int, int>>();

#if (false)
        private int _DrawingStep_TailId = -1;
        // Buffer for the redraw; starting at _DrawingStep_TailId.
        private List<SignalValuesType> _DrawingStep_RedrawBuffer = new List<SignalValuesType>();
#endif

        public void StartRedraw()
        {
            _DrawingStep = DRAWING_STEP.SEND_DB_QUERY;
        }

        // =====================================================================
        #region GUI METHODS
        public ChartsPanelControl()
        {
            SelectedSignals = new ObservableCollection<SelectedSignal>();
            AllSignals = new ObservableCollection<IOSignal>();
            InitializeComponent();
        }

        void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_FirstLoad)
            {
                timespanpanel.datetimepickerFrom.Value = DateTime.Now.Subtract(TimeSpan.FromMinutes(5));
                _FirstLoad = false;
            }
        }

        void TimespanPanelControl_OnTimespanChanged(object sender, RoutedEventArgs e)
        {
            if (!_FirstLoad)
            {
                RedrawChart();
            }
        }

        void Button_RemoveSignal_Click(object sender, RoutedEventArgs e)
        {
            var ss = (sender as Button).Tag as SelectedSignal;
            AllSignals.Insert(0, ss.Signal);
            SelectedSignals.Remove(ss);
        }


        void Button_AddSignal_Click(object sender, RoutedEventArgs e)
        {
            var ios = (sender as Button).Tag as IOSignal;
            _AddSignal(ios);
        }

        void Button_UpdateChart_Click(object sender, RoutedEventArgs e)
        {
            _RefreshStructure();
            // User must see the changes!
            RedrawChart();
        }
        #endregion // GUI METHODS

        #region PUBLIC INTERFACE
        /// <summary>Initialize the chart!</summary>
        public void Init(VisualizationControl StationVisual, List<IOSignal> DefaultSignals)
        {
            this.DataContext = StationVisual;

            // 1. Show the signals to select from.
            AllSignals.Clear();
            SelectedSignals.Clear();
            var computed_signals = (from ios in _ViewModel.Signals let csx = ios as ComputedSignal where csx != null select csx).ToArray();
            foreach (var ios in _ViewModel.Signals)
            {
                var cs = ios as ComputedSignal;
                if (cs == null)
                {
                    bool not_used = true;
                    foreach (var cs2 in computed_signals)
                    {
                        bool cs2_uses_ios = cs2.SourceSignals.Count(ios2 => ios2.Name == ios.Name) > 0;
                        not_used = not_used && (!cs2_uses_ios);
                    }
                    if (not_used)
                    {
                        AllSignals.Add(ios);
                    }
                }
                else
                {
                    AllSignals.Add(ios);
                }
            }

            foreach (var x in DefaultSignals)
            {
                _AddSignal(x);
            }

            // 2. and the chart, too.
            _RefreshStructure();

            StartRedraw();
        }

        void RedrawChart()
        {
            if (_FirstLoad)
            {
                return;
            }

            _QueryDbRangeIfConnected();
        }

        /// <summary>
        /// Append signal values to the present chart.
        /// </summary>
        /// <param name="SignalValues"></param>
        /// <param name="Timestamp"></param>
        public void AppendSignalValues(SignalValuesType SignalValues)
        {
            if (!_FirstLoad && IsRunningSelected && TimeSpan.TotalSeconds > 0.0)
            {
                DateTime timestamp_end = DateTime.Now;
                DateTime timestamp_begin = timestamp_end.Subtract(TimeSpan);

                // Have to detect the time offset... :(
                if (SignalValues.Timestamp + _PLC_Offset.Ticks > timestamp_end.Ticks && IsRunningSelected && Math.Abs(_PLC_Offset.TotalSeconds)<1)
                {
                    // Automatic offset detection.
                    _PLC_Offset = new TimeSpan(timestamp_end.Ticks - SignalValues.Timestamp);
                    RedrawChart();
                }
                else
                {
                    // Some variables for bitchart and XYchart
                    var nsignals = ChartSignalIndices.Count;
                    var bufferbits = new Tuple<bool, int>[_ViewModel.PhysicalSignals.Count];
                    long ticks_begin = timestamp_begin.Ticks;

                    // Correct size of XYchart images. 
                    int ndelete;
                    var xvalues = plcchart.XValues as List<Tuple<Int64, bool>>;
                    for (ndelete = 0; ndelete < xvalues.Count && xvalues[ndelete].Item1 < ticks_begin; ++ndelete)
                        ;
                    --ndelete;
                    if (ndelete == 0)
                    {
                        // stays the same
                        _ShiftLeftBy1(xvalues);
                        foreach (var lg in plcchart.LineGroups)
                        {
                            foreach (var ln in lg.Lines)
                            {
                                _ShiftLeftBy1(ln.Points as IList<double>);
                            }
                        }
                        ++_ChartTailId;
                        ++_ChartHeadId;
                    }
                    else if (ndelete < 0)
                    {
                        // at most 1 more.
                        _EnlargeBy(xvalues, 1, () => new Tuple<Int64,bool>(0L, false));
                        foreach (var lg in plcchart.LineGroups)
                        {
                            foreach (var ln in lg.Lines)
                            {
                                _EnlargeBy(ln.Points as IList<double>, 1, () => 0.0);
                            }
                        }
                        ++_ChartHeadId;
                    }
                    else if (ndelete > 0)
                    {
                        // Perhaps some less.
                        xvalues = _DecreaseByNAndShiftBy1(xvalues, ndelete, () => new Tuple<Int64,bool>(0L,false));
                        plcchart.XValues = xvalues;
                        foreach (var lg in plcchart.LineGroups)
                        {
                            foreach (var ln in lg.Lines)
                            {
                                ln.Points = _DecreaseByNAndShiftBy1(ln.Points as IList<double>, ndelete, () => 0.0);
                            }
                        }
                        _ChartTailId += ndelete + 1;
                        ++_ChartHeadId;
                    }
                    _UpdateSignalCopiesAndSetPlcChartColumns(xvalues.Count - 1, bufferbits, SignalValues);

                    plcchart.Redraw();
                }
            }
        }

        private void _QueryRowsIfPossible()
        {
            var server = _ViewModel.MainWindow.PlcConnection;
            if (server != null && server.IsConnected && _DrawingStep_RowQueries.Count > 0)
            {
                var rr = _DrawingStep_RowQueries.Dequeue();
                server.QueryDatabaseRows(_ViewModel, rr.Item1, rr.Item2);
            }
        }

        public void HandleMessageFromPlc(ServerConnection sender, PlcCommunication.MessageFromPlc message)
        {
            if (message.HasResponseToDbRangeQuery)
            {
                var rdr = message.ResponseToDbRangeQuery;
                if (rdr.HasHeadId && rdr.HasTailId)
                {
                    _ViewModel.LogLine("ResponseToDbRangeQuery: [" + rdr.TailId + " .. " + rdr.HeadId + ").");

                    // TODO: prepare the queries and send them...
                    int total_range = rdr.HeadId - rdr.TailId;
                    TimeSpan total_span = _DrawingStep_EndTime.Subtract(_DrawingStep_BeginTime);
                    int nbatches = (total_range + ServerConnection.BATCH_SIZE - 1) / ServerConnection.BATCH_SIZE;

                    // 1. Prepare the queries.
                    _DrawingStep_RowQueries.Clear();
                    for (int i = 0; i < nbatches; ++i)
                    {
                        int head_id = rdr.HeadId - i * ServerConnection.BATCH_SIZE;
                        int tail_id = Math.Max(rdr.TailId, head_id - ServerConnection.BATCH_SIZE);
                        _DrawingStep_RowQueries.Enqueue(new Tuple<int,int>(tail_id, head_id));
                    }

                    // 1. Clear the chart.
                    _EmptyPlcChart(rdr.TailId, rdr.HeadId);

                    // 3. Start ticking.
                    _QueryRowsIfPossible();
                }
            }

            // Update the chart, if possible.
            int nrows = message.ResponseToDatabaseRowsCount;
            int update_count = 0;
            for (int i = 0; i < nrows; ++i)
            {
                var row = message.GetResponseToDatabaseRows(i);
                var row_id = row.RowId;
                int index = row_id - _ChartTailId;
                if (_ChartTailId <= row_id && row_id < _ChartHeadId && index>=0 && index<plcchart.XValues.Count)
                {
                    var encoded_signals = row.SignalValues.ToByteArray();
                    var signals = new SignalValuesType()
                    {
                        Id = row.RowId,
                        Version = row.Version,
                        Timestamp = row.GetTimestamp().Ticks,
                        Values = encoded_signals,
                    };
                    _UpdateSignalCopiesAndSetPlcChartColumns(row_id - _ChartTailId, _GetBufferBits(), signals);
                    ++update_count;
                }
                else
                {
#if (false)
                    _ViewModel.LogLine("HandleMessageFromPlc: Database row (" + row_id + ") outside the chart horizontal scale [" + _ChartTailId + " ... " + _ChartHeadId + ")!");
#endif
                }
            }
            if (nrows > 0)
            {
                _QueryRowsIfPossible();
            }

            if (update_count > 0)
            {
                plcchart.Redraw();
                // FIXME: do what?
            }
        }

        public void TimerTick()
        {
            if (!_FirstLoad && IsRunningSelected && TimeSpan.TotalSeconds > 0.0)
            {
                var now = DateTime.Now;
                plcchart.TimeBegin = now.Subtract(TimeSpan);
                plcchart.TimeEnd = now;

                // Correct size of XYchart images. 
                var ticks_begin = plcchart.TimeBegin.Ticks;
                int ndelete;
                var xvalues = plcchart.XValues as IList<Tuple<Int64,bool>>;
                for (ndelete = 0; ndelete < xvalues.Count && xvalues[ndelete].Item2 && xvalues[ndelete].Item1 < ticks_begin; ++ndelete)
                    ;

                // Perhaps some less.
                if (ndelete > 0)
                {
                    plcchart.XValues = _DecreaseByN(plcchart.XValues as IList<Tuple<Int64, bool>>, ndelete);
                    foreach (var lg in plcchart.LineGroups)
                    {
                        foreach (var ln in lg.Lines)
                        {
                            ln.Points = _DecreaseByN(ln.Points as IList<double>, ndelete);
                        }
                    }
                    _ChartTailId += ndelete;
                }
                plcchart.Redraw();
            }

            switch (_DrawingStep)
            {
                case DRAWING_STEP.NONE:
                    break;
                case DRAWING_STEP.SEND_DB_QUERY:
                    _QueryDbRangeIfConnected();
                    break;
            }
        }
        #endregion // PUBLIC INTERFACE
    } // class ChartsPanelControl
}
