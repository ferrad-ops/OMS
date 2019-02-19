namespace OMS.API

open FSharp.Data.GraphQL
open FSharp.Data.GraphQL.Types
open FSharp.Data.GraphQL.Server.Middlewares

[<AutoOpen>]
module SchemaDefinition =
    type ProductType = {Id: System.Guid; Name: string; BrandId: string; CategoryId: string; Description: string option}

    let rec ProductType=
              Define.Object<ProductType>(
                name = "Product",
                description = "A product .",
                isTypeOf = (fun o -> o :? ProductType),
                fieldsFn = fun () ->
                [
                    Define.Field("id", ID<System.Guid>, "The id of the product.", fun _ dto -> dto.Id)
                    Define.Field("name", String, "The name of the product.", fun _ dto -> dto.Name)
                    Define.Field("description", Nullable String, "The description of the product.", fun _ dto -> dto.Description)
                ])

    let mutable products = [
                   {Id = "fc8c04ab-8678-4b3a-84cf-c04a319527f7" |> System.Guid; Name = "Music"; BrandId = "681f3f83-2580-4c54-ac0a-f18dd1b0d73a"; CategoryId = "681f3f83-2580-4c54-ac0a-f18dd1b0d73b"; Description = None}
                   {Id = "cc6b592e-b344-4daa-85d9-85ff501dc59c" |> System.Guid; Name = "Sport"; BrandId =  "c79fdfc5-cfa8-43ac-8617-9df4b94c4cd1"; CategoryId = "cc6b592e-b344-4daa-85d9-85ff501dc59c";Description = None}
                   {Id = "c79fdfc5-cfa8-43ac-8617-9df4b94c4cd1" |> System.Guid; Name = "MultiMedia"; BrandId = "cc6b592e-b344-4daa-85d9-85ff501dc59c"; CategoryId = "c79fdfc5-cfa8-43ac-8617-9df4b94c4cd1";Description = None}
                 ]

    let QueryType =
            Define.Object<RootType>(
                name = "Query",
                fields = [
                    Define.AsyncField(
                               "products",
                               ListOf ProductType,
                               "Gets products",
                               [
                               ],
                               fun ctx _ -> async {
                               return products
                               })

                    Define.AsyncField(
                                "product",
                                ProductType,
                                "Gets product by id",
                                [
                                    Define.Input("id", String)
                                ],
                                fun ctx _ -> async {
                                    let id = ctx.Arg("id").ToString() |> System.Guid

                                    let product = products |> Seq.filter (fun t -> t.Id = id) |> Seq.head

                                    return product
                                })
                                ])

module Schema =
    let Middlewares =
        [ Define.QueryWeightMiddleware(2.0, true)
          Define.LiveQueryMiddleware() ]
    let private instance = Schema(QueryType)
    let executor = (instance, Middlewares) |> Executor
    
