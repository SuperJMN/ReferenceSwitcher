using System.CommandLine;

namespace ReferenceSwitcher.Tool;

internal static class Program
{
    private static int Main(string[] args)
    {
        var rootCommand = ArgumentParser.BuildRootCommand();
        var parseResult = rootCommand.Parse(args);
        var invocationConfiguration = new InvocationConfiguration
        {
            Output = Console.Out,
            Error = Console.Error
        };

        return parseResult.Invoke(invocationConfiguration);
    }
}
