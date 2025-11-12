using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.IO;
using System.Text;
using System.Linq;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher;

internal static class ArgumentParser
{
    private const string ExecutableDisplayName = "ReferenceSwitcher";

    private static readonly Option<string> ModeOption = new("--mode", "-m")
    {
        Description = "Define el modo de ejecución.",
        HelpName = "modo",
    };

    private static readonly Option<string> SolutionOption = new("--solution", "-s")
    {
        Description = "Ruta al archivo .sln base.",
        HelpName = "ruta",
    };

    private static readonly Option<string> ScanDirectoryOption = new("--scan-directory", "-d")
    {
        Description = "Directorio donde se buscarán proyectos locales.",
        HelpName = "directorio",
    };

    private static readonly HelpOption HelpOption = new("--help", "-h")
    {
        Description = "Muestra esta ayuda.",
    };

    private static readonly RootCommand RootCommand = CreateRootCommand();

    public static Result<AppArguments> Parse(string[] args)
    {
        var parseResult = RootCommand.Parse(args);

        if (args.Length == 0)
            return Result.Failure<AppArguments>(BuildUsage("No se proporcionaron argumentos."));

        if (parseResult.GetResult(HelpOption) is not null)
            return Result.Failure<AppArguments>(BuildUsage(null));

        if (parseResult.UnmatchedTokens.Count > 0)
        {
            var unknown = parseResult.UnmatchedTokens[0];
            return Result.Failure<AppArguments>(BuildUsage($"Argumento desconocido: {unknown}"));
        }

        var modeResult = ReadRequiredOption(parseResult, ModeOption, "Debe especificar el modo de ejecución.", "Falta el valor para --mode.")
            .Bind(ParseMode);

        if (modeResult.IsFailure)
            return Result.Failure<AppArguments>(BuildUsage(modeResult.Error));

        var solutionResult = ReadRequiredOption(parseResult, SolutionOption, "Debe especificar la ruta de la solución.", "Falta el valor para --solution.");
        if (solutionResult.IsFailure)
            return Result.Failure<AppArguments>(BuildUsage(solutionResult.Error));

        var scanDirectoryResult = ReadRequiredOption(parseResult, ScanDirectoryOption, "Debe especificar el directorio de escaneo.", "Falta el valor para --scan-directory.");
        if (scanDirectoryResult.IsFailure)
            return Result.Failure<AppArguments>(BuildUsage(scanDirectoryResult.Error));

        var normalizedSolution = Path.GetFullPath(solutionResult.Value);
        var normalizedScanDirectory = Path.GetFullPath(scanDirectoryResult.Value);

        return Result.Success(new AppArguments(modeResult.Value, normalizedSolution, normalizedScanDirectory));
    }

    private static RootCommand CreateRootCommand()
    {
        var command = new RootCommand("Automatiza el cambio de referencias entre paquetes y proyectos.")
        {
            TreatUnmatchedTokensAsErrors = true,
        };

        command.Add(ModeOption);
        command.Add(SolutionOption);
        command.Add(ScanDirectoryOption);
        command.Add(HelpOption);

        return command;
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
                return Result.Failure<SwitchMode>($"Modo desconocido: {value}.");
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

        builder.AppendLine("Uso:");
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
        builder.AppendLine("Opciones:");

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
        var valueName = string.IsNullOrWhiteSpace(option.HelpName) ? "valor" : option.HelpName;

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
