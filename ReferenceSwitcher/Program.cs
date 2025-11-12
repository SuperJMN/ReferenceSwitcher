using CSharpFunctionalExtensions;

namespace ReferenceSwitcher;

internal static class Program
{
    private static int Main(string[] args)
    {
        var result = ArgumentParser.Parse(args)
            .Bind(arguments => new ApplicationRunner(arguments, Console.Out).Run());

        if (result.IsFailure)
        {
            Console.Error.WriteLine(result.Error);
            return 1;
        }

        return 0;
    }
}
