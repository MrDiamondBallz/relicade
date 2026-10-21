namespace Relicade.Tests;

public class RendererProcessBoundaryTests
{
    [Fact]
    public void Real_renderer_runs_without_the_dummy_test_assembly()
    {
        var assembly = typeof(RealRenderTestApp).Assembly;
        Assert.Equal("Relicade.RenderTests", assembly.GetName().Name);
        Assert.Null(assembly.GetType("Relicade.Tests.HeadlessTestApp"));
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name == "Relicade.Tests");
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "Relicade.Tests");
        Assert.Contains(assembly.GetReferencedAssemblies(), a => a.Name == "Avalonia.Skia");
    }
}
