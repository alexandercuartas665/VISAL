using System.Data;
using System.Data.Common;
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;

namespace Visal.Application.Tenancy.Reportes;

public interface IReportePackService
{
    /// <summary>
    /// Ejecuta TODAS las hojas de un pack y devuelve sus datos (columnas + filas por hoja).
    /// Cada hoja se traduce a SQL por el generador (con el filtro de tenant obligatorio) y
    /// se ejecuta parametrizada. El tenant sale del contexto, nunca de la definicion.
    /// </summary>
    Task<ReportePackResultado> EjecutarAsync(ReportePackDef pack, CancellationToken ct = default);

    /// <summary>Arma el Excel multi-hoja (una pestana por hoja) a partir del resultado.</summary>
    byte[] ExportarExcel(ReportePackResultado resultado);
}

public sealed class ReportePackService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IReporteSqlGenerator generador,
    IReporteCamposDinamicos dinamicos) : IReportePackService
{
    // Tope de lectura por hoja (ademas del LIMIT que ya pone el generador).
    private const int MaxFilasPorHoja = 50000;

    public async Task<ReportePackResultado> EjecutarAsync(ReportePackDef pack, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid)
        {
            throw new InvalidOperationException("Sin tenant activo.");
        }

        // Precarga los campos dinamicos del tenant: el generador (sincrono) los lee del cache
        // al resolver refs de paciente que no estan en el catalogo estatico.
        await dinamicos.CargarAsync(ct);

        // Conexion DEDICADA (no la del DbContext scoped): el lector crudo mantiene la
        // conexion ocupada mientras transmite filas; reusar la del DbContext chocaria
        // con otras operaciones EF del mismo circuito Blazor.
        var ctx = (DbContext)db;
        var connString = ctx.Database.GetConnectionString()
            ?? throw new InvalidOperationException("No hay cadena de conexion configurada.");
        var factory = DbProviderFactories.GetFactory(ctx.Database.GetDbConnection());
        await using var conn = factory.CreateConnection()
            ?? throw new InvalidOperationException("No se pudo crear la conexion para ejecutar el reporte.");
        conn.ConnectionString = connString;
        await conn.OpenAsync(ct);

        var hojas = new List<ReporteHojaResultado>(pack.Hojas.Count);
        foreach (var hoja in pack.Hojas)
        {
            var consulta = generador.Generar(hoja);
            var resultado = await EjecutarHojaAsync(conn, hoja.Nombre, consulta, tid, ct);
            // Pivote (Ola 3b): reshape puro del resultado agregado "largo" a matriz.
            if (hoja.Pivote is not null)
            {
                resultado = ReportePivote.Aplicar(hoja, resultado);
            }
            // Formato por columna: solo en detalle, alineado 1:1 al orden de salida.
            else if (hoja.Modo == ReporteHojaModo.Detalle)
            {
                resultado = resultado with { Formatos = hoja.Columnas.Select(c => FormatoExcel(c.Formato)).ToList() };
            }
            hojas.Add(resultado);
        }
        return new ReportePackResultado(pack.Nombre, hojas);
    }

    private static async Task<ReporteHojaResultado> EjecutarHojaAsync(
        DbConnection conn, string nombre, ReporteSqlConsulta consulta, Guid tid, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = consulta.Sql;
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = 60;

        // Tenant: lo liga el ejecutor (no la definicion del reporte). Declarar DbType
        // explicito evita que Postgres falle al inferir el tipo del parametro.
        AddParam(cmd, "_tenantId", tid, DbType.Guid);
        foreach (var p in consulta.Parametros)
        {
            AddParam(cmd, p.Name, p.Value ?? DBNull.Value, p.Type);
        }

        var filas = new List<IReadOnlyList<object?>>();
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            var row = new object?[rd.FieldCount];
            for (var i = 0; i < rd.FieldCount; i++)
            {
                var v = rd.GetValue(i);
                row[i] = v is DBNull ? null : v;
            }
            filas.Add(row);
            if (filas.Count >= MaxFilasPorHoja) { break; }
        }
        return new ReporteHojaResultado(nombre, consulta.Columnas, filas);
    }

    private static void AddParam(DbCommand cmd, string name, object value, DbType type)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    // ================= Excel multi-hoja =================

    public byte[] ExportarExcel(ReportePackResultado resultado)
    {
        using var wb = new XLWorkbook();
        var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hoja in resultado.Hojas)
        {
            var ws = wb.Worksheets.Add(NombreHojaUnico(hoja.Nombre, usados));
            var anchoTabla = Math.Max(1, hoja.Columnas.Count);

            // Cabecera institucional (Ola 3): ocupa unas filas arriba; la tabla empieza abajo.
            var filaHeader = EscribirEncabezado(ws, resultado.Encabezado, hoja.Nombre, anchoTabla);

            for (var c = 0; c < hoja.Columnas.Count; c++)
            {
                ws.Cell(filaHeader, c + 1).Value = hoja.Columnas[c];
                ws.Cell(filaHeader, c + 1).Style.Font.Bold = true;
                ws.Cell(filaHeader, c + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            for (var r = 0; r < hoja.Filas.Count; r++)
            {
                var row = hoja.Filas[r];
                for (var c = 0; c < hoja.Columnas.Count; c++)
                {
                    var v = c < row.Count ? row[c] : null;
                    ws.Cell(filaHeader + 1 + r, c + 1).Value = ToCell(v);
                }
            }
            // Formato de celda por columna (el que eligio el usuario en el constructor).
            if (hoja.Formatos is { } fmts && hoja.Filas.Count > 0)
            {
                for (var c = 0; c < hoja.Columnas.Count; c++)
                {
                    var fmt = c < fmts.Count ? fmts[c] : null;
                    if (!string.IsNullOrEmpty(fmt))
                    {
                        ws.Range(filaHeader + 1, c + 1, filaHeader + hoja.Filas.Count, c + 1)
                          .Style.NumberFormat.Format = fmt;
                    }
                }
            }
            ws.Columns().AdjustToContents();
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static XLCellValue ToCell(object? v) => v switch
    {
        null => XLCellValue.FromObject(null),
        DateTime dt => FechaCell(dt),
        DateTimeOffset dto => FechaCell(dto.LocalDateTime),
        DateOnly d => FechaCell(d.ToDateTime(TimeOnly.MinValue)),
        bool b => XLCellValue.FromObject(b),
        int i => XLCellValue.FromObject(i),
        long l => XLCellValue.FromObject(l),
        short s => XLCellValue.FromObject(s),
        decimal m => XLCellValue.FromObject(m),
        double d2 => XLCellValue.FromObject(d2),
        float f => XLCellValue.FromObject(f),
        _ => XLCellValue.FromObject(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "")
    };

    // Excel/OLE Automation solo admite fechas entre 1900 y 9999. Un valor fuera de ese
    // rango (p.ej. una fila con 0001-01-01 en vez de NULL, o un DateTimeOffset por defecto)
    // hace que ClosedXML lance "Not a legal OleAut date." al guardar y rompe TODO el archivo.
    // Para esos casos degradamos a celda vacia en vez de reventar la descarga.
    private static XLCellValue FechaCell(DateTime dt)
        => dt.Year is >= 1900 and <= 9999 ? XLCellValue.FromObject(dt) : XLCellValue.FromObject(null);

    // Traduce el formato elegido en el constructor a un formato de celda de ClosedXML.
    // Porcentaje usa "%" literal (no multiplica x100): el dato ya viene como porcentaje.
    private static string? FormatoExcel(ReporteColumnaFormato f) => f switch
    {
        ReporteColumnaFormato.Texto => "@",
        ReporteColumnaFormato.Entero => "#,##0",
        ReporteColumnaFormato.Decimal => "#,##0.00",
        ReporteColumnaFormato.Moneda => "\"$\"#,##0",
        ReporteColumnaFormato.Porcentaje => "0.0\"%\"",
        ReporteColumnaFormato.Fecha => "dd/mm/yyyy",
        ReporteColumnaFormato.FechaHora => "dd/mm/yyyy hh:mm",
        _ => null
    };

    // Pinta la cabecera institucional arriba de la hoja y devuelve la fila donde va el
    // encabezado de la TABLA (1 si no hay cabecera). Agencia + titulo/subtitulo + linea de
    // control documental (Codigo / Version / Fecha de generacion).
    private static int EscribirEncabezado(IXLWorksheet ws, ReporteEncabezado? enc, string nombreHoja, int ancho)
    {
        if (enc is null) { return 1; }

        void Merge(int fila)
        {
            if (ancho > 1) { ws.Range(fila, 1, fila, ancho).Merge(); }
        }

        ws.Cell(1, 1).Value = enc.Agencia;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        Merge(1);

        var titulo = string.IsNullOrWhiteSpace(enc.Subtitulo) ? enc.Titulo : $"{enc.Titulo} - {enc.Subtitulo}";
        ws.Cell(2, 1).Value = $"{titulo}  |  {nombreHoja}";
        ws.Cell(2, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Style.Font.FontSize = 11;
        Merge(2);

        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(enc.Codigo)) { partes.Add($"Codigo: {enc.Codigo}"); }
        if (!string.IsNullOrWhiteSpace(enc.Version)) { partes.Add($"Version: {enc.Version}"); }
        partes.Add($"Fecha de generacion: {enc.FechaGeneracion:dd/MM/yyyy HH:mm}");
        ws.Cell(3, 1).Value = string.Join("    ", partes);
        ws.Cell(3, 1).Style.Font.Italic = true;
        ws.Cell(3, 1).Style.Font.FontSize = 9;
        ws.Cell(3, 1).Style.Font.FontColor = XLColor.Gray;
        Merge(3);

        // Fila 4 en blanco; la tabla arranca en la 5.
        return 5;
    }

    // Nombre de pestana valido para Excel: sin caracteres prohibidos, max 31 chars, unico.
    private static string NombreHojaUnico(string nombre, HashSet<string> usados)
    {
        var limpio = new string((nombre ?? "Hoja")
            .Select(c => c is ':' or '\\' or '/' or '?' or '*' or '[' or ']' ? ' ' : c).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(limpio)) { limpio = "Hoja"; }
        if (limpio.Length > 31) { limpio = limpio[..31]; }

        var candidato = limpio;
        var n = 2;
        while (usados.Contains(candidato))
        {
            var sufijo = $" ({n++})";
            var baseLen = Math.Min(limpio.Length, 31 - sufijo.Length);
            candidato = limpio[..baseLen] + sufijo;
        }
        usados.Add(candidato);
        return candidato;
    }
}
