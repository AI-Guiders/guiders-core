namespace Cdp.Core;

/// <summary>
/// Typed diagnostic graph node — cause chain is carried structurally, depth-on-demand at render time.
/// Replaces flattened "cause: text" hacks (operator course: diagnostics are a graph, not text).
/// </summary>
public sealed record DiagnosticEnvelope(
    string Stage,
    string Message,
    string? Code,
    DiagnosticEnvelope? Cause)
{
    public static DiagnosticEnvelope FromException(string stage, Exception ex, int maxDepth = 8)
    {
        var cause = ex.InnerException;
        return new DiagnosticEnvelope(
            stage,
            ex.Message,
            ex.GetType().Name,
            cause is null || maxDepth <= 0 ? null : FromException(stage, cause, maxDepth - 1));
    }
}

/// <summary>Exception carrying a typed diagnostic envelope — surfaces the cause graph, not flat text.</summary>
public sealed class DiagnosticException : Exception
{
    public DiagnosticEnvelope Envelope { get; }

    public DiagnosticException(DiagnosticEnvelope envelope) : base(envelope.Message) => Envelope = envelope;
}
