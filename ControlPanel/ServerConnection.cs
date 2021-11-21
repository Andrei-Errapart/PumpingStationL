using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Threading;

using System.Net;
using System.Net.Sockets;

namespace ControlPanel
{
    /// <summary>
    /// Persistent connection to the PLC. Reconnects on timeouts, etc. Takes care of the LocalSignalDb, too.
    /// </summary>
    public class ServerConnection
    {
        /// <summary>
        /// Synchronization interval, milliseconds. The lower interval, the faster synchronization.
        /// </summary>
        public const int SYNCHRONIZATION_INTERVAL_MS = 100;
        public const int BATCH_SIZE = 100;

        private Dictionary<int, PlcCommunication.RowUser> _PlcUsersById = new Dictionary<int, PlcCommunication.RowUser>();
        private Dictionary<int, VisualizationControl> _StationVisualsById = new Dictionary<int, VisualizationControl>();
        private Dictionary<string, int> _IdsByName = new Dictionary<string, int>();
        private Queue<PlcCommunication.MessageToPlc> _QueueToServer = new Queue<PlcCommunication.MessageToPlc>();
        private MainWindow _MainWindow;

        /// <summary>
        /// Are we connected?
        /// </summary>
        public bool IsConnected { get { return _TcpClient != null && _TcpClient.Client != null && _TcpClient.Client.Connected; } }

        // Are we closed? After close, there is no return.
        private bool IsClosed { get { return _RemoteEndPoint == null; } }

        #region PUBLIC INTERFACE
        public ServerConnection(
            MainWindow MainWindow,
            IPEndPoint RemoteEndPoint,
            string UserName,
            string Password)
        {
            // 1. Set up the fields.
            this._MainWindow = MainWindow;
            this._RemoteEndPoint = RemoteEndPoint;
            this._UserName = UserName;
            this._Password = Password;
            this._TcpClient = null;
            this._TcpClientStream = null;
            this._ReadThread = new System.Threading.Thread(this._ReadThreadFunction);
            this._WriteThread = new System.Threading.Thread(this._WriteThreadFunction);

            // 2. Start the reading thread..
            this._ReadThread.Start();
            this._WriteThread.Start();
        }

        // ==================================================================
        /// <summary>
        /// Send the message to the given PLC.
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public int SendMessage(PlcCommunication.MessageToPlc.Builder builder)
        {
            int id;
            lock (this._QueueToServer)
            {
                id = _NextRequestId++;
                builder.Id = id;
                var msg = builder.Build();
                _QueueToServer.Enqueue(msg);
            }
            return id;
        }

        /// <summary>
        /// Write given signals.
        /// </summary>
        /// <param name="SignalValues"></param>
        public int WriteSignals(VisualizationControl Source, IList<KeyValuePair<string,int>> SignalValues)
        {
            int plc_id;
            if (_IdsByName.TryGetValue(Source.DisplayName, out plc_id) && plc_id >= 0)
            {
                var builder = PlcCommunication.MessageToPlc.CreateBuilder();
                builder.ForwardToPlcId = plc_id;

                foreach (var sv in SignalValues)
                {
                    var sv_builder = new PlcCommunication.SignalAndValue.Builder();
                    sv_builder.Name = sv.Key;
                    sv_builder.Value = sv.Value;
                    builder.AddSetSignals(sv_builder);
                }

                return SendMessage(builder);
            }
            else
            {
                _MainWindow.LogLine("ServerConnection", "WriteSignals: Plc '" + Source.DisplayName + "' is not listed by the server.");
                return -1;
            }
        }

        // ==================================================================
        public int QueryDbRange(VisualizationControl Source, DateTime FirstTime, DateTime? LastTime)
        {
            int plc_id;
            if (_IdsByName.TryGetValue(Source.DisplayName, out plc_id) && plc_id >= 0)
            {
                var builder = PlcCommunication.MessageToPlc.CreateBuilder();
                builder.TargetPlcId = plc_id;
                var rbuilder = PlcCommunication.DbRangeQuery.CreateBuilder();
                rbuilder.FirstTimeTicks = FirstTime.Ticks;
                if (LastTime.HasValue)
                {
                    rbuilder.LastTimeTicks = LastTime.Value.Ticks;
                }
                builder.QueryDbRange = rbuilder.Build();

                int r = SendMessage(builder);
                return r;
            }
            else
            {
                _MainWindow.LogLine("ServerConnection", "QueryDbRange: Plc '" + Source.DisplayName + "' is not listed by the server.");
                return -1;
            }
        }

        // ==================================================================
        public int QueryDatabaseRows(VisualizationControl Source, int TailId, int HeadId)
        {
            int plc_id;
            if (_IdsByName.TryGetValue(Source.DisplayName, out plc_id) && plc_id >= 0)
            {
                var builder = PlcCommunication.MessageToPlc.CreateBuilder();
                builder.TargetPlcId = plc_id;

                var range = PlcCommunication.IdRange.CreateBuilder().SetTailId(TailId).SetHeadId(HeadId).Build();
                builder.QueryDatabaseRows = range;

                return SendMessage(builder);
            }
            else
            {
                _MainWindow.LogLine("ServerConnection", "QueryDatabaseRows: Plc '" + Source.DisplayName + "' is not listed by the server.");
                return -1;
            }
        }

