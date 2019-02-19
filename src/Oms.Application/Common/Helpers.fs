namespace OMS.Application

open System
open System.Text
open System.Security.Cryptography
open System.Collections.Concurrent
open Microsoft.FSharp.Reflection

[<AutoOpen>]
module Common =
    let notNull x = not (isNull x)
    let flip f x y = f y x

    let tee f x =
        f x
        x

    let memoize (f : 'TIn -> 'TOut) =
        let cache = ConcurrentDictionary<'TIn, 'TOut>()
        fun x -> cache.GetOrAdd(x, f)

    let unionCaseName (x : 'T) =
        match FSharpValue.GetUnionFields(x, typeof<'T>) with
        | case, _ -> case.Name

    //let transformOrEmpty (transform : string -> string) (arg : string option) : string =
    //    (Option.defaultValue "" (arg |> Option.bind (fun q -> Some <| transform q)))
    let transformOrEmptyString (arg : string option) : string =
        (Option.defaultValue "" (arg |> Option.bind (fun q -> q |> Some)))
    let transformOrDefaultInt32 (arg : int option, defaultValue) : int =
        (Option.defaultValue defaultValue
             (arg |> Option.bind (fun q -> q |> Some)))

[<AutoOpen>]
module Extensions =
    type String with
        member this.IsNullOrEmpty() = String.IsNullOrEmpty(this)
        member this.IsNullOrWhiteSpace() = String.IsNullOrWhiteSpace(this)

    type Exception with

        member this.Flatten() =
            seq {
                yield this
                if notNull this.InnerException then
                    yield! this.InnerException.Flatten()
            }

        member this.AllMessages =
            this.Flatten()
            |> Seq.map
                   (fun ex ->
                   sprintf "%s: %s" (ex.GetType().FullName) ex.Message)
            |> (fun messages -> String.Join(" >>> ", messages))

        member this.AllMessagesAndStackTraces =
            this.Flatten()
            |> Seq.map (fun ex -> ex.StackTrace)
            |> (fun stackTraces ->
            String.Join("\n--- Inner Exception ---\n", stackTraces))
            |> sprintf "%s\n%s" this.AllMessages
            
[<AutoOpen>]
module StringHelpers =
    let utf8String (bytes : byte seq) =
        bytes
        |> Seq.filter (fun i -> i > 0uy)
        |> Array.ofSeq
        |> Encoding.UTF8.GetString

    let utf8Bytes (str : string) = str |> Encoding.UTF8.GetBytes
    let isNullOrWhiteSpace (str : string) = str |> String.IsNullOrWhiteSpace

    let isNotNullOrWhiteSpace (str : string) =
        str
        |> String.IsNullOrWhiteSpace
        |> not

[<AutoOpen>]
module HashHelper =
    let Encoding = "iso-8859-1" |> System.Text.Encoding.GetEncoding

    let hashString (value : string) =
        use md5 = MD5.Create()

        let value =
            (value
             |> Encoding.GetBytes
             |> md5.ComputeHash
             |> BitConverter.ToString)
                .Replace("-", String.Empty)
        value

[<AutoOpen>]
module ListHelper =
    let reduceList list = list |> List.reduce (List.append)
