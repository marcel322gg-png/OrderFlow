namespace OrderFlow.Domain;

public enum OrderStatus { Pending, Confirmed, Processing, Shipped, Delivered, Cancelled }
public enum InventoryTransactionType { Receipt, Reservation, Release, Adjustment }

public sealed class Customer
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; }
    public string Email { get; private set; }
    public string? Phone { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    private Customer() { Name = Email = string.Empty; }
    public Customer(string name, string email, string? phone = null) { Name = Required(name, nameof(name)); Email = Required(email, nameof(email)).ToLowerInvariant(); Phone = phone; }
    public void Update(string name, string email, string? phone) { Name = Required(name, nameof(name)); Email = Required(email, nameof(email)).ToLowerInvariant(); Phone = phone; }
    private static string Required(string value, string field) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{field} is required.") : value.Trim();
}

public sealed class Product
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Sku { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public int StockOnHand { get; private set; }
    public int ReservedStock { get; private set; }
    public int Version { get; private set; }
    public bool IsAvailable { get; private set; } = true;
    private Product() { Sku = Name = string.Empty; }
    public Product(string sku, string name, decimal price, int initialStock = 0, string? description = null)
    { Sku = Required(sku, nameof(sku)).ToUpperInvariant(); Name = Required(name, nameof(name)); SetPrice(price); if (initialStock < 0) throw new ArgumentOutOfRangeException(nameof(initialStock)); StockOnHand = initialStock; Description = description; }
    public void Update(string name, string? description, decimal price, bool isAvailable) { Name = Required(name, nameof(name)); Description = description; SetPrice(price); IsAvailable = isAvailable; Version++; }
    public void Receive(int quantity) { if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity)); StockOnHand = checked(StockOnHand + quantity); Version++; }
    public void Reserve(int quantity) { if (quantity <= 0 || AvailableStock < quantity) throw new InvalidOperationException($"Insufficient stock for {Sku}."); ReservedStock += quantity; Version++; }
    public void Release(int quantity) { if (quantity <= 0 || ReservedStock < quantity) throw new InvalidOperationException("Invalid stock release."); ReservedStock -= quantity; Version++; }
    public void Adjust(int quantity) { if (quantity < 0 || quantity < ReservedStock) throw new InvalidOperationException("Adjustment cannot go below reserved stock."); StockOnHand = quantity; Version++; }
    public int AvailableStock => StockOnHand - ReservedStock;
    private void SetPrice(decimal price) { if (price < 0) throw new ArgumentOutOfRangeException(nameof(price)); Price = price; }
    private static string Required(string value, string field) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{field} is required.") : value.Trim();
}

public sealed class Order
{
    private readonly List<OrderItem> _items = [];
    private readonly List<OrderStatusHistory> _history = [];
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CustomerId { get; private set; }
    public Customer? Customer { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public int Version { get; private set; }
    public IReadOnlyCollection<OrderItem> Items => _items;
    public IReadOnlyCollection<OrderStatusHistory> History => _history;
    public decimal Total => _items.Sum(x => x.LineTotal);
    private Order() { }
    public Order(Guid customerId, IEnumerable<(Product Product, int Quantity)> lines)
    {
        CustomerId = customerId;
        var groupedLines = lines.GroupBy(line => line.Product.Id)
            .Select(group => (Product: group.First().Product, Quantity: group.Sum(line => line.Quantity)))
            .ToArray();
        if (groupedLines.Length == 0) throw new ArgumentException("An order must contain at least one item.", nameof(lines));

        foreach (var (product, quantity) in groupedLines)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(lines), "Order quantities must be positive.");
            if (!product.IsAvailable) throw new InvalidOperationException($"Product {product.Sku} is unavailable.");
            if (product.AvailableStock < quantity) throw new InvalidOperationException($"Insufficient stock for {product.Sku}.");
        }

        foreach (var (product, quantity) in groupedLines)
        {
            product.Reserve(quantity);
            _items.Add(new OrderItem(Id, product.Id, quantity, product.Price));
        }
        _history.Add(new OrderStatusHistory(Id, Status, DateTime.UtcNow));
    }
    public void ChangeStatus(OrderStatus next)
    {
        var allowed = Status switch
        {
            OrderStatus.Pending => next is OrderStatus.Confirmed or OrderStatus.Cancelled,
            OrderStatus.Confirmed => next is OrderStatus.Processing or OrderStatus.Cancelled,
            OrderStatus.Processing => next is OrderStatus.Shipped or OrderStatus.Cancelled,
            OrderStatus.Shipped => next == OrderStatus.Delivered,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException($"Transition from {Status} to {next} is not allowed.");
        Status = next;
        Version++;
        _history.Add(new OrderStatusHistory(Id, next, DateTime.UtcNow));
    }
}

public sealed class OrderItem(Guid orderId, Guid productId, int quantity, decimal unitPrice)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; } = orderId;
    public Guid ProductId { get; private set; } = productId;
    public int Quantity { get; private set; } = quantity;
    public decimal UnitPrice { get; private set; } = unitPrice;
    public decimal LineTotal => Quantity * UnitPrice;
}

public sealed class OrderStatusHistory(Guid orderId, OrderStatus status, DateTime changedAtUtc)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; } = orderId;
    public OrderStatus Status { get; private set; } = status;
    public DateTime ChangedAtUtc { get; private set; } = changedAtUtc;
}

public sealed class InventoryTransaction(Guid productId, InventoryTransactionType type, int quantity, string? reference)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ProductId { get; private set; } = productId;
    public InventoryTransactionType Type { get; private set; } = type;
    public int Quantity { get; private set; } = quantity;
    public string? Reference { get; private set; } = reference;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
}

public sealed class ApplicationUser
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
}
