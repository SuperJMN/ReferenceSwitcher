using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher;

internal sealed record ProjectMetadata(string PackageId, string ProjectPath, string ProjectName)
{
    public bool HasPackageId => !string.IsNullOrWhiteSpace(PackageId);

    public static Result<ProjectMetadata> Create(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;

            var assemblyName = ReadElementValue(document, ns, "AssemblyName")
                .Match(value => value, () => Path.GetFileNameWithoutExtension(projectPath));

            var packageId = ReadPackageId(document, projectPath)
                .Match(value => NormalizePackageId(value, projectPath, assemblyName), () => string.Empty);

            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var normalizedPath = Path.GetFullPath(projectPath);

            return Result.Success(new ProjectMetadata(packageId, normalizedPath, projectName));
        }
        catch (Exception exception)
        {
            return Result.Failure<ProjectMetadata>($"Failed to parse project '{projectPath}': {exception.Message}");
        }
    }

    private static Maybe<string> ReadPackageId(XDocument document, string projectPath)
    {
        var normalizedPath = Path.GetFullPath(projectPath);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalizedPath };

        return ReadPropertyRecursive(document, normalizedPath, "PackageId", visited)
            .Or(() => ReadFromDirectoryBuildProps(normalizedPath));
    }

    private static Maybe<string> ReadPropertyRecursive(XDocument document, string documentPath, string elementName, ISet<string> visited)
    {
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var directValue = ReadElementValue(document, ns, elementName);
        if (directValue.HasValue)
            return directValue;

        var directory = Path.GetDirectoryName(documentPath);
        if (string.IsNullOrWhiteSpace(directory))
            return Maybe<string>.None;

        foreach (var import in ReadImportPaths(document, ns))
        {
            var resolvedPath = ResolveImportPath(import, directory);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
                continue;

            if (!visited.Add(resolvedPath))
                continue;

            try
            {
                var importDocument = XDocument.Load(resolvedPath);
                var value = ReadPropertyRecursive(importDocument, resolvedPath, elementName, visited);
                if (value.HasValue)
                    return value;
            }
            catch
            {
                // Ignore invalid import files.
            }
        }

        return Maybe<string>.None;
    }

    private static IEnumerable<string> ReadImportPaths(XDocument document, XNamespace ns)
    {
        return document
            .Descendants(ns + "Import")
            .Select(x => x.Attribute("Project")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!);
    }

    private static string ResolveImportPath(string importPath, string baseDirectory)
    {
        var normalizedBase = EnsureTrailingSeparator(Path.GetFullPath(baseDirectory));
        var candidate = importPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace("$(MSBuildThisFileDirectory)", normalizedBase)
            .Replace("$(MSBuildProjectDirectory)", normalizedBase);

        if (candidate.Contains("$("))
            return string.Empty;

        if (Path.IsPathRooted(candidate))
            return Path.GetFullPath(candidate);

        return Path.GetFullPath(Path.Combine(normalizedBase, candidate));
    }

    private static Maybe<string> ReadFromDirectoryBuildProps(string projectPath)
    {
        var directory = Path.GetDirectoryName(projectPath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var propsPath = Path.Combine(directory, "Directory.Build.props");
            if (File.Exists(propsPath))
            {
                try
                {
                    var document = XDocument.Load(propsPath);
                    var ns = document.Root?.Name.Namespace ?? XNamespace.None;
                    var value = ReadElementValue(document, ns, "PackageId");
                    if (value.HasValue)
                        return value;
                }
                catch
                {
                    // Ignore invalid files.
                }
            }

            directory = Path.GetDirectoryName(directory);
        }

        return Maybe<string>.None;
    }

    private static string NormalizePackageId(string packageId, string projectPath, string assemblyName)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var normalizedAssemblyName = string.IsNullOrWhiteSpace(assemblyName) ? projectName : assemblyName;

        return packageId
            .Replace("$(MSBuildProjectName)", projectName)
            .Replace("$(AssemblyName)", normalizedAssemblyName)
            .Trim();
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var separator = Path.DirectorySeparatorChar.ToString();
        return path.EndsWith(separator, StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static Maybe<string> ReadElementValue(XDocument document, XNamespace ns, string elementName)
    {
        var element = document
            .Descendants(ns + elementName)
            .Select(x => x.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return string.IsNullOrWhiteSpace(element) ? Maybe<string>.None : Maybe.From(element.Trim());
    }
}
