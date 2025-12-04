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
        switch (arguments.Mode)
        {
            case SwitchMode.PackageToProject:
            {
                var switcher = new PackageToProjectSwitcher(projectIndex, writer);
                var result = switcher.Switch(solutionProjects);
                if (result.IsFailure)
                    return result;

                if (!arguments.UpdateSolution)
                    return Result.Success();

                return SolutionProjectAdder.AddProjects(arguments.SolutionPath, solutionProjects, switcher.DiscoveredProjects);
            }
            case SwitchMode.ProjectToPackage:
            {
                var switcher = new ProjectToPackageSwitcher(projectIndex, writer);
                var result = switcher.Switch(solutionProjects);
                if (result.IsFailure)
                    return result;

                if (!arguments.UpdateSolution)
                    return Result.Success();

                return SolutionForeignProjectRemover.RemoveForeignProjects(arguments.SolutionPath);
            }
            default:
                return Result.Failure("Unsupported mode.");
        }
    }
}
