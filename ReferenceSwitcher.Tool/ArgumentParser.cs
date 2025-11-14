using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher.Tool;

internal static class ArgumentParser
{
    // Primary aliases requested; keep backward-compatible aliases from origin/master
    private static readonly Option<string> SolutionOption = new("--solution", "-s")
    {
        Description = "Path to the base .sln file.",
        HelpName = "path",
    };

    private static readonly Option<string> ScanDirectoryOption = new("--scan-directory", "-d")
    {
        Description = "Directory to scan for local projects.",
        HelpName = "directory",
    };

    private static readonly Command ToProjectsCommand = new("to-projects", "Switch PackageReference items to local ProjectReference entries.");
    private static readonly Command ToPackagesCommand = new("to-packages", "Switch local ProjectReference items back to PackageReference entries.");

    private static readonly RootCommand Root = CreateRootCommand();

    private static RootCommand CreateRootCommand()
    {
        // Required arity (displayed as required in help and enforced in parsing)
        SolutionOption.Arity = ArgumentArity.ExactlyOne;
        ScanDirectoryOption.Arity = ArgumentArity.ExactlyOne;

        // Backward-compatible aliases from origin/master
        SolutionOption.AddAlias("--solution-file");
        ScanDirectoryOption.AddAlias("--projects-folder");

        ToProjectsCommand.Add(SolutionOption);
        ToProjectsCommand.Add(ScanDirectoryOption);

        ToPackagesCommand.Add(SolutionOption);
        ToPackagesCommand.Add(ScanDirectoryOption);

        var root = new RootCommand("Automates switching references between packages and projects.")
        {
            TreatUnmatchedTokensAsErrors = true,
        };

        root.Add(ToProjectsCommand);
        root.Add(ToPackagesCommand);
        return root;
    }

    public static RootCommand BuildRootCommand() => Root;

    public static string BuildUsage()
    {
        var builder = new StringBuilder();

        builder.AppendLine("Usage:");
        builder.AppendLine($"  ReferenceSwitcher to-projects {FormatUsage(SolutionOption)} {FormatUsage(ScanDirectoryOption)}");
        builder.AppendLine($"  ReferenceSwitcher to-packages {FormatUsage(SolutionOption)} {FormatUsage(ScanDirectoryOption)}");
        builder.AppendLine();
        builder.AppendLine(Root.Description);
        builder.AppendLine();

        builder.AppendLine("Subcommands:");
        builder.AppendLine("  to-projects    Switch PackageReference items to local ProjectReference entries.");
        builder.AppendLine("  to-packages    Switch local ProjectReference items back to PackageReference entries.");
        builder.AppendLine();

        builder.AppendLine("Options:");

        foreach (var option in new Option[] { SolutionOption, ScanDirectoryOption })
        {
            var aliases = string.Join(", ", option.Aliases.Select(FormatAlias));
            builder.Append("  ");
            builder.AppendLine(aliases);

            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                builder.Append("      ");
                builder.AppendLine(option.Description);
            }

            builder.AppendLine();
        }

        if (builder.Length > 0 && builder[^1] == '\n')
            builder.Length--;

        return builder.ToString();
    }

    public static Result<AppArguments> ParseToArguments(string[] args)
    {
        var parseResult = Root.Parse(args);

        if (parseResult.UnmatchedTokens.Count > 0)
        {
            var unknown = parseResult.UnmatchedTokens[0];
            return Result.Failure<AppArguments>($"Unknown argument: {unknown}");
        }

        var mode = ResolveModeFromArgs(args);
        if (mode.IsFailure)
            return mode.ConvertFailure<AppArguments>();

        var solutionResult = ReadRequiredOption(parseResult, SolutionOption, "You must specify the solution path.", "Missing value for --solution.");
        if (solutionResult.IsFailure)
            return Result.Failure<AppArguments>(solutionResult.Error);

        var scanDirectoryResult = ReadRequiredOption(parseResult, ScanDirectoryOption, "You must specify the scan directory.", "Missing value for --scan-directory.");
        if (scanDirectoryResult.IsFailure)
            return Result.Failure<AppArguments>(scanDirectoryResult.Error);

        var normalizedSolution = Path.GetFullPath(solutionResult.Value);
        var normalizedScanDirectory = Path.GetFullPath(scanDirectoryResult.Value);

        return Result.Success(new AppArguments(mode.Value, normalizedSolution, normalizedScanDirectory));
    }

    private static Result<SwitchMode> ResolveModeFromArgs(string[] args)
    {
        if (args.Any(a => string.Equals(a, "to-projects", StringComparison.OrdinalIgnoreCase)))
            return Result.Success(SwitchMode.PackageToProject);

        if (args.Any(a => string.Equals(a, "to-packages", StringComparison.OrdinalIgnoreCase)))
            return Result.Success(SwitchMode.ProjectToPackage);

        return Result.Failure<SwitchMode>("You must specify a subcommand: 'to-projects' or 'to-packages'.");
    }

    private static Result<string> ReadRequiredOption(ParseResult parseResult, Option<string> option, string missingOptionMessage, string missingValueMessage)
    {
        var optionResult = parseResult.GetResult(option);
        if (optionResult is null)
            return Result.Failure<string>(missingOptionMessage);

        if (optionResult.Tokens.Count == 0)
            return Result.Failure<string>(missingValueMessage);

        var value = parseResult.GetValue(option);
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<string>(missingValueMessage);

        return Result.Success(value);
    }

    private static string FormatUsage(Option option)
    {
        var alias = option.Aliases.FirstOrDefault() ?? option.Name;
        var usageAlias = FormatAlias(alias);
        var valueName = string.IsNullOrWhiteSpace(option.HelpName) ? "value" : option.HelpName;
        return $"{usageAlias} <{valueName}>";
    }

    private static string FormatAlias(string alias)
    {
        if (alias.StartsWith("-"))
            return alias;

        return alias.Length == 1 ? $"-{alias}" : $"--{alias}";
    }
}
