using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Visal.Application.Common;
using Visal.Domain.Common;

namespace Visal.Application.Tenancy.Reportes;

/// <summary>Un parametro de comando producido por el generador (valor + tipo).</summary>
public sealed record ReporteSqlParam(string Name, object? Value, DbType Type);

/// <summary>SQL generado para una hoja: la sentencia, sus parametros y las columnas de salida.</summary>
public sealed record ReporteSqlConsulta(
    string Sql,
    IReadOnlyList<ReporteSqlParam> Parametros,
    IReadOnlyList<string> Columnas);

public interface IReporteSqlGenerator
{
    /// <summary>
    /// Traduce la definicion VISUAL de una hoja a un SELECT parametrizado de Postgres.
    /// Valida cada campo/cruce contra el modelo semantico (whitelist) e inyecta SIEMPRE
    /// el filtro por tenant sobre la tabla base. El token de tenant se referencia como
    /// <c>@_tenantId</c> y lo liga el ejecutor (que conoce el tenant real).
    /// </summary>
    ReporteSqlConsulta Generar(ReporteHojaDef hoja);
}

/// <summary>
/// Generador de SQL del constructor de reportes. El usuario nunca escribe SQL: elige
/// entidades, campos, cruces y filtros, y esta clase arma el SELECT. Los unicos
/// identificadores que entran al SQL salen del metamodelo de EF (nombres fisicos de
/// tabla/columna resueltos desde tipos CLR declarados en el modelo semantico); los
/// valores de filtro van siempre como parametros. El filtro por <c>tenant_id</c> es
/// obligatorio sobre la tabla base (regla bloqueante multi-tenant).
/// </summary>
public sealed class ReporteSqlGenerator(
    IApplicationDbContext db,
    IReportSemanticModel semantic,
    IReporteCamposDinamicos dinamicos) : IReporteSqlGenerator
{
    private const string EntidadPaciente = "paciente";

    // Salvaguardas: igual que el motor de reportes de 1 hoja.
    private const int DefaultLimite = 10000;
    private const int HardCapLimite = 50000;
    private const string TenantParam = "@_tenantId";
    private const string TenantProperty = nameof(ITenantScoped.TenantId);

    private IModel Model => ((DbContext)db).Model;

    public ReporteSqlConsulta Generar(ReporteHojaDef hoja)
    {
        var baseEntity = semantic.Find(hoja.EntidadKey)
            ?? throw new InvalidOperationException($"Entidad reportable desconocida: '{hoja.EntidadKey}'.");

        // Asigna alias a cada cruce usado en la hoja (t1, t2, ...). t0 es la base.
        var relAliases = AsignarAliasDeCruces(baseEntity, hoja);

        var parametros = new List<ReporteSqlParam>();
        var columnas = new List<string>();
        var sb = new StringBuilder();

        // ---- SELECT ----
        sb.Append("SELECT ");
        if (hoja.Modo == ReporteHojaModo.Detalle)
        {
            if (hoja.Columnas.Count == 0)
            {
                throw new InvalidOperationException($"La hoja '{hoja.Nombre}' (detalle) no tiene columnas.");
            }
            var sel = new List<string>();
            foreach (var col in hoja.Columnas)
            {
                var r = Resolver(baseEntity, relAliases, col.CampoRef);
                var titulo = string.IsNullOrWhiteSpace(col.Titulo) ? r.Field.Label : col.Titulo!;
                sel.Add($"{r.Expr} AS {Q(titulo)}");
                columnas.Add(titulo);
            }
            sb.Append(string.Join(", ", sel));
        }
        else
        {
            if (hoja.GroupBy.Count == 0 && hoja.Agregados.Count == 0)
            {
                throw new InvalidOperationException($"La hoja '{hoja.Nombre}' (agregado) no tiene group-by ni agregados.");
            }
            var sel = new List<string>();
            foreach (var g in hoja.GroupBy)
            {
                var r = Resolver(baseEntity, relAliases, g.CampoRef);
                var titulo = string.IsNullOrWhiteSpace(g.Titulo) ? r.Field.Label : g.Titulo!;
                sel.Add($"{r.Expr} AS {Q(titulo)}");
                columnas.Add(titulo);
            }
            foreach (var a in hoja.Agregados)
            {
                var (expr, tituloDefault) = ExprAgregado(baseEntity, relAliases, a);
                var titulo = string.IsNullOrWhiteSpace(a.Titulo) ? tituloDefault : a.Titulo!;
                sel.Add($"{expr} AS {Q(titulo)}");
                columnas.Add(titulo);
            }
            sb.Append(string.Join(", ", sel));
        }

        // ---- FROM + JOINs ----
        var baseEt = Et(baseEntity.ClrType);
        sb.Append($" FROM {TableRef(baseEt)} AS t0");
        foreach (var (relKey, alias) in relAliases)
        {
            var rel = baseEntity.FindRelation(relKey)!;
            var target = semantic.Find(rel.TargetEntityKey)
                ?? throw new InvalidOperationException($"El cruce '{relKey}' apunta a una entidad desconocida: '{rel.TargetEntityKey}'.");
            var targetEt = Et(target.ClrType);
            var fkCol = Col(baseEt, rel.LocalPropertyName);
            var pkCol = Col(targetEt, rel.TargetKeyPropertyName);
            sb.Append($" LEFT JOIN {TableRef(targetEt)} AS {alias} ON {alias}.{Q(pkCol)} = t0.{Q(fkCol)}");
            if (EsTenantScoped(target))
            {
                sb.Append($" AND {alias}.{Q(TenantColumn(targetEt))} = {TenantParam}");
            }
        }

        // ---- WHERE: tenant (obligatorio) + filtros ----
        var condiciones = new List<string>();
        if (!EsTenantScoped(baseEntity))
        {
            throw new InvalidOperationException(
                $"La entidad base '{baseEntity.Key}' no es tenant-scoped; no se puede garantizar el aislamiento.");
        }
        condiciones.Add($"t0.{Q(TenantColumn(baseEt))} = {TenantParam}");

        var pIdx = 0;
        foreach (var f in hoja.Filtros)
        {
            // Un filtro aun enlazado a un parametro (ParamClave) no fue resuelto: se omite
            // (equivale a parametro sin valor). El flujo real lo resuelve antes via
            // ReporteParametros.Resolver; esto cubre preview y generacion directa sin valores.
            if (!string.IsNullOrWhiteSpace(f.ParamClave)) { continue; }
            var r = Resolver(baseEntity, relAliases, f.CampoRef);
            condiciones.Add(Condicion(r, f, ref pIdx, parametros));
        }
        sb.Append(" WHERE ").Append(string.Join(" AND ", condiciones));

        // ---- GROUP BY ----
        if (hoja.Modo == ReporteHojaModo.Agregado && hoja.GroupBy.Count > 0)
        {
            var grp = hoja.GroupBy.Select(g => Resolver(baseEntity, relAliases, g.CampoRef).Expr);
            sb.Append(" GROUP BY ").Append(string.Join(", ", grp));
        }

        // ---- ORDER BY ----
        if (hoja.Orden.Count > 0)
        {
            var ord = new List<string>();
            foreach (var o in hoja.Orden)
            {
                // En agregado, permitir ordenar por un agregado referenciado por titulo de salida.
                var expr = ResolverOrden(baseEntity, relAliases, hoja, o.CampoRef);
                ord.Add($"{expr}{(o.Desc ? " DESC" : " ASC")}");
            }
            sb.Append(" ORDER BY ").Append(string.Join(", ", ord));
        }

        // ---- LIMIT ----
        var limite = Math.Clamp(hoja.LimiteFilas ?? DefaultLimite, 1, HardCapLimite);
        sb.Append(" LIMIT ").Append(limite.ToString(CultureInfo.InvariantCulture));

        return new ReporteSqlConsulta(sb.ToString(), parametros, columnas);
    }

    // ================= resolucion de referencias =================

    private sealed record Resuelto(string Expr, ReportableFieldDef Field, bool EsBase);

    private Resuelto Resolver(ReportableEntityDef baseEntity, IReadOnlyDictionary<string, string> relAliases, string campoRef)
    {
        var (relKey, fieldKey) = PartirRef(campoRef);
        if (relKey is null)
        {
            var field = FindFieldAug(baseEntity, fieldKey)
                ?? throw new InvalidOperationException($"Campo desconocido '{fieldKey}' en la entidad '{baseEntity.Key}'.");
            return new Resuelto(Expr("t0", baseEntity.ClrType, field), field, true);
        }
        else
        {
            var rel = baseEntity.FindRelation(relKey)
                ?? throw new InvalidOperationException($"Cruce desconocido '{relKey}' en la entidad '{baseEntity.Key}'.");
            var target = semantic.Find(rel.TargetEntityKey)
                ?? throw new InvalidOperationException($"El cruce '{relKey}' apunta a una entidad desconocida.");
            var field = FindFieldAug(target, fieldKey)
                ?? throw new InvalidOperationException($"Campo desconocido '{fieldKey}' en el cruce '{relKey}'.");
            var alias = relAliases[relKey];
            return new Resuelto(Expr(alias, target.ClrType, field), field, false);
        }
    }

    // Busca un campo en la entidad; si es la entidad paciente y no esta en el catalogo
    // estatico, lo busca en los campos dinamicos del tenant (ruta jsonb).
    private ReportableFieldDef? FindFieldAug(ReportableEntityDef entity, string fieldKey)
    {
        var field = entity.FindField(fieldKey);
        if (field is not null) { return field; }
        if (string.Equals(entity.Key, EntidadPaciente, StringComparison.OrdinalIgnoreCase))
        {
            return dinamicos.CamposPaciente.FirstOrDefault(
                c => string.Equals(c.Key, fieldKey, StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }

    // Zona horaria local (Colombia, fija -05, sin DST). Se usa para derivar partes de fechas
    // almacenadas como timestamptz (DateTimeOffset) a hora de pared local.
    private const string ZonaLocal = "America/Bogota";

    // Expresion SQL de un campo: columna CLR directa, lectura por clave si es jsonb, o parte
    // derivada (mes/hora/minuto) de una fecha. La columna siempre sale del metamodelo de EF.
    private string Expr(string alias, Type clrType, ReportableFieldDef field)
    {
        var col = Col(Et(clrType), field.PropertyName);
        string baseExpr;
        if (field.JsonKey is { } jk)
        {
            baseExpr = $"({alias}.{Q(col)} ->> '{jk.Replace("'", "''")}')";
        }
        else
        {
            baseExpr = $"{alias}.{Q(col)}";
        }

        return field.Derivacion switch
        {
            ReportDerivacion.AnioMes => $"to_char({baseExpr} AT TIME ZONE '{ZonaLocal}', 'YYYYMM')",
            ReportDerivacion.Hora24 => $"EXTRACT(HOUR FROM {baseExpr} AT TIME ZONE '{ZonaLocal}')::int",
            ReportDerivacion.Minuto => $"EXTRACT(MINUTE FROM {baseExpr} AT TIME ZONE '{ZonaLocal}')::int",
            _ => baseExpr,
        };
    }

    private (string Expr, string TituloDefault) ExprAgregado(
        ReportableEntityDef baseEntity, IReadOnlyDictionary<string, string> relAliases, ReporteAgregadoDef a)
    {
        if (a.Func == ReporteAgregadoFunc.Count && a.CampoRef is null)
        {
            return ("COUNT(*)", "Cantidad");
        }
        // Indice % del total: participacion de cada grupo sobre el gran total de la hoja.
        // Es una agregacion-de-agregacion con ventana vacia -> suma sobre todos los grupos.
        // Sin campo => share por conteo de filas; con campo => share por suma del campo.
        if (a.Func == ReporteAgregadoFunc.PctTotal)
        {
            if (a.CampoRef is null)
            {
                return (
                    "ROUND((COUNT(*) * 100.0) / NULLIF(SUM(COUNT(*)) OVER (), 0), 2)",
                    "% del total");
            }
            var rp = Resolver(baseEntity, relAliases, a.CampoRef);
            return (
                $"ROUND((SUM({rp.Expr}) * 100.0) / NULLIF(SUM(SUM({rp.Expr})) OVER (), 0), 2)",
                $"% {rp.Field.Label}");
        }
        if (a.CampoRef is null)
        {
            throw new InvalidOperationException($"El agregado {a.Func} requiere un campo.");
        }
        var r = Resolver(baseEntity, relAliases, a.CampoRef);
        var fn = a.Func switch
        {
            ReporteAgregadoFunc.Count => $"COUNT({r.Expr})",
            ReporteAgregadoFunc.CountDistinct => $"COUNT(DISTINCT {r.Expr})",
            ReporteAgregadoFunc.Sum => $"SUM({r.Expr})",
            ReporteAgregadoFunc.Avg => $"AVG({r.Expr})",
            ReporteAgregadoFunc.Min => $"MIN({r.Expr})",
            ReporteAgregadoFunc.Max => $"MAX({r.Expr})",
            _ => throw new InvalidOperationException($"Funcion de agregado no soportada: {a.Func}.")
        };
        var titulo = a.Func switch
        {
            ReporteAgregadoFunc.Count or ReporteAgregadoFunc.CountDistinct => $"Cantidad {r.Field.Label}",
            _ => $"{a.Func} {r.Field.Label}"
        };
        return (fn, titulo);
    }

    private string ResolverOrden(
        ReportableEntityDef baseEntity, IReadOnlyDictionary<string, string> relAliases, ReporteHojaDef hoja, string campoRef)
    {
        // Si el ref coincide con el titulo de un agregado de la hoja, ordenar por ese alias.
        if (hoja.Modo == ReporteHojaModo.Agregado)
        {
            foreach (var a in hoja.Agregados)
            {
                var (_, tituloDefault) = ExprAgregado(baseEntity, relAliases, a);
                var titulo = string.IsNullOrWhiteSpace(a.Titulo) ? tituloDefault : a.Titulo!;
                if (string.Equals(titulo, campoRef, StringComparison.OrdinalIgnoreCase))
                {
                    return Q(titulo);
                }
            }
        }
        return Resolver(baseEntity, relAliases, campoRef).Expr;
    }

    private string Condicion(Resuelto r, ReporteFiltroDef f, ref int pIdx, List<ReporteSqlParam> parametros)
    {
        switch (f.Op)
        {
            case ReporteFiltroOp.IsNull:
                return $"{r.Expr} IS NULL";
            case ReporteFiltroOp.NotNull:
                return $"{r.Expr} IS NOT NULL";
        }

        var valores = f.Valores ?? [];
        switch (f.Op)
        {
            case ReporteFiltroOp.Eq:
                return $"{r.Expr} = {AddParam(valores, 0, r.Field.Kind, ref pIdx, parametros)}";
            case ReporteFiltroOp.Neq:
                return $"{r.Expr} <> {AddParam(valores, 0, r.Field.Kind, ref pIdx, parametros)}";
            case ReporteFiltroOp.Gte:
                return $"{r.Expr} >= {AddParam(valores, 0, r.Field.Kind, ref pIdx, parametros)}";
            case ReporteFiltroOp.Lte:
                return $"{r.Expr} <= {AddParam(valores, 0, r.Field.Kind, ref pIdx, parametros)}";
            case ReporteFiltroOp.Contains:
                {
                    var p = $"@p{pIdx++}";
                    var raw = valores.Count > 0 ? valores[0] : "";
                    parametros.Add(new ReporteSqlParam(p[1..], $"%{raw}%", DbType.String));
                    return $"{r.Expr} ILIKE {p}";
                }
            case ReporteFiltroOp.Between:
                {
                    if (valores.Count < 2)
                    {
                        throw new InvalidOperationException("El filtro BETWEEN requiere dos valores.");
                    }
                    var p0 = AddParam(valores, 0, r.Field.Kind, ref pIdx, parametros);
                    var p1 = AddParam(valores, 1, r.Field.Kind, ref pIdx, parametros);
                    return $"{r.Expr} BETWEEN {p0} AND {p1}";
                }
            case ReporteFiltroOp.In:
                {
                    if (valores.Count == 0)
                    {
                        throw new InvalidOperationException("El filtro IN requiere al menos un valor.");
                    }
                    var ps = new List<string>();
                    for (var i = 0; i < valores.Count; i++)
                    {
                        ps.Add(AddParam(valores, i, r.Field.Kind, ref pIdx, parametros));
                    }
                    return $"{r.Expr} IN ({string.Join(", ", ps)})";
                }
            default:
                throw new InvalidOperationException($"Operador de filtro no soportado: {f.Op}.");
        }
    }

    private static string AddParam(
        IReadOnlyList<string> valores, int idx, ReportFieldKind kind, ref int pIdx, List<ReporteSqlParam> parametros)
    {
        if (idx >= valores.Count)
        {
            throw new InvalidOperationException("Faltan valores para el filtro.");
        }
        var name = $"p{pIdx++}";
        var (value, type) = Coerce(kind, valores[idx]);
        parametros.Add(new ReporteSqlParam(name, value, type));
        return "@" + name;
    }

    private static (object? Value, DbType Type) Coerce(ReportFieldKind kind, string raw)
    {
        raw = raw?.Trim() ?? "";
        return kind switch
        {
            ReportFieldKind.Integer => (long.Parse(raw, CultureInfo.InvariantCulture), DbType.Int64),
            ReportFieldKind.Number => (decimal.Parse(raw, CultureInfo.InvariantCulture), DbType.Decimal),
            ReportFieldKind.Bool => (ParseBool(raw), DbType.Boolean),
            ReportFieldKind.Guid => (Guid.Parse(raw), DbType.Guid),
            ReportFieldKind.Date => (DateOnly.Parse(raw, CultureInfo.InvariantCulture), DbType.Date),
            ReportFieldKind.DateTime => (DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal), DbType.DateTime),
            _ => (raw, DbType.String),
        };
    }

    private static bool ParseBool(string raw) => raw.ToLowerInvariant() switch
    {
        "true" or "1" or "si" or "sí" or "yes" or "t" => true,
        _ => false,
    };

    // ================= alias de cruces =================

    private Dictionary<string, string> AsignarAliasDeCruces(ReportableEntityDef baseEntity, ReporteHojaDef hoja)
    {
        var refs = new List<string>();
        refs.AddRange(hoja.Columnas.Select(c => c.CampoRef));
        refs.AddRange(hoja.GroupBy.Select(c => c.CampoRef));
        refs.AddRange(hoja.Filtros.Select(c => c.CampoRef));
        refs.AddRange(hoja.Orden.Select(c => c.CampoRef));
        refs.AddRange(hoja.Agregados.Where(a => a.CampoRef is not null).Select(a => a.CampoRef!));

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 1;
        foreach (var rf in refs)
        {
            var (relKey, _) = PartirRef(rf);
            if (relKey is null || map.ContainsKey(relKey))
            {
                continue;
            }
            // Valida que el cruce exista antes de darle alias.
            if (baseEntity.FindRelation(relKey) is null)
            {
                throw new InvalidOperationException($"Cruce desconocido '{relKey}' en la entidad '{baseEntity.Key}'.");
            }
            map[relKey] = "t" + i++;
        }
        return map;
    }

    private static (string? RelKey, string FieldKey) PartirRef(string campoRef)
    {
        if (string.IsNullOrWhiteSpace(campoRef))
        {
            throw new InvalidOperationException("Referencia de campo vacia.");
        }
        var dot = campoRef.IndexOf('.');
        return dot < 0 ? (null, campoRef) : (campoRef[..dot], campoRef[(dot + 1)..]);
    }

    // ================= metamodelo EF (nombres fisicos) =================

    private IEntityType Et(Type clr) =>
        Model.FindEntityType(clr)
        ?? throw new InvalidOperationException($"El tipo {clr.Name} no esta mapeado en el modelo de EF.");

    private static string TableRef(IEntityType et)
    {
        var table = et.GetTableName()
            ?? throw new InvalidOperationException($"La entidad {et.Name} no tiene tabla mapeada.");
        var schema = et.GetSchema();
        return string.IsNullOrEmpty(schema) ? Q(table) : $"{Q(schema)}.{Q(table)}";
    }

    private static string Col(IEntityType et, string propertyName)
    {
        var prop = et.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"La propiedad {propertyName} no existe en {et.Name}.");
        var table = et.GetTableName()!;
        var so = StoreObjectIdentifier.Table(table, et.GetSchema());
        return prop.GetColumnName(so)
            ?? throw new InvalidOperationException($"No se pudo resolver la columna de {et.Name}.{propertyName}.");
    }

    private string TenantColumn(IEntityType et) => Col(et, TenantProperty);

    private static bool EsTenantScoped(ReportableEntityDef e) =>
        typeof(ITenantScoped).IsAssignableFrom(e.ClrType);

    private static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
