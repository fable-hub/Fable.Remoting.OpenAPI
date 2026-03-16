namespace Fable.Remoting.OpenAPI

open System
open System.Collections.Generic
open System.Globalization
open System.Text
open System.Text.Json
open System.Threading.Tasks
open FSharp.Reflection
open Giraffe

type OpenApiContact = {
    Name: string option
    Url: string option
    Email: string option
}

type OpenApiLicense = {
    Name: string
    Url: string option
}

type OpenApiServer = {
    Url: string
    Description: string option
}

type OpenApiRoutes = {
    JsonPath: string
    YamlPath: string
    DocsPath: string
}

type DocsContentBlock = {
    Title: string
    Content: string
    IsMarkdown: bool
}

type EndpointDocumentation = {
    Summary: string option
    Description: string option
    Tags: string list
    RequestExample: obj option
    ResponseExample: obj option
    AdditionalResponses: Map<int, string>
}

type OpenApiOptions = {
    Title: string
    Version: string
    Description: string option
    Contact: OpenApiContact option
    License: OpenApiLicense option
    Servers: OpenApiServer list
    Routes: OpenApiRoutes
    DocsContent: DocsContentBlock list
    EndpointDocs: Map<string, EndpointDocumentation>
    OperationIdStrategy: string -> string
    EndpointRouteStrategy: string -> string
    SchemaNameStrategy: Type -> string
}

type OpenApiDiagnosticSeverity =
    | Warning
    | Error

type OpenApiDiagnostic = {
    Severity: OpenApiDiagnosticSeverity
    Message: string
}

type private PrimitiveKind =
    | PString
    | PInteger
    | PNumber
    | PBoolean

type private JsonValue =
    | JNull
    | JBool of bool
    | JNumber of string
    | JString of string
    | JObject of (string * JsonValue) list
    | JArray of JsonValue list

type private OpenApiSchema =
    | Primitive of PrimitiveKind * string option * bool
    | Array of OpenApiSchema * bool
    | Object of (string * OpenApiSchema * bool) list * OpenApiSchema option * bool
    | OneOf of OpenApiSchema list * bool
    | Enum of string list * bool
    | Reference of string * bool
    | Unsupported of string

type private OpenApiOperation = {
    Name: string
    Route: string
    OperationId: string
    Summary: string option
    Description: string option
    Tags: string list
    RequestSchema: OpenApiSchema option
    RequestExample: JsonValue option
    Responses: (int * string * OpenApiSchema option * JsonValue option) list
}

type private OpenApiDocumentModel = {
    InfoTitle: string
    InfoVersion: string
    InfoDescription: string option
    Contact: OpenApiContact option
    License: OpenApiLicense option
    Servers: OpenApiServer list
    DocsContent: DocsContentBlock list
    Operations: OpenApiOperation list
    Schemas: Map<string, OpenApiSchema>
    Diagnostics: OpenApiDiagnostic list
}

type OpenApiDocument = {
    Json: string
    Yaml: string
    Model: obj
    Diagnostics: OpenApiDiagnostic list
    Routes: OpenApiRoutes
}

type OpenApiContractEndpoint = {
    Name: string
    ArgTypes: Type list
    ReturnType: Type
}

type private ExtractedEndpoint = {
    Name: string
    ArgTypes: Type list
    ReturnType: Type
}

