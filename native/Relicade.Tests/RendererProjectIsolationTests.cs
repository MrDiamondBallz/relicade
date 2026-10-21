namespace Relicade.Tests;

public class RendererProjectIsolationTests
{
    [Fact]
    public void Dummy_test_assembly_cannot_discover_real_renderer_fixtures()
    {
        var assembly = typeof(HeadlessTestApp).Assembly;
        Assert.Equal("Relicade.Tests", assembly.GetName().Name);
        Assert.Null(assembly.GetType("Relicade.Tests.RealRenderSmokeTests"));
        Assert.Null(assembly.GetType("Relicade.Tests.RealRenderTestApp"));
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name == "Avalonia.Skia");
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name == "Relicade.RenderTests");
    }
}