        // ==================================================================
        /// <summary>
        /// Close connection.
        /// </summary>
        public void Close()
        {
            // Signal end of work.
            this._RemoteEndPoint = null;
            // Close the tcp client, if any.
            var tcpclient = _TcpClient;
            // signal the reading thread to be closed.
            _TcpClient = null;
            _TcpClientStream = null;
            if (tcpclient != null)
            {
                var socket = tcpclient.Client;
                if (socket != null && socket.Connected)
                {
                    try
                    {
                        tcpclient.Close();
                    }
                    catch (Exception)
                    {
                        // pass.
                    }
                }
            }
            _DispatchConnectionStatus(false);
        }
        #endregion // PUBLIC INTERFACE

        // ==================================================================
        /// <summary>
        /// Received some of the requested data...
        /// </summary>
        /// <param name="iar"></param>
        private void _ReadThreadFunction()
        {
            try
            {
                // Main loop. stuff.
                while (!IsClosed)
                {
                    var client = new TcpClient();

                    _DispatchConnectionStatus(false);
                    try
                    {
                        client.Connect(this._RemoteEndPoint);
                    }
                    catch (Exception)
                    {
                        // TODO: shall we log it?
                        System.Threading.Thread.Sleep(100);
                        continue;
                    }

                    long Next_OOB_Id = -1; // only negative values.
                    var stream = client.GetStream();
                    this._TcpClient = client;
                    this._TcpClientStream = stream;

                    _DispatchConnectionStatus(true);

                    bool read_ok = true;
                    // Server expects the credentials.
                    {
                        var b_msg = new PlcCommunication.MessageFromPlc.Builder();

                        b_msg.SetId(Next_OOB_Id);
                        var b_cfg = new PlcCommunication.Configuration.Builder();
                        b_cfg.SetDeviceId(_UserName);
                        b_cfg.SetPassword(_Password);
                        b_msg.SetOOBConfiguration(b_cfg);
                        var msg = b_msg.Build();
                        msg.WriteDelimitedTo(stream);
                        --Next_OOB_Id;
                    }

                    // Service loop.
                    while (!IsClosed && read_ok)
                    {
                        try
                        {
                            var message = PlcCommunication.MessageFromPlc.ParseDelimitedFrom(stream);
                            if (IsClosed)
                            {
                                read_ok = false;
                            }
                            else
                            {
                                // Keep ourselves alive.
                                _MainWindow.PostponeReconnect();
#if (true)
                                bool time_to_fetch_a_batch = false;
                                // 1. Hijack database stuff.
                                try
                                {
                                    IList<PlcCommunication.RowUser> users = message.OOBRowUsersList;
                                    if (users != null && users.Count>0)
                                    {
                                        // 1. Log the list of users.
                                        var sb = new StringBuilder();
                                        foreach (var uu in users)
                                        {
                                            if (uu.Type == "PLC" && uu.HasName && uu.HasId && uu.HasIsPublic && uu.IsPublic)
                                            {
                                                sb.Append("'" + uu.Name + "' ");
                                                _PlcUsersById.Add(uu.Id, uu);
                                            }
                                        }
                                        _MainWindow.LogLine("ServerConnection", "List of PLCs: " + sb.ToString());

                                        // select all our users.
                                        var msg = (new PlcCommunication.MessageToPlc.Builder()).SetId(Next_OOB_Id);
                                        sb.Clear();
                                        foreach (var v in this._MainWindow.StationVisuals)
                                        {
                                            var display_name = v.DisplayName;
                                            var u3 = (from u2 in users where u2.Type == "PLC" && u2.Name == display_name && u2.IsPublic select u2).SingleOrDefault();
                                            if (u3!=null)
                                            {
                                                msg = msg.AddMonitorUsers(u3.Id);
                                                sb.Append(sb.Length == 0 ? "" : ", ");
                                                sb.Append(display_name);
                                                _StationVisualsById.Add(u3.Id, v);
                                                _IdsByName.Add(u3.Name, u3.Id);
                                            }
                                        }
                                        _MainWindow.LogLine("ServerConnection", "Selected PLC-s to monitor: " + sb.ToString());
                                        msg.Build().WriteDelimitedTo(stream);
                                    }
#if (false)
                                    if (message.HasOOBDatabaseRange)
                                    {
                                        db.HandlePlcDatabaseRange(message.OOBDatabaseRange);
                                        time_to_fetch_a_batch = _ViewModel.LocalConfiguration.IsSynchronizationEnabled;
                                    }
                                    if (message.HasOOBDatabaseRow && _ViewModel.LocalConfiguration.IsSynchronizationEnabled)
                                    {
                                        db.HandleSignalValues(message.OOBDatabaseRow);
                                    }

                                    if (message.ResponseToDatabaseRowsCount > 0 && _ViewModel.LocalConfiguration.IsSynchronizationEnabled)
                                    {
                                        db.HandleSignalValues(message.ResponseToDatabaseRowsList);
                                        time_to_fetch_a_batch = true;
                                    }
#endif
                                }
                                catch (Exception ex)
                                {
                                    _MainWindow.LogLine("ServerConnection", "Error when handling message: " + ex.Message);
                                }

#if (false)
                                // TODO: handle timeouts.
                                if (time_to_fetch_a_batch)
                                {
                                    System.Threading.Thread.Sleep(SYNCHRONIZATION_INTERVAL_MS);
                                    var range = db.NextBatch();
                                    if (range != null)
                                    {
                                        var builder = PlcCommunication.MessageToPlc.CreateBuilder();
                                        builder.Id = _NextRequestId++;
                                        builder.SetQueryDatabaseRows(range);
                                        var query = builder.Build();
                                        query.WriteDelimitedTo(_TcpClientStream);
                                        _LastBatchQuerySent = query;
                                        time_to_fetch_a_batch = false;
                                    }
                                }
#endif
                                // 2. Send it to the main thread for processing, if needed.
                                if (message.HasSourceId)
                                {
                                    int id = message.SourceId;
                                    VisualizationControl vc;
                                    if (_StationVisualsById.TryGetValue(id, out vc) && vc != null)
                                    {
                                        vc.Dispatcher.BeginInvoke(new Action(() => { vc.HandleMessageFromPlc(this, message); }));
                                    }
                                    else
                                    {
                                        _MainWindow.LogLine("ServerConnection", "HandleMessageFromPlc: Not setup for plc(id=" + id + ")!");
                                    }
                                }
#endif
                            }
                        }
                        catch (Exception)
                        {
                            // Most probably the peer disconnected.
                            // TODO: shall we log it?
                            System.Threading.Thread.Sleep(100);
                            read_ok = false;
                            continue;
                        }
                    }

                    if (!IsClosed)
                    {
                        // Don't reconnect in a tight loop.
                        System.Threading.Thread.Sleep(100);
                    }
                }
                // it's all over. Nothing to do, anymore.
            }
            catch (Exception)
            {
                // TODO: should the exception be logged?
                // pass them this round...
            }
        }

