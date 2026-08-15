namespace Visal.Application.Tenancy.Menu;

/// <summary>Como se decide si una opcion built-in se ve para el usuario actual.</summary>
public enum NavGate
{
    /// <summary>Se ve si <see cref="NavMenuItemDef.Permission"/> es null (todos) o el usuario tiene ese permiso.</summary>
    Permission,
    /// <summary>Gate especial de Atencion: profesional vinculado o admin de agencia/operativo.</summary>
    Atencion,
    /// <summary>Solo admin de agencia (Owner / Admin / Administrador). Ej: Mi cuenta.</summary>
    AdminAgency,
    /// <summary>Solo operador de plataforma (super admin). Ej: Catalogo de Reportes.</summary>
    Platform,
}

/// <summary>Definicion inmutable de una opcion built-in del menu lateral.</summary>
/// <param name="Key">Identificador estable usado en el JSON de config. No cambia aunque cambie la ruta.</param>
/// <param name="Label">Etiqueta por defecto (el tenant puede sobreescribirla).</param>
/// <param name="Href">Ruta relativa del NavLink.</param>
/// <param name="IconKey">Clave del icono en el registro de iconos de la UI.</param>
/// <param name="Permission">Slug de permiso (null = visible para todo usuario de tenant). Solo aplica con <see cref="NavGate.Permission"/>.</param>
/// <param name="Group">Grupo por defecto.</param>
/// <param name="ExactMatch">True => NavLinkMatch.All (evita encender el item en sub-rutas).</param>
/// <param name="Gate">Regla de visibilidad especial.</param>
public sealed record NavMenuItemDef(
    string Key,
    string Label,
    string Href,
    string IconKey,
    string? Permission,
    string Group,
    bool ExactMatch = false,
    NavGate Gate = NavGate.Permission);

/// <summary>
/// Catalogo de todas las opciones built-in del menu lateral del tenant (fuente de
/// verdad). El bloque de Super Admin de plataforma NO esta aca: no es
/// personalizable por tenant y sigue hardcodeado en NavMenu.
/// </summary>
public static class NavMenuCatalog
{
    /// <summary>Orden por defecto de los grupos.</summary>
    public static readonly string[] DefaultGroups =
    {
        "Operacion Clinica",
        "Interoperabilidad",
        "Facturacion",
        "Infraestructura & IA",
        "Configuracion del Sistema",
        "Configuracion de la Entidad",
        "Mi agencia",
    };

