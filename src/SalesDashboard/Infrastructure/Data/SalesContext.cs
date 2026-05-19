using ImprovedSalesForecast.SalesDashboard.EntityModels;
using Microsoft.EntityFrameworkCore;

namespace ImprovedSalesForecast.SalesDashboard.Infrastructure.Data;

public class SalesContext : DbContext
{
    public SalesContext(DbContextOptions<SalesContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<MonthlySale> MonthlySales => Set<MonthlySale>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("Products");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.StockCode).IsRequired().HasMaxLength(20);
            e.Property(p => p.Description).IsRequired().HasMaxLength(200);
            e.Property(p => p.UnitPrice).HasColumnType("decimal(10,4)");
        });

        modelBuilder.Entity<MonthlySale>(e =>
        {
            e.ToTable("MonthlySales");
            e.HasKey(ms => ms.Id);
            e.HasOne(ms => ms.Product)
             .WithMany()
             .HasForeignKey(ms => ms.ProductId);
            e.HasIndex(ms => new { ms.ProductId, ms.Year, ms.Month }).IsUnique();
        });
    }
}
