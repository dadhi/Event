﻿namespace Lloyd.Core

open System
open System.Diagnostics

type UserID = private User of int

module User =
    let private map = Map.ofList [1,"admin"] |> ref
    let login name =
        match Map.tryFindKey (fun _ -> (=)name) !map with
        | Some userID -> User userID
        | None ->
            let userID = Map.toSeq !map |> Seq.map fst |> Seq.max |> (+)1
            map := Map.add userID name !map
            User userID
    let name (User userID) = Map.find userID !map

type EventID = private | EventID of time:DateTime * user:UserID
               static member Zero = EventID(DateTime.MinValue,User 0)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module EventID =
    let private timestampAndTicks0 = // Better resolution and can syncronise to NTP server on startup
        Stopwatch.GetTimestamp(), let d = DateTime.UtcNow in d.Ticks
    let private ticks() =
        let timestamp0,ticks0 = timestampAndTicks0
        ticks0+((Stopwatch.GetTimestamp()-timestamp0)*TimeSpan.TicksPerSecond)/Stopwatch.Frequency
    let lastTicks = ref 0L
    let internal gen user =
        let ticks = atomicUpdateInt64 (fun oldTicks ->
                        let newTicks = ticks()
                        if oldTicks=newTicks then oldTicks+1L
                        else newTicks) lastTicks |> snd
        EventID(DateTime ticks, user)
    let time (EventID(t,_)) = t
    let User (EventID(_,u)) = u

type 'Aggregate ID = private Created of EventID

module ID =
    let internal gen eventID = Created eventID

type 'Aggregate Events = (EventID * 'Aggregate list1) list1

[<NoEquality;NoComparison>]
type 'Aggregate MemoryStore = {Updates: Map<'Aggregate ID,'Aggregate Events>; Observers: IObserver<'Aggregate ID*'Aggregate Events> list}

[<NoEquality;NoComparison>]
type 'Aggregate Store =
    | MemoryStore of 'Aggregate MemoryStore ref

module Store =
    let emptyMemoryStore() = {Updates=Map.empty; Observers=[]} |> ref |> MemoryStore
    let observable (store:'Aggregate Store) =
        match store with
        | MemoryStore storeRef ->
            {new IObservable<_> with
                member __.Subscribe(ob:IObserver<_>) =
                    let _,newStore = atomicUpdate (fun i -> {Updates=i.Updates; Observers=ob::i.Observers}) storeRef
                    Map.toSeq newStore.Updates |> Seq.iter ob.OnNext
                    {new IDisposable with
                        member __.Dispose() =
                            atomicUpdate (fun i -> {Updates=i.Updates; Observers=List.where ((<>)ob) i.Observers}) storeRef |> ignore
                    }
            }

    type Error =
        | Concurrency

    let update (user:UserID) (aggregateID:'Aggregate ID) (updates:'Aggregate list1) (lastEvent:EventID) (store:'Aggregate Store) =
        match store with
        | MemoryStore storeRef ->
            let newStore,result =
                atomicUpdateQuery (fun store ->
                    let l = Map.find aggregateID store.Updates
                    if List1.head l |> fst = lastEvent then
                        let eventID = EventID.gen user
                        {Updates=Map.add aggregateID (List1.init (eventID,updates) (List1.toList l)) store.Updates; Observers=store.Observers}, Ok ()
                    else store, Error Concurrency
                ) storeRef
            if Result.isOk result then newStore.Observers |> Seq.iter (fun ob -> ob.OnNext(aggregateID,Map.find aggregateID newStore.Updates))
            result

    let create (user:UserID) (updates:'Aggregate list1) (store:'Aggregate Store) : Result<_,Error> =
        match store with
        | MemoryStore storeRef ->
            let newStore,result =
                atomicUpdateQuery (fun store ->
                    let eventID = EventID.gen user
                    let aggregateID = ID.gen eventID
                    {Updates=Map.add aggregateID (List1.singleton (eventID,updates)) store.Updates; Observers=store.Observers}, Ok aggregateID
                ) storeRef
            match result with
            | Ok aggregateID -> newStore.Observers |> Seq.iter (fun ob -> ob.OnNext(aggregateID,Map.find aggregateID newStore.Updates))
            | Error _ -> ()
            result

    let getAll (store:'Aggregate Store) =
        match store with
        | MemoryStore storeRef ->
            storeRef.Value.Updates

type 'a SetEvent =
    | SetAdd of 'a
    | SetRemove of 'a

module SetEvent =
    let difference (before:'a Set) (after:'a Set) =
        Seq.append (after-before |> Set.toSeq |> Seq.map SetAdd) (before-after |> Set.toSeq |> Seq.map SetRemove) |> Seq.toList

    let update events s =
        let removedAndAdded =
            List.fold (fun (removed,added) se ->
                    match se with
                    | SetAdd a -> if Set.contains a removed then removed,added else removed,Set.add a added
                    | SetRemove a -> Set.add a removed,added
               ) (Set.empty,Set.empty) events
        Set.fold (fun (removed,added) a -> if Set.contains a removed then removed,added else removed,Set.add a added) removedAndAdded s
        |> snd

    let toSet (events:'a SetEvent Events) =
        List1.toList events
        |> Seq.map snd
        |> Seq.fold (List1.fold (fun (removed,added) (se:'a SetEvent) ->
                match se with
                | SetAdd a -> if Set.contains a removed then removed,added else removed,Set.add a added
                | SetRemove a -> Set.add a removed,added
           )) (Set.empty,Set.empty)
        |> snd

type MapEvent<'k,'v> =
    | MapAdd of 'k * 'v
    | MapRemove of 'k

module MapEvent =
    let update events m =
        let removedAndAdded =
            List1.fold (fun (removed,added) se ->
                    match se with
                    | MapAdd (k,v) -> if Set.contains k removed then removed,added else removed,Map.add k v added
                    | MapRemove k -> Set.add k removed,added
               ) (Set.empty,Map.empty) events
        Map.fold (fun (removed,added) k v -> if Set.contains k removed then removed,added else removed,Map.add k v added) removedAndAdded m
        |> snd

[<NoEquality;NoComparison>]
type Property<'a,'b> = {Name:string; Getter:'a->'b option; Setter:'b->'a; Validation:'b option->Result<'b,'a*string>}

module Property =
    let create name setter getter validation = {Name=name; Getter=getter; Setter=setter; Validation=validation}
    let set (property:Property<'a,'b>) v = property.Setter v
    let get (property:Property<'a,'b>) (updates:'a Events) =
        List1.toList updates |> List.tryPick (snd >> List1.tryPick property.Getter)
    let getAndValidate (property:Property<'a,'b>) (updates:'a Events) =
        get property updates |> property.Validation
    let tryGetEvents (property:Property<'a,'b>) (update:'a Events) : 'b Events option =
        List1.tryChoose (fun (e,l) -> List1.tryChoose property.Getter l |> Option.map (addFst e)) update