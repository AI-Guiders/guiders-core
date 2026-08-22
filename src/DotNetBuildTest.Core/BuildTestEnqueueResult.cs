namespace DotNetBuildTest.Core;

public sealed record BuildTestEnqueueResult(bool Accepted, string? JobId, int RetryAfterSeconds);
