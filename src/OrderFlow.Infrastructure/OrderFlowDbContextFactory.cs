using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace OrderFlow.Infrastructure;

public sealed class OrderFlowDbContextFactory : IDesignTimeDbContextFactory<OrderFlowDbContext>
{
    public OrderFlowDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=orderflow;Username=orderflow;Password=design-time-only";
        var options = new DbContextOptionsBuilder<OrderFlowDbContext>().UseNpgsql(connectionString).Options;
        return new OrderFlowDbContext(options);
    }
}
