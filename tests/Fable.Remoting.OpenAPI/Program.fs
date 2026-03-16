module Fable.Remoting.OpenAPI.Tests

open FluentAssertions
open Fable.Remoting.OpenAPI
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

type ComplexApi = {
    ping: unit -> Async<string>
    search: SearchRequest -> Async<SearchResponse>
    enrich: string -> int -> Async<Outcome option>
    unsupported: (int -> int) -> Async<int>
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

    Snapshot.Match(doc1.Json)

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
