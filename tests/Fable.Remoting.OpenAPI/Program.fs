module Fable.Remoting.OpenAPI.Tests

open System.Text.Json
open FluentAssertions
open Fable.Remoting.OpenAPI
open Fable.Remoting.Server
open Snapshooter.Xunit
open Xunit

type Address = {
    Street: string
    PostalCode: string option
}

type Customer = {
    Name: string
    Address: Address
}

type SearchRequest = {
    Query: string
    IncludeArchived: bool option
    Tags: string list
    Metadata: Map<string, string option>
}

type SearchResponse = {
    Total: int
    Items: Customer list
}

type Outcome =
    | Accepted
    | Rejected of reason: string

type ShippingStatus =
    | Created
    | Failed of reason: string
    | Dispatched of trackingId: string * attempts: int

type ComplexApi = {
    ping: unit -> Async<string>
    search: SearchRequest -> Async<SearchResponse>
    enrich: string -> int -> Async<Outcome option>
    unsupported: (int -> int) -> Async<int>
}

type DuApi = {
    getStatus: unit -> Async<ShippingStatus>
    updateStatus: ShippingStatus -> Async<unit>
}

let private searchRequestExample : SearchRequest = {
    Query = "Contoso"
    IncludeArchived = Some false
    Tags = [ "vip" ]
    Metadata = Map.ofList [ ("segment", Some "enterprise") ]
}

let private searchResponseExample : SearchResponse = {
    Total = 1
    Items = [
        {
            Name = "Contoso Ltd"
            Address = {
                Street = "Main"
                PostalCode = Some "12345"
            }
        }
    ]
}

let private createBaseOptions () =
    OpenApi.options
    |> OpenApi.withTitle "Contract API"
    |> OpenApi.withVersion "2026.03"
    |> OpenApi.withDescription "Generated from a record-of-functions contract"
    |> OpenApi.withContact {
        Name = Some "API Team"
        Url = Some "https://example.test/team"
        Email = Some "api@example.test"
    }
    |> OpenApi.withLicense {
        Name = "MIT"
        Url = Some "https://opensource.org/licenses/MIT"
    }
    |> OpenApi.withServers [
        { Url = "https://api.example.test"; Description = Some "production" }
        { Url = "http://localhost:8080"; Description = Some "local" }
    ]
    |> OpenApi.withDocsContent [
        {
            Title = "Overview"
            Content = "The API is generated from F# shared contracts."
            IsMarkdown = false
        }
        {
            Title = "Notes"
            Content = "- deterministic output\n- endpoint metadata support"
            IsMarkdown = true
        }
    ]
    |> OpenApi.withEndpointDocs "search" {
        OpenApiDefaults.endpointDocumentation with
            Summary = Some "Search customers"
            Description = Some "Finds customers by query and metadata filters."
            Tags = [ "Search" ]
            RequestExample = Some(box searchRequestExample)
            ResponseExample = Some(box searchResponseExample)
            AdditionalResponses = Map.ofList [ 400, "Invalid request"; 500, "Server failure" ]
    }

[<Fact>]
let ``JSON output is deterministic and stable`` () =
    let baseOptions = createBaseOptions ()
    let doc1 = OpenApi.generate<ComplexApi> baseOptions
    let doc2 = OpenApi.generate<ComplexApi> baseOptions

    doc1.Json.Should().Be(doc2.Json) |> ignore
    doc1.Json.Should().Contain("\"openapi\": \"3.0.3\"") |> ignore
    doc1.Json.Should().Contain("\"components\"") |> ignore
    doc1.Json.Should().Contain("\"search\"") |> ignore

[<Fact>]
let ``YAML output has stable key ordering`` () =
    let baseOptions = createBaseOptions ()
    let doc = OpenApi.generate<ComplexApi> baseOptions

    doc.Yaml.Should().Contain("openapi: 3.0.3") |> ignore
    doc.Yaml.IndexOf("components:").Should().BeLessThan(doc.Yaml.IndexOf("paths:")) |> ignore
    doc.Yaml.Should().Contain("x-docs-content:") |> ignore

