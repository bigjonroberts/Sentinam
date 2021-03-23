namespace MsSqlStreamDataStore

open Sentinam.Persistence
open SqlStreamStore
open SqlStreamStore.Streams
open FsToolkit.ErrorHandling

module MsSqlStreamDataStore =
    type private JsonConvert = Newtonsoft.Json.JsonConvert
    
    let private readStream<'a> (streamStore: MsSqlStreamStoreV3) maxCount streamId version : Async<'a seq * int * NextEvent<int>> =                                 
        async {
                let! events = streamStore.ReadStreamForwards(StreamId streamId, version, maxCount + 1) |> Async.AwaitTask
                let! parsedData = 
                    events.Messages 
                    |> Seq.map (fun event -> 
                                    event.GetJsonData() 
                                    |> Async.AwaitTask
                                    |> Async.map (fun jsonData -> JsonConvert.DeserializeObject<'a>(jsonData)))
                    |> Async.Parallel    
                let nextVersionCandidate = 
                    if  Array.length parsedData <= maxCount then 
                        None 
                    else                        
                        Some (version + maxCount + 1) 
                return (Seq.ofArray parsedData |> Seq.truncate maxCount, Array.length parsedData, nextVersionCandidate)                                                            
        }

    let private appendToStream<'a> (streamStore: MsSqlStreamStoreV3) (streamId: string) (expectedVersion:int) (newEvents: 'a seq) =
        let expectedVersion =
            match expectedVersion with
            | 0 -> ExpectedVersion.NoStream
            | _ -> expectedVersion
        async {
            try       
                for eventData in newEvents do            
                    let message = NewStreamMessage (System.Guid.NewGuid(), nameof<'a>, JsonConvert.SerializeObject eventData )
                    //todo: Need to exam appendToStream result and raise an exception
                    let! appendResult = streamStore.AppendToStream ( streamId, expectedVersion, message) |> Async.AwaitTask
                    ()
                return Ok()
                          
            with
            | e -> return (Error e)
        }            
        
    let create<'a> streamStore =
        { StreamDataStore.ReadStream = readStream<'a> streamStore
          StreamDataStore.AppendToStream = appendToStream<'a> streamStore }