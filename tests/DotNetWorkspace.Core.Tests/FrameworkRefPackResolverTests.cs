using DotNetWorkspace.Core;

namespace DotNetWorkspace.Core.Tests;

public sealed class FrameworkRefPackResolverTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net8.0")]
    public void ResolveRefAssemblies_includes_system_runtime(string targetFramework)
    {
        var refs = FrameworkRefPackResolver.ResolveRefAssemblies(targetFramework);
        Assert.Contains(
            refs,
            r => r.EndsWith("System.Runtime.dll", StringComparison.OrdinalIgnoreCase));
    }
}
