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
    public DbSet<PortalUserPrefs> PortalUserPrefs => Set<PortalUserPrefs>();
    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();
    public DbSet<BusinessChangeRequest> BusinessChangeRequests => Set<BusinessChangeRequest>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<ActivationToken> ActivationTokens => Set<ActivationToken>();
    public DbSet<SentEmail> SentEmails => Set<SentEmail>();
    public DbSet<ClientRegistrationRequest> ClientRegistrationRequests => Set<ClientRegistrationRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(user =>
        {
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.ClientExternalId).HasMaxLength(100);
            user.Property(u => u.ClientNumber).HasMaxLength(50);
            user.Property(u => u.AgentExternalId).HasMaxLength(100);
            user.Property(u => u.Role).HasMaxLength(50);
            user.Property(u => u.Culture).HasMaxLength(10);
            user.Property(u => u.Name).HasMaxLength(200);
            user.HasIndex(u => u.ClientExternalId);
            user.HasIndex(u => u.AgentExternalId);
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

        // El plan (§4, Fase 4) nombra esta tabla portal_user_prefs
        modelBuilder.Entity<PortalUserPrefs>(prefs =>
        {
            prefs.ToTable("portal_user_prefs");
            prefs.HasKey(p => p.UserId);
            prefs.Property(p => p.ShowPrices).HasMaxLength(10);
            prefs.Property(p => p.ListDesktop).HasMaxLength(10);
            prefs.Property(p => p.ListMobile).HasMaxLength(10);
            prefs.Property(p => p.ShippingAddressId).HasMaxLength(100);
        });

        // El plan (§4, Fase 4) nombra esta tabla return_requests
        modelBuilder.Entity<ReturnRequest>(request =>
        {
            request.ToTable("return_requests");
            request.HasKey(r => r.Id);
            request.Property(r => r.Code).HasMaxLength(30);
            request.Property(r => r.ClientId).HasMaxLength(100);
            request.Property(r => r.Type).HasMaxLength(20);
            request.Property(r => r.PickupSlot).HasMaxLength(20);
            request.Property(r => r.Status).HasMaxLength(20);
            request.Property(r => r.Resolution).HasMaxLength(500);
            request.Property(r => r.PhotoUrl).HasMaxLength(500);
            request.Property(r => r.Reference).HasMaxLength(120);
            request.Property(r => r.Notes).HasMaxLength(1000);
            // El listado siempre entra por (cliente, estado): el índice cubre las dos
            request.HasIndex(r => new { r.ClientId, r.Status });
            request.HasIndex(r => r.Code).IsUnique();
        });

        modelBuilder.Entity<BusinessChangeRequest>(request =>
        {
            request.ToTable("business_change_requests");
            request.HasKey(r => r.Id);
            request.Property(r => r.ClientId).HasMaxLength(100);
            request.Property(r => r.Section).HasMaxLength(20);
            request.Property(r => r.Status).HasMaxLength(20);
            request.Property(r => r.ChangesJson).HasColumnType("jsonb");
            request.HasIndex(r => new { r.ClientId, r.Status });
        });

        modelBuilder.Entity<ActivationToken>(token =>
        {
            token.ToTable("activation_tokens");
            token.HasKey(t => t.Id);
            token.Property(t => t.TokenHash).HasMaxLength(64);
            token.Property(t => t.Purpose).HasMaxLength(20);
            // El canje busca por hash: índice único para encontrarlo de un golpe
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => t.UserId);
        });

        modelBuilder.Entity<SentEmail>(mail =>
        {
            mail.ToTable("sent_emails");
            mail.HasKey(m => m.Id);
            mail.Property(m => m.To).HasMaxLength(320);
            mail.Property(m => m.Subject).HasMaxLength(300);
            mail.Property(m => m.Transport).HasMaxLength(10);
            mail.HasIndex(m => m.To);
            mail.HasIndex(m => m.CreatedAt);
        });

        modelBuilder.Entity<ClientRegistrationRequest>(request =>
        {
            request.ToTable("client_registration_requests");
            request.HasKey(r => r.Id);
            request.Property(r => r.AgentExternalId).HasMaxLength(100);
            request.Property(r => r.Name).HasMaxLength(200);
            request.Property(r => r.Email).HasMaxLength(320);
            request.Property(r => r.Status).HasMaxLength(20);
            request.Property(r => r.PayloadJson).HasColumnType("jsonb");
            // La bandeja entra por (agente, estado): el índice cubre ambas
            request.HasIndex(r => new { r.AgentExternalId, r.Status });
            request.HasIndex(r => r.CreatedAt);
        });

        modelBuilder.Entity<ContactMessage>(message =>
        {
            message.ToTable("contact_messages");
            message.HasKey(m => m.Id);
            message.Property(m => m.ClientId).HasMaxLength(100);
            message.Property(m => m.Subject).HasMaxLength(200);
            message.Property(m => m.Email).HasMaxLength(320);
            message.Property(m => m.Message).HasMaxLength(4000);
            message.Property(m => m.AttachmentName).HasMaxLength(260);
            message.Property(m => m.AttachmentPath).HasMaxLength(500);
            message.Property(m => m.DeliveredTo).HasMaxLength(320);
            message.HasIndex(m => m.ClientId);
        });
    }
}
