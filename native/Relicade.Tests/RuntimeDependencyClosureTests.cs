using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Relicade.Tests;

public sealed class RuntimeDependencyClosureTests
{
    [Fact]
    public void Bundled_drawing_dependency_resolves_its_declared_Nrbf_types_without_loading_platform_code()
    {
        // Metadata only: no Windows API, image codec, audio device or installed payload runs.
        var root = AppContext.BaseDirectory;
        using var core = new PEReader(File.OpenRead(Path.Combine(root, "System.Private.Windows.Core.dll")));
        var metadata = core.GetMetadataReader();
        var reference = Assert.Single(metadata.AssemblyReferences.Select(metadata.GetAssemblyReference),
            row => metadata.GetString(row.Name) == "System.Formats.Nrbf");
        var requested = metadata.TypeReferences.Select(metadata.GetTypeReference)
            .Where(row => row.ResolutionScope.Kind == HandleKind.AssemblyReference &&
                metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)row.ResolutionScope).Name) == "System.Formats.Nrbf")
            .Select(row => metadata.GetString(row.Namespace) + "." + metadata.GetString(row.Name)).Distinct().ToArray();
        Assert.NotEmpty(requested);
        var dependency = Path.Combine(root, "System.Formats.Nrbf.dll");
        Assert.True(File.Exists(dependency), "The bundled Windows drawing assembly declares NRBF types, but System.Formats.Nrbf.dll is absent from runtime output.");
        var identity = AssemblyName.GetAssemblyName(dependency);
        Assert.Equal(metadata.GetString(reference.Name), identity.Name);
        Assert.Equal(reference.Version.Major, identity.Version!.Major);
        Assert.Equal(metadata.GetBlobBytes(reference.PublicKeyOrToken), identity.GetPublicKeyToken());
        using var candidate = new PEReader(File.OpenRead(dependency));
        var definitions = candidate.GetMetadataReader();
        var types = definitions.TypeDefinitions.Select(definitions.GetTypeDefinition)
            .Select(row => definitions.GetString(row.Namespace) + "." + definitions.GetString(row.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var type in requested) Assert.Contains(type, types);
    }
}
