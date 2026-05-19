namespace ImprovedSalesForecast.SalesDashboard.EntityModels;

public class MonthlySale
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int TotalUnits { get; set; }
    public float AvgDailyUnits { get; set; }
    public int MaxDailyUnits { get; set; }
    public int MinDailyUnits { get; set; }
    public int SalesDays { get; set; }
    public float AvgUnitPrice { get; set; }

    public Product Product { get; set; } = null!;
}
