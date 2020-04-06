namespace Sentinam.Persistence

type VersionMisMatchException<'eventId> (expected:'eventId,actual:'eventId) =
    inherit exn(sprintf "version %O expected, recieved %O" expected actual)
  with
    member __.Expected = expected
    member __.Actual = actual


type BatchSize = int

type NextEvent<'eventId> = 'eventId option 

type StreamDataStore<'key,'event,'eventId> = {
    ReadStream: BatchSize -> 'key -> 'eventId -> Async<'event seq * 'eventId * NextEvent<'eventId>>
    AppendToStream: 'key -> 'eventId -> seq<'event> -> Async<Result<unit,exn>> }

type RepositoryError =
    | MaxRepositoryFetchExceeded of int

type Repository<'key,'value when 'key : comparison> = {
    Retrieve: 'key -> Async<'value option>
    RetrieveItems: ('key -> 'value -> bool) -> System.IObservable<'key*'value>
    RetrieveItemsPaged: ('key -> 'value -> bool) -> bigint -> int -> System.IObservable<'key*'value>
    Save: 'key -> 'value -> Async<Result<unit,exn>> }
