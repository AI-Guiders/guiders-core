namespace Cdp.Core;

public interface ICdpBackendModule
{
    string Domain { get; }
    bool IsEnabled { get; }
    string HealthSummary { get; }
    IReadOnlyList<ToolAffordance> Affordances { get; }
    ValueTask<string> CallAsync(string underlyingName, IReadOnlyDictionary<string, System.Text.Json.JsonElement> args);
}
