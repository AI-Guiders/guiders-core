namespace DotNetBuildTest.Core;

public sealed record BuildTestBusyResponse(bool accepted, string status, int retry_after_seconds, string message);
