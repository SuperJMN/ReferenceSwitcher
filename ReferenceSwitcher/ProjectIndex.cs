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
            return Result.Failure<ProjectIndex>($"El directorio de escaneo '{scanDirectory}' no existe.");

        var projects = Directory.GetFiles(scanDirectory, "*.csproj", SearchOption.AllDirectories);
        if (projects.Length == 0)
            return Result.Failure<ProjectIndex>($"No se encontraron proyectos en '{scanDirectory}'.");

        var metadataList = new List<ProjectMetadata>();
        foreach (var project in projects)
        {
            var metadataResult = ProjectMetadata.Create(project);
            if (metadataResult.IsFailure)
                return metadataResult.ConvertFailure<ProjectIndex>();

            metadataList.Add(metadataResult.Value);
        }

        var duplicates = metadataList
            .GroupBy(m => m.PackageId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
            return Result.Failure<ProjectIndex>($"Se encontraron múltiples proyectos con el mismo PackageId: {string.Join(", ", duplicates)}.");

        var packageIdIndex = metadataList.ToDictionary(m => m.PackageId, m => m, StringComparer.OrdinalIgnoreCase);
        var pathIndex = metadataList.ToDictionary(m => m.ProjectPath, m => m, StringComparer.OrdinalIgnoreCase);

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
}
