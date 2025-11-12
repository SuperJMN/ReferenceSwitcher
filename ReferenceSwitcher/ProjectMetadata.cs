using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CSharpFunctionalExtensions;

namespace ReferenceSwitcher;

internal sealed record ProjectMetadata(string PackageId, string ProjectPath, string ProjectName)
{
    public static Result<ProjectMetadata> Create(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;

            var packageId = ReadElementValue(document, ns, "PackageId")
                .Match(value => value, () => ReadElementValue(document, ns, "AssemblyName")
                    .Match(v => v, () => Path.GetFileNameWithoutExtension(projectPath)));

            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var normalizedPath = Path.GetFullPath(projectPath);

            return Result.Success(new ProjectMetadata(packageId, normalizedPath, projectName));
        }
        catch (Exception exception)
        {
            return Result.Failure<ProjectMetadata>($"No se pudo analizar el proyecto '{projectPath}': {exception.Message}");
        }
    }

    private static Maybe<string> ReadElementValue(XDocument document, XNamespace ns, string elementName)
    {
        var element = document
            .Descendants(ns + elementName)
            .Select(x => x.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return string.IsNullOrWhiteSpace(element) ? Maybe<string>.None : Maybe.From(element.Trim());
    }
}
