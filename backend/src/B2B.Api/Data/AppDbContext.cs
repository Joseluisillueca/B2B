using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<SyncDocument> SyncDocuments => Set<SyncDocument>();
    public DbSet<CatalogModel> CatalogModels => Set<CatalogModel>();
    public DbSet<CatalogProduct> CatalogProducts => Set<CatalogProduct>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<ServiceWindow> ServiceWindows => Set<ServiceWindow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(user =>
        {
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.Email).HasMaxLength(320);
        });

        modelBuilder.Entity<SyncDocument>(doc =>
        {
            doc.HasIndex(d => new { d.EntityType, d.ExternalId }).IsUnique();
            doc.Property(d => d.EntityType).HasMaxLength(50);
            doc.Property(d => d.ExternalId).HasMaxLength(100);
            doc.Property(d => d.ParentId).HasMaxLength(100);
            doc.Property(d => d.Payload).HasColumnType("jsonb");
        });

        modelBuilder.Entity<CatalogModel>(model =>
        {
            model.HasKey(m => m.ExternalId);
            model.Property(m => m.ExternalId).HasMaxLength(100);
            model.HasIndex(m => m.FamilyId);
            model.Property(m => m.NameTranslationsJson).HasColumnType("jsonb");
            model.Property(m => m.AttributesJson).HasColumnType("jsonb");
            model.Property(m => m.ProductSegmentsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<CatalogProduct>(product =>
        {
            product.HasKey(p => p.ExternalId);
            product.Property(p => p.ExternalId).HasMaxLength(100);
            product.HasIndex(p => p.ModelExternalId);
            product.HasIndex(p => p.Sku);
            product.Property(p => p.AttributesJson).HasColumnType("jsonb");
            product.Property(p => p.BundleJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<StockLevel>(stock =>
        {
            stock.HasIndex(s => new { s.ProductExternalId, s.ServiceWindowKey }).IsUnique();
            stock.Property(s => s.ProductExternalId).HasMaxLength(100);
            stock.Property(s => s.ServiceWindowId).HasMaxLength(50);
            stock.Property(s => s.ServiceWindowKey).HasMaxLength(50);
        });

        modelBuilder.Entity<Offer>(offer =>
        {
            offer.HasKey(o => o.ExternalId);
            offer.Property(o => o.ExternalId).HasMaxLength(100);
            offer.HasIndex(o => o.ModelId);
            offer.HasIndex(o => o.ProductId);
            offer.Property(o => o.PayloadJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ServiceWindow>(window =>
        {
            window.HasKey(w => w.ExternalId);
            window.Property(w => w.ExternalId).HasMaxLength(50);
            window.Property(w => w.PayloadJson).HasColumnType("jsonb");
        });
    }
}
