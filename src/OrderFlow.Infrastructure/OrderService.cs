using Microsoft.EntityFrameworkCore;
using OrderFlow.Application;
using OrderFlow.Domain;

namespace OrderFlow.Infrastructure;

public sealed class OrderService(OrderFlowDbContext db) : IOrderService
{
    public async Task<Page<OrderDto>> GetOrdersAsync(Guid? customerId, OrderStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking();
        if (customerId.HasValue) query = query.Where(x => x.CustomerId == customerId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        var count = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.CustomerId, x.Status, x.CreatedAtUtc })
            .ToListAsync(ct);
        var ids = orders.Select(x => x.Id).ToArray();
        var lines = await db.OrderItems.AsNoTracking().Where(x => ids.Contains(x.OrderId))
            .Select(x => new { x.OrderId, Line = new OrderLineDto(x.ProductId, x.Quantity, x.UnitPrice, x.Quantity * x.UnitPrice) })
            .ToListAsync(ct);
        var linesByOrder = lines.GroupBy(x => x.OrderId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Line).ToArray());
        var items = orders.Select(order =>
        {
            var orderLines = linesByOrder.GetValueOrDefault(order.Id) ?? [];
            return new OrderDto(order.Id, order.CustomerId, order.Status,
                orderLines.Sum(line => line.LineTotal), order.CreatedAtUtc, orderLines);
        }).ToArray();
        return new Page<OrderDto>(items, page, pageSize, count);
    }

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(x => x.Id == request.CustomerId, ct))
            throw new KeyNotFoundException("Customer not found.");

        var requestedLines = request.Items.GroupBy(x => x.ProductId)
            .Select(group => (ProductId: group.Key, Quantity: group.Sum(item => item.Quantity)))
            .ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var products = await db.Products.Where(x => requestedLines.Select(line => line.ProductId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        if (products.Count != requestedLines.Length)
            throw new KeyNotFoundException("One or more products were not found.");

        var lines = requestedLines.Select(line => (products[line.ProductId], line.Quantity)).ToArray();
        var order = new Order(request.CustomerId, lines);
        db.Orders.Add(order);
        foreach (var (product, quantity) in lines)
            db.InventoryTransactions.Add(new InventoryTransaction(product.Id, InventoryTransactionType.Reservation,
                quantity, order.Id.ToString()));

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(order);
    }

    public async Task<OrderDto?> GetOrderAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        return order is null ? null : Map(order);
    }

    public async Task<IReadOnlyList<OrderStatusHistoryDto>?> GetOrderHistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Orders.AnyAsync(x => x.Id == id, ct)) return null;
        return await db.OrderStatusHistory.AsNoTracking().Where(x => x.OrderId == id)
            .OrderBy(x => x.ChangedAtUtc).ThenBy(x => x.Id)
            .Select(x => new OrderStatusHistoryDto(x.Status, x.ChangedAtUtc)).ToListAsync(ct);
    }

    public async Task<OrderDto?> ChangeOrderStatusAsync(Guid id, OrderStatus status, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (order is null) return null;

        var historyCount = order.History.Count;
        order.ChangeStatus(status);
        // New history rows have client-generated GUID keys, so explicitly mark
        // them as inserts when they are added to an already tracked order.
        db.OrderStatusHistory.AddRange(order.History.Skip(historyCount));
        if (status is OrderStatus.Shipped or OrderStatus.Cancelled)
            await ApplyReservationOutcomeAsync(order, status, ct);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(order);
    }

    private async Task ApplyReservationOutcomeAsync(Order order, OrderStatus status, CancellationToken ct)
    {
        var productIds = order.Items.Select(x => x.ProductId).Distinct().ToArray();
        var products = await db.Products.Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        foreach (var item in order.Items)
        {
            var product = products[item.ProductId];
            product.Release(item.Quantity);
            if (status == OrderStatus.Shipped)
                product.Adjust(product.StockOnHand - item.Quantity);

            var isShipment = status == OrderStatus.Shipped;
            db.InventoryTransactions.Add(new InventoryTransaction(product.Id,
                isShipment ? InventoryTransactionType.Adjustment : InventoryTransactionType.Release,
                isShipment ? -item.Quantity : item.Quantity, order.Id.ToString()));
        }
    }

    private static OrderDto Map(Order order)
    {
        var lines = order.Items.Select(item => new OrderLineDto(item.ProductId, item.Quantity,
            item.UnitPrice, item.LineTotal)).ToArray();
        return new OrderDto(order.Id, order.CustomerId, order.Status, lines.Sum(x => x.LineTotal),
            order.CreatedAtUtc, lines);
    }
}
