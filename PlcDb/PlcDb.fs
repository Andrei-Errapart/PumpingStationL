namespace PlcDb
    open System
    open System.Data
    open System.Data.SQLite
    open System.Collections.Generic
    open System.Linq
    open System.Xml

    // ---------------------------------------------------------------------------------------------------------------
    /// Session with the user.
    type public Session () =
        member val Id = 0 with get, set
        member val StartTime = DateTime.MinValue with get, set
        member val EndTime = DateTime.MinValue with get, set
        member val IpAddress = "" with get, set
        member val IsOpen = false with get, set

    // ---------------------------------------------------------------------------------------------------------------
    /// Common values
    type public User (id:int, name:string, db:SQLiteConnection, env:Dictionary<string,string>) =
        let session_update = U.prepare_query db "UPDATE Session SET EndTime=@EndTime, IsOpen=0 WHERE Id=@Id" [ "@Id", DbType.Int32; "@EndTime", DbType.DateTime ]

        static member val KEY_CREATED = "Created"
        static member val KEY_IS_PUBLIC = "IsPublic"
        /// Id = index into the OperatorUser/PlcUser array.
        member val Id = id with get
        // Fields in the database.
        member val Name = name with get
        member val Created = U.datetime_of_env env User.KEY_CREATED with get
        member val IsPublic = U.bool_of_env env User.KEY_IS_PUBLIC false with get
        // --- Normal fields.
        /// Must be supplied.
        member val Db:SQLiteConnection = db with get, set
        /// Must be supplied.
        member val Environment = env with get

        /// Update the session's EndTime/IsOpen.
        member this.UpdateSessionEnd (session:Session) =
            session.EndTime <- DateTime.Now
            session.IsOpen <- false
            use trn = db.BeginTransaction()
            session_update.Parameters.[0].Value <- session.Id
            session_update.Parameters.[1].Value <- session.EndTime
            session_update.ExecuteNonQuery() |> ignore
            ()

        /// Dispose pattern.
        interface IDisposable with
            member this.Dispose () =
                try
                    let x = this.Db
                    if x<>null then
                        this.Db <- null
                        x.Close()
                with
                    | ex -> System.Console.WriteLine(sprintf "Error closing '%s' db: %s'" name (ex.Message))


    // ============================ OPERATOR ============================================================================
    /// Operator database. Single-threaded access only!
    type public OperatorUser (id:int, name:string, db_filename:string, db:SQLiteConnection, env:Dictionary<string,string>) =
        inherit User(id, name, db, env)
        static member val KEY_PASSWORD_HASH = "PasswordHash"
        member val PasswordHash = "" with get, set

        /// Constructor
        new (id:int, name:string, db_filename:string) =
            let db, env = U.opendb_and_fetchenv db_filename
            new OperatorUser (id, name, db_filename, db, env)

        /// Fetch all OperatorUsers in the given directory.
        static member FetchAll (directory:string) =
            let r = new List<OperatorUser>()
            for db_filename in System.IO.Directory.EnumerateFiles(directory, "*.op", IO.SearchOption.TopDirectoryOnly) do
                let name = System.IO.Path.GetFileNameWithoutExtension(db_filename)
                try
                    r.Add(new OperatorUser(r.Count, name, db_filename))
                with
                    | ex -> System.Console.WriteLine(sprintf "Error loading OperatorUser %s: %s" name (ex.Message))
            r

    // ============================ PLC =================================================================================
    type public Configuration () =
        /// Id also maps to version.
        static member val KEY_SETUP_FILE = "Setup"
        static member val KEY_PREFERENCES = "Preferences"
        member val Id = 0 with get, set
        member val CreateDate = DateTime.MinValue with get, set
        member val Data = new Dictionary<string, byte[]>() with get, set
        /// Synthesized data.
        member val SetupFile:byte[] = [| |] with get, set
        member val Preferences:byte[] = [| |] with get, set
        member val Setup = new XmlDocument() with get, set

        static member parseSetup (xml : string) =
            let setup = new XmlDocument()
            try
                setup.LoadXml(xml.Substring(1)) // XML probably started with a UTF BOM
            with
                | ex -> setup.LoadXml(xml)
            setup

        // The latest known HeadId, TailId or 0,0. Either from the database or from the Plc.
        // member val DbRange = 0,0 with get, set
        /// Fetch all the configurations.
        static member FetchAll (db:SQLiteConnection) (user_id:int) =
            let l = new List<Configuration>()
            U.squery db "SELECT Id, CreateDate FROM Configuration ORDER BY Id ASC" (fun (r:SQLiteDataReader) -> l.Add(new Configuration(Id=r.GetInt32(0), CreateDate=r.GetDateTime(1))))
            for cfg in l do
                // 1. Get the configuration.
                U.squery db (sprintf "SELECT Name, Value FROM ConfigurationItem WHERE ConfigurationId=%d" cfg.Id) (fun (r:SQLiteDataReader) -> cfg.Data.Add(r.GetString(0), U.reader_getbytes r 1))
                cfg.SetupFile <- match cfg.Data.TryGetValue(Configuration.KEY_SETUP_FILE) with | true, x -> x | _ -> [| |]
                cfg.Preferences <- match cfg.Data.TryGetValue(Configuration.KEY_PREFERENCES) with | true, x -> x | _ -> [| |]
                cfg.Setup <-
                    match cfg.Data.TryGetValue("Setup") with 
                        | (true, x) -> Configuration.parseSetup(System.Text.Encoding.UTF8.GetString(x))
                        | _ -> new XmlDocument()
            l

        /// Fetch configuration by id.
        static member FetchById (db:SQLiteConnection) (cfg_id:int) =
            let l = new List<Configuration>()
            U.squery db (sprintf "SELECT Id, CreateDate FROM Configuration WHERE Id=%d" cfg_id) (fun (r:SQLiteDataReader) -> l.Add(new Configuration(Id=r.GetInt32(0), CreateDate=r.GetDateTime(1))))
            for cfg in l do
                // 1. Get the configuration.
                U.squery db (sprintf "SELECT Name, Value FROM ConfigurationItem WHERE ConfigurationId=%d" cfg.Id) (fun (r:SQLiteDataReader) -> cfg.Data.Add(r.GetString(0), U.reader_getbytes r 1))
                cfg.SetupFile <- match cfg.Data.TryGetValue(Configuration.KEY_SETUP_FILE) with | true, x -> x | _ -> [| |]
                cfg.Preferences <- match cfg.Data.TryGetValue(Configuration.KEY_PREFERENCES) with | true, x -> x | _ -> [| |]
                cfg.Setup <-
                    match cfg.Data.TryGetValue("Setup") with 
                        | (true, x) -> Configuration.parseSetup(System.Text.Encoding.UTF8.GetString(x))
                        | _ -> new XmlDocument()
            l


    // ---------------------------------------------------------------------------------------------------------------
    // TODO: fetch and store these.
    type public SignalValue () =
        member val Id = 0 with get, set
        member val CreateDate = DateTime.MinValue with get, set
        member val SessionId = 0 with get, set
        member val OriginalId = 0 with get, set
        member val TimeStamp = DateTime.MinValue with get, set
        member val ConfigurationId = 0 with get, set
        member val SignalValues:byte[] = [| |] with get, set

    // ---------------------------------------------------------------------------------------------------------------
    /// PLC database. Single-threaded access only!
    type public PlcUser (id:int, name:string, db_filename:string, db:SQLiteConnection, env:Dictionary<string,string>) =
        inherit User(id, name, db, env)

        let signalvalue_insert = U.insert_cmd db "SignalValue" [| ("SessionId", DbType.Int32); ("CreateDate", DbType.DateTime); ("OriginalId", DbType.Int32); ("TimeStamp", DbType.DateTime); ("ConfigurationId", DbType.Int32); ("SignalValues", DbType.Binary) |]

        let signalvalue_query_ids =
            U.prepare_query db "SELECT OriginalId FROM SignalValue WHERE ConfigurationId=@ConfigurationId ORDER BY OriginalId" [ "@ConfigurationId", DbType.Int32 ]

        let signalvalue_query_ids_range =
            U.prepare_query db "SELECT OriginalId FROM SignalValue WHERE ConfigurationId=@ConfigurationId AND OriginalId>=@TailId AND OriginalId<@HeadId ORDER BY OriginalId ASC" [ "@ConfigurationId", DbType.Int32; "@TailId", DbType.Int32; "@HeadId", DbType.Int32 ]

        let signalvalue_query_presence =
            U.prepare_query db "SELECT COUNT(*) FROM SignalValue WHERE ConfigurationId=@ConfigurationId AND OriginalId=@OriginalId AND [TimeStamp]=@TimeStamp" ["@ConfigurationId", DbType.Int32; "@OriginalId", DbType.Int32; "@TimeStamp", DbType.DateTime]

        // TODO: split this query into two: 1) select min(OriginalId) 2) select max(OriginalId), because it IS FASTER this way.
        let signalvalue_query_idrange_min =
            U.prepare_query db "SELECT min(OriginalId) FROM SignalValue WHERE ConfigurationId=@ConfigurationId" ["@ConfigurationId", DbType.Int32]
        let signalvalue_query_idrange_max =
            U.prepare_query db "SELECT max(OriginalId) FROM SignalValue WHERE ConfigurationId=@ConfigurationId" ["@ConfigurationId", DbType.Int32]

        let signalvalue_query_ids_bytime1 =
            U.prepare_query db "SELECT OriginalId FROM SignalValue WHERE ConfigurationId=@ConfigurationId AND [TimeStamp]>=@TailTime ORDER BY [TimeStamp] ASC LIMIT 1" ["@ConfigurationId", DbType.Int32; "@TailTime", DbType.DateTime]

        let signalvalue_query_ids_bytime2 =
            U.prepare_query db "SELECT OriginalId FROM SignalValue WHERE ConfigurationId=@ConfigurationId AND [TimeStamp]<=@HeadTime ORDER BY [TimeStamp] DESC LIMIT 1" ["@ConfigurationId", DbType.Int32; "@HeadTime", DbType.DateTime]

        let signalvalue_query_ids_bytime3 =
            U.prepare_query db "SELECT max(OriginalId) FROM SignalValue WHERE ConfigurationId=@ConfigurationId" ["@ConfigurationId", DbType.Int32]

        let signalvalue_query1 =
            U.prepare_query db "SELECT Id, CreateDate, SessionId, [TimeStamp], SignalValues FROM SignalValue WHERE ConfigurationId=@ConfigurationId AND OriginalId=@OriginalId" [ "@ConfigurationId", DbType.Int32; "@OriginalId", DbType.Int32 ]

        let signalvalue_query =
            U.prepare_query db "SELECT Id, CreateDate, SessionId, OriginalId, [TimeStamp], SignalValues FROM SignalValue WHERE ConfigurationId=@ConfigurationId AND OriginalId>=@TailId AND OriginalId<@HeadId ORDER BY OriginalId ASC" [ "@ConfigurationId", DbType.Int32; "@TailId", DbType.Int32; "@HeadId", DbType.Int32 ]

        let signalvalue_query_latest =
            U.prepare_query db "SELECT COUNT(*) FROM SignalValue WHERE CreateDate > @FromTime" ["@ConfigurationId", DbType.Int32; "@FromTime", DbType.DateTime]

        let signalvalue_delete_old =
            U.prepare_query db "DELETE FROM SignalValue WHERE [TimeStamp]<@DeleteDate" [ "@DeleteDate", DbType.DateTime ]

        let configuration_insert = U.insert_cmd db "Configuration" [| ("CreateDate", DbType.DateTime) |]
        let configurationitem_insert = U.insert_cmd db "ConfigurationItem" [| ("ConfigurationId", DbType.Int64); ("Name", DbType.String); ("Value", DbType.Binary) |]

        let session_insert = U.insert_cmd db "Session" [| ("StartTime", DbType.DateTime); ("EndTime", DbType.DateTime); ("IpAddress", DbType.String); ("IsOpen", DbType.Boolean) |]

        //
        static member val KEY_PLC_ID = "PlcId"

        /// Constructor.
        new (id:int, name:string, db_filename:string) =
            let db, env = U.opendb_and_fetchenv db_filename
            new PlcUser(id, name, db_filename, db, env)

        member val PlcId = env.[PlcUser.KEY_PLC_ID] with get
        member val Configurations = Configuration.FetchAll db id with get

        /// Fetch all PlcUsers in the given directory.
        static member FetchAll (directory:string) =
            let r = new List<PlcUser>()
            for db_filename in System.IO.Directory.EnumerateFiles(directory, "*.plc", IO.SearchOption.TopDirectoryOnly) do
                let name = System.IO.Path.GetFileNameWithoutExtension(db_filename)
                try
                    let db, env = U.opendb_and_fetchenv db_filename
                    U.check_plc_db db
                    let plc = new PlcUser(r.Count, name, db_filename, db, env)
                    r.Add(plc)
                with
                    | ex -> System.Console.WriteLine(sprintf "Error loading PlcUser %s: %s" name (ex.Message))
            r

        member private this._InsertSignalValue1(sv:SignalValue) =
            signalvalue_insert.Parameters.[0].Value <- sv.SessionId
            signalvalue_insert.Parameters.[1].Value <- DateTime.Now
            signalvalue_insert.Parameters.[2].Value <- sv.OriginalId
            signalvalue_insert.Parameters.[3].Value <- sv.TimeStamp
            signalvalue_insert.Parameters.[4].Value <- sv.ConfigurationId
            signalvalue_insert.Parameters.[5].Value <- sv.SignalValues
            signalvalue_insert.ExecuteNonQuery() |> ignore
            sv.Id <- int db.LastInsertRowId

        /// Insert 1 signal value.
        member this.InsertSignalValue (sv:SignalValue) =
            use trn = db.BeginTransaction()
            this._InsertSignalValue1(sv)
            trn.Commit()
            ()

        /// Insert bunch of signal values.
        member this.InsertSignalValues (svs:List<SignalValue>) =
            if svs.Count>0 then
                use trn = db.BeginTransaction()
                for sv in svs do
                    this.InsertSignalValue sv
                trn.Commit()
            ()

        /// Query all the ids of this given configuration...
        member this.QueryIdsOfSignalValues (cfg:Configuration) =
            let ids = new List<int>()
            signalvalue_query_ids.Parameters.[0].Value <- cfg.Id
            U.rquery signalvalue_query_ids (fun (r:SQLiteDataReader) -> ids.Add(r.GetInt32(0)))
            ids

        member this.QueryIdsOfSignalValuesRange (cfg:Configuration) (tail_id:int) (head_id:int) =
            let ids = new List<int>()
            signalvalue_query_ids_range.Parameters.[0].Value <- cfg.Id
            signalvalue_query_ids_range.Parameters.[1].Value <- tail_id
            signalvalue_query_ids_range.Parameters.[2].Value <- head_id
            U.rquery signalvalue_query_ids (fun (r:SQLiteDataReader) -> ids.Add(r.GetInt32(0)))
            ids

        member this.QuerySignalValuePresence (cfg:Configuration) (original_id:int) (timestamp:DateTime) =
            signalvalue_query_presence.Parameters.[0].Value <- cfg.Id
            signalvalue_query_presence.Parameters.[1].Value <- original_id
            signalvalue_query_presence.Parameters.[2].Value <- timestamp
            let n = U.rquery1 signalvalue_query_presence (fun (r:SQLiteDataReader) -> r.GetInt32(0))
            match n with | Some x -> x>0 | None -> false

        /// Return id [tail_id, head_id) from the database.
        member this.QuerySignalValuesIdRange (cfg:Configuration) =
            signalvalue_query_idrange_min.Parameters.[0].Value <- cfg.Id
            signalvalue_query_idrange_max.Parameters.[0].Value <- cfg.Id

            let rmin = U.rquery1int signalvalue_query_idrange_min
            let rmax = U.rquery1int signalvalue_query_idrange_max
            match rmin, rmax with
            | Some (idmin), Some (idmax) -> idmin, (idmax + 1)
            | _ -> 0,0

        /// Return [first_id, last_id]
        member this.QuerySignalValuesIdsByTime (cfg:Configuration) (tail_time:DateTime) (head_time:DateTime option) =
            /// First!
            signalvalue_query_ids_bytime1.Parameters.[0].Value <- cfg.Id
            signalvalue_query_ids_bytime1.Parameters.[1].Value <- tail_time
            signalvalue_query_ids_bytime2.Parameters.[0].Value <- cfg.Id
            signalvalue_query_ids_bytime2.Parameters.[1].Value <- match head_time with | Some x -> x | None -> DateTime.MaxValue
            signalvalue_query_ids_bytime3.Parameters.[0].Value <- cfg.Id

            let r1s = U.rquery1int signalvalue_query_ids_bytime1
            match r1s with
            | Some id1 ->
                match head_time with
                | Some htime ->
                    let r2s = U.rquery1int signalvalue_query_ids_bytime2
                    match r2s with
                    | Some id2 -> (id1, id2)
                    | None ->
                        failwith "this.QuerySignalValuesRangeByTime: Totally unexpected 1." 
                | None ->
                    let r3s = U.rquery1int signalvalue_query_ids_bytime3
                    match r3s with
                    | Some id2 -> (id1, id2)
                    | None ->
                        failwith "this.QuerySignalValuesRangeByTime: Totally unexpected 2." 
            | None -> (0, 0)

        /// Count latest signal value records from given time
        member this.QuerySignalCountFrom (from_time:DateTime) =
            signalvalue_query_latest.Parameters.[0].Value <- from_time
            let r = U.rquery1int signalvalue_query_latest
            match r with
            | Some count -> count
            | None -> 0

        /// Exactly 1 of the signal values.
        member this.QuerySignalValue1 (cfg:Configuration) (original_id:int) =
            signalvalue_query1.Parameters.[0].Value <- cfg.Id
            signalvalue_query1.Parameters.[1].Value <- original_id
            use r = signalvalue_query1.ExecuteReader()
            if r.Read() then
                let sv = new SignalValue(
                                Id=r.GetInt32(0), CreateDate=r.GetDateTime(1), SessionId=r.GetInt32(2),
                                OriginalId=original_id, TimeStamp=r.GetDateTime(3), ConfigurationId=cfg.Id, SignalValues=(U.reader_getbytes r 4))
                sv
            else
                failwith (sprintf "%s: QuerySignalValue1 failed for cfg_id=%d original_id=%d" (this.Name) (cfg.Id) original_id)


        member this.QuerySignalValues (cfg:Configuration) (tail_id:int) (head_id:int) =
            signalvalue_query.Parameters.[0].Value <- cfg.Id
            signalvalue_query.Parameters.[1].Value <- tail_id
            signalvalue_query.Parameters.[2].Value <- head_id
            use r = signalvalue_query.ExecuteReader()
            let svs = new List<SignalValue>()
            while r.Read() do
                let sv = new SignalValue(
                                Id=r.GetInt32(0), CreateDate=r.GetDateTime(1), SessionId=r.GetInt32(2),
                                OriginalId=r.GetInt32(3), TimeStamp=r.GetDateTime(4), ConfigurationId=cfg.Id, SignalValues=(U.reader_getbytes r 5))
                svs.Add(sv) |> ignore
            svs

        /// Delete data older than the given date.
        member this.DeleteSignalValuesOlderThan (ts:DateTime) =
            use trn = db.BeginTransaction()
            signalvalue_delete_old.Parameters.[0].Value <- ts
            let count = signalvalue_delete_old.ExecuteNonQuery()
            trn.Commit()
            count

        /// Insert 1 configuration.
        member this.InsertConfiguration (cfg:Configuration) =
            use trn = db.BeginTransaction()
            // 1. Insert the configuration.
            configuration_insert.Parameters.[0].Value <- DateTime.Now
            configuration_insert.ExecuteNonQuery() |> ignore
            let id = db.LastInsertRowId
            // 2. Insert missing pieces, if any.
            if cfg.SetupFile.Length>0 then
                configurationitem_insert.Parameters.[0].Value <- id
                configurationitem_insert.Parameters.[1].Value <- Configuration.KEY_SETUP_FILE
                configurationitem_insert.Parameters.[2].Value <- cfg.SetupFile
                configurationitem_insert.ExecuteNonQuery() |> ignore
            if cfg.Preferences.Length>0 then
                configurationitem_insert.Parameters.[0].Value <- id
                configurationitem_insert.Parameters.[1].Value <- Configuration.KEY_PREFERENCES
                configurationitem_insert.Parameters.[2].Value <- cfg.Preferences
                configurationitem_insert.ExecuteNonQuery() |> ignore
            trn.Commit()
            cfg.Id <- int id
            this.Configurations.Add(cfg)

        member this.InsertSession (session:Session) =
            use trn = db.BeginTransaction()
            session_insert.Parameters.[0].Value <- session.StartTime
            session_insert.Parameters.[1].Value <- session.EndTime
            session_insert.Parameters.[2].Value <- session.IpAddress
            session_insert.Parameters.[3].Value <- session.IsOpen
            session_insert.ExecuteNonQuery() |> ignore
            let id = db.LastInsertRowId
            trn.Commit()
            session.Id <- int id

