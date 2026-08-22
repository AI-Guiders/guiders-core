using System.Diagnostics;
using DotNetBuildTest.Core;

namespace DotNetBuildTest.Core.Tests;

public sealed class DotnetProcessIoEncodingTests
{
    [Fact]
    public void ApplyUtf8_sets_stdout_and_stderr_to_utf8_without_bom()
    {
        var psi = new ProcessStartInfo("dotnet");
        DotnetProcessIoEncoding.ApplyUtf8(psi);

        Assert.Same(DotnetProcessIoEncoding.Utf8NoBom, psi.StandardOutputEncoding);
        Assert.Same(DotnetProcessIoEncoding.Utf8NoBom, psi.StandardErrorEncoding);
        Assert.Empty(psi.StandardOutputEncoding!.GetPreamble());
    }
}
