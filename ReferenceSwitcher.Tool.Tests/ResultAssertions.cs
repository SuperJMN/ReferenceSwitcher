using CSharpFunctionalExtensions;

namespace ReferenceSwitcher.Tool.Tests;

internal static class ResultAssertions
{
    public static void Succeeded(Result result)
    {
        if (result.IsFailure)
            Assert.Fail(result.Error);
    }

    public static void Succeeded<T>(Result<T> result)
    {
        if (result.IsFailure)
            Assert.Fail(result.Error);
    }

    public static void Failed<T>(Result<T> result)
    {
        if (result.IsSuccess)
            Assert.Fail("Expected failure, but result succeeded.");
    }
}
