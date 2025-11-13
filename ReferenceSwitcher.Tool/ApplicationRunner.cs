using System.Collections.Generic;
using System.IO;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher.Tool;

internal sealed class ApplicationRunner
{
    private readonly AppArguments arguments;
    private readonly TextWriter writer;

    public ApplicationRunner(AppArguments arguments, TextWriter writer)
    {
        this.arguments = arguments;
        this.writer = writer;
    }

    public Result Run()
    {
        return SolutionReader.Read(arguments.SolutionPath)
            .Bind(solutionProjects => ProjectIndex.Build(arguments.ScanDirectory)
                .Map(index => (solutionProjects, index)))
            .Bind(tuple => Execute(tuple.solutionProjects, tuple.index));
    }

    private Result Execute(IReadOnlyCollection<string> solutionProjects, ProjectIndex projectIndex)
    {
        return arguments.Mode switch
        {
            SwitchMode.PackageToProject => new PackageToProjectSwitcher(projectIndex, writer).Switch(solutionProjects),
            SwitchMode.ProjectToPackage => new ProjectToPackageSwitcher(projectIndex, writer).Switch(solutionProjects),
            _ => Result.Failure("Unsupported mode.")
        };
    }
}
