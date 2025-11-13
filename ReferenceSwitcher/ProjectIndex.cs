using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher;

internal sealed class ProjectIndex
{
    private readonly IReadOnlyDictionary<string, ProjectMetadata> packageIdIndex;
    private readonly IReadOnlyDictionary<string, ProjectMetadata> pathIndex;

    private ProjectIndex(IReadOnlyDictionary<string, ProjectMetadata> packageIdIndex, IReadOnlyDictionary<string, ProjectMetadata> pathIndex)
    {
        this.packageIdIndex = packageIdIndex;
        this.pathIndex = pathIndex;
    }

    public static Result<ProjectIndex> Build(string scanDirectory)
    {
        if (!Directory.Exists(scanDirectory))
            return Result.Failure<ProjectIndex>($"The scan directory '{scanDirectory}' does not exist.");

        var projects = Directory.GetFiles(scanDirectory, "*.csproj", SearchOption.AllDirectories);
        if (projects.Length == 0)
            return Result.Failure<ProjectIndex>($"No projects were found in '{scanDirectory}'.");

        var metadataList = new List<ProjectMetadata>();
        foreach (var project in projects)
        {
            var metadataResult = ProjectMetadata.Create(project);
            if (metadataResult.IsFailure)
                return metadataResult.ConvertFailure<ProjectIndex>();

            if (!metadataResult.Value.HasPackageId)
                continue;

            metadataList.Add(metadataResult.Value);
        }

        if (metadataList.Count == 0)
            return Result.Failure<ProjectIndex>($"No projects with PackageId were found in '{scanDirectory}'.");

        var packageIdIndex = BuildPackageIndex(metadataList);
        if (packageIdIndex.Count == 0)
            return Result.Failure<ProjectIndex>($"No unique projects with PackageId were found in '{scanDirectory}'.");

        var pathIndex = packageIdIndex.Values.ToDictionary(m => m.ProjectPath, m => m, StringComparer.OrdinalIgnoreCase);

        return Result.Success(new ProjectIndex(packageIdIndex, pathIndex));
    }

    public Maybe<ProjectMetadata> FindByPackageId(string packageId)
    {
        return packageIdIndex.TryGetValue(packageId, out var metadata)
            ? Maybe.From(metadata)
            : Maybe<ProjectMetadata>.None;
    }

    public Maybe<ProjectMetadata> FindByPath(string projectPath)
    {
        var normalizedPath = Path.GetFullPath(projectPath);
        return pathIndex.TryGetValue(normalizedPath, out var metadata)
            ? Maybe.From(metadata)
            : Maybe<ProjectMetadata>.None;
    }

    private static IReadOnlyDictionary<string, ProjectMetadata> BuildPackageIndex(IEnumerable<ProjectMetadata> metadataList)
    {
        var duplicates = new List<string>();
        var index = new Dictionary<string, ProjectMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in metadataList.GroupBy(m => m.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = group.ToList();
            if (candidates.Count == 0)
                continue;

            if (candidates.Count > 1)
                duplicates.Add(group.Key);

            var selected = candidates
                .OrderBy(metadata => ContainsReferenceSegment(metadata.ProjectPath) ? 0 : 1)
                .ThenBy(metadata => metadata.ProjectPath.Length)
                .ThenBy(metadata => metadata.ProjectPath, StringComparer.OrdinalIgnoreCase)
                .First();

            index[group.Key] = selected;
        }

        if (duplicates.Count > 0)
        {
            Console.Error.WriteLine(
                $"Multiple projects with the same PackageId were found. The first match will be used for: {string.Join(", ", duplicates)}.");
        }

        return index;
    }

    private static bool ContainsReferenceSegment(string path)
    {
        var segments = path
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment => segment.Equals("reference", StringComparison.OrdinalIgnoreCase));
    }
}
