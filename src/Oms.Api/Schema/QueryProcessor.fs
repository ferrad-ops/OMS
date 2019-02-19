[<AutoOpen>]
module OMS.API.QueryProcessor

open System
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open Newtonsoft.Json.Converters
open Newtonsoft.Json.Converters.FSharp
open Newtonsoft.Json.Serialization
open FSharp.Data.GraphQL.Execution
open OMS.Application
open FSharp.Data.GraphQL
open FSharp.Control.Tasks.V2

[<CLIMutable>]
type GraphQLParameters =
    { Query : string
      OperationName : string
      Variables : Map<string, obj> }

let private jsonSettings =
    JsonSerializerSettings
        (Converters = jsonConverters,
         ContractResolver = CamelCasePropertyNamesContractResolver())

let private serializer =
    JsonSerializer(ContractResolver = CamelCasePropertyNamesContractResolver())
    |> tee (fun serializer ->
           serializer.Converters.Add(OptionConverter())
           serializer.Converters.Add(TypeSafeEnumConverter()))

let private normalizeQuery (query : string) =
    let query = query.Trim().Replace("\r\n", " ")
    query

let private isVariablesNullOrEmpty variables =
    Object.ReferenceEquals(variables, null) || variables |> Map.isEmpty

let private sanitizeVariables (variables : Map<string, obj>) =
    let tryConvertToInput (jObject : obj) =
        match jObject with
        | :? string -> jObject
        | _ ->
            JsonConvert.SerializeObject(jObject, jsonSettings)
            |> InputTypeConverter.convertToInput

    let variables =
        match variables |> isVariablesNullOrEmpty with
        | true -> variables
        | false ->
            variables
            |> Seq.map (fun (KeyValue(k, v)) -> k, tryConvertToInput v)
            |> Map.ofSeq

    variables

let private mapStringParameter value =
    match value |> isNullOrWhiteSpace with
    | true -> None
    | false ->
        value
        |> normalizeQuery
        |> Some

let private mapVariablesParameter (variables : Map<string, obj>) =
    match variables |> isVariablesNullOrEmpty with
    | true -> None
    | false -> variables |> Some

let json =
    function
    | Direct(data, _) -> JObject.FromObject(data, serializer)
    | Deferred(data, _, deferred) ->
        deferred
        |> Observable.add
               (fun d ->
               printfn "Deferred: %s"
                   (JsonConvert.SerializeObject(d, jsonSettings)))
        JObject.FromObject(data, serializer)
    | Stream _ -> JObject()

let processQuery (body : string, executor : Executor<RootType>,
                  sp : IServiceProvider) =
    task {
        let parameters =
            body |> JsonConvert.DeserializeObject<GraphQLParameters>
        match parameters.Query |> mapStringParameter,
              parameters.Variables |> mapVariablesParameter with
        | Some query, Some variables ->
            let variables = variables |> sanitizeVariables
            printfn "Received query: %s" query
            printfn "Received variables: %A" variables
            return! executor.AsyncExecute
                        (query, variables = variables,
                         operationName = parameters.OperationName,
                         data = rootType) |> Async.StartAsTask
        | Some query, None ->
            printfn "Received query: %s" query
            return! executor.AsyncExecute
                        (query, operationName = parameters.OperationName,
                         data = rootType) |> Async.StartAsTask
        | None, _ ->
            return! Introspection.introspectionQuery
                    |> executor.AsyncExecute
                    |> Async.StartAsTask
    }
