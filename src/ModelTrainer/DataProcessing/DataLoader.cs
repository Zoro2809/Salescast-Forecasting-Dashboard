using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using ImprovedSalesForecast.DataStructures;

namespace ImprovedSalesForecast.ModelTrainer.DataProcessing;

/// <summary>
/// Reads the raw UCI Online Retail CSV, cleans it, aggregates to monthly level,
/// and hands off to FeatureEngineer for lag feature computation.
/// </summary>
public static class DataLoader
{
    public static List<SaleData> LoadAndProcess(string csvPath)
    {
        Console.WriteLine($"Loading raw data from: {csvPath}");

        var rawRows = ReadRawCsv(csvPath);
        Console.WriteLine($"  Raw rows read: {rawRows.Count:N0}");

        var cleaned = CleanRows(rawRows);
        Console.WriteLine($"  After cleaning: {cleaned.Count:N0}");

        var monthly = AggregateToMonthly(cleaned);
        Console.WriteLine($"  Monthly product-month records: {monthly.Count:N0}");

        var withFeatures = FeatureEngineer.AddLagFeatures(monthly);
        Console.WriteLine($"  Records with lag features (ready for training): {withFeatures.Count:N0}");

        return withFeatures;
    }

    // ── Raw CSV reading ────────────────────────────────────────────────────

    private static List<RawRetailRow> ReadRawCsv(string path)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
        };

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, config);
        csv.Context.RegisterClassMap<RawRetailRowMap>();
        return csv.GetRecords<RawRetailRow>().ToList();
    }

    // ── Cleaning rules ─────────────────────────────────────────────────────

    private static List<RawRetailRow> CleanRows(List<RawRetailRow> rows)
    {
        return rows.Where(r =>
            !string.IsNullOrWhiteSpace(r.StockCode) &&
            !string.IsNullOrWhiteSpace(r.Description) &&
            r.Quantity > 0 &&                          // exclude returns/cancellations
            r.UnitPrice > 0 &&
            !r.InvoiceNo.StartsWith('C') &&            // 'C' prefix = cancelled invoice
            r.InvoiceDate != default
        ).ToList();
    }

    // ── Public method for SSA: returns raw monthly records before lag computation ──

    public static List<MonthlyProductRecord> LoadMonthlyRaw(string csvPath)
    {
        var rawRows = ReadRawCsv(csvPath);
        var cleaned = CleanRows(rawRows);
        return AggregateToMonthly(cleaned);
    }

    // ── Monthly aggregation ───────────────────────────────────────────────

    private static List<MonthlyProductRecord> AggregateToMonthly(List<RawRetailRow> rows)
    {
        // Assign a stable integer productId from StockCode
        var stockCodes = rows.Select(r => r.StockCode).Distinct().OrderBy(x => x).ToList();
        var stockCodeToId = stockCodes
            .Select((code, idx) => (code, id: idx + 1))
            .ToDictionary(x => x.code, x => x.id);

        // Group to daily per product first, then roll up to monthly
        var dailyGroups = rows
            .GroupBy(r => (
                StockCode: r.StockCode,
                Date: r.InvoiceDate.Date
            ))
            .Select(g => new
            {
                StockCode = g.Key.StockCode,
                Description = g.First().Description,
                Date = g.Key.Date,
                Year = g.Key.Date.Year,
                Month = g.Key.Date.Month,
                DailyUnits = g.Sum(x => x.Quantity),
                AvgPrice = g.Average(x => x.UnitPrice)
            });

        var monthly = dailyGroups
            .GroupBy(d => (d.StockCode, d.Year, d.Month))
            .Select(g => new MonthlyProductRecord
            {
                ProductId = stockCodeToId[g.Key.StockCode],
                StockCode = g.Key.StockCode,
                Description = g.First().Description,
                Year = g.Key.Year,
                Month = g.Key.Month,
                Units = g.Sum(x => x.DailyUnits),
                Avg = (float)g.Average(x => x.DailyUnits),
                Max = g.Max(x => x.DailyUnits),
                Min = g.Min(x => x.DailyUnits),
                Count = g.Count(),
                UnitPrice = (float)g.Average(x => x.AvgPrice)
            })
            .OrderBy(x => x.ProductId)
            .ThenBy(x => x.Year)
            .ThenBy(x => x.Month)
            .ToList();

        return monthly;
    }

    // ── Internal types ─────────────────────────────────────────────────────

    public class RawRetailRow
    {
        public string InvoiceNo { get; set; } = string.Empty;
        public string StockCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public DateTime InvoiceDate { get; set; }
        public double UnitPrice { get; set; }
        public string CustomerID { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
    }

    public class RawRetailRowMap : ClassMap<RawRetailRow>
    {
        public RawRetailRowMap()
        {
            Map(m => m.InvoiceNo).Name("InvoiceNo");
            Map(m => m.StockCode).Name("StockCode");
            Map(m => m.Description).Name("Description");
            Map(m => m.Quantity).Name("Quantity").TypeConverterOption.NullValues("").Default(0);
            Map(m => m.InvoiceDate).Name("InvoiceDate")
                .TypeConverterOption.DateTimeStyles(DateTimeStyles.None)
                .TypeConverterOption.Format("dd-MM-yyyy HH:mm", "d-MM-yyyy HH:mm", "dd-M-yyyy HH:mm");
            Map(m => m.UnitPrice).Name("UnitPrice").TypeConverterOption.NullValues("").Default(0.0);
            Map(m => m.CustomerID).Name("CustomerID").TypeConverterOption.NullValues("");
            Map(m => m.Country).Name("Country").TypeConverterOption.NullValues("");
        }
    }
}

/// <summary>One month of aggregated sales for one product.</summary>
public class MonthlyProductRecord
{
    public int ProductId { get; set; }
    public string StockCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public int Units { get; set; }
    public float Avg { get; set; }
    public int Max { get; set; }
    public int Min { get; set; }
    public int Count { get; set; }
    public float UnitPrice { get; set; }

    // Lag features — filled in by FeatureEngineer
    public float Lag1 { get; set; }
    public float Lag3 { get; set; }
    public float Lag12 { get; set; }
    public float RollingMean3 { get; set; }
    public float RollingMean6 { get; set; }
    public float Next { get; set; }   // label
}
