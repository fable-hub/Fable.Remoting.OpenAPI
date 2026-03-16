namespace Fable.Remoting.OpenAPI.Suave

open Fable.Remoting.OpenAPI
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Writers

module OpenApiSuave =
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

    let webPart (document: OpenApiDocument) : WebPart =
        choose [
            path document.Routes.JsonPath
            >=> setMimeType "application/json; charset=utf-8"
            >=> OK document.Json

            path document.Routes.YamlPath
            >=> setMimeType "application/yaml; charset=utf-8"
            >=> OK document.Yaml

            path document.Routes.DocsPath
            >=> setMimeType "text/html; charset=utf-8"
            >=> OK (docsHtml document)
        ]

    let withDocsWebPart (document: OpenApiDocument) (remotingWebPart: WebPart) : WebPart =
        let docsWebPart = webPart document

        choose [
            docsWebPart
            remotingWebPart
        ]
