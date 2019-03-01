namespace Graphscriber.AspNetCore

open System
open System.Text
open Newtonsoft.Json.Linq
open Newtonsoft.Json
open Newtonsoft.Json.Serialization
open Microsoft.FSharp.Reflection
open System.Collections.Generic
open FSharp.Data.GraphQL.Execution
open Microsoft.FSharp.Control
open System.Threading.Tasks
open System.Threading
open FSharp.Data.GraphQL
open System.Net.WebSockets
open System.Collections.Concurrent
open System.Linq
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

[<AutoOpen>]
module internal Helpers =
    let tee f x =
        f x
        x

[<AutoOpen>]
module internal StringHelpers =
    let utf8String (bytes : byte seq) =
        bytes
        |> Seq.filter (fun i -> i > 0uy)
        |> Array.ofSeq
        |> Encoding.UTF8.GetString

    let utf8Bytes (str : string) = str |> Encoding.UTF8.GetBytes
    let isNullOrWhiteSpace (str : string) = String.IsNullOrWhiteSpace(str)

[<AutoOpen>]
module internal JsonHelpers =
    let tryGetJsonProperty (jobj : JObject) prop =
        match jobj.Property(prop) with
        | null -> None
        | p -> Some(p.Value.ToString())

type GQLClientMessage =
    | ConnectionInit
    | ConnectionTerminate
    | Start of id : string * payload : GQLQuery
    | Stop of id : string

    static member SerializationSettings =
        JsonSerializerSettings
            (Converters = [| GQLClientMessageConverter()
                             OptionConverter() |],
             ContractResolver = CamelCasePropertyNamesContractResolver())

    member this.ToJsonString() =
        JsonConvert.SerializeObject
            (this, GQLClientMessage.SerializationSettings)
    static member FromJsonString(json : string) =
        JsonConvert.DeserializeObject<GQLClientMessage>
            (json, GQLClientMessage.SerializationSettings)

and GQLServerMessage =
    | ConnectionAck
    | ConnectionError of err : string
    | Data of id : string * payload : Output
    | Error of id : string option * err : string
    | Complete of id : string

    static member SerializationSettings =
        JsonSerializerSettings
            (Converters = [| GQLServerMessageConverter()
                             OptionConverter() |],
             ContractResolver = CamelCasePropertyNamesContractResolver())

    member this.ToJsonString() =
        JsonConvert.SerializeObject
            (this, GQLServerMessage.SerializationSettings)
    static member FromJsonString(json : string) =
        JsonConvert.DeserializeObject<GQLServerMessage>
            (json, GQLServerMessage.SerializationSettings)

and GQLQuery =
    { Query : string
      Variables : Map<string, obj> }
    static member SerializationSettings =
        JsonSerializerSettings
            (Converters = [| OptionConverter() |],
             ContractResolver = CamelCasePropertyNamesContractResolver())
    member this.ToJsonString() =
        JsonConvert.SerializeObject(this, GQLQuery.SerializationSettings)
    static member FromJsonString(json : string) =
        JsonConvert.DeserializeObject<GQLQuery>
            (json, GQLQuery.SerializationSettings)

and [<Sealed>] OptionConverter() =
    inherit JsonConverter()
    override __.CanConvert(t) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

    override __.WriteJson(writer, value, serializer) =
        let value =
            if value = null then null
            else
                let _, fields =
                    FSharpValue.GetUnionFields(value, value.GetType())
                fields.[0]
        serializer.Serialize(writer, value)

    override __.ReadJson(reader, t, _, serializer) =
        let innerType = t.GetGenericArguments().[0]

        let innerType =
            if innerType.IsValueType then
                (typedefof<Nullable<_>>).MakeGenericType([| innerType |])
            else innerType

        let value = serializer.Deserialize(reader, innerType)
        let cases = FSharpType.GetUnionCases(t)
        if value = null then FSharpValue.MakeUnion(cases.[0], [||])
        else FSharpValue.MakeUnion(cases.[1], [| value |])

and [<Sealed>] GQLQueryConverter() =
    inherit JsonConverter()
    override __.CanConvert(t) = t = typeof<GQLQuery>

    override __.WriteJson(writer, value, _) =
        let casted = value :?> GQLQuery
        writer.WritePropertyName("query")
        writer.WriteValue(casted.Query)
        if not (Map.isEmpty casted.Variables) then
            writer.WritePropertyName("variables")
            writer.WriteStartObject()
            casted.Variables
            |> Seq.iter (fun var ->
                   writer.WritePropertyName(var.Key)
                   writer.WriteValue(var.Value))
            writer.WriteEndObject()

    override __.ReadJson(reader, _, _, _) =
        let jobj = JObject.Load reader
        let query = jobj.Property("query").Value.ToString()
        let variables =
            jobj.Property("variables").Value.ToString()
            |> JsonConvert.DeserializeObject<Map<string, obj>>
        upcast { Query = query
                 Variables = variables }

