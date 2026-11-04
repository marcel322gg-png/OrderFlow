using Microsoft.EntityFrameworkCore;
using OrderFlow.Application;
using OrderFlow.Domain;

namespace OrderFlow.Infrastructure;

public sealed class InventoryService(OrderFlowDbContext db) : IInventoryService
{
    public async Task RecordInventoryAsync(InventoryRequest request, CancellationToken ct)
    {
        if (request.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(request.Quantity));
        if (request.Type != InventoryTransactionType.Receipt)
            throw new ArgumentException("Manual inventory operations support receipts only.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == request.ProductId, ct)
            ?? throw new KeyNotFoundException("Product not found.");
        product.Receive(request.Quantity);
        db.InventoryTransactions.Add(new InventoryTransaction(product.Id, request.Type,
            request.Quantity, request.Reference));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<Page<InventoryTransactionDto>> GetInventoryHistoryAsync(Guid productId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.InventoryTransactions.AsNoTracking().Where(x => x.ProductId == productId);
        var count = await query.CountAsync(ct);
        var transactions = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new InventoryTransactionDto(x.Id, x.ProductId, x.Type, x.Quantity, x.Reference, x.CreatedAtUtc))
            .ToListAsync(ct);
        return new Page<InventoryTransactionDto>(transactions, page, pageSize, count);
    }
}
