module Server

open SAFE
open Saturn
open Giraffe
open Shared
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Fable.Remoting.OpenAPI

module Storage =

    let DEFAULT_TODO_GUIDE = System.Guid("f76a6573-2318-4d8a-8f2f-2913a06624cc")

    let todos =
        ResizeArray [
            {
                Id = DEFAULT_TODO_GUIDE
                Description = "Create new SAFE project"
            }
            Todo.create "Write your app"
            Todo.create "Ship it!!!"
        ]

    let addTodo (todo: Todo) =
        if Todo.isValid todo.Description then
            todos.Add todo
            Result.Ok()
        else
            Result.Error "Invalid todo"

    let updateTodo (newTodo: Todo) =
        if Todo.isValid newTodo.Description then
            todos
            |> Seq.tryFindIndex (fun t -> t.Id = newTodo.Id)
            |> function
                | Some index ->
                    todos.[index] <- newTodo
                    Result.Ok()
                | None -> Result.Error "Todo not found"
        else
            Result.Error "Invalid todo"

    let deleteTodo (todoId: System.Guid) =
        match todos |> Seq.tryFindIndex (fun t -> t.Id = todoId) with
        | Some index ->
            todos.RemoveAt index
            Result.Ok()
        | None -> Result.Error "Todo not found"

let todosApi ctx = {
    getTodos = fun () -> async { return Storage.todos |> List.ofSeq }
    addTodo =
        fun (todo: Todo) -> async {
            return
                match Storage.addTodo todo with
                | Result.Ok() -> Storage.todos |> List.ofSeq
                | Result.Error e -> failwith e
        }
    updateTodo = fun (todo: Todo) -> async {
        return
            match Storage.updateTodo todo with
            | Result.Ok() -> Storage.todos |> List.ofSeq
            | Result.Error e -> failwith e
    }
    deleteTodo = fun todoId ->
        async {
            return
                match Storage.deleteTodo todoId with
                | Result.Ok() -> Storage.todos |> List.ofSeq
                | Result.Error e -> failwith e
        }
}

module Route =
    let builder typeName methodName = sprintf "/api/%s/%s" typeName methodName

let remotingApi =
    let docsOptions =
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
        // getTodos
        |> OpenAPI.withEndpointDocsFor<ITodosApi, unit -> Async<Todo list>> <@ fun api -> api.getTodos @> {
            OpenApiDefaults.endpointDocumentation with
                Summary = Some "List all todos"
                Description = Some "Returns the current todo collection in storage order."
                Tags = [ "Todos" ]
        }
        // addTodo
        |> OpenAPI.withEndpointDocsFor<ITodosApi, Todo -> Async<Todo list>> <@ fun api -> api.addTodo @> {
            OpenApiDefaults.endpointDocumentation with
                Summary = Some "Create a todo"
                Description = Some "Adds a todo item when validation passes and returns the updated list."
                Tags = [ "Todos" ]
        }
        |> OpenAPI.withEndpointRequestExampleFor<ITodosApi, Todo, Todo list>
            <@ fun api -> api.addTodo @>
            { Id = System.Guid.Empty; Description = "Write tests" }
        // deleteTodo
        |> OpenAPI.withEndpointDocsFor<ITodosApi, System.Guid -> Async<Todo list>> <@ fun api -> api.deleteTodo @> {
            OpenApiDefaults.endpointDocumentation with
                Summary = Some "Delete a todo"
                Description = Some "Deletes a todo by id if it exists and returns the updated list."
                Tags = [ "Todos" ]
        }
        |> OpenAPI.withEndpointRequestExampleFor<ITodosApi, System.Guid, Todo list>
            <@ fun api -> api.deleteTodo @>
            Storage.DEFAULT_TODO_GUIDE
        // updateTodo intentionally left without docs to demonstrate default behavior

    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.fromContext todosApi
    |> Remoting.OpenAPI.withDocs docsOptions

let webApp =
    choose [ remotingApi ]

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