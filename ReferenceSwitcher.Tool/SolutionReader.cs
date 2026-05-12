using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher.Tool;

internal static class SolutionReader
{
    private static readonly Regex ProjectLine = new(
        "^Project\\(\\\".*\\\"\\) = \\\"[^\\\"]+\\\", \\\"(?<path>[^\\\"]+\\.csproj)\\\"",
        RegexOptions.Compiled);

    public static Result<IReadOnlyCollection<string>> Read(string solutionPath)
    {
        if (!File.Exists(solutionPath))
            return Result.Failure<IReadOnlyCollection<string>>($"Solution '{solutionPath}' was not found.");

        if (SlnxSolutionFile.CanHandle(solutionPath))
            return SlnxSolutionFile.ReadProjects(solutionPath);

        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        var projectPaths = new List<string>();

        foreach (var line in File.ReadLines(solutionPath))
        {
            var match = ProjectLine.Match(line.Trim());
            if (!match.Success)
                continue;

            var relativePath = match.Groups["path"].Value;
            var normalizedRelativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar);
            var absolutePath = Path.GetFullPath(Path.Combine(solutionDirectory, normalizedRelativePath));

            if (!File.Exists(absolutePath))
                return Result.Failure<IReadOnlyCollection<string>>($"The project '{relativePath}' declared in the solution does not exist.");

            projectPaths.Add(absolutePath);
        }

        if (projectPaths.Count == 0)
            return Result.Failure<IReadOnlyCollection<string>>("The solution does not contain .csproj projects.");

        return Result.Success((IReadOnlyCollection<string>)projectPaths);
    }
}
