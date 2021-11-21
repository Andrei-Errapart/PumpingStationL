namespace PlcServer
    open System
    open System.Collections.Generic
    open System.Data.Linq.SqlClient
    open System.IO
    open System.Linq
    open System.Net
    open System.Net.Sockets
    open System.Runtime.Serialization
    open System.Text.RegularExpressions
    open System.Threading
    open Microsoft.FSharp.Data.TypeProviders
    open Microsoft.FSharp.Linq
    open PlcCommunication

    // ======================================================================
    module PlcServer =
        let MAX_BATCH_SIZE = 100
        type schema = SqlDataConnection<"Data Source=.\SqlExpress;Initial Catalog=PlcMaster;Integrated Security=True">

        let mutable cout = System.Console.Out
        // Log filename for the PlcService
        let log_filename = [| System.IO.Path.GetTempPath(); "PlcServer-log.txt" |] |> System.IO.Path.Combine
        let config_filename = "C:\\PlcServerService\\PlcServer.ini"
        let _RefTimeJava = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)

        // ======================================================================
        /// Object to Json 
        let internal json<'t> (myObj:'t) =   
                use ms = new System.IO.MemoryStream() 
                (new Json.DataContractJsonSerializer(typeof<'t>)).WriteObject(ms, myObj) 
                System.Text.Encoding.Default.GetString(ms.ToArray()) 

        // ======================================================================
        /// Object from Json 
        let internal unjson<'t> (jsonString:string)  : 't =  
                use ms = new System.IO.MemoryStream(System.Text.ASCIIEncoding.Default.GetBytes(jsonString)) 
                let obj = (new Json.DataContractJsonSerializer(typeof<'t>)).ReadObject(ms)
                obj :?> 't

        // ======================================================================
        let DateTimeOfJavaMilliseconds (ms:int64) =
            // this two-step process should preserve precision :)
            let minutes = ms / 60000L
            let milliseconds = ms - minutes * 60000L;
            let r = _RefTimeJava.AddMinutes(float minutes).AddMilliseconds(float milliseconds).ToLocalTime();
            r

        // ======================================================================
        let JavaMillisecondsOfDateTime (dt:DateTime) =
            let utc = dt.ToUniversalTime()
            let ts = utc.Subtract(_RefTimeJava)
            let minutes = int64 (ts.TotalMinutes)
            let milliseconds = ts.Seconds * 1000 + ts.Milliseconds
            minutes * 60000L + (int64 milliseconds)

        // ======================================================================
        let idrange_of (id_tail:int) (id_head:int) =
            (new IdRange.Builder()).SetHeadId(id_head).SetTailId(id_tail).Build()

        // ======================================================================
        let query_db_range (out:Stream) (id:int64) ((id_tail:int),(id_head:int)) =
            let msg = (new MessageToPlc.Builder()).SetId(id).SetQueryDatabaseRows(idrange_of id_tail id_head).Build()
            msg.WriteDelimitedTo(out)
            ()

        // ======================================================================
        let enqueue_range (q:Queue<int*int>) ((r_tail_id:int),(r_head_id:int)) =
            let n = (r_head_id - r_tail_id + MAX_BATCH_SIZE - 1) / MAX_BATCH_SIZE
            // cout.WriteLine(client_name + ": Initializing query in the range [" + r_tail_id.ToString() + " .. " + r_head_id.ToString() + ") in " + n.ToString() + " batches.")
            for i=0 to n-1 do
                let x_tail_id = r_tail_id + i * MAX_BATCH_SIZE
                let x_head_id = Math.Min(r_head_id, x_tail_id + MAX_BATCH_SIZE)
                q.Enqueue (x_tail_id, x_head_id)
            ()

        // ======================================================================
        let is_plc_id (db_json:string) (id:string) =
            let dbx = FsJson.parse db_json
            let plc_id = dbx ? PlcId
            if plc_id.IsNotNull && plc_id.Val<>null then
                plc_id.Val = id
            else
                false

        // ======================================================================
        let is_password_ok (db_json:string) (password:string) =
            // TODO: use the password hash correctly.
            let dbx = FsJson.parse db_json
            let u_password = dbx ? Password
            let u_hash = dbx ? PasswordHash
            if u_password.IsNotNull && u_password.Val<>null && u_hash.IsNotNull && u_hash.Val<>null then
                u_password.Val = password
            else
                false
        // ======================================================================
        let my_next_uid = ref 0
        let get_next_uid () =
            let r = !my_next_uid
            my_next_uid := !my_next_uid + 1
            r

        let builder_with_response (id:int64) (ok:bool) (msg:string) =
            (new PlcCommunication.MessageFromPlc.Builder()).SetId(id).SetResponse((new PlcCommunication.Response.Builder()).SetOK(ok).SetMessage(msg))

        // ======================================================================
        let are_bytes_equal (a1:byte[]) (a2:byte[]) =
            let l1,l2 = a1.Length, a2.Length
            if l1=l2 then
                let mutable eq = true
                for i=0 to l1-1 do
                    eq <- eq && a1.[i]=a2.[i]
                eq
            else
                false

        // ======================================================================
        // Protocol message from the row of SignalValues.
        let msg_of_db_row (db_sv:schema.ServiceTypes.SignalValue) =
            let bytes = db_sv.SignalValues.ToArray()
            let msg = (new PlcCommunication.DatabaseRow.Builder()).SetRowId(db_sv.OriginalId).SetVersion(db_sv.PlcConfiguration.Version).SetTimeMs(JavaMillisecondsOfDateTime (db_sv.TimeStamp)).SetSignalValues(Google.ProtocolBuffers.ByteString.CopyFrom(bytes))
            msg

        // ======================================================================
        type ServerConfiguration() =
            member val DatabaseConnection = "" with get, set

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
        and User (user_type:UserType, user_info:schema.ServiceTypes.User, user_session:schema.ServiceTypes.UserSession, tcp_client:TcpClient, tcp_stream:Stream, server:PlcServer) as self =
            let processor = new MailboxProcessor<MailboxMessageToUser>(self.mailbox_loop)
            member this.mailbox_loop (inbox:MailboxProcessor<MailboxMessageToUser>) =
                let rec loop () =
                    async {
                        let! msg_to_user = inbox.Receive()
                        match msg_to_user with
                        | MailboxMessageToUser.SendMessageToPlc (msg) ->
                            try
                                msg.WriteDelimitedTo(tcp_stream)
                            with 
                                | ex ->
                                    cout.WriteLine(self.ClientName + ": Cannot send MessageToPlc: " + ex.Message)
                                    return ()
                            return! loop ()
                        | MailboxMessageToUser.SendMessageToOperator (msg) ->
                            try
                                msg.WriteDelimitedTo(tcp_stream)
                            with 
                                | ex ->
                                    cout.WriteLine(self.ClientName + ": Cannot send MessageToOperator: " + ex.Message)
                                    return ()
                            return! loop ()
                        | MailboxMessageToUser.Finish  ->
                            return ()
                    }
                loop ()
            member this.Open () = processor.Start()
            member val UserType = user_type with get,set
            member val UserInfo = user_info with get,set
            member val UserSession = user_session with get,set
            // Name of the client.
            member val ClientName = user_info.Name + "(" + ((get_next_uid ()).ToString()) + ")" with get
            member val NextQueryId = 1L with get,set
            // Send message down the wire.
            member this.SendToPlc (msg:PlcCommunication.MessageToPlc) = processor.Post(MailboxMessageToUser.SendMessageToPlc(msg))
            member this.SendToOperator (msg:PlcCommunication.MessageFromPlc) = processor.Post(MailboxMessageToUser.SendMessageToOperator(msg))
            // Latest configuration, if any.
            member this.LatestRowPlcConfiguration (user_id:int) =
                let c = query { for plc_cfg in server.DB.PlcConfiguration do where (plc_cfg.UserId=user_id); sortByDescending plc_cfg.Version; select plc_cfg; headOrDefault }
                if c=null then
                    null, null
                else
                    let cfg_file, cfg_preferences = c.ConfigurationFile.ToArray(), c.Preferences.ToArray()
                    let row_cfg = (new PlcCommunication.RowPlcConfiguration.Builder()).
                                    SetId(c.Id).SetUserId(c.UserId).SetUserSessionId(c.UserSessionId).SetVersion(c.Version).SetCreateDate(c.CreateDate.Ticks).
                                    SetConfigurationFile(Google.ProtocolBuffers.ByteString.CopyFrom(cfg_file)).
                                    SetPreferences(Google.ProtocolBuffers.ByteString.CopyFrom(cfg_preferences)).Build()
                    c, row_cfg

            // Stop the mailbox servicing this user.
            member this.Close() =
                processor.Post(MailboxMessageToUser.Finish)
                try
                    tcp_client.Client.Close()
                with ex -> cout.WriteLine(this.ClientName + ": Error when closing: " + ex.Message)
        // ======================================================================
        and PlcUser (user_type:UserType, user_info:schema.ServiceTypes.User, user_session:schema.ServiceTypes.UserSession, tcp_client:TcpClient, tcp_stream:Stream, server:PlcServer) as self =
            inherit User(user_type, user_info, user_session, tcp_client, tcp_stream, server)
            let rows_to_fetch = new Queue<int*int>()
            let query_next_rows_if_any () =
                if rows_to_fetch.Count>0 then
                    let range = rows_to_fetch.Dequeue()
                    query_db_range tcp_stream self.NextQueryId range
                    self.NextQueryId <- self.NextQueryId + 1L
                ()
            let insert_row_skip_submit (plc_row:DatabaseRow) =
                let plc_cfg = self.PlcConfiguration
                if plc_cfg=null then
                    cout.WriteLine(self.ClientName + ": Skipped signal values, because there is no configuration (yet).")
                else
                    let some_ts =
                        try Some (DateTimeOfJavaMilliseconds plc_row.TimeMs)
                        with ex ->
                            cout.WriteLine(self.ClientName + ": Invalid timestamp (" + (plc_row.TimeMs.ToString()) + ") for the row (id:"  + (plc_row.RowId.ToString()) + ")." )
                            None
                    match some_ts with
                    | Some ts ->
                        let new_row = new schema.ServiceTypes.SignalValue(
                                                    UserId = user_info.Id,
                                                    UserSessionId = user_session.Id,
                                                    PlcConfigurationId = plc_cfg.Id,
                                                    OriginalId = plc_row.RowId,
                                                    TimeStamp = DateTimeOfJavaMilliseconds plc_row.TimeMs,
                                                    SignalValues = new Data.Linq.Binary(plc_row.SignalValues.ToArray()))
                        server.DB.SignalValue.InsertOnSubmit(new_row)
                    | None -> ()
            // Latest configuration, if any.
            member val PlcConfiguration:schema.ServiceTypes.PlcConfiguration = (null :> schema.ServiceTypes.PlcConfiguration) with get,set
            // Process the messages.
            member this.ProcessMessageFromPlc(msg:PlcCommunication.MessageFromPlc) =
                if msg.HasOOBConfiguration then
                    // TODO: is it needed anymore?
                    // Store the configuration in the configuration file.
                    let cfg = msg.OOBConfiguration
                    if cfg.HasDeviceId && cfg.HasVersion && cfg.HasConfigurationFile then
                        let cfg_bytes = cfg.ConfigurationFile.ToByteArray()
                        let list_of_cfgs = (query { for c in user_info.PlcConfiguration do sortBy c.Version; select c }).ToArray()
                        let first_match = (seq { for dbc in list_of_cfgs do if are_bytes_equal cfg_bytes (dbc.ConfigurationFile.ToArray()) then yield dbc }).FirstOrDefault()
                        if first_match=null then
                            cout.WriteLine(this.ClientName + ": The configuration is different from the one in the server database.")
                            let new_version = if list_of_cfgs.Count() = 0 then 0 else (list_of_cfgs.Last().Version + 1)
                            let new_cfg = new schema.ServiceTypes.PlcConfiguration(
                                                        UserId=user_info.Id,
                                                        UserSessionId = user_session.Id,
                                                        CreateDate = DateTime.Now,
                                                        Version = new_version,
                                                        ConfigurationFile = new Data.Linq.Binary(cfg_bytes),
                                                        Preferences = new Data.Linq.Binary([| |]))
                            server.DB.PlcConfiguration.InsertOnSubmit(new_cfg)
                            server.DB.DataContext.SubmitChanges()
                            self.PlcConfiguration <- new_cfg
                            cout.WriteLine(this.ClientName + ": New configuration version: " + (new_version.ToString()) + ", size: " + (cfg_bytes.Length.ToString()) + ".")
                        else
                            cout.WriteLine(this.ClientName + ": Configuration already seen as version: " + (first_match.Version.ToString()) )
                    ()
                if msg.HasOOBDatabaseRange then
                    let dbr = msg.OOBDatabaseRange
                    if dbr.HasTailId && dbr.HasHeadId then
                        let tail_id, head_id = dbr.TailId, dbr.HeadId
                        cout.WriteLine(this.ClientName + ": OOB Database range: [" + tail_id.ToString() + " .. " + head_id.ToString() + " ).")
                        rows_to_fetch.Clear()
                        // 1. Fetch the rows.
                        let ids = query { for sv in user_info.SignalValue do sortBy sv.OriginalId; select sv.OriginalId }
                        // 2. Scan for holes.
                        let mutable prev_tail_id = tail_id - 1
                        for r_head_id in ids do
                            let r_tail_id = prev_tail_id + 1
                            if r_tail_id < r_head_id then
                                enqueue_range rows_to_fetch (r_tail_id, r_head_id)
                            prev_tail_id <- r_head_id
                        // 3. Fix up the remaining hole (if any)
                        let r_tail_id = prev_tail_id + 1
                        if r_tail_id <= head_id then
                            enqueue_range rows_to_fetch (r_tail_id,head_id + 10) // only head_id+1 is necessary, the +10 is extra.
                        // 4. Start!
                        query_next_rows_if_any ()
                        ()
                    ()
                if msg.HasOOBDatabaseRow then
                    cout.WriteLine(this.ClientName + ": OOB Database row received: " + (msg.OOBDatabaseRow.RowId.ToString()));
                    let plc_row = msg.OOBDatabaseRow
                    if plc_row.HasRowId && plc_row.HasTimeMs && plc_row.HasVersion && plc_row.HasSignalValues then
                        let row_count = query { for sv in server.DB.SignalValue do where (sv.UserId=user_info.Id && sv.OriginalId=plc_row.RowId); count }
                        if row_count=0 then
                            cout.WriteLine(this.ClientName + ": OOB Database row signal values: " + (plc_row.SignalValues.Length.ToString()) + " bytes.")
                            insert_row_skip_submit plc_row
                            server.DB.DataContext.SubmitChanges()
                    ()
                if msg.ResponseToDatabaseRowsCount>0 then 
                    let rows = (seq {   for ri=0 to msg.ResponseToDatabaseRowsCount-1 do
                                            let r = msg.GetResponseToDatabaseRows(ri);
                                            if r.HasRowId && r.HasTimeMs && r.HasVersion && r.HasSignalValues then
                                                yield r } |> Seq.sortBy(fun x -> x.RowId)).ToArray()
                    if rows.Length>0 then
                        let id1, id2 = rows.First().RowId, rows.Last().RowId
                        cout.WriteLine(this.ClientName + ": Database rows: ok=" + (msg.Response.OK.ToString()) + (if msg.Response.HasMessage then ("msg=" + msg.Response.Message) else ""))
                        cout.WriteLine(this.ClientName + ": Database rows: [" + id1.ToString() + " .. " + (id2 + 1).ToString() + ").")
                        let indices_in_db = (query { for sv in server.DB.SignalValue do where (sv.UserId=user_info.Id && sv.OriginalId>=id1 && sv.OriginalId<=id2); select sv.OriginalId }).ToArray();
                        let mutable any_inserts = false
                        for plc_row in rows do
                            if indices_in_db.Contains(plc_row.RowId) then
                                ()
                            else
                                insert_row_skip_submit plc_row
                                any_inserts <- true
                        if any_inserts then
                            server.DB.DataContext.SubmitChanges()
                        query_next_rows_if_any ()
                    ()
                ()
            member this.SendLatestConfigurationIfAny () =
                let db_cfg, comm_cfg = self.LatestRowPlcConfiguration user_info.Id
                if db_cfg=null || comm_cfg=null then
                    ()
                else
                    this.PlcConfiguration <- db_cfg
                    cout.WriteLine(this.ClientName + ": Sending latest configuration.")
                    let msg =(new MessageToPlc.Builder()).SetId(this.NextQueryId).SetNewRowPlcConfiguration(comm_cfg).Build()
                    msg.WriteDelimitedTo(tcp_stream)
                    this.NextQueryId <- this.NextQueryId + 1L

        // ======================================================================
        and OperatorUser (user_type:UserType, user_info:schema.ServiceTypes.User, user_session:schema.ServiceTypes.UserSession, tcp_client:TcpClient, tcp_stream:Stream, server:PlcServer) as self =
            inherit User(user_type, user_info, user_session, tcp_client, tcp_stream, server)
            let send_latest_configuration (user_id:int) (msg_id:int64) = 
                let _, row_cfg = self.LatestRowPlcConfiguration user_id
                let msg =
                    if row_cfg = null then
                        (builder_with_response msg_id false (sprintf "No configuration found for UserId %d" user_id)).Build() 
                    else
                        (builder_with_response msg_id true "").SetResponseToQueryLatestRowPlcConfiguration(row_cfg).Build()
                self.SendToOperator(msg)
            // ControlPanel is expecting db range, etc.
            do
                let b = (builder_with_response self.NextQueryId true "")
                // 1. List of users.
                for u in server.DB.User do
                    let u_msg = (new PlcCommunication.RowUser.Builder()).SetId(u.Id).SetCreateDate(u.Created.Ticks).SetName(u.Name).SetType(u.Type).SetIsPublic(u.IsPublic).Build()
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
                        let plcs_to_monitor = (seq { for u in server.DB.User do if u.Type="PLC" && monitor_ids.Contains(u.Id) then yield u}).ToList()
                        this.PlcsToMonitor <- (seq { for u in plcs_to_monitor do yield u.Id }).ToList()
                        for plc_id in this.PlcsToMonitor do
                            // Initial message: Range
                            let sv_first = query { for sv in server.DB.SignalValue do where (sv.UserId = plc_id); sortBy sv.OriginalId; select sv; headOrDefault }
                            let sv_last = query { for sv in server.DB.SignalValue do where (sv.UserId = plc_id); sortByDescending sv.OriginalId; select sv; headOrDefault } // lastOrDefault is not supported.
                            let idrange =
                                if sv_first<>null && sv_last<>null then
                                    (new PlcCommunication.IdRange.Builder()).SetTailId(sv_first.OriginalId).SetHeadId(sv_last.OriginalId + 1)
                                else
                                    (new PlcCommunication.IdRange.Builder()).SetTailId(0).SetHeadId(0)
                            // Initial message: Configuration.
                            let name_or_device_id = 
                                let plc_id = (FsJson.parse user_info.Credentials) ? PlcId
                                if plc_id.IsNotNull && plc_id.Val<>null then plc_id.Val else user_info.Name
                            let plc_version, plc_config =
                                let pc = query { for pc2 in server.DB.PlcConfiguration do where (pc2.UserId=plc_id); sortByDescending pc2.Version; select pc2; headOrDefault }
                                if pc=null then
                                    0, [| |]
                                else
                                    pc.Version, pc.ConfigurationFile.ToArray()
                            let cfg = (new PlcCommunication.Configuration.Builder()).SetDeviceId(name_or_device_id).SetVersion(plc_version).SetConfigurationFile(Google.ProtocolBuffers.ByteString.CopyFrom(plc_config))
                            let msg = (new PlcCommunication.MessageFromPlc.Builder()).SetId(self.NextQueryId).SetOOBDatabaseRange(idrange).SetOOBConfiguration(cfg).SetSourceId(plc_id).Build()
                            cout.WriteLine(this.ClientName + ": monitors PLC: " + (plc_id.ToString()) + ", id range=[" + (msg.OOBDatabaseRange.TailId.ToString()) + ".." + (msg.OOBDatabaseRange.HeadId.ToString()) + ").")
                            this.SendToOperator(msg)
                            // Can we send the latest row?
                            if sv_last<>null then
                                cout.WriteLine(this.ClientName + ": Sending latest row: " + (sv_last.OriginalId.ToString()) + " from the PLC " + (plc_id.ToString()))
                                let msg_row = msg_of_db_row sv_last
                                let b = new PlcCommunication.MessageFromPlc.Builder()
                                b.SetOOBDatabaseRow(msg_row) |> ignore
                                b.SetSourceId(plc_id) |> ignore
                                b.SetId(-1L) |> ignore
                                this.SendToOperator(b.Build())
                    // 3. Database rows request.
                    let qrows = msg.QueryDatabaseRows
                    if qrows<>null && qrows.HasTailId && qrows.HasTailId then
                        if this.PlcsToMonitor.Count>0 || msg.HasTargetPlcId then
                            let plc_id = if msg.HasTargetPlcId then msg.TargetPlcId else this.PlcsToMonitor.[0]
                            let b = new PlcCommunication.MessageFromPlc.Builder()
                            b.SetId(msg.Id) |> ignore
                            b.SetSourceId(plc_id) |> ignore
                            let rows = query { for sv in server.DB.SignalValue do where (sv.UserId=plc_id && sv.OriginalId>=qrows.TailId && sv.OriginalId<qrows.HeadId); sortBy sv.OriginalId; select sv }
                            let mutable nrows = 0
                            for r in rows do
                                b.AddResponseToDatabaseRows(msg_of_db_row r) |> ignore
                                nrows <- nrows + 1
                            this.SendToOperator(b.Build())
                            cout.WriteLine(this.ClientName + ": Requested rows: [" + qrows.TailId.ToString() + " .. " + qrows.HeadId.ToString() + "), sending " + nrows.ToString() + " rows.")
                        else
                            cout.WriteLine(this.ClientName + ": Not responding to the database rows query because there are no plcs under monitoring.")
                    // 4. User configuration request.
                    if msg.HasQueryLatestRowPlcConfiguration then
                        send_latest_configuration msg.QueryLatestRowPlcConfiguration msg.Id
                    // 5. Request to set new configuration.
                    if msg.HasNewRowPlcConfiguration then
                        let src_cfg = msg.NewRowPlcConfiguration
                        let src_cfg_file, src_prefs = src_cfg.ConfigurationFile.ToByteArray(), src_cfg.Preferences.ToByteArray()
                        let list_of_cfgs = (query { for c in server.DB.PlcConfiguration do where (c.UserId=src_cfg.UserId); sortBy c.Version; select c }).ToArray()
                        let first_match = (seq { for dbc in list_of_cfgs do if (are_bytes_equal src_cfg_file (dbc.ConfigurationFile.ToArray())) && (are_bytes_equal src_prefs (dbc.Preferences.ToArray())) then yield dbc }).FirstOrDefault()
                        if first_match=null then
                            let new_version = if list_of_cfgs.Count() = 0 then 0 else (list_of_cfgs.Last().Version + 1)
                            let new_cfg = new schema.ServiceTypes.PlcConfiguration(
                                                        UserId=src_cfg.UserId,
                                                        UserSessionId = user_session.Id,
                                                        CreateDate = DateTime.Now,
                                                        Version = new_version,
                                                        ConfigurationFile = new Data.Linq.Binary(src_cfg_file),
                                                        Preferences = new Data.Linq.Binary(src_prefs))
                            server.DB.PlcConfiguration.InsertOnSubmit(new_cfg)
                            server.DB.DataContext.SubmitChanges()
                            cout.WriteLine(this.ClientName + ": New configuration version for user (id:" + (src_cfg.UserId.ToString()) + "): " + (new_version.ToString()) + ", file: " + (src_cfg_file.Length.ToString()) + "bytes, prefs:" + (src_prefs.Length.ToString()) + " bytes.")
                            // Send back new configuration :)
                            send_latest_configuration src_cfg.UserId msg.Id
                            for u in server.PlcUsers do
                                if u.UserInfo.Id = src_cfg.UserId then
                                    u.SendLatestConfigurationIfAny()
                        else
                            cout.WriteLine(this.ClientName + ": Configuration for user (id:" + (src_cfg.UserId.ToString()) + ") already seen as version: " + (first_match.Version.ToString()) )
                    if msg.HasQueryDbRange && msg.QueryDbRange.HasFirstTimeTicks && msg.HasId then
                        let q = msg.QueryDbRange
                        if msg.HasTargetPlcId then
                            let plc_id = msg.TargetPlcId
                            let first_time = new DateTime(q.FirstTimeTicks)
                            let last_time = ref DateTime.Now
                            let sv_first = query { for sv in server.DB.SignalValue do where (sv.UserId = plc_id && sv.TimeStamp>first_time); sortBy sv.OriginalId; select sv; headOrDefault }
                            let sv_last, last_plus =
                                if q.HasLastTimeTicks
                                    then
                                        last_time := new DateTime(q.LastTimeTicks)
                                        query { for sv in server.DB.SignalValue do where (sv.UserId = plc_id && sv.TimeStamp > !last_time); sortBy sv.OriginalId; select sv; headOrDefault }, 0 // lastOrDefault is not supported.
                                    else
                                        query { for sv in server.DB.SignalValue do where (sv.UserId = plc_id); sortByDescending sv.OriginalId; select sv; headOrDefault }, 1 // lastOrDefault is not supported.
                            ()
                            let id_range = if sv_first<>null && sv_last<>null then idrange_of sv_first.OriginalId (sv_last.OriginalId + last_plus) else idrange_of 0 0
                            let rb = builder_with_response msg.Id true ""
                            rb.SetSourceId(plc_id) |> ignore
                            rb.SetResponseToDbRangeQuery(id_range) |> ignore
                            cout.WriteLine(this.ClientName + ": QueryDbRange " + (first_time.ToString()) + " .. " + (if q.HasLastTimeTicks then ((!last_time).ToString()) else "_end_ => ") + (id_range.TailId.ToString()) + " .. " + (id_range.HeadId.ToString()) + ".")
                            let reply = rb.Build()
                            this.SendToOperator(reply)
                        else
                            let reply = (builder_with_response msg.Id false (sprintf "No Target PLC specified")).Build() 
                            cout.WriteLine(this.ClientName + ": QueryRange: No target PLC specified.")
                            this.SendToOperator reply
                ()
        // ======================================================================
        and PlcServer (db:schema.ServiceTypes.SimpleDataContextTypes.PlcMaster) as self =
            let processor = new MailboxProcessor<MailboxMessageToServer>(self.mailbox_loop db)
            member val DB:schema.ServiceTypes.SimpleDataContextTypes.PlcMaster = db with get
            member val PlcUsers:List<PlcUser> = new List<PlcUser>() with get
            member val OperatorUsers = new List<OperatorUser>() with get
            member this.Start() = processor.Start()
            member this.Post(msg:MailboxMessageToServer) = processor.Post(msg)
            member this.PostAndReply<'Reply>(buildMessage:(AsyncReplyChannel<'Reply> -> MailboxMessageToServer)) = processor.PostAndReply(buildMessage)
            member this.mailbox_loop (db:schema.ServiceTypes.SimpleDataContextTypes.PlcMaster) (inbox:MailboxProcessor<MailboxMessageToServer>) =
                let rec loop () =
                    async {
                        let! msg_to_server = inbox.Receive()
                        match msg_to_server with
                        | MailboxMessageToServer.Start (client_name, msg, tcp_client, stream, endpoint, reply) ->
                            // TODO: Check the credentials...
                            let reply_msg = ref None
                            try
                                let create_and_insert_session (user_id:int) =
                                    let now = DateTime.Now
                                    let s_ip = endpoint.ToString()
                                    let s_idx = s_ip.IndexOf(':')
                                    let session = new schema.ServiceTypes.UserSession(
                                                            UserId = user_id,
                                                            StartTime = now,
                                                            EndTime = now,
                                                            IpAddress = s_ip.Substring(0, s_idx),
                                                            IsOpen = true)
                                    db.UserSession.InsertOnSubmit(session)
                                    db.DataContext.SubmitChanges()
                                    session
                                if msg.HasOOBConfiguration then
                                    let cfg = msg.OOBConfiguration
                                    // PLC?
                                    if cfg.HasDeviceId && cfg.HasVersion && cfg.HasConfigurationFile then
                                        cout.WriteLine(client_name + ": OOB Configuration received, Device id=" + cfg.DeviceId)
                                        // 1. Is the device known?
                                        let u_seq = seq { for u2 in query { for u in db.User do where (u.Type = "PLC"); select u } do if is_plc_id u2.Credentials cfg.DeviceId then yield u2 }
                                        let user_info = u_seq.FirstOrDefault()
                                        if user_info<>null then
                                            let session = create_and_insert_session user_info.Id
                                            let new_user = new PlcUser(UserType.PLC, user_info, session, tcp_client, stream, this)
                                            new_user.Open()
                                            reply_msg := Some (new_user :> User)
                                            this.PlcUsers.Add(new_user)
                                            cout.WriteLine(client_name + ": User is PLC: " + user_info.Name)
                                            new_user.SendLatestConfigurationIfAny()
                                        else
                                            cout.WriteLine(client_name + ": DeviceId unknown for the PLC: " + cfg.DeviceId)
                                    // User
                                    else if cfg.HasDeviceId && cfg.HasPassword then
                                        cout.WriteLine(client_name + ": OOB Configuration received, User Name=" + cfg.DeviceId)
                                        let user_info = query { for u in db.User do where (u.Name = cfg.DeviceId); select u; exactlyOneOrDefault}
                                        if user_info<>null && is_password_ok user_info.Credentials cfg.Password then
                                            let session = create_and_insert_session user_info.Id
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
                        | MailboxMessageToServer.ProcessMessageFromPlc (user, msg) ->
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
                        | MailboxMessageToServer.ProcessMessageFromOperator (user, msg) ->
                            try
                                user.ProcessMessageFromOperator(msg)
                            with
                                | ex -> cout.WriteLine(user.ClientName + ": Message processing: " + ex.Message)
                        | MailboxMessageToServer.Finish (user) ->
                            try
                                user.UserSession.EndTime <- DateTime.Now
                                user.UserSession.IsOpen <- false
                                db.DataContext.SubmitChanges()
                            with
                                | ex -> cout.WriteLine(user.ClientName + ": Cannot close the session: " + ex.Message)
                            match user.UserType with
                            | UserType.PLC -> this.PlcUsers.Remove(user :?> PlcUser) |> ignore
                            | UserType.Operator -> this.OperatorUsers.Remove(user :?> OperatorUser) |> ignore
                            user.Close()
                        | MailboxMessageToServer.Die ->
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
                            return ()
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
            let db,listener,server = param :?> (schema.ServiceTypes.SimpleDataContextTypes.PlcMaster * TcpListener * PlcServer)
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
            // 1. Connect to the DB.
            let cfg =
                try
                    let s = File.ReadAllText PlcServer.config_filename
                    PlcServer.unjson<PlcServer.ServerConfiguration> s
                with
                | ex ->
                    PlcServer.cout.WriteLine("PlcServerFacade: Cannot read configuration file '" + (PlcServer.config_filename) + "': " + ex.Message)
                    let r = new PlcServer.ServerConfiguration()
                    try
                        let s_out = PlcServer.json<PlcServer.ServerConfiguration> r
                        File.WriteAllText(PlcServer.config_filename, s_out)
                    with
                    | ex2 ->
                        PlcServer.cout.WriteLine("PlcServerFacade: Cannot write default configuration file '" + (PlcServer.config_filename) + "': " + ex.Message)
                    r
            let db = if cfg.DatabaseConnection.Length=0 then PlcServer.schema.GetDataContext() else PlcServer.schema.GetDataContext(cfg.DatabaseConnection)
            db.Connection.Open()
            // 2. Open the TCP/IP port for listening.
            let listener = TcpListener(ipAddress, port)
            listener.Start()
            // 3. Create the PlcServer
            let plc_server = new PlcServer.PlcServer(db)
            plc_server.Start()
            // 4. Create a thread for the listener.
            let listener_th = new Thread(new ParameterizedThreadStart(PlcServer.serve_incoming_connections))
            listener_th.Start( (db, listener, plc_server))
            ctx <- Some (db, listener, listener_th, plc_server)
        // Shutdown our server.
        member this.Stop()=
            match ctx with
            | Some (db, listener, listener_th, plc_server) ->
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
                PlcServer.cout <- new System.IO.StreamWriter(PlcServer.log_filename, (*append=*)true)
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
            if argv.Contains("--run") then
                let server = new PlcServerFacade()
                server.Start()
                while true do
                    System.Threading.Thread.Sleep(3600* 1000)
                server.Stop()
            else
                // the service.
                let service = new PlcService()
                System.ServiceProcess.ServiceBase.Run(service) 
            0
