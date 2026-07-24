using System.Globalization;
using System.Security.Claims;
using Visal.Application;
using Visal.Application.Common;
using Visal.Application.Common.Auth;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Visal.Infrastructure;
using Visal.Infrastructure.Persistence;
using Visal.SuperAdmin.Auth;
using Visal.SuperAdmin.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Serilog: log estructurado a archivo rotativo diario + console. Configurado
// ANTES de builder para captar tambien los eventos de startup. Nivel default
// Information; Microsoft.AspNetCore a Warning (para que aparezcan los errores
// de circuito Blazor que se logean por default en la categoria
// Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost a nivel Error).
// Archivos en logs/visal-YYYYMMDD.log, retencion 30 dias, 100 MB por archivo.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(AppContext.BaseDirectory, "logs", "visal-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 100L * 1024 * 1024,
        rollOnFileSizeLimit: true,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// Formato numerico uniforme en todo el sistema, independiente del locale del servidor (dev o Railway):
// coma = separador de miles, punto = decimal (ej. 3,500,000.50). Evita que el host cambie como se ven los montos.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // Sube el limite de mensajes del circuito SignalR: al arrastrar y soltar archivos al chat,
    // el contenido viaja como base64 por invokeMethodAsync y el limite por defecto (32 KB) lo
    // rechazaba en silencio. 32 MB cubre el tope de 16 MB del archivo (~21 MB en base64).
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 32L * 1024 * 1024);

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorizationBuilder()
    // Operador de plataforma (Super Admin / roles internos): tiene claim platform_role.
    .AddPolicy("PlatformOperator", p => p.RequireClaim("platform_role"))
    // Miembro de una agencia: tiene claim tenant_id.
    .AddPolicy("TenantMember", p => p.RequireClaim("tenant_id"));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddScoped<ITenantContext, CookieUserContext>();

// Chat en tiempo real (SignalR): reemplaza el broadcaster no-op por el real.
builder.Services.AddSignalR();
builder.Services.AddScoped<Visal.Application.Tenancy.IChatBroadcaster, Visal.SuperAdmin.RealTime.SignalRChatBroadcaster>();
// Tunel de desarrollo real (cloudflared); reemplaza el no-op de Application.
builder.Services.AddSingleton<Visal.Application.Tenancy.IDevTunnel, Visal.SuperAdmin.RealTime.CloudflaredTunnel>();
// Storage de archivos servibles (wwwroot/uploads) para que servicios de Application
// puedan persistir binarios (ej. firmas remotas) sin acoplarse a IWebHostEnvironment.
builder.Services.AddSingleton<Visal.Application.Common.IUploadStorage, Visal.SuperAdmin.RealTime.WwwRootUploadStorage>();
// CircuitHandler propio: agrega contexto (usuario + tenant + path) a los logs de
// Blazor Server cuando un circuito se abre, cae o se cierra. Correla con los
// Error que el framework loguea automaticamente cuando una excepcion tumba el
// circuito y dispara el banner "Ha ocurrido un error. Recargar." en el cliente.
builder.Services.AddScoped<CircuitHandler, Visal.SuperAdmin.RealTime.VisalCircuitHandler>();

var app = builder.Build();

// Log request/response de cada peticion HTTP (method, path, status, duracion).
// Sirve para diagnosticar si el error de circuito viene precedido de un 500 en
// una peticion normal (ej. autosave POST).
app.UseSerilogRequestLogging();

// Detras del proxy de Railway (TLS en el borde, HTTP al contenedor): leer
// X-Forwarded-Proto/For para que Request.Scheme sea "https". Asi las cookies
// seguras del login y UseHttpsRedirection funcionan sin bucles de redireccion.
// Debe ir lo antes posible en el pipeline.
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedOptions.KnownNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();

    // En produccion las migraciones NO se aplican solas. Si VISAL_RUN_MIGRATIONS=true
    // (variable de Railway), aplicar las migraciones pendientes al arrancar. Es seguro
    // con una sola instancia web; el seed de demo no corre en produccion.
    if (string.Equals(Environment.GetEnvironmentVariable("VISAL_RUN_MIGRATIONS"), "true", StringComparison.OrdinalIgnoreCase))
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VisalDbContext>();
        await db.Database.MigrateAsync();
    }
}
else
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<VisalDbContext>();
    await db.Database.MigrateAsync();
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
    await seeder.EnsureDemoTemplateAssetsAsync();
    await seeder.EnsureAdministradorRolAsync();
    await seeder.EnsureSedesVisalAsync();
    await seeder.EnsureVisalRealUsersAsync();
    await seeder.EnsureCatalogosPacienteDefaultAsync();
    await seeder.EnsureCie11ConfigAsync();
    await seeder.EnsureTiposTurnoDefaultAsync();

    // Geografia (Pais/Departamento/Municipio) via api-colombia.com. Idempotente.
    // Si la API esta caida, solo registra warning y sigue.
    var geoSeeder = scope.ServiceProvider.GetRequiredService<Visal.Infrastructure.Geo.ApiColombiaSeeder>();
    await geoSeeder.EnsureColombiaAsync();
}

app.UseHttpsRedirection();
// Sirve archivos subidos en tiempo de ejecucion (logos de agencias en wwwroot/uploads).
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapHub<Visal.SuperAdmin.RealTime.ChatHub>("/hubs/chat");

