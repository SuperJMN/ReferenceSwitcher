using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher.Tool;

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
        var solutionProjects = new HashSet<string>(
            rootProjects.Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in rootProjects)
        {
            var result = ReplaceProjects(project, visited, solutionProjects);
            if (result.IsFailure)
                return result;
        }

        return Result.Success();
    }

    private Result ReplaceProjects(string projectPath, ISet<string> visited, ISet<string> solutionProjects)
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

        // Collect paths to recurse into BEFORE we modify the document (removing references)
        var projectPathsToVisit = document
            .Descendants(ns + "ProjectReference")
            .Select(x => x.Attribute("Include")?.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Path.GetFullPath(Path.Combine(projectDirectory, x!.Replace('\\', Path.DirectorySeparatorChar))))
            .ToList();

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

            var absoluteReference = Path.GetFullPath(Path.Combine(projectDirectory, include.Replace('\\', Path.DirectorySeparatorChar)));
            var metadataOption = projectIndex.FindByPath(absoluteReference);
            if (metadataOption.HasNoValue)
                continue;

            var metadata = metadataOption.Value;

            // Skip if referenced project is in the same repository (preserve internal references)
            var currentRoot = FindReferenceRoot(projectDirectory);
            var referencedDirectory = Path.GetDirectoryName(metadata.ProjectPath) ?? projectDirectory;
            var referencedRoot = FindReferenceRoot(referencedDirectory);
            if (string.Equals(currentRoot, referencedRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            var parentGroup = projectReference.Parent as XElement;

            var alreadyExists = document
                .Descendants(ns + "PackageReference")
                .Any(x => string.Equals(x.Attribute("Include")?.Value, metadata.PackageId, StringComparison.OrdinalIgnoreCase));

            if (solutionProjects.Contains(metadata.ProjectPath))
            {
                writer.WriteLine(
                    $"[{metadata.PackageId}] ProjectReference kept in '{normalizedPath}' because '{metadata.ProjectPath}' belongs to the provided solution.");
            }
            else
            {
                RemoveElementWithWhitespace(projectReference);
                changed = true;
                if (parentGroup is not null)
                    groupsToReview.Add(parentGroup);

                if (alreadyExists)
                {
                    writer.WriteLine(
                        $"[{metadata.PackageId}] PackageReference already exists in '{normalizedPath}'. Removed ProjectReference.");
                }
                else
                {
                    var targetGroup = parentGroup ?? FindOrCreatePackageGroup(document, ns);
                    var packageReference = new XElement(ns + "PackageReference", new XAttribute("Include", metadata.PackageId));
                    AddElementWithFormatting(targetGroup, packageReference);
                    writer.WriteLine(
                        $"[{metadata.PackageId}] Replaced ProjectReference with PackageReference in '{normalizedPath}'.");
                }
            }
        }

        // Recursively visit all ProjectReferences (identified before modification)
        foreach (var projectPathToVisit in projectPathsToVisit)
        {
            var recursionResult = ReplaceProjects(projectPathToVisit, visited, solutionProjects);
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

    private static string FindReferenceRoot(string directory)
    {
        var current = directory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var gitDirectory = Path.Combine(current, ".git");
            if (Directory.Exists(gitDirectory))
                return current;

            current = Path.GetDirectoryName(current);
        }

        // No repository found: use the parent directory.
        var parent = Path.GetDirectoryName(directory);
        return string.IsNullOrWhiteSpace(parent) ? directory : parent;
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
