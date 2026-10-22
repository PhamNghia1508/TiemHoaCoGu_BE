using HoaSap.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HoaSap.DAL;

public class HoaSapDbContext : DbContext
{
    public HoaSapDbContext(DbContextOptions<HoaSapDbContext> options) : base(options) { }
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<ShoppingCartItem> ShoppingCartItems => Set<ShoppingCartItem>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryItem> DeliveryItems => Set<DeliveryItem>();
    public DbSet<Instructor> Instructors => Set<Instructor>();
    public DbSet<Workshop> Workshops => Set<Workshop>();
    public DbSet<WorkshopImage> WorkshopImages => Set<WorkshopImage>();
    public DbSet<WorkshopSession> WorkshopSessions => Set<WorkshopSession>();
    public DbSet<WorkshopBooking> WorkshopBookings => Set<WorkshopBooking>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Refund> Refunds => Set<Refund>();

    private void UpdateConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            var token = entry.Metadata.FindProperty("ConcurrencyToken");
            if (token is not null) entry.Property("ConcurrencyToken").CurrentValue = Guid.NewGuid();
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        UpdateConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        UpdateConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        foreach (var t in new[] { typeof(Customer), typeof(Address), typeof(Category), typeof(Product), typeof(ProductImage), typeof(Inventory), typeof(ShoppingCartItem), typeof(Voucher), typeof(Order), typeof(OrderItem), typeof(Delivery), typeof(DeliveryItem), typeof(Instructor), typeof(Workshop), typeof(WorkshopImage), typeof(WorkshopSession), typeof(WorkshopBooking), typeof(Payment), typeof(Refund) })
            b.Entity(t).Property<Guid>("Id").ValueGeneratedNever();

        b.Entity<Customer>().HasIndex(x => x.PhoneNumber);
        b.Entity<Customer>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Category>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Product>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Voucher>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Order>().HasIndex(x => x.OrderCode).IsUnique();
        b.Entity<Delivery>().HasIndex(x => x.DeliveryCode).IsUnique();
        b.Entity<Workshop>().HasIndex(x => x.Code).IsUnique();
        b.Entity<WorkshopBooking>().HasIndex(x => x.BookingCode).IsUnique();
        b.Entity<Payment>().HasIndex(x => x.TransactionCode).IsUnique();
        b.Entity<ProductImage>().HasIndex(x => new { x.ProductId, x.DisplayOrder });
        b.Entity<WorkshopImage>().HasIndex(x => new { x.WorkshopId, x.DisplayOrder });
        b.Entity<ShoppingCartItem>().HasIndex(x => new { x.CustomerId, x.ProductId }).IsUnique();
        b.Entity<Inventory>().HasIndex(x => x.ProductId).IsUnique();
        b.Entity<WorkshopSession>().HasIndex(x => new { x.WorkshopId, x.SessionDate, x.StartTime }).IsUnique();

        foreach (var t in new[] { typeof(Product), typeof(Voucher), typeof(Order), typeof(OrderItem), typeof(Delivery), typeof(Workshop), typeof(WorkshopBooking), typeof(Payment), typeof(Refund) })
        {
            foreach (var p in b.Entity(t).Metadata.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
            {
                p.SetPrecision(18);
                p.SetScale(2);
            }
        }
        b.Entity<Inventory>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        b.Entity<Order>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        b.Entity<WorkshopSession>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        b.Entity<WorkshopBooking>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();

        b.Entity<Address>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Product>().HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProductImage>().HasOne<Product>().WithMany(x => x.Images).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Inventory>().HasOne<Product>().WithOne(x => x.Inventory).HasForeignKey<Inventory>(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ShoppingCartItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ShoppingCartItem>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Order>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Order>().HasOne<Voucher>().WithMany().HasForeignKey(x => x.VoucherId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<OrderItem>().HasOne<Order>().WithMany(x => x.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OrderItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Delivery>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<DeliveryItem>().HasOne<Delivery>().WithMany(x => x.Items).HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DeliveryItem>().HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Workshop>().HasOne<Instructor>().WithMany().HasForeignKey(x => x.InstructorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkshopImage>().HasOne<Workshop>().WithMany(x => x.Images).HasForeignKey(x => x.WorkshopId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkshopSession>().HasOne<Workshop>().WithMany().HasForeignKey(x => x.WorkshopId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkshopBooking>().HasOne<WorkshopSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<WorkshopBooking>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Payment>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Payment>().HasOne<WorkshopBooking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Refund>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Refund>().HasOne<WorkshopBooking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Product>().ToTable(t => t.HasCheckConstraint("CK_Product_Price", "Price >= 0 AND (OriginalPrice IS NULL OR OriginalPrice >= 0)"));
        b.Entity<Inventory>().ToTable(t => t.HasCheckConstraint("CK_Inventory_Quantities", "QuantityAvailable >= 0 AND QuantityReserved >= 0"));
        b.Entity<Order>().ToTable(t => t.HasCheckConstraint("CK_Order_Amounts", "SubTotal >= 0 AND DiscountAmount >= 0 AND ShippingFee >= 0 AND TotalAmount >= 0"));
        b.Entity<OrderItem>().ToTable(t => t.HasCheckConstraint("CK_OrderItem_Quantities", "Quantity > 0 AND UnitPrice >= 0 AND FinalPrice >= 0"));
        b.Entity<WorkshopSession>().ToTable(t => t.HasCheckConstraint("CK_Session_Capacity", "Capacity > 0 AND BookedSlots >= 0 AND BookedSlots <= Capacity"));
        b.Entity<WorkshopBooking>().ToTable(t => t.HasCheckConstraint("CK_Booking_Tickets", "TicketCount > 0 AND TotalAmount >= 0 AND DepositAmount >= 0 AND RemainingAmount >= 0"));
        b.Entity<Payment>().ToTable(t => t.HasCheckConstraint("CK_Payment_Amount", "Amount >= 0 AND ((OrderId IS NOT NULL AND BookingId IS NULL) OR (OrderId IS NULL AND BookingId IS NOT NULL))"));
        b.Entity<Refund>().ToTable(t => t.HasCheckConstraint("CK_Refund_Target", "RefundAmount >= 0 AND ((OrderId IS NOT NULL AND BookingId IS NULL) OR (OrderId IS NULL AND BookingId IS NOT NULL))"));
    }
}
