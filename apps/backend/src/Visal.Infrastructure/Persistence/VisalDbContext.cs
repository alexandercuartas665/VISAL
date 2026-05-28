using System.Reflection;
using Visal.Application.Common;
using Visal.Domain.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Visal.Infrastructure.Persistence;

public class VisalDbContext : DbContext, IApplicationDbContext, IDataProtectionKeyContext
{
    private readonly ITenantContext _tenantContext;

    public VisalDbContext(DbContextOptions<VisalDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    // Globales (administradas por Super Admin / plataforma)
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<SaasPlan> SaasPlans => Set<SaasPlan>();
    public DbSet<SaasPlanLimit> SaasPlanLimits => Set<SaasPlanLimit>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<TenantPayment> TenantPayments => Set<TenantPayment>();
    public DbSet<WompiMasterConfig> WompiMasterConfigs => Set<WompiMasterConfig>();
    public DbSet<WompiWebhookEvent> WompiWebhookEvents => Set<WompiWebhookEvent>();
    public DbSet<EvolutionMasterConfig> EvolutionMasterConfigs => Set<EvolutionMasterConfig>();
    public DbSet<AiProviderConfig> AiProviderConfigs => Set<AiProviderConfig>();
    public DbSet<PlatformBranding> PlatformBrandings => Set<PlatformBranding>();
    public DbSet<EmailConfig> EmailConfigs => Set<EmailConfig>();
    public DbSet<GoogleAuthConfig> GoogleAuthConfigs => Set<GoogleAuthConfig>();
    public DbSet<TenantApiConfig> TenantApiConfigs => Set<TenantApiConfig>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<SuperAdminAuditLog> SuperAdminAuditLogs => Set<SuperAdminAuditLog>();

    // Llaves de Data Protection compartidas entre apps (Api, SuperAdmin, Workers) para
    // que los secretos cifrados (Wompi, Evolution) se descifren en cualquiera de ellas.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    // Tenant-scoped (con filtro global de consulta)
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<TenantConfiguration> TenantConfigurations => Set<TenantConfiguration>();
    public DbSet<TenantEvolutionConfig> TenantEvolutionConfigs => Set<TenantEvolutionConfig>();
    public DbSet<WhatsAppLine> WhatsAppLines => Set<WhatsAppLine>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<PipelineFieldDefinition> PipelineFieldDefinitions => Set<PipelineFieldDefinition>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadActivity> LeadActivities => Set<LeadActivity>();
    public DbSet<LeadNote> LeadNotes => Set<LeadNote>();
    public DbSet<LeadFile> LeadFiles => Set<LeadFile>();
    public DbSet<FollowUpTask> FollowUpTasks => Set<FollowUpTask>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<QuoteTemplate> QuoteTemplates => Set<QuoteTemplate>();
    public DbSet<TemplateAsset> TemplateAssets => Set<TemplateAsset>();
    public DbSet<AiAgent> AiAgents => Set<AiAgent>();
    public DbSet<AiAgentResource> AiAgentResources => Set<AiAgentResource>();
    public DbSet<AiAgentPrompt> AiAgentPrompts => Set<AiAgentPrompt>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();
    public DbSet<Aseguradora> Aseguradoras => Set<Aseguradora>();
    public DbSet<ContratoAseguradora> ContratosAseguradora => Set<ContratoAseguradora>();
    public DbSet<ServicioContrato> ServiciosContrato => Set<ServicioContrato>();
    public DbSet<TipoProfesional> TiposProfesional => Set<TipoProfesional>();
    public DbSet<SubCategoriaProfesional> SubCategoriasProfesional => Set<SubCategoriaProfesional>();
    public DbSet<Profesional> Profesionales => Set<Profesional>();
    public DbSet<ProfesionalSubCategoria> ProfesionalSubCategorias => Set<ProfesionalSubCategoria>();
    public DbSet<ProfesionalAgencia> ProfesionalAgencias => Set<ProfesionalAgencia>();
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<RolPermiso> RolPermisos => Set<RolPermiso>();
    public DbSet<Sucursal> Sucursales => Set<Sucursal>();
    public DbSet<TenantUserSucursal> TenantUserSucursales => Set<TenantUserSucursal>();
    public DbSet<Paciente> Pacientes => Set<Paciente>();
    public DbSet<CatalogoPaciente> CatalogosPaciente => Set<CatalogoPaciente>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Todos los enums se persisten como texto (legibles y estables ante reordenamientos).
        configurationBuilder.Properties<TenantStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<TenantKind>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<SubscriptionStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<BillingFrequency>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<PlatformRole>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<LimitEnforcementMode>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AuditActorType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<TenantRole>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<PlatformUserStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<LeadVisibility>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WhatsAppLineStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<LeadStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<FollowUpTaskStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<MessageDirection>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<MessageMediaType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AiProvider>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AgentResourceType>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AutomationTrigger>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<AutomationAction>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WompiEnvironment>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WompiIntegrationStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<EvolutionIntegrationStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<WebhookProcessingStatus>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<PipelineFieldType>().HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureEntities(modelBuilder);
        ApplyTenantQueryFilters(modelBuilder);
    }

