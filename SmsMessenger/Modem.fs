namespace SmsMessenger
    open System.Collections.Generic
    open System.Threading
    open System

    type Modem () =
        member this.SendSms(receiver: string, content:string) = ()
        member this.Close () = ()

    /// Serial port modem interface.
    type SerialModem (port_name: string) =
        inherit Modem ()
        let timeout_ms = 10 * 1000
        let port = new System.IO.Ports.SerialPort()

        /// Write a string to the port.
        let port_write (s:string) =
            // printfn "Write: '%s'" s
            port.Write(s)

        /// Write a line to the port.
        let port_writeln (s:string) =
            // printfn "WriteLine: '%s'" s
            port.Write(s + "\r")

        /// Read a line from the port. End of line is marked by 0x0D.
        let port_readln () =
            let r = new System.Text.StringBuilder()
            let mutable finished = false
            while not finished do
                let x  = port.ReadByte()
                if x<0 then
                    raise (new ApplicationException("Modem: Port read error"))
                else if x=0x0A then
                    finished <- true
                else if x<>0x0D then
                    r.Append(char x) |> ignore
            r.ToString()

        /// Read given number of characters from the port.
        let port_read (nchars:int) =
            let r = System.Text.StringBuilder()
            for i=1 to nchars do
                let x = port.ReadByte()
                r.Append((char x)) |> ignore
            r.ToString()

        /// Expect a given string from the port.
        let port_expect (s:string) =
            let s_read = port_read (s.Length)
            if s_read<>s then
                raise (new ApplicationException(sprintf "Modem: Read '%s', expected '%s'" (s_read.Trim()) s))
            ()

        /// Read a line from the port and expect the beginning to match the string
        let port_expectln (s:string) =
            let s_read = port_readln ()
            // printfn "Read: %s" s_read
            if s.Length>0 then
                if not (s_read.StartsWith(s)) then
                    raise (new ApplicationException(sprintf "Modem: Read '%s', expected '%s'" (s_read.Trim()) s))
            elif s_read.Length>0 then
                raise (new ApplicationException(sprintf "Modem: Read '%s', expected empty line." (s_read.Trim()) ))
            ()

        let at_cmd (cmd: string) =
            port_writeln cmd
            port_expectln cmd
            port_expectln "OK"

        do
            port.PortName <- port_name
            port.BaudRate <- 115200
            port.DtrEnable <- true
            port.RtsEnable <- true
            port.ReadTimeout <- timeout_ms
            port.WriteTimeout <- timeout_ms
            // port.NewLine <- ""
            port.Open()
            at_cmd "AT+CMGF=1"
            ()

        /// Send a 1-line message.
        member this.SendSms(receiver: string, content:string) =
            let start_cmd = sprintf "AT+CMGS=\"%s\"" receiver
            port_writeln start_cmd
            port_expectln start_cmd
            port_expect "> "
            port_write (content + Char.ConvertFromUtf32(26))
            port_expectln content
            port_expectln "+CMGS:"
            port_expectln ""
            port_expectln "OK"
            System.Threading.Thread.Sleep(1000)
            ()

        /// Close the modem.
        member this.Close () =
            port.Close()
            port.Dispose()

    /// Dummy modem interface for testing.
    type DummyModem () =
        inherit Modem ()

        let log_file_name = "dummy-modem-sms-log.txt"
        let app_dir = AppDomain.CurrentDomain.SetupInformation.ApplicationBase
        let log_file_path = System.IO.Path.Combine(app_dir, log_file_name)

        /// Write sent SMS to file
        member this.SendSms(receiver: string, content:string) =
            let stream = new System.IO.StreamWriter(log_file_path, true)
            stream.AutoFlush <- true
            stream.WriteLine(sprintf "SMS to %s: '%s'" receiver content)
            stream.Dispose()

    type ModemType =
        | Serial of SerialModem
        | Dummy of DummyModem