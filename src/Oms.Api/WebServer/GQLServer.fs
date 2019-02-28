[<AutoOpen>]
module OMS.API.GQLServer

open Giraffe
open Microsoft.AspNetCore.Http
open System.IO
open FSharp.Data.GraphQL
open FSharp.Control.Tasks.V2
open OMS.Application
open Giraffe.HttpStatusCodeHandlers.Successful
open Giraffe.HttpStatusCodeHandlers.RequestErrors

let createGQLServer (executor : Executor<RootType>) =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let request = ctx.Request
            // GraphQL HTTP only supports GET, POST and OPTIONS methods.
            match (request.Method = "GET")
                  |> not
                  && (request.Method = "POST") |> not
                  && (request.Method = "OPTIONS") |> not with
            | true ->
                return! METHOD_NOT_ALLOWED
                            "GraphQL HTTP only supports GET, POST and OPTIONS methods"
                            next ctx
            | false ->
                use sr = new StreamReader(request.Body)
                let! body = sr.ReadToEndAsync()
                match body |> isNullOrWhiteSpace with
                | true ->
                    let! result = Introspection.introspectionQuery
                                    |> executor.AsyncExecute
                                    |> Async.StartAsTask
                    return! OK (json result) next ctx
                | false ->
                    let! result = (body, executor, ctx.RequestServices)
                                    |> processQuery
                    return! OK (json result) next ctx
        }
        
let GQLServer : HttpHandler = Schema.executor |> createGQLServer
