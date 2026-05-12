namespace ReferenceSwitcher.Tool.Tests;

public sealed class SolutionReaderTests
{
    [Fact]
    public void ReadsProjectsFromSlnx()
    {
        using var workspace = TestWorkspace.Create();
        var appProject = workspace.WriteProject("src/App/App.csproj");
        var libraryProject = workspace.WriteProject("libs/Library/Library.csproj");
        var solution = workspace.WriteFile("Sample.slnx", """
            <Solution>
              <Folder Name="/libs/">
                <Project Path="libs/Library/Library.csproj" />
              </Folder>
              <Folder Name="/src/">
                <Project Path="src/App/App.csproj" />
              </Folder>
            </Solution>
            """);

        var result = SolutionReader.Read(solution);

        ResultAssertions.Succeeded(result);
        Assert.Equal(new[] { libraryProject, appProject }, result.Value);
    }
}
