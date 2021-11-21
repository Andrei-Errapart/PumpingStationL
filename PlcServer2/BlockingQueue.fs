namespace PlcServer2
    open System.Collections.Generic
    open System.Threading

    /// Stoppable blocking Queue.
    /// Source: http://element533.blogspot.com/2010/01/stoppable-blocking-queue-for-net.html
    type BlockingQueue<'T> () =
        let queue = new Queue<'T>()
        let mutable stopped = false

        /// Enqueue, if possible.
        member this.Enqueue (item:'T) =
            if not stopped then
                lock queue (fun () ->
                    if not stopped then
                        queue.Enqueue(item)
                        Monitor.Pulse(queue)
                )

        /// Dequeue something. When stopped, return None.
        member this.Dequeue() =
            if stopped then None
            else
                lock queue (fun () ->
                    while (not stopped) && queue.Count=0 do
                        Monitor.Wait(queue) |> ignore
                    if (not stopped) && (queue.Count>0) then Some (queue.Dequeue()) else None
                )
 
        /// Signals stop to all consumers.
        member this.Stop() =
            if not stopped then
                lock queue (fun () ->
                    if not stopped then
                        stopped <- true
                        Monitor.PulseAll(queue)
                )

        member this.IsStopped() = stopped
