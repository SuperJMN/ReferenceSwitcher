using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.IO;
using System.Text;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher;

internal static class ArgumentParser
{
    private static readonly Option<string> ModeOption = new("--mode", "-m")
    {
        Description = "Define el modo de ejecución.",
    };

    private static readonly Option<string> SolutionOption = new("--solution", "-s")
    {
        Description = "Ruta al archivo .sln base.",
    };

    private static readonly Option<string> ScanDirectoryOption = new("--scan-directory", "-d")
    {
        Description = "Directorio donde se buscarán proyectos locales.",
    };

    private static readonly HelpOption HelpOption = new("--help", "-h")
    {
        Description = "Muestra esta ayuda.",
    };

    private static readonly RootCommand RootCommand = CreateRootCommand();
    private static readonly HelpBuilder HelpBuilder = new(100);

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

        using var writer = new StringWriter();
        var context = new HelpContext(HelpBuilder, RootCommand, writer);
        HelpBuilder.Write(context);
        builder.Append(writer.ToString());

        return builder.ToString();
    }
}
