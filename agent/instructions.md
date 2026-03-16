You are an autonomous coding agent working inside this repository.  
Goal: implement a production-ready OpenAPI plugin for Fable.Remoting that auto-generates OpenAPI docs from shared server-client contracts, with strong testability and customization.

Repository constraints:
1. Put library/plugin source under src, specifically Fable.Remoting.OpenAPI.
2. Use app as an in-production integration playground. You may change any server/client setup needed.
3. Add/modify tests under tests and tests as needed.
4. You are allowed to make any repo changes required.

Functional requirements:
1. Generate OpenAPI documentation automatically for Fable.Remoting APIs (record-of-functions style).
2. Support output in JSON and YAML, with deterministic formatting for snapshot-style tests.
3. Expose docs endpoints in the sample app (for example /openapi.json, /openapi.yaml, /docs).
4. Implement customizable documentation options inspired by FastAPI and C# ecosystems (Swashbuckle/NSwag style):
- Title, version, description, contact, license, servers.
- Route customization for schema and UI pages.
- Per-endpoint summary/description/tags.
- Free-text docs content (markdown or plain text blocks).
- Examples for request/response payloads.
- OperationId naming strategy customization.
5. Design architecture for testability:
- Separate metadata extraction, intermediate model, and OpenAPI rendering.
- Prefer pure functions and small composable modules.
- Minimize framework coupling in core logic.
6. Add edge-case coverage in tests:
- Nested records/unions/options/lists/maps.
- Async return types and error modeling.
- Duplicate route names, unsupported types, missing metadata.
- Empty APIs and mixed success/failure responses.
- YAML rendering stability and key ordering.
7. Set up missing test dependencies (xUnit and any required assertion/snapshot libs).

Project context to inspect first:
- Shared contract example: Shared.fs
- Current server wiring: Server.fs
- Current plugin stub: Library.fs

Implementation guidance:
1. Keep public API ergonomic for existing Fable.Remoting users.
2. Provide extension points rather than hardcoding conventions.
3. Add examples in README or docs showing:
- Basic setup.
- Custom metadata/docs text.
- Custom route paths.
- YAML output usage.
4. Preserve compatibility mindset with typical Fable.Remoting usage patterns.

Deliverables:
1. Working plugin code under src.
2. Integration wired and runnable in app.
3. Comprehensive tests under tests and/or tests.
4. Updated documentation explaining installation, configuration, and customization.
5. Clear notes on known limitations and future extension points.

Execution rules:
1. Make incremental commits internally (if needed), but prioritize passing tests.
2. Run and report test results.
3. If you hit ambiguity, choose the most maintainable design and document reasoning in the final summary.
4. Do not stop at analysis; implement end-to-end.

Final acceptance criteria:
1. Running tests succeeds for plugin and integration tests.
2. Sample app exposes generated OpenAPI JSON and YAML.
3. Docs customization options are demonstrated and verified by tests.
4. README contains copy-paste integration examples.