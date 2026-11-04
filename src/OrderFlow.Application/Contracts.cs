using OrderFlow.Domain;

namespace OrderFlow.Application;

public sealed record Page<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount);
public sealed record CustomerDto(Guid Id, string Name, string Email, string? Phone, DateTime CreatedAtUtc);
public sealed record CustomerRequest(string Name, string Email, string? Phone);
public sealed record ProductDto(Guid Id, string Sku, string Name, string? Description, decimal Price, int StockOnHand, int AvailableStock, bool IsAvailable);
public sealed record CreateProductRequest(string Sku, string Name, string? Description, decimal Price, int Stock);
public sealed record UpdateProductRequest(string Name, string? Description, decimal Price, bool IsAvailable);
public sealed record OrderItemDto(Guid ProductId, int Quantity);
public sealed record OrderDto(Guid Id, Guid CustomerId, OrderStatus Status, decimal Total, DateTime CreatedAtUtc, IReadOnlyList<OrderLineDto> Items);
public sealed record OrderStatusHistoryDto(OrderStatus Status, DateTime ChangedAtUtc);
public sealed record OrderLineDto(Guid ProductId, int Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record CreateOrderRequest(Guid CustomerId, IReadOnlyList<OrderItemDto> Items);
public sealed record InventoryRequest(Guid ProductId, int Quantity, InventoryTransactionType Type, string? Reference);
public sealed record InventoryTransactionDto(Guid Id, Guid ProductId, InventoryTransactionType Type, int Quantity, string? Reference, DateTime CreatedAtUtc);
public sealed record LoginRequest(string Email, string Password);
public sealed record RegisterRequest(string Email, string Password, string? DisplayName);
public sealed record TokenDto(string AccessToken, DateTime ExpiresAtUtc, string Email, IReadOnlyList<string> Roles);
public sealed record AnalyticsOverview(int OrderCount, decimal Sales, decimal AverageOrderValue, int CancelledOrders, IReadOnlyList<PopularProductDto> PopularProducts);
public sealed record PopularProductDto(Guid ProductId, string Sku, string Name, int QuantitySold);

public interface ICustomerService
{
    Task<Page<CustomerDto>> GetCustomersAsync(string? search, int page, int pageSize, CancellationToken ct);
    Task<CustomerDto?> GetCustomerAsync(Guid id, CancellationToken ct);
    Task<CustomerDto> CreateCustomerAsync(string name, string email, string? phone, CancellationToken ct);
    Task<CustomerDto?> UpdateCustomerAsync(Guid id, string name, string email, string? phone, CancellationToken ct);
    Task<bool> DeleteCustomerAsync(Guid id, CancellationToken ct);
}

public interface IProductService
{
    Task<Page<ProductDto>> GetProductsAsync(string? search, string? sort, int page, int pageSize, CancellationToken ct);
    Task<ProductDto> CreateProductAsync(string sku, string name, string? description, decimal price, int stock, CancellationToken ct);
    Task<ProductDto?> GetProductAsync(Guid id, CancellationToken ct);
    Task<ProductDto?> UpdateProductAsync(Guid id, string name, string? description, decimal price, bool available, CancellationToken ct);
    Task<bool> DeleteProductAsync(Guid id, CancellationToken ct);
}

public interface IOrderService
{
    Task<Page<OrderDto>> GetOrdersAsync(Guid? customerId, OrderStatus? status, int page, int pageSize, CancellationToken ct);
    Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct);
    Task<OrderDto?> GetOrderAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<OrderStatusHistoryDto>?> GetOrderHistoryAsync(Guid id, CancellationToken ct);
    Task<OrderDto?> ChangeOrderStatusAsync(Guid id, OrderStatus status, CancellationToken ct);
}

public interface IInventoryService
{
    Task RecordInventoryAsync(InventoryRequest request, CancellationToken ct);
    Task<Page<InventoryTransactionDto>> GetInventoryHistoryAsync(Guid productId, int page, int pageSize, CancellationToken ct);
}

public interface IAnalyticsService
{
    Task<AnalyticsOverview> GetAnalyticsAsync(DateTime? from, DateTime? to, CancellationToken ct);
}

public interface IAuthService
{
    Task<TokenDto> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<TokenDto?> LoginAsync(LoginRequest request, CancellationToken ct);
}
