namespace  Sentinam.Persistence.Memory

module MemoryRepository =
    open System.Collections.Concurrent
    open Sentinam.Persistence
    open FSharp.Control.Reactive.Builders

    let private readItem (store:ConcurrentDictionary<'a,'b>) itemId =
        match store.TryGetValue itemId with
        | false, _ -> None 
        | true, value -> value |> Some
        |> Async.result

    let private addOrUpdateItem (store: ConcurrentDictionary<'a,'b>) itemId newValue =
        // AddOrUpdate returns the same type as input, so we have to raise the exception to
        // then catch it and pass the error type back out
        // unless we wanted to store Result type, which we don't
        try
            store.AddOrUpdate(itemId,(fun _ -> newValue),(fun _ _ -> newValue)) |> ignore
            |> Ok
        with
            | e -> Error e
        |> Async.result

    let private retrieveItems (store: ConcurrentDictionary<'a,'b>) filter =
            let rec obsv (dict:System.Collections.Generic.KeyValuePair<'a,'b> seq) =
                observe {
                    match Seq.tryHead dict with
                    | Some x ->
                        if filter x.Key x.Value then
                            yield (x.Key, x.Value)
                            yield! dict |> Seq.skip 1 |> obsv
                    | _ -> ()
                }
            obsv store
    
    let private retrieveItemsPaged (store: ConcurrentDictionary<'a,'b>) filter startAt pageSize =
            let mutable i = bigint 0
            let tail =
                store // we'll do it this way because Seq.skip raises exceptions
                |> Seq.filter (fun kvp -> filter kvp.Key kvp.Value)
                |> Seq.skipWhile (fun _ ->
                    let i' = i
                    i <- 1 |> bigint |> (+) i
                    i' < startAt )
            let rec obsv remainingItems (dict:System.Collections.Generic.KeyValuePair<'a,'b> seq) =
                observe {
                    match (Seq.tryHead dict, remainingItems) with
                    | (Some x, remaining) when 0 < remaining ->
                        if remaining > 0 && filter x.Key x.Value then
                            yield (x.Key, x.Value)
                            yield! dict |> Seq.skip 1 |> obsv (remaining - 1)
                    | _ -> ()
                }
            obsv pageSize tail

    let create<'a,'b when 'a : comparison> () =
        let store = ConcurrentDictionary<'a,'b> ()
        { Retrieve = readItem store
          RetrieveItems = retrieveItems store
          RetrieveItemsPaged = retrieveItemsPaged store
          Save = addOrUpdateItem store }
