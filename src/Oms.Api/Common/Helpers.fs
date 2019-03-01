namespace OMS.API

open OMS.Application
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open FluentValidation.Results
open System.Collections.Generic
open Giraffe

[<AutoOpen>]
module JsonHelpers =
    open Newtonsoft.Json.Serialization

    let tryGetJsonProperty (jobj : JObject) prop =
        match jobj.Property(prop) with
        | null -> None
        | p -> p.Value.ToString() |> Some

    let jsonSerializerSettings (converters : JsonConverter seq) =
        JsonSerializerSettings()
        |> tee (fun s ->
               s.Converters <- converters |> List<JsonConverter>
               s.ContractResolver <- CamelCasePropertyNamesContractResolver())

[<AutoOpen>]
module ValidationHelpers =
    let aggregateErrorMessages (result : ValidationResult) =
        result.Errors
        |> Seq.map (fun error -> error.ErrorMessage)
        |> Seq.reduce
               (fun current failure ->
               current + failure + System.Environment.NewLine)

    let mapErrors (result : ValidationResult) =
        let errors =
            result.Errors
            |> Seq.map (fun error -> (error.PropertyName, error.ErrorMessage))
            |> Map.ofSeq
        errors

type CustomNegotiationConfig(baseConfig : INegotiationConfig) =
    interface INegotiationConfig with
        member __.UnacceptableHandler = baseConfig.UnacceptableHandler
        member __.Rules =
            dict [ "*/*", json
                   "application/json", json ]
