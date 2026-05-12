using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher.Tool;

internal static class SlnxSolutionFile
{
    public static bool CanHandle(string solutionPath)
    {
        return string.Equals(Path.GetExtension(solutionPath), ".slnx", StringComparison.OrdinalIgnoreCase);
    }

    public static Result<IReadOnlyCollection<string>> ReadProjects(string solutionPath)
    {
        var documentResult = Load(solutionPath);
        if (documentResult.IsFailure)
            return documentResult.ConvertFailure<IReadOnlyCollection<string>>();

        var solutionDirectory = GetSolutionDirectory(solutionPath);
        var projectPaths = new List<string>();

        foreach (var relativePath in ReadRelativeProjectPaths(documentResult.Value))
        {
            var absolutePath = ResolveProjectPath(solutionDirectory, relativePath);
            if (!File.Exists(absolutePath))
                return Result.Failure<IReadOnlyCollection<string>>($"The project '{relativePath}' declared in the solution does not exist.");

            projectPaths.Add(absolutePath);
        }

        if (projectPaths.Count == 0)
            return Result.Failure<IReadOnlyCollection<string>>("The solution does not contain .csproj projects.");

        return Result.Success((IReadOnlyCollection<string>)projectPaths);
    }

    public static Result AddProjects(string solutionPath, IReadOnlyCollection<string> existingSolutionProjects, IReadOnlyCollection<string> discoveredProjects)
    {
        var existingSet = new HashSet<string>(existingSolutionProjects.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        var candidates = discoveredProjects
            .Select(Path.GetFullPath)
            .Where(path => !existingSet.Contains(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
            return Result.Success();

        var documentResult = Load(solutionPath);
        if (documentResult.IsFailure)
            return documentResult.ConvertFailure();

        var root = documentResult.Value.Root;
        if (root is null || !IsSolution(root))
            return Result.Failure($"Solution '{solutionPath}' is not a valid .slnx file.");

        var solutionDirectory = GetSolutionDirectory(solutionPath);
        foreach (var candidate in candidates)
        {
            var relativePath = ToSlnxPath(Path.GetRelativePath(solutionDirectory, candidate));
            root.Add(new XElement("Project", new XAttribute("Path", relativePath)));
        }

        Save(documentResult.Value, solutionPath);
        return Result.Success();
    }

    public static Result RemoveForeignProjects(string solutionPath)
    {
        var documentResult = Load(solutionPath);
        if (documentResult.IsFailure)
            return documentResult.ConvertFailure();

        var document = documentResult.Value;
        var solutionDirectory = GetSolutionDirectory(solutionPath);
        var referenceRoot = FindReferenceRoot(solutionDirectory);
        var normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(referenceRoot));

        var projectsToRemove = document
            .Descendants()
            .Where(IsProject)
            .Where(project => IsForeignProject(project, solutionDirectory, normalizedRoot))
            .ToList();

        if (projectsToRemove.Count == 0)
            return Result.Success();

        foreach (var project in projectsToRemove)
        {
            project.Remove();
        }

        RemoveEmptyFolders(document);
        Save(document, solutionPath);
        return Result.Success();
    }

    private static Result<XDocument> Load(string solutionPath)
    {
        try
        {
            return Result.Success(XDocument.Load(solutionPath));
        }
        catch (Exception exception)
        {
            return Result.Failure<XDocument>($"Failed to read solution '{solutionPath}': {exception.Message}");
        }
    }

    private static IEnumerable<string> ReadRelativeProjectPaths(XDocument document)
    {
        return document
            .Descendants()
            .Where(IsProject)
            .Select(project => project.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsForeignProject(XElement project, string solutionDirectory, string normalizedRoot)
    {
        var path = project.Attribute("Path")?.Value;
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return false;

        var absolutePath = ResolveProjectPath(solutionDirectory, path);
        return !IsUnderRoot(normalizedRoot, absolutePath);
    }

    private static string ResolveProjectPath(string solutionDirectory, string relativePath)
    {
        var nativePath = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(solutionDirectory, nativePath));
    }

    private static string ToSlnxPath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string GetSolutionDirectory(string solutionPath)
    {
        return Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
    }

    private static bool IsSolution(XElement element)
    {
        return element.Name.LocalName.Equals("Solution", StringComparison.Ordinal);
    }

    private static bool IsProject(XElement element)
    {
        return element.Name.LocalName.Equals("Project", StringComparison.Ordinal);
    }

    private static bool IsFolder(XElement element)
    {
        return element.Name.LocalName.Equals("Folder", StringComparison.Ordinal);
    }

    private static void RemoveEmptyFolders(XDocument document)
    {
        var emptyFolders = document
            .Descendants()
            .Where(IsFolder)
            .Reverse()
            .Where(folder => !folder.Elements().Any())
            .ToList();

        foreach (var folder in emptyFolders)
        {
            folder.Remove();
        }
    }

    private static string FindReferenceRoot(string solutionDirectory)
    {
        var directory = solutionDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var gitDirectory = Path.Combine(directory, ".git");
            if (Directory.Exists(gitDirectory))
                return directory;

            directory = Path.GetDirectoryName(directory);
        }

        var parent = Path.GetDirectoryName(solutionDirectory);
        return string.IsNullOrWhiteSpace(parent) ? solutionDirectory : parent;
    }

    private static bool IsUnderRoot(string rootWithSeparator, string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        if (!rootWithSeparator.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            rootWithSeparator += Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
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

    private static void Save(XDocument document, string path)
    {
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace
        };

        using var writer = XmlWriter.Create(path, settings);
        document.Save(writer);
    }
}
