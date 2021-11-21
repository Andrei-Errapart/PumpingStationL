namespace SmsMessenger

open System.Collections.Generic
open System.Threading
open System.Diagnostics
open System

module SmsMessenger =
    // Create and add a console debug listener.
    type DbgListener() =
        inherit ConsoleTraceListener()
        override u.Write (msg : string) = printf "[DBG] %s" msg
        override u.WriteLine (msg : string) = printfn "[DBG] %s" msg
    let console_debug_listener = new DbgListener()
    Debug.Listeners.Add(console_debug_listener) |> ignore
    Debug.AutoFlush <- true

    /// Keep sending the sms-s.
    type SmsSender (serialPort:string, receivers:string[], logf:string -> unit, smsSize:int, msgSeparator:string) =
        let max_retry_count = 10
        let max_errors_in_round = 3
        let start_time = DateTime.Now
        let queue = new BlockingQueue<string*string*int>()
        let mutable modem : ModemType option = None
        let mutable stat_round = 0
        let mutable stat_ok_open = 0
        let mutable stat_errors_open = 0
        let mutable stat_errors_send = 0
        let mutable stat_last_open_time = DateTime.MinValue
        let mutable stat_last_fail_time = DateTime.MinValue
        
        /// Sleep for the given amount of milliseconds.
        let sleep_ms (ms:int) =
            let mutable remaining = ms
            while remaining > 0 && not (queue.IsStopped()) do
                let this_round = Math.Min(100, remaining)
                System.Threading.Thread.Sleep(this_round)
                remaining <- remaining - 100
            ()
        
        /// Service the modem.
        let modem_service_thread () =
            while not (queue.IsStopped()) do
                stat_round <- stat_round + 1
                try
                    // 1. Open the new modem.
                    logf (sprintf "%s the modem at port '%s'." (if stat_round=1 then "Opening" else "Re-opening") serialPort)
                    let m : ModemType = if serialPort.Length > 0 then Serial (new SerialModem(serialPort)) else Dummy (new DummyModem())
                    modem <- Some (m)
                    stat_ok_open <- stat_ok_open + 1
                    stat_last_open_time <- DateTime.Now
                    logf (sprintf "Modem opened at port '%s'" serialPort)

                    let mutable round_errors = 0
                    while not (queue.IsStopped()) && round_errors < max_errors_in_round do
                        match (queue.Dequeue()) with
                        | Some (receiver, content, count) ->
                            begin
                                try
                                    match m with
                                    | ModemType.Serial ms -> ms.SendSms(receiver, content)
                                    | ModemType.Dummy md -> md.SendSms(receiver, content)
                                    
                                    logf (sprintf "Sending SMS \"%s\" to %s" content receiver)
                                    round_errors <- 0
                                with
                                    | ex ->
                                        begin
                                            round_errors <- round_errors + 1
                                            if count < max_retry_count then
                                                logf (sprintf "Cannot send '%s' to '%s': %s" content receiver ex.Message)
                                                queue.Enqueue(receiver, content, count+1)
                                            else
                                                logf (sprintf "Gave up on sending '%s' to '%s': %s" content receiver ex.Message)
                                        end
                            end
                        | _ -> ()

                    logf "Closing the modem."
                    modem <- None
                    match m with
                    | ModemType.Serial ms -> ms.Close()
                    | ModemType.Dummy md -> md.Close()
                with
                    | ex -> logf (ex.Message)

                if not (queue.IsStopped()) then
                    logf ("Sleep time!")
                sleep_ms 2000
            logf "Stopped."
            ()

        let th = new System.Threading.Thread(modem_service_thread)
        do
            th.Start()

        /// For sending multiple messages in one SMS, or as many as needed. Text * Message count
        member val private MessageQueue : string*int = ("", 0) with get, set

        /// Send an SMS message. Requires message length conformance to SMS spec
        member this.SendSms (message:string) =
            if queue.IsStopped() then
                raise (new ApplicationException ("SmsSender: Already closed."))
            else
                for r in receivers do
                    Debug.WriteLine (sprintf "Queuing SMS '%s' to %s" message r)
                    queue.Enqueue (r, message, 0)

        /// Send text in as many SMS messages as needed
        member this.SendMessage (text : string) =
            Debug.WriteLine (sprintf "Sending message '%s'" text)
            if text.Length > smsSize then
                let mutable message = ""
                for word in text.Split ([|" "|], System.StringSplitOptions.RemoveEmptyEntries) do
                    if word.Length + message.Length + 1 > smsSize then
                        this.SendSms (message)
                        message <- ""
                    
                    message <- (message + " " + word).Trim()
                if message.Length > 0 then
                    this.SendSms (message)
            else
                this.SendSms (text)

        /// Add text to queue, to be sent as SMS message(s)
        member this.QueueMessage (text : string) =
            Debug.WriteLine (sprintf "Queuing message '%s'" text)
            let queue = fst this.MessageQueue
            let count = snd this.MessageQueue
            this.MessageQueue <- ((queue + System.String.Format(msgSeparator, count) + text).Trim(), count + 1)

        /// Send queued text in as many SMS messages as needed
        member this.SendQueued () =
            Debug.WriteLine (sprintf "Sending queued messages")
            if snd this.MessageQueue > 0 then
                this.SendMessage (fst this.MessageQueue)
                this.MessageQueue <- ("", 0)

        /// Close the modem.
        member this.Close() =
            queue.Stop()