    private static void ConfigureEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.LegalName).HasMaxLength(250);
            b.Property(x => x.TaxId).HasMaxLength(80);
            b.Property(x => x.Country).HasMaxLength(80);
            b.Property(x => x.Currency).HasMaxLength(10);
            b.Property(x => x.LogoUrl).HasMaxLength(500);
        });

        modelBuilder.Entity<SaasPlan>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(10);
            b.Property(x => x.MonthlyPrice).HasPrecision(12, 2);
            b.Property(x => x.YearlyPrice).HasPrecision(12, 2);
            b.HasMany(x => x.Limits)
                .WithOne(x => x.Plan!)
                .HasForeignKey(x => x.PlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SaasPlanLimit>(b =>
        {
            b.Property(x => x.LimitKey).HasMaxLength(150).IsRequired();
            b.Property(x => x.LimitUnit).HasMaxLength(50);
            b.HasIndex(x => new { x.PlanId, x.LimitKey }).IsUnique();
        });

        modelBuilder.Entity<TenantSubscription>(b =>
        {
            b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.PaymentMethodLabel).HasMaxLength(80);
            b.HasIndex(x => x.TenantId);
        });

        modelBuilder.Entity<TenantPayment>(b =>
        {
            b.Property(x => x.Amount).HasPrecision(12, 2);
            b.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            b.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            b.Property(x => x.ProviderReference).HasMaxLength(200);
            b.HasOne(x => x.Subscription).WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.TenantId);
        });

        modelBuilder.Entity<PlatformUser>(b =>
        {
            b.Property(x => x.Email).HasMaxLength(256).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(200);
            b.Property(x => x.GoogleSubject).HasMaxLength(255);
            b.Property(x => x.AuthProvider).HasMaxLength(50).IsRequired();
            b.Property(x => x.Documento).HasMaxLength(40);
            b.HasIndex(x => x.Email).IsUnique();
            b.HasIndex(x => x.GoogleSubject).IsUnique().HasFilter("google_subject IS NOT NULL");
            b.HasIndex(x => x.Documento).IsUnique().HasFilter("documento IS NOT NULL");
        });

        modelBuilder.Entity<SuperAdminAuditLog>(b =>
        {
            b.Property(x => x.ActionName).HasMaxLength(200).IsRequired();
            b.Property(x => x.EntityName).HasMaxLength(150).IsRequired();
            b.Property(x => x.IpAddress).HasMaxLength(80);
            b.Property(x => x.PreviousValue).HasColumnType("jsonb");
            b.Property(x => x.NewValue).HasColumnType("jsonb");
            b.HasIndex(x => x.TenantId);
            b.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<PlatformBranding>(b =>
        {
            b.Property(x => x.PlatformName).HasMaxLength(120).IsRequired();
            b.Property(x => x.Tagline).HasMaxLength(160);
            b.Property(x => x.LoginLogoUrl).HasMaxLength(500);
            b.Property(x => x.LoginHeadline).HasMaxLength(160);
            b.Property(x => x.LoginSubtext).HasMaxLength(600);
        });

        modelBuilder.Entity<EmailConfig>(b =>
        {
            b.Property(x => x.SmtpHost).HasMaxLength(200);
            b.Property(x => x.SmtpUser).HasMaxLength(200);
            b.Property(x => x.FromEmail).HasMaxLength(200);
            b.Property(x => x.FromName).HasMaxLength(160);
        });

        modelBuilder.Entity<PasswordResetToken>(b =>
        {
            b.Property(x => x.TokenHash).HasMaxLength(80).IsRequired();
            b.HasIndex(x => x.TokenHash);
            b.HasIndex(x => x.PlatformUserId);
        });

        modelBuilder.Entity<GoogleAuthConfig>(b =>
        {
            b.Property(x => x.ClientId).HasMaxLength(300);
        });

        modelBuilder.Entity<TenantApiConfig>(b =>
        {
            b.Property(x => x.ApiKeyHash).HasMaxLength(80);
            b.HasIndex(x => x.ApiKeyHash);
            b.HasIndex(x => x.TenantId).IsUnique();
        });

        modelBuilder.Entity<WompiMasterConfig>(b =>
        {
            b.Property(x => x.PublicKey).HasMaxLength(200);
            b.Property(x => x.WebhookEndpoint).HasMaxLength(500);
            b.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        });

        modelBuilder.Entity<EvolutionMasterConfig>(b =>
        {
            b.Property(x => x.BaseUrl).HasMaxLength(500);
            b.Property(x => x.WebhookMode).HasMaxLength(20).HasDefaultValue("Development");
            b.Property(x => x.WebhookPublicUrl).HasMaxLength(500);
            b.Property(x => x.WebhookActiveUrl).HasMaxLength(500);
            b.Property(x => x.WebhookToken).HasMaxLength(200);
        });

        modelBuilder.Entity<AiProviderConfig>(b =>
        {
            b.Property(x => x.Model).HasMaxLength(120);
            b.Property(x => x.BaseUrl).HasMaxLength(500);
            b.HasIndex(x => x.Provider).IsUnique();
        });

        modelBuilder.Entity<WompiWebhookEvent>(b =>
        {
            b.Property(x => x.ProviderEventId).HasMaxLength(250).IsRequired();
            b.Property(x => x.TransactionId).HasMaxLength(200);
            b.Property(x => x.Reference).HasMaxLength(200);
            b.Property(x => x.Note).HasMaxLength(500);
            b.Property(x => x.RawPayload).HasColumnType("jsonb");
            // Idempotencia: un evento (transaction + timestamp) no se procesa dos veces.
            b.HasIndex(x => x.ProviderEventId).IsUnique();
        });

        modelBuilder.Entity<TenantUser>(b =>
        {
            b.Property(x => x.Email).HasMaxLength(256).IsRequired();
            b.Property(x => x.InvitationToken).HasMaxLength(128);
            b.HasOne(x => x.PlatformUser).WithMany().HasForeignKey(x => x.PlatformUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.PlatformUserId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            b.HasIndex(x => x.InvitationToken);
        });

        modelBuilder.Entity<TenantConfiguration>(b =>
        {
            b.Property(x => x.ConfigKey).HasMaxLength(150).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.ConfigKey }).IsUnique();
        });

        modelBuilder.Entity<TenantEvolutionConfig>(b =>
        {
            // Campos del servidor propio: opcionales (cuando la agencia usa el servidor maestro quedan nulos).
            b.Property(x => x.BaseUrl).HasMaxLength(500);
            b.Property(x => x.InstanceName).HasMaxLength(200);
            b.Property(x => x.WebhookUrl).HasMaxLength(500);
            // Una configuracion Evolution por tenant.
            b.HasIndex(x => x.TenantId).IsUnique();
        });

        modelBuilder.Entity<WhatsAppLine>(b =>
        {
            b.Property(x => x.InstanceName).HasMaxLength(200).IsRequired();
            b.Property(x => x.PhoneNumber).HasMaxLength(40);
            b.HasIndex(x => new { x.TenantId, x.InstanceName }).IsUnique();
            b.HasIndex(x => x.AssignedToTenantUserId);
        });

        modelBuilder.Entity<PipelineStage>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
        });

        modelBuilder.Entity<PipelineFieldDefinition>(b =>
        {
            b.Property(x => x.FieldKey).HasMaxLength(80).IsRequired();
            b.Property(x => x.Label).HasMaxLength(150).IsRequired();
            b.Property(x => x.Options).HasMaxLength(2000);
            b.Property(x => x.Description).HasMaxLength(600);
            b.Property(x => x.RepeatWithFieldKey).HasMaxLength(80);
            b.HasOne(x => x.Stage).WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.StageId, x.SortOrder });
            b.HasIndex(x => new { x.StageId, x.FieldKey }).IsUnique();
        });

        modelBuilder.Entity<Lead>(b =>
        {
            b.Property(x => x.ContactName).HasMaxLength(200).IsRequired();
            b.Property(x => x.ContactPhone).HasMaxLength(40);
            b.Property(x => x.Destination).HasMaxLength(200);
            b.Property(x => x.Currency).HasMaxLength(10);
            b.Property(x => x.LossReason).HasMaxLength(500);
            b.Property(x => x.EstimatedValue).HasPrecision(14, 2);
            b.Property(x => x.FieldValuesJson).HasColumnType("jsonb");
            b.Property(x => x.ArchiveReason).HasMaxLength(80);
            b.Property(x => x.ArchiveNote).HasMaxLength(1000);
            b.Property(x => x.ArchivedByName).HasMaxLength(200);
            b.HasOne(x => x.Stage).WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.StageId });
            b.HasIndex(x => x.AssignedToTenantUserId);
            b.HasIndex(x => new { x.TenantId, x.ArchivedAt });
        });

        modelBuilder.Entity<LeadActivity>(b =>
        {
            b.Property(x => x.ActivityType).HasMaxLength(80).IsRequired();
            b.Property(x => x.Description).HasMaxLength(1000);
            b.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.LeadId });
        });

        modelBuilder.Entity<LeadNote>(b =>
        {
            b.Property(x => x.Content).HasMaxLength(2000).IsRequired();
            b.Property(x => x.Color).HasMaxLength(20).IsRequired();
            b.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.LeadId });
        });

        modelBuilder.Entity<LeadFile>(b =>
        {
            b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            b.Property(x => x.Url).HasMaxLength(500).IsRequired();
            b.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            b.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.LeadId });
        });

        modelBuilder.Entity<FollowUpTask>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.Notes).HasMaxLength(1000);
            b.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.Status });
            b.HasIndex(x => new { x.TenantId, x.DueAt });
        });

        modelBuilder.Entity<Conversation>(b =>
        {
            b.Property(x => x.ContactPhone).HasMaxLength(40).IsRequired();
            b.Property(x => x.ContactName).HasMaxLength(200);
            b.HasIndex(x => new { x.TenantId, x.ContactPhone }).IsUnique();
        });

        modelBuilder.Entity<Message>(b =>
        {
            b.Property(x => x.Body).HasMaxLength(4000);
            b.Property(x => x.MessageType).HasMaxLength(40).IsRequired();
            b.Property(x => x.ExternalId).HasMaxLength(200);
            b.Property(x => x.MediaUrl).HasMaxLength(500);
            b.Property(x => x.MediaMimeType).HasMaxLength(120);
            b.Property(x => x.SentByName).HasMaxLength(200);
            b.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.ConversationId });
            // Idempotencia de ingesta: un mensaje externo no se inserta dos veces.
            b.HasIndex(x => new { x.TenantId, x.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL");
        });

        modelBuilder.Entity<MessageTemplate>(b =>
        {
            b.Property(x => x.Category).HasMaxLength(40).IsRequired();
            b.Property(x => x.Body).HasMaxLength(4000);
            b.Property(x => x.MediaUrl).HasMaxLength(500);
            b.Property(x => x.MediaMimeType).HasMaxLength(120);
            b.HasIndex(x => new { x.TenantId, x.Category, x.SortOrder });
        });

        modelBuilder.Entity<QuoteTemplate>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.HtmlContent).HasColumnType("text");
            b.HasIndex(x => new { x.TenantId, x.IsDefault });
        });

        modelBuilder.Entity<TemplateAsset>(b =>
        {
            b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            b.Property(x => x.Url).HasMaxLength(500).IsRequired();
            b.Property(x => x.MimeType).HasMaxLength(120);
            b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        modelBuilder.Entity<AiAgent>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Role).HasMaxLength(100);
            b.Property(x => x.Model).HasMaxLength(100);
            b.Property(x => x.SystemPrompt).HasColumnType("text");
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
        });

        modelBuilder.Entity<AiAgentResource>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Detail).HasColumnType("text");
            b.Property(x => x.FileUrl).HasMaxLength(500);
            b.Property(x => x.FileName).HasMaxLength(255);
            b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.AgentId, x.SortOrder });
        });

        modelBuilder.Entity<AiAgentPrompt>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Rule).HasMaxLength(500);
            b.Property(x => x.Body).HasColumnType("text");
            b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.AgentId, x.SortOrder });
        });

        modelBuilder.Entity<AiUsageLog>(b =>
        {
            b.Property(x => x.Model).HasMaxLength(120);
            b.Property(x => x.Source).HasMaxLength(40);
            b.Property(x => x.EstimatedCostUsd).HasPrecision(12, 6);
            b.HasIndex(x => new { x.TenantId, x.AgentId });
            b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        modelBuilder.Entity<AutomationRule>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.FollowUpTitle).HasMaxLength(200);
            b.Property(x => x.TimeWindowStart).HasMaxLength(5);
            b.Property(x => x.TimeWindowEnd).HasMaxLength(5);
            b.Property(x => x.TemplateCategory).HasMaxLength(40);
            b.Property(x => x.ShiftName).HasMaxLength(60);
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
        });

        modelBuilder.Entity<FormDefinition>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(40).IsRequired();
            b.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            b.Property(x => x.Version).HasMaxLength(20);
            b.Property(x => x.Tipo).HasMaxLength(40);
            b.Property(x => x.SchemaJson).HasColumnType("jsonb").IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Codigo }).IsUnique();
        });

        modelBuilder.Entity<Aseguradora>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(40).IsRequired();
            b.Property(x => x.Tipo).HasMaxLength(20).IsRequired();
            b.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            b.Property(x => x.CodigoMovilidad).HasMaxLength(40);
            b.Property(x => x.Nit).HasMaxLength(30);
            b.Property(x => x.Regimen).HasMaxLength(40);
            b.Property(x => x.CodInt).HasMaxLength(20);
            b.Property(x => x.Descripcion).HasMaxLength(1000);
            b.HasIndex(x => new { x.TenantId, x.Codigo }).IsUnique();
        });

        modelBuilder.Entity<ContratoAseguradora>(b =>
        {
            b.Property(x => x.CodigoContrato).HasMaxLength(60).IsRequired();
            b.Property(x => x.Estado).HasMaxLength(20).IsRequired();
            b.HasOne(x => x.Aseguradora).WithMany().HasForeignKey(x => x.AseguradoraId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.AseguradoraId });
            b.HasIndex(x => new { x.TenantId, x.CodigoContrato });
        });

        modelBuilder.Entity<TipoProfesional>(b =>
        {
            b.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Nombre }).IsUnique();
        });

        modelBuilder.Entity<SubCategoriaProfesional>(b =>
        {
            b.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Nombre }).IsUnique();
        });

        modelBuilder.Entity<Profesional>(b =>
        {
            b.Property(x => x.NumeroDocumento).HasMaxLength(30).IsRequired();
            b.Property(x => x.TipoDocumento).HasMaxLength(10).IsRequired();
            b.Property(x => x.PrimerNombre).HasMaxLength(80);
            b.Property(x => x.SegundoNombre).HasMaxLength(80);
            b.Property(x => x.PrimerApellido).HasMaxLength(80);
            b.Property(x => x.SegundoApellido).HasMaxLength(80);
            b.Property(x => x.NombreCompleto).HasMaxLength(250).IsRequired();
            b.Property(x => x.RegistroMedico).HasMaxLength(60);
            b.Property(x => x.Ciudad).HasMaxLength(120);
            b.Property(x => x.Celular).HasMaxLength(40);
            b.Property(x => x.FirmaUrl).HasMaxLength(500);
            b.HasOne(x => x.TipoProfesional).WithMany().HasForeignKey(x => x.TipoProfesionalId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.NumeroDocumento }).IsUnique();
        });

        modelBuilder.Entity<ProfesionalSubCategoria>(b =>
        {
            b.HasOne(x => x.Profesional).WithMany().HasForeignKey(x => x.ProfesionalId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.SubCategoria).WithMany().HasForeignKey(x => x.SubCategoriaId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.ProfesionalId });
        });

        modelBuilder.Entity<ProfesionalAgencia>(b =>
        {
            b.Property(x => x.Agencia).HasMaxLength(120).IsRequired();
            b.HasOne(x => x.Profesional).WithMany().HasForeignKey(x => x.ProfesionalId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.ProfesionalId });
        });

        modelBuilder.Entity<ServicioContrato>(b =>
        {
            b.Property(x => x.Sede).HasMaxLength(200);
            b.Property(x => x.Historia).HasMaxLength(40);
            b.Property(x => x.CodigoServicio).HasMaxLength(40);
            b.Property(x => x.CodigoInterno).HasMaxLength(40);
            b.Property(x => x.Descripcion).HasMaxLength(300);
            b.Property(x => x.Tarifa).HasPrecision(14, 2);
            b.Property(x => x.Modulo).HasMaxLength(80);
            b.Property(x => x.Especialidad).HasMaxLength(80);
            b.Property(x => x.Modalidad).HasMaxLength(80);
            b.Property(x => x.Clasificacion).HasMaxLength(80);
            b.Property(x => x.Observaciones).HasMaxLength(1000);
            b.HasOne(x => x.Contrato).WithMany().HasForeignKey(x => x.ContratoId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.ContratoId });
        });

        modelBuilder.Entity<Rol>(b =>
        {
            b.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            b.Property(x => x.Descripcion).HasMaxLength(400);
            b.HasIndex(x => new { x.TenantId, x.Nombre }).IsUnique();
        });

        modelBuilder.Entity<RolPermiso>(b =>
        {
            b.Property(x => x.Modulo).HasMaxLength(60).IsRequired();
            b.HasOne(x => x.Rol).WithMany().HasForeignKey(x => x.RolId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.RolId, x.Modulo }).IsUnique();
        });

        modelBuilder.Entity<Sucursal>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(40).IsRequired();
            b.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            b.Property(x => x.Direccion).HasMaxLength(300);
            b.Property(x => x.Ciudad).HasMaxLength(120);
            b.Property(x => x.Telefono).HasMaxLength(40);
            b.HasIndex(x => new { x.TenantId, x.Codigo }).IsUnique();
        });

        modelBuilder.Entity<Paciente>(b =>
        {
            // Identificacion
            b.Property(x => x.NumeroDocumento).HasMaxLength(30).IsRequired();
            b.Property(x => x.TipoDocumento).HasMaxLength(10).IsRequired();
            b.Property(x => x.PrimerNombre).HasMaxLength(80);
            b.Property(x => x.SegundoNombre).HasMaxLength(80);
            b.Property(x => x.PrimerApellido).HasMaxLength(80);
            b.Property(x => x.SegundoApellido).HasMaxLength(80);
            b.Property(x => x.NombreCompleto).HasMaxLength(250).IsRequired();
            // Admin PAD
            b.Property(x => x.CodigoAceptacion).HasMaxLength(40);
            // Clasificaciones (texto libre por ahora)
            b.Property(x => x.Incapacidad).HasMaxLength(60);
            b.Property(x => x.GrupoRh).HasMaxLength(10);
            b.Property(x => x.Estado).HasMaxLength(40);
            b.Property(x => x.EstratoSocial).HasMaxLength(20);
            b.Property(x => x.Sexo).HasMaxLength(20);
            b.Property(x => x.EstadoCivil).HasMaxLength(40);
            b.Property(x => x.Zona).HasMaxLength(40);
            b.Property(x => x.Ocupacion).HasMaxLength(120);
            b.Property(x => x.Regimen).HasMaxLength(40);
            b.Property(x => x.Tutela).HasMaxLength(40);
            b.Property(x => x.MedContratado).HasMaxLength(80);
            // Diagnostico
            b.Property(x => x.DiagnosticoPrincipal).HasMaxLength(500);
            // Geografia
            b.Property(x => x.Direccion).HasMaxLength(300);
            b.Property(x => x.Barrio).HasMaxLength(120);
            b.Property(x => x.Ciudad).HasMaxLength(120);
            // Contacto
            b.Property(x => x.Telefono).HasMaxLength(40);
            b.Property(x => x.Email).HasMaxLength(160);
            // Emergencia
            b.Property(x => x.ContactoEmergencia).HasMaxLength(200);
            b.Property(x => x.Parentesco).HasMaxLength(80);
            b.Property(x => x.TelefonoEmergencia).HasMaxLength(40);
            // FKs concretas (las catalogo se haran luego)
            b.HasOne(x => x.Aseguradora).WithMany().HasForeignKey(x => x.AseguradoraId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.SedeAtencion).WithMany().HasForeignKey(x => x.SedeAtencionId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.NumeroDocumento }).IsUnique();
        });

        modelBuilder.Entity<TenantUser>(b =>
        {
            b.HasOne(x => x.Rol).WithMany().HasForeignKey(x => x.RolId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Sucursal).WithMany().HasForeignKey(x => x.SucursalId).OnDelete(DeleteBehavior.SetNull);
            b.HasMany(x => x.Sucursales).WithOne(x => x.TenantUser!).HasForeignKey(x => x.TenantUserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantUserSucursal>(b =>
        {
            b.HasOne(x => x.Sucursal).WithMany().HasForeignKey(x => x.SucursalId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantUserId, x.SucursalId }).IsUnique();
        });

        modelBuilder.Entity<CatalogoPaciente>(b =>
        {
            b.Property(x => x.Tipo).HasMaxLength(60).IsRequired();
            b.Property(x => x.Codigo).HasMaxLength(40).IsRequired();
            b.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            b.Property(x => x.Descripcion).HasMaxLength(500);
            // Unico por tenant + tipo + codigo: cada catalogo tiene su propio espacio de codigos.
            b.HasIndex(x => new { x.TenantId, x.Tipo, x.Codigo }).IsUnique();
        });
    }

    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        var applyMethod = typeof(VisalDbContext)
            .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                applyMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped
    {
        // Fail-closed: si no hay tenant activo, TenantId del contexto es null y no devuelve filas.
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
    }
}