and [<Sealed>] GQLClientMessageConverter() =
    inherit JsonConverter()
    override __.CanConvert(t) =
        t = typedefof<GQLClientMessage>
        || t.DeclaringType = typedefof<GQLClientMessage>

    override __.WriteJson(writer, obj, _) =
        let msg = obj :?> GQLClientMessage
        let jobj = JObject()
        match msg with
        | ConnectionInit -> jobj.Add(JProperty("type", "connection_init"))
        | ConnectionTerminate ->
            jobj.Add(JProperty("type", "connection_terminate"))
        | Start(id, payload) ->
            jobj.Add(JProperty("type", "start"))
            jobj.Add(JProperty("id", id))
            jobj.Add(JProperty("payload", payload.ToJsonString()))
        | Stop id ->
            jobj.Add(JProperty("type", "stop"))
            jobj.Add(JProperty("id", id))
        jobj.WriteTo(writer)

    override __.ReadJson(reader, _, _, _) =
        let jobj = JObject.Load reader
        let typ = jobj.Property("type").Value.ToString()
        match typ with
        | "connection_init" -> upcast ConnectionInit
        | "connection_terminate" -> upcast ConnectionTerminate
        | "start" ->
            let id = jobj.Property("id").Value.ToString()
            let payload = jobj.Property("payload").Value.ToString()
            upcast Start(id, GQLQuery.FromJsonString(payload))
        | "stop" ->
            let id = jobj.Property("id").Value.ToString()
            upcast Stop(id)
        | t ->
            raise
            <| InvalidOperationException
                   (sprintf "Message Type %s is not supported." t)

and [<Sealed>] GQLServerMessageConverter() =
    inherit JsonConverter()
    override __.CanConvert(t) =
        t = typedefof<GQLServerMessage>
        || t.DeclaringType = typedefof<GQLServerMessage>

    override __.WriteJson(writer, value, _) =
        let value = value :?> GQLServerMessage
        let jobj = JObject()
        match value with
        | ConnectionAck -> jobj.Add(JProperty("type", "connection_ack"))
        | ConnectionError err ->
            let errObj = JObject()
            errObj.Add(JProperty("error", err))
            jobj.Add(JProperty("type", "connection_error"))
            jobj.Add(JProperty("payload", errObj))
        | Error(id, err) ->
            let errObj = JObject()
            errObj.Add(JProperty("error", err))
            jobj.Add(JProperty("type", "error"))
            jobj.Add(JProperty("payload", errObj))
            jobj.Add(JProperty("id", id))
        | Data(id, result) ->
            jobj.Add(JProperty("type", "data"))
            jobj.Add(JProperty("id", id))
            jobj.Add(JProperty("payload", JObject.FromObject(result)))
        | Complete(id) ->
            jobj.Add(JProperty("type", "complete"))
            jobj.Add(JProperty("id", id))
        jobj.WriteTo(writer)

    override __.ReadJson(reader, _, _, serializer) =
        let format (payload : Output) : Output =
            let rec helper (data : obj) : obj =
                match data with
                | :? JObject as jobj ->
                    upcast (jobj.ToObject<Dictionary<string, obj>>(serializer)
                            |> Seq.map
                                   (fun kval ->
                                   KeyValuePair<string, obj>
                                       (kval.Key, helper kval.Value))
                            |> Array.ofSeq
                            |> NameValueLookup)
                | :? JArray as jarr ->
                    upcast (jarr.ToObject<obj list>(serializer)
                            |> List.map helper)
                | _ -> data

            let toOutput (seq : KeyValuePair<string, obj> seq) : Output =
                upcast Enumerable.ToDictionary
                           (seq, (fun x -> x.Key), fun x -> x.Value)
            payload
            |> Seq.map
                   (fun kval ->
                   KeyValuePair<string, obj>(kval.Key, helper kval.Value))
            |> toOutput

        let jobj = JObject.Load reader
        let typ = jobj.Property("type").Value.ToString()
        match typ with
        | "connection_ack" -> upcast ConnectionAck
        | "connection_error" ->
            let payload = jobj.Property("payload").Value.ToString()
            let errObj = JObject.Parse(payload)
            let errMsg = errObj.Property("error").Value.ToString()
            upcast ConnectionError errMsg
        | "error" ->
            let id = tryGetJsonProperty jobj "id"
            let payload = jobj.Property("payload").Value.ToString()
            let errObj = JObject.Parse(payload)
            let errMsg = errObj.Property("error").Value.ToString()
            upcast Error(id, errMsg)
        | "data" ->
            let id = jobj.Property("id").Value.ToString()
            let payload =
                jobj.Property("payload").Value.ToObject<Dictionary<string, obj>>
                    (serializer)
            upcast Data(id, format payload)
        | "complete" ->
            let id = jobj.Property("id").Value.ToString()
            upcast Complete id
        | t ->
            raise
            <| InvalidOperationException
                   (sprintf "Message Type %s is not supported." t)

