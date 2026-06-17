namespace Astral.Core.Diagnostics;

public interface IAstralDiagnostics
{
    void Info(
        string source,
        string message,
        IReadOnlyDictionary<string, string?>? details = null);

    void Warning(
        string source,
        string message,
        IReadOnlyDictionary<string, string?>? details = null);

    void Failure(
        string source,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, string?>? details = null);

    void WriteHealth(
        string status,
        IReadOnlyDictionary<string, string?>? details = null);

    string CreateBundle();
}
