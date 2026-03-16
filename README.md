# Fable.Remoting.OpenAPI

Production-oriented OpenAPI plugin for Fable.Remoting record-of-functions contracts.

## Features

- Generates OpenAPI 3.0.3 from shared F# API contracts.
- Deterministic JSON and YAML rendering for snapshot-style tests.
- Built-in docs endpoints for Giraffe/Saturn apps:
  - `/openapi.json`
  - `/openapi.yaml`
  - `/docs`
- Customization options inspired by FastAPI/Swashbuckle/NSwag:
  - `title`, `version`, `description`
  - contact, license, servers
  - docs route customization
  - per-endpoint summary/description/tags
  - request/response examples
  - operation id strategy
  - route strategy
  - schema naming strategy
  - free-text docs blocks

## Basic Setup

```fsharp
open Fable.Remoting.OpenAPI
open Giraffe

let document =
    OpenApi.options
    |> OpenApi.withTitle "My API"
    |> OpenApi.withVersion "1.0.0"
    |> OpenApi.generate<MySharedApi>

let webApp =
    choose [
        OpenApiGiraffe.httpHandler document
        Api.make apiImplementation
    ]
```

## Custom Metadata and Docs Content

```fsharp
let document =
    OpenApi.options
    |> OpenApi.withTitle "Orders API"
    |> OpenApi.withDescription "Generated from shared contracts"
    |> OpenApi.withServers [
        { Url = "https://api.contoso.com"; Description = Some "prod" }
        { Url = "http://localhost:8080"; Description = Some "local" }
    ]
    |> OpenApi.withDocsContent [
        { Title = "Auth"; Content = "Use bearer token"; IsMarkdown = false }
        { Title = "Release Notes"; Content = "- v1 stable"; IsMarkdown = true }
    ]
    |> OpenApi.withEndpointDocs "createOrder" {
        OpenApiDefaults.endpointDocumentation with
            Summary = Some "Create order"
            Description = Some "Creates a new order and returns its id"
            Tags = [ "Orders" ]
            RequestExample = Some(box {| customerId = "c-1"; amount = 12.5 |})
            ResponseExample = Some(box {| id = "o-1" |})
            AdditionalResponses = Map.ofList [ 400, "Validation failed" ]
    }
    |> OpenApi.generate<MySharedApi>
```

## Custom Route Paths and Operation IDs

```fsharp
let document =
    OpenApi.options
    |> OpenApi.withRoutes {
        JsonPath = "/schema/openapi.json"
        YamlPath = "/schema/openapi.yaml"
        DocsPath = "/dev/docs"
    }
    |> OpenApi.withOperationIdStrategy (fun endpoint -> "my_" + endpoint)
    |> OpenApi.withEndpointRouteStrategy (fun endpoint -> "/rpc/" + endpoint)
    |> OpenApi.generate<MySharedApi>
```

## YAML Output Usage

```fsharp
let yamlText =
    OpenApi.options
    |> OpenApi.generate<MySharedApi>
    |> fun doc -> doc.Yaml
```

## Architecture

The implementation is intentionally split for testability:

- metadata extraction: reflection over record-of-functions contracts
- intermediate model: operation/schema model plus diagnostics
- deterministic renderers: JSON and YAML serializers with stable ordering
- web integration: `OpenApiGiraffe.httpHandler` for route exposure

## Known Limitations

- HTTP method inference is currently `POST` per endpoint.
- Function parameter names are not available from F# function types; multi-arg payloads use `arg1`, `arg2`, ...
- Complex recursive/discriminated union shapes are represented conservatively.
- Response modeling is primarily success + configurable additional status descriptions.

## Extension Points

- richer HTTP verb mapping and route templating
- richer error/typed-response schemas
- markdown rendering in `/docs` sidebar
- custom schema converters for domain-specific types
