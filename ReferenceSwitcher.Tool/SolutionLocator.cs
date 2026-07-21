using CSharpFunctionalExtensions;

namespace ReferenceSwitcher.Tool;

internal static class SolutionLocator
{
    public static Result<string> Resolve(string? providedSolutionPath, string directory)
    {
        if (!string.IsNullOrWhiteSpace(providedSolutionPath))
            return Result.Success(Path.GetFullPath(providedSolutionPath));

        var solutions = Directory.EnumerateFiles(directory)
            .Where(IsSolutionFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return solutions.Length switch
        {
            1 => Result.Success(Path.GetFullPath(solutions[0])),
            0 => Result.Failure<string>("Option '--solution' is required when the current directory does not contain a .sln or .slnx file."),
            _ => Result.Failure<string>("Option '--solution' is required when the current directory contains more than one .sln or .slnx file.")
        };
    }

    private static bool IsSolutionFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }
}
