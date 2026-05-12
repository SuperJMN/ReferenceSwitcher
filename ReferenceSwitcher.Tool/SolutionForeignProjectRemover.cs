using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher.Tool;

internal static class SolutionForeignProjectRemover
{
    private static readonly Regex ProjectLine = new(
        "^Project\\(\"(?<typeGuid>[^\"]+)\"\\) = \"(?<name>[^\"]+)\", \"(?<path>[^\"]+\\.csproj)\", \"(?<projectGuid>{[^}]+})\"",
        RegexOptions.Compiled);

    private sealed record SolutionProjectBlock(string RelativePath, string ProjectGuid, int StartIndex, int EndIndex);

    public static Result RemoveForeignProjects(string solutionPath)
    {
        if (!File.Exists(solutionPath))
            return Result.Failure($"Solution '{solutionPath}' was not found.");

        if (SlnxSolutionFile.CanHandle(solutionPath))
            return SlnxSolutionFile.RemoveForeignProjects(solutionPath);

        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        var referenceRoot = FindReferenceRoot(solutionDirectory);
        var normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(referenceRoot));

        var lines = File.ReadAllLines(solutionPath).ToList();
        var projectBlocks = ReadProjectBlocks(lines);

        if (projectBlocks.Count == 0)
            return Result.Success();

        var foreignGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blocksToRemove = new List<SolutionProjectBlock>();

        foreach (var block in projectBlocks)
        {
            var normalizedRelative = block.RelativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            var absolutePath = Path.GetFullPath(Path.Combine(solutionDirectory, normalizedRelative));
            if (!IsUnderRoot(normalizedRoot, absolutePath))
            {
                foreignGuids.Add(block.ProjectGuid);
                blocksToRemove.Add(block);
            }
        }

        if (foreignGuids.Count == 0)
            return Result.Success();

        // Remove project blocks from bottom to top to keep indices valid.
        foreach (var block in blocksToRemove
                     .OrderByDescending(b => b.StartIndex))
        {
            var count = block.EndIndex - block.StartIndex + 1;
            lines.RemoveRange(block.StartIndex, count);
        }

        // Remove any configuration or nested lines that reference the removed project GUIDs.
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (IsLineReferencingAnyGuid(line, foreignGuids))
                lines.RemoveAt(i);
        }

        File.WriteAllLines(solutionPath, lines);
        return Result.Success();
    }

    private static List<SolutionProjectBlock> ReadProjectBlocks(IReadOnlyList<string> lines)
    {
        var blocks = new List<SolutionProjectBlock>();

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("Project(", StringComparison.Ordinal))
                continue;

            var match = ProjectLine.Match(trimmed);
            if (!match.Success)
                continue;

            var relativePath = match.Groups["path"].Value;
            var projectGuid = match.Groups["projectGuid"].Value;

            var startIndex = i;
            var endIndex = i;
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (lines[j].TrimStart().StartsWith("EndProject", StringComparison.Ordinal))
                {
                    endIndex = j;
                    break;
                }
            }

            blocks.Add(new SolutionProjectBlock(relativePath, projectGuid, startIndex, endIndex));

            i = endIndex;
        }

        return blocks;
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

        // No repository found: use the parent directory of the solution.
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

    private static bool IsLineReferencingAnyGuid(string line, ISet<string> guids)
    {
        foreach (var guid in guids)
        {
            if (line.Contains(guid, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
