using System.Globalization;
using Astral.Core.Targets;

namespace Astral.Core.Connection;

public interface ITargetApplicationProofProvider
{
    Task<TargetApplicationProofResult> VerifyAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken);
}

public sealed record TargetApplicationProofResult(
    bool Required,
    bool IsVerified,
    IReadOnlyList<string> VerifiedTargetIds,
    IReadOnlyList<string> MissingTargetIds,
    string Message,
    string? FailureKind = null,
    string? Diagnostic = null)
{
    public static TargetApplicationProofResult NotRun() =>
        new(
            Required: false,
            IsVerified: false,
            VerifiedTargetIds: [],
            MissingTargetIds: [],
            Message: "Target application proof has not run.");

    public static TargetApplicationProofResult NotRequired() =>
        new(
            Required: false,
            IsVerified: true,
            VerifiedTargetIds: [],
            MissingTargetIds: [],
            Message: "Target application proof is not required.");

    public static TargetApplicationProofResult Verified(
        IReadOnlyList<TargetDefinition> targets,
        string? message = null) =>
        new(
            Required: targets.Count > 0,
            IsVerified: true,
            VerifiedTargetIds: TargetIdsFrom(targets),
            MissingTargetIds: [],
            Message: string.IsNullOrWhiteSpace(message)
                ? "Selected application targets were verified."
                : message);

    public static TargetApplicationProofResult Unavailable(
        IReadOnlyList<TargetDefinition> targets,
        string? diagnostic = null) =>
        new(
            Required: targets.Count > 0,
            IsVerified: false,
            VerifiedTargetIds: [],
            MissingTargetIds: TargetIdsFrom(targets),
            Message: "Selected application targets require target-specific proof before Astral can report connected.",
            FailureKind: "TargetSpecificAppProofUnavailable",
            Diagnostic: string.IsNullOrWhiteSpace(diagnostic)
                ? "No target-specific application proof provider is available for the selected application targets."
                : diagnostic);

    public static TargetApplicationProofResult Failed(
        IReadOnlyList<TargetDefinition> targets,
        string message,
        string failureKind,
        string? diagnostic = null) =>
        new(
            Required: targets.Count > 0,
            IsVerified: false,
            VerifiedTargetIds: [],
            MissingTargetIds: TargetIdsFrom(targets),
            Message: message,
            FailureKind: failureKind,
            Diagnostic: diagnostic);

    public Dictionary<string, string?> ToDiagnosticDetails() =>
        new()
        {
            ["targetAppProof.required"] = Required.ToString(),
            ["targetAppProof.verified"] = IsVerified.ToString(),
            ["targetAppProof.verifiedTargetCount"] =
                VerifiedTargetIds.Count.ToString(CultureInfo.InvariantCulture),
            ["targetAppProof.missingTargetCount"] =
                MissingTargetIds.Count.ToString(CultureInfo.InvariantCulture),
            ["targetAppProof.verifiedTargetIds"] =
                string.Join(",", VerifiedTargetIds),
            ["targetAppProof.missingTargetIds"] =
                string.Join(",", MissingTargetIds),
            ["targetAppProof.message"] = Message,
            ["targetAppProof.failureKind"] = FailureKind,
            ["targetAppProof.diagnostic"] = Diagnostic
        };

    private static string[] TargetIdsFrom(
        IReadOnlyList<TargetDefinition> targets) =>
        targets
            .Select(target => target.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

internal sealed class NullTargetApplicationProofProvider : ITargetApplicationProofProvider
{
    public static NullTargetApplicationProofProvider Instance { get; } = new();

    private NullTargetApplicationProofProvider()
    {
    }

    public Task<TargetApplicationProofResult> VerifyAsync(
        RoutingPlan routingPlan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routingPlan);
        cancellationToken.ThrowIfCancellationRequested();

        var targets = routingPlan.SelectedTargets
            .Where(target => target.HasApplicationScope)
            .ToArray();
        return Task.FromResult(
            targets.Length == 0
                ? TargetApplicationProofResult.NotRequired()
                : TargetApplicationProofResult.Unavailable(targets));
    }
}
