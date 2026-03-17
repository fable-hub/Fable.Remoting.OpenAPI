module Fable.Remoting.OpenAPI.Adapters.Tests

open System.Text.Json
open FluentAssertions
open Fable.Remoting.OpenAPI
open global.Suave
open global.Suave.Successful
open Xunit

[<CLIMutable>]
type SearchRequest = { Query: string }

type IComplexApi = {
    ping: unit -> Async<string>
    search: SearchRequest -> Async<string list>
}

type ShippingStatus =
    | Created
    | Failed of reason: string

type IDuApi = {
    getStatus: unit -> Async<ShippingStatus>
}

let private routeBuilder typeName methodName = sprintf "/api/%s/%s" typeName methodName

[<Fact>]
let ``Giraffe adapter exposes docs with expected transport semantics`` () =
    let options =
        OpenApi.options
        |> OpenApi.withTitle "Adapter parity"
        |> OpenApi.withVersion "1.0.0"
        |> OpenApi.withEndpointRouteStrategy (routeBuilder "IComplexApi")

    let document = OpenApi.generate<IComplexApi> options
    let webApp = Fable.Remoting.OpenAPI.Giraffe.OpenApiGiraffe.httpHandler document

    webApp.Should().NotBeNull() |> ignore
    document.Routes.DocsPath.Should().Be("/docs") |> ignore

    use parsed = JsonDocument.Parse(document.Json)
    let paths = parsed.RootElement.GetProperty("paths")
    (paths.GetProperty("/api/IComplexApi/ping").TryGetProperty("get") |> fst).Should().BeTrue() |> ignore
    (paths.GetProperty("/api/IComplexApi/search").TryGetProperty("post") |> fst).Should().BeTrue() |> ignore

[<Fact>]
let ``Suave adapter can compose docs and remoting webparts`` () =
    let options =
        OpenApi.options
        |> OpenApi.withTitle "Adapter parity"
        |> OpenApi.withVersion "1.0.0"
        |> OpenApi.withEndpointRouteStrategy (routeBuilder "IComplexApi")

    let document = OpenApi.generate<IComplexApi> options
    let remotingWebPart : WebPart = Successful.OK "remoting"
    let combinedWebPart = Fable.Remoting.OpenAPI.Suave.OpenApiSuave.withDocsWebPart document remotingWebPart

    combinedWebPart.Should().NotBeNull() |> ignore
    document.Routes.JsonPath.Should().Be("/openapi.json") |> ignore

[<Fact>]
let ``Adapters preserve DU schema wire-shape semantics`` () =
    let options =
        OpenApi.options
        |> OpenApi.withTitle "Adapter DU parity"
        |> OpenApi.withVersion "1.0.0"
        |> OpenApi.withEndpointRouteStrategy (routeBuilder "IDuApi")

    let document = OpenApi.generate<IDuApi> options

    use parsed = JsonDocument.Parse(document.Json)
    let oneOf =
        parsed.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ShippingStatus")
            .GetProperty("oneOf")

    let hasStringCase =
        oneOf.EnumerateArray()
        |> Seq.exists (fun schema ->
            match schema.TryGetProperty("enum") with
            | true, enumNode ->
                enumNode.EnumerateArray()
                |> Seq.exists (fun entry -> entry.GetString() = "Created")
            | false, _ -> false)

    let hasObjectCase =
        oneOf.EnumerateArray()
        |> Seq.exists (fun schema ->
            match schema.TryGetProperty("properties") with
            | true, props -> props.TryGetProperty("Failed") |> fst
            | false, _ -> false)

    hasStringCase.Should().BeTrue() |> ignore
    hasObjectCase.Should().BeTrue() |> ignore