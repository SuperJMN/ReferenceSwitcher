using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher;

internal sealed class ProjectToPackageSwitcher
{
    private readonly ProjectIndex projectIndex;
    private readonly TextWriter writer;

    public ProjectToPackageSwitcher(ProjectIndex projectIndex, TextWriter writer)
    {
        this.projectIndex = projectIndex;
        this.writer = writer;
    }

    public Result Switch(IReadOnlyCollection<string> rootProjects)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in rootProjects)
        {
            var result = ReplaceProjects(project, visited);
            if (result.IsFailure)
                return result;
        }

        return Result.Success();
    }

    private Result ReplaceProjects(string projectPath, ISet<string> visited)
    {
        var normalizedPath = Path.GetFullPath(projectPath);
        if (!visited.Add(normalizedPath))
            return Result.Success();

        if (!File.Exists(normalizedPath))
            return Result.Failure($"No se encontró el proyecto '{normalizedPath}'.");

        if (!TryLoadDocument(normalizedPath, out var document, out var error))
            return Result.Failure(error);

        var projectDirectory = Path.GetDirectoryName(normalizedPath) ?? Directory.GetCurrentDirectory();
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var projectReferences = document
            .Descendants(ns + "ProjectReference")
            .ToList();

        var groupsToReview = new HashSet<XElement>();
        var changed = false;

        foreach (var projectReference in projectReferences)
        {
            var include = projectReference.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
                continue;

            var absoluteReference = Path.GetFullPath(Path.Combine(projectDirectory, include));
            var metadataOption = projectIndex.FindByPath(absoluteReference);
            if (metadataOption.HasNoValue)
                continue;

            var metadata = metadataOption.Value;
            var parentGroup = projectReference.Parent as XElement;

            var alreadyExists = document
                .Descendants(ns + "PackageReference")
                .Any(x => string.Equals(x.Attribute("Include")?.Value, metadata.PackageId, StringComparison.OrdinalIgnoreCase));

            projectReference.Remove();
            changed = true;
            if (parentGroup is not null)
                groupsToReview.Add(parentGroup);

            if (alreadyExists)
            {
                writer.WriteLine($"[{metadata.PackageId}] Ya existe PackageReference en '{normalizedPath}'. Eliminado ProjectReference.");
            }
            else
            {
                var targetGroup = parentGroup ?? FindOrCreatePackageGroup(document, ns);
                var packageReference = new XElement(ns + "PackageReference", new XAttribute("Include", metadata.PackageId));
                targetGroup.Add(packageReference);
                writer.WriteLine($"[{metadata.PackageId}] Reemplazado ProjectReference por PackageReference en '{normalizedPath}'.");
            }

            var recursionResult = ReplaceProjects(metadata.ProjectPath, visited);
            if (recursionResult.IsFailure)
                return recursionResult;
        }

        foreach (var group in groupsToReview)
        {
            if (!group.Elements().Any())
            {
                group.Remove();
                changed = true;
            }
        }

        if (changed)
            document.Save(normalizedPath);

        return Result.Success();
    }

    private static bool TryLoadDocument(string projectPath, out XDocument document, out string error)
    {
        try
        {
            document = XDocument.Load(projectPath);
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            document = new XDocument();
            error = $"No se pudo leer '{projectPath}': {exception.Message}";
            return false;
        }
    }

    private static XElement FindOrCreatePackageGroup(XDocument document, XNamespace ns)
    {
        var existing = document
            .Descendants(ns + "ItemGroup")
            .FirstOrDefault(group => group.Elements(ns + "PackageReference").Any());

        if (existing is not null)
            return existing;

        var newGroup = new XElement(ns + "ItemGroup");
        document.Root?.Add(newGroup);
        return newGroup;
    }
}
