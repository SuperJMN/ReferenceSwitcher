using System.CommandLine;

namespace ReferenceSwitcher.Tool;

internal static class ArgumentParser
{
    public static RootCommand BuildRootCommand()
    {
        var solutionOption = new Option<FileInfo>("--solution", ["-s"])
        {
            Description = "Path to the base .sln file.",
            Required = true
        };

        var scanDirectoryOption = new Option<DirectoryInfo>("--scan-directory", ["-d"])
        {
            Description = "Directory to scan for local projects.",
            Required = true
        };

        var rootCommand = new RootCommand("Automates switching references between packages and projects.")
        {
            BuildCommand("to-projects", "Switch PackageReference items to local ProjectReference entries.", SwitchMode.PackageToProject),
            BuildCommand("to-packages", "Switch local ProjectReference items back to PackageReference entries.", SwitchMode.ProjectToPackage)
        };

        return rootCommand;

        Command BuildCommand(string name, string description, SwitchMode mode)
        {
            var command = new Command(name, description)
            {
                solutionOption,
                scanDirectoryOption
            };

            command.SetAction(parseResult =>
            {
                var solutionFile = parseResult.GetValue(solutionOption);
                var scanDirectory = parseResult.GetValue(scanDirectoryOption);

                ArgumentNullException.ThrowIfNull(solutionFile);
                ArgumentNullException.ThrowIfNull(scanDirectory);

                RunSwitch(mode, solutionFile, scanDirectory);
            });

            return command;
        }
    }

    private static void RunSwitch(SwitchMode mode, FileInfo solutionFile, DirectoryInfo scanDirectory)
    {
        var arguments = new AppArguments(mode, solutionFile.FullName, scanDirectory.FullName);
        var result = new ApplicationRunner(arguments, Console.Out).Run();

        if (result.IsFailure)
        {
            Console.Error.WriteLine(result.Error);
            Environment.ExitCode = 1;
        }
    }
}
