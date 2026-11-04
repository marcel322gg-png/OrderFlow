using OrderFlow.Application;

namespace OrderFlow.Tests;

public sealed class ValidatorTests
{
    [Fact]
    public async Task Create_order_requires_customer_and_positive_product_lines()
    {
        var validator = new CreateOrderValidator();
        var result = await validator.ValidateAsync(new CreateOrderRequest(Guid.Empty,
            [new OrderItemDto(Guid.Empty, 0)]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateOrderRequest.CustomerId));
        Assert.Contains(result.Errors, error => error.PropertyName.StartsWith(nameof(CreateOrderRequest.Items), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Registration_requires_a_strong_password()
    {
        var validator = new RegisterValidator();
        var result = await validator.ValidateAsync(new RegisterRequest("not-an-email", "short", null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(RegisterRequest.Email));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(RegisterRequest.Password));
    }

    [Fact]
    public async Task Customer_requires_a_valid_email_address()
    {
        var validator = new CustomerRequestValidator();
        var result = await validator.ValidateAsync(new CustomerRequest("A customer", "invalid", null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CustomerRequest.Email));
    }

    [Fact]
    public async Task Product_rejects_negative_price_and_stock()
    {
        var validator = new CreateProductValidator();
        var result = await validator.ValidateAsync(new CreateProductRequest("SKU-1", "Widget", null, -1, -1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateProductRequest.Price));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateProductRequest.Stock));
    }

    [Fact]
    public async Task Inventory_requests_only_allow_positive_receipts()
    {
        var validator = new InventoryRequestValidator();
        var result = await validator.ValidateAsync(new InventoryRequest(Guid.Empty, 0,
            OrderFlow.Domain.InventoryTransactionType.Adjustment, null));

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
    }
}
