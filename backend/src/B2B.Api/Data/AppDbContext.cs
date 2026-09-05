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
    public DbSet<AgentAppointment> AgentAppointments => Set<AgentAppointment>();
    public DbSet<ModelSelection> ModelSelections => Set<ModelSelection>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<PortalMediaFile> PortalMediaFiles => Set<PortalMediaFile>();
    public DbSet<IntegrationSettings> IntegrationSettings => Set<IntegrationSettings>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<SalesRule> SalesRules => Set<SalesRule>();
    public DbSet<DocumentSource> DocumentSources => Set<DocumentSource>();
    public DbSet<CatalogVisibility> CatalogVisibilities => Set<CatalogVisibility>();

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
            cart.Property(c => c.SourceJson).HasColumnType("jsonb");
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

        modelBuilder.Entity<AgentAppointment>(appt =>
        {
            appt.ToTable("agent_appointments");
            appt.HasKey(a => a.Id);
            appt.Property(a => a.AgentExternalId).HasMaxLength(100);
            appt.Property(a => a.ClientId).HasMaxLength(100);
            appt.Property(a => a.ClientName).HasMaxLength(200);
            appt.Property(a => a.Title).HasMaxLength(200);
            appt.Property(a => a.Notes).HasMaxLength(2000);
            appt.Property(a => a.Kind).HasMaxLength(20);
            appt.Property(a => a.Status).HasMaxLength(20);
            // La agenda entra por (agente, fecha): el índice cubre ambas
            appt.HasIndex(a => new { a.AgentExternalId, a.Start });
        });

        modelBuilder.Entity<ModelSelection>(sel =>
        {
            sel.ToTable("model_selections");
            sel.HasKey(s => s.Id);
            sel.Property(s => s.AgentExternalId).HasMaxLength(100);
            sel.Property(s => s.Name).HasMaxLength(200);
            sel.Property(s => s.Status).HasMaxLength(20);
            sel.Property(s => s.ModelIdsJson).HasColumnType("jsonb");
            sel.Property(s => s.ClientIdsJson).HasColumnType("jsonb");
            sel.HasIndex(s => new { s.AgentExternalId, s.CreatedAt });
        });

        modelBuilder.Entity<Payment>(pay =>
        {
            pay.ToTable("payments");
            pay.HasKey(p => p.Id);
            pay.Property(p => p.ClientId).HasMaxLength(100);
            pay.Property(p => p.Kind).HasMaxLength(20);
            pay.Property(p => p.TargetId).HasMaxLength(100);
            pay.Property(p => p.Description).HasMaxLength(200);
            pay.Property(p => p.Currency).HasMaxLength(10);
            pay.Property(p => p.Provider).HasMaxLength(20);
            pay.Property(p => p.SessionId).HasMaxLength(255);
            pay.Property(p => p.Secret).HasMaxLength(64);
            pay.Property(p => p.Status).HasMaxLength(20);
            pay.Property(p => p.Amount).HasColumnType("numeric(12,2)");
            pay.HasIndex(p => new { p.ClientId, p.Kind, p.TargetId });
        });

        // ── Integración BC / Notificaciones (canales, transformers, conexiones) ──
        modelBuilder.Entity<IntegrationSettings>(s =>
        {
            s.ToTable("integration_settings");
            s.HasKey(x => x.Id);
            s.Property(x => x.BcBaseUrl).HasMaxLength(500);
            s.Property(x => x.BcTokenUrl).HasMaxLength(500);
            s.Property(x => x.BcClientId).HasMaxLength(200);
            s.Property(x => x.BcClientSecret).HasMaxLength(500);
            s.Property(x => x.BcScope).HasMaxLength(200);
            s.Property(x => x.ApiRestBaseUrl).HasMaxLength(500);
            s.Property(x => x.ApiRestHeadersJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<NotificationChannel>(c =>
        {
            c.ToTable("notification_channels");
            c.HasKey(x => x.Id);
            c.Property(x => x.EventKey).HasMaxLength(80);
            c.Property(x => x.ChannelType).HasMaxLength(30);
            c.Property(x => x.Endpoint).HasMaxLength(200);
            c.Property(x => x.ToVars).HasMaxLength(500);
            c.Property(x => x.CcVars).HasMaxLength(500);
            c.Property(x => x.BccVars).HasMaxLength(500);
            c.HasIndex(x => x.EventKey);
        });

        modelBuilder.Entity<NotificationLog>(l =>
        {
            l.ToTable("notification_logs");
            l.HasKey(x => x.Id);
            l.Property(x => x.EventKey).HasMaxLength(80);
            l.Property(x => x.EntityType).HasMaxLength(60);
            l.Property(x => x.EntityId).HasMaxLength(120);
            l.Property(x => x.ChannelType).HasMaxLength(30);
            l.Property(x => x.Status).HasMaxLength(20);
            l.Property(x => x.PayloadJson).HasColumnType("jsonb");
            l.HasIndex(x => x.CreatedAt);
            l.HasIndex(x => x.EventKey);
        });

        modelBuilder.Entity<SalesRule>(r =>
        {
            r.ToTable("sales_rules");
            r.HasKey(x => x.Id);
            r.Property(x => x.Name).HasMaxLength(160);
            r.Property(x => x.ConditionsJson).HasColumnType("jsonb");
            r.Property(x => x.ActionsJson).HasColumnType("jsonb");
            r.HasIndex(x => x.Priority);
        });

        modelBuilder.Entity<DocumentSource>(d =>
        {
            d.ToTable("document_sources");
            d.HasKey(x => x.DocType);
            d.Property(x => x.DocType).HasMaxLength(30);
            d.Property(x => x.SourceType).HasMaxLength(30);
            d.Property(x => x.Method).HasMaxLength(10);
            d.Property(x => x.Endpoint).HasMaxLength(500);
        });

        modelBuilder.Entity<CatalogVisibility>(v =>
        {
            v.ToTable("catalog_visibility");
            v.HasKey(x => x.Id);
            v.Property(x => x.SubjectType).HasMaxLength(20);
            v.Property(x => x.SubjectId).HasMaxLength(120);
            v.Property(x => x.Source).HasMaxLength(10);
            v.Property(x => x.RulesJson).HasColumnType("jsonb");
            v.HasIndex(x => new { x.SubjectType, x.SubjectId, x.Source }).IsUnique();
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
