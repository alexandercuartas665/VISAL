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
            // La cedula 13069774 pertenece a JESUS ALBERTO TORO (owner real de Visal IPS RT)
            // y se carga via EnsureVisalRealUsersAsync. Si demo-admin la tenia, liberarla.
            if (u.Email == TenantAdminEmail && u.Documento == "13069774")
            {
                u.Documento = null;
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

    // Carga los usuarios reales del archivo maestro de Visal IPS RT con rol "Coordinador"
    // (V/C/E sobre todos los modulos) y acceso a TODAS las sedes activas. Idempotente:
    // identifica usuarios por cedula y los omite si ya existen. Clave inicial = cedula.
    public async Task EnsureVisalRealUsersAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        // 1) Rol "Coordinador" con V/C/E (sin eliminar) en todos los modulos.
        var coord = await _db.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Nombre == "Coordinador", cancellationToken);
        if (coord is null)
        {
            coord = new Rol
            {
                TenantId = tenant.Id,
                Nombre = "Coordinador",
                Descripcion = "Coordinador de operacion clinica. V/C/E en todos los modulos.",
                Activo = true
            };
            _db.Roles.Add(coord);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var existentes = await _db.RolPermisos.IgnoreQueryFilters()
            .Where(p => p.RolId == coord.Id).ToListAsync(cancellationToken);
        _db.RolPermisos.RemoveRange(existentes);
        foreach (var modulo in ModuloCatalogo.Todos)
        {
            _db.RolPermisos.Add(new RolPermiso
            {
                TenantId = tenant.Id,
                RolId = coord.Id,
                Modulo = modulo.Key,
                Ver = true, Crear = true, Editar = true, Eliminar = false
            });
        }
        await _db.SaveChangesAsync(cancellationToken);

        // 2) Sedes activas del tenant (se asignaran TODAS a cada usuario).
        var sedes = await _db.Sucursales.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenant.Id && s.Activo)
            .ToListAsync(cancellationToken);

        // 3) Lista maestra: (nombre, cedula, email, usuario).
        (string nombre, string cedula, string? email, string usuario)[] usuarios =
        {
            ("ANA MATILDE TORO ANDRADE", "59177247", "visalrtsas@gmail.com", "ANA.TORO"),
            ("ANDRES EDUARDO BRAVO VELASQUEZ", "1085257309", "acuartas@bitcode.com.co", "ANDRES.BRAVO"),
            ("Angela Maria Lopez Benavides", "1086133424", "homecarevisalrtsas.pasto@gmail.com", "ANGELA.LOPEZ"),
            ("CAROLINA ROMERO CHAPARRO", "1022969566", "visalequiposmedicos@gmail.com", "CAROLINA.CHAPARRO"),
            ("CHRISTIAN LINARES GOMEZ", "1130636792", "usuario@visal.com.co", "CHRISTIAN.GOMEZ"),
            ("CLON JESUS ALBERTO TORO", "C80001976", null, "CLON.JESUS.TORO"),
            ("CLON VALERIA URIBE RESTREPO", "C1002924632", "CamiloTDH24@gmail.com", "CLON.VALERIA.RESTREPO"),
            ("DANIELA LOPEZ", "1059915437", "autorizaciones.visalrt@gmail.com", "DANIELA.LOPEZ"),
            ("DAYANA ROSERO", "1085335046", "homecarevisalrtsas.pasto@gmail.com", "DAYANA.ROSERO"),
            ("FERNANDA ZAMBRANO", "1086137278", "correo@visal.com.co", "FERNANDA.ZAMBRANO"),
            ("JESUS ALBERTO TORO", "13069774", "talentohumano@visalrtsas.com.co", "JESUS.TORO"),
            ("LILIANA TORO", "1023003747", "visaltesoreria@gmail.com", "LILIANA.TORO"),
            ("LUISA TORO", "1006009064", "homecare.visalrtsas@gmail.com", "LUISA.TORO"),
            ("MAIRA ALEJANDRA USCATEGUI", "1085291717", "terapiasvisalpasto@gmail.com", "MAIRA.USCATEGUI"),
            ("MARIO SEBASTIAN RUBIO TORO", "1031171951", "visalrtsas@gmail.com", "MARIO.RUBIO"),
            ("MARTHA ORTIZ", "59835952", "coordinacionpad.visalrt@gmail.com", "MARTHA.ORTIZ"),
            ("MERLIN MOSQUERA", "1105368044", "terapiasvisalrt2@gmail.com", "MERLIN.MOSQUERA"),
            ("MONICA GUERRERO", "1086133758", "insumovisalpasto@gmail.com", "MONICA.GUERRERO"),
            ("NATALY ARTEGA", "1085921101", "atencionalusuario.visalrt@gmail.com", "NATALY.ARTEGA"),
            ("NATHALIA MAZO", "1005867637", "gestionpositivavisalcali@gmail.com", "NATHALIA.MAZO"),
            ("NATHALIA ISABEL CAICEDO ROJAS", "1086133791", "correo@visal.com.co", "NATHALIA.CAICEDO"),
            ("NORVI ORLANDI MUÑOZ CORDOBA", "1085662574", "homecarevisalrt.popayan@gmail.com", "NORVI.MUNOZ"),
            ("PAOLA ANDREA BURGOS MENESES", "1125180774", "correo@visal.com.co", "PAOLA.BURGOS"),
            ("VALERIA URIBE RESTREPO", "1109661916", "valeriauribecbo@gmail.com", "VALERIA.RESTREPO"),
            ("VICKY MUÑOZ", "1088970257", "fact.visalrtsas@gmail.com", "VICKY.MUNOZ"),
            ("Yenifer Astrid Burbano Erazo", "1085315888", "asignacion.visal.arl.pasto@gmail.com", "YENIFER.BURBANO"),
            ("YESSICA GUERRERO", "1088976322", "enfvisrt@outlook.com", "YESSICA.GUERRERO")
        };

        // Set de emails ya en BD para evitar colisiones (constraint unique).
        var emailsEnBd = (await _db.PlatformUsers.IgnoreQueryFilters()
            .Select(p => p.Email).ToListAsync(cancellationToken))
            .Select(e => e.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int agregados = 0;
        foreach (var (nombre, cedula, emailRaw, usuario) in usuarios)
        {
            // Idempotencia: si ya existe por cedula, saltar.
            var existePorDoc = await _db.PlatformUsers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Documento == cedula, cancellationToken);
            if (existePorDoc is not null) { continue; }

            // Resolver email evitando colisiones.
            var emailLower = (emailRaw ?? "").Trim().ToLowerInvariant();
            string email;
            if (string.IsNullOrEmpty(emailLower) || emailsEnBd.Contains(emailLower))
            {
                var slug = usuario.ToLowerInvariant().Trim('.').Replace(' ', '.');
                email = $"{slug}@visal.local";
                int n = 1;
                var baseEmail = email;
                while (emailsEnBd.Contains(email))
                {
                    email = baseEmail.Replace("@", $"{n}@");
                    n++;
                }
            }
            else
            {
                email = emailLower;
            }
            emailsEnBd.Add(email);

            // PlatformUser (identidad). Clave inicial = cedula.
            var pu = new PlatformUser
            {
                Email = email,
                DisplayName = nombre,
                Documento = cedula,
                EmailVerified = true,
                AuthProvider = "local",
                PasswordHash = _hasher.Hash(cedula),
                Status = PlatformUserStatus.Active,
                EsGlobal = false
            };
            _db.PlatformUsers.Add(pu);
            await _db.SaveChangesAsync(cancellationToken);

            // TenantUser (membresia) con rol Coordinador.
            var tu = new TenantUser
            {
                TenantId = tenant.Id,
                PlatformUserId = pu.Id,
                Email = email,
                TenantRole = TenantRole.Advisor,
                Status = PlatformUserStatus.Active,
                RolId = coord.Id
            };
            _db.TenantUsers.Add(tu);
            await _db.SaveChangesAsync(cancellationToken);

            // Asignar TODAS las sedes activas del tenant.
            foreach (var s in sedes)
            {
                _db.TenantUserSucursales.Add(new TenantUserSucursal
                {
                    TenantId = tenant.Id,
                    TenantUserId = tu.Id,
                    SucursalId = s.Id
                });
            }
            await _db.SaveChangesAsync(cancellationToken);
            agregados++;
        }

        _logger.LogInformation("Usuarios reales Visal cargados: {N} nuevos (de {T} en archivo maestro). Rol: Coordinador. Sedes: todas ({S}).",
            agregados, usuarios.Length, sedes.Count);
    }

    // Siembra valores por defecto de los catalogos del modulo Configuracion Pacientes
    // segun el sistema original de Visal IPS RT. Idempotente: solo agrega los que no
    // existan (verificacion por tenant + tipo + codigo).
    public async Task EnsureCatalogosPacienteDefaultAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Kind == TenantKind.Demo, cancellationToken);
        if (tenant is null) { return; }

        // Datos extraidos de las capturas del sistema legacy Visal.
        (Visal.Domain.Enums.CatalogoPacienteTipo tipo, string codigo, string nombre)[] items =
        {
            // ===== TIPO USUARIO =====
            (Visal.Domain.Enums.CatalogoPacienteTipo.TipoUsuario, "COT", "Cotizante"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.TipoUsuario, "BEN", "Beneficiario"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.TipoUsuario, "SUB", "Subsidiado"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.TipoUsuario, "VIN", "Vinculado"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.TipoUsuario, "PAR", "Particular"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.TipoUsuario, "PRE", "Prepagada"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.TipoUsuario, "RES", "Regimen Especial"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.TipoUsuario, "MIG", "Poblacion Migrante"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.TipoUsuario, "NAF", "No afiliado"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.TipoUsuario, "EXC", "Excepcion"),

            // ===== CLASIFICACION PACIENTE =====
            (Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionPaciente, "AGU", "AGUDO"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionPaciente, "CRO", "CRONICO"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionPaciente, "CRV", "CRONICO CON VENTILADOR"),

            // ===== CLASIFICACION GRUPO DE PATOLOGIA =====
            (Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionGrupoPatologia, "RES", "RESPIRATORIO"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionGrupoPatologia, "CAR", "CARDIOVASCULAR"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionGrupoPatologia, "HEP", "HEPATICA"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionGrupoPatologia, "REN", "RENAL Y METABOLICA"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionGrupoPatologia, "NEU", "NEUROLOGICA"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionGrupoPatologia, "OST", "OSTEOARTICULAR"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionGrupoPatologia, "INF", "INFECCIOSAS"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionGrupoPatologia, "ONC", "ONCOLOGICA"),
            (Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionGrupoPatologia, "MEN", "MENTAL")
        };

        int agregados = 0;
        foreach (var (tipo, codigo, nombre) in items)
        {
            var existe = await _db.CatalogosPaciente.IgnoreQueryFilters()
                .AnyAsync(c => c.TenantId == tenant.Id && c.Tipo == tipo && c.Codigo == codigo, cancellationToken);
            if (existe) { continue; }
            _db.CatalogosPaciente.Add(new CatalogoPaciente
            {
                TenantId = tenant.Id,
                Tipo = tipo,
                Codigo = codigo,
                Nombre = nombre,
                Activo = true
            });
            agregados++;
        }
        if (agregados > 0) { await _db.SaveChangesAsync(cancellationToken); }
        _logger.LogInformation("Catalogos Paciente: {N} items por defecto (de {T} en lista maestra).", agregados, items.Length);
    }
}
