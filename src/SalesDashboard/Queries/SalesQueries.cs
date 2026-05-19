using System.Data;
using Dapper;
using ImprovedSalesForecast.DataStructures;
using ImprovedSalesForecast.SalesDashboard.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ImprovedSalesForecast.SalesDashboard.Queries;

/// <summary>
/// Dapper-based read queries — fast SQL for the hot paths
/// (search, history retrieval, lag computation).
/// </summary>
public class SalesQueries : ISalesQueries
{
    private readonly SalesContext _context;

    public SalesQueries(SalesContext context)
    {
        _context = context;
    }

    private IDbConnection Connection =>
        new SqlConnection(_context.Database.GetConnectionString());

    // ── Product search ─────────────────────────────────────────────────────

    public async Task<IEnumerable<ProductSearchResult>> GetProductsByDescriptionAsync(string description)
    {
        const string sql = @"
            SELECT p.Id, p.StockCode, p.Description, p.UnitPrice,
                   COUNT(DISTINCT CONCAT(ms.Year, '-', ms.Month)) AS MonthCount
            FROM Products p
            INNER JOIN MonthlySales ms ON ms.ProductId = p.Id
            WHERE p.Description LIKE @Search
            GROUP BY p.Id, p.StockCode, p.Description, p.UnitPrice
            HAVING COUNT(DISTINCT CONCAT(ms.Year, '-', ms.Month)) >= 6
            ORDER BY p.Description";

        using var conn = Connection;
        return await conn.QueryAsync<ProductSearchResult>(sql, new { Search = $"%{description}%" });
    }

    // ── Sales history for chart display ───────────────────────────────────

    public async Task<IEnumerable<MonthlyHistoryDto>> GetProductHistoryAsync(int productId)
    {
        const string sql = @"
            SELECT
                ms.Year, ms.Month, ms.TotalUnits,
                ms.AvgDailyUnits, ms.MaxDailyUnits, ms.MinDailyUnits,
                ms.AvgUnitPrice
            FROM MonthlySales ms
            WHERE ms.ProductId = @ProductId
            ORDER BY ms.Year, ms.Month";

        using var conn = Connection;
        return await conn.QueryAsync<MonthlyHistoryDto>(sql, new { ProductId = productId });
    }

    // ── Structured SaleData for ML model input ────────────────────────────

    public async Task<IEnumerable<SaleData>> GetProductSaleDataAsync(int productId)
    {
        const string sql = @"
            WITH ordered AS (
                SELECT
                    ms.ProductId,
                    ms.Year, ms.Month,
                    ms.TotalUnits    AS Units,
                    ms.AvgDailyUnits AS Avg,
                    ms.MaxDailyUnits AS [Max],
                    ms.MinDailyUnits AS [Min],
                    ms.SalesDays     AS [Count],
                    ms.AvgUnitPrice  AS UnitPrice,
                    LAG(ms.TotalUnits, 1)  OVER (ORDER BY ms.Year, ms.Month) AS Lag1,
                    LAG(ms.TotalUnits, 3)  OVER (ORDER BY ms.Year, ms.Month) AS Lag3,
                    LAG(ms.TotalUnits, 12) OVER (ORDER BY ms.Year, ms.Month) AS Lag12,
                    AVG(ms.TotalUnits) OVER (
                        ORDER BY ms.Year, ms.Month
                        ROWS BETWEEN 2 PRECEDING AND CURRENT ROW) AS RollingMean3,
                    AVG(ms.TotalUnits) OVER (
                        ORDER BY ms.Year, ms.Month
                        ROWS BETWEEN 5 PRECEDING AND CURRENT ROW) AS RollingMean6,
                    LEAD(ms.TotalUnits, 1) OVER (ORDER BY ms.Year, ms.Month) AS [Next]
                FROM MonthlySales ms
                WHERE ms.ProductId = @ProductId
            )
            SELECT * FROM ordered WHERE [Next] IS NOT NULL
            ORDER BY Year, Month";

        using var conn = Connection;
        var rows = await conn.QueryAsync(sql, new { ProductId = productId });

        return rows.Select(r => new SaleData
        {
            ProductId = (float)(int)r.ProductId,
            Year = (float)(int)r.Year,
            Month = (float)(int)r.Month,
            Units = (float)(int)r.Units,
            Avg = (float)(double)r.Avg,
            Max = r.Max != null ? (float)(int)r.Max : 0f,
            Min = r.Min != null ? (float)(int)r.Min : 0f,
            Count = r.Count != null ? (float)(int)r.Count : 0f,
            UnitPrice = r.UnitPrice != null ? (float)(double)r.UnitPrice : 0f,
            Lag1 = r.Lag1 != null ? (float)(int)r.Lag1 : 0f,
            Lag3 = r.Lag3 != null ? (float)(int)r.Lag3 : 0f,
            Lag12 = r.Lag12 != null ? (float)(int)r.Lag12 : 0f,
            RollingMean3 = r.RollingMean3 != null ? (float)(decimal)r.RollingMean3 : 0f,
            RollingMean6 = r.RollingMean6 != null ? (float)(decimal)r.RollingMean6 : 0f,
            Next = r.Next != null ? (float)(int)r.Next : 0f
        });
    }

    public async Task<bool> ProductHasEnoughHistoryAsync(int productId, int minMonths = 6)
    {
        const string sql = @"
            SELECT COUNT(*) FROM MonthlySales WHERE ProductId = @ProductId";
        using var conn = Connection;
        var count = await conn.ExecuteScalarAsync<int>(sql, new { ProductId = productId });
        return count >= minMonths;
    }
}
