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
    public DbSet<PortalContent> PortalContents => Set<PortalContent>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<PortalFavorite> PortalFavorites => Set<PortalFavorite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(user =>
        {
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.ClientExternalId).HasMaxLength(100);
            user.Property(u => u.ClientNumber).HasMaxLength(50);
            user.Property(u => u.Role).HasMaxLength(50);
            user.Property(u => u.Culture).HasMaxLength(10);
            user.HasIndex(u => u.ClientExternalId);
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

        // El plan (§3) nombra esta tabla portal_content; se respeta el nombre para
        // que el CMS y la documentación hablen del mismo objeto.
        modelBuilder.Entity<PortalContent>(content =>
        {
            content.ToTable("portal_content");
            content.HasKey(c => new { c.Key, c.Locale });
            content.Property(c => c.Key).HasMaxLength(100);
            content.Property(c => c.Locale).HasMaxLength(10);
            content.Property(c => c.Json).HasColumnType("jsonb");
            content.Property(c => c.UpdatedBy).HasMaxLength(320);
        });

        // El plan (§4, Fase 2) nombra esta tabla carts
        modelBuilder.Entity<Cart>(cart =>
        {
            cart.ToTable("carts");
            cart.HasKey(c => c.Id);
            cart.Property(c => c.ClientId).HasMaxLength(100);
            cart.Property(c => c.Name).HasMaxLength(120);
            cart.Property(c => c.ServiceWindowId).HasMaxLength(50);
            cart.Property(c => c.Status).HasMaxLength(20);
            cart.Property(c => c.Reference).HasMaxLength(120);
            cart.Property(c => c.LinesJson).HasColumnType("jsonb");
            // El listado siempre entra por (cliente, estado): el índice cubre las dos
            cart.HasIndex(c => new { c.ClientId, c.Status });
            cart.HasIndex(c => c.UserId);
        });

        modelBuilder.Entity<PortalFavorite>(favorite =>
        {
            favorite.ToTable("portal_favorites");
            favorite.HasKey(f => new { f.UserId, f.ModelId });
            favorite.Property(f => f.ModelId).HasMaxLength(100);
        });
    }
}
