#nullable enable

namespace Cdp.Ignite.Client;

public sealed class IgniteArm
{
    public string Id { get; set; } = "";
    public string Event { get; set; } = "timer";
    public string? Task { get; set; }
    public string? Reason { get; set; }
    public bool Once { get; set; } = true;
    public bool OkOnly { get; set; }
    public string Status { get; set; } = "armed";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? FiredUtc { get; set; }
}

internal sealed class ArmStoreDoc
{
    public string Schema { get; set; } = IgniteArmStore.StoreSchema;
    public DateTimeOffset SavedUtc { get; set; }
    public List<IgniteArm> Arms { get; set; } = [];
}
