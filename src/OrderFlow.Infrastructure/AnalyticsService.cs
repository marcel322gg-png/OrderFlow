using Microsoft.EntityFrameworkCore;
using OrderFlow.Application;
using OrderFlow.Domain;

namespace OrderFlow.Infrastructure;

public sealed class AnalyticsService(OrderFlowDbContext db) : IAnalyticsService
{
    public async Task<AnalyticsOverview> GetAnalyticsAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var activeOrders = ApplyPeriod(db.Orders.AsNoTracking()
            .Where(x => x.Status != OrderStatus.Cancelled), from, to);
        var orderIds = activeOrders.Select(x => x.Id);
        var orderCount = await activeOrders.CountAsync(ct);
        var sales = await db.OrderItems.AsNoTracking().Where(x => orderIds.Contains(x.OrderId))
            .Select(x => (decimal?)(x.Quantity * x.UnitPrice)).SumAsync(ct) ?? 0m;
        var popularRows = await db.OrderItems.AsNoTracking().Where(x => orderIds.Contains(x.OrderId))
            .GroupBy(x => x.ProductId)
            .Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .OrderByDescending(x => x.Quantity).ThenBy(x => x.ProductId).Take(5).ToListAsync(ct);
        var productIds = popularRows.Select(x => x.ProductId).ToArray();
        var products = await db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var popular = popularRows.Select(x => new PopularProductDto(x.ProductId,
            products[x.ProductId].Sku, products[x.ProductId].Name, x.Quantity)).ToArray();
        var cancelled = ApplyPeriod(db.Orders.AsNoTracking()
            .Where(x => x.Status == OrderStatus.Cancelled), from, to);
        var cancelledCount = await cancelled.CountAsync(ct);
        var averageOrderValue = orderCount == 0 ? 0m : sales / orderCount;
        return new AnalyticsOverview(orderCount, sales, averageOrderValue, cancelledCount, popular);
    }

    private static IQueryable<Order> ApplyPeriod(IQueryable<Order> query, DateTime? from, DateTime? to)
    {
        if (from.HasValue) query = query.Where(x => x.CreatedAtUtc >= AsUtc(from.Value));
        if (to.HasValue) query = query.Where(x => x.CreatedAtUtc < AsUtc(to.Value));
        return query;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
