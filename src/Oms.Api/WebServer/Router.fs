[<AutoOpen>]
module OMS.API.Router

open Giraffe
open Saturn

let apiRouter =
    router {
        not_found_handler (text "Not Found")
        pipe_through (pipeline {
                          plug GQLServer
                          plug acceptJson
                          set_header "x-pipeline-type" "Api"
                      })
    }


let browserRouter =
    router {
        not_found_handler (text "Not Found")
        pipe_through (pipeline {
                          plug putSecureBrowserHeaders
                          plug fetchSession
                          set_header "x-pipeline-type" "Browser"
                      })
        get "" (text "Welcome to OMS GraphQL API")
    }

let appRouter =
    router {
        forward "/graphql" apiRouter
        forward "/" browserRouter
    }

