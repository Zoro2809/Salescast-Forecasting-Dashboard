using ImprovedSalesForecast.DataStructures;
using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;

namespace ImprovedSalesForecast.ModelTrainer.TimeSeriesTrainer;

/// <summary>
/// Trains SSA (Single Spectrum Analysis) time series models per product.
///
/// Improvements over original:
///   1. Trains one model per product (not just product 988).
///   2. Window size is computed from actual series length, not hardcoded.
///   3. Forecasts 3 months ahead instead of 2.
///   4. Minimum series length enforced to avoid unreliable models.
///   5. Progress reporting so the user knows it's working.
/// </summary>
public static class SsaModelHelper
{
    private const int ForecastHorizon = 3;
    private const int MinSeriesLength = 12;   // need at least 1 year of monthly data

    public static void TrainTopProducts(
        MLContext mlContext,
        IEnumerable<(int ProductId, float[] UnitSeries)> productSeries,
        string modelOutputFolder,
        int maxProducts = 50)
    {
        Console.WriteLine($"\n=== SSA Time Series Training (top {maxProducts} products) ===");
        Directory.CreateDirectory(modelOutputFolder);

        var products = productSeries
            .Where(p => p.UnitSeries.Length >= MinSeriesLength)
            .OrderByDescending(p => p.UnitSeries.Sum())
            .Take(maxProducts)
            .ToList();

        Console.WriteLine($"  Training {products.Count} SSA models...");

        int trained = 0;
        foreach (var (productId, series) in products)
        {
            var modelPath = Path.Combine(modelOutputFolder, $"product{productId}_ssa.zip");
            TrainSingleProduct(mlContext, series, modelPath);
            trained++;

            if (trained % 10 == 0)
                Console.WriteLine($"  Progress: {trained}/{products.Count}");
        }

        Console.WriteLine($"  Done. {trained} SSA models saved to {modelOutputFolder}");
    }

    private static void TrainSingleProduct(
        MLContext mlContext,
        float[] series,
        string modelPath)
    {
        int seriesLength = series.Length;
        // trainSize must be strictly > 2 * windowSize per ML.NET requirement
        int windowSize = Math.Max(2, Math.Min(12, (seriesLength - 1) / 2));

        var saleData = series.Select(u => new SaleData { Units = u }).ToList();
        var dataView = mlContext.Data.LoadFromEnumerable(saleData);

        var forecastEstimator = mlContext.Forecasting.ForecastBySsa(
            outputColumnName: nameof(SaleTimeSeriesPrediction.ForecastedUnits),
            inputColumnName: nameof(SaleData.Units),
            windowSize: windowSize,
            seriesLength: seriesLength,
            trainSize: seriesLength,
            horizon: ForecastHorizon,
            confidenceLevel: 0.95f,
            confidenceLowerBoundColumn: nameof(SaleTimeSeriesPrediction.ConfidenceLowerBound),
            confidenceUpperBoundColumn: nameof(SaleTimeSeriesPrediction.ConfidenceUpperBound));

        // Explicitly type as SsaForecastingTransformer so CreateTimeSeriesEngine is accessible
        SsaForecastingTransformer forecastTransformer = forecastEstimator.Fit(dataView);
        TimeSeriesPredictionEngine<SaleData, SaleTimeSeriesPrediction> forecastEngine =
            forecastTransformer.CreateTimeSeriesEngine<SaleData, SaleTimeSeriesPrediction>(mlContext);

        forecastEngine.CheckPoint(mlContext, modelPath);
    }
}
