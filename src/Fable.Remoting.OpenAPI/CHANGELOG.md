# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog,
and this project adheres to Semantic Versioning.

## [Unreleased]

## 0.0.2 - 2026-03-16

### Added

- Support for OpenAPI media-type `examples` objects for endpoint request and response bodies.
- `OpenApiExampleMetadata` for named examples with optional `summary`, `description`, and `externalValue`.
- New helpers:
	- `OpenApi.withEndpointRequestNamedExampleFor`
	- `OpenApi.withEndpointResponseNamedExampleFor`
	- `OpenAPI.withEndpointRequestNamedExampleFor`
	- `OpenAPI.withEndpointResponseNamedExampleFor`

### Changed

- `OpenApi.withEndpointResponseExampleFor` now accepts typed endpoint expressions for both unit and non-unit input endpoints.
- When named examples are configured, generated media types emit `examples` (plural) and prefer it over legacy single `example` output.

## 0.0.1 - 2026-03-16

### Added

- Type-safe endpoint documentation and example helpers via quotations.
- Remoting-aware OpenAPI generation that follows route builder configuration.
- Route-builder-derived default docs routes under the API type path.

### Changed

- Request modeling now aligns with Fable.Remoting transport semantics.
- Unit-input endpoints are generated as GET operations.
- Non-unit endpoints use POST with JSON-array request body schemas.

### Fixed

- Stable generation for request examples in remoting array-body format.
