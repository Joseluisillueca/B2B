using Microsoft.EntityFrameworkCore;

namespace B2B.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<SyncDocument> SyncDocuments => Set<SyncDocument>();
    public DbSet<CatalogModel> CatalogModels => Set<CatalogModel>();
    public DbSet<CatalogProduct> CatalogProducts => Set<CatalogProduct>();

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
    }
}