app.MapPost("/auth/login", async (
    HttpContext http,
    [FromForm] string usuario,
    [FromForm] string password,
    [FromForm] string? sede,
    IApplicationDbContext db,
    IPasswordHasher hasher) =>
{
    // Aceptar email o documento (cedula). Si trae '@' lo tratamos como correo.
    var raw = (usuario ?? string.Empty).Trim();
    var lower = raw.ToLowerInvariant();
    PlatformUser? user;
    if (raw.Contains('@'))
    {
        user = await db.PlatformUsers.FirstOrDefaultAsync(u => u.Email == lower);
    }
    else
    {
        user = await db.PlatformUsers.FirstOrDefaultAsync(u => u.Documento == raw);
    }

    if (user is null
        || user.Status != PlatformUserStatus.Active
        || string.IsNullOrEmpty(user.PasswordHash)
        || !hasher.Verify(user.PasswordHash, password ?? string.Empty))
    {
        return Results.Redirect("/login?error=1");
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.DisplayName ?? user.Email),
        new(ClaimTypes.Email, user.Email)
    };

    // Super Admin: ignora la sede seleccionada y va al panel SaaS.
    if (user.PlatformRole is PlatformRole role)
    {
        claims.Add(new Claim("platform_role", role.ToString()));
        var idSuper = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(idSuper));
        return Results.Redirect("/");
    }

    // Memberships del usuario.
    var memberships = await db.TenantUsers.IgnoreQueryFilters()
        .Where(tu => tu.PlatformUserId == user.Id && tu.Status == PlatformUserStatus.Active)
        .OrderBy(tu => tu.CreatedAt)
        .ToListAsync();

    var sedeStr = (sede ?? "").Trim();

    // GLOBAL: solo permitido para usuarios marcados globales. Entra sin sede pero con tenant_id.
    if (string.Equals(sedeStr, "GLOBAL", StringComparison.OrdinalIgnoreCase))
    {
        if (!user.EsGlobal) { return Results.Redirect("/login?error=2"); }
        Guid tenantId;
        TenantRole rol = TenantRole.Owner;
        if (memberships.Count > 0)
        {
            tenantId = memberships[0].TenantId;
            rol = memberships[0].TenantRole;
        }
        else
        {
            // Sin membresia: tomar el primer tenant activo del SaaS.
            var first = await db.Tenants.IgnoreQueryFilters()
                .Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial)
                .OrderBy(t => t.Name)
                .FirstOrDefaultAsync();
            if (first is null) { return Results.Redirect("/login?error=3"); }
            tenantId = first.Id;
        }
        claims.Add(new Claim("tenant_id", tenantId.ToString()));
        claims.Add(new Claim("tenant_role", rol.ToString()));
        claims.Add(new Claim("global_access", "1"));
        // NOTA: No agregamos profesional_id en el flujo global. Ese claim
        // tiene un side-effect (NavMenu lo interpreta como "perfil de campo"
        // y oculta el resto de los modulos). Para firmas se resuelve via
        // FirmaResolverService.ResolverFirmaProfesionalPorPlatformUserAsync
        // a partir del NameIdentifier (platform_user_id) + tenant_id.
        var idGlobal = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(idGlobal));
        return Results.Redirect("/admision");
    }

    // Sede especifica: el usuario eligio en que sucursal trabajar.
    if (Guid.TryParse(sedeStr, out var sucursalId))
    {
        var suc = await db.Sucursales.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == sucursalId && s.Activo);
        if (suc is null) { return Results.Redirect("/login?error=4"); }

        var membership = memberships.FirstOrDefault(m => m.TenantId == suc.TenantId);
        // Verificar que la sede este dentro de las asignadas al usuario, salvo que sea global.
        if (membership is null && !user.EsGlobal) { return Results.Redirect("/login?error=5"); }
        if (membership is not null)
        {
            var asignadas = await db.TenantUserSucursales.IgnoreQueryFilters()
                .Where(x => x.TenantUserId == membership.Id)
                .Select(x => x.SucursalId)
                .ToListAsync();
            if (asignadas.Count > 0 && !asignadas.Contains(sucursalId) && !user.EsGlobal)
            {
                return Results.Redirect("/login?error=6");
            }
        }

        claims.Add(new Claim("tenant_id", suc.TenantId.ToString()));
        claims.Add(new Claim("tenant_role", (membership?.TenantRole ?? TenantRole.Owner).ToString()));
        claims.Add(new Claim("sucursal_id", sucursalId.ToString()));
        if (user.EsGlobal) { claims.Add(new Claim("global_access", "1")); }
        // Si el TenantUser esta vinculado a un Profesional, el claim "profesional_id"
        // marca al usuario como perfil de campo (solo Atencion en el menu lateral).
        if (membership?.ProfesionalId is Guid pidSede)
        {
            claims.Add(new Claim("profesional_id", pidSede.ToString()));
        }
        // Rol + permisos: cargamos los modulos con Ver=true del rol asignado y
        // los inyectamos como claim "perms" (coma-separado). NavMenu filtra las
        // entradas leyendo este claim. Si el usuario no tiene rol (RolId=null),
        // no agregamos "perms" — el menu asume permisos completos por compat.
        if (membership?.RolId is Guid rolId)
        {
            claims.Add(new Claim("rol_id", rolId.ToString()));
            // rol_nombre permite al NavMenu tratar como admin de agencia a usuarios
            // con rol "Administrador" en el sistema nuevo — indepediente del legacy
            // tenant_role, que puede seguir siendo "Advisor" en usuarios migrados.
            var rolNombre = await db.Roles.IgnoreQueryFilters()
                .Where(r => r.Id == rolId).Select(r => r.Nombre).FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(rolNombre))
            {
                claims.Add(new Claim("rol_nombre", rolNombre));
            }
            var permisos = await db.RolPermisos.IgnoreQueryFilters()
                .Where(p => p.RolId == rolId && p.Ver)
                .Select(p => p.Modulo)
                .ToListAsync();
            if (permisos.Count > 0)
            {
                claims.Add(new Claim("perms", string.Join(',', permisos)));
            }
        }
        var idSede = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(idSede));
        // Profesionales van directo a Atencion; el resto (admin/coordinador), a Admision
        // que es el punto de partida natural del flujo clinico.
        return Results.Redirect(membership?.ProfesionalId is not null ? "/atencion" : "/admision");
    }

    // Sin sede valida: fallback al flujo anterior (compatibilidad).
    if (memberships.Count == 1 && !user.EsGlobal)
    {
        var m = memberships[0];
        claims.Add(new Claim("tenant_id", m.TenantId.ToString()));
        claims.Add(new Claim("tenant_role", m.TenantRole.ToString()));
        if (m.ProfesionalId is Guid pidFb) { claims.Add(new Claim("profesional_id", pidFb.ToString())); }
    }
    else
    {
        claims.Add(new Claim("needs_tenant", "1"));
        if (user.EsGlobal) { claims.Add(new Claim("is_global", "1")); }
    }
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect(memberships.Count == 1 && !user.EsGlobal ? "/admision" : "/seleccionar-empresa");
}).DisableAntiforgery();