[<Fact>]
let ``Nested records unions options lists and maps are represented`` () =
    let baseOptions = createBaseOptions ()
    let doc = OpenApi.generate<ComplexApi> baseOptions

    doc.Json.Should().Contain("SearchRequest") |> ignore
    doc.Json.Should().Contain("SearchResponse") |> ignore
    doc.Json.Should().Contain("Customer") |> ignore
    doc.Json.Should().Contain("Address") |> ignore
    doc.Json.Should().Contain("Outcome") |> ignore
    doc.Json.Should().Contain("oneOf") |> ignore
    doc.Json.Should().Contain("additionalProperties") |> ignore

[<Fact>]
let ``Async return types and additional error responses are modeled`` () =
    let baseOptions = createBaseOptions ()
    let doc = OpenApi.generate<ComplexApi> baseOptions

    doc.Json.Should().Contain("\"200\"") |> ignore
    doc.Json.Should().Contain("\"400\"") |> ignore
    doc.Json.Should().Contain("\"500\"") |> ignore
    doc.Json.Should().Contain("Successful response") |> ignore

[<Fact>]
let ``Unit input endpoints use GET method`` () =
    let baseOptions = createBaseOptions ()
    let doc = OpenApi.generate<ComplexApi> baseOptions

    doc.Json.Should().Contain("\"/api/ping\": {") |> ignore
    doc.Json.Should().Contain("\"get\": {") |> ignore

[<Fact>]
let ``Non-unit input endpoints use JSON array request body`` () =
    let baseOptions = createBaseOptions ()
    let doc = OpenApi.generate<ComplexApi> baseOptions

    doc.Json.Should().Contain("\"/api/search\": {") |> ignore
    doc.Json.Should().Contain("\"requestBody\": {") |> ignore
    doc.Json.Should().Contain("\"type\": \"array\"") |> ignore

[<Fact>]
let ``Duplicate route names are reported as diagnostics`` () =
    let baseOptions = createBaseOptions ()
    let options =
        baseOptions
        |> OpenApi.withEndpointRouteStrategy (fun _ -> "/api/collision")

    let doc = OpenApi.generate<ComplexApi> options

    Assert.True(doc.Diagnostics |> List.exists (fun d -> d.Message.Contains("Duplicate route '/api/collision'")))

[<Fact>]
let ``Unsupported types are reported as diagnostics`` () =
    let baseOptions = createBaseOptions ()
    let doc = OpenApi.generate<ComplexApi> baseOptions

    Assert.True(doc.Diagnostics |> List.exists (fun d -> d.Message.Contains("Function type")))

[<Fact>]
let ``Empty API contracts render valid empty paths`` () =
    let baseOptions = createBaseOptions ()
    let doc = OpenApi.generateFromEndpoints [] baseOptions

    doc.Json.Should().Contain("\"paths\": {}") |> ignore
    Assert.Empty(doc.Diagnostics)

[<Fact>]
let ``OperationId strategy can be customized`` () =
    let baseOptions = createBaseOptions ()
    let doc =
        baseOptions
        |> OpenApi.withOperationIdStrategy (fun endpointName -> "op_" + endpointName.ToUpperInvariant())
        |> OpenApi.generate<ComplexApi>

    doc.Json.Should().Contain("\"operationId\": \"op_SEARCH\"") |> ignore

[<Fact>]
let ``Typed endpoint docs helper resolves record member names`` () =
    let baseOptions = createBaseOptions ()

    let doc =
        baseOptions
        |> OpenApi.withEndpointDocsFor<ComplexApi, SearchRequest -> Async<SearchResponse>> <@ fun api -> api.search @> {
            OpenApiDefaults.endpointDocumentation with
                Summary = Some "Typed search summary"
        }
        |> OpenApi.generate<ComplexApi>

    doc.Json.Should().Contain("Typed search summary") |> ignore

[<Fact>]
let ``OpenAPI withDocs uses active Remoting route builder`` () =
    let remotingOptions =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder (fun t m -> sprintf "/api/%s/%s" t m)
        |> Remoting.fromValue {
            ping = fun () -> async { return "ok" }
            search = fun _ -> async { return searchResponseExample }
            enrich = fun _ _ -> async { return Some Accepted }
            unsupported = fun _ -> async { return 0 }
        }

    let doc =
        createBaseOptions ()
        |> OpenAPI.withDocs remotingOptions

    doc.Json.Should().Contain("/api/ComplexApi/search") |> ignore