module private Utils =
    let invariant (value: obj) =
        Convert.ToString(value, CultureInfo.InvariantCulture)

    let isGenericOf (genericType: Type) (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = genericType

    let unwrapAsyncLike (t: Type) =
        if isGenericOf typedefof<Async<_>> t then
            t.GetGenericArguments().[0]
        elif isGenericOf typedefof<Task<_>> t then
            t.GetGenericArguments().[0]
        elif isGenericOf typedefof<ValueTask<_>> t then
            t.GetGenericArguments().[0]
        else
            t

    let rec extractFunctionSignature (t: Type) =
        if FSharpType.IsFunction t then
            let domain, codomain = FSharpType.GetFunctionElements t
            let args, returnType = extractFunctionSignature codomain
            domain :: args, returnType
        else
            [], t

    let normalizePath (path: string) =
        if String.IsNullOrWhiteSpace(path) then "/"
        elif path.StartsWith("/") then path
        else "/" + path

module private JsonValues =
    let rec ofJsonElement (element: JsonElement) : JsonValue =
        match element.ValueKind with
        | JsonValueKind.Null
        | JsonValueKind.Undefined -> JNull
        | JsonValueKind.True -> JBool true
        | JsonValueKind.False -> JBool false
        | JsonValueKind.Number -> JNumber(element.GetRawText())
        | JsonValueKind.String -> JString(element.GetString())
        | JsonValueKind.Array ->
            element.EnumerateArray() |> Seq.map ofJsonElement |> Seq.toList |> JArray
        | JsonValueKind.Object ->
            element.EnumerateObject()
            |> Seq.map (fun p -> p.Name, ofJsonElement p.Value)
            |> Seq.toList
            |> JObject
        | _ -> JNull

    let ofObj (value: obj) =
        if isNull value then
            None
        else
            let json = JsonSerializer.Serialize(value)
            use document = JsonDocument.Parse(json)
            Some(ofJsonElement document.RootElement)

module private MetadataExtraction =
    let extractEndpoints<'Api> () =
        let apiType = typeof<'Api>

        if not (FSharpType.IsRecord(apiType, true)) then
            invalidArg "'Api" "OpenAPI generation requires a record-of-functions API contract type."

        FSharpType.GetRecordFields(apiType, true)
        |> Array.toList
        |> List.map (fun field ->
            let args, ret = Utils.extractFunctionSignature field.PropertyType

            if List.isEmpty args then
                invalidArg "'Api" (sprintf "Field '%s' must be a function value." field.Name)

            {
                Name = field.Name
                ArgTypes = args
                ReturnType = Utils.unwrapAsyncLike ret
            })

module private SchemaModel =
    type State = {
        Definitions: Dictionary<string, OpenApiSchema>
        Diagnostics: ResizeArray<OpenApiDiagnostic>
        InFlight: HashSet<string>
    }

    let private warning (state: State) message =
        state.Diagnostics.Add({ Severity = Warning; Message = message })

    let private primitive kind format nullable = Primitive(kind, format, nullable)

    let private reference name nullable = Reference(name, nullable)

    let private schemaName (options: OpenApiOptions) (t: Type) =
        options.SchemaNameStrategy t

    let rec private schemaFor (options: OpenApiOptions) (state: State) (t: Type) : OpenApiSchema =
        if t = typeof<string> || t = typeof<char> then
            primitive PString None false
        elif t = typeof<bool> then
            primitive PBoolean None false
        elif t = typeof<int16> || t = typeof<int> || t = typeof<int64> || t = typeof<byte> then
            primitive PInteger None false
        elif t = typeof<float> || t = typeof<double> || t = typeof<decimal> then
            primitive PNumber None false
        elif t = typeof<Guid> then
            primitive PString (Some "uuid") false
        elif t = typeof<DateTime> then
            primitive PString (Some "date-time") false
        elif t = typeof<DateTimeOffset> then
            primitive PString (Some "date-time") false
        elif t = typeof<unit> then
            Object([], None, false)
        elif t.IsArray then
            Array(schemaFor options state (t.GetElementType()), false)
        elif Utils.isGenericOf typedefof<option<_>> t then
            nullableSchema (schemaFor options state (t.GetGenericArguments().[0]))
        elif Utils.isGenericOf typedefof<list<_>> t then
            Array(schemaFor options state (t.GetGenericArguments().[0]), false)
        elif Utils.isGenericOf typedefof<seq<_>> t then
            Array(schemaFor options state (t.GetGenericArguments().[0]), false)
        elif Utils.isGenericOf typedefof<Map<_, _>> t then
            let keyType = t.GetGenericArguments().[0]
            let valueType = t.GetGenericArguments().[1]

            if keyType <> typeof<string> then
                warning state (sprintf "Map key type '%s' is not supported in OpenAPI; falling back to string-key object." keyType.FullName)

            Object([], Some(schemaFor options state valueType), false)
        elif FSharpType.IsRecord(t, true) then
            namedSchema options state t buildRecordSchema
        elif FSharpType.IsUnion(t, true) then
            if t.IsEnum then
                let names = Enum.GetNames(t) |> Array.toList
                Enum(names, false)
            elif
                FSharpType.GetUnionCases(t, true)
                |> Array.forall (fun c -> c.GetFields().Length = 0)
            then
                let names =
                    FSharpType.GetUnionCases(t, true)
                    |> Array.map (fun c -> c.Name)
                    |> Array.toList

                Enum(names, false)
            else
                namedSchema options state t buildUnionSchema
        elif FSharpType.IsFunction t then
            warning state (sprintf "Function type '%s' is not supported by OpenAPI schemas." t.FullName)
            Unsupported(sprintf "Unsupported function type '%s'" t.FullName)
        else
            warning state (sprintf "Type '%s' is not explicitly supported; defaulting to string." t.FullName)
            primitive PString None false

    and private nullableSchema schema =
        match schema with
        | Primitive(kind, format, _) -> Primitive(kind, format, true)
        | Array(items, _) -> Array(items, true)
        | Object(props, addl, _) -> Object(props, addl, true)
        | OneOf(cases, _) -> OneOf(cases, true)
        | Enum(values, _) -> Enum(values, true)
        | Reference(name, _) -> Reference(name, true)
        | Unsupported x -> Unsupported x

    and private namedSchema options state t builder =
        let name = schemaName options t

        if state.Definitions.ContainsKey(name) then
            reference name false
        elif state.InFlight.Contains(name) then
            reference name false
        else
            state.InFlight.Add(name) |> ignore
            let schema = builder options state t
            state.Definitions.[name] <- schema
            state.InFlight.Remove(name) |> ignore
            reference name false

    and private buildRecordSchema options state t =
        let fields = FSharpType.GetRecordFields(t, true)

        let properties =
            fields
            |> Array.toList
            |> List.map (fun field ->
                let isOptional = Utils.isGenericOf typedefof<option<_>> field.PropertyType
                let schema = schemaFor options state field.PropertyType
                field.Name, schema, not isOptional)

        Object(properties, None, false)

    and private buildUnionSchema options state t =
        let cases =
            FSharpType.GetUnionCases(t, true)
            |> Array.toList
            |> List.map (fun unionCase ->
                let fields = unionCase.GetFields()

                if fields.Length = 0 then
                    Object([ "case", Enum([ unionCase.Name ], false), true ], None, false)
                else
                    let payloadProperties =
                        fields
                        |> Array.toList
                        |> List.mapi (fun idx field ->
                            let name =
                                if String.IsNullOrWhiteSpace(field.Name) then
                                    sprintf "item%d" (idx + 1)
                                else
                                    field.Name

                            name, schemaFor options state field.PropertyType, true)

                    let properties =
                        ("case", Enum([ unionCase.Name ], false), true)
                        :: payloadProperties

                    Object(properties, None, false))

        OneOf(cases, false)

    let generate options endpoints =
        let state = {
            Definitions = Dictionary<string, OpenApiSchema>()
            Diagnostics = ResizeArray<OpenApiDiagnostic>()
            InFlight = HashSet<string>()
        }

        let operations =
            endpoints
            |> List.map (fun endpoint ->
                let endpointDocs =
                    options.EndpointDocs
                    |> Map.tryFind endpoint.Name
                    |> Option.defaultValue {
                        Summary = None
                        Description = None
                        Tags = []
                        RequestExample = None
                        ResponseExample = None
                        AdditionalResponses = Map.empty
                    }

                let requestSchema =
                    match endpoint.ArgTypes with
                    | [] -> None
                    | [ t ] when t = typeof<unit> -> None
                    | [ t ] -> Some(schemaFor options state t)
                    | manyArgs ->
                        let properties =
                            manyArgs
                            |> List.mapi (fun idx argType -> sprintf "arg%d" (idx + 1), schemaFor options state argType, true)

                        Some(Object(properties, None, false))

                let responseSchema =
                    if endpoint.ReturnType = typeof<unit> then
                        None
                    else
                        Some(schemaFor options state endpoint.ReturnType)

                let responses =
                    let okResponse =
                        200,
                        "Successful response",
                        responseSchema,
                        endpointDocs.ResponseExample |> Option.bind JsonValues.ofObj

                    let additional =
                        endpointDocs.AdditionalResponses
                        |> Map.toList
                        |> List.map (fun (statusCode, description) -> statusCode, description, None, None)

                    okResponse :: additional

                {
                    Name = endpoint.Name
                    Route = options.EndpointRouteStrategy endpoint.Name |> Utils.normalizePath
                    OperationId = options.OperationIdStrategy endpoint.Name
                    Summary = endpointDocs.Summary
                    Description = endpointDocs.Description
                    Tags = endpointDocs.Tags
                    RequestSchema = requestSchema
                    RequestExample = endpointDocs.RequestExample |> Option.bind JsonValues.ofObj
                    Responses = responses
                })

        let duplicatePaths =
            operations
            |> List.groupBy (fun op -> op.Route)
            |> List.filter (fun (_, ops) -> ops.Length > 1)

        for (route, collidingOps) in duplicatePaths do
            let operationNames = collidingOps |> List.map (fun op -> op.Name) |> String.concat ", "

            state.Diagnostics.Add {
                Severity = Error
                Message = sprintf "Duplicate route '%s' generated for endpoints: %s" route operationNames
            }

        let schemas =
            state.Definitions
            |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
            |> Seq.sortBy fst
            |> Map.ofSeq

        operations, schemas, List.ofSeq state.Diagnostics

module private JsonRendering =
    let private sortObjectFields fields = fields |> List.sortBy fst

    let private appendEscapedString (builder: StringBuilder) (value: string) =
        builder.Append('"') |> ignore

        value
        |> Seq.iter (fun c ->
            match c with
            | '\\' -> builder.Append("\\\\") |> ignore
            | '"' -> builder.Append("\\\"") |> ignore
            | '\n' -> builder.Append("\\n") |> ignore
            | '\r' -> builder.Append("\\r") |> ignore
            | '\t' -> builder.Append("\\t") |> ignore
            | _ when int c < 32 -> builder.Append(sprintf "\\u%04x" (int c)) |> ignore
            | _ -> builder.Append(c) |> ignore)

        builder.Append('"') |> ignore

    let rec private writeJsonValue (builder: StringBuilder) indentLevel (value: JsonValue) =
        let indent () = builder.Append(String.replicate (indentLevel * 2) " ") |> ignore

        match value with
        | JNull -> builder.Append("null") |> ignore
        | JBool b -> builder.Append(if b then "true" else "false") |> ignore
        | JNumber n -> builder.Append(n) |> ignore
        | JString s -> appendEscapedString builder s
        | JArray values ->
            if List.isEmpty values then
                builder.Append("[]") |> ignore
            else
                builder.Append("[\n") |> ignore

                values
                |> List.iteri (fun idx item ->
                    builder.Append(String.replicate ((indentLevel + 1) * 2) " ") |> ignore
                    writeJsonValue builder (indentLevel + 1) item

                    if idx < values.Length - 1 then
                        builder.Append(',') |> ignore

                    builder.Append('\n') |> ignore)

                indent ()
                builder.Append(']') |> ignore
        | JObject fields ->
            let sorted = sortObjectFields fields

            if List.isEmpty sorted then
                builder.Append("{}") |> ignore
            else
                builder.Append("{\n") |> ignore

                sorted
                |> List.iteri (fun idx (name, fieldValue) ->
                    builder.Append(String.replicate ((indentLevel + 1) * 2) " ") |> ignore
                    appendEscapedString builder name
                    builder.Append(": ") |> ignore
                    writeJsonValue builder (indentLevel + 1) fieldValue

                    if idx < sorted.Length - 1 then
                        builder.Append(',') |> ignore

                    builder.Append('\n') |> ignore)

                indent ()
                builder.Append('}') |> ignore

    let private schemaToJson schema =
        let nullableProp nullable = if nullable then [ "nullable", JBool true ] else []

        let rec loop schemaValue =
            match schemaValue with
            | Primitive(kind, format, nullable) ->
                let kindValue =
                    match kind with
                    | PString -> "string"
                    | PInteger -> "integer"
                    | PNumber -> "number"
                    | PBoolean -> "boolean"

                let core = [ "type", JString kindValue ]

                let withFormat =
                    match format with
                    | Some f -> ("format", JString f) :: core
                    | None -> core

                JObject(withFormat @ nullableProp nullable)
            | Array(items, nullable) ->
                JObject(
                    [ "type", JString "array"
                      "items", loop items ]
                    @ nullableProp nullable
                )
            | Object(properties, additional, nullable) ->
                let propsObj =
                    properties
                    |> List.map (fun (name, propertySchema, _) -> name, loop propertySchema)
                    |> JObject

                let required =
                    properties
                    |> List.choose (fun (name, _, isRequired) -> if isRequired then Some(JString name) else None)

                let baseFields =
                    [ "type", JString "object"
                      "properties", propsObj ]

                let withRequired =
                    if List.isEmpty required then
                        baseFields
                    else
                        ("required", JArray required) :: baseFields

                let withAdditional =
                    match additional with
                    | Some addl -> ("additionalProperties", loop addl) :: withRequired
                    | None -> withRequired

                JObject(withAdditional @ nullableProp nullable)
            | OneOf(cases, nullable) ->
                JObject(
                    [ "oneOf", JArray(cases |> List.map loop) ]
                    @ nullableProp nullable
                )
            | Enum(values, nullable) ->
                JObject(
                    [ "type", JString "string"
                      "enum", JArray(values |> List.map JString) ]
                    @ nullableProp nullable
                )
            | Reference(name, nullable) ->
                if nullable then
                    JObject(
                        [ "allOf", JArray [ JObject [ "$ref", JString(sprintf "#/components/schemas/%s" name) ] ]
                          "nullable", JBool true ]
                    )
                else
                    JObject [ "$ref", JString(sprintf "#/components/schemas/%s" name) ]
            | Unsupported reason ->
                JObject(
                    [ "type", JString "string"
                      "description", JString reason ]
                )

        loop schema

    let private diagnosticsToJson diagnostics =
        diagnostics
        |> List.map (fun d ->
            JObject [
                "severity", JString(if d.Severity = Error then "error" else "warning")
                "message", JString d.Message
            ])
        |> JArray

    let toJsonValue (model: OpenApiDocumentModel) =
        let infoFields =
            [
                "title", JString model.InfoTitle
                "version", JString model.InfoVersion
            ]

        let infoWithDescription =
            match model.InfoDescription with
            | Some description -> ("description", JString description) :: infoFields
            | None -> infoFields

        let infoWithContact =
            match model.Contact with
            | None -> infoWithDescription
            | Some contact ->
                let fields =
                    [
                        match contact.Name with
                        | Some name -> "name", JString name
                        | None -> ()

                        match contact.Url with
                        | Some url -> "url", JString url
                        | None -> ()

                        match contact.Email with
                        | Some email -> "email", JString email
                        | None -> ()
                    ]

                ("contact", JObject fields) :: infoWithDescription

        let info =
            match model.License with
            | None -> JObject infoWithContact
            | Some license ->
                let fields =
                    [ "name", JString license.Name ]
                    @
                    [
                        match license.Url with
                        | Some url -> "url", JString url
                        | None -> ()
                    ]

                JObject(("license", JObject fields) :: infoWithContact)

        let serverValues =
            model.Servers
            |> List.map (fun server ->
                JObject(
                    [ "url", JString server.Url ]
                    @
                    [
                        match server.Description with
                        | Some description -> "description", JString description
                        | None -> ()
                    ]
                ))

        let operationsByPath =
            model.Operations
            |> List.groupBy (fun operation -> operation.Route)
            |> List.sortBy fst
            |> List.map (fun (path, operations) ->
                let op = operations.Head

                let requestBodyField =
                    match op.RequestSchema with
                    | None -> []
                    | Some schema ->
                        let mediaTypeFields =
                            [ "schema", schemaToJson schema ]
                            @
                            [
                                match op.RequestExample with
                                | Some example -> "example", example
                                | None -> ()
                            ]

                        [
                            "requestBody",
                            JObject [
                                "required", JBool true
                                "content", JObject [ "application/json", JObject mediaTypeFields ]
                            ]
                        ]

                let responsesField =
                    op.Responses
                    |> List.sortBy (fun (statusCode, _, _, _) -> statusCode)
                    |> List.map (fun (statusCode, description, schemaOpt, exampleOpt) ->
                        let contentField =
                            match schemaOpt with
                            | None -> []
                            | Some schema ->
                                let mediaFields =
                                    [ "schema", schemaToJson schema ]
                                    @
                                    [
                                        match exampleOpt with
                                        | Some example -> "example", example
                                        | None -> ()
                                    ]

                                [ "content", JObject [ "application/json", JObject mediaFields ] ]

                        Utils.invariant statusCode,
                        JObject(
                            [ "description", JString description ]
                            @ contentField
                        ))
                    |> JObject

                let baseOperation =
                    [
                        "operationId", JString op.OperationId
                        "responses", responsesField
                    ]

                let withSummary =
                    match op.Summary with
                    | Some s -> ("summary", JString s) :: baseOperation
                    | None -> baseOperation

                let withDescription =
                    match op.Description with
                    | Some d -> ("description", JString d) :: withSummary
                    | None -> withSummary

                let withTags =
                    if List.isEmpty op.Tags then
                        withDescription
                    else
                        ("tags", JArray(op.Tags |> List.map JString)) :: withDescription

                let operationObject = JObject(withTags @ requestBodyField)

                path, JObject [ "post", operationObject ])
            |> JObject

        let schemas =
            model.Schemas
            |> Map.toList
            |> List.map (fun (name, schema) -> name, schemaToJson schema)
            |> JObject

        let docsContent =
            model.DocsContent
            |> List.map (fun block ->
                JObject [
                    "title", JString block.Title
                    "content", JString block.Content
                    "isMarkdown", JBool block.IsMarkdown
                ])
            |> JArray

        JObject [
            "openapi", JString "3.0.3"
            "info", info
            "servers", JArray serverValues
            "paths", operationsByPath
            "components", JObject [ "schemas", schemas ]
            "x-docs-content", docsContent
            "x-diagnostics", diagnosticsToJson model.Diagnostics
        ]

    let serialize value =
        let builder = StringBuilder()
        writeJsonValue builder 0 value
        builder.ToString()

module private YamlRendering =
    let private safeScalar (text: string) =
        if String.IsNullOrEmpty(text) then
            "''"
        elif
            text.IndexOfAny([| ':'; '-'; '#'; '{'; '}'; '['; ']'; ','; '&'; '*'; '!'; '|'; '>'; '%'; '@'; '`'; '"'; '\''; '\n'; '\r'; '\t' |]) >= 0
            || text.StartsWith(" ")
            || text.EndsWith(" ")
        then
            "'" + text.Replace("'", "''") + "'"
        else
            text

    let rec private writeYaml (builder: StringBuilder) indentLevel value =
        let indent = String.replicate (indentLevel * 2) " "

        let writeLine (line: string) =
            builder.Append(indent).Append(line).Append('\n') |> ignore

        match value with
        | JNull -> writeLine "null"
        | JBool b -> writeLine(if b then "true" else "false")
        | JNumber n -> writeLine n
        | JString s -> writeLine(safeScalar s)
        | JArray items ->
            if List.isEmpty items then
                writeLine "[]"
            else
                items
                |> List.iter (fun item ->
                    match item with
                    | JObject _
                    | JArray _ ->
                        builder.Append(indent).Append("- ") |> ignore

                        match item with
                        | JObject fields when not (List.isEmpty fields) ->
                            builder.Append('\n') |> ignore
                            writeYaml builder (indentLevel + 1) item
                        | JArray arr when not (List.isEmpty arr) ->
                            builder.Append('\n') |> ignore
                            writeYaml builder (indentLevel + 1) item
                        | _ ->
                            builder.Append("{}\n") |> ignore
                    | _ ->
                        builder.Append(indent).Append("- ") |> ignore

                        match item with
                        | JNull -> builder.Append("null") |> ignore
                        | JBool b -> builder.Append(if b then "true" else "false") |> ignore
                        | JNumber n -> builder.Append(n) |> ignore
                        | JString s -> builder.Append(safeScalar s) |> ignore
                        | _ -> ()

                        builder.Append('\n') |> ignore)
        | JObject fields ->
            let sorted = fields |> List.sortBy fst

            if List.isEmpty sorted then
                writeLine "{}"
            else
                sorted
                |> List.iter (fun (key, item) ->
                    match item with
                    | JObject o when List.isEmpty o ->
                        builder.Append(indent).Append(key).Append(": {}\n") |> ignore
                    | JArray a when List.isEmpty a ->
                        builder.Append(indent).Append(key).Append(": []\n") |> ignore
                    | JObject _
                    | JArray _ ->
                        builder.Append(indent).Append(key).Append(":\n") |> ignore
                        writeYaml builder (indentLevel + 1) item
                    | JNull -> builder.Append(indent).Append(key).Append(": null\n") |> ignore
                    | JBool b -> builder.Append(indent).Append(key).Append(": ").Append(if b then "true" else "false").Append('\n') |> ignore
                    | JNumber n -> builder.Append(indent).Append(key).Append(": ").Append(n).Append('\n') |> ignore
                    | JString s -> builder.Append(indent).Append(key).Append(": ").Append(safeScalar s).Append('\n') |> ignore)

    let serialize value =
        let builder = StringBuilder()
        writeYaml builder 0 value
        builder.ToString().TrimEnd()

module private DocumentBuilding =
    let defaultEndpointDocs = {
        Summary = None
        Description = None
        Tags = []
        RequestExample = None
        ResponseExample = None
        AdditionalResponses = Map.empty
    }

    let private buildFromExtracted options extractedEndpoints =
        let operations, schemas, diagnostics = SchemaModel.generate options extractedEndpoints

        {
            InfoTitle = options.Title
            InfoVersion = options.Version
            InfoDescription = options.Description
            Contact = options.Contact
            License = options.License
            Servers = options.Servers
            DocsContent = options.DocsContent
            Operations = operations
            Schemas = schemas
            Diagnostics = diagnostics
        }

    let build<'Api> options =
        let extractedEndpoints = MetadataExtraction.extractEndpoints<'Api> ()
        buildFromExtracted options extractedEndpoints

    let buildFromContractEndpoints options (endpoints: OpenApiContractEndpoint list) =
        let extracted =
            endpoints
            |> List.map (fun endpoint ->
                {
                    Name = endpoint.Name
                    ArgTypes = endpoint.ArgTypes
                    ReturnType = endpoint.ReturnType
                })

        buildFromExtracted options extracted

module OpenApiDefaults =
    let endpointDocumentation = DocumentBuilding.defaultEndpointDocs

    let options : OpenApiOptions =
        {
            Title = "Fable.Remoting API"
            Version = "1.0.0"
            Description = None
            Contact = None
            License = None
            Servers = []
            Routes = {
                JsonPath = "/openapi.json"
                YamlPath = "/openapi.yaml"
                DocsPath = "/docs"
            }
            DocsContent = []
            EndpointDocs = Map.empty
            OperationIdStrategy = id
            EndpointRouteStrategy = (fun endpointName -> sprintf "/api/%s" endpointName)
            SchemaNameStrategy = (fun t -> t.Name)
        }

module OpenApi =
    let options : OpenApiOptions = OpenApiDefaults.options

    let withTitle title (options: OpenApiOptions) = { options with Title = title }
    let withVersion version (options: OpenApiOptions) = { options with Version = version }
    let withDescription description (options: OpenApiOptions) = { options with Description = Some description }
    let withoutDescription (options: OpenApiOptions) = { options with Description = None }
    let withContact contact (options: OpenApiOptions) = { options with Contact = Some contact }
    let withoutContact (options: OpenApiOptions) = { options with Contact = None }
    let withLicense license (options: OpenApiOptions) = { options with License = Some license }
    let withoutLicense (options: OpenApiOptions) = { options with License = None }
    let withServers servers (options: OpenApiOptions) = { options with Servers = servers }
    let withRoutes routes (options: OpenApiOptions) = { options with Routes = routes }
    let withDocsContent content (options: OpenApiOptions) = { options with DocsContent = content }

    let withEndpointDocs endpointName endpointDocs (options: OpenApiOptions) =
        { options with EndpointDocs = options.EndpointDocs |> Map.add endpointName endpointDocs }

    let withOperationIdStrategy strategy (options: OpenApiOptions) = { options with OperationIdStrategy = strategy }
    let withEndpointRouteStrategy strategy (options: OpenApiOptions) = { options with EndpointRouteStrategy = strategy }
    let withSchemaNameStrategy strategy (options: OpenApiOptions) = { options with SchemaNameStrategy = strategy }

    let generate<'Api> (options: OpenApiOptions) =
        let model = DocumentBuilding.build<'Api> options
        let asJsonValue = JsonRendering.toJsonValue model
        let json = JsonRendering.serialize asJsonValue
        let yaml = YamlRendering.serialize asJsonValue

        {
            Json = json
            Yaml = yaml
            Model = model :> obj
            Diagnostics = model.Diagnostics
            Routes = options.Routes
        }

    let generateFromEndpoints (endpoints: OpenApiContractEndpoint list) (options: OpenApiOptions) =
        let model = DocumentBuilding.buildFromContractEndpoints options endpoints
        let asJsonValue = JsonRendering.toJsonValue model
        let json = JsonRendering.serialize asJsonValue
        let yaml = YamlRendering.serialize asJsonValue

        {
            Json = json
            Yaml = yaml
            Model = model :> obj
            Diagnostics = model.Diagnostics
            Routes = options.Routes
        }

module OpenApiGiraffe =
    let private docsHtml (document: OpenApiDocument) =
        let docsBlocks =
            (document.Model :?> OpenApiDocumentModel).DocsContent
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
