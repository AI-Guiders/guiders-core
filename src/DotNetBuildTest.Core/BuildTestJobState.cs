namespace DotNetBuildTest.Core;

public enum BuildTestJobState
{
    Queued,
    Running,
    Done,
    Failed,
    Cancelled,
    TimedOut
}