[<Fact>]
let ``OpenAPI withDocs uses route-builder-derived default docs urls`` () =
    let remotingOptions =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder (fun t m -> sprintf "/api/%s/%s" t m)
        |> Remoting.fromValue {
            ping = fun () -> async { return "ok" }
            search = fun _ -> async { return searchResponseExample }
            enrich = fun _ _ -> async { return Some Accepted }
            unsupported = fun _ -> async { return 0 }
        }

    let doc =
        createBaseOptions ()
        |> OpenAPI.withDocs remotingOptions

    Assert.Equal("/api/ComplexApi/docs", doc.Routes.DocsPath)
    Assert.Equal("/api/ComplexApi/docs/openapi.json", doc.Routes.JsonPath)
    Assert.Equal("/api/ComplexApi/docs/openapi.yaml", doc.Routes.YamlPath)

[<Fact>]
let ``OpenAPI withDocs keeps explicitly configured docs routes`` () =
    let remotingOptions =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder (fun t m -> sprintf "/api/%s/%s" t m)
        |> Remoting.fromValue {
            ping = fun () -> async { return "ok" }
            search = fun _ -> async { return searchResponseExample }
            enrich = fun _ _ -> async { return Some Accepted }
            unsupported = fun _ -> async { return 0 }
        }

    let doc =
        createBaseOptions ()
        |> OpenApi.withRoutes {
            DocsPath = "/custom/docs"
            JsonPath = "/custom/docs/openapi.json"
            YamlPath = "/custom/docs/openapi.yaml"
        }
        |> OpenAPI.withDocs remotingOptions

    Assert.Equal("/custom/docs", doc.Routes.DocsPath)
    Assert.Equal("/custom/docs/openapi.json", doc.Routes.JsonPath)
    Assert.Equal("/custom/docs/openapi.yaml", doc.Routes.YamlPath)

[<Fact>]
let ``Named request examples are emitted as OpenAPI examples object`` () =
    let archivedRequestExample = {
        Query = "Archived"
        IncludeArchived = Some true
        Tags = [ "legacy" ]
        Metadata = Map.ofList [ ("segment", Some "archive") ]
    }

    let doc =
        createBaseOptions ()
        |> OpenApi.withEndpointRequestNamedExampleFor<ComplexApi, SearchRequest, SearchResponse>
            <@ fun api -> api.search @>
            {
                Name = "primary"
                Summary = Some "Primary search payload"
                Description = Some "Preferred search request"
                ExternalValue = None
            }
            searchRequestExample
        |> OpenApi.withEndpointRequestNamedExampleFor<ComplexApi, SearchRequest, SearchResponse>
            <@ fun api -> api.search @>
            {
                Name = "archived"
                Summary = Some "Archived payload"
                Description = None
                ExternalValue = Some "https://example.test/examples/search-archived.json"
            }
            archivedRequestExample
        |> OpenApi.generate<ComplexApi>

    use parsed = JsonDocument.Parse(doc.Json)
    let mediaType =
        parsed.RootElement
            .GetProperty("paths")
            .GetProperty("/api/search")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")

    (mediaType.TryGetProperty("examples") |> fst).Should().BeTrue() |> ignore
    (mediaType.TryGetProperty("example") |> fst).Should().BeFalse() |> ignore

    let examples = mediaType.GetProperty("examples")
    let primary = examples.GetProperty("primary")
    primary.GetProperty("summary").GetString().Should().Be("Primary search payload") |> ignore
    primary.GetProperty("description").GetString().Should().Be("Preferred search request") |> ignore

    doc.Json.Should().Contain("\"primary\"") |> ignore

    let archived = examples.GetProperty("archived")
    archived.GetProperty("externalValue").GetString().Should().Be("https://example.test/examples/search-archived.json")
    |> ignore

