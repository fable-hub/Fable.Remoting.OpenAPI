# AGENTS

Instructions for future AI coding agents working in this repository.

## Repository Overview

- Core library: src/Fable.Remoting.OpenAPI
- Giraffe adapter: src/Fable.Remoting.OpenAPI.Giraffe
- Suave adapter: src/Fable.Remoting.OpenAPI.Suave
- Main solution (development/test): Fable.Remoting.OpenApi.sln
- Release solution (pack/publish): Release.sln
- Integration playground: app
- Core tests: tests/Fable.Remoting.OpenAPI
- Adapter tests: tests/Fable.Remoting.OpenAPI.Adapters

## Standard Commands

- Restore workspace:
  - dotnet restore ./Fable.Remoting.OpenApi.sln
- Build workspace:
  - dotnet build ./Fable.Remoting.OpenApi.sln --no-restore
- Run tests:
  - dotnet test ./Fable.Remoting.OpenApi.sln
- Restore release solution:
  - dotnet restore ./Release.sln
- Pack release artifacts:
  - dotnet pack ./Release.sln -c Release --no-restore -o artifacts

## Important Behavioral Contracts

- OpenAPI generation should stay aligned with Fable.Remoting semantics:
  - Unit input endpoints map to GET.
  - Non-unit input endpoints map to POST.
  - Remoting request body shape is JSON array.
- Route-builder integration must remain first-class:
  - OpenApi.withDocs should follow active remoting route builder.
  - Default docs routes derive from route builder and API type name unless explicitly overridden.

## Architecture Boundaries

- Keep framework-specific HTTP handler code out of core library.
- Core project should remain framework-agnostic and focused on:
  - metadata extraction
  - document model creation
  - deterministic rendering
- Adapter projects own web framework composition helpers.

## Test and Change Discipline

- Add or update tests when behavior changes.
- Prefer deterministic assertions for JSON/YAML output.
- If changing route or transport semantics, update:
  - core tests
  - adapter tests
  - sample app docs links
  - README and CONTRIBUTING
  - project specific CHANGELOG.md with details and rationale.

## CI Alignment

- CI is solution-driven. Keep workflow commands targeting:
  - ./Fable.Remoting.OpenApi.sln for restore/build/test
  - ./Release.sln for restore/pack
- Keep publish command using --skip-duplicate.

## Safety Notes

- Do not revert unrelated user changes in working tree.
- Avoid introducing framework coupling into core project references.
- Preserve package version compatibility across adapter and core projects.