[<AutoOpen>]
module internal WebSocketUtils =
    let sendMessage message (settings : JsonSerializerSettings)
        (socket : WebSocket) (ct : CancellationToken) =
        async {
            let json = JsonConvert.SerializeObject(message, settings)
            let buffer = utf8Bytes json
            let segment = ArraySegment<byte>(buffer)
            do! socket.SendAsync(segment, WebSocketMessageType.Text, true, ct)
                |> Async.AwaitTask
        }
        |> Async.StartAsTask :> Task

    let receiveMessage<'T> (settings : JsonSerializerSettings)
        (socket : WebSocket) (ct : CancellationToken) =
        async {
            let buffer = Array.zeroCreate 4096
            let segment = ArraySegment<byte>(buffer)
            do! socket.ReceiveAsync(segment, ct)
                |> Async.AwaitTask
                |> Async.Ignore
            let message = utf8String buffer
            if isNullOrWhiteSpace message then return None
            else
                return JsonConvert.DeserializeObject<'T>(message, settings)
                       |> Some
        }
        |> Async.StartAsTask

type IGQLSocket =
    inherit IDisposable
    abstract CloseAsync : unit -> Task
    abstract CloseAsync : CancellationToken -> Task
    abstract State : WebSocketState
    abstract CloseStatus : WebSocketCloseStatus option
    abstract CloseStatusDescription : string option

type IGQLServerSocket =
    inherit IGQLSocket
    abstract Subscribe : string * IDisposable -> unit
    abstract Unsubscribe : string -> unit
    abstract UnsubscribeAll : unit -> unit
    abstract Id : Guid
    abstract SendAsync : GQLServerMessage * CancellationToken -> Task
    abstract ReceiveAsync : CancellationToken -> Task<GQLClientMessage option>
    abstract SendAsync : GQLServerMessage -> Task
    abstract ReceiveAsync : unit -> Task<GQLClientMessage option>

type IGQLClientSocket =
    inherit IGQLSocket
    abstract SendAsync : GQLClientMessage * CancellationToken -> Task
    abstract ReceiveAsync : CancellationToken -> Task<GQLServerMessage option>
    abstract SendAsync : GQLClientMessage -> Task
    abstract ReceiveAsync : unit -> Task<GQLServerMessage option>

