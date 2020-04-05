namespace Sentinam

module private Async =
    let map f value = async {
      let! v = value
      return f v
    }

    let bind f xAsync = async {
      let! x = xAsync
      return! f x
    }

type ReadStreamReply<'event,'eventId,'error> = Result<'event seq * 'eventId * 'eventId option,'error>

type ReadStream<'aggregateId,'event,'eventId,'error> = {
    AggregateId: 'aggregateId
    FirstEventId: 'eventId
    BufferSize: int
    ReplyChannel: AsyncReplyChannel<ReadStreamReply<'event,'eventId,'error>> }
module ReadStream =
    let inline create
        (aggregateId:'aggregateId)
        firstEventId 
        bufferSize
        (replyChannel:AsyncReplyChannel<ReadStreamReply<'event,'eventId,'error>>) = {
            AggregateId = aggregateId
            FirstEventId = firstEventId
            BufferSize = bufferSize
            ReplyChannel = replyChannel }

type ResultCommand<'command,'aggregate,'eventId,'error> = {
    Command: 'command
    ReplyChannel: AsyncReplyChannel<Result<'eventId * 'aggregate,'error>> }
module ResultCommand =
    let inline create command replyChannel = {
        Command = command
        ReplyChannel = replyChannel }

type ResultCommandStream<'command,'event,'eventId,'error> = {
    Command: 'command
    ReplyChannel: AsyncReplyChannel<Result<'eventId * 'event seq,'error>> }
module ResultCommandStream =
    let inline create command replyChannel = {
        ResultCommandStream.Command = command
        ResultCommandStream.ReplyChannel = replyChannel }

type GetState<'aggregateId,'eventId,'aggregate,'error> = 'aggregateId * 'eventId * AsyncReplyChannel<Result<'eventId * 'aggregate,'error>>

module GetState =
    let create aggregateId version replyChannel =
        GetState (aggregateId,version,replyChannel)

type Envelope<'command,'event,'eventId,'aggregateId,'aggregate,'error> =
    | GetState of GetState<'aggregateId,'eventId,'aggregate,'error>
    | ReadStream of ReadStream<'aggregateId,'event,'eventId,'error>
    | ResultCommand of ResultCommand<'command,'aggregate,'eventId,'error>
    | ResultCommandStream of ResultCommandStream<'command,'event,'eventId,'error>

module Envelope =
    let inline newGetState aggId ver replyChannel = GetState.create aggId ver replyChannel |> Envelope.GetState
    let inline newCmd command replyChannel = ResultCommand { Command = command; ReplyChannel = replyChannel }
    let inline newCommandStream command replyChannel = ResultCommandStream { Command = command; ReplyChannel = replyChannel }
    let inline newReadStream aggregateId firstEventId bufferSize replyChannel =
        { AggregateId = aggregateId
          FirstEventId = firstEventId
          BufferSize = bufferSize
          ReplyChannel = replyChannel }
        |> ReadStream


type Agent<'T> = MailboxProcessor<'T>

type Dispatcher<'Msg,'event> = {
    //TODO: would like to make this "inherit" MBP instead ?
    Agent: Agent<'Msg>
    Observable: System.IObservable<'event>
    CancellationTokenSource: System.Threading.CancellationTokenSource }

type Evolve<'aggregate,'event> = 'aggregate -> 'event -> 'aggregate
type Load<'aggregate,'aggregateId,'event,'eventId> = Evolve<'aggregate,'event> -> 'eventId -> 'aggregateId -> Async<'eventId*'aggregate>
type Save<'aggregateId,'eventId,'event> = 'aggregateId -> 'eventId -> 'event seq -> Async<Result<unit,exn>>
type ExnToAggregateError<'aggregateId,'aggregateError> = 'aggregateId -> exn -> 'aggregateError
type HandleCommand<'command,'aggregate,'event,'aggregateError> = 'command -> 'aggregate -> Result<'event seq, 'aggregateError>
type IncrementVersion<'event,'eventId> = 'event seq -> 'eventId -> 'eventId
type EventAction<'event> = 'event -> unit
type CommandToId<'command,'aggregateId> = 'command -> 'aggregateId
type IsInitCommand<'command,'aggregateId,'aggregateError> = 'command -> Result<'aggregateId,'aggregateError>

type CommandProcessor<'aggregate,'aggregateId,'command,'event,'eventId,'aggregateError> = {
    Evolve : Evolve<'aggregate,'event>
    Load: Load<'aggregate,'aggregateId,'event,'eventId>
    Save: Save<'aggregateId,'eventId,'event>
    ExnToAggregateError: ExnToAggregateError<'aggregateId,'aggregateError>
    HandleCommand: HandleCommand<'command,'aggregate,'event,'aggregateError>
    IncrementVersion: IncrementVersion<'event,'eventId>
    MinimumEventId: 'eventId
    ReadStream: int -> 'aggregateId -> 'eventId -> Async<'event seq * 'eventId * 'eventId option>
    // EventAction: EventAction<'event>
    // CommandToId: CommandToId<'command,'aggregateId>
    // IsInitCommand: IsInitCommand<'command,'aggregateId,'aggregateError>
}


module Common =

    let sendEvents readStream startVersion streamOut aggregateIdStr =
        let rec stream version =
            async {
            let! events, _, nextEvent =
                readStream aggregateIdStr version 500
            events |> Seq.iter(Some >> streamOut >> Async.Start)
            match nextEvent with
            | None ->
                None |> streamOut |> Async.Start
                return ()
            | Some n ->
                return! stream n }
        stream startVersion

    let getState aggregateId version replyChannel =
        GetState (aggregateId,version,replyChannel)

[<AutoOpen>]
module Sentinam =

    let getAggregateId<'command,'event,'eventId,'aggregateId,'aggregate,'error>
        (getId: 'command -> 'aggregateId)
        (envelope: Envelope<'command,'event,'eventId,'aggregateId,'aggregate,'error>) =
            match envelope with 
            | GetState (w,_,_) -> w
            | ReadStream rs -> rs.AggregateId
            | ResultCommand p -> getId p.Command
            | ResultCommandStream p -> getId p.Command

    let load<'aggregate,'event,'eventId when 'eventId : comparison>
        (uninitializedAggregate:'aggregate)
        (minEventId:'eventId)
        (subtractEventId: 'eventId -> 'eventId -> 'eventId)
        (indexSeq: seq<'event> -> seq<'eventId*'event>)
        (readStream: 'eventId -> Async<'event seq * 'eventId * 'eventId option>)
        (evolve:'aggregate -> 'event -> 'aggregate)
        maxVer =
            let takeUpTo (mn:'eventId) (mx:'eventId) =
                let f = indexSeq >> Seq.takeWhile(fun (i,_) -> i < subtractEventId mx mn) >> Seq.map snd
                if mx = minEventId then id else f
            let rec fold state version = async {
                let! events, (lastEvent:'eventId), nextEvent =
                    readStream version
                let state = events |> takeUpTo version maxVer |> Seq.fold evolve state
                match nextEvent with
                | None -> return lastEvent, state
                | Some n -> return! fold state n }
            fold uninitializedAggregate minEventId


    // this is the "repository"
    let inline internal save appendToStream aggregateId (expectedVersion:'eventId) events =
        appendToStream aggregateId expectedVersion events
    
    //let inline internal processReadStream (rdStrm:ReadStream<'aggregateId,'aggregateEvent,'eventId,'aggregateError>) readStream =
    //    readStream rdStrm.AggregateId rdStrm.FirstEventId rdStrm.BufferSize
    //    |> Async.map(Ok >> rdStrm.ReplyChannel.Reply) |> Async.Start

    let createAgent<'aggregate,'aggregateId,'command,'event,'eventId,'aggregateError when 'eventId : equality>
        (cp: CommandProcessor<'aggregate,'aggregateId,'command,'event,'eventId,'aggregateError>)
        (sendToObservers: EventAction<'event>)
        (cancellationToken: System.Threading.CancellationToken)
        (aggregateId:'aggregateId)
        =
            let getResult version state (reply:Result<('eventId*'aggregate*'event seq),'aggregateError> -> unit) (eventResult:Result<'event seq,'aggregateError>) = async {
              match eventResult with
                | Ok events ->
                    match! cp.Save aggregateId version events with // save eventStore.AppendToStream workflowId version events with
                        | Ok (_:unit) ->
                            let newState = Seq.fold cp.Evolve state events
                            let newVersion = cp.IncrementVersion events version
                            (newVersion,newState,events) |> Ok |> reply
                            events |> Seq.iter sendToObservers
                            return (newVersion, newState)
                        | Error e ->
                            cp.ExnToAggregateError aggregateId e |> Error |> reply
                            return (version,state)
                | Error e -> 
                    e |> Error |> reply
                    return (version,state) }
            Agent.Start ((fun inbox ->
                let rec loop (version,state) = async {
                    let getResult = getResult version state
                    match! inbox.Receive() with
                    | GetState (_,ver,replyChannel) ->
                        match ver = version || ver = cp.MinimumEventId with
                          | true -> async { return (version,state) }
                          | false ->
                            cp.Load cp.Evolve ver aggregateId
                            //load eventStore.ReadStream (EventId ver) takeUpTo (streamIdString workflowId)
                        |> Async.map (Ok >> replyChannel.Reply) |> Async.Start
                        return! loop (version,state)
                    | ReadStream (rdStrm) ->
                        cp.ReadStream rdStrm.BufferSize rdStrm.AggregateId rdStrm.FirstEventId
                        |> Async.map (Ok >> rdStrm.ReplyChannel.Reply) |> Async.Start
                        return! loop (version,state)
                    | ResultCommand command ->
                        let replyFunc = ((Result.map (fun (newVersion,newState,_) -> (newVersion,newState))) >> command.ReplyChannel.Reply)
                        return! state |> cp.HandleCommand command.Command |> getResult replyFunc |> Async.bind loop
                    | ResultCommandStream command ->
                        let replyFunc = ((Result.map (fun (newVersion,_,eList) -> (newVersion,eList))) >> command.ReplyChannel.Reply)
                        return! state |> cp.HandleCommand command.Command |> getResult replyFunc |> Async.bind loop
                    }
                cp.Load cp.Evolve cp.MinimumEventId aggregateId |> Async.bind loop)
              , cancellationToken)
    
    let createDispatcherAgent<'aggregate,'aggregateId,'event,'eventId,'command,'aggregateError when 'aggregateId : comparison>
        (commandToId: CommandToId<'command,'aggregateId>)
        (cancellationToken:System.Threading.CancellationToken)
        (storeAggregate:
            Envelope<'command,'event,'eventId,'aggregateId,'aggregate,'aggregateError>
            -> Map<'aggregateId,Agent<Envelope<'command,'event,'eventId,'aggregateId,'aggregate,'aggregateError>>>
            -> 'aggregateId
            -> (unit -> Agent<Envelope<'command,'event,'eventId,'aggregateId,'aggregate,'aggregateError>>)
            -> Async<Map<'aggregateId,Agent<Envelope<'command,'event,'eventId,'aggregateId,'aggregate,'aggregateError>>>>)
        (startPump: System.Threading.CancellationToken -> 'aggregateId -> Agent<Envelope<'command,'event,'eventId,'aggregateId,'aggregate,'aggregateError>>)
        =
            let forward (agent: Agent<_>) command = agent.Post command
            Agent.Start ((fun inbox ->
                let rec loop aggregateAgents = async {
                    let! command = inbox.Receive()
                    let iD = getAggregateId commandToId command
                    match Map.tryFind iD aggregateAgents with
                    | Some aggregateAgent ->
                        forward aggregateAgent command
                        return! loop aggregateAgents
                    | None ->
                        let! aggregates = storeAggregate command aggregateAgents iD (fun () -> startPump cancellationToken iD)
                        return! loop aggregates

                        //let aggregateAgent = startPump cancellationToken iD
                        //forward aggregateAgent command
                        //return! loop (Map.add iD aggregateAgent aggregateAgents)
                }
                loop Map.empty)
                , cancellationToken)
