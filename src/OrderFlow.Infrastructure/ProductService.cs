using Microsoft.EntityFrameworkCore;
using OrderFlow.Application;
using OrderFlow.Domain;

namespace OrderFlow.Infrastructure;

public sealed class ProductService(OrderFlowDbContext db) : IProductService
{
    public async Task<Page<ProductDto>> GetProductsAsync(string? search, string? sort, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search) || x.Sku.Contains(search));

        var ordered = sort?.ToLowerInvariant() switch
        {
            "price" => query.OrderBy(x => x.Price),
            "price_desc" => query.OrderByDescending(x => x.Price),
            "sku" => query.OrderBy(x => x.Sku),
            _ => query.OrderBy(x => x.Name)
        };
        var count = await query.CountAsync(ct);
        var products = await ordered.ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ProductDto(x.Id, x.Sku, x.Name, x.Description, x.Price,
                x.StockOnHand, x.StockOnHand - x.ReservedStock, x.IsAvailable))
            .ToListAsync(ct);
        return new Page<ProductDto>(products, page, pageSize, count);
    }

    public async Task<ProductDto> CreateProductAsync(string sku, string name, string? description, decimal price, int stock, CancellationToken ct)
    {
        var product = new Product(sku, name, price, stock, description);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return Map(product);
    }

    public async Task<ProductDto?> GetProductAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return product is null ? null : Map(product);
    }

    public async Task<ProductDto?> UpdateProductAsync(Guid id, string name, string? description, decimal price, bool available, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([id], ct);
        if (product is null) return null;

        product.Update(name, description, price, available);
        await db.SaveChangesAsync(ct);
        return Map(product);
    }

    public async Task<bool> DeleteProductAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([id], ct);
        if (product is null) return false;
        if (await db.OrderItems.AnyAsync(x => x.ProductId == id, ct))
            throw new InvalidOperationException("Product has order history and cannot be deleted.");

        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static ProductDto Map(Product product) =>
        new(product.Id, product.Sku, product.Name, product.Description, product.Price,
            product.StockOnHand, product.AvailableStock, product.IsAvailable);
}
