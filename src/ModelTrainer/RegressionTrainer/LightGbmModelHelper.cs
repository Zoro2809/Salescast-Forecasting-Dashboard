using ImprovedSalesForecast.DataStructures;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace ImprovedSalesForecast.ModelTrainer.RegressionTrainer;

/// <summary>
/// Trains a LightGBM regression model with lag features.
///
/// Improvement over original FastTreeTweedie:
///   1. LightGBM is faster and typically more accurate on tabular data.
///   2. Lag features (lag1, lag3, lag12, rollingMean3, rollingMean6) give the
///      model actual temporal awareness — the original completely lacked these.
///   3. Cross-validation is used to report honest out-of-sample metrics.
/// </summary>
public static class LightGbmModelHelper
{
    public static void TrainAndSave(MLContext mlContext, string dataPath, string outputModelPath)
    {
        Console.WriteLine("\n=== LightGBM Regression Training ===");

        if (File.Exists(outputModelPath))
            File.Delete(outputModelPath);

        var schema = SchemaDefinition.Create(typeof(SaleData));
        var dataView = mlContext.Data.LoadFromTextFile<SaleData>(
            dataPath,
            hasHeader: true,
            separatorChar: ',');

        var pipeline = BuildPipeline(mlContext);

        Console.WriteLine("Running 5-fold cross-validation...");
        var cvResults = mlContext.Regression.CrossValidate(dataView, pipeline, numberOfFolds: 5);
        var avgRSquared = cvResults.Average(r => r.Metrics.RSquared);
        var avgMae = cvResults.Average(r => r.Metrics.MeanAbsoluteError);
        var avgRmse = cvResults.Average(r => r.Metrics.RootMeanSquaredError);

        Console.WriteLine($"  R²   (avg): {avgRSquared:F4}  (higher = better, 1.0 = perfect)");
        Console.WriteLine($"  MAE  (avg): {avgMae:F2}  units per month");
        Console.WriteLine($"  RMSE (avg): {avgRmse:F2}  units per month");

        Console.WriteLine("Training final model on full dataset...");
        var model = pipeline.Fit(dataView);

        Directory.CreateDirectory(Path.GetDirectoryName(outputModelPath)!);
        mlContext.Model.Save(model, dataView.Schema, outputModelPath);
        Console.WriteLine($"  Saved → {outputModelPath}");

        TestPrediction(mlContext, outputModelPath);
    }

    private static IEstimator<ITransformer> BuildPipeline(MLContext mlContext)
    {
        // All numeric features including the new lag features
        var numericFeatures = new[]
        {
            nameof(SaleData.Year),
            nameof(SaleData.Month),
            nameof(SaleData.Units),
            nameof(SaleData.Avg),
            nameof(SaleData.Max),
            nameof(SaleData.Min),
            nameof(SaleData.Count),
            nameof(SaleData.UnitPrice),
            nameof(SaleData.Lag1),        // NEW vs original
            nameof(SaleData.Lag3),        // NEW vs original
            nameof(SaleData.Lag12),       // NEW vs original (same month last year)
            nameof(SaleData.RollingMean3), // NEW vs original
            nameof(SaleData.RollingMean6), // NEW vs original
        };

        return mlContext.Transforms
            .Concatenate("NumFeatures", numericFeatures)
            .Append(mlContext.Transforms.Categorical.OneHotEncoding("CatProductId", nameof(SaleData.ProductId)))
            .Append(mlContext.Transforms.Concatenate("Features", "NumFeatures", "CatProductId"))
            .Append(mlContext.Transforms.CopyColumns("Label", nameof(SaleData.Next)))
            .Append(mlContext.Regression.Trainers.LightGbm(
                labelColumnName: "Label",
                featureColumnName: "Features",
                numberOfLeaves: 40,
                numberOfIterations: 200,
                minimumExampleCountPerLeaf: 10,
                learningRate: 0.05));
    }

    private static void TestPrediction(MLContext mlContext, string modelPath)
    {
        Console.WriteLine("  Spot-check prediction:");
        using var stream = File.OpenRead(modelPath);
        var model = mlContext.Model.Load(stream, out _);
        var engine = mlContext.Model.CreatePredictionEngine<SaleData, SaleRegressionPrediction>(model);

        var sample = new SaleData
        {
            ProductId = 1, Year = 2011, Month = 10,
            Units = 200, Avg = 8f, Max = 45, Min = 1, Count = 25,
            UnitPrice = 2.5f, Lag1 = 180f, Lag3 = 150f, Lag12 = 190f,
            RollingMean3 = 175f, RollingMean6 = 160f
        };

        var prediction = engine.Predict(sample);
        Console.WriteLine($"    Sample input Units=200, Lag1=180, Lag12=190 → Predicted next month: {prediction.Score:F1}");
    }
}
