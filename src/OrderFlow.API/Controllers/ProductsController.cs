using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application;
using FluentValidation;
namespace OrderFlow.API.Controllers;

[ApiController, Authorize, Route("api/products")]
public sealed class ProductsController(
    IProductService service,
    IValidator<CreateProductRequest> createValidator,
    IValidator<UpdateProductRequest> updateValidator) : ControllerBase
{
    [HttpGet]
    public Task<Page<ProductDto>> List(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        service.GetProductsAsync(search, sort, Math.Max(page, 1), Math.Clamp(pageSize, 1, 100), ct);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var product = await service.GetProductAsync(id, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(CreateProductRequest input, CancellationToken ct)
    {
        var validation = await createValidator.ValidateAsync(input, ct);
        if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        var product = await service.CreateProductAsync(input.Sku, input.Name,
            input.Description, input.Price, input.Stock, ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest input, CancellationToken ct)
    {
        var validation = await updateValidator.ValidateAsync(input, ct);
        if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        var product = await service.UpdateProductAsync(id, input.Name, input.Description,
            input.Price, input.IsAvailable, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await service.DeleteProductAsync(id, ct) ? NoContent() : NotFound();

}
