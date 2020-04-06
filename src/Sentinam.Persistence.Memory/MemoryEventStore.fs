namespace Sentinam.Persistence.Memory

open System.Collections.Concurrent
open Sentinam.Persistence

module MemoryEventStore =
    
    /// MemoryStore.StreamReader will internally go through the full eventList every time it's called
    /// It's inefficient
    /// It also only supports int32 event addresses
    /// But that's OK it's really just a mock/sample implementation for testing and development
    let private readStream (store:ConcurrentDictionary<'a,'b list>) count streamId version =
        let takeUpTo mx = 
            match mx with 
            | 0 -> id 
            | _ -> Seq.indexed >> Seq.takeWhile(fun (i,_) -> i < mx) >> Seq.map snd
        match store.TryGetValue streamId with
        | false, _ -> (Seq.empty<'b>, 0UL, None) |> Async.result
        | true, value ->
            let stream =
                value |> Seq.indexed |>
                match version with
                | 0UL -> id
                | _ -> Seq.skip (version - 1UL |> int)
            let iEvents = stream |> takeUpTo count
            let events = iEvents |> Seq.map snd
            let lastEventNumber = iEvents |> Seq.last |> fst |> (+) 1
            let nextEventNumber =
                let nextEventCandidate = stream |> takeUpTo (count+1) |> Seq.last |> fst |> (+) 1
                match lastEventNumber = nextEventCandidate with
                    | true -> None
                    | false -> nextEventCandidate |> uint64 |> Some
            (events, uint64 lastEventNumber, nextEventNumber) |> Async.result

    let private appendToStream (store: ConcurrentDictionary<'a,'b list>) streamId (expectedVersion:uint64) newEvents = 
        // AddOrUpdate returns the same type as input,
        //     so we have to raise the exception to then catch it and pass the error type back out,
        //   unless we wanted to store Result type, which we don't
        // Once again, this is really just a mock/sample implementation for testing and development
        let addValueFactory _ =
            if expectedVersion = 0UL then Seq.toList newEvents
            else raise (VersionMisMatchException (expectedVersion,0UL))
        let updateValueFactory _ stream =
            let version = Seq.length stream |> uint64
            if version = expectedVersion then
                stream @ (Seq.toList newEvents)
            else raise (VersionMisMatchException (expectedVersion,version))
        try
            match Seq.isEmpty newEvents with
            | true -> () // means we don't add or append for empty events
            | false -> store.AddOrUpdate(streamId, addValueFactory, updateValueFactory) |> ignore
            |> Ok
        with
            | e -> Error e
        |> Async.result

    let create<'a,'b,'c> () =
        let store = ConcurrentDictionary<'a,'b list> ()
        { ReadStream = readStream store
          AppendToStream = appendToStream store }
