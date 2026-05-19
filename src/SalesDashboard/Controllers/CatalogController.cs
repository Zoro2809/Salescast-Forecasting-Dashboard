using ImprovedSalesForecast.SalesDashboard.Queries;
using Microsoft.AspNetCore.Mvc;

namespace ImprovedSalesForecast.SalesDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CatalogController : ControllerBase
{
    private readonly ISalesQueries _queries;

    public CatalogController(ISalesQueries queries)
    {
        _queries = queries;
    }

    /// <summary>
    /// Returns products whose description contains the search term.
    /// Only products with at least 6 months of sales history are returned
    /// (those are the ones we can generate meaningful forecasts for).
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string description)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Length < 2)
            return BadRequest("Search term must be at least 2 characters.");

        var results = await _queries.GetProductsByDescriptionAsync(description);
        return Ok(results);
    }

    /// <summary>Returns the full monthly sales history for a product (for the chart).</summary>
    [HttpGet("{productId:int}/history")]
    public async Task<IActionResult> History(int productId)
    {
        var history = await _queries.GetProductHistoryAsync(productId);
        return Ok(history);
    }
}
