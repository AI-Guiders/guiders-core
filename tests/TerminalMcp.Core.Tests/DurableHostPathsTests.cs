using TerminalMcp.Core;
using Xunit;

namespace TerminalMcp.Core.Tests;

public sealed class DurableHostPathsTests
{
    [Fact]
    public void Binary_names_rid_aware()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("CdpMcp.exe", DurableHostPaths.CdpMcpBinaryName);
            Assert.Equal("TerminalMcp.Supervisor.exe", DurableHostPaths.SupervisorBinaryName);
        }
        else
        {
            Assert.Equal("CdpMcp", DurableHostPaths.CdpMcpBinaryName);
            Assert.Equal("TerminalMcp.Supervisor", DurableHostPaths.SupervisorBinaryName);
        }
    }

    [Fact]
    public void ResolveCdpMcpExe_prefers_worker_hint()
    {
        var dir = Path.Combine(Path.GetTempPath(), "durable-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var bin = Path.Combine(dir, DurableHostPaths.CdpMcpBinaryName);
        File.WriteAllText(bin, OperatingSystem.IsWindows() ? "" : "#!/bin/sh\n");

        try
        {
            var hit = DurableHostPaths.ResolveCdpMcpExe(bin);
            Assert.Equal(Path.GetFullPath(bin), hit);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void EnumerateAiguidersRoots_non_empty()
    {
        Assert.NotEmpty(DurableHostPaths.EnumerateAiguidersRoots().ToList());
    }

    [Theory]
    [InlineData(@"D:\cdp-mcp-debug\CdpMcp.exe", "cdp-debug")]
    [InlineData(@"D:\cdp-mcp\CdpMcp.exe", "cdp")]
    [InlineData(@"C:\Users\dev\AppData\Local\AIGuiders\cdp\CdpMcp.exe", "cdp")]
    [InlineData(@"C:\Users\dev\AppData\Local\AIGuiders\cdp-debug\CdpMcp.exe", "cdp-debug")]
    [InlineData("/opt/cdp-mcp-debug/CdpMcp", "cdp-debug")]
    [InlineData("/opt/cdp-mcp/CdpMcp", "cdp")]
    [InlineData("/home/user/.local/share/AIGuiders/cdp/CdpMcp", "cdp")]
    [InlineData("/home/user/.local/share/AIGuiders/cdp-debug/CdpMcp", "cdp-debug")]
    [InlineData("/Users/dev/Library/Application Support/AIGuiders/cdp/CdpMcp", "cdp")]
    [InlineData("/Users/dev/Library/Application Support/AIGuiders/cdp-debug/CdpMcp", "cdp-debug")]
    [InlineData("/srv/custom/cdp-mcp/CdpMcp", "cdp")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("/usr/local/bin/CdpMcp", null)]
    public void DeriveIgniteSeat_maps_install_roots(string? worker, string? seat) =>
        Assert.Equal(seat, DurableHostPaths.DeriveIgniteSeat(worker));

    [Fact]
    public void DeriveIgniteSeat_prefers_nearest_install_folder_to_binary()
    {
        var nested = "/opt/stacks/cdp-mcp-debug/nested/CdpMcp";
        Assert.Equal("cdp-debug", DurableHostPaths.DeriveIgniteSeat(nested));
    }
}
