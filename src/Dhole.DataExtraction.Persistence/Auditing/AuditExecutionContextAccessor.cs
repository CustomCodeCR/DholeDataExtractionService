namespace Dhole.DataExtraction.Persistence.Auditing;

public sealed record AuditExecutionContext(
    Guid? UserId,
    string? UserName,
    string? IpAddress,
    string? UserAgent,
    Guid CorrelationId
);

public static class AuditExecutionContextAccessor
{
    private static readonly AsyncLocal<AuditExecutionContext?> CurrentContext = new();

    public static AuditExecutionContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }

    public static IDisposable Begin(AuditExecutionContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope(AuditExecutionContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}
