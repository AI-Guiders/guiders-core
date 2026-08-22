using Cdp.Ignite.Client;
using Xunit;
namespace Cdp.Ignite.Client.Tests;

public sealed class IgniteArmStoreTests : IDisposable
{
    readonly string _root;

    public IgniteArmStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ignite-client-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        IgniteArmStore.RootOverrideForTests = _root;
        IgniteArmStore.SeatOverrideForTests = "terminal-test";
        IgniteWakeLatch.RootOverrideForTests = _root;
        IgniteArmStore.SuppressForTests = false;
    }

    public void Dispose()
    {
        IgniteArmStore.RootOverrideForTests = null;
        IgniteArmStore.SeatOverrideForTests = null;
        IgniteWakeLatch.RootOverrideForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void TryArm_and_Notify_publishes_habitat_latch()
    {
        var id = "terminal-shell-bg-fremus";
        Assert.True(IgniteArmStore.TryArm("shell_finished", id, "terminal:fremus: mirror.py"));
        Assert.Equal(1, IgniteArmStore.Notify("shell_finished", ok: true, pulse: "fremus", detail: "done"));

        var latch = IgniteWakeLatch.TryRead();
        Assert.NotNull(latch);
        Assert.Equal(id, latch!.ArmId);
        Assert.Equal(IgniteWakeLatch.ChannelHabitat, latch.Channel);

        var snap = IgniteArmStore.Snapshot().First(a => a.Id == id);
        Assert.Equal("fired", snap.Status);
    }

    [Fact]
    public void TryAutoArmBackground_uses_tab_prefix()
    {
        Assert.True(IgniteShellBridge.TryAutoArmBackground("fremus", "python x", enabled: true, out var armId));
        Assert.StartsWith(IgniteShellBridge.BackgroundArmIdPrefix, armId!, StringComparison.Ordinal);
        Assert.Contains(IgniteArmStore.Snapshot(), a => a.Id == armId);
    }
}
