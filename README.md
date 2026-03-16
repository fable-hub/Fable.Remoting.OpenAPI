# Fable.Remoting.OpenAPI

![NuGet Downloads](https://img.shields.io/nuget/dt/Fable.Remoting.OpenAPI?style=plastic&label=Fable.Remoting.OpenAPI&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FFable.Remoting.OpenAPI)
![NuGet Downloads](https://img.shields.io/nuget/dt/Fable.Remoting.OpenAPI.Giraffe?style=plastic&label=Fable.Remoting.OpenAPI.Giraffe&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FFable.Remoting.OpenAPI.Giraffe)
![NuGet Downloads](https://img.shields.io/nuget/dt/Fable.Remoting.OpenAPI.Suave?style=plastic&label=Fable.Remoting.OpenAPI.Suave&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FFable.Remoting.OpenAPI.Suave)


OpenAPI generation for Fable.Remoting contracts.

## Packages

- `Fable.Remoting.OpenAPI`: Core document generation and customization APIs.
- `Fable.Remoting.OpenAPI.Giraffe`: Giraffe docs handlers and remoting composition.
- `Fable.Remoting.OpenAPI.Suave`: Suave docs webpart helpers and composition.

## Highlights

- OpenAPI 3.0.3 JSON and YAML generation from shared API contracts.
- Typed endpoint metadata helpers via quotations.
- Remoting-aware route generation (uses active route builder).
- Defaults docs routes from route builder and API type name.
- Deterministic output suitable for snapshot-style tests.

> [!NOTE]
> This project was instantiated by AI agents and was not fully reviewed by humans at the time of this commit. It was done to quickly deviler a working prototype of the intended functionality.

## Core Usage

```fsharp
open Fable.Remoting.OpenAPI

let document =
    OpenApi.options
    |> OpenApi.withTitle "My API"
    |> OpenApi.withVersion "1.0.0"
    |> OpenApi.generate<MySharedApi>
```

## Giraffe Integration

```fsharp
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Fable.Remoting.OpenAPI

let docsOptions =
    OpenApi.options
    |> OpenApi.withTitle "My API"
    |> OpenApi.withVersion "1.0.0"

let webApp =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder (fun typeName methodName -> sprintf "/api/%s/%s" typeName methodName)
    |> Remoting.fromValue apiImplementation
    |> Remoting.OpenAPI.withDocs docsOptions
```

By default, the docs routes follow the same remoting route base:

- `/api/<TypeName>/docs`
- `/api/<TypeName>/docs/openapi.json`
- `/api/<TypeName>/docs/openapi.yaml`

You can still override with `OpenApi.withRoutes`.

## Suave Integration

```fsharp
open Fable.Remoting.OpenAPI
open Fable.Remoting.OpenAPI.Suave

let document =
    OpenApi.options
    |> OpenApi.withTitle "My API"
    |> OpenAPI.withDocs remotingOptions

let app =
    OpenApiSuave.withDocsWebPart document remotingWebPart
```

## Type-safe Endpoint Metadata

```fsharp
open Fable.Remoting.OpenAPI

let docs =
    OpenApi.options
    |> OpenAPI.withEndpointDocsFor<MyApi, CreateOrder -> Async<OrderId>>
        <@ fun api -> api.createOrder @>
        { OpenApiDefaults.endpointDocumentation with Summary = Some "Create order" }
    |> OpenAPI.withEndpointRequestExampleFor<MyApi, CreateOrder, OrderId>
        <@ fun api -> api.createOrder @>
        { customerId = "c-1"; amount = 12.5m }
```

## Development

Solution files used by CI and local workflows:

- `Fable.Remoting.OpenApi.sln` for restore/build/test.
- `Release.sln` for restore/pack of publishable packages only.

Typical local commands:

```bash
dotnet restore ./Fable.Remoting.OpenApi.sln
dotnet test ./Fable.Remoting.OpenApi.sln
dotnet restore ./Release.sln
dotnet pack ./Release.sln -c Release --no-restore -o artifacts
```

See `CONTRIBUTING.md` for full setup and contribution guidance.
