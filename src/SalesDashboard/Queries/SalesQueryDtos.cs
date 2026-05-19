namespace ImprovedSalesForecast.SalesDashboard.Queries;

/// <summary>Product row returned by the search API.</summary>
public record ProductSearchResult(
    int     Id,
    string  StockCode,
    string  Description,
    decimal UnitPrice,
    int     MonthCount);

/// <summary>One month of sales history returned by the history API.</summary>
public record MonthlyHistoryDto(
    int   Year,
    int   Month,
    int   TotalUnits,
    float AvgDailyUnits,
    int   MaxDailyUnits,
    int   MinDailyUnits,
    float AvgUnitPrice);
