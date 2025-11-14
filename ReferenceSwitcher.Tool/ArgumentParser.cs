using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.IO;
using System.Text;
using System.Linq;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher.Tool;

internal static class ArgumentParser
{
    private const string ExecutableDisplayName = "ReferenceSwitcher";

    private static readonly Option<string> ModeOption = new("--switch-direction", "-m")
    {
        Description = "Choose how references should be converted.",
        HelpName = "direction",
    };

    private static readonly Option<bool> UseProjectReferencesOption = new("--switch-to-projects")
    {
        Description = "Convert NuGet package references into local project references.",
    };

    private static readonly Option<bool> UsePackageReferencesOption = new("--switch-to-packages")
    {
        Description = "Convert local project references back into NuGet package references.",
    };

    private static readonly Option<string> SolutionOption = new("--solution-file", "-s")
    {
        Description = "Path to the solution file that orchestrates the switch.",
        HelpName = "solution",
    };

    private static readonly Option<string> ScanDirectoryOption = new("--projects-folder", "-d")
    {
        Description = "Directory containing the projects that should be inspected.",
        HelpName = "folder",
    };

    private static readonly HelpOption HelpOption = new("--help", "-h")
    {
        Description = "Shows this help.",
    };

    private static readonly RootCommand RootCommand = CreateRootCommand();

    public static Result<AppArguments> Parse(string[] args)
    {
        var parseResult = RootCommand.Parse(args);

        if (args.Length == 0)
            return Result.Failure<AppArguments>(BuildUsage("No arguments were provided."));

        if (parseResult.GetResult(HelpOption) is not null)
            return Result.Failure<AppArguments>(BuildUsage(null));

        if (parseResult.UnmatchedTokens.Count > 0)
        {
            var unknown = parseResult.UnmatchedTokens[0];
            return Result.Failure<AppArguments>(BuildUsage($"Unknown argument: {unknown}"));
        }

        var modeResult = ResolveMode(parseResult);

        if (modeResult.IsFailure)
            return Result.Failure<AppArguments>(BuildUsage(modeResult.Error));

        var solutionResult = ReadRequiredOption(parseResult, SolutionOption, "You must specify the solution path.", "Missing value for --solution-file.");
        if (solutionResult.IsFailure)
            return Result.Failure<AppArguments>(BuildUsage(solutionResult.Error));

        var scanDirectoryResult = ReadRequiredOption(parseResult, ScanDirectoryOption, "You must specify the scan directory.", "Missing value for --projects-folder.");
        if (scanDirectoryResult.IsFailure)
            return Result.Failure<AppArguments>(BuildUsage(scanDirectoryResult.Error));

        var normalizedSolution = Path.GetFullPath(solutionResult.Value);
        var normalizedScanDirectory = Path.GetFullPath(scanDirectoryResult.Value);

        return Result.Success(new AppArguments(modeResult.Value, normalizedSolution, normalizedScanDirectory));
    }

    private static RootCommand CreateRootCommand()
    {
        var command = new RootCommand("Automates switching references between packages and projects.")
        {
            TreatUnmatchedTokensAsErrors = true,
        };

        command.Add(ModeOption);
        command.Add(UseProjectReferencesOption);
        command.Add(UsePackageReferencesOption);
        command.Add(SolutionOption);
        command.Add(ScanDirectoryOption);
        command.Add(HelpOption);

        return command;
    }

    private static Result<SwitchMode> ResolveMode(ParseResult parseResult)
    {
        var useProjects = parseResult.GetValue(UseProjectReferencesOption);
        var usePackages = parseResult.GetValue(UsePackageReferencesOption);

        if (useProjects && usePackages)
            return Result.Failure<SwitchMode>("Choose either --switch-to-projects or --switch-to-packages, not both.");

        var modeOptionResult = parseResult.GetResult(ModeOption);
        if (modeOptionResult is not null)
        {
            var rawModeResult = ReadRequiredOption(parseResult, ModeOption, "You must specify how references should be switched.", "Missing value for --switch-direction.");
            if (rawModeResult.IsFailure)
                return Result.Failure<SwitchMode>(rawModeResult.Error);

            var parsedModeResult = ParseMode(rawModeResult.Value);
            if (parsedModeResult.IsFailure)
                return parsedModeResult;

            if (useProjects && parsedModeResult.Value != SwitchMode.PackageToProject)
                return Result.Failure<SwitchMode>("Conflicting mode arguments were provided. Remove --switch-to-projects or adjust --switch-direction.");

            if (usePackages && parsedModeResult.Value != SwitchMode.ProjectToPackage)
                return Result.Failure<SwitchMode>("Conflicting mode arguments were provided. Remove --switch-to-packages or adjust --switch-direction.");

            return parsedModeResult;
        }

        if (useProjects)
            return Result.Success(SwitchMode.PackageToProject);

        if (usePackages)
            return Result.Success(SwitchMode.ProjectToPackage);

        return Result.Failure<SwitchMode>("You must specify how references should be switched.");
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

    private static Result<SwitchMode> ParseMode(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "package-to-project":
            case "packages-to-projects":
            case "package2project":
                return Result.Success(SwitchMode.PackageToProject);
            case "project-to-package":
            case "projects-to-packages":
            case "project2package":
                return Result.Success(SwitchMode.ProjectToPackage);
            default:
                return Result.Failure<SwitchMode>($"Unknown switch direction: {value}.");
        }
    }

    private static string BuildUsage(string? error)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(error))
        {
            builder.AppendLine(error);
            builder.AppendLine();
        }

        builder.AppendLine("Usage:");
        builder.Append("  ");
        builder.Append(ExecutableDisplayName);

        foreach (var option in RootCommand.Options)
        {
            builder.Append(' ');
            builder.Append(FormatUsage(option));
        }

        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine(RootCommand.Description);
        builder.AppendLine();
        builder.AppendLine("Options:");

        foreach (var option in RootCommand.Options)
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

    private static string FormatUsage(Option option)
    {
        var alias = option.Aliases.FirstOrDefault() ?? option.Name;
        var usageAlias = FormatAlias(alias);
        var valueName = string.IsNullOrWhiteSpace(option.HelpName) ? "value" : option.HelpName;

        return option.Arity.MaximumNumberOfValues switch
        {
            0 => usageAlias,
            _ => $"{usageAlias} <{valueName}>",
        };
    }

    private static string FormatAlias(string alias)
    {
        if (alias.StartsWith("-"))
            return alias;

        return alias.Length == 1 ? $"-{alias}" : $"--{alias}";
    }
}
