namespace ReferenceSwitcher.Tool.Tests;

internal sealed class TestWorkspace : IDisposable
{
    private TestWorkspace(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public static TestWorkspace Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "reference-switcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new TestWorkspace(root);
    }

    public void CreateDirectory(string relativePath)
    {
        Directory.CreateDirectory(Path.Combine(Root, relativePath));
    }

    public string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        return path;
    }

    public string WriteProject(string relativePath)
    {
        var path = Path.Combine(Root, relativePath);
        WriteProjectAt(path);
        return Path.GetFullPath(path);
    }

    public static void WriteProjectAt(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """.ReplaceLineEndings("\n"));
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, true);
    }
}