    public static readonly IReadOnlyList<NavMenuItemDef> Items = new List<NavMenuItemDef>
    {
        // ===== Operacion Clinica =====
        new("admision", "Admision", "admision", "admision", "pacientes", "Operacion Clinica"),
        new("asignacion", "Asignacion", "asignacion", "portapapeles", "asignacion", "Operacion Clinica"),
        new("coordinacion", "Coordinacion", "coordinacion", "red", "coordinacion", "Operacion Clinica"),
        new("atencion", "Atencion", "atencion", "corazon", null, "Operacion Clinica", Gate: NavGate.Atencion),
        new("ordenes", "Ordenes Clinicas", "ordenes", "check-doc", "ordenes", "Operacion Clinica"),
        new("seguimiento", "Seguimiento", "seguimiento", "telefono", null, "Operacion Clinica"),
        new("reportes", "Reportes", "reportes", "tabla", null, "Operacion Clinica"),
        new("tableros", "Tableros", "tableros", "kanban", null, "Operacion Clinica"),
        new("formularios", "Formularios", "formularios", "documento", "formularios", "Operacion Clinica"),

        // ===== Interoperabilidad =====
        new("interoperabilidad-rda", "Consola RDA", "interoperabilidad/rda", "rda", "interoperabilidad.rda", "Interoperabilidad"),

        // ===== Facturacion =====
        new("facturacion-snapshots", "Snapshots", "facturacion-clinica/snapshots", "factura", "facturacion.snapshots", "Facturacion"),

        // ===== Infraestructura & IA =====
        new("lineas", "Lineas WhatsApp", "lineas", "movil", "whatsapp.lineas", "Infraestructura & IA", ExactMatch: true),
        new("lineas-plantillas", "Plantillas WhatsApp", "lineas/plantillas", "documento", "whatsapp.plantillas", "Infraestructura & IA"),
        new("agentes", "Agentes IA", "agentes", "robot", "agentes-ia", "Infraestructura & IA"),
        new("automatizaciones", "Automatizaciones", "automatizaciones", "rayo", "automatizaciones", "Infraestructura & IA"),
        new("metricas", "Metricas", "metricas", "grafico", "metricas", "Infraestructura & IA"),
        new("ai-usage", "Auditoria uso IA", "admin/ai-usage", "medidor", "admin.ai-usage", "Infraestructura & IA"),

        // ===== Configuracion del Sistema =====
        new("cfg-turnos", "Programaciones de turnos", "cfg-turnos", "calendario", "cfg-turnos", "Configuracion del Sistema"),
        new("cfg-profesionales", "Profesionales", "cfg-profesionales", "usuarios", "cfg-profesionales", "Configuracion del Sistema"),
        new("cfg-aseguradoras", "Entidades Aseguradoras", "cfg-aseguradoras", "escudo", "cfg-aseguradoras", "Configuracion del Sistema"),
        new("cfg-tipos-profesional", "Tipos de Profesional", "cfg-tipos-profesional", "etiqueta", "cfg-tipos-profesional", "Configuracion del Sistema"),
        new("cfg-subcategorias", "Subcategorias", "cfg-subcategorias", "etiqueta", "cfg-subcategorias", "Configuracion del Sistema"),
        new("cfg-paquetes", "Paquetes", "cfg-paquetes", "caja", "cfg-paquetes", "Configuracion del Sistema"),
        new("cfg-cuotas-copagos", "Cuotas / Copagos", "cfg-cuotas-copagos", "moneda", "cfg-cuotas-copagos", "Configuracion del Sistema"),
        new("cfg-pacientes", "Configuracion Pacientes", "cfg-pacientes", "engrane", "cfg-pacientes", "Configuracion del Sistema"),
        new("cie11", "Configuracion CIE-11", "cie11", "libro", "cie11", "Configuracion del Sistema"),
        new("medicamentos", "Base de datos Medicamentos", "medicamentos", "pastilla", "medicamentos", "Configuracion del Sistema"),
        new("diagnosticos", "Base de datos Diagnosticos", "diagnosticos", "estetoscopio", "diagnosticos", "Configuracion del Sistema"),
        new("catalogo-rx", "Catalogo RX Imagenologia", "catalogo/rx-imagenologia", "rayosx", "catalogo.rx", "Configuracion del Sistema"),
        new("catalogo-laboratorios", "Catalogo Laboratorios", "catalogo/laboratorios", "matraz", "catalogo.laboratorios", "Configuracion del Sistema"),
        new("catalogo-servicios", "Catalogo Servicios", "catalogo/servicios", "lista", "catalogo.servicios", "Configuracion del Sistema"),
        new("catalogo-insumos", "Catalogo Insumos", "catalogo/insumos", "caja", "catalogo.insumos", "Configuracion del Sistema"),
        new("relaciones-formularios", "Relaciones de formularios", "relaciones-formularios", "enlace", "relaciones-formularios", "Configuracion del Sistema"),
        new("cfg-tipos-servicio", "Tipos de servicio", "config/tipos-servicio", "etiqueta", "cfg-tipos-servicio", "Configuracion del Sistema"),
        new("cfg-menu-hc", "Menu HC por servicio", "config/menu-hc", "menu", "cfg-menu-hc", "Configuracion del Sistema"),
        new("cfg-reportes", "Galeria de Reportes", "config/reportes", "grafico", null, "Configuracion del Sistema"),
        new("correos-pqr", "Correos -> PQR", "config/correos-pqr", "correo", null, "Configuracion del Sistema"),
        new("admin-reportes-catalogo", "Catalogo de Reportes", "admin/reportes-catalogo", "tabla", null, "Configuracion del Sistema", Gate: NavGate.Platform),
        new("cfg-tipologia-archivos", "Tipologia Archivos", "cfg-tipologia-archivos", "carpeta", "cfg-tipologia-archivos", "Configuracion del Sistema"),
        new("sql-console", "Consola SQL", "admin/sql-console", "terminal", "admin.sql-console", "Configuracion del Sistema"),

        // ===== Configuracion de la Entidad =====
        new("cfg-empresa", "Configuracion de Empresa", "cfg-empresa", "edificio", "cfg-empresa", "Configuracion de la Entidad"),
        new("cfg-interoperabilidad", "Configuracion de Interoperabilidad", "cfg-interoperabilidad", "rda", "cfg-interoperabilidad", "Configuracion de la Entidad"),
        new("cfg-revision-policy", "Politica de Revision Clinica", "config/revision-policy", "escudo-check", "cfg-revision-policy", "Configuracion de la Entidad"),
        new("cfg-roles", "Roles y Permisos", "cfg-roles", "llave", "cfg-roles", "Configuracion de la Entidad"),
        new("cfg-usuarios", "Administracion de Usuarios", "cfg-usuarios", "usuarios", "cfg-usuarios", "Configuracion de la Entidad"),
        new("cfg-menu-lateral", "Menu lateral", "config/menu-lateral", "menu", "cfg-menu-lateral", "Configuracion de la Entidad"),

        // ===== Mi agencia =====
        new("mi-perfil", "Mi perfil", "mi-perfil", "persona", null, "Mi agencia"),
        new("mi-cuenta", "Mi cuenta", "mi-cuenta", "persona-cog", null, "Mi agencia", Gate: NavGate.AdminAgency),
    };
}
