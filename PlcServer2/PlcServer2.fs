namespace PlcServer2
    open System
    open System.Collections.Generic
    open System.Data.SQLite
    open System.IO
    open System.Linq
    open System.Net
    open System.Net.Sockets
    open System.Runtime.Serialization
    open System.Text.RegularExpressions
    open System.Threading
    open System.Xml
    open System.Diagnostics
    open Microsoft.FSharp.Data.TypeProviders
    open Microsoft.FSharp.Linq
    open PlcCommunication
    open PlcDb
    open SmsMessenger
    open Types

    // ======================================================================
    module PlcServer =
        // Create and add a console debug listener.
        type DbgListener() =
            inherit ConsoleTraceListener()
            override u.Write (msg : string) = printf "[DBG] %s" msg
            override u.WriteLine (msg : string) = printfn "[DBG] %s" msg
        let console_debug_listener = new DbgListener()
        Debug.Listeners.Add(console_debug_listener) |> ignore
        Debug.AutoFlush <- true

        let MAX_BATCH_SIZE = 100
        let ARCHIVE_TIMESPAN = TimeSpan.FromDays(100.0)
        let PING_PERIOD_MS = 30 * 1000
        /// Send PLC online status notification only after this time since server start
        let PLC_ONLINE_NOTIFICATION_AFTER_S : float = 300.0
        /// Send PLC offline status notification only after this time since last signal reception
        let PLC_OFFLINE_NOTIFICATION_AFTER_S : int = 300

        let mutable config_filename = "C:\\PlcServerService\\PlcServer.ini"

        let mutable cout = System.Console.Out

        // Log filename for the PlcService
        let log_filename = [| System.IO.Path.GetTempPath(); "C:\\PlcServerService\\PlcServer-log.txt" |] |> System.IO.Path.Combine

        // ======================================================================
        let my_next_uid = ref 0
        // get process-unique id, relatively ugly.
        let get_next_uid () =
            let r = !my_next_uid
            my_next_uid := !my_next_uid + 1
            r

        // ======================================================================
        type MailboxMessageToServer =
            | Start of (string * PlcCommunication.MessageFromPlc * TcpClient * Stream * EndPoint * AsyncReplyChannel<User option>)
            | ProcessMessageFromPlc of (PlcUser * PlcCommunication.MessageFromPlc)
            | ProcessMessageFromOperator of (OperatorUser * PlcCommunication.MessageToPlc)
            | Finish of User
            | Die
        // ======================================================================
        and MailboxMessageToUser = 
            | SendMessageToPlc of (PlcCommunication.MessageToPlc)
            | SendMessageToOperator of (PlcCommunication.MessageFromPlc)
            | Finish
        // ======================================================================
        and UserType =
            | PLC
            | Operator
        // ======================================================================
        and User (user_type:UserType, user_info:PlcDb.User, user_session:PlcDb.Session, tcp_client:TcpClient, tcp_stream:Stream, server:PlcServer) as self =
            let write_queue = new BlockingQueue<MailboxMessageToUser>()
            let write_thread = new Thread(fun () ->
                while not (write_queue.IsStopped()) do
                    let msg_to_user = write_queue.Dequeue()
                    match msg_to_user with
                    | Some(MailboxMessageToUser.SendMessageToPlc (msg)) ->
                        try
                            msg.WriteDelimitedTo(tcp_stream)
                        with 
                            | ex ->
                                cout.WriteLine(self.ClientName + ": Cannot send MessageToPlc: " + ex.Message)
                    | Some(MailboxMessageToUser.SendMessageToOperator (msg)) ->
                        try
                            msg.WriteDelimitedTo(tcp_stream)
                        with 
                            | ex ->
                                cout.WriteLine(self.ClientName + ": Cannot send MessageToOperator: " + ex.Message)
                    | _  -> ()
            )

            member this.Open () = write_thread.Start()
            member val UserType = user_type with get
            member val UserInfo = user_info with get
            member val UserSession = user_session with get
            // Name of the client.
            member val ClientName = user_info.Name + "(" + ((get_next_uid ()).ToString()) + ")" with get
            member val NextQueryId = 1L with get,set
            // Send message down the wire.
            member this.SendToPlc (msg:PlcCommunication.MessageToPlc) = write_queue.Enqueue(MailboxMessageToUser.SendMessageToPlc(msg))
            member this.SendToOperator (msg:PlcCommunication.MessageFromPlc) = write_queue.Enqueue(MailboxMessageToUser.SendMessageToOperator(msg))

            // Stop the mailbox servicing this user.
            member this.Close() =
                if not (write_queue.IsStopped()) then
                    write_queue.Stop()
                    try
                        tcp_client.Client.Close()
                    with ex -> cout.WriteLine(this.ClientName + ": Error when stopping write queue: " + ex.Message)

        // ======================================================================
        and PlcUser (user_type:UserType, user_info:PlcDb.PlcUser, user_session:PlcDb.Session, tcp_client:TcpClient, tcp_stream:Stream, server:PlcServer) as self =
            inherit User(user_type, user_info, user_session, tcp_client, tcp_stream, server)
            let rows_to_fetch = new Queue<int*int>()
            let query_next_rows_if_any () =
                if rows_to_fetch.Count>0 then
                    let range = rows_to_fetch.Dequeue()
                    U.query_db_range tcp_stream self.NextQueryId range
                    self.NextQueryId <- self.NextQueryId + 1L
                ()

            /// UserInfo
            member val UserInfo = user_info

            /// Latest configuration, if any.
            member this.PlcConfiguration
                with get() : (PlcDb.Configuration option) = if user_info.Configurations.Count>0 then Some (user_info.Configurations.Last()) else None

            /// Id of cfg based on which signals etc. are currently loaded.
            member val private SignalTablesCfgId = 0 with get, set

            /// Current signals with values. Updated with each PLC row reception and configuration change.
            member val private Signals = new Dictionary<string,IOSignal> () with get, set

            /// Current computed signals with values. Updated with each PLC row reception and configuration change.
            member val private ComputedSignals = new Dictionary<string,ComputedSignal> () with get, set

            /// Indicates if PLC online state has been notified to operators
            member val private NotifiedAsOnline = false with get, set

            /// Lookup table of consecutive failure counts and indication whether operators are notified.
            member val private SignalConnectionFails = new Dictionary<string,AlarmCounter> () with get, set

            /// Lookup table of consequtive failure counts and indication whether alarm sent.
            member val private SignalValueFails = new Dictionary<string,AlarmCounter> () with get, set

            /// Process the messages.
            member this.ProcessMessageFromPlc(msg:PlcCommunication.MessageFromPlc) =
                if msg.HasOOBConfiguration then
                    // TODO: is it needed anymore?
                    // Store the configuration in the configuration file.
                    let cfg = msg.OOBConfiguration
                    if cfg.HasDeviceId && cfg.HasVersion && cfg.HasConfigurationFile then
                        let cfg_bytes = cfg.ConfigurationFile.ToByteArray()
                        let cfg_index = user_info.Configurations.FindLastIndex(fun (x:PlcDb.Configuration) -> U.are_bytes_equal cfg_bytes x.SetupFile)
                        // let first_match = (seq { for dbc in list_of_cfgs do if are_bytes_equal cfg_bytes (dbc.ConfigurationFile.ToArray()) then yield dbc }).FirstOrDefault()
                        if cfg_index<0 then
                            cout.WriteLine(this.ClientName + ": The configuration is different from the one in the server database.")
                            let new_cfg = new PlcDb.Configuration(
                                                        CreateDate = DateTime.Now,
                                                        SetupFile = cfg_bytes,
                                                        Preferences = [| |])
                            user_info.InsertConfiguration(new_cfg)
                            cout.WriteLine(this.ClientName + ": New configuration version: " + (new_cfg.Id.ToString()) + ", size: " + (cfg_bytes.Length.ToString()) + ".")
                        else
                            cout.WriteLine(this.ClientName + ": Configuration already seen as version: " + (user_info.Configurations.[cfg_index].Id.ToString()) )
                    ()
                if msg.HasOOBDatabaseRange then
                    let dbr = msg.OOBDatabaseRange
                    if dbr.HasTailId && dbr.HasHeadId then
                        let tail_id, head_id = dbr.TailId, dbr.HeadId
                        // TODO: how slow it is?
                        cout.WriteLine(this.ClientName + ": OOB Database range: [" + tail_id.ToString() + " .. " + head_id.ToString() + " ).")
                        match this.PlcConfiguration with
                            | Some cfg ->
                                rows_to_fetch.Clear()
                                let rrows = new List<int*int>()
                                // 1. Fetch the rows.
                                let ids = user_info.QueryIdsOfSignalValues (cfg)
                                if ids.Count=0 then
                                    // the easy case.
                                    let nbatches = (head_id - tail_id + MAX_BATCH_SIZE - 1) / MAX_BATCH_SIZE
                                    for i=nbatches-1 downto 0 do
                                        let start_id = i * MAX_BATCH_SIZE + tail_id
                                        rows_to_fetch.Enqueue( (start_id, start_id + MAX_BATCH_SIZE) )
                                else
                                    // 2. Scan for holes.c  
                                    let mutable prev_tail_id = tail_id - 1
                                    for r_head_id in ids do
                                        let r_tail_id = prev_tail_id + 1
                                        if r_tail_id < r_head_id then
                                            U.enqueue_range MAX_BATCH_SIZE rrows (r_tail_id, r_head_id)
                                        prev_tail_id <- r_head_id
                                    // 3. Fix up the remaining hole (if any)
                                    let r_tail_id = prev_tail_id + 1
                                    if r_tail_id <= head_id then
                                        U.enqueue_range MAX_BATCH_SIZE rrows (r_tail_id,head_id + 10) // only head_id+1 is necessary, the +10 is extra.
                                    for i=rrows.Count-1 downto 0 do
                                        rows_to_fetch.Enqueue(rrows.[i])
                                // 4. Start!
                                query_next_rows_if_any ()
                                ()
                            | None -> cout.WriteLine(this.ClientName + ": No current configuration!")
                    ()
                if msg.HasOOBDatabaseRow then
                    cout.WriteLine(this.ClientName + ": OOB Database row received: " + (msg.OOBDatabaseRow.RowId.ToString()));
                    let plc_row = msg.OOBDatabaseRow
                    if plc_row.HasRowId && plc_row.HasTimeMs && plc_row.HasVersion && plc_row.HasSignalValues then
                        match this.PlcConfiguration with
                            | Some plc_cfg ->
                                let is_present = user_info.QuerySignalValuePresence 
                                                                (plc_cfg)
                                                                (plc_row.RowId)
                                                                (U.DateTimeOfJavaMilliseconds (plc_row.TimeMs))
                                if not is_present then
                                    cout.WriteLine(this.ClientName + ": OOB Database row signal values: " + (plc_row.SignalValues.Length.ToString()) + " bytes.")
                                    try
                                        let sv = U.dbsv_of_commrow plc_cfg (user_session.Id) plc_row
                                        this._ReceiveSignals (sv.SignalValues)
                                        user_info.InsertSignalValue sv
                                    with ex -> U.print_ex (this.ClientName, ex, "Failed to add/receive signal.")

                            | None ->
                                cout.WriteLine(this.ClientName + ": Error: no configuration (yet)!")
                    ()
                if msg.ResponseToDatabaseRowsCount>0 then 
                    match this.PlcConfiguration with
                        | Some plc_cfg ->
                            let rows = (seq {   for ri=0 to msg.ResponseToDatabaseRowsCount-1 do
                                                    let r = msg.GetResponseToDatabaseRows(ri);
                                                    if r.HasRowId && r.HasTimeMs && r.HasVersion && r.HasSignalValues then
                                                        yield r } |> Seq.sortBy(fun x -> x.RowId)).ToArray()
                            if rows.Length>0 then
                                let delete_time = DateTime.Now.Subtract(ARCHIVE_TIMESPAN)
                                let delete_time_java = U.JavaMillisecondsOfDateTime delete_time
                                let mutable delete_count = 0;
                                let id1, id2 = rows.First().RowId, rows.Last().RowId
                                // cout.WriteLine(this.ClientName + ": Database rows: ok=" + (msg.Response.OK.ToString()) + (if msg.Response.HasMessage then ("msg=" + msg.Response.Message) else ""))
                                cout.WriteLine(this.ClientName + ": Database rows: [" + id1.ToString() + " .. " + (id2 + 1).ToString() + ").")
                                let indices_in_db = user_info.QueryIdsOfSignalValuesRange plc_cfg id1 (id2+1)
                                // let indices_in_db = (seq { for id=id1 to id2 do if db_indices.Contains(id) then yield id}).ToArray();
                                let svs = List<PlcDb.SignalValue>()
                                for plc_row in rows do
                                    if not (indices_in_db.Contains(plc_row.RowId)) then
                                        if plc_row.TimeMs>=delete_time_java then
                                            try
                                                let sv = U.dbsv_of_commrow plc_cfg (user_session.Id) plc_row
                                                this._ReceiveSignals (sv.SignalValues)
                                                svs.Add(sv)
                                            with
                                                | ex -> U.print_ex (this.ClientName, ex, "Failed to add/receive a signal.")
                                                    
                                        else
                                            delete_count <- delete_count + 1
                                user_info.InsertSignalValues svs

                                //
                                if delete_count>0 then
                                    cout.WriteLine(sprintf "%s: Skipped %d records because of the old age. Synchronization stopped." (this.ClientName) delete_count)
                                    rows_to_fetch.Clear()
                                else
                                    query_next_rows_if_any ()
                        | None ->
                            cout.WriteLine(this.ClientName + ": Error: No configuration (yet)!")

                this.NotifySignalEvents()
                ()

            /// Send latest configuration in a form suitable for Google protocol buffers.
            member this.SendLatestConfigurationIfAny () =
                if user_info.Configurations.Count>0 then
                    let db_cfg, msg_cfg = U.msg_of_db_row_configuration (user_info.Id) (user_info.Configurations.Last())
                    cout.WriteLine(this.ClientName + ": Sending latest configuration.")
                    let msg = (MessageToPlc.CreateBuilder()).SetId(this.NextQueryId).SetNewRowPlcConfiguration(msg_cfg).Build()
                    this.SendToPlc(msg)
                    this.NextQueryId <- this.NextQueryId + 1L
                    ()

            /// Populate signal tables
            member private this._InitSignals () =
                let plc_cfg = this.PlcConfiguration.Value
                this.Signals <- new Dictionary<string,IOSignal> ()
                this.ComputedSignals <- new Dictionary<string,ComputedSignal> ()

                // Signals
                let signal_dfns = plc_cfg.Setup.SelectNodes("//signal")
                for s_dfn in signal_dfns do
                    let s_name = U.attr s_dfn "name"
                    let ios = new IOSignal(
                                    (System.Convert.ToInt32(U.attr s_dfn "id")),
                                    (U.attr s_dfn "name"),
                                    false,
                                    (IOSignal.TypeOfString(U.attr s_dfn "type").Value),
                                    "",
                                    (U.attr s_dfn "ioindex"),
                                    (U.attr s_dfn "description"),
                                    (U.attr s_dfn "text0"),
                                    (U.attr s_dfn "text1"),
                                    (this.UserInfo.Name)
                    )
                    this.Signals.Add(s_name, ios)
                
                // Computed signals
                let computed_signal_dfns = plc_cfg.Setup.SelectNodes("//computedsignal")
                for cs_dfn in computed_signal_dfns do
                    let cs_name = U.attr cs_dfn "name"
                    let cs = new ComputedSignal(
                                    cs_name,
                                    ComputedSignal.ComputationTypeOf(U.attr cs_dfn "type"),
                                    (U.attr cs_dfn "sources").Split([|";"|], StringSplitOptions.RemoveEmptyEntries),
                                    (U.attr cs_dfn "params"),
                                    (U.attr cs_dfn "formatstring"),
                                    (U.attr cs_dfn "unit"),
                                    (U.attr cs_dfn "description"),
                                    (this.UserInfo.Name)
                    )
                    try
                        cs.ConnectWithSourceSignals(this.Signals)
                        this.ComputedSignals.Add(cs_name, cs)
                    with
                        | ex -> U.print_ex (this.ClientName, ex, ("Failed to initialize computed signal " + cs_name + "."))



                // Remove obsolete signals from history
                for scf in this.SignalConnectionFails do
                    if 
                        (not (this.Signals.ContainsKey(scf.Key) || this.ComputedSignals.ContainsKey(scf.Key))) &&
                        this.SignalConnectionFails.ContainsKey(scf.Key)
                    then
                        this.SignalConnectionFails.Remove(scf.Key) |> ignore
                
                for svf in this.SignalValueFails do
                    if
                        (not (this.Signals.ContainsKey(svf.Key) || this.ComputedSignals.ContainsKey(svf.Key))) &&
                        this.SignalValueFails.ContainsKey(svf.Key)
                    then
                        this.SignalValueFails.Remove(svf.Key) |> ignore
                
                // Set cfg id
                this.SignalTablesCfgId <- plc_cfg.Id
                ()

            /// Update signal connection and value histories
            member private this._UpdateHistory (s : IOSignal) (notification_condition) (is_connected) (is_alarming) =
                let is_computed = match s with
                                    | :? ComputedSignal -> true
                                    | _ -> false

                // Update signal connection history
                if this.SignalConnectionFails.ContainsKey(s.Name) then
                    if is_connected then
                        if this.SignalConnectionFails.[s.Name].Count > 0 then
                            // Fictitiously set notified if connection restored but failure wasn't notified
                            this.SignalConnectionFails.[s.Name].Notified <- not(this.SignalConnectionFails.[s.Name].Notified)

                        this.SignalConnectionFails.[s.Name].Count <- 0
                    else
                        if
                            this.SignalConnectionFails.[s.Name].Count = 0 &&
                            this.SignalConnectionFails.[s.Name].Notified
                        then
                            this.SignalConnectionFails.[s.Name].Notified <- false

                        this.SignalConnectionFails.[s.Name].Count <- this.SignalConnectionFails.[s.Name].Count + 1
                elif is_connected then
                    // Fictitiously set notified for no signal history yet
                    this.SignalConnectionFails.Add(s.Name, {
                        SignalName = s.Name
                        IsComputed = is_computed
                        Count = 0
                        Limit = notification_condition.MaxConnectionFails
                        Notified = true
                    })
                else
                    this.SignalConnectionFails.Add(s.Name, { 
                        SignalName = s.Name
                        IsComputed = is_computed
                        Count = 1
                        Limit = notification_condition.MaxConnectionFails
                        Notified = false
                    })

                // Update signal value history
                if this.SignalValueFails.ContainsKey(s.Name) then
                    if is_alarming then
                        if
                            this.SignalValueFails.[s.Name].Count = 0 &&
                            this.SignalValueFails.[s.Name].Notified
                        then
                            this.SignalValueFails.[s.Name].Notified <- false

                        this.SignalValueFails.[s.Name].Count <- this.SignalValueFails.[s.Name].Count + 1
                    else
                        if this.SignalValueFails.[s.Name].Count > 0 then
                            // Fictitiously set notified if value restored but failure wasn't notified
                            this.SignalValueFails.[s.Name].Notified <- not(this.SignalValueFails.[s.Name].Notified)

                        this.SignalValueFails.[s.Name].Count <- 0

                elif is_alarming then
                    this.SignalValueFails.Add(s.Name, {
                        SignalName = s.Name
                        IsComputed = is_computed
                        Count = 1
                        Limit = notification_condition.MaxFails
                        Notified = false
                    })
                else
                    // Fictitiously set notified for no signal history yet
                    this.SignalValueFails.Add(s.Name, {
                        SignalName = s.Name
                        IsComputed = is_computed
                        Count = 0
                        Limit = notification_condition.MaxFails
                        Notified = true
                    })
                
                ()

            /// Populate signal tables
            member private this._LoadSignals (packet : byte []) =
                let mutable byte_index = 0
                let mutable bit_index = 7
                let mutable signal_index = 0

                for _s in this.Signals do
                    let s = _s.Value

                    // Extract signal value
                    let is_connected = ((U.extract_bit packet &byte_index &bit_index) <> 0uy)
                    let mutable signal_value = 0uy
                    let bit_count = 
                        match (s.Type) with
                        | IOSignal.TYPE.DISCRETE_INPUT | IOSignal.TYPE.COIL -> 1
                        | _ -> 16

                    for i = 0 to (bit_count - 1) do
                        signal_value <- ((signal_value <<< 1) ||| (U.extract_bit (packet) (&byte_index) (&bit_index)))

                    signal_index <- signal_index + 1

                    // Load signal value
                    s.UpdateValueOnly (is_connected, System.Convert.ToInt32(signal_value))

                    // Check notification conditions and update failure counts
                    let s_c =
                        server.Cfg.SmsAlarmNotificationConditions
                        |> Seq.filter (fun c -> c.SignalName = s.FqName)

                    if s_c.Count() > 0 then
                        let notification_condition = Seq.head s_c
                        let is_alarming = (s.RealValue < notification_condition.LowerBound || s.RealValue > notification_condition.UpperBound)
                        this._UpdateHistory s notification_condition is_connected is_alarming


                for _s in this.ComputedSignals do
                    let s = _s.Value

                    // Update signal value
                    s.UpdateValueOnlyNoDisplay ()

                    // Check notification conditions and update failure counts
                    let s_c =
                        server.Cfg.SmsAlarmNotificationConditions
                        |> Seq.filter (fun c -> c.SignalName = s.FqName)

                    if s_c.Count() > 0 then
                        let notification_condition = Seq.head s_c
                        let is_connected = true // For all computed signals
                        let is_alarming = (s.RealValue < notification_condition.LowerBound || s.RealValue > notification_condition.UpperBound)
                        this._UpdateHistory s notification_condition is_connected is_alarming

                ()

            /// Convert/load PLC communication row signal values into history stack.
            /// Extract signal values and connection status from the packet.
            /// Update connection and value failure tables.
            member private this._ReceiveSignals (packet : byte []) =
                match this.PlcConfiguration with
                | Some cfg -> 
                    if cfg.Id <> this.SignalTablesCfgId then this._InitSignals()
                    this._LoadSignals(packet)
                | None ->
                    cout.WriteLine(this.ClientName + ": Error, no configuration (yet)!")


            /// Notify operators this PLC now online
            member this.NotifyOnline () =
                if not(this.NotifiedAsOnline) && server.Cfg.SmsEnable then
                    U.cout (this.ClientName + ": Sending online notification")
                    server.SmsSender.SendMessage(System.String.Format(server.Cfg.SmsPlcOnlineFormat, this.UserInfo.Name))
                    this.NotifiedAsOnline <- true

            /// Notify operators this PLC offline
            member this.NotifyOffline () =
                if this.NotifiedAsOnline && server.Cfg.SmsEnable then
                    U.cout (this.ClientName + ": Sending offline notification")
                    server.SmsSender.SendMessage(System.String.Format(server.Cfg.SmsPlcOfflineFormat, this.UserInfo.Name))
                    this.NotifiedAsOnline <- false
            
            /// Notify operators of signal connection failure
            member private this._NotifyConnectionFail (s : IOSignal) =
                if server.Cfg.SmsEnable then
                    U.cout (sprintf "%s: Sending signal %s connection failure notification" (this.ClientName) (s.Name))
                    server.SmsSender.QueueMessage (System.String.Format(server.Cfg.SmsSignalDisconnectFormat, this.UserInfo.Name, s.Name))
            
            /// Notify operators of signal connection restore
            member private this._NotifyConnectionRestore (s : IOSignal) =
                if server.Cfg.SmsEnable then
                    U.cout (sprintf "%s: Sending signal %s connection restore notification" (this.ClientName) (s.Name))
                    server.SmsSender.QueueMessage (System.String.Format(server.Cfg.SmsSignalConnectedFormat, this.UserInfo.Name, s.Name))
            
            /// Notify operators of signal value alarm
            member private this._NotifyValueFail (s : IOSignal) =
                if server.Cfg.SmsEnable then
                    let s_val = (System.Math.Round(s.RealValue, server.Cfg.SmsSignalValuePrecision)).ToString()
                    U.cout (sprintf "%s: Sending notification about signal %s having value %s" (this.ClientName) (s.Name) (s_val))
                    server.SmsSender.QueueMessage (System.String.Format(server.Cfg.SmsSignalAlarmFormat, this.UserInfo.Name, s.Name, s_val))
            
            /// Notify operators of signal value restore
            member private this._NotifyValueRestore (s : IOSignal) =
                if server.Cfg.SmsEnable then
                    let s_val = (System.Math.Round(s.RealValue, server.Cfg.SmsSignalValuePrecision)).ToString()
                    U.cout (sprintf "%s: Sending notification about signal %s alarm end" (this.ClientName) (s.Name))
                    server.SmsSender.QueueMessage (System.String.Format(server.Cfg.SmsSignalOKFormat, this.UserInfo.Name, s.Name, s_val))


            /// Check new signal value communications and notify operator users of changes if necessary
            // TODO: tyhja andmebaasi puhul ka toime tulla, praegu errorid.
            member this.NotifySignalEvents () =                  
                // Send signal connection alarms
                let notify_connection_event (c : AlarmCounter)  = 
                    if not(c.Notified) && c.Limit > 0 then
                        if c.Count >= c.Limit then
                            if c.IsComputed 
                            then this._NotifyConnectionFail(this.ComputedSignals.Item(c.SignalName))
                            else this._NotifyConnectionFail(this.Signals.Item(c.SignalName))
                            c.Notified <- true
                        elif c.Count = 0 then
                            if c.IsComputed 
                            then this._NotifyConnectionRestore(this.ComputedSignals.Item(c.SignalName))
                            else this._NotifyConnectionRestore(this.Signals.Item(c.SignalName))
                            c.Notified <- true
                    ()

                let notify_value_event (c : AlarmCounter)  = 
                    if not(c.Notified) && c.Limit > 0 then
                        if c.Count >= c.Limit then
                            if c.IsComputed 
                            then this._NotifyValueFail(this.ComputedSignals.Item(c.SignalName))
                            else this._NotifyValueFail(this.Signals.Item(c.SignalName))
                            c.Notified <- true
                        elif c.Count = 0 then
                            if c.IsComputed 
                            then this._NotifyValueRestore(this.ComputedSignals.Item(c.SignalName))
                            else this._NotifyValueRestore(this.Signals.Item(c.SignalName))
                            c.Notified <- true
                    ()

                this.SignalConnectionFails
                |> Seq.iter (fun c -> notify_connection_event(c.Value))

                this.SignalValueFails
                |> Seq.iter (fun c -> notify_value_event(c.Value))

                ()


        // ======================================================================
        and OperatorUser (user_type:UserType, user_info:PlcDb.OperatorUser, user_session:PlcDb.Session, tcp_client:TcpClient, tcp_stream:Stream, server:PlcServer) as self =
            inherit User(user_type, user_info, user_session, tcp_client, tcp_stream, server)
            let send_latest_configuration (plc_id:int) (msg_id:int64) = 
                let msg =
                    if plc_id>=0 && plc_id<server.DbPlcUsers.Count then
                        let plc = server.DbPlcUsers.[plc_id]
                        if plc.Configurations.Count>0 then
                            let cfg = plc.Configurations.Last()
                            let _, row_cfg = U.msg_of_db_row_configuration plc_id cfg
                            (U.builder_with_response msg_id true "").SetResponseToQueryLatestRowPlcConfiguration(row_cfg).Build()
                        else
                            (U.builder_with_response msg_id false (sprintf "No configuration found for PlcId %d" plc_id)).Build()
                    else
                        (U.builder_with_response msg_id false (sprintf "No configuration found for PlcId %d" plc_id)).Build()
                self.SendToOperator(msg)
            // ControlPanel is expecting db range, etc.
            do
                let b = (U.builder_with_response self.NextQueryId true "")
                // 1. List of users.
                for u in server.DbPlcUsers do
                    let u_msg = (new PlcCommunication.RowUser.Builder()).SetId(u.Id).SetCreateDate(u.Created.Ticks).SetName(u.Name).SetType("PLC").SetIsPublic(u.IsPublic).Build()
                    b.AddOOBRowUsers(u_msg) |> ignore
                for u in server.DbOperatorUsers do
                    let u_msg = (new PlcCommunication.RowUser.Builder()).SetId(u.Id).SetCreateDate(u.Created.Ticks).SetName(u.Name).SetType("Operator").SetIsPublic(u.IsPublic).Build()
                    b.AddOOBRowUsers(u_msg) |> ignore
                // Finally - SEND
                let msg = b.Build()
                self.SendToOperator(msg)
                self.NextQueryId <- self.NextQueryId + 1L

            member val PlcsToMonitor:IList<int> = new List<int>() :> IList<int> with get,set

            member this.ProcessMessageFromOperator(msg:PlcCommunication.MessageToPlc) =
                // 1. First, is the request to be forwarded?
                if msg.HasForwardToPlcId then
                    for plc in server.PlcUsers.FindAll(fun p -> p.UserInfo.Id = msg.ForwardToPlcId) do
                        cout.WriteLine(this.ClientName + ": Forwarding message to user (id:" + (msg.ForwardToPlcId.ToString()) + ").")
                        let msg2 = (PlcCommunication.MessageToPlc.CreateBuilder(msg)).SetSourceUserId(user_info.Id).Build()
                        plc.SendToPlc(msg2)
                else
                    // Everything else.
                    // 2. Monitoring request.
                    if msg.MonitorUsersCount>0 then
                        let monitor_ids = msg.MonitorUsersList
                        this.PlcsToMonitor <- (seq { for u in server.DbPlcUsers do if monitor_ids.Contains(u.Id) then yield u.Id}).ToList()
                        for plc_id in this.PlcsToMonitor do
                            let plc_db = server.DbPlcUsers.[plc_id]
                            // Initial message: Range
                            let tail_id, head_id = if plc_db.Configurations.Count>0 then plc_db.QuerySignalValuesIdRange (plc_db.Configurations.Last()) else (0,0)
                            let msg_idrange = (new PlcCommunication.IdRange.Builder()).SetTailId(tail_id).SetHeadId(head_id).Build()
                            // Initial message: Configuration.
                            let plc_version, plc_config =
                                if plc_db.Configurations.Count>0 then
                                    let pc = plc_db.Configurations.Last()
                                    pc.Id, pc.SetupFile
                                else
                                    0, [| |]
                            let cfg = (new PlcCommunication.Configuration.Builder()).SetDeviceId(plc_db.PlcId).SetVersion(plc_version).SetConfigurationFile(Google.ProtocolBuffers.ByteString.CopyFrom(plc_config))
                            let msg = (new PlcCommunication.MessageFromPlc.Builder()).SetId(self.NextQueryId).SetOOBDatabaseRange(msg_idrange).SetOOBConfiguration(cfg).SetSourceId(plc_id).Build()
                            cout.WriteLine(this.ClientName + ": monitors PLC: " + (plc_id.ToString()) + ", id range=[" + (msg.OOBDatabaseRange.TailId.ToString()) + ".." + (msg.OOBDatabaseRange.HeadId.ToString()) + ").")
                            this.SendToOperator(msg)
                            // Can we send the latest row?
                            if head_id<>tail_id && plc_db.Configurations.Count>0 then
                                let original_id = head_id - 1
                                cout.WriteLine(this.ClientName + ": Sending latest row: " + (original_id.ToString()) + " from the PLC " + (plc_id.ToString()))
                                let sv_last = plc_db.QuerySignalValue1 (plc_db.Configurations.Last()) original_id
                                let msg_row = U.msg_of_db_row sv_last
                                let b = new PlcCommunication.MessageFromPlc.Builder()
                                b.SetOOBDatabaseRow(msg_row) |> ignore
                                b.SetSourceId(plc_id) |> ignore
                                b.SetId(-1L) |> ignore
                                this.SendToOperator(b.Build())
                    // 3. Database rows request.
                    let qrows = msg.QueryDatabaseRows
                    if qrows<>null && qrows.HasTailId && qrows.HasTailId then
                        if this.PlcsToMonitor.Count>0 || (msg.HasTargetPlcId && msg.TargetPlcId>=0 && msg.TargetPlcId<server.DbPlcUsers.Count) then
                            let plc_id = if msg.HasTargetPlcId then msg.TargetPlcId else this.PlcsToMonitor.[0]
                            let plc_db = server.DbPlcUsers.[plc_id]
                            if plc_db.Configurations.Count>0 then
                                let b = new PlcCommunication.MessageFromPlc.Builder()
                                b.SetId(msg.Id) |> ignore
                                b.SetSourceId(plc_id) |> ignore
                                let plc_cfg = plc_db.Configurations.Last()
                                let rows = plc_db.QuerySignalValues plc_cfg  (qrows.TailId) (qrows.HeadId)
                                let mutable nrows = 0
                                for r in rows do
                                    b.AddResponseToDatabaseRows(U.msg_of_db_row r) |> ignore
                                    nrows <- nrows + 1
                                this.SendToOperator(b.Build())
                                cout.WriteLine(this.ClientName + ": Requested rows: [" + qrows.TailId.ToString() + " .. " + qrows.HeadId.ToString() + "), sending " + nrows.ToString() + " rows.")
                            else
                                let b = U.builder_with_response (msg.Id) false "No configuration exists for me!"
                                b.SetSourceId(plc_id) |> ignore
                                this.SendToOperator(b.Build())
                                cout.WriteLine(this.ClientName + ": Requested rows: [" + qrows.TailId.ToString() + " .. " + qrows.HeadId.ToString() + "), but no configuration exists.")
                                ()
                        else
                            cout.WriteLine(this.ClientName + ": Not responding to the database rows query because there are no plcs under monitoring.")
                    // 4. User configuration request.
                    if msg.HasQueryLatestRowPlcConfiguration then
                        send_latest_configuration msg.QueryLatestRowPlcConfiguration msg.Id
                    // 5. Request to set new configuration.
                    if msg.HasNewRowPlcConfiguration then
                        let src_cfg = msg.NewRowPlcConfiguration
                        if src_cfg.HasId && src_cfg.HasUserId && src_cfg.HasCreateDate && src_cfg.HasVersion && src_cfg.HasConfigurationFile then
                            let src_cfg_file, src_prefs = src_cfg.ConfigurationFile.ToByteArray(), src_cfg.Preferences.ToByteArray()
                            let plc_id = src_cfg.UserId
                            if plc_id>=0 && plc_id<server.DbPlcUsers.Count then
                                let plc_db = server.DbPlcUsers.[plc_id]
                                let cfg_index = plc_db.Configurations.FindIndex(fun dbc -> (U.are_bytes_equal src_cfg_file (dbc.SetupFile)) && (U.are_bytes_equal src_prefs (dbc.Preferences)))
                                if cfg_index<0 then
                                    let new_cfg = new PlcDb.Configuration(SetupFile=src_cfg_file, Preferences=src_prefs)
                                    plc_db.InsertConfiguration(new_cfg)
                                    cout.WriteLine(this.ClientName + ": New configuration for (user id:" + (src_cfg.UserId.ToString()) + "): id=" + (new_cfg.Id.ToString()) + ", file: " + (src_cfg_file.Length.ToString()) + "bytes, prefs:" + (src_prefs.Length.ToString()) + " bytes.")
                                    // Send back new configuration :)
                                    send_latest_configuration src_cfg.UserId msg.Id
                                    for u in server.PlcUsers do
                                        if u.UserInfo.Id = src_cfg.UserId then
                                            u.SendLatestConfigurationIfAny()
                                else
                                    let old_cfg = plc_db.Configurations.[cfg_index]
                                    cout.WriteLine(this.ClientName + ": Configuration for user (id:" + (src_cfg.UserId.ToString()) + ") already seen, id: " + (old_cfg.Id.ToString()) )
                            else
                                cout.WriteLine(this.ClientName + ": Configuration change, however, the user (id:" + (src_cfg.UserId.ToString()) + ") is nowhere to be found!!")
                        else
                            cout.WriteLine(this.ClientName + ": Configuration for user (id:" + (src_cfg.UserId.ToString()) + ") is incomplete!")
                    if msg.HasQueryDbRange && msg.QueryDbRange.HasFirstTimeTicks && msg.HasId then
                        let q = msg.QueryDbRange
                        if msg.HasTargetPlcId then
                            let plc_id = msg.TargetPlcId
                            if plc_id>=0 && plc_id<server.DbPlcUsers.Count then
                                let plc_db = server.DbPlcUsers.[plc_id]
                                if plc_db.Configurations.Count>0 then
                                    let plc_cfg = plc_db.Configurations.Last()
                                    let first_time = new DateTime(q.FirstTimeTicks)
                                    let last_time = if q.HasLastTimeTicks then Some (new DateTime(q.LastTimeTicks)) else None
                                    let first_id, last_id = plc_db.QuerySignalValuesIdsByTime plc_cfg first_time last_time
                                    let id_range = if (not (first_id=0 && last_id=0)) then U.idrange_of first_id (last_id + 1) else U.idrange_of 0 0
                                    let rb = U.builder_with_response msg.Id true ""
                                    rb.SetSourceId(plc_id) |> ignore
                                    rb.SetResponseToDbRangeQuery(id_range) |> ignore
                                    cout.WriteLine(this.ClientName + ": QueryDbRange " + (first_time.ToString()) + " .. " + (match last_time with | Some x -> x.ToString()| None -> "_end") + " => " + (id_range.TailId.ToString()) + " .. " + (id_range.HeadId.ToString()) + ".")
                                    let reply = rb.Build()
                                    this.SendToOperator(reply)
                                else
                                    let reply = (U.builder_with_response msg.Id false (sprintf "Target PLC %d has no configuration!" plc_id)).Build() 
                                    cout.WriteLine(this.ClientName + (sprintf ": QueryRange: Target PLC %d has no configuration." plc_id))
                                    this.SendToOperator reply
                            else
                                let reply = (U.builder_with_response msg.Id false (sprintf "Target PLC %d not found!" plc_id)).Build() 
                                cout.WriteLine(this.ClientName + (sprintf ": QueryRange: Target PLC %d not found." plc_id))
                                this.SendToOperator reply
                        else
                            let reply = (U.builder_with_response msg.Id false (sprintf "No Target PLC specified")).Build() 
                            cout.WriteLine(this.ClientName + ": QueryRange: No target PLC specified.")
                            this.SendToOperator reply
                ()
        // ======================================================================
        and PlcServer (cfg:ServerConfiguration) as self =
            let db_plcs = PlcDb.PlcUser.FetchAll cfg.DataDirectory
            let db_operators = PlcDb.OperatorUser.FetchAll cfg.DataDirectory
            let processor = new MailboxProcessor<MailboxMessageToServer>(self.mailbox_loop)

            let sms_file_logger (message:string) =
                let log_file = cfg.SmsLogFilename
                let stream = new StreamWriter(log_file, true)
                stream.AutoFlush <- true
                stream.WriteLine(sprintf "%s: %s" (System.DateTime.Now.ToString("s")) message)
                stream.Dispose()

            let delete_old_plc_data (plc_db:PlcDb.PlcUser) (delete_time:DateTime) =
                let this_round = plc_db.DeleteSignalValuesOlderThan(delete_time)
                if this_round>0 then
                    cout.WriteLine(sprintf "%s: Deleted %d old records." (plc_db.Name) this_round)
                ()

            do
                // 1. Enlist the PLC-s.
                cout.Write("PLCs: ");
                for plc in db_plcs do
                    cout.Write("'" + plc.Name + "' (" + plc.PlcId + ") ")
                cout.WriteLine()
                cout.Write("Operators: ")
                for o in db_operators do
                    cout.Write("'" + o.Name + "' ")
                cout.WriteLine()

                // 2. Delete old data.
                let delete_time = DateTime.Now.Subtract(ARCHIVE_TIMESPAN)
                for plc in db_plcs do
                    delete_old_plc_data plc delete_time

                // 3. List SMS receivers
                cout.Write("SMS recipients: ");
                for nr in cfg.SmsReceiverPhoneNrs do
                    cout.Write("'" + nr + "' ")
                cout.WriteLine()

                // 4. List notification conditions
                Debug.WriteLine("Notification conditions: ");
                for nc in cfg.SmsAlarmNotificationConditions do
                    Debug.WriteLine(nc.LowerBound.ToString() + " < '" + nc.SignalName + "' < " + nc.UpperBound.ToString())
                Debug.WriteLine("");

            member val Cfg:ServerConfiguration = cfg with get
            member val PlcUsers:List<PlcUser> = new List<PlcUser>() with get
            member val OperatorUsers:List<OperatorUser> = new List<OperatorUser>() with get
            member val DbPlcUsers:List<PlcDb.PlcUser> = db_plcs with get
            member val DbOperatorUsers:List<PlcDb.OperatorUser> = db_operators with get
            member val NextPingTime = ref (DateTime.Now.AddMilliseconds(float PING_PERIOD_MS))
            member val SmsSender : SmsMessenger.SmsSender = new SmsMessenger.SmsSender (cfg.SmsModemPort, cfg.SmsReceiverPhoneNrs, sms_file_logger, cfg.SmsSize, cfg.SmsMessageSeparator) with get
            member val StartupTime : System.DateTime = System.DateTime.Now with get

            member this.Start() = processor.Start()
            member this.Post(msg:MailboxMessageToServer) = processor.Post(msg)
            member this.PostAndReply<'Reply>(buildMessage:(AsyncReplyChannel<'Reply> -> MailboxMessageToServer)) = processor.PostAndReply(buildMessage)

            member private this.ProcessStart(client_name:string, msg:MessageFromPlc, tcp_client:TcpClient, stream:Stream, endpoint:EndPoint, reply:AsyncReplyChannel<User option>) =
                let reply_msg = ref None
                try
                    let session =
                        let now = DateTime.Now
                        let s_ip = endpoint.ToString()
                        let s_idx = s_ip.IndexOf(':')
                        new PlcDb.Session(StartTime = now, EndTime = now, IpAddress = s_ip.Substring(0, s_idx), IsOpen = true)
                    if msg.HasOOBConfiguration then
                        let cfg = msg.OOBConfiguration
                        // PLC?
                        if cfg.HasDeviceId && cfg.HasVersion && cfg.HasConfigurationFile then
                            // 1. Is the device known?
                            let plc_id = this.DbPlcUsers.FindIndex (fun plc -> cfg.DeviceId = plc.PlcId)
                            if plc_id>=0 then
                                let plc_db = this.DbPlcUsers.[plc_id]
                                delete_old_plc_data plc_db (DateTime.Now.Subtract(ARCHIVE_TIMESPAN))
                                plc_db.InsertSession session
                                let new_user = new PlcUser(UserType.PLC, plc_db, session, tcp_client, stream, this)
                                new_user.Open()
                                reply_msg := Some (new_user :> User)
                                this.PlcUsers.Add(new_user)
                                cout.WriteLine(client_name + ": User is PLC: " + plc_db.Name)
                                new_user.SendLatestConfigurationIfAny()
                            else
                                cout.WriteLine(client_name + ": DeviceId unknown for the PLC: " + cfg.DeviceId)
                        // User
                        else if cfg.HasDeviceId && cfg.HasPassword then
                            cout.WriteLine(client_name + ": OOB Configuration received, User Name=" + cfg.DeviceId)
                            let user_index = this.DbOperatorUsers.FindIndex (fun u -> u.Name = cfg.DeviceId)
                            if user_index>=0 && this.DbOperatorUsers.[user_index].PasswordHash=cfg.Password then
                                let user_info = this.DbOperatorUsers.[user_index]
                                let new_user = new OperatorUser(UserType.Operator, user_info, session, tcp_client, stream, this)
                                new_user.Open()
                                reply_msg := Some (new_user :> User)
                                this.OperatorUsers.Add(new_user)
                                cout.WriteLine(client_name + ": User is Operator: " + user_info.Name)
                            else
                                cout.WriteLine(client_name + ": Invalid password supplied for the Operator: " + cfg.DeviceId)
                            ()
                    // The message might contain additional information...
                    match !reply_msg with
                    | Some u -> if u.UserType=UserType.PLC then (u :?> PlcUser).ProcessMessageFromPlc(msg)
                    | None -> ()
                with
                    | ex -> cout.WriteLine("Error: " + ex.Message)
                reply.Reply(!reply_msg)

            member private this.ProcessMessageFromPlc(user:PlcUser, msg:MessageFromPlc) =
                try
                    // 1. Process it ourselves.
                    user.ProcessMessageFromPlc(msg)
                    
                    // 2. Forward it to the operators.
                    let source_id = user.UserInfo.Id
                    let new_msg = PlcCommunication.MessageFromPlc.CreateBuilder(msg).SetSourceId(source_id).Build()
                    for o in this.OperatorUsers do
                        if o.PlcsToMonitor.Contains(source_id) then
                            cout.WriteLine(user.ClientName + ": Message forwarded to: " + o.ClientName)
                            o.SendToOperator(new_msg)
                        else
                            cout.WriteLine(user.ClientName + ": Message NOT forwarded to: " + o.ClientName + ", monitoring " + (sprintf "%A" o.PlcsToMonitor) + ".")
                with
                    | ex -> cout.WriteLine(user.ClientName + ": Message processing: " + ex.Message)

            member private this.ProcessMessageFromOperator(user:OperatorUser, msg:MessageToPlc) =
                try
                    user.ProcessMessageFromOperator(msg)
                with
                    | ex -> cout.WriteLine(user.ClientName + ": Message processing: " + ex.Message)

            member private this.ProcessFinish(user:User) =
                try
                    user.UserInfo.UpdateSessionEnd(user.UserSession)
                with
                    | ex -> cout.WriteLine(user.ClientName + ": Cannot close the session: " + ex.Message)
                match user.UserType with
                | UserType.PLC ->
                    this.PlcUsers.Remove(user :?> PlcUser) |> ignore
                    (user :?> PlcUser).NotifyOffline()
                | UserType.Operator -> this.OperatorUsers.Remove(user :?> OperatorUser) |> ignore
                user.Close()

            member private this.ProcessDie() =
                for o in this.OperatorUsers do
                    try
                        o.Close()
                    with ex -> cout.WriteLine(o.ClientName + ": Error when closing: " + ex.Message)
                for p in this.PlcUsers do
                    try
                        p.Close()
                    with ex -> cout.WriteLine(p.ClientName + ": Error when closing: " + ex.Message)
                // ok, shut it down! :)
                cout.WriteLine("PlcMaster: finished.")

            member private this.PingAllClients() =
                // 1. Ping all operators.
                let operator_ping = (new PlcCommunication.MessageFromPlc.Builder()).SetId(-1L).Build()
                for o in this.OperatorUsers do
                    try
                        o.SendToOperator(operator_ping)
                    with
                        | ex -> cout.WriteLine(o.ClientName + ": Ping error: " + ex.Message)
                // 2. Ping all PLC-s.
                let plc_ping = (new PlcCommunication.MessageToPlc.Builder()).SetId(-1L).Build()
                for plc in this.PlcUsers do
                    try
                        plc.SendToPlc(plc_ping)
                    with
                        | ex ->
                            cout.WriteLine(plc.ClientName + ": Ping error: " + ex.Message)

                this.NextPingTime := DateTime.Now.AddMilliseconds(float PING_PERIOD_MS)
                cout.WriteLine("Ping round completed.")
                ()

            member private this.CheckPlcUserConnections() =
                for plc in this.PlcUsers do
                    if this.StartupTime.AddSeconds(PLC_ONLINE_NOTIFICATION_AFTER_S) < DateTime.Now then
                        // check database for signals in past n seconds, if not found then declare offline
                        let time = DateTime.Now.Subtract(new TimeSpan(0, 0, PLC_OFFLINE_NOTIFICATION_AFTER_S))
                        let db_plc = db_plcs.[plc.UserInfo.Id]
                        let c = db_plc.QuerySignalCountFrom time
                        if c > 0 then
                            plc.NotifyOnline()
                        else
                            plc.NotifyOffline()
                    

            member this.mailbox_loop (inbox:MailboxProcessor<MailboxMessageToServer>) =
                let rec loop () =
                    async {
                        let now = DateTime.Now
                        let time_to_ping = ref (int ((! this.NextPingTime).Subtract(now).TotalMilliseconds))
                        if !time_to_ping < 0 then
                            this.PingAllClients()
                            this.CheckPlcUserConnections()
                            if this.Cfg.SmsEnable then this.SmsSender.SendQueued()
                            time_to_ping := int ((! this.NextPingTime).Subtract(now).TotalMilliseconds)
                        try
                            let! msg_to_server = inbox.Receive( !time_to_ping )
                            match msg_to_server with
                            | MailboxMessageToServer.Start (client_name, msg, tcp_client, stream, endpoint, reply) -> this.ProcessStart(client_name, msg, tcp_client, stream, endpoint, reply)
                            | MailboxMessageToServer.ProcessMessageFromPlc (user, msg) -> this.ProcessMessageFromPlc(user, msg)
                            | MailboxMessageToServer.ProcessMessageFromOperator (user, msg) -> this.ProcessMessageFromOperator(user, msg)
                            | MailboxMessageToServer.Finish (user) -> this.ProcessFinish(user)
                            | MailboxMessageToServer.Die ->
                                this.ProcessDie()
                                return ()
                        with
                        | :? TimeoutException ->
                            this.PingAllClients()
                            this.CheckPlcUserConnections()
                            if this.Cfg.SmsEnable then this.SmsSender.SendQueued()
                        return! loop()
                    }
                loop ()
        // ======================================================================
        let serve_user (param: obj) =
            let client,server = param :?> (TcpClient * PlcServer)
            let endpoint = client.Client.RemoteEndPoint
            let client_name = ref (endpoint.ToString())
            let stream = client.GetStream()
            let mutable read_ok = true
            let mutable user :User option = None
            try
                try
                    // 1st. message is different for the Operator.
                    while client.Connected && read_ok && user.IsNone do
                        let msg = MessageFromPlc.ParseDelimitedFrom(stream)
                        if msg.Command = COMMAND.DROP_CONNECTION then
                            cout.WriteLine(!client_name + ": Received drop connection command.")
                            read_ok <- false
                        else
                            let reply = server.PostAndReply(fun rch -> MailboxMessageToServer.Start(!client_name, msg, client, stream :> Stream, endpoint, rch))
                            match reply with
                            // Success!
                            | Some u ->
                                begin
                                    user <- reply
                                    client_name := u.ClientName
                                end
                            // Fail auth!
                            | None -> read_ok <- false
                    // Remaining messages.
                    match user with
                    | Some u ->
                        begin
                            match u.UserType with
                            | UserType.PLC ->
                                begin
                                    let plc_user = u :?> PlcUser
                                    while client.Connected && read_ok do
                                        let msg = MessageFromPlc.ParseDelimitedFrom(stream)
                                        if msg.Command = COMMAND.DROP_CONNECTION then
                                            cout.WriteLine(!client_name + ": Received drop connection command.")
                                            read_ok <- false
                                        else
                                            server.Post(MailboxMessageToServer.ProcessMessageFromPlc(plc_user, msg))
                                end
                            | UserType.Operator ->
                                begin
                                    let operator_user = u :?> OperatorUser
                                    while client.Connected && read_ok do
                                        let msg = MessageToPlc.ParseDelimitedFrom(stream)
                                        server.Post(MailboxMessageToServer.ProcessMessageFromOperator(operator_user, msg))
                                end
                        end
                    | None -> ()
                with
                    | ex -> cout.WriteLine(!client_name + ": Error: " + (ex.Message))
            finally
                match user with
                | Some u -> server.Post(MailboxMessageToServer.Finish(u))
                | None -> ()
            client.Close()
            cout.WriteLine(!client_name + ": Finished serving.")
            ()

        // ======================================================================
        let serve_incoming_connections (param: obj) =
            let listener,server = param :?> (TcpListener * PlcServer)
            try
                // Socket listening loop.
                while true do
                    let client = listener.AcceptTcpClient()
                    let client_name = client.Client.RemoteEndPoint.ToString()
                    cout.WriteLine(client_name + ": New client.")
                    let th = new Thread(new ParameterizedThreadStart(serve_user))
                    th.Start( (client, server))
                    ()
                ()
            with
                | ex -> cout.WriteLine ("Error: " + ex.Message)
            ()

    // ======================================================================
    type PlcServerFacade() =
        let ipAddress = IPAddress.Any
        let port = 1503
        let mutable ctx = None
        // Start this nice server :)
        member this.Start() =
            // 1. Open all the databases.
            // 1. Connect to the DB.
            // 1.1. Read server configuration
            let cfg =
                try
                    let s = File.ReadAllText PlcServer.config_filename
                    U.unjson<ServerConfiguration> s
                with
                | ex ->
                    PlcServer.cout.WriteLine("PlcServerFacade: Cannot read configuration file '" + (PlcServer.config_filename) + "': " + ex.Message)
                    let r = new ServerConfiguration()
                    try
                        let s_out = U.json<ServerConfiguration> r
                        File.WriteAllText(PlcServer.config_filename, s_out)
                    with
                    | ex2 ->
                        PlcServer.cout.WriteLine("PlcServerFacade: Cannot write default configuration file '" + (PlcServer.config_filename) + "': " + ex.Message)
                    r

            // 2. Create the server...
            let plc_server = new PlcServer.PlcServer(cfg)
            // 3. Open the TCP/IP port for listening.
            let listener = TcpListener(ipAddress, port)
            listener.Start()
            // 4. Start the PlcServer
            plc_server.Start()
            // 5. Create a thread for the listener.
            let listener_th = new Thread(new ParameterizedThreadStart(PlcServer.serve_incoming_connections))
            listener_th.Start( (listener, plc_server))
            ctx <- Some (listener, listener_th, plc_server)

        // Shutdown our server.
        member this.Stop()=
            match ctx with
            | Some (listener, listener_th, plc_server) ->
                // 1. Stop listening on the socket.
                try
                    listener.Server.Close()
                with ex -> PlcServer.cout.WriteLine("Error when closing the listener: " + ex.Message)
                // 2. Send stop message to the server.
                try
                    plc_server.Post(PlcServer.MailboxMessageToServer.Die)
                with ex -> PlcServer.cout.WriteLine("Error when stopping PlcServer: " + ex.Message)
            | None -> ()

    // ======================================================================
    type public PlcService() as this = 
        inherit System.ServiceProcess.ServiceBase()
        do
            this.ServiceName <- "PlcServer" 
            this.AutoLog <- true
            this.EventLog.Source <- "PlcServerSource"
            if not (System.Diagnostics.EventLog.SourceExists("PlcServerSource")) then
                System.Diagnostics.EventLog.CreateEventSource("PlcServerSource", "Application")
        member val PlcServer = new PlcServerFacade() with get,set
        override this.OnStart(args:string[]) = 
            try
                this.EventLog.WriteEntry("Starting, logfile: '" + PlcServer.log_filename + "', current directory:'" + System.IO.Directory.GetCurrentDirectory() + "'.")
                let sw = new System.IO.StreamWriter(PlcServer.log_filename, (*append=*)true);
                sw.AutoFlush <- true
                PlcServer.cout <- sw
                PlcServer.cout.Flush()
                this.PlcServer.Start()
            with ex ->
                PlcServer.cout.WriteLine("Error: " + ex.Message)
                PlcServer.cout.Flush()
                this.EventLog.WriteEntry("Failed: " + ex.Message)
                PlcServer.cout.Close()

        override this.OnStop() = 
            this.PlcServer.Stop()
            PlcServer.cout.Flush()
            PlcServer.cout.Close()
            PlcServer.cout <- System.Console.Out
            this.EventLog.WriteEntry("Stopped.")

    // ======================================================================
    [<System.ComponentModel.RunInstaller(true)>] 
    type public PlcServiceInstaller() as this = 
        inherit System.Configuration.Install.Installer() 
        do 
            let spi = new System.ServiceProcess.ServiceProcessInstaller() 
            let si = new System.ServiceProcess.ServiceInstaller() 
            spi.Account <- System.ServiceProcess.ServiceAccount.LocalSystem 
            spi.Username <- null 
            spi.Password <- null

            si.DisplayName <- "PlcServer service" 
            si.StartType <- System.ServiceProcess.ServiceStartMode.Automatic 
            si.ServiceName <- "PlcServer"
            si.Description <- "Server for the PLC system."

            this.Installers.Add(spi) |> ignore 
            this.Installers.Add(si) |> ignore

    // ======================================================================
    module PlcServerEntryPoint =
        [<EntryPoint>]
        let Main argv =
            let mutable run_it = false
            let mutable skip_count = 0
            for i=0 to argv.Length-1 do
                if skip_count > 0 then
                    skip_count <- skip_count - 1
                else
                    let a = argv.[i]
                    if a="--run" then
                        run_it <- true
                    elif a="--config" && i+1<argv.Length then
                        skip_count <- 1
                        PlcServer.config_filename <- argv.[i+1] 
            if run_it then
                PlcServer.cout.WriteLine("PlcServer: starting.")
                PlcServer.cout.WriteLine("Configuration file: " + PlcServer.config_filename)
                let server = new PlcServerFacade()
                server.Start()
                PlcServer.cout.WriteLine("PlcServer: started!")
                while true do
                    System.Threading.Thread.Sleep(3600* 1000)
                server.Stop()
            else
                // the service.
                let service = new PlcService()
                System.ServiceProcess.ServiceBase.Run(service) 
            0
