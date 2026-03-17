namespace Shared

open System

[<RequireQualifiedAccess>]
type TodoUpdate =
    | Create of description: string
    | Update of id: Guid * description: string
    | Delete of id: Guid

type Todo = { Id: Guid; Description: string }

module Todo =
    let isValid (description: string) =
        String.IsNullOrWhiteSpace description |> not

    let create (description: string) = {
        Id = Guid.NewGuid()
        Description = description
    }

type ITodosApi = {
    getTodos: unit -> Async<Todo list>
    addTodo: Todo -> Async<Todo list>
    updateTodo: TodoUpdate -> Async<Todo list>
    deleteTodo: Guid -> Async<Todo list>
    deleteTodos: Guid [] -> Async<Todo list>
    clearTodos: unit -> Async<Result<unit, string>>
}