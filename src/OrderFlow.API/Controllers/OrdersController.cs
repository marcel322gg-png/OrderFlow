using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application;
using OrderFlow.Domain;
namespace OrderFlow.API.Controllers;

[ApiController, Authorize, Route("api/orders")]
public sealed class OrdersController(IOrderService service, IValidator<CreateOrderRequest> validator) : ControllerBase
{
    [HttpGet]
    public Task<Page<OrderDto>> List([FromQuery] Guid? customerId, [FromQuery] OrderStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        service.GetOrdersAsync(customerId, status, Math.Max(page, 1), Math.Clamp(pageSize, 1, 100), ct);

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));

        var order = await service.CreateOrderAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var order = await service.GetOrderAsync(id, ct);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> History(Guid id, CancellationToken ct)
    {
        var history = await service.GetOrderHistoryAsync(id, ct);
        return history is null ? NotFound() : Ok(history);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, StatusInput input, CancellationToken ct)
    {
        var order = await service.ChangeOrderStatusAsync(id, input.Status, ct);
        return order is null ? NotFound() : Ok(order);
    }

    public sealed record StatusInput(OrderStatus Status);
}
