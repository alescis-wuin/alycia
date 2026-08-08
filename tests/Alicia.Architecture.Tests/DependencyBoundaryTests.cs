using System.Reflection;
using Alicia.Application;
using Alicia.Desktop;
using Alicia.Domain;
using Alicia.Infrastructure;
using Alicia.Presentation;

namespace Alicia.Architecture.Tests;

public sealed class DependencyBoundaryTests
{
    [Fact]
    public void DomainHasNoInternalProjectDependencies()
    {
        Assert.Empty(InternalReferences(typeof(DomainAssembly).Assembly));
    }

    [Fact]
    public void ApplicationHasNoOutwardLayerDependency()
    {
        string[] references = InternalReferences(typeof(ApplicationAssembly).Assembly);

        Assert.DoesNotContain("Alicia.Infrastructure", references);
        Assert.DoesNotContain("Alicia.Presentation", references);
        Assert.DoesNotContain("Alicia.Desktop", references);
    }

    [Fact]
    public void InfrastructureDoesNotReferencePresentationOrHost()
    {
        string[] references = InternalReferences(typeof(InfrastructureAssembly).Assembly);

        Assert.DoesNotContain("Alicia.Presentation", references);
        Assert.DoesNotContain("Alicia.Desktop", references);
    }

    [Fact]
    public void PresentationDoesNotReferenceInfrastructureOrHost()
    {
        string[] references = InternalReferences(typeof(App).Assembly);

        Assert.DoesNotContain("Alicia.Infrastructure", references);
        Assert.DoesNotContain("Alicia.Desktop", references);
    }

    [Fact]
    public void DesktopDoesNotBypassPresentation()
    {
        string[] references = InternalReferences(typeof(DesktopAssembly).Assembly);

        Assert.DoesNotContain("Alicia.Application", references);
        Assert.DoesNotContain("Alicia.Domain", references);
        Assert.DoesNotContain("Alicia.Infrastructure", references);
    }

    private static string[] InternalReferences(Assembly assembly)
    {
        return assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("Alicia.", StringComparison.Ordinal))
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
