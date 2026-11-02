using System.Globalization;
using GitHub.Copilot;

namespace TokenBudget.Provider.Copilot;

/// <summary>Uninterpreted quota observations; clients must check source, account, kind, unit and acquisition status.</summary>
public sealed record CopilotSessionQuotaObservation(string ProviderReference, string QuotaId, string ObservationKind,
    string QuantityKind, string AcquisitionStatus, string Unit, decimal? EntitledQuantity,
    decimal? AvailableQuantity, DateTimeOffset? ObservedAt);

/// <summary>Experimental SDK session quota projection. No observation is silently promoted to an account credit balance.</summary>
public sealed class CopilotSdkSessionQuotaReader
{
    /// <summary>Read locally cached observations or explicitly refresh from supported quota sources.</summary>
    public async Task<IReadOnlyList<CopilotSessionQuotaObservation>> ReadAsync(CopilotSession session, bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
#pragma warning disable GHCP001 // Session quota RPC is explicitly experimental in SDK 1.0.27.
        IEnumerable<ProviderQuotaState> observations;
        if (refresh)
        {
            var response = await session.Rpc.Quota.RefreshAsync(cancellationToken);
            observations = response.ProviderQuotas ?? [];
        }
        else
        {
            var response = await session.Rpc.Quota.GetAsync(cancellationToken);
            observations = response.ProviderQuotas ?? [];
        }
#pragma warning restore GHCP001
        return observations.Select(item => new CopilotSessionQuotaObservation(
            item.Provider?.ToString() ?? "unknown", item.QuotaId?.ToString() ?? "unknown",
            item.ObservationKind?.ToString() ?? "unknown", item.QuantityKind?.ToString() ?? "unknown",
            item.AcquisitionStatus?.ToString() ?? "unknown", item.Unit.ToString(),
            CopilotSdkPricingProvider.DecimalOrNull(item.EntitledQuantity),
            CopilotSdkPricingProvider.DecimalOrNull(item.AvailableQuantity), ParseDate(item.ObservedAt))).ToArray();
    }

    private static DateTimeOffset? ParseDate(object? value) => DateTimeOffset.TryParse(
        Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal, out DateTimeOffset result) ? result : null;
}