// Selector de empresa: el usuario eligio un tenant tras el login. Validamos que pueda entrar
// (membership activo, o usuario global con tenant activo), enriquecemos el cookie con
// tenant_id + tenant_role y devolvemos al panel.
app.MapPost("/auth/select-empresa", async (
    HttpContext http,
    [FromForm] Guid tenantId,
    IApplicationDbContext db,
    Visal.Application.Tenancy.IEmpresaSelectorService selector,
    Visal.Application.Tenancy.ISedeSelectorService sedes) =>
{
    if (http.User?.Identity?.IsAuthenticated != true)
    {
        return Results.Redirect("/login");
    }
    if (!Guid.TryParse(http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
    {
        return Results.Redirect("/login");
    }

    var resultado = await selector.ResolverAsync(userId, tenantId);
    if (resultado is null)
    {
        return Results.Redirect("/seleccionar-empresa?error=1");
    }

    // Reconstruir claims preservando identidad y agregando tenant_id + tenant_role.
    // Tambien limpiamos los claims por-tenant que dependen del membership del tenant
    // ANTERIOR (profesional_id, rol_id, rol_nombre, perms) — los recalculamos abajo
    // desde el membership del tenant elegido. Sin este reset, un usuario que cambia
    // de agencia arrastraria los permisos/rol de la anterior.
    var keep = new List<Claim>();
    foreach (var c in http.User.Claims)
    {
        if (c.Type is "tenant_id" or "tenant_role" or "needs_tenant" or "sucursal_id"
            or "profesional_id" or "rol_id" or "rol_nombre" or "perms") { continue; }
        keep.Add(c);
    }
    keep.Add(new Claim("tenant_id", resultado.TenantId.ToString()));
    keep.Add(new Claim("tenant_role", resultado.TenantRole));
    if (resultado.EsGlobalAccess) { keep.Add(new Claim("global_access", "1")); }

    // Cargar membership del tenant elegido para emitir los mismos claims que el
    // flujo /auth/login (profesional_id / rol_id / rol_nombre / perms). Sin esto,
    // un especialista que entra via el selector no aparece vinculado en /atencion
    // y un admin cuyo rol vive en la tabla Roles pierde su menu completo.
    var membership = await db.TenantUsers.IgnoreQueryFilters()
        .Where(tu => tu.PlatformUserId == userId
                  && tu.TenantId == resultado.TenantId
                  && tu.Status == PlatformUserStatus.Active)
        .FirstOrDefaultAsync();
    if (membership?.ProfesionalId is Guid pidSel)
    {
        keep.Add(new Claim("profesional_id", pidSel.ToString()));
    }
    if (membership?.RolId is Guid rolIdSel)
    {
        keep.Add(new Claim("rol_id", rolIdSel.ToString()));
        var rolNombreSel = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.Id == rolIdSel).Select(r => r.Nombre).FirstOrDefaultAsync();
        if (!string.IsNullOrWhiteSpace(rolNombreSel))
        {
            keep.Add(new Claim("rol_nombre", rolNombreSel));
        }
        var permisosSel = await db.RolPermisos.IgnoreQueryFilters()
            .Where(p => p.RolId == rolIdSel && p.Ver)
            .Select(p => p.Modulo)
            .ToListAsync();
        if (permisosSel.Count > 0)
        {
            keep.Add(new Claim("perms", string.Join(',', permisosSel)));
        }
    }

    // Si el usuario tiene exactamente una sede a su alcance, entrar directo con sucursal_id.
    // Si tiene varias o ninguna, dejarlo en el selector de sede (o seguir sin sede si no hay).
    var disponibles = await sedes.GetSedesAsync(userId, resultado.TenantId);
    string destino = "/admision";
    if (disponibles.Count == 1)
    {
        keep.Add(new Claim("sucursal_id", disponibles[0].Id.ToString()));
    }
    else if (disponibles.Count > 1)
    {
        keep.Add(new Claim("needs_sucursal", "1"));
        destino = "/seleccionar-sede";
    }

    var identity = new ClaimsIdentity(keep, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect(destino);
}).DisableAntiforgery();

// Selector de sede: el usuario eligio en que sucursal va a trabajar dentro del tenant activo.
app.MapPost("/auth/select-sede", async (
    HttpContext http,
    [FromForm] Guid sucursalId,
    Visal.Application.Tenancy.ISedeSelectorService sedes) =>
{
    if (http.User?.Identity?.IsAuthenticated != true)
    {
        return Results.Redirect("/login");
    }
    if (!Guid.TryParse(http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
    {
        return Results.Redirect("/login");
    }
    if (!Guid.TryParse(http.User.FindFirst("tenant_id")?.Value, out var tenantId))
    {
        return Results.Redirect("/seleccionar-empresa");
    }
    if (!await sedes.PuedeAccederAsync(userId, tenantId, sucursalId))
    {
        return Results.Redirect("/seleccionar-sede?error=1");
    }

    var keep = new List<Claim>();
    foreach (var c in http.User.Claims)
    {
        if (c.Type is "sucursal_id" or "needs_sucursal") { continue; }
        keep.Add(c);
    }
    keep.Add(new Claim("sucursal_id", sucursalId.ToString()));

    var identity = new ClaimsIdentity(keep, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/admision");
}).DisableAntiforgery();

// Auto-registro (autogestion): un visitante crea su propia agencia + usuario Owner y queda
// con sesion iniciada. La agencia nace activa sin plan; elige plan luego en "Mi cuenta".
app.MapPost("/auth/register", async (
    HttpContext http,
    [FromForm] string agencyName,
    [FromForm] string displayName,
    [FromForm] string email,
    [FromForm] string password,
    Visal.Application.Auth.ISelfSignupService signup) =>
{
    var result = await signup.SignUpAsync(
        new Visal.Application.Auth.SelfSignupRequest(agencyName, displayName, email, password));

    if (!result.Success)
    {
        var msg = Uri.EscapeDataString(result.Error ?? "No se pudo crear la cuenta.");
        return Results.Redirect($"/login?mode=signup&regerror={msg}");
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, result.AdminUserId.ToString()),
        new(ClaimTypes.Name, string.IsNullOrWhiteSpace(displayName) ? result.Email : displayName.Trim()),
        new(ClaimTypes.Email, result.Email),
        new("tenant_id", result.TenantId.ToString()),
        new("tenant_role", TenantRole.Owner.ToString())
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect("/mi-cuenta");
}).DisableAntiforgery();

// Recuperar contrasena (autogestion): envia un enlace de reseteo por correo. Nunca revela si el
// correo existe. El enlace usa el host de la peticion (sirve en dev y en prod tras forwarded headers).
app.MapPost("/auth/forgot", async (
    HttpContext http,
    [FromForm] string email,
    Visal.Application.Auth.IPasswordResetService reset) =>
{
    var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
    var result = await reset.RequestAsync(email, baseUrl);
    if (!result.Success)
    {
        return Results.Redirect($"/recuperar?error={Uri.EscapeDataString(result.Error ?? "No se pudo procesar la solicitud.")}");
    }
    return Results.Redirect("/recuperar?sent=1");
}).DisableAntiforgery();

// Aplica la nueva contrasena usando el token del enlace del correo.
app.MapPost("/auth/reset", async (
    [FromForm] string token,
    [FromForm] string password,
    Visal.Application.Auth.IPasswordResetService reset) =>
{
    var result = await reset.ResetAsync(token, password);
    if (!result.Success)
    {
        return Results.Redirect($"/restablecer?token={Uri.EscapeDataString(token)}&error={Uri.EscapeDataString(result.Error ?? "No se pudo restablecer la contrasena.")}");
    }
    return Results.Redirect("/login?reset=1");
}).DisableAntiforgery();

// Inicia el flujo OIDC con Google: arma la URL de challenge y guarda un state (proteccion CSRF).
// Con mode=signup se recuerda el nombre de la agencia para crear el tenant al volver del callback.
// Feed anonimo del dropdown SEDE del login. El cliente lo consulta cuando el usuario
// termina de escribir su correo/documento para reducir el listado a solo las sedes
// asignadas. Ver ISedeCatalogoPublicoService.ListParaUsuarioAsync para la regla de
// fallback anti-enumeracion.
app.MapGet("/api/login/sedes", async (
    [FromQuery] string? usuario,
    Visal.Application.Tenancy.ISedeCatalogoPublicoService sedes,
    CancellationToken ct) =>
{
    var data = await sedes.ListParaUsuarioAsync(usuario ?? string.Empty, ct);
    return Results.Json(new
    {
        mostrarGlobal = data.MostrarGlobal,
        sedes = data.Sedes.Select(s => new { id = s.Id, nombre = s.Nombre })
    });
}).AllowAnonymous();

app.MapGet("/connect/google", async (
    HttpContext http,
    [FromQuery] string? mode,
    [FromQuery] string? agency,
    Visal.Application.Auth.IGoogleSignInService google) =>
{
    var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}/signin-google";
    var state = Guid.NewGuid().ToString("N");
    var url = await google.BuildAuthorizeUrlAsync(redirectUri, state);
    if (url is null) { return Results.Redirect("/login?gerror=" + Uri.EscapeDataString("El ingreso con Google no esta habilitado.")); }

    var cookieOpts = new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = http.Request.IsHttps,
        MaxAge = TimeSpan.FromMinutes(10),
        Path = "/"
    };
    http.Response.Cookies.Append("g_oauth_state", state, cookieOpts);

    var isSignup = string.Equals(mode, "signup", StringComparison.OrdinalIgnoreCase);
    if (isSignup && !string.IsNullOrWhiteSpace(agency))
    {
        http.Response.Cookies.Append("g_signup_agency", Uri.EscapeDataString(agency.Trim()), cookieOpts);
    }
    else
    {
        http.Response.Cookies.Delete("g_signup_agency");
    }
    return Results.Redirect(url);
}).AllowAnonymous();

// Callback de Google: valida el state, intercambia el code y, si el usuario existe y esta activo,
// inicia sesion por cookie. No hay auto-registro: usuarios desconocidos reciben un mensaje claro.
app.MapGet("/signin-google", async (
    HttpContext http,
    [FromQuery] string? code,
    [FromQuery] string? state,
    [FromQuery] string? error,
    Visal.Application.Auth.IGoogleSignInService google) =>
{
    if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
    {
        return Results.Redirect("/login?gerror=" + Uri.EscapeDataString("No se completo el ingreso con Google."));
    }

    var expectedState = http.Request.Cookies["g_oauth_state"];
    http.Response.Cookies.Delete("g_oauth_state");

    var signupAgencyRaw = http.Request.Cookies["g_signup_agency"];
    http.Response.Cookies.Delete("g_signup_agency");
    var signupAgency = string.IsNullOrWhiteSpace(signupAgencyRaw) ? null : Uri.UnescapeDataString(signupAgencyRaw);

    if (string.IsNullOrEmpty(state) || !string.Equals(state, expectedState, StringComparison.Ordinal))
    {
        return Results.Redirect("/login?gerror=" + Uri.EscapeDataString("Sesion de ingreso invalida. Intenta de nuevo."));
    }

    var redirectUri = $"{http.Request.Scheme}://{http.Request.Host}/signin-google";
    var result = await google.ResolveAsync(code, redirectUri, signupAgency);
    if (!result.Success)
    {
        // Si venia del formulario de registro, mostramos el error dentro del panel "Crear cuenta".
        if (signupAgency is not null)
        {
            return Results.Redirect("/login?mode=signup&regerror=" + Uri.EscapeDataString(result.Error ?? "No se pudo crear la cuenta con Google."));
        }
        return Results.Redirect("/login?gerror=" + Uri.EscapeDataString(result.Error ?? "No se pudo iniciar sesion con Google."));
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, result.UserId.ToString()),
        new(ClaimTypes.Name, result.DisplayName ?? result.Email ?? string.Empty),
        new(ClaimTypes.Email, result.Email ?? string.Empty)
    };

    string redirect;
    if (result.PlatformRole is not null)
    {
        claims.Add(new Claim("platform_role", result.PlatformRole));
        redirect = "/";
    }
    else
    {
        claims.Add(new Claim("tenant_id", result.TenantId!.Value.ToString()));
        claims.Add(new Claim("tenant_role", result.TenantRole ?? string.Empty));
        redirect = "/admision";
    }

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Redirect(redirect);
}).AllowAnonymous();

app.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

// API publica de ingestion de leads por agencia. Auth por API key (header X-Api-Key) que resuelve
// el tenant. Permite crear un lead y llenar cualquier campo del embudo desde sistemas externos.
app.MapPost("/api/public/leads", async (
    HttpRequest request,
    Visal.Application.Tenancy.ITenantApiService api,
    Visal.Application.Tenancy.ApiCreateLeadRequest body,
    CancellationToken ct) =>
{
    var apiKey = request.Headers["X-Api-Key"].ToString();
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.Json(new { error = "Falta el header X-Api-Key." }, statusCode: 401);
    }
    var tenantId = await api.ResolveTenantAsync(apiKey, ct);
    if (tenantId is null)
    {
        return Results.Json(new { error = "API key invalida o deshabilitada." }, statusCode: 401);
    }
    var result = await api.CreateLeadAsync(tenantId.Value, body, ct);
    return result.Ok
        ? Results.Json(new { ok = true, leadId = result.LeadId }, statusCode: 201)
        : Results.Json(new { ok = false, error = result.Error }, statusCode: 400);
}).AllowAnonymous().DisableAntiforgery();

// Pagina publica de la cotizacion de un lead (HTML del diseno con los datos del lead). La usa el
// boton "Ver cotizacion" y tambien el render de PDF (Chromium navega aqui). Clave: el id del lead.
app.MapGet("/cotizacion/{leadId:guid}", async (
    Guid leadId,
    [FromQuery] Guid? templateId,
    Visal.Application.Tenancy.IQuoteRenderService render,
    CancellationToken ct) =>
{
    var html = await render.RenderHtmlAsync(leadId, templateId, ct);
    return html is null ? Results.NotFound() : Results.Content(html, "text/html; charset=utf-8");
}).AllowAnonymous();

// PDF de la cotizacion (render headless de la pagina anterior). Para descargar/ver como PDF.
app.MapGet("/cotizacion/{leadId:guid}/pdf", async (
    Guid leadId,
    [FromQuery] Guid? templateId,
    HttpRequest httpReq,
    Visal.Application.Common.IQuotePdfRenderer pdf,
    CancellationToken ct) =>
{
    // Chromium corre en el MISMO contenedor que la app: navega al loopback interno (Kestrel escucha
    // en ASPNETCORE_HTTP_PORTS), no al dominio publico. El contenedor no puede alcanzar su propia URL
    // publica desde adentro (hairpin) y GoToAsync expira. La pagina /cotizacion es AllowAnonymous.
    var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split(';', ',')[0].Trim();
    var url = $"http://localhost:{port}/cotizacion/{leadId}" + (templateId is Guid t ? $"?templateId={t}" : "");
    var bytes = await pdf.RenderUrlToPdfAsync(url, ct);
    return bytes.Length == 0 ? Results.NotFound() : Results.File(bytes, "application/pdf", $"cotizacion-{leadId}.pdf");
}).AllowAnonymous();

// Descarga del comprobante de pago (PDF). Solo pagos aprobados; el usuario de agencia solo
// puede descargar comprobantes de su propio tenant; el operador de plataforma puede cualquiera.
app.MapGet("/comprobante/{paymentId:guid}", async (
    Guid paymentId,
    HttpContext http,
    Visal.Application.Admin.IPaymentReceiptService receipts) =>
{
    var receipt = await receipts.GenerateAsync(paymentId);
    if (receipt is null)
    {
        return Results.NotFound();
    }

    var isOperator = http.User.FindFirst("platform_role") is not null;
    var ownsTenant = Guid.TryParse(http.User.FindFirst("tenant_id")?.Value, out var tid) && tid == receipt.TenantId;
    if (!isOperator && !ownsTenant)
    {
        return Results.Forbid();
    }

    return Results.File(receipt.Content, "application/pdf", receipt.FileName);
}).RequireAuthorization();

// Webhook crudo de Evolution: traduce el evento, deduce el tenant del nombre de instancia,
// valida un token global y persiste el entrante (con difusion SignalR en este mismo proceso).
app.MapPost("/webhooks/evolution", async (
    HttpRequest request,
    IApplicationDbContext db,
    Visal.Application.Tenancy.IChatIngestService ingest,
    CancellationToken ct) =>
{
    var master = await db.EvolutionMasterConfigs.FirstOrDefaultAsync(ct);
    var expected = master?.WebhookToken
        ?? Environment.GetEnvironmentVariable("VISAL_EVOLUTION_WEBHOOK_TOKEN");
    if (string.IsNullOrEmpty(expected)) { return Results.StatusCode(503); }

    var provided = request.Headers["x-webhook-token"].ToString();
    if (string.IsNullOrEmpty(provided)) { provided = request.Query["token"].ToString(); }
    if (!string.Equals(provided, expected, StringComparison.Ordinal)) { return Results.Unauthorized(); }

    using var doc = await System.Text.Json.JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
    var parsed = Visal.SuperAdmin.RealTime.EvolutionWebhookParser.Parse(doc.RootElement);
    if (parsed is null) { return Results.Ok(new { status = "ignored" }); }

    var result = await ingest.IngestTrustedAsync(parsed.TenantId, parsed.Payload, ct);
    return result == Visal.Application.Tenancy.ChatIngestResult.Duplicate
        ? Results.Ok(new { status = "duplicate" })
        : Results.Accepted();
}).AllowAnonymous().DisableAntiforgery();

// Webhook Gupshup por linea. A diferencia del Evolution (que trae el nombre
// de instancia y valida contra un token global), Gupshup no manda
// identificacion en el payload -- por eso identificamos + autenticamos con
// un token opaco unico por linea en el path: /webhooks/gupshup/{token}.
// Si coincide con WhatsAppLine.InboundToken, la linea nos da el TenantId y
// pasamos a IngestTrustedAsync. Regenerable desde /lineas si se filtra
// (rota sin tocar la App de Gupshup). Ningun error 5xx: acusamos 200/401
// rapido para que Gupshup no reintente ni llene el log.
app.MapPost("/webhooks/gupshup/{token}", async (
    string token,
    HttpRequest request,
    IApplicationDbContext db,
    Visal.Application.Tenancy.IChatIngestService ingest,
    Visal.Application.Tenancy.IFirmaRemotaService firma,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(token) || token.Length > 128) { return Results.Unauthorized(); }
    var line = await db.WhatsAppLines
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(l => l.InboundToken == token, ct);
    if (line is null) { return Results.Unauthorized(); }

    using var doc = await System.Text.Json.JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
    var payload = Visal.SuperAdmin.RealTime.GupshupWebhookParser.Parse(doc.RootElement);
    if (payload is null) { return Results.Ok(new { status = "ignored" }); }

    var result = await ingest.IngestTrustedAsync(line.TenantId, payload, ct);

    // Auto-respuesta a la plantilla de firma remota: si el destinatario respondio
    // al Quick Reply o mando un afirmativo con keyword ("si", "enviar", "enlace",
    // "link") Y tiene una solicitud pendiente, respondemos con el link automatico.
    // El operador no tiene que reabrir el modal "Solicitar firmas".
    // Nota: idempotencia + dedupe viven adentro de AutoResponderConLinkAsync.
    if (result == Visal.Application.Tenancy.ChatIngestResult.Accepted
        && EsRespuestaAfirmativaFirma(payload.MessageType, payload.Body))
    {
        var baseUri = $"{request.Scheme}://{request.Host.Value}";
        _ = await firma.AutoResponderConLinkAsync(line.TenantId, payload.ContactPhone, line.Id, baseUri, ct);
    }

    return result == Visal.Application.Tenancy.ChatIngestResult.Duplicate
        ? Results.Ok(new { status = "duplicate" })
        : Results.Accepted();
}).AllowAnonymous().DisableAntiforgery();

