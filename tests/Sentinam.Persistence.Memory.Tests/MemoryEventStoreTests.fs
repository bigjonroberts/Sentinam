module MemoryStoreTests

open Xunit
open Sentinam.Persistence.Memory
open TestUtil
open System

type TestId = TestId of System.Guid

type TestEvent =
  | Turtle
  | Complex of Complex
  | Simple of int
and Complex = {
    A:System.Guid
    Hare: Map<int,string> }

let testStore = MemoryEventStore.create<TestId,TestEvent,uint64> ()
let getTestId () = System.Guid.NewGuid () |> TestId

[<Fact>]
let ``unsaved stream yields empty event list`` () =
    testStore.ReadStream 1000 (System.Guid.Empty |> TestId) 0UL
    |> Async.map(fun (readList,version,nextVersion) ->
            Assert.True(Seq.isEmpty readList)
            Assert.Equal(None,nextVersion)
            Assert.Equal(0UL,version))

[<Fact>]
let ``version mismatch`` () =
    let testId = getTestId ()
    async {
        let! result = [ Turtle ] |> Seq.ofList |> testStore.AppendToStream testId 1UL
        fun () -> result |> Result.mapError(fun e -> raise e) |> ignore
        |> expectExn<Sentinam.Persistence.VersionMisMatchException<uint64>> }

[<Fact>]
let ``write, append, and read back events`` () =
    let testId = getTestId ()
    let expectedVersion = 0UL
    let inputEvents =
        [ Turtle
          Complex { A = System.Guid.NewGuid (); Hare = Map.empty<int,string> }
          Simple 573 ]
    async {
        match! inputEvents |> Seq.ofList |> testStore.AppendToStream testId expectedVersion with
        | Error e -> raise e
        | Ok () ->
          let! readList,version,nextVersion = testStore.ReadStream 1000 testId 0UL
          Assert.Equal(3,Seq.length readList)
          Assert.Equal(None,nextVersion)
          Assert.Equal(3UL,version)
          readList |> Seq.zip inputEvents
          |> Seq.iter (fun (a,b) -> Assert.Equal(a,b))
          let appendEvents = [ Turtle; Turtle; Turtle ]
          match! appendEvents |> Seq.ofList |> testStore.AppendToStream testId 3UL with
          | Error e -> raise e
          | Ok () ->
            // only read back the three events we just appended
            let! endList,endVersion,endNextVersion = testStore.ReadStream 1000 testId 4UL
            Assert.Equal(3,Seq.length endList)
            Assert.Equal(None,endNextVersion)
            Assert.Equal(6UL,endVersion)
            endList |> Seq.zip appendEvents
            |> Seq.iter (fun (a,b) -> Assert.Equal(a,b))
            // now read back all events
            let expectedAllEvents = inputEvents @ appendEvents
            let! allList,allVersion,allNextVersion = testStore.ReadStream 1000 testId 0UL
            Assert.Equal(6,Seq.length allList)
            Assert.Equal(None,allNextVersion)
            Assert.Equal(6UL,allVersion)
            allList |> Seq.zip expectedAllEvents
            |> Seq.iter (fun (a,b) -> Assert.Equal(a,b)) }

[<Fact>]
let ``read list larger than buffer`` () =
    let testId = getTestId ()
    let inputEvents =
        [ Turtle
          Complex { A = System.Guid.NewGuid (); Hare = Map.empty<int,string> }
          Simple 573 ]
    async {
        match! inputEvents |> Seq.ofList |> testStore.AppendToStream testId 0UL with
        | Error e -> raise e
        | Ok () ->
          let! readList,version,nextVersion = testStore.ReadStream 2 testId 0UL
          Assert.Equal(2,Seq.length readList)
          Assert.Equal(Some 3UL,nextVersion)
          Assert.Equal(2UL,version)
        return () }
