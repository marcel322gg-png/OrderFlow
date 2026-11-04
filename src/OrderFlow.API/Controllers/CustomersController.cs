using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application;
using FluentValidation;
namespace OrderFlow.API.Controllers;

[ApiController, Authorize, Route("api/customers")]
public sealed class CustomersController(ICustomerService service, IValidator<CustomerRequest> validator) : ControllerBase
{
    [HttpGet]
    public Task<Page<CustomerDto>> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        service.GetCustomersAsync(search, Math.Max(page, 1), Math.Clamp(pageSize, 1, 100), ct);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var customer = await service.GetCustomerAsync(id, ct);
        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CustomerRequest input, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        var customer = await service.CreateCustomerAsync(input.Name, input.Email, input.Phone, ct);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CustomerRequest input, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        var customer = await service.UpdateCustomerAsync(id, input.Name, input.Email, input.Phone, ct);
        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await service.DeleteCustomerAsync(id, ct) ? NoContent() : NotFound();

}
