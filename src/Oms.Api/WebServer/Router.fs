[<AutoOpen>]
module OMS.API.Router

open Giraffe
open Saturn

let GQLServer = Schema.executor |> createGQLServer

let apiRouter =
    router {
        not_found_handler (text "Not Found")
        pipe_through (pipeline {
                          plug acceptJson
                          set_header "x-pipeline-type" "Api"
                      })
        get "" GQLServer
        post "" GQLServer
        //option "" GQLServer
    }

let appRouter =
    router {
        forward "/" apiRouter
    }

