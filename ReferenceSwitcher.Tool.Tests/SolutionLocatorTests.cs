namespace ReferenceSwitcher.Tool.Tests;

public sealed class SolutionLocatorTests
{
    [Fact]
    public void UsesProvidedSolutionPath()
    {
        using var workspace = TestWorkspace.Create();
        var solution = workspace.WriteFile("App.sln", "Microsoft Visual Studio Solution File");

        var result = SolutionLocator.Resolve(solution, workspace.Root);

        ResultAssertions.Succeeded(result);
        Assert.Equal(Path.GetFullPath(solution), result.Value);
    }

    [Fact]
    public void FindsSingleSlnInDirectory()
    {
        using var workspace = TestWorkspace.Create();
        var solution = workspace.WriteFile("App.sln", "Microsoft Visual Studio Solution File");

        var result = SolutionLocator.Resolve(null, workspace.Root);

        ResultAssertions.Succeeded(result);
        Assert.Equal(Path.GetFullPath(solution), result.Value);
    }

    [Fact]
    public void FindsSingleSlnxInDirectory()
    {
        using var workspace = TestWorkspace.Create();
        var solution = workspace.WriteFile("App.slnx", "<Solution />");

        var result = SolutionLocator.Resolve(null, workspace.Root);

        ResultAssertions.Succeeded(result);
        Assert.Equal(Path.GetFullPath(solution), result.Value);
    }

    [Fact]
    public void FailsWhenNoSolutionExists()
    {
        using var workspace = TestWorkspace.Create();

        var result = SolutionLocator.Resolve(null, workspace.Root);

        ResultAssertions.Failed(result);
        Assert.Contains("--solution", result.Error);
    }

    [Fact]
    public void FailsWhenMultipleSolutionsExist()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("App.sln", "Microsoft Visual Studio Solution File");
        workspace.WriteFile("Other.slnx", "<Solution />");

        var result = SolutionLocator.Resolve(null, workspace.Root);

        ResultAssertions.Failed(result);
        Assert.Contains("--solution", result.Error);
        Assert.Contains("more than one", result.Error);
    }

    [Fact]
    public void PrefersProvidedPathEvenWhenDirectoryHasSolutions()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("App.sln", "Microsoft Visual Studio Solution File");
        var other = workspace.WriteFile("nested/Other.slnx", "<Solution />");

        var result = SolutionLocator.Resolve(other, workspace.Root);

        ResultAssertions.Succeeded(result);
        Assert.Equal(Path.GetFullPath(other), result.Value);
    }
}
