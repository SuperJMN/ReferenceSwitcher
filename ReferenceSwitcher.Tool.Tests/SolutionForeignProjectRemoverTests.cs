namespace ReferenceSwitcher.Tool.Tests;

public sealed class SolutionForeignProjectRemoverTests
{
    [Fact]
    public void RemovesForeignProjectsFromSlnx()
    {
        using var workspace = TestWorkspace.Create();
        workspace.CreateDirectory(".git");
        workspace.WriteProject("src/App/App.csproj");

        var foreignProject = Path.Combine(workspace.Root, "..", "External", "Library", "Library.csproj");
        TestWorkspace.WriteProjectAt(foreignProject);

        var solution = workspace.WriteFile("Sample.slnx", """
            <Solution>
              <Folder Name="/external/">
                <Project Path="../External/Library/Library.csproj" />
              </Folder>
              <Folder Name="/src/">
                <Project Path="src/App/App.csproj" />
              </Folder>
            </Solution>
            """);

        var result = SolutionForeignProjectRemover.RemoveForeignProjects(solution);

        ResultAssertions.Succeeded(result);
        var content = File.ReadAllText(solution);
        Assert.DoesNotContain("../External/Library/Library.csproj", content);
        Assert.DoesNotContain("""<Folder Name="/external/">""", content);
        Assert.Contains("""<Project Path="src/App/App.csproj" />""", content);
    }
}
