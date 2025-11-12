using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher;

internal static class SolutionReader
{
    private static readonly Regex ProjectLine = new(
        "^Project\\(\\\".*\\\"\\) = \\\"[^\\\"]+\\\", \\\"(?<path>[^\\\"]+\\.csproj)\\\"",
        RegexOptions.Compiled);

    public static Result<IReadOnlyCollection<string>> Read(string solutionPath)
    {
        if (!File.Exists(solutionPath))
            return Result.Failure<IReadOnlyCollection<string>>($"No se encontró la solución '{solutionPath}'.");

        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        var projectPaths = new List<string>();

        foreach (var line in File.ReadLines(solutionPath))
        {
            var match = ProjectLine.Match(line.Trim());
            if (!match.Success)
                continue;

            var relativePath = match.Groups["path"].Value;
            var absolutePath = Path.GetFullPath(Path.Combine(solutionDirectory, relativePath));

            if (!File.Exists(absolutePath))
                return Result.Failure<IReadOnlyCollection<string>>($"El proyecto '{relativePath}' declarado en la solución no existe.");

            projectPaths.Add(absolutePath);
        }

        if (projectPaths.Count == 0)
            return Result.Failure<IReadOnlyCollection<string>>("La solución no contiene proyectos .csproj.");

        return Result.Success((IReadOnlyCollection<string>)projectPaths);
    }
}
