namespace SmsMessenger

open System

    module TestSmsMessenger =
        let console_log (s:string) =
            Console.Out.WriteLine s

        let test_modem () =
            let modem = new SerialModem("COM4")
            modem.SendSms("+372...", "hallo!")
            printfn "Finished!"

        let test_smssender () =
            let receivers = [| "+372..." |]
            let sms = new SmsSender("COM4", receivers, console_log, 160, "[Teade {0}] ")
            printfn "Started SMS sender!"
            Threading.Thread.Sleep(3000)
            sms.SendSms("Hallo!")
            Threading.Thread.Sleep(1000)
            sms.Close()
            printfn "Finished!"

        let port () =
            Console.Write("Port:")
            Console.ReadLine()

        let receivers (port : string) =
            if port.Length > 0 then
                Console.Write("Receiver:")
                let input = Console.ReadLine()
                [| input |]
            else
                [| "1-900-TEST" |]
        
        let text () =
            Console.Write("Message text:")
            Console.ReadLine()            

        [<EntryPoint>]
        let main args =
            Console.WriteLine "Commands:"
            Console.WriteLine "qm - queue message"
            Console.WriteLine "ss - send sms"
            Console.WriteLine "sm - send message"
            Console.WriteLine "sq - send queued"
            Console.WriteLine "x - exit"
            let port = port()
            let receivers = receivers(port)
            let s = new SmsSender(port, receivers, console_log, 160, "[Teade {0}] ")
            
            while true do
                Threading.Thread.Sleep(1000)
                Console.Write("Cmd:")
                let cmd = Console.ReadLine()
                if "x" = cmd then Environment.Exit 0
                elif "qm" = cmd then s.QueueMessage ("test") (text())
                elif "ss" = cmd then s.SendSms (text())
                elif "sm" = cmd then s.SendMessage (text())
                elif "sq" = cmd then s.SendQueued ("test")
                else Console.WriteLine (sprintf "Invalid command: %s" cmd)

            0
