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
- Native Fable.Remoting.Json-compatible example serialization.
- Discriminated union schema modeling aligned with Fable.Remoting JSON wire shapes.

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
    |> OpenAPI.withDocs docsOptions
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
    |> OpenApi.withEndpointDocsFor<MyApi, CreateOrder -> Async<OrderId>>
        <@ fun api -> api.createOrder @>
        { OpenApiDefaults.endpointDocumentation with Summary = Some "Create order" }
    |> OpenApi.withEndpointRequestExampleFor<MyApi, CreateOrder, OrderId>
        <@ fun api -> api.createOrder @>
        { customerId = "c-1"; amount = 12.5m }
    |> OpenApi.withEndpointRequestNamedExampleFor<MyApi, CreateOrder, OrderId>
        <@ fun api -> api.createOrder @>
        {
            Name = "bulk"
            Summary = Some "Bulk order example"
            Description = Some "Example payload for batch processing"
            ExternalValue = None
        }
        { customerId = "c-2"; amount = 42m }
    |> OpenApi.withEndpointResponseNamedExampleFor<MyApi, CreateOrder -> Async<OrderId>, OrderId>
        <@ fun api -> api.createOrder @>
        {
            Name = "created"
            Summary = Some "Successful response"
            Description = Some "Order id returned when creation succeeds"
            ExternalValue = None
        }
        { value = "order-123" }
```

Named example helpers emit OpenAPI `examples` objects (multiple examples with metadata like `summary` and `description`) in request and response media types.

## Endpoint Example APIs

The options pipeline exposes two styles of example helpers:

- Single-example helpers (emit OpenAPI `example` when no named examples are configured)
    - `OpenApi.withEndpointRequestExampleFor<'Api, 'Input, 'Output>`
    - `OpenApi.withEndpointResponseExampleFor<'Api, 'Endpoint, 'Output>`
- Named example helpers (emit OpenAPI `examples`)
    - `OpenApi.withEndpointRequestNamedExampleFor<'Api, 'Input, 'Output>`
    - `OpenApi.withEndpointResponseNamedExampleFor<'Api, 'Endpoint, 'Output>`

### Response Helper Expression Type

`withEndpointResponseExampleFor` and `withEndpointResponseNamedExampleFor` accept endpoint quotations typed as `Expr<'Api -> 'Endpoint>`, which means they work for both:

- unit-input endpoints, for example: `unit -> Async<'Output>`
- non-unit endpoints, for example: `'Input -> Async<'Output>`

For non-unit endpoints, pass the function type as `'Endpoint` in the generic argument list:

```fsharp
|> OpenApi.withEndpointResponseNamedExampleFor<MyApi, CreateOrder -> Async<OrderId>, OrderId>
        <@ fun api -> api.createOrder @>
        {
                Name = "created"
                Summary = Some "Successful response"
                Description = Some "Order id returned when creation succeeds"
                ExternalValue = None
        }
        { value = "order-123" }
```

### Example Metadata Shape

Named helpers use `OpenApiExampleMetadata`:

```fsharp
type OpenApiExampleMetadata = {
        Name: string
        Summary: string option
        Description: string option
        ExternalValue: string option
}
```

This maps to OpenAPI example object fields (`summary`, `description`, `value`, `externalValue`) as described by Swagger/OpenAPI documentation.

### Rendering Behavior

- If named examples exist for a request or response media type, output uses `examples`.
- If no named examples exist, the legacy single-example value is emitted as `example`.
- For Fable.Remoting request bodies, request example values are normalized to array payload shape.

This keeps existing single-example pipelines working while enabling richer multi-example docs.

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
