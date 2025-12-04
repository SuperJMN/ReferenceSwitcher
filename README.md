# ReferenceSwitcher

ReferenceSwitcher is a .NET command line tool that automates switching between `PackageReference` and `ProjectReference` entries inside solutions. It is intended to make it easy to switch between developing against local projects and consuming NuGet packages.

## Installation

The tool is published as a .NET tool (see the `.csproj` configuration). You can build and install it locally with:

```bash
dotnet build
# Then install the tool locally from the produced package if desired
```

## Usage

The tool exposes two main commands:

- `to-projects`: replaces `PackageReference` entries with `ProjectReference` entries when matching projects are found on disk.
- `to-packages`: replaces `ProjectReference` entries with `PackageReference` entries when matching packages are found.

Both commands share the same basic arguments:

```bash
reference-switcher <command> \
  --solution <path-to-sln> \
  --scan-directory <directory-to-scan> \
  [--add-projects-to-solution]
```

- `--solution` (`-s`): path to the `.sln` file that defines the starting projects.
- `--scan-directory` (`-d`): directory that will be scanned recursively for `.csproj` files used to build the project index.
- `--add-projects-to-solution`: when specified, the tool also updates the solution file to reflect the changes.

### Behavior of `--add-projects-to-solution`

When `--add-projects-to-solution` is used, the behavior depends on the selected command:

#### `to-projects`

- After switching `PackageReference` entries to `ProjectReference`, the tool tracks all projects discovered via matching `PackageId` values.
- Any discovered project that is not already present in the solution is added as a new `Project(...)` entry in the `.sln` file.
- The new projects also receive entries in the `ProjectConfigurationPlatforms` section so that they participate in the existing solution configurations (for example `Debug|Any CPU` and `Release|Any CPU`).

If `--add-projects-to-solution` is **not** specified, only the `.csproj` files are updated and the solution file is left untouched.

#### `to-packages`

- After switching `ProjectReference` entries back to `PackageReference`, the tool looks for projects in the solution that are considered **foreign** and removes them from the `.sln` file.
- A project is considered foreign if its `.csproj` path is outside a reference root:
  - First, the tool walks up from the solution directory until it finds a directory that contains a `.git` folder. That directory is used as the reference root.
  - If no Git repository is found, the parent directory of the solution file is used as the reference root instead.
- Any project whose resolved absolute `.csproj` path is not under this reference root is treated as foreign.
- For each foreign project, the corresponding `Project(...)` / `EndProject` block is removed from the solution file, and any lines in `Global` sections that reference the project GUID (such as `ProjectConfigurationPlatforms` or nested project mappings) are also removed.

If `--add-projects-to-solution` is **not** specified, only the `.csproj` files are updated and the solution file is left untouched.

## Notes

- The tool relies on `PackageId` metadata (and related MSBuild properties) to match projects with NuGet packages.
- When multiple projects share the same `PackageId`, a deterministic selection heuristic is used to pick one candidate, and a warning is written to `stderr`.
