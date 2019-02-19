module OMS.API.WebServer

open Saturn
open Giraffe
open Microsoft.AspNetCore.Cors.Infrastructure

let endpointPipe =
    pipeline {
        plug head
        plug requestId
    }

let corsConfig (builder : CorsPolicyBuilder) =
    builder.AllowAnyMethod().AllowAnyOrigin().AllowAnyHeader() |> ignore

let app =
    application {
        pipe_through endpointPipe
        error_handler
            (fun _ _ ->
            text
                "An unhandled exception has occurred while executing the request.")
        use_router appRouter
        memory_cache
        disable_diagnostics
        use_static "static"
        use_gzip
        use_cors "CORS_policy" corsConfig
    }

run app
