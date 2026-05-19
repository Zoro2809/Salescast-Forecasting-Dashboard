using Microsoft.ML.Data;

namespace ImprovedSalesForecast.DataStructures;

/// <summary>
/// Output from LightGBM regression model — single predicted value for next month.
/// </summary>
public class SaleRegressionPrediction
{
    [ColumnName("Score")]
    public float Score { get; set; }
}

/// <summary>
/// Output from SSA time series model — array of forecasted values with confidence bounds.
/// </summary>
public class SaleTimeSeriesPrediction
{
    public float[] ForecastedUnits { get; set; } = Array.Empty<float>();
    public float[] ConfidenceLowerBound { get; set; } = Array.Empty<float>();
    public float[] ConfidenceUpperBound { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Combined ensemble result returned to the UI — all three forecasts in one object.
/// </summary>
public class EnsembleForecastResult
{
    public float RegressionForecast { get; set; }
    public float TimeSeriesForecast { get; set; }
    public float EnsembleForecast { get; set; }    // weighted combination
    public float ConfidenceLower { get; set; }
    public float ConfidenceUpper { get; set; }
    public int ForecastMonth { get; set; }
    public int ForecastYear { get; set; }
}
