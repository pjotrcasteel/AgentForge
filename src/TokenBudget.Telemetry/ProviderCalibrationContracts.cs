using TokenBudget.Billing;

namespace TokenBudget.Telemetry;

/// <summary>Conservative historical calibration status; never authorizes an AI execution or changes budget policy.</summary>
public enum ProviderCalibrationStatus
{
    NoMatchedRuns,
    CollectMoreEvidence,
    InvestigateUnderforecast,
    ReviewCandidate
}

/// <summary>
/// Historical single-model forecast coverage. Wilson lower bounds are descriptive 95% intervals,
/// not guarantees of future accuracy. Verified Copilot credit evidence is tracked separately.
/// </summary>
public sealed record ProviderCalibrationDiagnostics(ProviderAccount Account, string ModelId,
    int MatchedCompletedSessions, int VerifiedCreditSessions,
    double? ObservedP10TokenCoverage, double? ObservedP50TokenCoverage,
    double? ObservedP90TokenCoverage, double? P90TokenCoverageLowerBound95,
    double? MedianActualToP50Ratio, double? ObservedP90CreditCoverage,
    double? P90CreditCoverageLowerBound95, ProviderCalibrationStatus Status,
    bool CreditEvidenceReadyForReview, string Recommendation);
