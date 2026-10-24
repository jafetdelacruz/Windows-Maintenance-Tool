namespace WinMaint.Application;

public enum DiskOperationalState { Unknown, Normal, Adverse }

public static class DiskOperationalPolicy
{
    // Exact names from MSFT_StorageFaultDomain; Healthy is retained for legacy fixtures.
    // Transitional, vendor-specific and unrecognized states do not assert physical failure.
    public static DiskOperationalState Classify(string? value)
    {
        var states = Split(value);
        if (states.Any(state => state.ToUpperInvariant() is
            "PREDICTIVE FAILURE" or "ERROR" or "NON-RECOVERABLE ERROR" or "FAILED MEDIA" or "IO ERROR"))
            return DiskOperationalState.Adverse;
        return states.Count > 0 && states.All(state =>
            state.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
            state.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
            ? DiskOperationalState.Normal : DiskOperationalState.Unknown;
    }

    public static IReadOnlyList<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : value.Split(',', StringSplitOptions.TrimEntries);
}
