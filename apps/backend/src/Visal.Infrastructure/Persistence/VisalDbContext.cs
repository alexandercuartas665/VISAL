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
    public DbSet<TenantGupshupConfig> TenantGupshupConfigs => Set<TenantGupshupConfig>();
    public DbSet<TenantWhatsAppTemplateBinding> TenantWhatsAppTemplateBindings => Set<TenantWhatsAppTemplateBinding>();
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
    public DbSet<FormDefinitionSnapshot> FormDefinitionSnapshots => Set<FormDefinitionSnapshot>();
    public DbSet<HistoriaClinica> HistoriasClinicas => Set<HistoriaClinica>();
    public DbSet<HistoriaClinicaMedicamento> HistoriaClinicaMedicamentos => Set<HistoriaClinicaMedicamento>();
    public DbSet<HistoriaClinicaOrdenServicio> HistoriaClinicaOrdenesServicio => Set<HistoriaClinicaOrdenServicio>();
    public DbSet<HistoriaClinicaInsumo> HistoriaClinicaInsumos => Set<HistoriaClinicaInsumo>();
    public DbSet<SqlConsoleLog> SqlConsoleLogs => Set<SqlConsoleLog>();
    public DbSet<HistoriaClinicaIncapacidad> HistoriaClinicaIncapacidades => Set<HistoriaClinicaIncapacidad>();
    public DbSet<HistoriaClinicaCertificacion> HistoriaClinicaCertificaciones => Set<HistoriaClinicaCertificacion>();
    public DbSet<HistoriaClinicaRemision> HistoriaClinicaRemisiones => Set<HistoriaClinicaRemision>();
    public DbSet<AsistenteChatMensaje> AsistenteChatMensajes => Set<AsistenteChatMensaje>();
    public DbSet<RelacionFormulario> RelacionesFormulario => Set<RelacionFormulario>();
    public DbSet<HistoriaClinicaEscala> HistoriaClinicaEscalas => Set<HistoriaClinicaEscala>();
    public DbSet<HistoriaClinicaDocumento> HistoriaClinicaDocumentos => Set<HistoriaClinicaDocumento>();
    public DbSet<Medicamento> Medicamentos => Set<Medicamento>();
    public DbSet<Diagnostico> Diagnosticos => Set<Diagnostico>();
    public DbSet<CatalogoServicioReferencia> CatalogosServicioReferencia => Set<CatalogoServicioReferencia>();
    public DbSet<HistoriaClinicaOrdenExterna> HistoriaClinicaOrdenesExternas => Set<HistoriaClinicaOrdenExterna>();
    public DbSet<NotaMedica> NotasMedicas => Set<NotaMedica>();
    public DbSet<NotaMedicaDocumento> NotaMedicaDocumentos => Set<NotaMedicaDocumento>();
    public DbSet<HcMenuConfig> HcMenuConfigs => Set<HcMenuConfig>();
    public DbSet<HcPestanaAlias> HcPestanaAliases => Set<HcPestanaAlias>();
    public DbSet<AtencionColumnaConfig> AtencionColumnaConfigs => Set<AtencionColumnaConfig>();
    public DbSet<TurnoProgramacion> TurnoProgramaciones => Set<TurnoProgramacion>();
    public DbSet<TurnoProgramacionSucursal> TurnoProgramacionSucursales => Set<TurnoProgramacionSucursal>();
    public DbSet<TipoTurno> TiposTurno => Set<TipoTurno>();
    public DbSet<CatalogoTipoServicio> CatalogosTipoServicio => Set<CatalogoTipoServicio>();
    public DbSet<TenantUserTipoCoordinado> TenantUserTiposCoordinados => Set<TenantUserTipoCoordinado>();
    public DbSet<FirmaPacienteRequest> FirmaPacienteRequests => Set<FirmaPacienteRequest>();
    public DbSet<TipologiaArchivo> TipologiaArchivos => Set<TipologiaArchivo>();
    public DbSet<Aseguradora> Aseguradoras => Set<Aseguradora>();
    public DbSet<ContratoAseguradora> ContratosAseguradora => Set<ContratoAseguradora>();
    public DbSet<AseguradoraCuentaMedicaConfig> AseguradoraCuentaMedicaConfigs => Set<AseguradoraCuentaMedicaConfig>();
    public DbSet<AseguradoraInformeItem> AseguradoraInformeItems => Set<AseguradoraInformeItem>();
    public DbSet<ServicioContrato> ServiciosContrato => Set<ServicioContrato>();
    public DbSet<Paquete> Paquetes => Set<Paquete>();
    public DbSet<PaqueteServicio> PaqueteServicios => Set<PaqueteServicio>();
    public DbSet<CuotaCopago> CuotasCopagos => Set<CuotaCopago>();
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
    public DbSet<PacienteContactoEmergencia> PacienteContactosEmergencia => Set<PacienteContactoEmergencia>();
    public DbSet<CatalogoPaciente> CatalogosPaciente => Set<CatalogoPaciente>();
    public DbSet<AsignacionLote> AsignacionLotes => Set<AsignacionLote>();
    public DbSet<Asignacion> Asignaciones => Set<Asignacion>();
    public DbSet<AsignacionTurno> AsignacionTurnos => Set<AsignacionTurno>();
    public DbSet<AsignacionTurnoSesion> AsignacionTurnoSesiones => Set<AsignacionTurnoSesion>();
    public DbSet<Cie11Config> Cie11Configs => Set<Cie11Config>();
    public DbSet<Pais> Paises => Set<Pais>();
    public DbSet<Departamento> Departamentos => Set<Departamento>();
    public DbSet<Municipio> Municipios => Set<Municipio>();
    public DbSet<InteroperabilidadConfig> InteroperabilidadConfigs => Set<InteroperabilidadConfig>();
    public DbSet<InteroperabilidadCredencialSede> InteroperabilidadCredencialesSede => Set<InteroperabilidadCredencialSede>();
    public DbSet<RdaEvento> RdaEventos => Set<RdaEvento>();
    public DbSet<FacturacionSnapshot> FacturacionSnapshots => Set<FacturacionSnapshot>();
    public DbSet<FacturacionSnapshotFila> FacturacionSnapshotFilas => Set<FacturacionSnapshotFila>();
    public DbSet<RevisionClinica> RevisionesClinica => Set<RevisionClinica>();
    public DbSet<RevisionClinicaEvento> RevisionClinicaEventos => Set<RevisionClinicaEvento>();
    public DbSet<RevisionPolicy> RevisionPolicies => Set<RevisionPolicy>();
    public DbSet<PreRevisionIaPending> PreRevisionIaPendings => Set<PreRevisionIaPending>();
    public DbSet<DocumentoNota> DocumentoNotas => Set<DocumentoNota>();

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
        configurationBuilder.Properties<WhatsAppProvider>().HaveConversion<string>().HaveMaxLength(40);
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
        configurationBuilder.Properties<AmbienteIhce>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ModalidadRdaIhce>().HaveConversion<string>().HaveMaxLength(30);
        configurationBuilder.Properties<EstadoRdaEvento>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<TipoRdaIhce>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<TipoSnapshot>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<EstadoSnapshot>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<RevisionEstadoAgregado>().HaveConversion<string>().HaveMaxLength(30);
        configurationBuilder.Properties<RevisionTipoEvento>().HaveConversion<string>().HaveMaxLength(30);
        configurationBuilder.Properties<RevisionResultado>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<RevisionActorTipo>().HaveConversion<string>().HaveMaxLength(20);
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
            // Campos personales (modulo Administracion de Usuarios)
            b.Property(x => x.Username).HasMaxLength(80);
            b.Property(x => x.PrimerNombre).HasMaxLength(80);
            b.Property(x => x.SegundoNombre).HasMaxLength(80);
            b.Property(x => x.PrimerApellido).HasMaxLength(80);
            b.Property(x => x.SegundoApellido).HasMaxLength(80);
            b.Property(x => x.Celular).HasMaxLength(40);
            b.Property(x => x.Fijo).HasMaxLength(40);
            b.Property(x => x.Ciudad).HasMaxLength(120);
            b.Property(x => x.Direccion).HasMaxLength(250);
            b.HasIndex(x => x.Email).IsUnique();
            b.HasIndex(x => x.GoogleSubject).IsUnique().HasFilter("google_subject IS NOT NULL");
            b.HasIndex(x => x.Documento).IsUnique().HasFilter("documento IS NOT NULL");
            // Username unico (excluye nulls para no chocar entre usuarios sin username).
            b.HasIndex(x => x.Username).IsUnique().HasFilter("username IS NOT NULL");
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
            // Token opaco del webhook entrante; formato base64url ~32-64 chars.
            // Buscamos por este valor en el endpoint anonimo, por eso unique
            // globalmente (no scoped a tenant): dos lineas de tenants distintos
            // no pueden colisionar.
            b.Property(x => x.InboundToken).HasMaxLength(128);
            b.HasIndex(x => new { x.TenantId, x.InstanceName }).IsUnique();
            b.HasIndex(x => x.AssignedToTenantUserId);
            b.HasIndex(x => x.InboundToken).IsUnique().HasFilter("inbound_token IS NOT NULL");
            b.HasIndex(x => new { x.TenantId, x.Provider });
        });

        modelBuilder.Entity<TenantGupshupConfig>(b =>
        {
            b.Property(x => x.AppName).HasMaxLength(200).IsRequired();
            b.Property(x => x.WabaId).HasMaxLength(80);
            b.Property(x => x.PhoneNumber).HasMaxLength(40);
            b.Property(x => x.DisplayName).HasMaxLength(200);
            // Blobs cifrados (Data Protection): dejamos text sin cota; el
            // ciphertext base64 supera los ~500 chars con facilidad.
            b.Property(x => x.ApiKeyEncrypted).HasColumnType("text").IsRequired();
            b.Property(x => x.PartnerTokenEncrypted).HasColumnType("text");
            // Multiples Apps por tenant permitidas; unica por (tenant, appId).
            b.HasIndex(x => new { x.TenantId, x.AppId }).IsUnique();
        });

        modelBuilder.Entity<TenantWhatsAppTemplateBinding>(b =>
        {
            b.Property(x => x.Role).HasConversion<int>();
            b.Property(x => x.TemplateId).HasMaxLength(80).IsRequired();
            b.Property(x => x.TemplateName).HasMaxLength(200).IsRequired();
            b.Property(x => x.LanguageCode).HasMaxLength(20).IsRequired();
            b.HasOne(x => x.Line).WithMany().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Restrict);
            // Solo un binding por (tenant, rol). Al reasignar hacemos UPDATE, no INSERT.
            b.HasIndex(x => new { x.TenantId, x.Role }).IsUnique();
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
            b.HasOne(x => x.AiAgent).WithMany().HasForeignKey(x => x.AiAgentId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.SortOrder });
            b.HasIndex(x => x.AiAgentId);
        });

        modelBuilder.Entity<FormDefinition>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(40).IsRequired();
            b.Property(x => x.CodigoSecundario).HasMaxLength(80);
            b.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            b.Property(x => x.Version).HasMaxLength(20);
            b.Property(x => x.Tipo).HasMaxLength(40);
            b.Property(x => x.SchemaJson).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.PrefillRoutesJson).HasColumnType("jsonb");
            b.HasIndex(x => new { x.TenantId, x.Codigo }).IsUnique();
            // CodigoSecundario NO es unico - es un id alternativo libre.
            b.HasIndex(x => new { x.TenantId, x.CodigoSecundario });
        });

        modelBuilder.Entity<FormDefinitionSnapshot>(b =>
        {
            // Misma forma que FormDefinition para que el snapshot pueda contener
            // cualquier valor que aceptaba la fila viva. MaxLength espejado.
            b.Property(x => x.Codigo).HasMaxLength(40).IsRequired();
            b.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            b.Property(x => x.Version).HasMaxLength(20);
            b.Property(x => x.Tipo).HasMaxLength(40);
            b.Property(x => x.SchemaJson).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.PrefillRoutesJson).HasColumnType("jsonb");
            b.Property(x => x.Motivo).HasMaxLength(80);
            b.HasOne(x => x.FormDefinition)
                .WithMany()
                .HasForeignKey(x => x.FormDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            // Index compuesto para el listado descendente por FormDefinition
            // (la consulta dominante: "ultimas 20 versiones de este formulario").
            b.HasIndex(x => new { x.FormDefinitionId, x.SnapshotAt })
                .IsDescending(false, true);
        });

        modelBuilder.Entity<Medicamento>(b =>
        {
            // Catalogo CUM (INVIMA). Sin uniques: la unicidad funcional viene del
            // Excel oficial. NO usamos HasMaxLength: el INVIMA cambia los anchos
            // sin avisar (vimos campos de 40 chars desbordando el ancho oficial
            // documentado). En Postgres `text` y `varchar(n)` tienen el mismo
            // rendimiento, asi que dejamos text para tolerar cualquier ancho.
            // Indices solo en las columnas que se usan en busquedas frecuentes.
            b.Property(x => x.Expediente).HasColumnType("text");
            b.Property(x => x.Producto).HasColumnType("text");
            b.Property(x => x.Titular).HasColumnType("text");
            b.Property(x => x.RegistroSanitario).HasColumnType("text");
            b.Property(x => x.EstadoRegistro).HasColumnType("text");
            b.Property(x => x.ExpedienteCum).HasColumnType("text");
            b.Property(x => x.ConsecutivoCum).HasColumnType("text");
            b.Property(x => x.CantidadCum).HasColumnType("text");
            b.Property(x => x.DescripcionComercial).HasColumnType("text");
            b.Property(x => x.EstadoCum).HasColumnType("text");
            b.Property(x => x.MuestraMedica).HasColumnType("text");
            b.Property(x => x.Unidad).HasColumnType("text");
            b.Property(x => x.Atc).HasColumnType("text");
            b.Property(x => x.DescripcionAtc).HasColumnType("text");
            b.Property(x => x.ViaAdministracion).HasColumnType("text");
            b.Property(x => x.Concentracion).HasColumnType("text");
            b.Property(x => x.PrincipioActivo).HasColumnType("text");
            b.Property(x => x.UnidadMedida).HasColumnType("text");
            b.Property(x => x.Cantidad).HasColumnType("text");
            b.Property(x => x.UnidadReferencia).HasColumnType("text");
            b.Property(x => x.FormaFarmaceutica).HasColumnType("text");
            b.Property(x => x.NombreRol).HasColumnType("text");
            b.Property(x => x.TipoRol).HasColumnType("text");
            b.Property(x => x.Modalidad).HasColumnType("text");
            b.Property(x => x.Ium).HasColumnType("text");
            b.HasIndex(x => new { x.TenantId, x.Producto });
            b.HasIndex(x => new { x.TenantId, x.RegistroSanitario });
            b.HasIndex(x => new { x.TenantId, x.Ium });
            b.HasIndex(x => new { x.TenantId, x.Atc });
        });

        modelBuilder.Entity<Diagnostico>(b =>
        {
            // Base de datos de diagnosticos (reemplaza WHO ICD-11 API). Codigo unico
            // por tenant. Nombre y descripcion en text por si la fuente trae
            // clasificaciones largas ("SECCION 00 PROCEDIMIENTOS QUIRURGICOS...").
            b.Property(x => x.Codigo).HasMaxLength(60).IsRequired();
            b.Property(x => x.Nombre).HasColumnType("text").IsRequired();
            b.Property(x => x.Descripcion).HasColumnType("text");
            b.Property(x => x.Fuente).HasMaxLength(60);
            b.HasIndex(x => new { x.TenantId, x.Codigo }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Habilitado });
        });

        modelBuilder.Entity<CatalogoServicioReferencia>(b =>
        {
            // Catalogos "de referencia" (RxImag, Lab, Servicios, Insumos). Un codigo
            // unico por (tenant, tipo) — el usuario puede recargar el Excel y
            // hacemos upsert por codigo. Nombre en text por si el Excel del
            // proveedor trae descripciones largas o con caracteres especiales.
            b.Property(x => x.Codigo).HasMaxLength(60).IsRequired();
            b.Property(x => x.Nombre).HasColumnType("text").IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Tipo, x.Codigo }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Tipo, x.Activo });
        });

        modelBuilder.Entity<HistoriaClinicaOrdenExterna>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(60);
            b.Property(x => x.Descripcion).HasColumnType("text").IsRequired();
            b.Property(x => x.Cantidad).HasMaxLength(60);
            b.Property(x => x.Observaciones).HasColumnType("text");
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId, x.Tipo });
        });

        modelBuilder.Entity<HistoriaClinicaMedicamento>(b =>
        {
            b.Property(x => x.NombreMedicamento).HasColumnType("text").IsRequired();
            b.Property(x => x.Cantidad).HasColumnType("text");
            b.Property(x => x.Frecuencia).HasColumnType("text");
            b.Property(x => x.Dias).HasColumnType("text");
            b.Property(x => x.Posologia).HasColumnType("text");
            b.Property(x => x.Observacion).HasColumnType("text");
            b.HasOne(x => x.HistoriaClinica).WithMany().HasForeignKey(x => x.HistoriaClinicaId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Medicamento).WithMany().HasForeignKey(x => x.MedicamentoId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId, x.Orden });
        });

        modelBuilder.Entity<HistoriaClinicaOrdenServicio>(b =>
        {
            b.Property(x => x.CodigoServicio).HasColumnType("text");
            b.Property(x => x.Descripcion).HasColumnType("text").IsRequired();
            b.Property(x => x.Cantidad).HasColumnType("text");
            b.Property(x => x.Observaciones).HasColumnType("text");
            b.HasOne(x => x.HistoriaClinica).WithMany().HasForeignKey(x => x.HistoriaClinicaId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.ServicioContrato).WithMany().HasForeignKey(x => x.ServicioContratoId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId, x.Orden });
        });

        modelBuilder.Entity<HistoriaClinicaIncapacidad>(b =>
        {
            b.Property(x => x.Motivo).HasColumnType("text").IsRequired();
            b.Property(x => x.Tipo).HasMaxLength(60);
            b.HasOne(x => x.HistoriaClinica).WithMany().HasForeignKey(x => x.HistoriaClinicaId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId, x.Orden });
        });

        modelBuilder.Entity<HistoriaClinicaInsumo>(b =>
        {
            b.Property(x => x.Codigo).HasColumnType("text");
            b.Property(x => x.Descripcion).HasColumnType("text").IsRequired();
            b.Property(x => x.Cantidad).HasColumnType("text");
            b.Property(x => x.Observaciones).HasColumnType("text");
            b.HasOne(x => x.HistoriaClinica).WithMany().HasForeignKey(x => x.HistoriaClinicaId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId, x.Orden });
        });

        modelBuilder.Entity<SqlConsoleLog>(b =>
        {
            b.Property(x => x.Query).HasColumnType("text").IsRequired();
            b.Property(x => x.QueryType).HasMaxLength(20);
            b.Property(x => x.UserName).HasMaxLength(200);
            b.Property(x => x.ErrorMessage).HasColumnType("text");
            b.HasIndex(x => new { x.TenantId, x.ExecutedAt });
            b.HasIndex(x => x.ExecutedAt);
        });

        modelBuilder.Entity<HistoriaClinicaCertificacion>(b =>
        {
            b.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
            b.Property(x => x.Contenido).HasColumnType("text").IsRequired();
            b.HasOne(x => x.HistoriaClinica).WithMany().HasForeignKey(x => x.HistoriaClinicaId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId, x.Orden });
        });

        modelBuilder.Entity<HistoriaClinicaRemision>(b =>
        {
            // Texto libre (sin HasMaxLength) para tolerar capitulos / nombres largos del CUPS oficial.
            b.Property(x => x.Capitulo).HasColumnType("text").IsRequired();
            b.Property(x => x.EspecialidadCodigo).HasColumnType("text");
            b.Property(x => x.EspecialidadNombre).HasColumnType("text").IsRequired();
            b.Property(x => x.Motivo).HasColumnType("text");
            b.HasOne(x => x.HistoriaClinica).WithMany().HasForeignKey(x => x.HistoriaClinicaId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId, x.Orden });
        });

        modelBuilder.Entity<AsistenteChatMensaje>(b =>
        {
            b.Property(x => x.Rol).HasMaxLength(20).IsRequired();
            b.Property(x => x.Texto).HasColumnType("text").IsRequired();
            b.Property(x => x.AgenteNombreSnapshot).HasMaxLength(200);
            b.HasOne(x => x.Paciente).WithMany().HasForeignKey(x => x.PacienteId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.PacienteId, x.Cuando });
        });

        modelBuilder.Entity<RelacionFormulario>(b =>
        {
            b.Property(x => x.TipoRelacion).HasMaxLength(40);
            b.Property(x => x.Observacion).HasMaxLength(500);
            b.HasOne(x => x.FormularioOrigen).WithMany().HasForeignKey(x => x.FormularioOrigenId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.FormularioDestino).WithMany().HasForeignKey(x => x.FormularioDestinoId)
                .OnDelete(DeleteBehavior.Restrict);
            // No se permite el mismo par (origen, destino) duplicado dentro del tenant.
            b.HasIndex(x => new { x.TenantId, x.FormularioOrigenId, x.FormularioDestinoId }).IsUnique();
        });

        modelBuilder.Entity<HistoriaClinicaEscala>(b =>
        {
            b.Property(x => x.ValoresJson).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.EspecialistaNombre).HasMaxLength(200);
            b.HasOne(x => x.HistoriaClinica).WithMany().HasForeignKey(x => x.HistoriaClinicaId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.FormDefinition).WithMany().HasForeignKey(x => x.FormDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId, x.FechaApertura });
        });

        modelBuilder.Entity<HistoriaClinicaDocumento>(b =>
        {
            b.Property(x => x.Tipo).HasMaxLength(40).IsRequired();
            b.Property(x => x.ValoresJson).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.EspecialistaNombre).HasMaxLength(200);
            b.HasOne(x => x.HistoriaClinica).WithMany().HasForeignKey(x => x.HistoriaClinicaId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.FormDefinition).WithMany().HasForeignKey(x => x.FormDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId, x.Tipo, x.FechaApertura });
        });

        modelBuilder.Entity<NotaMedica>(b =>
        {
            b.Property(x => x.CodigoUnico).HasMaxLength(20);
            b.Property(x => x.Contenido).HasColumnType("text").IsRequired();
            b.Property(x => x.EspecialistaNombre).HasMaxLength(200);
            b.Property(x => x.FirmaDataUrl).HasColumnType("text");
            b.Property(x => x.FirmaPacienteDataUrl).HasColumnType("text");
            b.HasOne(x => x.HistoriaClinica).WithMany().HasForeignKey(x => x.HistoriaClinicaId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Paciente).WithMany().HasForeignKey(x => x.PacienteId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId, x.FechaNota });
            b.HasIndex(x => new { x.TenantId, x.PacienteId, x.FechaNota });
            b.HasIndex(x => new { x.TenantId, x.Estado });
            b.HasIndex(x => new { x.TenantId, x.Criticidad });
        });

        modelBuilder.Entity<NotaMedicaDocumento>(b =>
        {
            b.Property(x => x.NombreOriginal).HasMaxLength(255).IsRequired();
            b.Property(x => x.RutaArchivo).HasMaxLength(500).IsRequired();
            b.Property(x => x.TipoMime).HasMaxLength(120);
            b.Property(x => x.Categoria).HasMaxLength(80);
            b.Property(x => x.TipoTerapia).HasMaxLength(80);
            b.Property(x => x.Mes).HasMaxLength(20);
            b.Property(x => x.Anotaciones).HasColumnType("text");
            b.HasOne(x => x.NotaMedica).WithMany().HasForeignKey(x => x.NotaMedicaId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.NotaMedicaId });
            // Indice por paciente para que el tab "Documentos" de Admision liste rapido.
            b.HasIndex(x => new { x.TenantId, x.PacienteId });
            // Indice por historia clinica para la pestana "Documentos externos" del HC.
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId });
        });

        modelBuilder.Entity<HcMenuConfig>(b =>
        {
            b.Property(x => x.TipoServicio).HasMaxLength(40).IsRequired();
            b.Property(x => x.PestanaKey).HasMaxLength(80).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.TipoServicio, x.PestanaKey }).IsUnique();
        });

        modelBuilder.Entity<HcPestanaAlias>(b =>
        {
            b.Property(x => x.PestanaKey).HasMaxLength(80).IsRequired();
            b.Property(x => x.Alias).HasMaxLength(80);
            b.HasIndex(x => new { x.TenantId, x.PestanaKey }).IsUnique();
        });

        modelBuilder.Entity<AtencionColumnaConfig>(b =>
        {
            b.Property(x => x.ColumnaKey).HasMaxLength(60).IsRequired();
            b.Property(x => x.Alias).HasMaxLength(80);
            b.HasIndex(x => new { x.TenantId, x.ColumnaKey }).IsUnique();
        });

        // Capa 6 - Gestion de Turnos.
        modelBuilder.Entity<TurnoProgramacion>(b =>
        {
            b.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            b.Property(x => x.Descripcion).HasMaxLength(500);
            // jsonb en Postgres para permitir queries y validacion nativa.
            b.Property(x => x.GridDataJson).HasColumnType("jsonb").IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Anio, x.Mes });
            // Unicidad: no dos programaciones con mismo nombre para el mismo (tenant, anio, mes).
            // La sede salio de la unica porque ahora es N:N (una programacion puede aplicar a
            // varias sedes) y no podemos usarla como discriminador.
            b.HasIndex(x => new { x.TenantId, x.Anio, x.Mes, x.Nombre }).IsUnique();
            // Nav collection a la tabla puente.
            b.HasMany(x => x.Sucursales)
                .WithOne(x => x.TurnoProgramacion!)
                .HasForeignKey(x => x.TurnoProgramacionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TurnoProgramacionSucursal>(b =>
        {
            b.HasOne(x => x.Sucursal)
                .WithMany()
                .HasForeignKey(x => x.SucursalId)
                .OnDelete(DeleteBehavior.Restrict);
            // Id es la PK (heredada de BaseEntity). El par (TurnoProgramacionId,
            // SucursalId) va como indice unico para evitar duplicados en el N:N.
            b.HasIndex(x => new { x.TurnoProgramacionId, x.SucursalId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.SucursalId });
        });

        modelBuilder.Entity<TipoTurno>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(8).IsRequired();
            b.Property(x => x.Etiqueta).HasMaxLength(40).IsRequired();
            b.Property(x => x.HorasDefault).HasColumnType("numeric(4,1)");
            b.Property(x => x.ColorFondo).HasMaxLength(9).IsRequired();
            b.Property(x => x.ColorTexto).HasMaxLength(9).IsRequired();
            b.Property(x => x.ColorBorde).HasMaxLength(9).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Codigo }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Activo, x.Orden });
        });

        modelBuilder.Entity<CatalogoTipoServicio>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(40).IsRequired();
            b.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Codigo }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Activo, x.Orden });
        });

        modelBuilder.Entity<TenantUserTipoCoordinado>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(40).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.TenantUserId, x.Codigo }).IsUnique();
            b.HasIndex(x => x.TenantUserId);
        });

        modelBuilder.Entity<FirmaPacienteRequest>(b =>
        {
            b.Property(x => x.Token).HasMaxLength(64).IsRequired();
            b.Property(x => x.Telefono).HasMaxLength(20).IsRequired();
            b.Property(x => x.NombreContacto).HasMaxLength(200);
            b.Property(x => x.ImageDataUrl).HasColumnType("text");
            // Token globalmente unico (no por tenant): es la clave de la URL publica
            // y por eso lo buscamos via IgnoreQueryFilters en la pagina anonima.
            b.HasIndex(x => x.Token).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.NotaMedicaId, x.Status });
            b.HasIndex(x => new { x.TenantId, x.PacienteId });
            b.HasIndex(x => x.ContactoEmergenciaId);
        });

        modelBuilder.Entity<TipologiaArchivo>(b =>
        {
            b.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            b.Property(x => x.Color).HasMaxLength(20).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Nombre }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Activo });
        });

        modelBuilder.Entity<HistoriaClinica>(b =>
        {
            b.Property(x => x.ValoresJson).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.EspecialistaNombre).HasMaxLength(200);
            b.Property(x => x.MotivoInactivacion).HasMaxLength(500);
            b.Property(x => x.RipsViaIngresoCodigo).HasMaxLength(10);
            b.Property(x => x.RipsViaIngresoNombre).HasMaxLength(200);
            b.Property(x => x.RipsFinalidadCodigo).HasMaxLength(10);
            b.Property(x => x.RipsFinalidadNombre).HasMaxLength(200);
            b.Property(x => x.RipsCausaExternaCodigo).HasMaxLength(10);
            b.Property(x => x.RipsCausaExternaNombre).HasMaxLength(200);
            b.HasOne(x => x.Paciente).WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.FormDefinition).WithMany().HasForeignKey(x => x.FormDefinitionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Profesional).WithMany().HasForeignKey(x => x.ProfesionalId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.PacienteId, x.FechaApertura });
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
            b.Property(x => x.CorreoFacturacion).HasMaxLength(200);
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

        // Cuenta medica: singleton por aseguradora + N items ordenados.
        modelBuilder.Entity<AseguradoraCuentaMedicaConfig>(b =>
        {
            b.ToTable("aseguradora_cuenta_medica_configs");
            b.Property(x => x.PortadaLogoUrl).HasMaxLength(500);
            b.Property(x => x.PortadaTitulo).HasMaxLength(200);
            b.Property(x => x.PortadaSubtitulo).HasMaxLength(300);
            b.Property(x => x.PortadaTextoLegal).HasMaxLength(2000);
            b.Property(x => x.PatronNombreDefault).HasMaxLength(200);
            b.HasIndex(x => new { x.TenantId, x.AseguradoraId }).IsUnique();
        });

        modelBuilder.Entity<AseguradoraInformeItem>(b =>
        {
            b.ToTable("aseguradora_informe_items");
            b.Property(x => x.Alias).HasMaxLength(20).IsRequired();
            b.Property(x => x.Descripcion).HasMaxLength(300);
            b.Property(x => x.Seccion).HasMaxLength(80);
            b.Property(x => x.PatronNombre).HasMaxLength(200);
            b.Property(x => x.Origen).HasConversion<int>();
            b.HasIndex(x => new { x.TenantId, x.ConfigId, x.Orden });
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
            b.Property(x => x.FirmaUrl).HasColumnType("text");
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
            // Codigos RIPS Res 2275 — string directo (spec Facturacion §5.10).
            b.Property(x => x.Finalidad).HasMaxLength(10);
            b.Property(x => x.CausaExterna).HasMaxLength(10);
            b.Property(x => x.ModalidadAtencion).HasMaxLength(10);
            b.Property(x => x.ViaIngreso).HasMaxLength(10);
            b.Property(x => x.GrupoServicios).HasMaxLength(10);
            b.Property(x => x.Servicios).HasMaxLength(10);
            b.Property(x => x.ValorTotal).HasPrecision(14, 2);
            b.HasOne(x => x.Contrato).WithMany().HasForeignKey(x => x.ContratoId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Paquete).WithMany().HasForeignKey(x => x.PaqueteId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.ContratoId });
            b.HasIndex(x => new { x.TenantId, x.PaqueteId });
        });

        modelBuilder.Entity<Paquete>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(40).IsRequired();
            b.Property(x => x.Nombre).HasMaxLength(500).IsRequired();
            b.Property(x => x.Precio).HasPrecision(14, 2);
            // FK opcional al PaqueteServicio representativo para facturacion (spec §5.9).
            b.HasOne(x => x.CupsRepresentativoServicio)
                .WithMany()
                .HasForeignKey(x => x.CupsRepresentativoServicioId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.TenantId, x.Codigo }).IsUnique();
        });

        modelBuilder.Entity<PaqueteServicio>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(60).IsRequired();
            b.HasOne(x => x.Paquete)
                .WithMany(p => p.Servicios)
                .HasForeignKey(x => x.PaqueteId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.CatalogoServicioReferencia)
                .WithMany()
                .HasForeignKey(x => x.CatalogoServicioReferenciaId)
                .OnDelete(DeleteBehavior.SetNull);
            // Un codigo no puede repetirse dentro de un paquete (para subir cantidad, se
            // edita la fila existente en vez de agregar duplicados).
            b.HasIndex(x => new { x.TenantId, x.PaqueteId, x.Codigo }).IsUnique();
        });

        modelBuilder.Entity<CuotaCopago>(b =>
        {
            b.Property(x => x.Tipo).HasMaxLength(20).IsRequired();
            b.Property(x => x.Categoria).HasMaxLength(80).IsRequired();
            b.Property(x => x.ValorSugerido).HasPrecision(14, 2);
            b.Property(x => x.Descripcion).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.Tipo, x.Categoria }).IsUnique();
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
            b.Property(x => x.CodigoHabilitacion).HasMaxLength(20);
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
            b.Property(x => x.CodigoAceptacion).HasMaxLength(120);
            // Clasificaciones (texto libre por ahora)
            b.Property(x => x.Incapacidad).HasMaxLength(60);
            b.Property(x => x.GrupoRh).HasMaxLength(10);
            b.Property(x => x.Estado).HasMaxLength(120);
            b.Property(x => x.EstratoSocial).HasMaxLength(20);
            b.Property(x => x.Sexo).HasMaxLength(20);
            b.Property(x => x.EstadoCivil).HasMaxLength(120);
            b.Property(x => x.Zona).HasMaxLength(120);
            b.Property(x => x.Ocupacion).HasMaxLength(120);
            b.Property(x => x.Regimen).HasMaxLength(120);
            b.Property(x => x.Tutela).HasMaxLength(120);
            // Diagnostico
            b.Property(x => x.DiagnosticoPrincipal).HasMaxLength(500);
            b.Property(x => x.Cie10Codigo).HasMaxLength(30);
            // Geografia
            b.Property(x => x.Direccion).HasMaxLength(300);
            b.Property(x => x.Barrio).HasMaxLength(120);
            b.Property(x => x.Ciudad).HasMaxLength(120);
            // Contacto
            b.Property(x => x.CodigoPaisTelefono).HasMaxLength(5);
            b.Property(x => x.Telefono).HasMaxLength(120);
            b.Property(x => x.Email).HasMaxLength(160);
            // Emergencia (legacy: primer contacto duplicado en columnas planas)
            b.Property(x => x.ContactoEmergencia).HasMaxLength(200);
            b.Property(x => x.Parentesco).HasMaxLength(80);
            b.Property(x => x.TelefonoEmergencia).HasMaxLength(120);
            // FKs concretas (las catalogo se haran luego)
            b.HasOne(x => x.Aseguradora).WithMany().HasForeignKey(x => x.AseguradoraId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.SedeAtencion).WithMany().HasForeignKey(x => x.SedeAtencionId).OnDelete(DeleteBehavior.SetNull);
            b.HasMany(x => x.ContactosEmergencia).WithOne(x => x.Paciente!)
                .HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.NumeroDocumento }).IsUnique();
            // Estado de admision del paciente. Default "Abierto" para pacientes existentes.
            b.Property(x => x.EstadoAdmision).HasMaxLength(20).HasDefaultValue("Abierto").IsRequired();
            b.HasIndex(x => new { x.TenantId, x.EstadoAdmision });
        });

        modelBuilder.Entity<PacienteContactoEmergencia>(b =>
        {
            b.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            b.Property(x => x.Parentesco).HasMaxLength(80);
            b.Property(x => x.CodigoPais).HasMaxLength(5).IsRequired();
            b.Property(x => x.Telefono).HasMaxLength(40);
            // Firma como data URL (image/png base64) — puede pesar decenas de
            // KB, asi que usamos text sin cota para tolerar canvases grandes.
            b.Property(x => x.FirmaUrl).HasColumnType("text");
            b.HasIndex(x => x.PacienteId);
        });

        modelBuilder.Entity<TenantUser>(b =>
        {
            b.HasOne(x => x.Rol).WithMany().HasForeignKey(x => x.RolId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Sucursal).WithMany().HasForeignKey(x => x.SucursalId).OnDelete(DeleteBehavior.SetNull);
            b.HasMany(x => x.Sucursales).WithOne(x => x.TenantUser!).HasForeignKey(x => x.TenantUserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Profesional).WithMany().HasForeignKey(x => x.ProfesionalId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => x.ProfesionalId).IsUnique().HasFilter("profesional_id IS NOT NULL");
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

        modelBuilder.Entity<AsignacionLote>(b =>
        {
            b.Property(x => x.Sucursal).HasMaxLength(40).IsRequired();
            b.Property(x => x.ContratoCodigo).HasMaxLength(60).IsRequired();
            b.HasOne(x => x.Paciente).WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Items).WithOne(x => x.Lote!).HasForeignKey(x => x.LoteId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.PacienteId });
        });

        modelBuilder.Entity<Asignacion>(b =>
        {
            b.Property(x => x.Sucursal).HasMaxLength(40).IsRequired();
            b.Property(x => x.ServicioId).HasMaxLength(60).IsRequired();
            b.Property(x => x.NombreServicio).HasMaxLength(200).IsRequired();
            b.Property(x => x.TipoServicio).HasMaxLength(40).IsRequired();
            b.Property(x => x.Modulo).HasMaxLength(40);
            b.Property(x => x.ContratoCodigo).HasMaxLength(60).IsRequired();
            b.Property(x => x.CodigoAutorizacion).HasMaxLength(60);
            b.Property(x => x.FormatoHistoria).HasMaxLength(60);
            b.Property(x => x.PdfAutorizacionUrl).HasMaxLength(500);
            b.Property(x => x.TipoPago).HasMaxLength(20);
            b.Property(x => x.CategoriaCopago).HasMaxLength(80);
            b.Property(x => x.ValorPagoSugerido).HasPrecision(14, 2);
            b.Property(x => x.ValorPagoReal).HasPrecision(14, 2);
            b.Property(x => x.Estado).HasMaxLength(30).IsRequired();
            b.Property(x => x.PaqueteCodigo).HasMaxLength(40);
            b.Property(x => x.PaqueteValorPactado).HasPrecision(14, 2);
            b.HasOne(x => x.Paciente).WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Restrict);
            b.HasCheckConstraint("ck_asignaciones_cantidad", "cantidad > 0");
            b.HasIndex(x => new { x.TenantId, x.PacienteId });
            b.HasIndex(x => new { x.TenantId, x.Estado, x.MesVigencia, x.AnioServicio });
            b.HasIndex(x => x.LoteId);
            b.HasIndex(x => x.PaqueteInstanciaId);
        });

        modelBuilder.Entity<Cie11Config>(b =>
        {
            b.Property(x => x.TokenUrl).HasMaxLength(300);
            b.Property(x => x.ClientId).HasMaxLength(200);
            b.Property(x => x.ClientSecret).HasMaxLength(400);
            b.Property(x => x.SearchUrl).HasMaxLength(300);
            b.Property(x => x.MmsUrlBase).HasMaxLength(300);
            // Una sola fila de config por tenant.
            b.HasIndex(x => x.TenantId).IsUnique();
        });

        modelBuilder.Entity<Pais>(b =>
        {
            b.Property(x => x.Codigo).HasMaxLength(10).IsRequired();
            b.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            b.HasIndex(x => x.Codigo).IsUnique();
        });

        modelBuilder.Entity<Departamento>(b =>
        {
            b.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            b.HasOne(x => x.Pais).WithMany().HasForeignKey(x => x.PaisId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.PaisId, x.Nombre }).IsUnique();
            b.HasIndex(x => new { x.PaisId, x.ExternalId });
        });

        modelBuilder.Entity<Municipio>(b =>
        {
            b.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            b.HasOne(x => x.Departamento).WithMany().HasForeignKey(x => x.DepartamentoId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.DepartamentoId, x.Nombre }).IsUnique();
        });

        modelBuilder.Entity<InteroperabilidadConfig>(b =>
        {
            b.Property(x => x.EndpointSandbox).HasMaxLength(500);
            b.Property(x => x.EndpointProduccion).HasMaxLength(500);
            b.Property(x => x.AzureTenantId).HasMaxLength(80);
            b.Property(x => x.Scope).HasMaxLength(400);
            // Secretos cifrados con Data Protection: el ciphertext crece ~3-4x, dejamos margen.
            b.Property(x => x.ApimSubskeySandboxCifrada).HasColumnType("text");
            b.Property(x => x.ApimSubskeyProduccionCifrada).HasColumnType("text");
            // Paths de operaciones FHIR custom del API IHCE.
            b.Property(x => x.PathEnvioRda).HasMaxLength(200).IsRequired();
            b.Property(x => x.PathEnvioRdaConsulta).HasMaxLength(200).IsRequired();
            b.Property(x => x.PathConsultarPaciente).HasMaxLength(200).IsRequired();
            b.Property(x => x.PathConsultarProfesional).HasMaxLength(200).IsRequired();
            // Una sola fila de configuracion por tenant.
            b.HasIndex(x => x.TenantId).IsUnique();
        });

        modelBuilder.Entity<InteroperabilidadCredencialSede>(b =>
        {
            b.Property(x => x.CodigoHabilitacion).HasMaxLength(20);
            b.Property(x => x.NombreLlave).HasMaxLength(200);
            b.Property(x => x.ClientId).HasMaxLength(80);
            b.Property(x => x.ClientSecretCifrado).HasColumnType("text");
            b.HasOne(x => x.Sucursal).WithMany().HasForeignKey(x => x.SucursalId)
                .OnDelete(DeleteBehavior.Cascade);
            // Una credencial por sucursal x ambiente.
            b.HasIndex(x => new { x.TenantId, x.SucursalId, x.Ambiente }).IsUnique();
        });

        modelBuilder.Entity<RdaEvento>(b =>
        {
            // El Bundle FHIR completo como texto. Postgres jsonb seria mas rico pero
            // FHIR exige orden estable de propiedades y jsonb no lo respeta — text mantiene
            // el JSON canonico identico al hash calculado.
            b.Property(x => x.BundleJson).HasColumnType("text").IsRequired();
            // SHA-256 hex (64 chars). Limitamos para que el indice unico sea barato.
            b.Property(x => x.BundleHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.ReferenciaMinsalud).HasMaxLength(120);
            b.Property(x => x.ErroresJson).HasColumnType("jsonb");

            b.HasOne(x => x.HistoriaClinica).WithMany().HasForeignKey(x => x.HistoriaClinicaId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Paciente).WithMany().HasForeignKey(x => x.PacienteId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Profesional).WithMany().HasForeignKey(x => x.ProfesionalId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Sucursal).WithMany().HasForeignKey(x => x.SucursalId)
                .OnDelete(DeleteBehavior.Restrict);

            // Encontrar rapido el RDA activo de una HC.
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId, x.Estado });
            // Idempotencia: el mismo contenido no se procesa dos veces.
            b.HasIndex(x => new { x.TenantId, x.BundleHash }).IsUnique();
            // Feed de la lista en /interoperabilidad/rda (Ola 4): ultimos eventos por estado.
            b.HasIndex(x => new { x.TenantId, x.Estado, x.FechaGeneracion });
        });

        modelBuilder.Entity<FacturacionSnapshot>(b =>
        {
            b.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            b.Property(x => x.MotivoArchivado).HasMaxLength(1000);
            b.Property(x => x.ErrorMensaje).HasMaxLength(4000);
            // Filtros del snapshot como jsonb — permite index gin/consultas futuras y
            // el motor no necesita saber la forma concreta por tipo.
            b.Property(x => x.FiltrosJson).HasColumnType("jsonb").IsRequired();

            // Listado principal en /facturacion-clinica/snapshots: tab Vigentes/Archivados
            // ordenado por CreatedAt DESC.
            b.HasIndex(x => new { x.TenantId, x.Estado, x.CreatedAt });
            // Filtro por Tipo en la misma pagina.
            b.HasIndex(x => new { x.TenantId, x.Tipo, x.Estado });
        });

        modelBuilder.Entity<FacturacionSnapshotFila>(b =>
        {
            // El corazon del motor: cada fila del snapshot se persiste con su carga
            // como jsonb. Consultas de la UI hacen LIMIT/OFFSET y opcionalmente
            // WHERE DatosJson::text ILIKE '%buscar%' — barato para N por debajo de
            // 100k filas por snapshot.
            b.Property(x => x.DatosJson).HasColumnType("jsonb").IsRequired();

            b.HasOne(x => x.Snapshot).WithMany(x => x.Filas)
                .HasForeignKey(x => x.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);

            // Orden natural + paginacion server-side. Unico por snapshot para que
            // el builder pueda re-emitir la misma numeracion si retry.
            b.HasIndex(x => new { x.SnapshotId, x.NumeroFila }).IsUnique();
        });

        modelBuilder.Entity<RevisionClinica>(b =>
        {
            b.HasOne(x => x.HistoriaClinica).WithMany().HasForeignKey(x => x.HistoriaClinicaId).OnDelete(DeleteBehavior.Restrict);

            // UNIQUE por HC dentro del tenant: una sola revision viva por HC.
            b.HasIndex(x => new { x.TenantId, x.HistoriaClinicaId }).IsUnique();

            // Feed de /ordenes tab Kanban y contador por columna. El estado agregado
            // se cachea en la cabecera para no scannear la bitacora en cada refresh.
            b.HasIndex(x => new { x.TenantId, x.EstadoAgregado, x.UltimaAccionEn });
        });

        modelBuilder.Entity<RevisionClinicaEvento>(b =>
        {
            b.Property(x => x.ActorAgenteCodigo).HasMaxLength(60);
            b.Property(x => x.Motivo).HasMaxLength(1000);
            b.Property(x => x.Nota).HasMaxLength(4000);
            // Payload estructurado opcional en jsonb; el agente lo puede indexar despues.
            b.Property(x => x.PayloadJson).HasColumnType("jsonb");

            b.HasOne(x => x.RevisionClinica).WithMany(x => x.Eventos)
                .HasForeignKey(x => x.RevisionClinicaId)
                .OnDelete(DeleteBehavior.Cascade);

            // Feed de la bitacora del modal HC — orden cronologico descendente.
            b.HasIndex(x => new { x.RevisionClinicaId, x.OcurridoEn });
        });

        modelBuilder.Entity<RevisionPolicy>(b =>
        {
            b.Property(x => x.UmbralConfianza).HasPrecision(4, 3);
            // Singleton por tenant — una fila max por TenantId.
            b.HasIndex(x => x.TenantId).IsUnique();
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