        // ==================================================================
        private void _WriteThreadFunction()
        {
            while (!IsClosed)
            {
                bool did_some_work = false;
                var client = this._TcpClient;
                var stream = this._TcpClientStream;
                if (client != null && client.Connected && stream!=null)
                {
                    try
                    {
                        PlcCommunication.MessageToPlc msg = null;
                        lock (_QueueToServer)
                        {
                            if (_QueueToServer.Count > 0)
                            {
                                msg = _QueueToServer.Dequeue();
                            }
                        }
                        if (msg != null)
                        {
                            did_some_work = true;
                            msg.WriteDelimitedTo(stream);
                        }
                    }
                    catch(Exception ex)
                    {
                        _MainWindow.LogLine("ServerConnection", "WriteTheadFunction error: " + ex.Message);
                    }
                }
                System.Threading.Thread.Sleep(did_some_work ? 0 : 100);
            }
        }

        // ==================================================================
        /// <summary>
        /// Endpoint we are connected to. If null, no longer connect!
        /// </summary>
        volatile IPEndPoint _RemoteEndPoint;
        /// <summary>Username to be sent to the server.</summary>
        volatile string _UserName;
        /// <summary>Password to be sent to the server.</summary>
        volatile string _Password;
        /// <summary>
        /// Socket we are connected on.
        /// </summary>
        volatile TcpClient _TcpClient;
        /// <summary>
        /// Network stream of the _TcpClient.
        /// </summary>
        volatile NetworkStream _TcpClientStream;

        /// <summary>
        /// Connection thread, if any.
        /// </summary>
        System.Threading.Thread _ReadThread;

        /// <summary>
        /// Connection write thread, if any.
        /// </summary>
        System.Threading.Thread _WriteThread;

        /// <summary>
        /// Id of the next request.
        /// </summary>
        volatile int _NextRequestId = 1;
        /// <summary>
        /// Last batch query sent to the PLC, if any.
        /// </summary>
        PlcCommunication.MessageToPlc _LastBatchQuerySent = null;

        // ==================================================================
        void _DispatchConnectionStatus(bool IsConnected)
        {
            var mw = _MainWindow;
            if (System.Threading.Thread.CurrentThread == mw.Dispatcher.Thread)
            {
                mw.IsConnected = IsConnected;
            }
            else
            {
                mw.Dispatcher.BeginInvoke(new Action(() => { mw.IsConnected = IsConnected; }));
            }
        }
    }
}
