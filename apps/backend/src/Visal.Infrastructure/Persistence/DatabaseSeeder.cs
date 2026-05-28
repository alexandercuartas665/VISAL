using Visal.Application.Common.Auth;
using Visal.Application.Tenancy;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Visal.Infrastructure.Persistence;

/// <summary>
/// Siembra datos iniciales de desarrollo de forma idempotente: un Super Admin, un plan,
/// una agencia demo con su administrador y una suscripcion. Solo crea si la base esta vacia.
/// </summary>
public sealed class DatabaseSeeder
{
    public const string SuperAdminEmail = "admin@visal.travels";
    public const string SuperAdminPassword = "Admin123*";
    public const string TenantAdminEmail = "demo-admin@visal.travels";
    public const string TenantAdminPassword = "Demo123*";

    private readonly VisalDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(VisalDbContext db, IPasswordHasher hasher, ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _db.PlatformUsers.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            return;
        }

        var superAdmin = new PlatformUser
        {
            Email = SuperAdminEmail,
            EmailVerified = true,
            DisplayName = "Super Admin",
            Status = PlatformUserStatus.Active,
            PlatformRole = PlatformRole.SuperAdmin,
            PasswordHash = _hasher.Hash(SuperAdminPassword)
        };

        var plan = new SaasPlan
        {
            Name = "Plan Inicial",
            Description = "Plan de arranque para agencias pequenas.",
            MonthlyPrice = 99000m,
            YearlyPrice = 990000m,
            Currency = "COP",
            IsActive = true,
            Limits =
            [
                new SaasPlanLimit { LimitKey = "max_users", LimitValue = 10, LimitUnit = "users", EnforcementMode = LimitEnforcementMode.Hard },
                new SaasPlanLimit { LimitKey = "max_whatsapp_lines", LimitValue = 2, LimitUnit = "lines", EnforcementMode = LimitEnforcementMode.Hard },
                new SaasPlanLimit { LimitKey = "max_ai_tokens_monthly", LimitValue = 100000, LimitUnit = "tokens", EnforcementMode = LimitEnforcementMode.Soft }
            ]
        };

        var tenant = new Tenant
        {
            Name = "Agencia Demo",
            LegalName = "Agencia Demo SAS",
            TaxId = "900123456-7",
            Country = "CO",
            Currency = "COP",
            Status = TenantStatus.Active,
            Kind = TenantKind.Demo
        };

        var tenantAdmin = new PlatformUser
        {
            Email = TenantAdminEmail,
            EmailVerified = true,
            DisplayName = "Administrador Agencia Demo",
            Status = PlatformUserStatus.Active,
            PasswordHash = _hasher.Hash(TenantAdminPassword)
        };

        _db.PlatformUsers.AddRange(superAdmin, tenantAdmin);
        _db.SaasPlans.Add(plan);
        _db.Tenants.Add(tenant);

