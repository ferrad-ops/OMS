module OMS.API.WebServer

open Giraffe
open Saturn
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.Extensions.DependencyInjection
open Graphscriber.AspNetCore
open Microsoft.AspNetCore.Builder

let endpointPipe =
    pipeline {
        plug head
        plug requestId
    }

let corsConfig (builder : CorsPolicyBuilder) =
    builder.AllowAnyMethod().AllowAnyOrigin().AllowAnyHeader() |> ignore

let configureServices (services : IServiceCollection) =
    services.AddSingleton<INegotiationConfig>
        (CustomNegotiationConfig(DefaultNegotiationConfig())) |> ignore
    services.AddGQLServerSocketManager<RootType>() |> ignore
    services

let configureApp (builder : IApplicationBuilder) =
    builder.UseGQLWebSockets
        (Schema.executor,
         (fun _ -> builder.ApplicationServices |> createRootType))

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
        use_iis
        service_config configureServices
        app_config configureApp
    }

run app
