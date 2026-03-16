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