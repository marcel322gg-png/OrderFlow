using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Domain;
namespace OrderFlow.Infrastructure;

public sealed class IdentityUserEntity : IdentityUser<Guid> { }
public sealed class OrderFlowDbContext(DbContextOptions<OrderFlowDbContext> options) : IdentityDbContext<IdentityUserEntity, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Customer>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Customer>().HasIndex(x => x.Name);
        b.Entity<Product>().HasIndex(x => x.Sku).IsUnique();
        b.Entity<Product>().HasIndex(x => x.Name);
        b.Entity<Product>().HasIndex(x => x.Price);
        b.Entity<Product>().Property(x => x.Price).HasPrecision(18, 2);
        b.Entity<Product>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Order>().Property(x => x.Status).HasConversion<string>();
        b.Entity<Order>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Order>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Order>().HasIndex(x => new { x.CustomerId, x.CreatedAtUtc });
        b.Entity<Order>().HasIndex(x => new { x.Status, x.CreatedAtUtc });
        b.Entity<Order>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId);
        b.Entity<Order>().HasMany(x => x.History).WithOne().HasForeignKey(x => x.OrderId);
        b.Entity<OrderItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<OrderItem>().Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.Entity<OrderItem>().HasIndex(x => new { x.OrderId, x.ProductId });
        b.Entity<OrderItem>().Ignore(x => x.LineTotal); b.Entity<Order>().Ignore(x => x.Total); b.Entity<Product>().Ignore(x => x.AvailableStock);
        b.Entity<Order>().Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Entity<Order>().Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Entity<InventoryTransaction>().Property(x => x.Type).HasConversion<string>();
        b.Entity<InventoryTransaction>().HasIndex(x => new { x.ProductId, x.CreatedAtUtc });
    }
}
