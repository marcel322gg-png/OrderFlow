using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using OrderFlow.Application;
namespace OrderFlow.API.Controllers;

[ApiController, Authorize(Roles = "Admin,Employee"), Route("api/inventory")]
public sealed class InventoryController(IInventoryService service, IValidator<InventoryRequest> validator) : ControllerBase
{
    [HttpGet("products/{productId:guid}/transactions")]
    public Task<Page<InventoryTransactionDto>> History(
        Guid productId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        service.GetInventoryHistoryAsync(productId, Math.Max(page, 1), Math.Clamp(pageSize, 1, 100), ct);

    [HttpPost("transactions")]
    public async Task<IActionResult> Create(InventoryRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        await service.RecordInventoryAsync(request, ct);
        return NoContent();
    }
}
