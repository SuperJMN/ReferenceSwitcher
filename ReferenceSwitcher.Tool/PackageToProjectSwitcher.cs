using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
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
                AddElementWithFormatting(parentGroup, projectReference);
                writer.WriteLine($"[{metadata.PackageId}] Replaced PackageReference with ProjectReference in '{normalizedPath}'.");
            }
            else
            {
                var attributesChanged = ApplyProjectReferenceAttributes(existingProjectReference, metadata);
                if (attributesChanged)
                    changed = true;

                writer.WriteLine($"[{metadata.PackageId}] ProjectReference already exists in '{normalizedPath}'. Removing duplicate PackageReference.");
            }

            RemoveElementWithWhitespace(packageReference);
            changed = true;

            if (parentGroup is not null)
                groupsToReview.Add(parentGroup);

            var recursionResult = ReplacePackages(metadata.ProjectPath, visited);
            if (recursionResult.IsFailure)
                return recursionResult;
        }

        // Recursively visit all ProjectReferences (both existing and newly created)
        var projectReferences = document
            .Descendants(ns + "ProjectReference")
            .Select(x => x.Attribute("Include")?.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Path.GetFullPath(Path.Combine(projectDirectory, x!.Replace('\\', Path.DirectorySeparatorChar))))
            .ToList();

        foreach (var projectRef in projectReferences)
        {
            var result = ReplacePackages(projectRef, visited);
            if (result.IsFailure)
                return result;
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
            SaveDocument(document, normalizedPath);

        return Result.Success();
    }

    private static bool TryLoadDocument(string projectPath, out XDocument document, out string error)
    {
        try
        {
            document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
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

    private static void SaveDocument(XDocument document, string path)
    {
        var indentChars = DetectIndentation(document);

        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = indentChars,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace
        };

        using var writer = XmlWriter.Create(path, settings);
        document.Save(writer);
    }

    private static void AddElementWithFormatting(XElement parent, XElement newElement)
    {
        // Detect indentation from the parent's children
        var indentChars = DetectElementIndentation(parent);

        // Add newline and indent before the new element
        if (parent.LastNode is XText lastText && lastText.Value.EndsWith("\n"))
        {
            // Parent already has proper formatting, just add indent
            parent.Add(new XText(indentChars));
        }
        else if (parent.Elements().Any())
        {
            // Parent has elements but no trailing newline
            parent.Add(new XText("\n" + indentChars));
        }
        else
        {
            // First element in parent
            parent.Add(new XText("\n" + indentChars));
        }

        parent.Add(newElement);

        // Add newline after the element
        parent.Add(new XText("\n"));
    }

    private static void RemoveElementWithWhitespace(XElement element)
    {
        // Remove preceding whitespace-only text node if it exists
        var previousNode = element.PreviousNode;
        if (previousNode is XText previousText &&
            !string.IsNullOrEmpty(previousText.Value) &&
            previousText.Value.All(char.IsWhiteSpace))
        {
            previousText.Remove();
        }

        // Also check for and remove trailing newline node
        var nextNode = element.NextNode;
        if (nextNode is XText nextText &&
            nextText.Value == "\n")
        {
            nextText.Remove();
        }

        element.Remove();
    }

    private static string DetectElementIndentation(XElement element)
    {
        // Look for indentation in existing child elements
        foreach (var node in element.Nodes())
        {
            if (node is XText text && text.Value.Contains('\n'))
            {
                var lines = text.Value.Split('\n');
                var lastLine = lines[^1];
                if (!string.IsNullOrEmpty(lastLine) && lastLine.All(char.IsWhiteSpace))
                {
                    return lastLine;
                }
            }
        }

        // Fallback: detect from document level
        return DetectIndentation(element.Document ?? new XDocument(element));
    }

    private static string DetectIndentation(XDocument document)
    {
        // Analyze whitespace-only text nodes that appear between elements
        var whitespaceNodes = document.DescendantNodes()
            .OfType<XText>()
            .Where(t => !string.IsNullOrEmpty(t.Value) && t.Value.All(c => char.IsWhiteSpace(c)))
            .ToList();

        foreach (var node in whitespaceNodes)
        {
            var lines = node.Value.Split('\n');
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line))
                    continue;

                // Check if it's a tab-indented document
                if (line.StartsWith("\t"))
                    return "\t";

                // Check for spaces (common patterns: 2, 4 spaces)
                if (line.StartsWith("  "))
                {
                    // Count leading spaces to determine indent level
                    var spaceCount = line.TakeWhile(c => c == ' ').Count();
                    if (spaceCount >= 2)
                        return new string(' ', spaceCount >= 4 ? 4 : 2);
                }
            }
        }

        // Default to 2 spaces if no indentation detected
        return "  ";
    }
}
