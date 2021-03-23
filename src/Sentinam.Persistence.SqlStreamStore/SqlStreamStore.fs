namespace Sentinam.Persistence

open Sentinam.Persistence
open SqlStreamStore
open SqlStreamStore.Streams

module SqlStreamDataStore =
    type private JsonConvert = Newtonsoft.Json.JsonConvert

    let private readStream<'a> (streamStore: IStreamStore) maxCount streamId version : Async<'a seq * int * NextEvent<int>> =
        async {
                let! events = streamStore.ReadStreamForwards(StreamId streamId, version, if maxCount < System.Int32.MaxValue then maxCount + 1 else System.Int32.MaxValue) |> Async.AwaitTask
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
                        if uint32(version) + uint32(maxCount) + 1u > uint32(System.Int32.MaxValue) then
                            System.Int32.MaxValue
                        else
                            version + maxCount + 1
                        |> Some
                return (Seq.ofArray parsedData |> Seq.truncate maxCount, Array.length parsedData, nextVersionCandidate)
        }

    let private appendToStream<'a> (streamStore: IStreamStore) (streamId: string) (expectedVersion: int) (newEvents: 'a seq) =
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
