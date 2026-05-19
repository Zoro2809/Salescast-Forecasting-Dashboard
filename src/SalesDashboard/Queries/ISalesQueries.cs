using ImprovedSalesForecast.DataStructures;

namespace ImprovedSalesForecast.SalesDashboard.Queries;

public interface ISalesQueries
{
    Task<IEnumerable<ProductSearchResult>> GetProductsByDescriptionAsync(string description);
    Task<IEnumerable<MonthlyHistoryDto>> GetProductHistoryAsync(int productId);
    Task<IEnumerable<SaleData>> GetProductSaleDataAsync(int productId);
    Task<bool> ProductHasEnoughHistoryAsync(int productId, int minMonths = 6);
}
