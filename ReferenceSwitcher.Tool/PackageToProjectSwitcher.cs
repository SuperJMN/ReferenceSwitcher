using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher.Tool;

internal sealed class PackageToProjectSwitcher
{
    private readonly ProjectIndex projectIndex;
    private readonly TextWriter writer;
    private readonly HashSet<string> discoveredProjects = new(StringComparer.OrdinalIgnoreCase);

    public PackageToProjectSwitcher(ProjectIndex projectIndex, TextWriter writer)
    {
        this.projectIndex = projectIndex;
        this.writer = writer;
    }

    public IReadOnlyCollection<string> DiscoveredProjects => discoveredProjects;

    public Result Switch(IReadOnlyCollection<string> rootProjects)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in rootProjects)
        {
            var result = ReplacePackages(project, visited);
            if (result.IsFailure)
                return result;
        }

        return Result.Success();
    }

    private Result ReplacePackages(string projectPath, ISet<string> visited)
    {
        var normalizedPath = Path.GetFullPath(projectPath);
        if (!visited.Add(normalizedPath))
            return Result.Success();

        if (!File.Exists(normalizedPath))
            return Result.Failure($"Project '{normalizedPath}' was not found.");

        if (!TryLoadDocument(normalizedPath, out var document, out var error))
            return Result.Failure(error);

        var projectDirectory = Path.GetDirectoryName(normalizedPath) ?? Directory.GetCurrentDirectory();
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var packageReferences = document
            .Descendants(ns + "PackageReference")
            .ToList();

        var groupsToReview = new HashSet<XElement>();
        var changed = false;

        foreach (var packageReference in packageReferences)
        {
            var include = packageReference.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
                continue;

            var metadataOption = projectIndex.FindByPackageId(include);
            if (metadataOption.HasNoValue)
                continue;

            var metadata = metadataOption.Value;
            if (string.Equals(metadata.ProjectPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                continue;

            discoveredProjects.Add(metadata.ProjectPath);

            var relativePath = Path.GetRelativePath(projectDirectory, metadata.ProjectPath);
            var normalizedRelativePath = NormalizeRelativePath(relativePath);

            var existingProjectReference = document
                .Descendants(ns + "ProjectReference")
                .FirstOrDefault(x => string.Equals(NormalizeRelativePath(x.Attribute("Include")?.Value ?? string.Empty), normalizedRelativePath, StringComparison.OrdinalIgnoreCase));

            var parentGroup = packageReference.Parent as XElement;

            if (existingProjectReference is null)
            {
                var projectReference = CreateProjectReference(metadata, normalizedRelativePath, ns);
                parentGroup ??= CreateItemGroup(document, ns);
                parentGroup.Add(projectReference);
                writer.WriteLine($"[{metadata.PackageId}] Replaced PackageReference with ProjectReference in '{normalizedPath}'.");
            }
            else
            {
                var attributesChanged = ApplyProjectReferenceAttributes(existingProjectReference, metadata);
                if (attributesChanged)
                    changed = true;

                writer.WriteLine($"[{metadata.PackageId}] ProjectReference already exists in '{normalizedPath}'. Removing duplicate PackageReference.");
            }

            packageReference.Remove();
            changed = true;

            if (parentGroup is not null)
                groupsToReview.Add(parentGroup);

            var recursionResult = ReplacePackages(metadata.ProjectPath, visited);
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
            error = $"Failed to read '{projectPath}': {exception.Message}";
            return false;
        }
    }

    private static XElement CreateProjectReference(ProjectMetadata metadata, string include, XNamespace ns)
    {
        var projectReference = new XElement(ns + "ProjectReference", new XAttribute("Include", include));
        ApplyProjectReferenceAttributes(projectReference, metadata);
        return projectReference;
    }

    private static bool ApplyProjectReferenceAttributes(XElement projectReference, ProjectMetadata metadata)
    {
        if (!metadata.IsAnalyzer)
            return false;

        var updated = false;
        updated |= EnsureAttribute(projectReference, "OutputItemType", "Analyzer");
        updated |= EnsureAttribute(projectReference, "ReferenceOutputAssembly", "false");
        return updated;
    }

    private static bool EnsureAttribute(XElement projectReference, string attributeName, string value)
    {
        var attribute = projectReference.Attribute(attributeName);
        if (attribute is null || !string.Equals(attribute.Value, value, StringComparison.Ordinal))
        {
            projectReference.SetAttributeValue(attributeName, value);
            return true;
        }

        return false;
    }

    private static XElement CreateItemGroup(XDocument document, XNamespace ns)
    {
        var itemGroup = new XElement(ns + "ItemGroup");
        document.Root?.Add(itemGroup);
        return itemGroup;
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
