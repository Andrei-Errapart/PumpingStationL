namespace PlcServer2
    open System
    open System.Collections.Generic
    open System.Data.SQLite
    open System.IO
    open System.Linq
    open System.Net
    open System.Net.Sockets
    open System.Text.RegularExpressions
    open System.Threading
    open System.Xml
    open System.Diagnostics
    open Microsoft.FSharp.Data.TypeProviders
    open Microsoft.FSharp.Linq
    open PlcCommunication
    open Newtonsoft.Json
    open Types

    module U =
        let _RefTimeJava = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)

        // ======================================================================
        /// Object to Json 
        let internal json<'t> (myObj:'t) = JsonConvert.SerializeObject(myObj, Formatting.Indented)

        // ======================================================================
        /// Object from Json 
        let internal unjson<'t> (jsonString:string)  : 't = JsonConvert.DeserializeObject<'t> (jsonString)

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
        let enqueue_range (max_batch_size:int) (q:IList<int*int>) ((r_tail_id:int),(r_head_id:int)) =
            let n = (r_head_id - r_tail_id + max_batch_size - 1) / max_batch_size
            // cout.WriteLine(client_name + ": Initializing query in the range [" + r_tail_id.ToString() + " .. " + r_head_id.ToString() + ") in " + n.ToString() + " batches.")
            for i=0 to n-1 do
                let x_tail_id = r_tail_id + i * max_batch_size
                let x_head_id = Math.Min(r_head_id, x_tail_id + max_batch_size)
                q.Add(x_tail_id, x_head_id)
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
        let msg_of_db_row (db_sv:PlcDb.SignalValue) =
            let bytes = db_sv.SignalValues
            let msg = (new PlcCommunication.DatabaseRow.Builder()).SetRowId(db_sv.OriginalId).SetVersion(db_sv.ConfigurationId).SetTimeMs(JavaMillisecondsOfDateTime (db_sv.TimeStamp)).SetSignalValues(Google.ProtocolBuffers.ByteString.CopyFrom(bytes))
            msg

        // ======================================================================
        // Protocol message from the row of Configuration.
        let msg_of_db_row_configuration (user_id:int) (db_cfg:PlcDb.Configuration)=
            let comm_cfg = (new PlcCommunication.RowPlcConfiguration.Builder()).
                            SetId(db_cfg.Id).SetUserId(user_id).SetUserSessionId(0).SetVersion(db_cfg.Id).SetCreateDate(db_cfg.CreateDate.Ticks).
                            SetConfigurationFile(Google.ProtocolBuffers.ByteString.CopyFrom(db_cfg.SetupFile)).
                            SetPreferences(Google.ProtocolBuffers.ByteString.CopyFrom(db_cfg.Preferences)).Build()
            (db_cfg, comm_cfg)

        // ======================================================================
        // PlcDb.SignalValues from the communication packet...
        let dbsv_of_commrow (plc_cfg:PlcDb.Configuration) (session_id:int) (plc_row:DatabaseRow) =
        (*
            match self.PlcConfiguration with
                | None -> failwith "Skipped signal values, because there is no configuration (yet)."
                | Some plc_cfg ->
                *)
            try
                let some_ts = DateTimeOfJavaMilliseconds plc_row.TimeMs
                let new_row = new PlcDb.SignalValue(
                                            SessionId = session_id,
                                            ConfigurationId = plc_cfg.Id,
                                            OriginalId = plc_row.RowId,
                                            TimeStamp = some_ts,
                                            SignalValues = plc_row.SignalValues.ToArray())
                new_row
            with ex ->
                failwith (": Invalid timestamp (" + (plc_row.TimeMs.ToString()) + ") for the row (id:"  + (plc_row.RowId.ToString()) + ")." )

        // ======================================================================
        let extract_bit (packet : byte[]) (byte_index : int byref) (bit_index : int byref) =    
            let r = 
                try
                    (packet.[byte_index] >>> bit_index) &&& 1uy
                with 
                    | :? System.IndexOutOfRangeException -> 0uy

            bit_index <- bit_index - 1
            if bit_index < 0 then
                bit_index <- 7
                byte_index <- byte_index + 1
            r

        // ==================================================================
        /// Output text to console
        let cout (text : string) =
            System.Console.Out.WriteLine text

        // ==================================================================
        /// Get named attribute value or empty string if not found
        let attr (s_dfn : XmlNode) (name : string) =
            match s_dfn.Attributes.GetNamedItem(name) with | null -> "" | a -> a.Value

        // ==================================================================

        // ==================================================================
        /// Print a caught exception
        let print_ex (client_name : string, e : Exception, msg : string) =
            cout(System.String.Format("{0}: {1} '{2}'. {3}", client_name, e.Message, e.GetType().FullName, msg))
            Debug.WriteLine(e.StackTrace)
