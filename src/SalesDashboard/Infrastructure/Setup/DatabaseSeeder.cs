using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using ImprovedSalesForecast.SalesDashboard.EntityModels;
using ImprovedSalesForecast.SalesDashboard.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ImprovedSalesForecast.SalesDashboard.Infrastructure.Setup;

public class DatabaseSeeder
{
    private readonly SalesContext _context;
    private readonly ILogger<DatabaseSeeder> _logger;
    private readonly IWebHostEnvironment _env;

    public DatabaseSeeder(SalesContext context, ILogger<DatabaseSeeder> logger, IWebHostEnvironment env)
    {
        _context = context;
        _logger = logger;
        _env = env;
    }

    public async Task SeedAsync()
    {
        // ── Step 1: Ensure DB and tables exist ────────────────────────────
        try
        {
            await _context.Database.EnsureCreatedAsync();
        }
        catch (SqlException ex) when (ex.Number == 1801) // DB already exists
        {
            _logger.LogWarning("Database already exists — checking tables.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("EnsureCreated warning (non-fatal): {Message}", ex.Message);
        }

        // ── Step 2: Check seeding state ───────────────────────────────────
        bool hasProducts = false;
        bool hasSales = false;
        try
        {
            hasProducts = await _context.Products.AnyAsync();
            hasSales    = await _context.MonthlySales.AnyAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read tables (will try to seed): {Message}", ex.Message);
        }

        if (hasProducts && hasSales)
        {
            _logger.LogInformation("Database already seeded — skipping.");
            return;
        }

        // Partial seed from a previous failed run — clear and start fresh
        if (hasProducts && !hasSales)
        {
            _logger.LogInformation("Partial seed detected — clearing Products to reseed.");
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM [MonthlySales]");
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM [Products]");
        }

        // ── Step 3: Find CSV ──────────────────────────────────────────────
        var csvPath = FindCsvPath();
        if (csvPath == null)
        {
            _logger.LogWarning("online_retail.csv not found. Looked in: {Root}", _env.ContentRootPath);
            return;
        }

        _logger.LogInformation("Seeding database from {Path}", csvPath);

        var (products, monthlySales) = ProcessCsv(csvPath);

        // ── Step 4: Insert Products ───────────────────────────────────────
        _logger.LogInformation("Inserting {Count} products...", products.Count);
        await _context.Products.AddRangeAsync(products);
        await _context.SaveChangesAsync();

        // ── Step 5: Insert MonthlySales in batches ────────────────────────
        _logger.LogInformation("Inserting {Count:N0} monthly sale records...", monthlySales.Count);
        foreach (var batch in monthlySales.Chunk(500))
        {
            await _context.MonthlySales.AddRangeAsync(batch);
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("Database seeding complete.");
    }

    // ── CSV path search ────────────────────────────────────────────────────

    private string? FindCsvPath()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "data", "online_retail.csv")),
            Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "..", "data", "online_retail.csv")),
            Path.GetFullPath(Path.Combine(_env.ContentRootPath, "data", "online_retail.csv")),
        };

        foreach (var p in candidates)
            _logger.LogDebug("Checking CSV path: {Path}", p);

        return candidates.FirstOrDefault(File.Exists);
    }

    // ── CSV processing ─────────────────────────────────────────────────────

    private (List<Product> Products, List<MonthlySale> MonthlySales) ProcessCsv(string path)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
        };

        List<RawRow> rows;
        using (var reader = new StreamReader(path))
        using (var csv = new CsvReader(reader, config))
        {
            csv.Context.RegisterClassMap<RawRowMap>();
            rows = csv.GetRecords<RawRow>()
                .Where(r => r.Quantity > 0 && r.UnitPrice > 0 &&
                            !r.InvoiceNo.StartsWith('C') &&
                            !string.IsNullOrWhiteSpace(r.StockCode) &&
                            !string.IsNullOrWhiteSpace(r.Description) &&
                            r.InvoiceDate != default)
                .ToList();
        }

        // Stable integer ID from StockCode
        var stockMeta = rows
            .GroupBy(r => r.StockCode)
            .Select(g => new
            {
                StockCode   = g.Key,
                Description = g.First().Description,
                AvgPrice    = (decimal)g.Average(r => r.UnitPrice)
            })
            .OrderBy(x => x.StockCode)
            .Select((x, idx) => new { x.StockCode, x.Description, x.AvgPrice, Id = idx + 1 })
            .ToList();

        var stockToId = stockMeta.ToDictionary(x => x.StockCode, x => x.Id);

        var products = stockMeta.Select(x => new Product
        {
            Id          = x.Id,
            StockCode   = x.StockCode,
            Description = x.Description.Length > 200 ? x.Description[..200] : x.Description,
            UnitPrice   = x.AvgPrice
        }).ToList();

        // Aggregate to monthly
        var monthlySales = rows
            .GroupBy(r => (r.StockCode, r.InvoiceDate.Date))
            .Select(g => new
            {
                StockCode  = g.Key.StockCode,
                Year       = g.Key.Date.Year,
                Month      = g.Key.Date.Month,
                DailyUnits = g.Sum(x => x.Quantity),
                AvgPrice   = g.Average(x => x.UnitPrice)
            })
            .GroupBy(d => (d.StockCode, d.Year, d.Month))
            .Select(g => new MonthlySale
            {
                ProductId     = stockToId[g.Key.StockCode],
                Year          = g.Key.Year,
                Month         = g.Key.Month,
                TotalUnits    = g.Sum(x => x.DailyUnits),
                AvgDailyUnits = (float)g.Average(x => x.DailyUnits),
                MaxDailyUnits = g.Max(x => x.DailyUnits),
                MinDailyUnits = g.Min(x => x.DailyUnits),
                SalesDays     = g.Count(),
                AvgUnitPrice  = (float)g.Average(x => x.AvgPrice)
            })
            .ToList();

        return (products, monthlySales);
    }

    // ── Internal types ─────────────────────────────────────────────────────

    private class RawRow
    {
        public string   InvoiceNo   { get; set; } = string.Empty;
        public string   StockCode   { get; set; } = string.Empty;
        public string   Description { get; set; } = string.Empty;
        public int      Quantity    { get; set; }
        public DateTime InvoiceDate { get; set; }
        public double   UnitPrice   { get; set; }
        public string   CustomerID  { get; set; } = string.Empty;
        public string   Country     { get; set; } = string.Empty;
    }

    private class RawRowMap : ClassMap<RawRow>
    {
        public RawRowMap()
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
