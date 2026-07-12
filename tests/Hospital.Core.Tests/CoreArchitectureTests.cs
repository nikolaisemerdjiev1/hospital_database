using System.Xml.Linq;

using Hospital.Core;

namespace Hospital.Core.Tests;

public sealed class CoreArchitectureTests
{
    [Theory]
    [InlineData("Hospital.Api")]
    [InlineData("Hospital.Infrastructure")]
    public void CoreDoesNotReferenceOuterProjects(string forbiddenAssembly)
    {
        string[] referencedAssemblies = typeof(CoreAssemblyReference)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(forbiddenAssembly, referencedAssemblies);
    }

    [Fact]
    public void CoreProjectDoesNotDeclareOuterProjectOrProviderReferences()
    {
        string repositoryRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(
            repositoryRoot,
            "backend",
            "Hospital.Core",
            "Hospital.Core.csproj");
        XDocument project = XDocument.Load(projectPath);

        string[] projectReferences = project
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
        string[] packageReferences = project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            projectReferences,
            reference =>
                reference.Contains("Hospital.Api", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("Hospital.Infrastructure", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            packageReferences,
            reference =>
                reference.StartsWith("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                reference.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Hospital.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
