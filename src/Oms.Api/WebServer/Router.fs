[<AutoOpen>]
module OMS.API.Router

open Giraffe
open Saturn

let browserRouter =
    router {
        not_found_handler (text "Not Found")
        pipe_through (pipeline {
                          plug putSecureBrowserHeaders
                          plug fetchSession
                          set_header "x-pipeline-type" "Browser"
                      })
        get "" (text "Welcome to OMS API")
    }

let appRouter =
    router {
        forward "/" browserRouter
    }

