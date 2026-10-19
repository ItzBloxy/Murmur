namespace Murmur.Core.Abstractions;

/// <summary>
/// Session diagnostics. Deliberately a plain text log rather than structured events — its
/// audience is a human reading it after a dictation that went wrong.
/// </summary>
public interface IDiagnosticLog
{
    void Write(string category, string message);
}

/// <summary>Used by tests and anywhere logging is not wired up.</summary>
public sealed class NullDiagnosticLog : IDiagnosticLog
{
    public static readonly NullDiagnosticLog Instance = new();

    private NullDiagnosticLog() { }

    public void Write(string category, string message) { }
}
