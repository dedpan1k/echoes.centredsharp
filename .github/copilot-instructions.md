---
description: Workspace-wide guidance for contributing to centeredsharp.
---

<!-- Tip: Use /create-instructions in chat to generate content with agent assistance -->

# centeredsharp Workspace Instructions

## Project Scope

These instructions apply across the centeredsharp workspace.

- Main solution: `CentrEDSharp.sln`
- Primary app: `CentrED/` is the desktop editor built on FNA.
- Supporting projects: `Client/`, `Server/`, and `Shared/`
- First-party tests: `CentrED.Tests/` and `Shared.Tests/`
- Examples: `examples/`
- Vendored dependencies: `external/` and platform libraries under `lib/`

## Build And Test

- Use .NET 10 SDK. The repository README explicitly requires it.
- Prefer solution-level commands unless a task is scoped to one project.
- Build with `dotnet build CentrEDSharp.sln` or the VS Code task `build CentrEDSharp solution`.
- Run tests with `dotnet test CentrEDSharp.sln`.
- Assume submodules are required. If external dependencies appear to be missing, check whether the repo was cloned with `--recursive` before changing project files.

## Architecture

- `Shared/` contains core data structures, networking primitives, and logic reused by other projects.
- `Client/` contains client-side communication and map-related support code used by the editor.
- `Server/` contains the collaborative editing server and its configuration.
- `CentrED/` composes the client, server, shared code, FNA, and FontStashSharp into the editor application.
- `external/` contains vendored upstream dependencies. Do not modify these unless the task explicitly targets them.

## Conventions

- Follow the existing `.editorconfig` formatting rules: C# uses spaces with 4-space indentation.
- Keep the existing C# style: file-scoped namespaces, nullable enabled, implicit usings enabled, and the current naming/style conventions.
- Match the surrounding style before introducing new abstractions. This codebase does not favor broad refactors for small fixes.
- Preserve manual assembly/versioning setup. Projects commonly use `GenerateAssemblyInfo=false`; avoid reworking version metadata unless the task requires it.

## Build Pitfalls

- `CentrED/CentrED.csproj` writes build output to `output/` instead of the default `bin/<Configuration>/<TFM>/` layout.
- The build copies OS-specific native libraries from `lib/` and `external/fna-libs/`. Be careful when changing project files, publish settings, or runtime asset handling.
- The editor and server may behave differently when run in non-interactive environments; avoid changing startup or console behavior without checking the entry points first.

## Testing And Changes

- For code changes, run the narrowest relevant tests first, then broader solution tests if the change affects shared behavior.
- Keep fixes focused. Do not rewrite unrelated files or update vendored code to solve a local issue.
- When editing rendering, map, networking, or shared serialization code, verify the affected project boundaries before making changes because these areas are reused across multiple assemblies.

## Useful References

- `Readme.md` for setup and top-level build requirements
- `.editorconfig` for formatting and naming defaults
- `CentrED/CentrED.csproj` for runtime asset copying and output behavior
- `CentrED/Application.cs` and `Server/Application.cs` for application startup patterns