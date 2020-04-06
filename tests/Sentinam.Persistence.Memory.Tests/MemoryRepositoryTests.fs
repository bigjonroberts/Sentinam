module MemoryRepositoryTests

open Sentinam.Persistence.Memory
open Xunit

type DummyEvent =
    | Dummy1
    | Dummy2

type RecordThatSupportsComparison = {
    Field1: string
    Field2: int }

[<Fact>]
let ``project versions from dispatcher`` () =
    let cts = new System.Threading.CancellationTokenSource ()

    let (observable,broadcast) = Sentinam.Observable.createObservableAgent<DummyEvent> 0 cts.Token
    //let dispatcher = MemoryEventStore.create () |> Workflow.createDispatcherAgent broadcast cts.Token
    let repo = MemoryRepository.create<int,int> ()
    //observable.Add (Workflow.versionProjection dispatcher repo.Save)
    ()