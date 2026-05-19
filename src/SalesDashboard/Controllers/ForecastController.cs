using ImprovedSalesForecast.DataStructures;
using ImprovedSalesForecast.SalesDashboard.Queries;
using ImprovedSalesForecast.SalesDashboard.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.ML;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;

namespace ImprovedSalesForecast.SalesDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ForecastController : ControllerBase
{
    private readonly PredictionEnginePool<SaleData, SaleRegressionPrediction> _regressionPool;
    private readonly ISalesQueries _queries;
    private readonly IWebHostEnvironment _env;
    private readonly EnsembleWeightSettings _weights;

    public ForecastController(
        PredictionEnginePool<SaleData, SaleRegressionPrediction> regressionPool,
        ISalesQueries queries,
        IWebHostEnvironment env,
        IOptions<EnsembleWeightSettings> weights)
    {
        _regressionPool = regressionPool;
        _queries = queries;
        _env = env;
        _weights = weights.Value;
    }

    // ── Regression (LightGBM) ──────────────────────────────────────────────

    /// <summary>
    /// LightGBM regression prediction for the next month's units.
    /// Uses lag features pulled from the database.
    /// </summary>
    /// 
    [HttpGet("{productId:int}/timeseries")]
    public async Task<IActionResult> TimeSeries(int productId)
    {
        var saleData = (await _queries.GetProductSaleDataAsync(productId)).ToList();
        if (saleData.Count < 6)
            return NotFound($"Not enough history for product {productId} (need at least 6 months).");

        var mlContext = new MLContext(seed: 42);
        var prediction = await GetTimeSeriesPredictionAsync(mlContext, productId, saleData);
        return Ok(prediction);
    }


    // ── SSA Time Series ────────────────────────────────────────────────────

    /// <summary>
    /// SSA time series forecast — loads a pre-trained per-product model.
    /// Falls back to on-demand fitting if no pre-trained model exists.
    /// Returns ForecastHorizon=3 months of predictions.
    /// </summary>
    [HttpGet("{productId:int}/regression")]
    public async Task<IActionResult> Regression(int productId)
    {
        var saleData = (await _queries.GetProductSaleDataAsync(productId)).ToList();
        if (saleData.Count == 0)
            return NotFound($"No sales history found for product {productId}.");

        var latest = saleData.Last();
        var prediction = _regressionPool.Predict("LightGbm", latest);
        return Ok(Math.Max(0, prediction.Score));
    }
  

    // ── Ensemble (LightGBM + SSA weighted average) ────────────────────────

    /// <summary>
    /// Combines LightGBM and SSA forecasts using configurable weights.
    /// Default: 60% LightGBM + 40% SSA — regression tends to be more stable
    /// on this dataset due to the limited time range.
    /// </summary>
    [HttpGet("{productId:int}/ensemble")]
    public async Task<IActionResult> Ensemble(int productId)
    {
        var saleData = (await _queries.GetProductSaleDataAsync(productId)).ToList();
        if (saleData.Count == 0)
            return NotFound($"No sales history found for product {productId}.");

        var latest = saleData.Last();

        // Regression prediction
        var regressionScore = Math.Max(0, _regressionPool.Predict("LightGbm", latest).Score);

        // Time series prediction
        float tsScore = 0f;
        float confidenceLow = 0f;
        float confidenceHigh = 0f;

        if (saleData.Count >= 6)
        {
            var mlContext = new MLContext(seed: 42);
            var tsPrediction = await GetTimeSeriesPredictionAsync(mlContext, productId, saleData);
            tsScore = tsPrediction.ForecastedUnits.FirstOrDefault();
            confidenceLow = tsPrediction.ConfidenceLowerBound.FirstOrDefault();
            confidenceHigh = tsPrediction.ConfidenceUpperBound.FirstOrDefault();
        }

        // Weighted ensemble
        double w1 = _weights.Regression;
        double w2 = _weights.TimeSeries;
        var ensembleScore = (float)(w1 * regressionScore + w2 * tsScore);

        // Determine forecast month
        var lastRecord = saleData.Last();
        var forecastDate = new DateTime((int)lastRecord.Year, (int)lastRecord.Month, 1).AddMonths(1);

        var result = new EnsembleForecastResult
        {
            RegressionForecast = regressionScore,
            TimeSeriesForecast = tsScore,
            EnsembleForecast = ensembleScore,
            ConfidenceLower = Math.Max(0, confidenceLow),
            ConfidenceUpper = confidenceHigh,
            ForecastMonth = forecastDate.Month,
            ForecastYear = forecastDate.Year
        };

        return Ok(result);
    }

    // ── SSA helper ─────────────────────────────────────────────────────────

    private async Task<SaleTimeSeriesPrediction> GetTimeSeriesPredictionAsync(
        MLContext mlContext, int productId, List<SaleData> saleData)
    {
        // Try pre-trained model first (much faster)
        var modelPath = Path.Combine(
            _env.ContentRootPath,
            "Forecast", "ModelFiles", "ssa",
            $"product{productId}_ssa.zip");

        if (System.IO.File.Exists(modelPath))
        {
            using var stream = System.IO.File.OpenRead(modelPath);
            SsaForecastingTransformer transformer = (SsaForecastingTransformer)mlContext.Model.Load(stream, out _);
            TimeSeriesPredictionEngine<SaleData, SaleTimeSeriesPrediction> tsEngine =
                transformer.CreateTimeSeriesEngine<SaleData, SaleTimeSeriesPrediction>(mlContext);
            return tsEngine.Predict();
        }

        // On-demand fitting (fallback — slower but always works)
        return await Task.Run(() => FitSsaOnDemand(mlContext, saleData));
    }

    private static SaleTimeSeriesPrediction FitSsaOnDemand(MLContext mlContext, List<SaleData> saleData)
    {
        int seriesLength = saleData.Count;
        int windowSize = Math.Max(2, Math.Min(12, (seriesLength - 1) / 2));

        var dataView = mlContext.Data.LoadFromEnumerable(saleData);
        var estimator = mlContext.Forecasting.ForecastBySsa(
            outputColumnName: nameof(SaleTimeSeriesPrediction.ForecastedUnits),
            inputColumnName: nameof(SaleData.Units),
            windowSize: windowSize,
            seriesLength: seriesLength,
            trainSize: seriesLength,
            horizon: 3,
            confidenceLevel: 0.95f,
            confidenceLowerBoundColumn: nameof(SaleTimeSeriesPrediction.ConfidenceLowerBound),
            confidenceUpperBoundColumn: nameof(SaleTimeSeriesPrediction.ConfidenceUpperBound));

        SsaForecastingTransformer transformer = estimator.Fit(dataView);
        TimeSeriesPredictionEngine<SaleData, SaleTimeSeriesPrediction> engine =
            transformer.CreateTimeSeriesEngine<SaleData, SaleTimeSeriesPrediction>(mlContext);
        return engine.Predict();
    }
}
