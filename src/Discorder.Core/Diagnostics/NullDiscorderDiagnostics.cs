namespace Discorder.Core.Diagnostics;

public sealed class NullDiscorderDiagnostics : IDiscorderDiagnostics
{
    public static NullDiscorderDiagnostics Instance { get; } = new();

    private NullDiscorderDiagnostics()
    {
    }

    public void Info(
        string source,
        string message,
        IReadOnlyDictionary<string, string?>? details = null)
    {
    }

    public void Warning(
        string source,
        string message,
        IReadOnlyDictionary<string, string?>? details = null)
    {
    }

    public void Failure(
        string source,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, string?>? details = null)
    {
    }

    public void WriteHealth(
        string status,
        IReadOnlyDictionary<string, string?>? details = null)
    {
    }

    public string CreateBundle()
    {
        return string.Empty;
    }
}
