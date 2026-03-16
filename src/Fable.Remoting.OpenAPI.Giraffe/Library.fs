namespace Fable.Remoting.OpenAPI.Giraffe

open System
open Microsoft.AspNetCore.Http
open Fable.Remoting.OpenAPI
open Giraffe

module OpenApiGiraffe =
    let private docsHtml (document: OpenApiDocument) =
        let docsBlocks =
            document.DocsContent
            |> List.map (fun block ->
                let contentClass = if block.IsMarkdown then "doc-block markdown" else "doc-block"
                sprintf "<section class=\"%s\"><h3>%s</h3><pre>%s</pre></section>" contentClass block.Title block.Content)
            |> String.concat "\n"

        sprintf
            "<!doctype html>
<html>
<head>
  <meta charset=\"utf-8\" />
  <meta name=\"viewport\" content=\"width=device-width,initial-scale=1\" />
  <title>API Docs</title>
  <link rel=\"stylesheet\" href=\"https://unpkg.com/swagger-ui-dist@5/swagger-ui.css\" />
  <style>
    body { margin: 0; font-family: 'Segoe UI', sans-serif; background: #f5f7fb; }
    #app { display: grid; grid-template-columns: minmax(0, 1fr) 340px; min-height: 100vh; }
    #swagger-ui { background: #fff; }
    .sidebar { background: linear-gradient(180deg, #11243a 0%%, #0e1724 100%%); color: #e6edf5; padding: 24px; overflow-y: auto; }
    .sidebar h2 { margin: 0 0 8px 0; font-size: 1.2rem; }
    .sidebar p { margin: 0 0 16px 0; opacity: 0.9; }
    .doc-block { margin-top: 20px; }
    .doc-block h3 { margin: 0 0 8px 0; }
    .doc-block pre { background: #0a1220; color: #bfd6ff; padding: 12px; border-radius: 10px; white-space: pre-wrap; }
    @media (max-width: 960px) {
      #app { grid-template-columns: 1fr; }
      .sidebar { order: -1; }
    }
  </style>
</head>
<body>
  <div id=\"app\">
    <div id=\"swagger-ui\"></div>
    <aside class=\"sidebar\">
      <h2>Documentation Notes</h2>
      <p>Additional content configured through OpenApi options.</p>
      %s
    </aside>
  </div>
  <script src=\"https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js\"></script>
  <script>
    window.onload = function() {
      window.ui = SwaggerUIBundle({
        url: '%s',
        dom_id: '#swagger-ui'
      });
    }
  </script>
</body>
</html>"
            docsBlocks
            document.Routes.JsonPath

    let httpHandler (document: OpenApiDocument) : HttpHandler =
        choose [
            route document.Routes.JsonPath
            >=> setHttpHeader "Content-Type" "application/json; charset=utf-8"
            >=> text document.Json

            route document.Routes.YamlPath
            >=> setHttpHeader "Content-Type" "application/yaml; charset=utf-8"
            >=> text document.Yaml

            route document.Routes.DocsPath
            >=> htmlString (docsHtml document)
        ]

namespace Fable.Remoting.Server

open System
open Microsoft.AspNetCore.Http
open Fable.Remoting.Giraffe
open Giraffe

module Remoting =
    module OpenAPI =
        let withDocs
          (options: Fable.Remoting.OpenAPI.OpenApiOptions)
            (remotingOptions: RemotingOptions<HttpContext, 'Api>)
            : HttpHandler =
            let document = Fable.Remoting.OpenAPI.OpenAPI.withDocs remotingOptions options
            let docsHandler = Fable.Remoting.OpenAPI.Giraffe.OpenApiGiraffe.httpHandler document
            let remotingHandler = remotingOptions |> Fable.Remoting.Giraffe.Remoting.buildHttpHandler

            let normalizedRemotingHandler : HttpHandler =
                fun next ctx ->
                    if
                        ctx.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                        && isNull ctx.Request.ContentType
                    then
                        // Fable.Remoting expects a non-null content type even for GET/no-body calls.
                        ctx.Request.ContentType <- ""

                    remotingHandler next ctx

            choose [
                docsHandler
                normalizedRemotingHandler
            ]
