using Microsoft.EntityFrameworkCore;
using OrderFlow.Application;
using OrderFlow.Domain;

namespace OrderFlow.Infrastructure;

public sealed class CustomerService(OrderFlowDbContext db) : ICustomerService
{
    public async Task<Page<CustomerDto>> GetCustomersAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search) || x.Email.Contains(search));

        var count = await query.CountAsync(ct);
        var customers = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new CustomerDto(x.Id, x.Name, x.Email, x.Phone, x.CreatedAtUtc))
            .ToListAsync(ct);
        return new Page<CustomerDto>(customers, page, pageSize, count);
    }

    public Task<CustomerDto?> GetCustomerAsync(Guid id, CancellationToken ct) =>
        db.Customers.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new CustomerDto(x.Id, x.Name, x.Email, x.Phone, x.CreatedAtUtc))
            .SingleOrDefaultAsync(ct);

    public async Task<CustomerDto> CreateCustomerAsync(string name, string email, string? phone, CancellationToken ct)
    {
        var customer = new Customer(name, email, phone);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return Map(customer);
    }

    public async Task<CustomerDto?> UpdateCustomerAsync(Guid id, string name, string email, string? phone, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([id], ct);
        if (customer is null) return null;

        customer.Update(name, email, phone);
        await db.SaveChangesAsync(ct);
        return Map(customer);
    }

    public async Task<bool> DeleteCustomerAsync(Guid id, CancellationToken ct)
    {
        var customer = await db.Customers.FindAsync([id], ct);
        if (customer is null) return false;
        if (await db.Orders.AnyAsync(x => x.CustomerId == id, ct))
            throw new InvalidOperationException("Customer has orders and cannot be deleted.");

        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static CustomerDto Map(Customer customer) =>
        new(customer.Id, customer.Name, customer.Email, customer.Phone, customer.CreatedAtUtc);
}
