using OrderFlow.Domain;
namespace OrderFlow.Tests;

public sealed class DomainTests
{
    [Fact]
    public void Order_calculates_total_from_server_side_prices()
    {
        var customer = new Customer("Ada", "ada@example.com"); var product = new Product("SKU-1", "Widget", 12.50m, 10);
        var order = new Order(customer.Id, [(product, 2)]);
        Assert.Equal(25m, order.Total); Assert.Equal(8, product.AvailableStock);
    }
    [Fact]
    public void Order_rejects_invalid_status_transition()
    {
        var customer = new Customer("Ada", "ada@example.com"); var product = new Product("SKU-1", "Widget", 5m, 2); var order = new Order(customer.Id, [(product, 1)]);
        Assert.Throws<InvalidOperationException>(() => order.ChangeStatus(OrderStatus.Shipped));
    }
    [Fact]
    public void Order_allows_valid_flow_and_records_history()
    {
        var customer = new Customer("Ada", "ada@example.com"); var product = new Product("SKU-1", "Widget", 5m, 2); var order = new Order(customer.Id, [(product, 1)]);
        order.ChangeStatus(OrderStatus.Confirmed); order.ChangeStatus(OrderStatus.Processing); order.ChangeStatus(OrderStatus.Shipped);
        Assert.Equal(OrderStatus.Shipped, order.Status); Assert.Equal(4, order.History.Count); Assert.Equal(3, order.Version);
    }

    [Fact]
    public void Order_can_be_cancelled_before_shipment_but_not_after_delivery()
    {
        var customer = new Customer("Ada", "ada@example.com");
        var product = new Product("SKU-1", "Widget", 5m, 3);
        var order = new Order(customer.Id, [(product, 1)]);

        order.ChangeStatus(OrderStatus.Cancelled);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Throws<InvalidOperationException>(() => order.ChangeStatus(OrderStatus.Confirmed));

        var delivered = new Order(customer.Id, [(product, 1)]);
        delivered.ChangeStatus(OrderStatus.Confirmed);
        delivered.ChangeStatus(OrderStatus.Processing);
        delivered.ChangeStatus(OrderStatus.Shipped);
        delivered.ChangeStatus(OrderStatus.Delivered);
        Assert.Throws<InvalidOperationException>(() => delivered.ChangeStatus(OrderStatus.Cancelled));
    }
    [Fact]
    public void Product_rejects_reservation_beyond_available_stock()
    {
        var product = new Product("SKU-1", "Widget", 5m, 1);
        Assert.Throws<InvalidOperationException>(() => product.Reserve(2));
        Assert.Equal(1, product.AvailableStock);
    }
    [Fact]
    public void Product_rejects_negative_price_and_stock()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Product("SKU-1", "Widget", -1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Product("SKU-1", "Widget", 1m, -1));
    }

    [Fact]
    public void Customer_emails_and_product_skus_are_normalized_for_unique_matching()
    {
        var customer = new Customer("Ada", "ADA@EXAMPLE.COM");
        var product = new Product("sku-abc", "Widget", 2m);

        Assert.Equal("ada@example.com", customer.Email);
        Assert.Equal("SKU-ABC", product.Sku);
    }

    [Fact]
    public void Order_validation_does_not_partially_reserve_inventory()
    {
        var customer = new Customer("Ada", "ada@example.com");
        var available = new Product("SKU-1", "Available", 5m, 3);
        var insufficient = new Product("SKU-2", "Insufficient", 7m, 1);

        Assert.Throws<InvalidOperationException>(() => new Order(customer.Id,
            [(available, 2), (insufficient, 2)]));

        Assert.Equal(3, available.AvailableStock);
        Assert.Equal(1, insufficient.AvailableStock);
    }
}
