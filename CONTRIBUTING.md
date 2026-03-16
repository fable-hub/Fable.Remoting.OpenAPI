# Contributing

## Prerequisites

- .NET SDK 8.0+
- Node.js 18+ (required for sample app client tooling)

## Repository Layout

- `src/Fable.Remoting.OpenAPI`: Core OpenAPI generator and options.
- `src/Fable.Remoting.OpenAPI.Giraffe`: Giraffe adapter and remoting composition API.
- `src/Fable.Remoting.OpenAPI.Suave`: Suave docs adapter/composition helpers.
- `tests/Fable.Remoting.OpenAPI`: Core behavior tests.
- `tests/Fable.Remoting.OpenAPI.Adapters`: Adapter parity and integration-shape tests.
- `app/`: SAFE sample application.

## Setup

1. Restore .NET dependencies:

   ```bash
   dotnet restore ./Fable.Remoting.OpenApi.sln
   ```

2. Build the full workspace solution:

   ```bash
   dotnet build ./Fable.Remoting.OpenApi.sln --no-restore
   ```

## Testing

Run the package test suite:

```bash
dotnet test ./Fable.Remoting.OpenApi.sln
```

Optional targeted test run:

```bash
dotnet test app/tests/Server/Server.Tests.fsproj
```

## Packaging

Restore and pack publishable projects via release solution:

```bash
dotnet restore ./Release.sln
dotnet pack ./Release.sln -c Release --no-restore -o artifacts
```

Push packages manually:

```bash
dotnet nuget push "artifacts/*.nupkg" --source https://api.nuget.org/v3/index.json --api-key <NUGET_API_KEY> --skip-duplicate
```

## Release Notes

Each project under `src` keeps release notes in a local `CHANGELOG.md`.
`EasyBuild.PackageReleaseNotes.Tasks` is configured through `Directory.Build.props` so package release notes are populated at pack time.