[<Fact>]
let ``Named response examples are emitted for non-unit endpoints`` () =
    let altResponseExample = {
        Total = 2
        Items = [
            {
                Name = "Tailspin Toys"
                Address = {
                    Street = "South"
                    PostalCode = None
                }
            }
        ]
    }

    let doc =
        createBaseOptions ()
        |> OpenApi.withEndpointResponseNamedExampleFor<ComplexApi, SearchRequest -> Async<SearchResponse>, SearchResponse>
            <@ fun api -> api.search @>
            {
                Name = "success"
                Summary = Some "Success payload"
                Description = Some "Typical successful response"
                ExternalValue = None
            }
            searchResponseExample
        |> OpenApi.withEndpointResponseNamedExampleFor<ComplexApi, SearchRequest -> Async<SearchResponse>, SearchResponse>
            <@ fun api -> api.search @>
            {
                Name = "alternate"
                Summary = Some "Alternate payload"
                Description = None
                ExternalValue = Some "https://example.test/examples/search-response-alt.json"
            }
            altResponseExample
        |> OpenApi.generate<ComplexApi>

    use parsed = JsonDocument.Parse(doc.Json)
    let mediaType =
        parsed.RootElement
            .GetProperty("paths")
            .GetProperty("/api/search")
            .GetProperty("post")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")

    (mediaType.TryGetProperty("examples") |> fst).Should().BeTrue() |> ignore
    (mediaType.TryGetProperty("example") |> fst).Should().BeFalse() |> ignore

    let examples = mediaType.GetProperty("examples")
    let success = examples.GetProperty("success")
    success.GetProperty("summary").GetString().Should().Be("Success payload") |> ignore
    success.GetProperty("description").GetString().Should().Be("Typical successful response") |> ignore

    let alternate = examples.GetProperty("alternate")
    alternate.GetProperty("externalValue").GetString().Should().Be("https://example.test/examples/search-response-alt.json")
    |> ignore

[<Fact>]
let ``Union schema models Fable.Remoting JSON wire shape`` () =
    let doc =
        OpenApi.options
        |> OpenApi.withTitle "DU API"
        |> OpenApi.withVersion "1.0.0"
        |> OpenApi.generate<DuApi>

    use parsed = JsonDocument.Parse(doc.Json)
    let oneOf =
        parsed.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ShippingStatus")
            .GetProperty("oneOf")

    let hasCreatedCaseAsStringEnum =
        oneOf.EnumerateArray()
        |> Seq.exists (fun schema ->
            match schema.TryGetProperty("enum") with
            | true, enumNode ->
                enumNode.EnumerateArray()
                |> Seq.exists (fun entry -> entry.GetString() = "Created")
            | false, _ -> false)

    let hasFailedCaseAsNamedObject =
        oneOf.EnumerateArray()
        |> Seq.exists (fun schema ->
            match schema.TryGetProperty("properties") with
            | true, props ->
                (props.TryGetProperty("Failed") |> fst)
            | false, _ -> false)

    let hasDispatchedCaseAsNamedObject =
        oneOf.EnumerateArray()
        |> Seq.exists (fun schema ->
            match schema.TryGetProperty("properties") with
            | true, props ->
                (props.TryGetProperty("Dispatched") |> fst)
            | false, _ -> false)

    hasCreatedCaseAsStringEnum.Should().BeTrue() |> ignore
    hasFailedCaseAsNamedObject.Should().BeTrue() |> ignore
    hasDispatchedCaseAsNamedObject.Should().BeTrue() |> ignore

[<Fact>]
let ``DU examples use Fable.Remoting JSON serialization`` () =
    let doc =
        OpenApi.options
        |> OpenApi.withTitle "DU API"
        |> OpenApi.withVersion "1.0.0"
        |> OpenApi.withEndpointRequestExampleFor<DuApi, ShippingStatus, unit>
            <@ fun api -> api.updateStatus @>
            (Failed "invalid-address")
        |> OpenApi.withEndpointResponseExampleFor<DuApi, unit -> Async<ShippingStatus>, ShippingStatus>
            <@ fun api -> api.getStatus @>
            (Dispatched("trk-123", 2))
        |> OpenApi.generate<DuApi>

    use parsed = JsonDocument.Parse(doc.Json)

    let requestExample =
        parsed.RootElement
            .GetProperty("paths")
            .GetProperty("/api/updateStatus")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("example")
            .EnumerateArray()
            |> Seq.head

    let responseExample =
        parsed.RootElement
            .GetProperty("paths")
            .GetProperty("/api/getStatus")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("example")
            .GetProperty("Dispatched")
            .EnumerateArray()
            |> Seq.toArray

    requestExample.GetProperty("Failed").GetString().Should().Be("invalid-address") |> ignore
    responseExample.[0].GetString().Should().Be("trk-123") |> ignore
    responseExample.[1].GetInt32().Should().Be(2) |> ignore
