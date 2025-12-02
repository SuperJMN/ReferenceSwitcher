using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher.Tool;

internal static class SolutionProjectAdder
{
    private const string CSharpProjectTypeGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";

    public static Result AddProjects(string solutionPath, IReadOnlyCollection<string> existingSolutionProjects, IReadOnlyCollection<string> discoveredProjects)
    {
        if (!File.Exists(solutionPath))
            return Result.Failure($"Solution '{solutionPath}' was not found.");

        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        var existingSet = new HashSet<string>(existingSolutionProjects.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        var candidates = discoveredProjects
            .Select(Path.GetFullPath)
            .Where(path => !existingSet.Contains(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
            return Result.Success();

        var lines = File.ReadAllLines(solutionPath).ToList();
        var globalIndex = FindGlobalSectionIndex(lines);
        if (globalIndex < 0)
            return Result.Failure($"Solution '{solutionPath}' does not contain a Global section.");

        var entries = candidates
            .Select(path => CreateProjectEntry(solutionDirectory, path))
            .ToList();

        InsertProjectEntries(lines, globalIndex, entries);
        InsertProjectConfigurationLines(lines, entries);

        File.WriteAllLines(solutionPath, lines);
        return Result.Success();
    }

    private static int FindGlobalSectionIndex(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith("Global", StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static SolutionProjectEntry CreateProjectEntry(string solutionDirectory, string absoluteProjectPath)
    {
        var name = Path.GetFileNameWithoutExtension(absoluteProjectPath);
        var relativePath = Path.GetRelativePath(solutionDirectory, absoluteProjectPath)
            .Replace(Path.DirectorySeparatorChar, '\\')
            .Replace(Path.AltDirectorySeparatorChar, '\\');
        var guid = Guid.NewGuid().ToString("B").ToUpperInvariant();

        return new SolutionProjectEntry(name, relativePath, guid);
    }

    private static void InsertProjectEntries(List<string> lines, int globalIndex, IReadOnlyCollection<SolutionProjectEntry> entries)
    {
        var insertIndex = globalIndex;

        foreach (var entry in entries)
        {
            lines.Insert(insertIndex++, $"Project(\"{CSharpProjectTypeGuid}\") = \"{entry.Name}\", \"{entry.RelativePath}\", \"{entry.Guid}\"");
            lines.Insert(insertIndex++, "EndProject");
        }
    }

    private static void InsertProjectConfigurationLines(List<string> lines, IReadOnlyCollection<SolutionProjectEntry> entries)
    {
        var configurations = ReadSolutionConfigurations(lines);
        if (configurations.Count == 0)
            return;

        var sectionBounds = FindProjectConfigurationSection(lines);
        if (sectionBounds is null)
            return;

        var (start, end) = sectionBounds.Value;
        var insertIndex = end;

        foreach (var entry in entries)
        {
            foreach (var configuration in configurations)
            {
                lines.Insert(insertIndex++, $"\t\t{entry.Guid}.{configuration}.ActiveCfg = {configuration}");
                lines.Insert(insertIndex++, $"\t\t{entry.Guid}.{configuration}.Build.0 = {configuration}");
            }
        }
    }

    private static List<string> ReadSolutionConfigurations(IReadOnlyList<string> lines)
    {
        var configurations = new List<string>();
        var start = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains("GlobalSection(SolutionConfigurationPlatforms)", StringComparison.Ordinal))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
            return configurations;

        var end = -1;
        for (var i = start + 1; i < lines.Count; i++)
        {
            if (lines[i].Contains("EndGlobalSection", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        if (end < 0)
            return configurations;

        for (var i = start + 1; i < end; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                continue;

            var configuration = parts[0].Trim();
            if (!string.IsNullOrWhiteSpace(configuration))
                configurations.Add(configuration);
        }

        return configurations;
    }

    private static (int start, int end)? FindProjectConfigurationSection(IReadOnlyList<string> lines)
    {
        var start = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains("GlobalSection(ProjectConfigurationPlatforms)", StringComparison.Ordinal))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
            return null;

        for (var i = start + 1; i < lines.Count; i++)
        {
            if (lines[i].Contains("EndGlobalSection", StringComparison.Ordinal))
                return (start, i);
        }

        return null;
    }

    private sealed record SolutionProjectEntry(string Name, string RelativePath, string Guid);
}
