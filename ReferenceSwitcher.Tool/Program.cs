using System.CommandLine;

namespace ReferenceSwitcher.Tool;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Help requested or no args: print usage and exit 0
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            Console.Out.WriteLine(ArgumentParser.BuildUsage());
            return 0;
        }

        var parse = ArgumentParser.ParseToArguments(args);
        if (parse.IsFailure)
        {
            Console.Error.WriteLine(parse.Error);
            Console.Out.WriteLine();
            Console.Out.WriteLine(ArgumentParser.BuildUsage());
            return 2;
        }

        var result = new ApplicationRunner(parse.Value, Console.Out).Run();
        if (result.IsFailure)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        return 0;
    }
}