        _db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            BillingFrequency = BillingFrequency.Monthly,
            StartsAt = DateTimeOffset.UtcNow,
            CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1)
        });

        _db.TenantUsers.Add(new TenantUser
        {
            TenantId = tenant.Id,
            PlatformUserId = tenantAdmin.Id,
            Email = TenantAdminEmail,
            TenantRole = TenantRole.Owner,
            Status = PlatformUserStatus.Active
        });

        _db.TenantConfigurations.AddRange(
            new TenantConfiguration { TenantId = tenant.Id, ConfigKey = "tono", ConfigValue = "cordial" },
            new TenantConfiguration { TenantId = tenant.Id, ConfigKey = "horario", ConfigValue = "8-18" });

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Seed inicial creado. Super Admin: {SuperAdmin} / {SuperPass}. Admin agencia: {TenantAdmin} / {TenantPass}",
            SuperAdminEmail, SuperAdminPassword, TenantAdminEmail, TenantAdminPassword);
    }

    // Recursos de ejemplo (imagenes) de la galeria de plantillas para la agencia demo. Idempotente:
    // solo registra si la agencia aun no tiene recursos. Se llama en cada arranque de Desarrollo.
    public async Task EnsureDemoTemplateAssetsAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        if (await _db.TemplateAssets.IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenant.Id, cancellationToken))
        {
            return;
        }

        (string name, string file)[] assets =
        {
            ("Logo agencia", "demo-logo.svg"),
            ("Hotel (foto)", "demo-hotel.svg"),
            ("Avianca (aerolinea)", "demo-avianca.svg"),
            ("Icono Vuelos", "demo-icon-vuelo.svg"),
            ("Icono Traslados", "demo-icon-traslado.svg"),
            ("Icono Hotel", "demo-icon-hotel.svg"),
            ("Icono Asistencia", "demo-icon-salud.svg")
        };
        foreach (var (name, file) in assets)
        {
            _db.TemplateAssets.Add(new TemplateAsset
            {
                TenantId = tenant.Id,
                FileName = name,
                Url = $"/uploads/templates/{file}",
                MimeType = "image/svg+xml",
                SizeBytes = 600
            });
        }
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Recursos demo de la galeria de plantillas registrados ({Count}).", assets.Length);
    }

    // Garantiza que cada tenant tenga un rol "Administrador" con TODOS los permisos de TODOS
    // los modulos del catalogo, y lo asigna a los TenantUsers que sean Owner (o no tengan rol).
    // Tambien marca como global a los usuarios admin@visal.travels y demo-admin@visal.travels.
    // Idempotente: se ejecuta en cada arranque de desarrollo sin duplicar datos.
    public async Task EnsureAdministradorRolAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters().ToListAsync(cancellationToken);
        foreach (var tenant in tenants)
        {
            // 1) Asegurar rol "Administrador" para el tenant.
            var rol = await _db.Roles.IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Nombre == "Administrador", cancellationToken);
            if (rol is null)
            {
                rol = new Rol
                {
                    TenantId = tenant.Id,
                    Nombre = "Administrador",
                    Descripcion = "Acceso total a todos los modulos del sistema.",
                    Activo = true
                };
                _db.Roles.Add(rol);
                await _db.SaveChangesAsync(cancellationToken);
            }

            // 2) Sincronizar permisos: borrar y reinsertar con todo en true.
            var existentes = await _db.RolPermisos.IgnoreQueryFilters()
                .Where(p => p.RolId == rol.Id).ToListAsync(cancellationToken);
            _db.RolPermisos.RemoveRange(existentes);
            foreach (var modulo in ModuloCatalogo.Todos)
            {
                _db.RolPermisos.Add(new RolPermiso
                {
                    TenantId = tenant.Id,
                    RolId = rol.Id,
                    Modulo = modulo.Key,
                    Ver = true, Crear = true, Editar = true, Eliminar = true
                });
            }
            await _db.SaveChangesAsync(cancellationToken);

            // 3) Asignar el rol a los TenantUsers Owner o sin rol del tenant.
            var users = await _db.TenantUsers.IgnoreQueryFilters()
                .Where(tu => tu.TenantId == tenant.Id)
                .ToListAsync(cancellationToken);
            foreach (var u in users)
            {
                if (u.RolId is null || u.TenantRole == TenantRole.Owner)
                {
                    u.RolId = rol.Id;
                }
            }
            await _db.SaveChangesAsync(cancellationToken);
        }

        // 4) Marcar admin@visal.travels y demo-admin@visal.travels como globales y asignarles cedula demo.
        var globales = await _db.PlatformUsers.IgnoreQueryFilters()
            .Where(u => u.Email == SuperAdminEmail || u.Email == TenantAdminEmail)
            .ToListAsync(cancellationToken);
        foreach (var u in globales)
        {
            u.EsGlobal = true;
            // Cedula demo para login por documento (igual a la captura de la solicitud).
            if (string.IsNullOrWhiteSpace(u.Documento) && u.Email == TenantAdminEmail)
            {
                u.Documento = "13069774";
            }
        }
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Rol 'Administrador' garantizado con todos los permisos en {N} tenant(s).", tenants.Count);
    }

    // Asegura las sedes principales de Visal IPS RT (IBAGUE, NARIÑO, PASTO, POPAYAN, SANTIAGO DE CALI)
    // en el tenant demo. Desactiva las sedes legacy S001 "Sede Cali" y S002 "Sede Bogota" si existen
    // con esos nombres. Idempotente: solo agrega las que faltan, no toca otras sedes del cliente.
    public async Task EnsureSedesVisalAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        // Desactivar sedes legacy del seed inicial.
        var legacy = await _db.Sucursales.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenant.Id && (
                (s.Codigo == "S001" && s.Nombre == "Sede Cali") ||
                (s.Codigo == "S002" && s.Nombre == "Sede Bogota")))
            .ToListAsync(cancellationToken);
        foreach (var s in legacy) { s.Activo = false; }

        (string codigo, string nombre, string ciudad)[] visalSedes =
        {
            ("IBA", "IBAGUE", "IBAGUE"),
            ("NAR", "NARIÑO", "PASTO"),
            ("PAS", "PASTO", "PASTO"),
            ("POP", "POPAYAN", "POPAYAN"),
            ("SCL", "SANTIAGO DE CALI", "SANTIAGO DE CALI")
        };
        foreach (var (codigo, nombre, ciudad) in visalSedes)
        {
            var existente = await _db.Sucursales.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == tenant.Id && s.Codigo == codigo, cancellationToken);
            if (existente is null)
            {
                _db.Sucursales.Add(new Sucursal
                {
                    TenantId = tenant.Id,
                    Codigo = codigo,
                    Nombre = nombre,
                    Ciudad = ciudad,
                    Activo = true
                });
            }
            else if (!existente.Activo)
            {
                existente.Activo = true;
                existente.Nombre = nombre;
                existente.Ciudad = ciudad;
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Sedes Visal IPS aseguradas en el tenant demo ({N}).", visalSedes.Length);
    }
}
