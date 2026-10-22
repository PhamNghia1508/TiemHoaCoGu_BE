using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HoaSap.DAL;
using HoaSap.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HoaSap.Tests;

[Collection("api")]
public sealed class CmsOperationsTests : ApiTestBase
{
    private readonly ApiFixture fixture;
    public CmsOperationsTests(ApiFixture fixture) : base(fixture) => this.fixture = fixture;
    private async Task<HttpClient> AdminAsync() => await AuthenticatedAsync(await AdminTokenAsync());

    [Fact]
    public async Task Operations_endpoints_require_admin_role()
    {
        var response = await Client.GetAsync("/api/management/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_paging_and_date_ranges_are_rejected()
    {
        var client = await AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/management/orders?page=0&pageSize=20")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/management/vouchers?page=1&pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/management/dashboard?from=2025-01-02T00:00:00Z&to=2025-01-01T00:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/management/dashboard?from=2024-01-01T00:00:00Z&to=2026-01-01T00:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/management/orders?page=2147483647&pageSize=100")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/management/dashboard?from=2025-01-01T00:00:00Z")).StatusCode);
    }

    [Fact]
    public async Task Voucher_edit_cannot_lower_usage_limit_below_used_count_and_active_patch_is_no_content()
    {
        var client = await AdminAsync();
        var code = "OPS-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var create = await client.PostAsync("/api/management/vouchers", Json(new
        {
            code, description = "operations test", discountType = "FixedAmount", discountValue = 10,
            minOrderAmount = 0, maxDiscountAmount = (decimal?)null,
            startDate = DateTime.UtcNow.AddHours(-1), endDate = DateTime.UtcNow.AddDays(1), usageLimit = 2
        }));
        create.EnsureSuccessStatusCode();
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetGuid();
        var invalid = await client.PutAsync($"/api/management/vouchers/{id}", Json(new
        {
            code, description = "operations test", discountType = "FixedAmount", discountValue = 10,
            minOrderAmount = 0, maxDiscountAmount = (decimal?)null,
            startDate = DateTime.UtcNow.AddHours(-1), endDate = DateTime.UtcNow.AddDays(1), usageLimit = 0
        }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsync($"/api/management/vouchers/{id}/active?value=false", null)).StatusCode);
    }

    [Fact]
    public async Task Orders_csv_is_bounded_and_downloadable()
    {
        var client = await AdminAsync();
        var response = await client.GetAsync("/api/management/reports/orders.csv?page=1&pageSize=20");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("\uFEFForderCode,createdAt,status,paymentStatus", csv);
    }

    [Fact]
    public async Task Dashboard_uses_global_catalog_counts_and_paid_processed_aggregates()
    {
        var now = new DateTime(2030, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var customer = new Customer { FullName = "Operations customer", Email = $"ops-{Guid.NewGuid():N}@example.test", PhoneNumber = "09" + Random.Shared.Next(100000000, 999999999), PasswordHash = "x" };
        var guest = new Customer { FullName = "Operations guest", Email = $"guest-{Guid.NewGuid():N}@example.test", PhoneNumber = "08" + Random.Shared.Next(100000000, 999999999), IsGuest = true };
        var category = new Category { Code = $"ops-{Guid.NewGuid():N}"[..20], Name = "Operations" };
        var product = new Product { CategoryId = category.Id, Code = $"ops-{Guid.NewGuid():N}", Name = "Operations product", Price = 12 };
        var otherProduct = new Product { CategoryId = category.Id, Code = $"ops-{Guid.NewGuid():N}", Name = "Catalog-only product", Price = 15 };
        var valid = new Order { CustomerId = customer.Id, OrderCode = "OPS-VALID-" + Guid.NewGuid().ToString("N"), TotalAmount = 24, SubTotal = 24, OrderStatus = "Confirmed", PaymentStatus = "Paid", CreatedAt = now.AddHours(-2), ShippingAddress = "safe" };
        valid.Items.Add(new OrderItem { ProductId = product.Id, Quantity = 2, UnitPrice = 12, FinalPrice = 12 });
        var cancelled = new Order { CustomerId = customer.Id, OrderCode = "OPS-CANCELLED-" + Guid.NewGuid().ToString("N"), TotalAmount = 999, SubTotal = 999, OrderStatus = "Cancelled", PaymentStatus = "Paid", CreatedAt = now.AddHours(-1), ShippingAddress = "safe" };
        cancelled.Items.Add(new OrderItem { ProductId = product.Id, Quantity = 99, UnitPrice = 10, FinalPrice = 10 });
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
            db.AddRange(customer, guest, category, product, otherProduct, valid, cancelled);
            db.Payments.Add(new Payment { OrderId = valid.Id, TransactionCode = Guid.NewGuid().ToString("N"), Amount = 20, PaymentStatus = "Paid" });
            db.Payments.Add(new Payment { OrderId = cancelled.Id, TransactionCode = Guid.NewGuid().ToString("N"), Amount = 999, PaymentStatus = "Paid" });
            db.Refunds.Add(new Refund { OrderId = valid.Id, RefundAmount = 3, RefundStatus = "Processed" });
            db.Inventories.Add(new Inventory { ProductId = product.Id, QuantityAvailable = 2 });
            db.Inventories.Add(new Inventory { ProductId = otherProduct.Id, QuantityAvailable = 10 });
            await db.SaveChangesAsync();
            var expectedCustomers = await db.Customers.CountAsync(x => !x.IsGuest);
            var expectedProducts = await db.Products.CountAsync();
            var client = await AdminAsync();
            using var response = await client.GetAsync($"/api/management/dashboard?from={now.AddDays(-1):O}&to={now.AddHours(1):O}");
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            Assert.Equal(24, root.GetProperty("revenue").GetDecimal());
            Assert.Equal(1019, root.GetProperty("paidAmount").GetDecimal());
            Assert.Equal(3, root.GetProperty("refundAmount").GetDecimal());
            Assert.Equal(expectedCustomers, root.GetProperty("customerCount").GetInt32());
            Assert.Equal(expectedProducts, root.GetProperty("productCount").GetInt32());
            Assert.Contains(root.GetProperty("topProducts").EnumerateArray(), x => x.GetProperty("name").GetString() == product.Name && x.GetProperty("quantity").GetInt32() == 2);
        }
    }

    [Fact]
    public async Task Csv_neutralizes_formula_values_after_leading_whitespace()
    {
        var order = new Order { OrderCode = "\t=2+2", TotalAmount = 1, SubTotal = 1, ShippingAddress = "safe", CreatedAt = DateTime.UtcNow };
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoaSapDbContext>();
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }
        var response = await (await AdminAsync()).GetAsync($"/api/management/reports/orders.csv?search={Uri.EscapeDataString(order.OrderCode)}");
        response.EnsureSuccessStatusCode();
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"'\t=2+2\"", csv);
    }
}
