# ReferenceSwitcher Codebase Guidelines

## Project Purpose
ReferenceSwitcher is a .NET tool that automates switching between `PackageReference` and `ProjectReference` entries inside solutions. Its goal is to simplify local debugging when multiple interdependent libraries need to be temporarily developed together or decoupled into NuGet packages.

## General Development Rules
- Prefer functional programming patterns and leverage [CSharpFunctionalExtensions](https://github.com/vkhorikov/CSharpFunctionalExtensions) where doing so keeps the code clear.
- Avoid underscores when naming private fields.
- Do not suffix method names with `Async`, even if they return `Task`.
- When building reactive components, avoid placing logic directly inside `Subscribe` calls and prefer using DynamicData within view models.
- UI work should be built on Avalonia, with ReactiveUI and programming reactive patterns allowed when helpful.

These rules apply to the entire repository unless a more specific `AGENTS.md` is present in a subdirectory.
