namespace ReferenceSwitcher.Tool.Tests;

public sealed class SolutionProjectAdderTests
{
    [Fact]
    public void AddsMissingProjectsToSlnx()
    {
        using var workspace = TestWorkspace.Create();
        var appProject = workspace.WriteProject("src/App/App.csproj");
        var libraryProject = workspace.WriteProject("libs/Library/Library.csproj");
        var solution = workspace.WriteFile("Sample.slnx", """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/App/App.csproj" />
              </Folder>
            </Solution>
            """);

        var result = SolutionProjectAdder.AddProjects(solution, [appProject], [appProject, libraryProject]);

        ResultAssertions.Succeeded(result);
        var content = File.ReadAllText(solution);
        Assert.Contains("""<Project Path="libs/Library/Library.csproj" />""", content);
    }
}
