namespace  Sentinam

// from http://www.fssnip.net/nC/title/Observable-Agent
module Observable =
    open System

    let createObservableAgent<'T> maxQueueDepth (token:System.Threading.CancellationToken) =
        let finished = ref false
        let subscribers = ref (Map.empty : Map<int, IObserver<'T>>)

        let inline publish msg =
            !subscribers 
            |> Seq.iter (fun (KeyValue(_, sub)) ->
                try
                        sub.OnNext(msg)
                with ex -> 
                    System.Diagnostics.Debug.Write(ex)
                    sub.OnError(ex))

        let completed () =
            lock subscribers (fun () ->
            finished := true
            !subscribers |> Seq.iter (fun (KeyValue(_, sub)) -> sub.OnCompleted())
            subscribers := Map.empty)

        token.Register (fun () -> completed()) |> ignore //callback for when token is cancelled
            
        let count = ref 0
        let agent =
            MailboxProcessor.Start
                ((fun inbox ->
                    async {
                        while true do
                            let! msg = inbox.Receive()
                            publish msg } ),
                    token)
        let obs =
            { new IObservable<'T> with
                member __.Subscribe(obs) =
                    let key1 =
                        lock subscribers (fun () ->
                            if !finished then failwith "Observable has already completed"
                            let key1 = !count
                            count := !count + 1
                            subscribers := subscribers.Value.Add(key1, obs)
                            key1)
                    { new IDisposable with  
                        member __.Dispose() = 
                            lock subscribers (fun () -> 
                                subscribers := subscribers.Value.Remove(key1)) } }
        let post x = if maxQueueDepth = 0 || agent.CurrentQueueLength < maxQueueDepth then agent.Post x else printfn "queue depth exceed %d" maxQueueDepth
        obs,post
(*
#load "ObservableExtensions.fs"
open System
let cts = new System.Threading.CancellationTokenSource()
type Data = {Value:string}

let observable,fPost = Observable.createObservableAgent<Data> cts.Token

let sub1 = 
    observable.Subscribe
        ({new IObserver<Data> with
            member x.OnNext msg = printfn "sub1 received msg %A" msg
            member x.OnError(e) = ()
            member x.OnCompleted() = printfn "sub1 received OnCompleted"
        })
let sub2 = 
    observable.Subscribe
        ({new IObserver<Data> with
            member x.OnNext msg = printfn "sub2 received msg %A" msg
            member x.OnError(e) = ()
            member x.OnCompleted() = printfn "sub2 received OnCompleted"
        })

for i in 1 .. 10 do fPost {Value=i.ToString()}

sub1.Dispose()

for i in 11 .. 14 do fPost {Value=i.ToString()}

cts.Cancel() //sends OnCompleted

*)
