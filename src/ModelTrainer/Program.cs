using ImprovedSalesForecast.ModelTrainer.DataProcessing;
using ImprovedSalesForecast.ModelTrainer.RegressionTrainer;
using ImprovedSalesForecast.ModelTrainer.TimeSeriesTrainer;
using Microsoft.ML;

// ── Paths ──────────────────────────────────────────────────────────────────
// Data file: place online_retail.csv in the data folder at the solution root.
// Model output: models are saved to SalesDashboard/Forecast/ModelFiles/ so the
// web app can pick them up without manual copying.

string solutionRoot = FindSolutionRoot();
string dataPath = Path.Combine(solutionRoot, "data", "online_retail.csv");
string modelsFolder = Path.Combine(solutionRoot, "src", "SalesDashboard", "Forecast", "ModelFiles");
string regressionModelPath = Path.Combine(modelsFolder, "lightgbm_regression.zip");
string ssaModelsFolder = Path.Combine(modelsFolder, "ssa");

Console.WriteLine("==============================================");
Console.WriteLine(" Improved Sales Forecast — Model Trainer");
Console.WriteLine("==============================================");
Console.WriteLine($"Data   : {dataPath}");
Console.WriteLine($"Models : {modelsFolder}");
Console.WriteLine();

if (!File.Exists(dataPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"ERROR: Data file not found at:\n  {dataPath}");
    Console.WriteLine("\nPlace online_retail.csv in the data/ folder at the solution root.");
    Console.ResetColor();
    return;
}

Directory.CreateDirectory(modelsFolder);
Directory.CreateDirectory(ssaModelsFolder);

var mlContext = new MLContext(seed: 42);

// ── Step 1: Load & process raw UCI data ───────────────────────────────────
var saleData = DataLoader.LoadAndProcess(dataPath);

if (saleData.Count == 0)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("ERROR: No training records produced. Check the CSV format.");
    Console.ResetColor();
    return;
}

// ── Step 2: Save processed CSV for the ML pipeline to read ────────────────
string processedCsvPath = Path.Combine(modelsFolder, "processed_sales.csv");
SaveProcessedCsv(saleData, processedCsvPath);

// ── Step 3: Train LightGBM regression model ───────────────────────────────
LightGbmModelHelper.TrainAndSave(mlContext, processedCsvPath, regressionModelPath);

// ── Step 4: Train SSA time series models per product ──────────────────────
// Use raw monthly records (before lag computation) so each product has its
// full series length available — needed for SSA's minimum series requirement
var productSeries = DataLoader.LoadMonthlyRaw(dataPath)
    .GroupBy(r => r.ProductId)
    .Select(g => (
        ProductId: g.Key,
        UnitSeries: g.OrderBy(r => r.Year).ThenBy(r => r.Month)
                     .Select(r => (float)r.Units).ToArray()
    ));

SsaModelHelper.TrainTopProducts(mlContext, productSeries, ssaModelsFolder, maxProducts: 50);

Console.WriteLine("\n==============================================");
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine(" Training complete! Models saved.");
Console.ResetColor();
Console.WriteLine(" You can now run SalesDashboard.");
Console.WriteLine("==============================================");

// ── Helpers ────────────────────────────────────────────────────────────────

static void SaveProcessedCsv(List<ImprovedSalesForecast.DataStructures.SaleData> records, string path)
{
    var lines = new List<string>
    {
        "ProductId,Year,Month,Units,Avg,Max,Min,Count,UnitPrice,Lag1,Lag3,Lag12,RollingMean3,RollingMean6,Next"
    };
    foreach (var r in records)
    {
        lines.Add(string.Join(",",
            r.ProductId, r.Year, r.Month, r.Units, r.Avg, r.Max, r.Min,
            r.Count, r.UnitPrice, r.Lag1, r.Lag3, r.Lag12,
            r.RollingMean3, r.RollingMean6, r.Next));
    }
    File.WriteAllLines(path, lines);
    Console.WriteLine($"  Processed CSV saved ({records.Count:N0} rows) → {path}");
}

static string FindSolutionRoot()
{
    // Walk up from the executable until we find the .slnx file
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (dir.GetFiles("*.slnx").Any())
            return dir.FullName;
        dir = dir.Parent;
    }
    // Fallback: two levels up from bin/Debug/net8.0 → project root, then go up two more
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
