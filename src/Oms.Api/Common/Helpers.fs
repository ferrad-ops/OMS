namespace OMS.API

open OMS.Application
open Newtonsoft.Json
open Newtonsoft.Json.Linq
open FluentValidation.Results
open Newtonsoft.Json.Converters.FSharp

[<AutoOpen>]
module JsonHelpers =

    let tryGetJsonProperty (jobj : JObject) prop =
        match jobj.Property(prop) with
        | null -> None
        | p -> p.Value.ToString() |> Some

    let jsonConverters =
        [| OptionConverter() :> JsonConverter
           TypeSafeEnumConverter() :> JsonConverter |]

    let strictJsonSettings =
        JsonSerializerSettings()
        |> tee
               (fun s ->
               s.Converters <- jsonConverters
               s.MissingMemberHandling <- MissingMemberHandling.Error
               s.ContractResolver <- Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver
                                         ())

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
