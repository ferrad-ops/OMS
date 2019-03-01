namespace OMS.API

open Newtonsoft.Json
open Newtonsoft.Json.Converters.FSharp
open FSharp.Data.GraphQL
open FSharp.Data.GraphQL.Types

type RootType =
    { RequestId : System.Guid }

[<AutoOpen>]
module RootType =
    open System

    let private requestId = System.Guid.NewGuid()

    let rootType =
            { RootType.RequestId = requestId }

    let createRootType (sp : IServiceProvider) = rootType

type QueryResolvers = FieldDef<RootType> list

type MutationResolvers = FieldDef<RootType> list

type SubscriptionResolvers = SubscriptionFieldDef<RootType> list

[<AutoOpen>]
module SchemaHelpers =
    let raiseError error =
        error
        |> GraphQLException
        |> raise
        
    let RootType =
        Define.Object<RootType>
            (name = "Root", description = "The root object for all operations.",
             isTypeOf = (fun x -> x :? RootType),
             fieldsFn = fun () ->
                 [ Define.Field
                       ("requestId", ID,
                        "The ID of the requisition made by the client.",
                        fun _ (x : RootType) -> x.RequestId) ])

    let jsonConverters =
        [| OptionConverter() :> JsonConverter
           TypeSafeEnumConverter() :> JsonConverter |]

[<RequireQualifiedAccess>]
module InputTypeConverter =
    open OMS.Application

    let strictJsonSettings =
        JsonSerializerSettings()
        |> tee
               (fun s ->
               s.Converters <- jsonConverters
               s.MissingMemberHandling <- MissingMemberHandling.Error
               s.ContractResolver <- Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver
                                         ())

    type Input = string

    type ConversionResult =
        | ConvertedInput of obj * Input
        | Nothing of Input

    let tryConvert<'a> input =
        try
            let result =
                JsonConvert.DeserializeObject<'a>(input, strictJsonSettings)
            ConvertedInput(result, input)
        with _ -> Nothing input

    let convert<'a> (previousResult : ConversionResult) =
        match previousResult with
        | ConvertedInput _ -> (previousResult)
        | Nothing input -> (tryConvert<'a> input)

    let convertToInput input =
        let result =
            //convert<CreatePlaceInput> (input |> ConversionResult.Nothing)
            //|> convert<RenamePlaceInput>
            ConversionResult.Nothing "" // TODO: Should provide an actual implementation
        match result with
        | ConvertedInput(object, _) -> object
        | Nothing _ -> box input
