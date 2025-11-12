using System.IO;
using System.Text;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher;

internal static class ArgumentParser
{
    public static Result<AppArguments> Parse(string[] args)
    {
        if (args.Length == 0)
            return Result.Failure<AppArguments>(BuildUsage("No se proporcionaron argumentos."));

        SwitchMode? mode = null;
        string? solution = null;
        string? scanDirectory = null;

        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];
            switch (current)
            {
                case "--mode":
                case "-m":
                    if (!TryReadNext(args, ref index, out var modeValue))
                        return Result.Failure<AppArguments>(BuildUsage("Falta el valor para --mode."));

                    var modeResult = ParseMode(modeValue);
                    if (modeResult.IsFailure)
                        return Result.Failure<AppArguments>(BuildUsage(modeResult.Error));

                    mode = modeResult.Value;
                    break;

                case "--solution":
                case "-s":
                    if (!TryReadNext(args, ref index, out var solutionValue))
                        return Result.Failure<AppArguments>(BuildUsage("Falta el valor para --solution."));

                    solution = solutionValue;
                    break;

                case "--scan-directory":
                case "-d":
                    if (!TryReadNext(args, ref index, out var directoryValue))
                        return Result.Failure<AppArguments>(BuildUsage("Falta el valor para --scan-directory."));

                    scanDirectory = directoryValue;
                    break;

                case "--help":
                case "-h":
                    return Result.Failure<AppArguments>(BuildUsage(null));

                default:
                    return Result.Failure<AppArguments>(BuildUsage($"Argumento desconocido: {current}"));
            }
        }

        if (mode is null)
            return Result.Failure<AppArguments>(BuildUsage("Debe especificar el modo de ejecución."));

        if (string.IsNullOrWhiteSpace(solution))
            return Result.Failure<AppArguments>(BuildUsage("Debe especificar la ruta de la solución."));

        if (string.IsNullOrWhiteSpace(scanDirectory))
            return Result.Failure<AppArguments>(BuildUsage("Debe especificar el directorio de escaneo."));

        var normalizedSolution = Path.GetFullPath(solution);
        var normalizedScanDirectory = Path.GetFullPath(scanDirectory);

        return Result.Success(new AppArguments(mode.Value, normalizedSolution, normalizedScanDirectory));
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

    private static bool TryReadNext(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        index++;
        value = args[index];
        return true;
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
        builder.AppendLine("  reference-switcher --mode <package-to-project|project-to-package> --solution <ruta> --scan-directory <ruta>");
        builder.AppendLine();
        builder.AppendLine("Opciones:");
        builder.AppendLine("  --mode, -m            Define el modo de ejecución.");
        builder.AppendLine("  --solution, -s        Ruta al archivo .sln base.");
        builder.AppendLine("  --scan-directory, -d  Directorio donde se buscarán proyectos locales.");
        builder.AppendLine("  --help, -h            Muestra esta ayuda.");
        return builder.ToString();
    }
}
