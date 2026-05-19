using Microsoft.ML.Data;

namespace ImprovedSalesForecast.DataStructures;

/// <summary>
/// Input record for ML models — one row per product per month.
/// Enriched with lag features that the original project lacked.
/// </summary>
public class SaleData
{
    [LoadColumn(0)] public float ProductId { get; set; }
    [LoadColumn(1)] public float Year { get; set; }
    [LoadColumn(2)] public float Month { get; set; }
    [LoadColumn(3)] public float Units { get; set; }       // total units sold this month
    [LoadColumn(4)] public float Avg { get; set; }         // avg units per day
    [LoadColumn(5)] public float Max { get; set; }         // max units in a single day
    [LoadColumn(6)] public float Min { get; set; }         // min units in a single day
    [LoadColumn(7)] public float Count { get; set; }       // number of days with sales
    [LoadColumn(8)] public float UnitPrice { get; set; }   // avg unit price that month
    [LoadColumn(9)] public float Lag1 { get; set; }        // units sold 1 month ago
    [LoadColumn(10)] public float Lag3 { get; set; }       // units sold 3 months ago
    [LoadColumn(11)] public float Lag12 { get; set; }      // units sold same month last year
    [LoadColumn(12)] public float RollingMean3 { get; set; } // 3-month rolling average
    [LoadColumn(13)] public float RollingMean6 { get; set; } // 6-month rolling average
    [LoadColumn(14)] public float Next { get; set; }       // TARGET: next month units (label)

    public override string ToString() =>
        $"Product={ProductId} Year={Year} Month={Month} Units={Units} " +
        $"Lag1={Lag1} Lag12={Lag12} RollingMean3={RollingMean3} Next={Next}";
}