// Heuristica de afirmativo para auto-responder con el link de firma. Cubre:
//   - Quick Reply de plantillas HSM (parser marca messageType=button_reply).
//   - Respuestas de texto libre: "si", "sí", "si enviar", "enviar enlace",
//     "enviar link", "envieme el link", "link por favor", etc.
// Deliberadamente laxo: es mejor auto-responder de mas (el link igual esta
// pendiente para el paciente) que dejarlo esperando.
static bool EsRespuestaAfirmativaFirma(string? messageType, string body)
{
    if (string.Equals(messageType, "button_reply", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }
    if (string.IsNullOrWhiteSpace(body)) { return false; }
    var norm = body.Trim().ToLowerInvariant();
    // Match por keywords aisladas para no gatillar en "no envien link" o similar.
    string[] keywords = { "enlace", "link", "envienlace", "envieme", "enviar" };
    string[] afirmativos = { "si", "sí", "yes", "ok", "listo", "acepto", "claro" };
    if (keywords.Any(k => norm.Contains(k))) { return true; }
    // "si" solo lo aceptamos como afirmativo si es palabra corta (<=15 chars)
    // para no matchear frases como "no siga con eso" o "prosiga".
    if (norm.Length <= 15 && afirmativos.Any(a => norm == a || norm.StartsWith(a + " ") || norm.StartsWith(a + ","))) { return true; }
    return false;
}

// Endpoint publico que recibe la firma capturada en el celular del paciente.
// La pagina /firma/{token} la sirve la propia app Blazor (componente FirmaPacienteRemota).
// La submission usa este POST API porque persistir desde el Blazor anonimo sin tenant
// scope era enredado: con un POST plano es trivial.
// Plantilla del import de pacientes en /admision. 51 columnas que cubren
// TODOS los campos del SavePacienteRequest. Donde tiene sentido, hay tabs
// auxiliares con dropdowns (Sedes, Aseguradoras, Catalogos por tipo, Pais)
// para evitar errores tipograficos. El parser en Admision.razor resuelve
// nombres a IDs via lookups y deja warnings en el log para los que no
// matchean.
app.MapGet("/api/pacientes/plantilla.xlsx", async (
    Visal.Application.Tenancy.ISucursalService sucSvc,
    Visal.Application.Tenancy.IAseguradoraService aseSvc,
    Visal.Application.Tenancy.ICatalogoPacienteService catSvc,
    Visal.Application.Tenancy.IGeografiaService geoSvc,
    CancellationToken ct) =>
{
    using var wb = new ClosedXML.Excel.XLWorkbook();
    var ws = wb.AddWorksheet("Pacientes");

    // 51 columnas. Agrupadas por seccion: Identificacion, Caracteristicas,
    // Contacto, Geografia, Admin PAD, Clasificacion, Diagnostico, Tutela,
    // Contratos, Sede, Emergencia, Activo.
    var headers = new[]
    {
        // A-H Identificacion (1-8)
        "No documento", "Tipo doc", "Primer Nombre", "Segundo Nombre",
        "Primer Apellido", "Segundo Apellido", "Fecha nacimiento", "Edad",
        // I-O Caracteristicas (9-15)
        "Sexo", "Estado civil", "Grupo Rh", "Zona", "Regimen", "Estrato", "Ocupacion",
        // P-T Contacto (16-20)
        "Cod pais tel", "Telefono", "Email", "Direccion", "Barrio",
        // U-Y Geografia (21-25)
        "Pais residencia", "Pais origen", "Departamento", "Municipio", "Ciudad",
        // Z-AG Admin PAD (26-33)
        "Aseguradora", "IPS Que Comenta", "Codigo aceptacion", "Fecha comentan",
        "Fecha ingreso PAD", "Fecha egreso PAD", "Dias estancia", "Op ingreso dias",
        // AH-AM Clasificacion (34-39)
        "Incapacidad", "Tipo usuario", "Estado", "Clasificacion paciente",
        "Clasificacion grupo patologia", "Med contratado",
        // AN-AO Diagnostico (40-41)
        "CIE codigo", "Diagnostico principal",
        // AP-AQ Tutela (42-43)
        "Tutela", "Tipo tutela",
        // AR-AT Contratos (44-46)
        "Contrato 1", "Contrato 2", "Contrato 3",
        // AU Sede (47)
        "Sede atencion",
        // AV-AX Emergencia (48-50)
        "Emergencia nombre", "Emergencia parentesco", "Emergencia telefono",
        // AY Activo (51)
        "Activo"
    };
    for (var i = 0; i < headers.Length; i++)
    {
        ws.Cell(1, i + 1).Value = headers[i];
        ws.Cell(1, i + 1).Style.Font.Bold = true;
        ws.Cell(1, i + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1565C0");
        ws.Cell(1, i + 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
        ws.Cell(1, i + 1).Style.Alignment.WrapText = true;
    }
    ws.Row(1).Height = 30;

    // Cargar catalogos en paralelo (cada uno es una query independiente).
    var sedes = await sucSvc.ListAsync(soloActivas: true, ct);
    var aseguradoras = await aseSvc.ListAseguradorasAsync(ct);
    var tiposUsuario = await catSvc.ListAsync(Visal.Domain.Enums.CatalogoPacienteTipo.TipoUsuario, ct);
    var clasifPac = await catSvc.ListAsync(Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionPaciente, ct);
    var clasifGrp = await catSvc.ListAsync(Visal.Domain.Enums.CatalogoPacienteTipo.ClasificacionGrupoPatologia, ct);
    var tiposTutela = await catSvc.ListAsync(Visal.Domain.Enums.CatalogoPacienteTipo.TipoTutela, ct);
    var medsContratados = await catSvc.ListAsync(Visal.Domain.Enums.CatalogoPacienteTipo.MedContratado, ct);
    var paises = await geoSvc.ListPaisesAsync(ct);

    var primeraSede = sedes.FirstOrDefault()?.Nombre ?? "";
    var primeraAse = aseguradoras.FirstOrDefault()?.Nombre ?? "";
    var primerPais = paises.FirstOrDefault(p => p.Codigo?.Equals("CO", StringComparison.OrdinalIgnoreCase) == true)?.Nombre
                     ?? paises.FirstOrDefault()?.Nombre ?? "COLOMBIA";

    // 3 filas de ejemplo. Solo identificacion + caracteristicas + contacto
    // basico. El resto se deja vacio para que el usuario ponga sus datos.
    var ejemplos = new object?[][]
    {
        new object?[] {
            "1010101010", "CC", "JUAN", "CARLOS", "PEREZ", "MOLINA", "22/11/1978", null,
            "MASCULINO", "CASADO", "O+", "URBANA", "CONTRIBUTIVO", "3", "OPERARIO",
            "+57", "3001234567", "juan.perez@ejemplo.com", "Cra 50 # 12-08 Apt 502", "EL POBLADO",
            primerPais, primerPais, "", "", "CALI",
            primeraAse, primeraAse, "ACE-12345", "10/06/2026",
            "12/06/2026", "", "30", "0",
            "NO", "", "", "", "", "",
            "I50.0", "Insuficiencia cardiaca congestiva",
            "NO", "",
            "", "", "",
            primeraSede,
            "MARIA PEREZ", "ESPOSA", "3001234999",
            "SI"
        },
        new object?[] {
            "38945877", "CC", "GRACIELA", "", "GOMEZ", "GOMEZ", "15/03/1965", null,
            "FEMENINO", "VIUDA", "A+", "URBANA", "SUBSIDIADO", "2", "AMA DE CASA",
            "+57", "3119876543", "g.gomez@ejemplo.com", "Calle 5 # 23-15", "EL PRADO",
            primerPais, primerPais, "", "", "CALI",
            "", "", "", "",
            "", "", "", "",
            "NO", "", "", "", "", "",
            "", "",
            "NO", "",
            "", "", "",
            primeraSede,
            "PEDRO GOMEZ", "HIJO", "3119876544",
            "SI"
        },
        new object?[] {
            "1144099887", "CC", "MARIA", "FERNANDA", "GOMEZ", "", "08/07/1992", null,
            "FEMENINO", "SOLTERA", "B+", "URBANA", "CONTRIBUTIVO", "4", "INGENIERA",
            "+57", "3155551122", "maria.gomez@ejemplo.com", "Av 6N # 45-12", "GRANADA",
            primerPais, primerPais, "", "", "SANTIAGO DE CALI",
            "", "", "", "",
            "", "", "", "",
            "NO", "", "", "", "", "",
            "", "",
            "NO", "",
            "", "", "",
            primeraSede,
            "ANA GOMEZ", "MADRE", "3155551133",
            "SI"
        }
    };
    for (var r = 0; r < ejemplos.Length; r++)
    {
        for (var c = 0; c < ejemplos[r].Length; c++)
        {
            ws.Cell(r + 2, c + 1).Value = ejemplos[r][c]?.ToString();
        }
    }

    // Tabs auxiliares + data validations. Solo se crean si el catalogo tiene
    // contenido para no contaminar el archivo con hojas vacias.
    static void AgregarLookupSheet(ClosedXML.Excel.XLWorkbook workbook,
        ClosedXML.Excel.IXLWorksheet hoja, string nombre, string titulo,
        IReadOnlyList<string> valores, string colLetra)
    {
        if (valores.Count == 0) { return; }
        var aux = workbook.AddWorksheet(nombre);
        aux.Cell(1, 1).Value = titulo;
        aux.Cell(1, 1).Style.Font.Bold = true;
        for (var i = 0; i < valores.Count; i++) { aux.Cell(i + 2, 1).Value = valores[i]; }
        aux.Columns().AdjustToContents();

        var rango = $"{nombre}!$A$2:$A${valores.Count + 1}";
        var val = hoja.Range($"{colLetra}2:{colLetra}500").CreateDataValidation();
        val.List(rango);
        val.IgnoreBlanks = true;
        val.InCellDropdown = true;
        val.ErrorStyle = ClosedXML.Excel.XLErrorStyle.Warning;
        val.ErrorTitle = $"{titulo} no esta en la lista";
        val.ErrorMessage = $"Selecciona un valor de la lista (tab '{nombre}') o ignora si quieres dejarlo vacio.";
    }

    AgregarLookupSheet(wb, ws, "Sedes", "Sedes activas del tenant",
        sedes.Select(s => s.Nombre).ToList(), "AU");
    AgregarLookupSheet(wb, ws, "Aseguradoras", "Aseguradoras del tenant",
        aseguradoras.Select(a => a.Nombre).ToList(), "Z");
    AgregarLookupSheet(wb, ws, "IPS_Comenta", "IPS Que Comenta (mismo catalogo que Aseguradoras)",
        aseguradoras.Select(a => a.Nombre).ToList(), "AA");
    AgregarLookupSheet(wb, ws, "TiposUsuario", "Tipos de usuario",
        tiposUsuario.Select(c => c.Nombre).ToList(), "AI");
    AgregarLookupSheet(wb, ws, "ClasifPaciente", "Clasificacion paciente",
        clasifPac.Select(c => c.Nombre).ToList(), "AK");
    AgregarLookupSheet(wb, ws, "ClasifGrupo", "Clasificacion grupo patologia",
        clasifGrp.Select(c => c.Nombre).ToList(), "AL");
    AgregarLookupSheet(wb, ws, "TiposTutela", "Tipos de tutela",
        tiposTutela.Select(c => c.Nombre).ToList(), "AQ");
    AgregarLookupSheet(wb, ws, "MedContratado", "Medicamentos contratados",
        medsContratados.Select(c => c.Nombre).ToList(), "AM");
    AgregarLookupSheet(wb, ws, "Paises", "Paises",
        paises.Select(p => p.Nombre).ToList(), "U");
    AgregarLookupSheet(wb, ws, "Paises2", "Paises (origen)",
        paises.Select(p => p.Nombre).ToList(), "V");

    // Instrucciones al final de la hoja, debajo de las filas de ejemplo.
    var info = 2 + ejemplos.Length + 1;
    ws.Cell(info, 1).Value = "Instrucciones:";
    ws.Cell(info, 1).Style.Font.Bold = true;
    ws.Cell(info + 1, 1).Value = "1. Una fila por paciente. Las filas de ejemplo pueden borrarse antes de importar.";
    ws.Cell(info + 2, 1).Value = "2. Tipo doc: CC / CE / TI / PA / RC / NIT.";
    ws.Cell(info + 3, 1).Value = "3. Fechas: formato dd/mm/aaaa o celda tipo fecha de Excel.";
    ws.Cell(info + 4, 1).Value = "4. Sexo: MASCULINO / FEMENINO / OTRO. Zona: URBANA / RURAL. Incapacidad/Tutela/Activo: SI / NO.";
    ws.Cell(info + 5, 1).Value = "5. Columnas con dropdown (tabs auxiliares): Aseguradora (Z), IPS Que Comenta (AA), Tipo usuario (AI), Clasif paciente (AK), Clasif grupo (AL), Med contratado (AM), Tipo tutela (AQ), Sede (AU), Paises (U y V).";
    ws.Cell(info + 6, 1).Value = "6. Departamento (W), Municipio (X), Ciudad (Y), Contratos (AR-AT) y CIE codigo (AN): texto libre. Se resuelven por nombre/codigo; los que no matcheen quedan vacios y aparecen en el log.";
    ws.Cell(info + 7, 1).Value = "7. Documento duplicado => upsert (se actualiza el paciente existente).";
    ws.Cell(info + 8, 1).Value = "8. Edad (H): si la dejas vacia se calcula desde la fecha de nacimiento.";

    ws.Columns().AdjustToContents();
    // Forzar ancho minimo a las columnas para que se vea el header en wrap.
    for (var i = 1; i <= headers.Length; i++)
    {
        if (ws.Column(i).Width < 12) { ws.Column(i).Width = 14; }
    }

    using var ms = new MemoryStream();
    wb.SaveAs(ms);
    return Results.File(ms.ToArray(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "plantilla-pacientes.xlsx");
}).RequireAuthorization();

// Genera al vuelo un .xlsx con la plantilla esperada por el import de
// profesionales (mismas 12 columnas que CfgProfesionales.OnExcelSelected
// consume). Es minimal: header + una fila de ejemplo + comentario explicativo.
app.MapGet("/api/profesionales/plantilla.xlsx", () =>
{
    using var wb = new ClosedXML.Excel.XLWorkbook();
    var ws = wb.AddWorksheet("Profesionales");

    var headers = new[]
    {
        "No documento", "Tipo documento", "Primer Nombre", "Segundo Nombre",
        "Primer apellido", "Segundo apellido", "Tipo profesional", "Registro medico",
        "Ciudad", "Celular", "SubCategoria", "Firma",
        "Rol"
    };
    for (var i = 0; i < headers.Length; i++)
    {
        ws.Cell(1, i + 1).Value = headers[i];
        ws.Cell(1, i + 1).Style.Font.Bold = true;
        ws.Cell(1, i + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1565C0");
        ws.Cell(1, i + 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
    }
    var ejemplo = new object[] {
        "1010101010", "CC", "JUAN", "CARLOS", "PEREZ", "MOLINA",
        "ENFERMERIA", "RM-12345", "CALI", "300 1234567", "AUX ENFERMERIA", "",
        "Coordinador"
    };
    for (var i = 0; i < ejemplo.Length; i++) { ws.Cell(2, i + 1).Value = ejemplo[i]?.ToString(); }

    ws.Cell(4, 1).Value = "Instrucciones:";
    ws.Cell(4, 1).Style.Font.Bold = true;
    ws.Cell(5, 1).Value = "1. Una fila por profesional (la fila 2 es solo un ejemplo, puedes borrarla).";
    ws.Cell(6, 1).Value = "2. 'Tipo profesional' y 'SubCategoria' deben existir en los catalogos del sistema.";
    ws.Cell(7, 1).Value = "3. 'Firma' (col 12): pega la imagen DENTRO de la celda. El importador la extrae como PNG.";
    ws.Cell(8, 1).Value = "4. Documentos repetidos hacen upsert; si la firma viene vacia se preserva la actual.";
    ws.Cell(9, 1).Value = "5. 'Rol' (col 13): nombre EXACTO de un rol existente. Si el rol NO existe, el profesional se importa pero NO se crea usuario de login.";

    ws.Columns().AdjustToContents();

    using var ms = new MemoryStream();
    wb.SaveAs(ms);
    return Results.File(ms.ToArray(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "plantilla-profesionales.xlsx");
}).RequireAuthorization();

// Plantilla del import de la Base de datos Diagnosticos. Compatible con el
// Excel TablaReferencia_CUPSRips (usa columna "Tabla" -> Fuente) y con listados
// simples de CIE-10/CIE-11. Requiere Codigo + Nombre; el resto es opcional.
app.MapGet("/api/diagnosticos/plantilla.xlsx", () =>
{
    using var wb = new ClosedXML.Excel.XLWorkbook();
    var ws = wb.AddWorksheet("Diagnosticos");

    var headers = new[] { "Codigo", "Nombre", "Descripcion", "Habilitado", "Fuente" };
    for (var i = 0; i < headers.Length; i++)
    {
        ws.Cell(1, i + 1).Value = headers[i];
        ws.Cell(1, i + 1).Style.Font.Bold = true;
        ws.Cell(1, i + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1565C0");
        ws.Cell(1, i + 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
    }
    var ejemplos = new object[,]
    {
        { "A00.0",  "COLERA DEBIDO A VIBRIO CHOLERAE 01",   "Enfermedades infecciosas intestinales", "SI", "CIE10" },
        { "010100", "PUNCION CISTERNAL SOD",                "SECCION 00 PROCEDIMIENTOS QUIRURGICOS", "SI", "CUPSRips" }
    };
    for (var r = 0; r < ejemplos.GetLength(0); r++)
        for (var c = 0; c < ejemplos.GetLength(1); c++)
            ws.Cell(2 + r, c + 1).Value = ejemplos[r, c]?.ToString();

    ws.Cell(5, 1).Value = "Instrucciones:";
    ws.Cell(5, 1).Style.Font.Bold = true;
    ws.Cell(6, 1).Value = "1. 'Codigo' y 'Nombre' son obligatorios. El resto puede quedar vacio.";
    ws.Cell(7, 1).Value = "2. 'Habilitado' acepta SI/NO, TRUE/FALSE o 1/0. Vacio = SI.";
    ws.Cell(8, 1).Value = "3. 'Fuente' es libre (CIE10, CIE11, CUPSRips, PROPIA...). Sirve para filtrar por tipo.";
    ws.Cell(9, 1).Value = "4. Si un codigo ya existe en la BD se ACTUALIZA; si no existe se INSERTA.";
    ws.Cell(10, 1).Value = "5. Este importador tolera el Excel TablaReferencia_CUPSRips: usa la columna 'Tabla' como Fuente automaticamente.";

    ws.Columns().AdjustToContents();

    using var ms = new MemoryStream();
    wb.SaveAs(ms);
    return Results.File(ms.ToArray(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "plantilla-diagnosticos.xlsx");
}).RequireAuthorization();

// Plantilla del import de catalogos de servicios (Codigo, Nombre). Un solo
// endpoint sirve las 4 (RxImagenologia, Laboratorio, ServicioGeneral, Insumo);
// el header del Excel cambia levemente segun el tipo, pero el parser en la
// pagina solo lee columnas 1 y 2.
app.MapGet("/api/catalogo-servicios/plantilla.xlsx", (Visal.Domain.Enums.TipoCatalogoServicio tipo) =>
{
    using var wb = new ClosedXML.Excel.XLWorkbook();
    var (nombreHoja, titulo) = tipo switch
    {
        Visal.Domain.Enums.TipoCatalogoServicio.RxImagenologia  => ("RX Imagenologia",  "Codigo/Nombre CUPS de imagenologia"),
        Visal.Domain.Enums.TipoCatalogoServicio.Laboratorio     => ("Laboratorios",     "Codigo/Nombre CUPS de laboratorios"),
        Visal.Domain.Enums.TipoCatalogoServicio.ServicioGeneral => ("Servicios",        "Codigo/Nombre CUPS de servicios"),
        Visal.Domain.Enums.TipoCatalogoServicio.Insumo          => ("Insumos",          "Codigo/Nombre del insumo"),
        _ => ("Catalogo", "Codigo/Nombre")
    };
    var ws = wb.AddWorksheet(nombreHoja);
    ws.Cell(1, 1).Value = "Codigo";
    ws.Cell(1, 2).Value = "Nombre";
    ws.Row(1).Style.Font.Bold = true;
    ws.Row(1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1565C0");
    ws.Row(1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

    ws.Cell(2, 1).Value = "870001";
    ws.Cell(2, 2).Value = "EJEMPLO — reemplaza con tus filas reales";
    ws.Cell(4, 1).Value = "Instrucciones:";
    ws.Cell(4, 1).Style.Font.Bold = true;
    ws.Cell(5, 1).Value = "1. Columna A: codigo unico. Columna B: nombre.";
    ws.Cell(6, 1).Value = "2. La primera fila es el header (no la borres).";
    ws.Cell(7, 1).Value = "3. Codigo duplicado => upsert (actualiza el nombre).";
    ws.Cell(8, 1).Value = $"4. Contenido esperado: {titulo}.";
    ws.Columns().AdjustToContents();
    using var ms = new MemoryStream();
    wb.SaveAs(ms);
    var fileName = tipo switch
    {
        Visal.Domain.Enums.TipoCatalogoServicio.RxImagenologia  => "plantilla-rx-imagenologia.xlsx",
        Visal.Domain.Enums.TipoCatalogoServicio.Laboratorio     => "plantilla-laboratorios.xlsx",
        Visal.Domain.Enums.TipoCatalogoServicio.ServicioGeneral => "plantilla-servicios.xlsx",
        Visal.Domain.Enums.TipoCatalogoServicio.Insumo          => "plantilla-insumos.xlsx",
        _ => "plantilla-catalogo.xlsx"
    };
    return Results.File(ms.ToArray(),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        fileName);
}).RequireAuthorization();

app.MapPost("/api/firma/{token}/submit", async (
    string token,
    SubmitFirmaPayload payload,
    Visal.Application.Tenancy.IFirmaRemotaService svc,
    CancellationToken ct) =>
{
    var ok = await svc.GuardarFirmaPorTokenAsync(token, payload?.DataUrl ?? string.Empty, ct);
    return ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { ok = false, error = "Solicitud invalida, ya cerrada o expirada." });
}).AllowAnonymous().DisableAntiforgery();

// Descarga de snapshots de facturacion: xlsx o csv. El tenant se aplica dentro
// del servicio via query filter global — un intento de descargar un snapshot de
// otro tenant devuelve 404.
app.MapGet("/facturacion-clinica/snapshots/{id:guid}/download", async (
    Guid id,
    string? formato,
    Visal.Application.Facturacion.IFacturacionSnapshotService svc,
    CancellationToken ct) =>
{
    var fmt = (formato ?? "xlsx").ToLowerInvariant();
    if (fmt == "json")
    {
        var r = await svc.ExportarJsonRipsAsync(id, ct);
        if (r.Archivo is not null) { return Results.File(r.Archivo.Contenido, r.Archivo.MimeType, r.Archivo.NombreArchivo); }
        if (r.Errores.Count > 0) { return Results.Problem(string.Join(" | ", r.Errores), statusCode: 422, title: "JSON RIPS invalido"); }
        return Results.NotFound();
    }
    Visal.Application.Facturacion.ArchivoExportado? archivo = fmt switch
    {
        "xlsx" => await svc.ExportarExcelAsync(id, ct),
        "csv"  => await svc.ExportarCsvAsync(id, ct),
        _ => null
    };
    if (archivo is null) { return Results.NotFound(); }
    return Results.File(archivo.Contenido, archivo.MimeType, archivo.NombreArchivo);
}).RequireAuthorization();

// Export XLSX del listado /ordenes (todas las filas visibles con los filtros
// aplicados). Reusa BuscarAsync + global query filter por tenant, asi que la
// respuesta esta siempre acotada al tenant activo.
app.MapGet("/ordenes/export.xlsx", async (
    string? paciente,
    DateOnly? desde,
    DateOnly? hasta,
    string? especialista,
    Guid? aseguradoraId,
    Guid? sucursalId,
    string? estado,
    Visal.Application.Tenancy.IOrdenesClinicasService svc,
    CancellationToken ct) =>
{
    var f = new Visal.Application.Tenancy.OrdenesClinicasFiltro(
        PacienteTexto: string.IsNullOrWhiteSpace(paciente) ? null : paciente.Trim(),
        Desde: desde,
        Hasta: hasta,
        Especialista: string.IsNullOrWhiteSpace(especialista) ? null : especialista,
        SoloCerradas: string.Equals(estado, "Cerrada", StringComparison.OrdinalIgnoreCase),
        AseguradoraId: aseguradoraId,
        SucursalId: sucursalId);
    var archivo = await svc.ExportarExcelAsync(f, ct);
    return Results.File(archivo.Contenido, archivo.MimeType, archivo.NombreArchivo);
}).RequireAuthorization();

// Ola 7 RC7b — Export CSV del tab Archivo de /ordenes. Los filtros van en
// query string. El servicio aplica el global query filter por tenant, asi que
// otro tenant recibe solo sus propias archivadas o vacio.
app.MapGet("/revision/archivo/export.csv", (
    string? paciente,
    string? sabor,
    DateOnly? desde,
    DateOnly? hasta,
    Visal.Application.Revision.IRevisionKanbanService kanbanSvc,
    CancellationToken ct) =>
{
    Visal.Domain.Entities.RevisionEstadoAgregado? saborFiltro = sabor switch
    {
        "ArchivadaOk" => Visal.Domain.Entities.RevisionEstadoAgregado.ArchivadaOk,
        "Inactivada" => Visal.Domain.Entities.RevisionEstadoAgregado.Inactivada,
        _ => null,
    };
    var filtro = new Visal.Application.Revision.RevisionArchivoFiltro(
        PacienteTexto: string.IsNullOrWhiteSpace(paciente) ? null : paciente.Trim(),
        Sabor: saborFiltro,
        FechaDesde: desde,
        FechaHasta: hasta);
    var nombre = $"revision-archivo-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
    // Ola 9 RC9b — streaming. Escribimos BOM UTF-8 primero para que Excel lea
    // tildes; luego una linea por row desde el cursor pgSQL.
    return Results.Stream(async output =>
    {
        var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await using var sw = new StreamWriter(output, utf8);
        await foreach (var linea in kanbanSvc.ExportarArchivoCsvLineasAsync(filtro, ct))
        {
            await sw.WriteLineAsync(linea);
        }
        await sw.FlushAsync(ct);
    }, "text/csv; charset=utf-8", nombre);
}).RequireAuthorization();

// Ola 9 RC9a — Export CSV del panel /admin/ai-usage. Los filtros espejan los
// del grid. El global query filter por tenant sigue aplicando; el gate por
// rol Owner/SuperAdmin se aplica en la UI (endpoint solo requiere autenticar
// porque los datos ya estan tenant-scoped y no exponen otros tenants).
app.MapGet("/admin/ai-usage/export.csv", async (
    Guid? agentId,
    string? source,
    DateTimeOffset? desde,
    DateTimeOffset? hasta,
    string? exito,
    Visal.Application.Tenancy.IAiUsageService usageSvc,
    CancellationToken ct) =>
{
    bool? successFiltro = exito switch
    {
        "ok" => true,
        "fail" => false,
        _ => null,
    };
    var filtro = new Visal.Application.Tenancy.AiUsageExportFiltro(
        AgentId: agentId,
        Source: string.IsNullOrWhiteSpace(source) ? null : source.Trim(),
        Desde: desde,
        Hasta: hasta,
        Success: successFiltro);
    var bytes = await usageSvc.ExportarCsvAsync(filtro, ct);
    var nombre = $"ai-usage-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
    return Results.File(bytes, "text/csv; charset=utf-8", nombre);
}).RequireAuthorization();

try
{
    app.Run();
}
finally
{
    // Vaciar buffers de Serilog al apagar (sino se pueden perder los ultimos
    // eventos, incluidos los que expliquen POR QUE el proceso murio).
    Log.CloseAndFlush();
}

// Payload del POST publico de firma (queda al fondo del file porque Program.cs es top-level).
record SubmitFirmaPayload(string DataUrl);
