using ImprovedSalesForecast.DataStructures;

namespace ImprovedSalesForecast.ModelTrainer.DataProcessing;

/// <summary>
/// Computes lag features and rolling averages per product time series.
/// This is the core improvement over the original — the original had no lag features,
/// so the regression model had zero awareness of temporal ordering.
/// </summary>
public static class FeatureEngineer
{
    public static List<SaleData> AddLagFeatures(List<MonthlyProductRecord> monthly)
    {
        var result = new List<SaleData>();

        // Process each product's time series independently
        var byProduct = monthly.GroupBy(r => r.ProductId);

        foreach (var group in byProduct)
        {
            var series = group
                .OrderBy(r => r.Year)
                .ThenBy(r => r.Month)
                .ToList();

            // Need at least 13 months: 12 for lag12 + 1 for the label
            if (series.Count < 13)
                continue;

            // Build a lookup: (year, month) → units for fast lag access
            var lookup = series.ToDictionary(
                r => (r.Year, r.Month),
                r => (float)r.Units
            );

            for (int i = 0; i < series.Count - 1; i++)
            {
                var current = series[i];
                var next = series[i + 1];

                // Lag 1: units from 1 month ago
                float lag1 = GetLag(lookup, current.Year, current.Month, 1);
                float lag3 = GetLag(lookup, current.Year, current.Month, 3);
                float lag12 = GetLag(lookup, current.Year, current.Month, 12);

                // Rolling averages over the actual series (not calendar months)
                float rollingMean3 = RollingMean(series, i, 3);
                float rollingMean6 = RollingMean(series, i, 6);

                // Skip rows where critical lags are missing (beginning of series)
                if (lag1 == 0 && lag3 == 0 && lag12 == 0)
                    continue;

                result.Add(new SaleData
                {
                    ProductId = current.ProductId,
                    Year = current.Year,
                    Month = current.Month,
                    Units = current.Units,
                    Avg = current.Avg,
                    Max = current.Max,
                    Min = current.Min,
                    Count = current.Count,
                    UnitPrice = current.UnitPrice,
                    Lag1 = lag1,
                    Lag3 = lag3,
                    Lag12 = lag12,
                    RollingMean3 = rollingMean3,
                    RollingMean6 = rollingMean6,
                    Next = next.Units      // label: what we want to predict
                });
            }
        }

        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static float GetLag(
        Dictionary<(int Year, int Month), float> lookup,
        int year, int month, int lagMonths)
    {
        var (ly, lm) = SubtractMonths(year, month, lagMonths);
        return lookup.TryGetValue((ly, lm), out var val) ? val : 0f;
    }

    private static float RollingMean(List<MonthlyProductRecord> series, int currentIndex, int window)
    {
        int start = Math.Max(0, currentIndex - window + 1);
        var slice = series.Skip(start).Take(currentIndex - start + 1).ToList();
        return slice.Count == 0 ? 0f : (float)slice.Average(r => r.Units);
    }

    private static (int Year, int Month) SubtractMonths(int year, int month, int months)
    {
        var dt = new DateTime(year, month, 1).AddMonths(-months);
        return (dt.Year, dt.Month);
    }
}