[<Sealed>]
type GQLServerSocket(inner : WebSocket) =
    let subscriptions : IDictionary<string, IDisposable> =
        upcast ConcurrentDictionary<string, IDisposable>()
    let id = System.Guid.NewGuid()
    member __.Subscribe(id : string, unsubscriber : IDisposable) =
        subscriptions.Add(id, unsubscriber)

    member __.Unsubscribe(id : string) =
        match subscriptions.ContainsKey(id) with
        | true ->
            subscriptions.[id].Dispose()
            subscriptions.Remove(id) |> ignore
        | false -> ()

    member __.UnsubscribeAll() =
        subscriptions |> Seq.iter (fun x -> x.Value.Dispose())
        subscriptions.Clear()

    member __.Id = id
    member __.SendAsync(message : GQLServerMessage, cancellationToken) =
        sendMessage message GQLServerMessage.SerializationSettings inner
            cancellationToken
    member __.ReceiveAsync(cancellationToken) =
        receiveMessage<GQLClientMessage> GQLClientMessage.SerializationSettings
            inner cancellationToken
    member this.SendAsync(message : GQLServerMessage) =
        this.SendAsync(message, CancellationToken.None)
    member this.ReceiveAsync() = this.ReceiveAsync(CancellationToken.None)
    member __.Dispose = inner.Dispose
    member __.CloseAsync(cancellationToken) =
        inner.CloseAsync
            (WebSocketCloseStatus.NormalClosure, "", cancellationToken)
    member __.CloseAsync() =
        inner.CloseAsync
            (WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
    member __.State = inner.State
    member __.CloseStatus = inner.CloseStatus |> Option.ofNullable
    member __.CloseStatusDescription =
        inner.CloseStatusDescription |> Option.ofObj

    interface IDisposable with
        member this.Dispose() = this.Dispose()

    interface IGQLServerSocket with
        member this.Subscribe(id, unsubscriber) =
            this.Subscribe(id, unsubscriber)
        member this.Unsubscribe(id) = this.Unsubscribe(id)
        member this.UnsubscribeAll() = this.UnsubscribeAll()
        member this.Id = this.Id
        member this.SendAsync(message, ct) = this.SendAsync(message, ct)
        member this.ReceiveAsync(ct) = this.ReceiveAsync(ct)
        member this.SendAsync(message) = this.SendAsync(message)
        member this.ReceiveAsync() = this.ReceiveAsync()
        member this.State = this.State
        member this.CloseAsync(ct) = this.CloseAsync(ct)
        member this.CloseAsync() = this.CloseAsync()
        member this.CloseStatus = this.CloseStatus
        member this.CloseStatusDescription = this.CloseStatusDescription

[<Sealed>]
type GQLClientSocket(inner : WebSocket) =
    member __.SendAsync(message : GQLClientMessage,
                        cancellationToken : CancellationToken) =
        sendMessage message GQLClientMessage.SerializationSettings inner
            cancellationToken
    member __.ReceiveAsync(cancellationToken : CancellationToken) =
        receiveMessage<GQLServerMessage> GQLServerMessage.SerializationSettings
            inner cancellationToken
    member this.SendAsync(message : GQLClientMessage) =
        this.SendAsync(message, CancellationToken.None)
    member this.ReceiveAsync() = this.ReceiveAsync(CancellationToken.None)
    member __.Dispose = inner.Dispose
    member __.CloseAsync(cancellationToken) =
        inner.CloseAsync
            (WebSocketCloseStatus.NormalClosure, "", cancellationToken)
    member __.CloseAsync() =
        inner.CloseAsync
            (WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
    member __.State = inner.State
    member __.CloseStatus = inner.CloseStatus |> Option.ofNullable
    member __.CloseStatusDescription =
        inner.CloseStatusDescription |> Option.ofObj

    interface IDisposable with
        member this.Dispose() = this.Dispose()

    interface IGQLClientSocket with
        member this.SendAsync(message, ct) = this.SendAsync(message, ct)
        member this.ReceiveAsync(ct) = this.ReceiveAsync(ct)
        member this.SendAsync(message) = this.SendAsync(message)
        member this.ReceiveAsync() = this.ReceiveAsync()
        member this.State = this.State
        member this.CloseAsync(ct) = this.CloseAsync(ct)
        member this.CloseAsync() = this.CloseAsync()
        member this.CloseStatus = this.CloseStatus
        member this.CloseStatusDescription = this.CloseStatusDescription

[<AllowNullLiteral>]
type IGQLServerSocketManager<'Root> =
    abstract StartSocket : IGQLServerSocket * Executor<'Root> * 'Root -> Task

[<AllowNullLiteral>]
type GQLServerSocketManager<'Root>() =
    let sockets : IDictionary<Guid, IGQLServerSocket> =
        upcast ConcurrentDictionary<Guid, IGQLServerSocket>()

    let disposeSocket (socket : IGQLServerSocket) =
        sockets.Remove(socket.Id) |> ignore
        socket.Dispose()

    let sendMessage (socket : IGQLServerSocket) (message : GQLServerMessage) =
        async {
            if socket.State = WebSocketState.Open then
                do! socket.SendAsync(message, CancellationToken.None)
                    |> Async.AwaitTask
            else disposeSocket socket
        }

    let receiveMessage (socket : IGQLServerSocket) =
        socket.ReceiveAsync(CancellationToken.None) |> Async.AwaitTask

    let handleMessages (executor : Executor<'Root>) (root : 'Root)
        (socket : IGQLServerSocket) =
        async {
            let send id output =
                Data(id, output)
                |> sendMessage socket
                |> Async.RunSynchronously

            let handle id =
                function
                | Stream output ->
                    let unsubscriber =
                        output |> Observable.subscribe (fun o -> send id o)
                    socket.Subscribe(id, unsubscriber)
                | Deferred(data, _, output) ->
                    send id data
                    let unsubscriber =
                        output |> Observable.subscribe (fun o -> send id o)
                    socket.Subscribe(id, unsubscriber)
                | Direct(data, _) -> send id data

            try
                let mutable loop = true
                while loop do
                    let! message = socket |> receiveMessage
                    match message with
                    | Some ConnectionInit ->
                        do! sendMessage socket ConnectionAck
                    | Some(Start(id, payload)) ->
                        executor.AsyncExecute
                            (payload.Query, root, payload.Variables)
                        |> Async.RunSynchronously
                        |> handle id
                    | Some ConnectionTerminate ->
                        do! socket.CloseAsync(CancellationToken.None)
                            |> Async.AwaitTask
                        disposeSocket socket
                        loop <- false
                    | Some(Stop id) ->
                        socket.Unsubscribe(id)
                        do! Complete id |> sendMessage socket
                    | None -> ()
            with _ -> disposeSocket socket
        }

    member __.StartSocket(socket : IGQLServerSocket, executor : Executor<'Root>,
                          root : 'Root) =
        sockets.Add(socket.Id, socket)
        handleMessages executor root socket |> Async.StartAsTask :> Task

    interface IGQLServerSocketManager<'Root> with
        member this.StartSocket(socket, executor, root) =
            this.StartSocket(socket, executor, root)

type GQLWebSocketMiddleware<'Root>(next : RequestDelegate, executor : Executor<'Root>, rootFactory : IGQLServerSocket -> 'Root, socketManager : IGQLServerSocketManager<'Root>, socketFactory : WebSocket -> IGQLServerSocket) =
    member __.Invoke(ctx : HttpContext) =
        async {
            if ctx.WebSockets.IsWebSocketRequest then
                let! socket = ctx.WebSockets.AcceptWebSocketAsync("graphql-ws")
                              |> Async.AwaitTask
                if not
                       (ctx.WebSockets.WebSocketRequestedProtocols.Contains
                            (socket.SubProtocol)) then
                    do! socket.CloseAsync
                            (WebSocketCloseStatus.ProtocolError,
                             "Server only supports graphql-ws protocol.",
                             ctx.RequestAborted) |> Async.AwaitTask
                else
                    use socket = socketFactory socket
                    let root = rootFactory socket
                    do! socketManager.StartSocket(socket, executor, root)
                        |> Async.AwaitTask
            else do! next.Invoke(ctx) |> Async.AwaitTask
        }
        |> Async.StartAsTask :> Task

[<AutoOpen>]
module Extensions =
    open Microsoft.AspNetCore.Builder

    let private socketManagerOrDefault (socketManager : IGQLServerSocketManager<'Root> option)
        (serviceProvider : IServiceProvider) =
        match socketManager with
        | Some mgr -> mgr
        | None ->
            match serviceProvider.GetService<IGQLServerSocketManager<'Root>>() with
            | null ->
                raise
                <| InvalidOperationException
                       ("No server socket manager implementation is registered. You must add a implementation of IGQLServerSocketManager<'Root> to the service collection of the application, or provide one in the middleware arguments.")
            | mgr -> mgr

    let private socketFactoryOrDefault (socketFactory : (WebSocket -> IGQLServerSocket) option) =
        defaultArg socketFactory
            (fun socket -> upcast new GQLServerSocket(socket))

    type IApplicationBuilder with
        member this.UseGQLWebSockets<'Root>(executor : Executor<'Root>,
                                            rootFactory : IGQLServerSocket -> 'Root,
                                            ?socketManager : IGQLServerSocketManager<'Root>,
                                            ?socketFactory : WebSocket -> IGQLServerSocket) =
            this.UseWebSockets().UseMiddleware<GQLWebSocketMiddleware<'Root>>
                (executor, rootFactory,
                 socketManagerOrDefault socketManager (this.ApplicationServices),
                 socketFactoryOrDefault socketFactory)
        member this.UseGQLWebSockets<'Root>(executor : Executor<'Root>,
                                            root : 'Root,
                                            ?socketManager : IGQLServerSocketManager<'Root>,
                                            ?socketFactory : WebSocket -> IGQLServerSocket) =
            this.UseGQLWebSockets
                (executor, (fun _ -> root),
                 socketManagerOrDefault socketManager (this.ApplicationServices),
                 socketFactoryOrDefault socketFactory)

    type IServiceCollection with
        member this.AddGQLServerSocketManager<'Root>() =
            this.AddSingleton<IGQLServerSocketManager<'Root>>
                (GQLServerSocketManager<'Root>())
