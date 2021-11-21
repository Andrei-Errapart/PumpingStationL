namespace PlcDb
    open System
    open System.Collections.Generic
    open System.Data
    open System.Data.SQLite

    // ========================================================================================================
    /// Utility methods.
    module internal U =
        let prepare_query (db:SQLiteConnection) (query:string) (args: (string * DbType) list) =
            let r = new SQLiteCommand(query, db)
            for (param_name, param_type) in args do
                let p = new SQLiteParameter(param_name, param_type)
                r.Parameters.Add(p) |> ignore
            r

        let rquery (cmd:SQLiteCommand) (row_fun: SQLiteDataReader -> unit) =
            use r = cmd.ExecuteReader()
            while r.Read() do
                row_fun r
            ()

        // ---------------------------------------------------------------------------------------------------------------
        let rquery1 (cmd:SQLiteCommand) (row_fun: SQLiteDataReader -> 'a) =
            use r = cmd.ExecuteReader()
            if r.Read() then Some (row_fun r) else None

        // ---------------------------------------------------------------------------------------------------------------
        let rquery1int (cmd:SQLiteCommand)  =
            use r = cmd.ExecuteReader()
            if r.Read() then Some (r.GetInt32(0)) else None

        // ---------------------------------------------------------------------------------------------------------------
        let squery (db:SQLiteConnection) (query_string:string) (row_fun: SQLiteDataReader -> unit) =
            use cmd = new SQLiteCommand(query_string, db)
            rquery cmd row_fun

        // ---------------------------------------------------------------------------------------------------------------

        // ---------------------------------------------------------------------------------------------------------------
        let reader_getbytes (reader:SQLiteDataReader) (index:int) =
            let CHUNK_SIZE = 2 * 1024
            let buffer = Array.zeroCreate<byte> CHUNK_SIZE
            let mutable bytes_read = 1
            let mutable field_offset = 0
            use stream = new System.IO.MemoryStream()
            while bytes_read>0 do
                bytes_read <- int (reader.GetBytes(index, int64 field_offset, buffer, 0, buffer.Length))
                stream.Write(buffer, 0, bytes_read)
                if bytes_read>0 then
                    field_offset <- field_offset + bytes_read
            stream.ToArray()

        // ---------------------------------------------------------------------------------------------------------------
        let bool_of_env (env:Dictionary<string,string>) (key:string) (default_value:bool) =
            match env.TryGetValue(key) with
            | true, s -> let s2 = s.ToUpperInvariant() in s2="TRUE" || s2="1"
            | _ -> default_value

        // ---------------------------------------------------------------------------------------------------------------
        let datetime_of_env (env:Dictionary<string,string>) (key:string) =
            match env.TryGetValue(key) with
            | true, s -> DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture)
            | _ -> failwith (sprintf "datetime_of_env: Key '%s' not found in the environment." key)

        // ---------------------------------------------------------------------------------------------------------------
        let opendb_and_fetchenv (db_filename:string) =
            let db = new SQLiteConnection("Data Source=\"" + db_filename + "\"")
            db.Open()
            let env = new Dictionary<string,string>()
            squery db "SELECT Id, Name, Value FROM Environment" (fun (r:SQLiteDataReader) -> env.Add(r.GetString(1), r.GetString(2)) )
            db, env

        // ---------------------------------------------------------------------------------------------------------------
        let insert_cmd (db:SQLiteConnection) (table:string) (fields:(string * System.Data.DbType)[]) =
            let cols = new System.Text.StringBuilder()
            let param_names = new System.Text.StringBuilder()
            for i=0 to fields.Length-1 do
                let col_name, _ = fields.[i]
                if i>0 then
                    cols.Append(", ") |> ignore
                    param_names.Append(", ") |> ignore
                cols.Append(col_name) |> ignore
                param_names.Append("@" + col_name) |> ignore

            let cmd = new SQLiteCommand(sprintf "INSERT INTO %s(%s) VALUES(%s)" table (cols.ToString()) (param_names.ToString()) , db)
            for i=0 to fields.Length-1 do
                let col_name, col_type = fields.[i]
                cmd.Parameters.Add(new SQLiteParameter("@"  + col_name, col_type)) |> ignore
            cmd
        

        /// Check if schema versioning exists, apply updates as necessary
        let check_plc_db (db : SQLiteConnection) =
            // Queries related to database schema versioning
            let db_version_query = 
                prepare_query db "SELECT Value FROM Environment WHERE Name='DbVersion'" []

            let initialize_versioning =
                insert_cmd db "Environment" [| ("Name", DbType.String); ("Value", DbType.String) |]

            let update_version =
                prepare_query db "UPDATE Environment SET Value=@Version WHERE Name='DbVersion'" [ "@Version", DbType.String ]

            /// Reflects the schema version of this code
            let current_version = 1
            // Check DB version
            use r = db_version_query.ExecuteReader()
            /// Schema version in the data base file
            let db_version = 
                if r.Read() then 
                    let db_version = Int32.Parse(r.GetString(0))
                    r.Dispose()
                    db_version
                else
                    use trn = db.BeginTransaction()
                    initialize_versioning.Parameters.[0].Value <- "DbVersion"
                    initialize_versioning.Parameters.[1].Value <- current_version.ToString()
                    initialize_versioning.ExecuteNonQuery() |> ignore
                    trn.Commit()
                    initialize_versioning.Dispose()
                    trn.Dispose()
                    current_version
            
            // Apply updates in order
            use trn = db.BeginTransaction()
            if db_version < 1 then
                let update_1_sql = System.IO.File.ReadAllText("Update1.sql")
                let update_1 = new SQLiteCommand(update_1_sql, db, trn)
                update_1.ExecuteNonQuery() |> ignore
                update_1.Dispose()

            // Update version info
            update_version.Parameters.[0].Value <- current_version.ToString()
            update_version.ExecuteNonQuery() |> ignore
            trn.Commit()
            update_version.Dispose()
            trn.Dispose()
