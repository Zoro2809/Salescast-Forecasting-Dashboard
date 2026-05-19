namespace ImprovedSalesForecast.SalesDashboard.EntityModels;

public class Product
{
    public int Id { get; set; }
    public string StockCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
}
