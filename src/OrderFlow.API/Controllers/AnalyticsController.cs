using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application;
namespace OrderFlow.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/analytics")]
public sealed class AnalyticsController(IAnalyticsService service) : ControllerBase
{
    [HttpGet("overview")]
    public Task<AnalyticsOverview> Overview(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct) => service.GetAnalyticsAsync(from, to, ct);
}
