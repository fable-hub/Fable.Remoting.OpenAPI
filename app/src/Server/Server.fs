module Server

open SAFE
open Saturn
open Giraffe
open Shared
open Fable.Remoting.OpenAPI

module Storage =
    let todos =
        ResizeArray [
            Todo.create "Create new SAFE project"
            Todo.create "Write your app"
            Todo.create "Ship it!!!"
        ]

    let addTodo (todo: Todo) =
        if Todo.isValid todo.Description then
            todos.Add todo
            Result.Ok()
        else
            Result.Error "Invalid todo"

let todosApi ctx = {
    getTodos = fun () -> async { return Storage.todos |> List.ofSeq }
    addTodo =
        fun (todo: Todo) -> async {
            return
                match Storage.addTodo todo with
                | Result.Ok() -> Storage.todos |> List.ofSeq
                | Result.Error e -> failwith e
        }
}

let openApiDocument =
    OpenApi.options
    |> OpenApi.withTitle "SAFE Todos API"
    |> OpenApi.withVersion "1.0.0"
    |> OpenApi.withDescription "OpenAPI documentation generated directly from Shared.ITodosApi contract."
    |> OpenApi.withServers [
        {
            Url = "http://localhost:8080"
            Description = Some "Local development"
        }
    ]
    |> OpenApi.withDocsContent [
        {
            Title = "Authentication"
            Content = "This sample does not require auth in development mode."
            IsMarkdown = false
        }
        {
            Title = "Error handling"
            Content = "Server may fail with 500 for invalid data paths in this playground."
            IsMarkdown = false
        }
    ]
    |> OpenApi.withEndpointDocs "getTodos" {
        OpenApiDefaults.endpointDocumentation with
            Summary = Some "List all todos"
            Description = Some "Returns the current todo collection in storage order."
            Tags = [ "Todos" ]
    }
    |> OpenApi.withEndpointDocs "addTodo" {
        OpenApiDefaults.endpointDocumentation with
            Summary = Some "Create a todo"
            Description = Some "Adds a todo item when validation passes and returns the updated list."
            Tags = [ "Todos" ]
            RequestExample = Some(box ({ Id = System.Guid.Empty; Description = "Write tests" } : Todo))
    }
    |> OpenApi.generate<ITodosApi>

let webApp =
    choose [
        OpenApiGiraffe.httpHandler openApiDocument
        Api.make todosApi
    ]

let app = application {
    use_router webApp
    memory_cache
    use_static "public"
    use_gzip
}

[<EntryPoint>]
let main _ =
    run app
    0