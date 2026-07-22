using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Visal.Application.Common;
using Visal.Application.Facturacion;
using Visal.Domain.Enums;
using Visal.Infrastructure.Persistence;
using Xunit;

namespace Visal.Application.Tests.Facturacion;

/// <summary>
/// Tests unitarios del motor generico de snapshots.
///
/// Backend: EF Core InMemory. jsonb se degrada a string plano en InMemory pero
/// el servicio serializa/deserializa manualmente, asi que la semantica se
/// preserva. Global query filter por TenantId aplica en InMemory igual que en Postgres.
/// </summary>
public sealed class FacturacionSnapshotServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Actor = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static (VisalDbContext ctx, FakeTenantContext tenant) Db(Guid tenantId, string? dbName = null)
    {
        var name = dbName ?? Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<VisalDbContext>()
            .UseInMemoryDatabase(name)
            // El motor jsonb no existe en InMemory; ignoramos el warning cuando setea
            // HasColumnType("jsonb") — el string almacena igual.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var tenant = new FakeTenantContext { TenantId = tenantId, UserId = Actor };
        return (new VisalDbContext(opts, tenant), tenant);
    }

    /// <summary>
    /// Fake vacio del override de columnas — devuelve la lista canonica del
    /// builder tal cual. Los tests de este archivo no ejercitan el override,
    /// solo el motor de snapshot.
    /// </summary>
    private sealed class FakeColumnaConfig : ISnapshotColumnaConfigService
    {
        public Task<IReadOnlyList<ColumnaConfigItemDto>> ListarAsync(TipoSnapshot tipo, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ColumnaConfigItemDto>>(Array.Empty<ColumnaConfigItemDto>());
        public Task GuardarAsync(TipoSnapshot tipo, IReadOnlyList<ColumnaConfigItemDto> items, Guid actorUserId, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetAsync(TipoSnapshot tipo, Guid actorUserId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ColumnaExportInfo>> ObtenerParaExportAsync(TipoSnapshot tipo, IReadOnlyList<string> columnasCanonicas, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ColumnaExportInfo>>(columnasCanonicas.Select(c => new ColumnaExportInfo(c, c)).ToArray());
    }

    [Fact]
    public async Task GenerarAsync_ConBuilderQueEmite3Filas_TerminaVigenteYPersistePrompt()
    {
        var (ctx, tenant) = Db(TenantA);
        var builder = new BuilderFake(
            TipoSnapshot.RelacionFacturas,
            new[] { "Col1", "Col2" },
            new IReadOnlyDictionary<string, object?>[]
            {
                new Dictionary<string, object?> { ["Col1"] = "a", ["Col2"] = 1L },
                new Dictionary<string, object?> { ["Col1"] = "b", ["Col2"] = 2L },
                new Dictionary<string, object?> { ["Col1"] = "c", ["Col2"] = 3L }
            });
        var svc = new FacturacionSnapshotService(ctx, tenant, new[] { builder }, new FakeColumnaConfig());

        var id = await svc.GenerarAsync(
            new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "ASMET Junio", "{\"foo\":1}"),
            Actor);

        var snap = await ctx.FacturacionSnapshots.SingleAsync(x => x.Id == id);
        Assert.Equal(EstadoSnapshot.Vigente, snap.Estado);
        Assert.Equal(3, snap.TotalFilas);
        Assert.Equal("ASMET Junio", snap.Nombre);
        Assert.NotNull(snap.FechaEjecucionFin);
        Assert.Null(snap.ErrorMensaje);

        var filas = await ctx.FacturacionSnapshotFilas.Where(x => x.SnapshotId == id).ToListAsync();
        Assert.Equal(3, filas.Count);
        Assert.Equal(new[] { 1, 2, 3 }, filas.OrderBy(x => x.NumeroFila).Select(x => x.NumeroFila));
    }

    [Fact]
    public async Task GenerarAsync_SinBuilderRegistrado_LanzaInvalidOperation()
    {
        var (ctx, tenant) = Db(TenantA);
        var svc = new FacturacionSnapshotService(ctx, tenant, Array.Empty<ISnapshotBuilder>(), new FakeColumnaConfig());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "x", "{}"), Actor));
    }

    [Fact]
    public async Task GenerarAsync_BuilderQueLanza_QuedaFallidoConMensaje()
    {
        var (ctx, tenant) = Db(TenantA);
        var builder = new BuilderQueLanza(TipoSnapshot.RelacionFacturas, "boom!");
        var svc = new FacturacionSnapshotService(ctx, tenant, new[] { (ISnapshotBuilder)builder }, new FakeColumnaConfig());

        var id = await svc.GenerarAsync(
            new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, null, "{}"),
            Actor);

        var snap = await ctx.FacturacionSnapshots.SingleAsync(x => x.Id == id);
        Assert.Equal(EstadoSnapshot.Fallido, snap.Estado);
        Assert.Equal("boom!", snap.ErrorMensaje);
        // El nombre debe haberse auto-generado.
        Assert.StartsWith(TipoSnapshot.RelacionFacturas.ToString(), snap.Nombre);
    }

    [Fact]
    public async Task ListarAsync_FiltraPorEstadoYTipo()
    {
        var dbName = Guid.NewGuid().ToString();
        var (ctx, tenant) = Db(TenantA, dbName);
        var builder = new BuilderFake(TipoSnapshot.RelacionFacturas, new[] { "X" },
            new IReadOnlyDictionary<string, object?>[] { new Dictionary<string, object?> { ["X"] = "y" } });
        var svc = new FacturacionSnapshotService(ctx, tenant, new[] { builder }, new FakeColumnaConfig());

        var idVigente = await svc.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "v", "{}"), Actor);
        var idArchivar = await svc.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "a", "{}"), Actor);
        await svc.ArchivarAsync(idArchivar, "motivo prueba", Actor);

        var vigentes = await svc.ListarAsync(EstadoSnapshot.Vigente);
        var archivados = await svc.ListarAsync(EstadoSnapshot.Archivado);

        Assert.Single(vigentes);
        Assert.Equal(idVigente, vigentes[0].Id);
        Assert.Single(archivados);
        Assert.Equal(idArchivar, archivados[0].Id);
    }

    [Fact]
    public async Task ArchivarAsync_MotivoMenorA10Chars_Lanza()
    {
        var (ctx, tenant) = Db(TenantA);
        var builder = new BuilderFake(TipoSnapshot.RelacionFacturas, new[] { "X" },
            new IReadOnlyDictionary<string, object?>[] { new Dictionary<string, object?> { ["X"] = 1L } });
        var svc = new FacturacionSnapshotService(ctx, tenant, new[] { builder }, new FakeColumnaConfig());

        var id = await svc.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "x", "{}"), Actor);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ArchivarAsync(id, "corto", Actor));

        var snap = await ctx.FacturacionSnapshots.SingleAsync(x => x.Id == id);
        Assert.Equal(EstadoSnapshot.Vigente, snap.Estado);
    }

    [Fact]
    public async Task ArchivarAsync_SnapshotNoVigente_Lanza()
    {
        var (ctx, tenant) = Db(TenantA);
        var builder = new BuilderQueLanza(TipoSnapshot.RelacionFacturas, "boom");
        var svc = new FacturacionSnapshotService(ctx, tenant, new[] { (ISnapshotBuilder)builder }, new FakeColumnaConfig());

        var id = await svc.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "x", "{}"), Actor);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ArchivarAsync(id, "no aplica al fallido", Actor));
    }

    [Fact]
    public async Task ListarFilasAsync_PaginacionYBusquedaFuncionan()
    {
        var (ctx, tenant) = Db(TenantA);
        var builder = new BuilderFake(TipoSnapshot.RelacionFacturas, new[] { "Nombre" },
            Enumerable.Range(1, 10)
                .Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["Nombre"] = $"paciente{i}" })
                .ToArray());
        var svc = new FacturacionSnapshotService(ctx, tenant, new[] { builder }, new FakeColumnaConfig());

        var id = await svc.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "x", "{}"), Actor);

        var page1 = await svc.ListarFilasAsync(id, pagina: 1, tamanoPagina: 4);
        Assert.Equal(10, page1.Total);
        Assert.Equal(4, page1.Items.Count);
        Assert.Equal("paciente1", page1.Items[0]["Nombre"]);

        var page3 = await svc.ListarFilasAsync(id, pagina: 3, tamanoPagina: 4);
        Assert.Equal(2, page3.Items.Count);
        Assert.Equal("paciente9", page3.Items[0]["Nombre"]);

        var busqueda = await svc.ListarFilasAsync(id, 1, 50, buscar: "paciente5");
        Assert.Single(busqueda.Items);
    }

    [Fact]
    public async Task ExportarExcelAsync_HeadersExactosYFilasCorrectas()
    {
        var (ctx, tenant) = Db(TenantA);
        // Header con tildes y otro caracter raro para verificar que se preserva.
        var cols = new[] { "Consecutivo Factura", "Identificación", "Descripción del procedimiento (Factura)" };
        var filas = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?> { ["Consecutivo Factura"] = null, ["Identificación"] = "2245956", ["Descripción del procedimiento (Factura)"] = "ATENCION DOMICILIARIA" },
            new Dictionary<string, object?> { ["Consecutivo Factura"] = null, ["Identificación"] = "104578855", ["Descripción del procedimiento (Factura)"] = "TERAPIA FISICA" }
        };
        var builder = new BuilderFake(TipoSnapshot.RelacionFacturas, cols, filas);
        var svc = new FacturacionSnapshotService(ctx, tenant, new[] { builder }, new FakeColumnaConfig());

        var id = await svc.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "ASMET Junio", "{}"), Actor);
        var archivo = await svc.ExportarExcelAsync(id);

        Assert.NotNull(archivo);
        Assert.EndsWith(".xlsx", archivo!.NombreArchivo);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", archivo.MimeType);

        // Re-abrir el xlsx y validar headers + filas.
        using var ms = new MemoryStream(archivo.Contenido);
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var hoja = wb.Worksheets.First();
        Assert.Equal("Consecutivo Factura", hoja.Cell(1, 1).GetString());
        Assert.Equal("Identificación", hoja.Cell(1, 2).GetString());
        Assert.Equal("Descripción del procedimiento (Factura)", hoja.Cell(1, 3).GetString());
        Assert.Equal("2245956", hoja.Cell(2, 2).GetString());
        Assert.Equal("104578855", hoja.Cell(3, 2).GetString());
    }

    [Fact]
    public async Task ExportarExcelAsync_FormatoFechaYHoraSePreservanComoTexto()
    {
        // Spec §Fase 4C: las fechas deben quedar como "YYYY-MM-DD" y las horas
        // como "HH:MM:SS". El builder ya las emite formateadas — este test
        // garantiza que el pipeline generar->persistir->exportar no altera el
        // string original (por ejemplo, ClosedXML no debe re-interpretar como
        // fecha nativa y cambiar el formato).
        var (ctx, tenant) = Db(TenantA);
        var cols = new[] { "Fecha de Nacimiento", "Fecha suministro de tecnologia", "Hora" };
        var filas = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?>
            {
                ["Fecha de Nacimiento"] = "1985-04-12",
                ["Fecha suministro de tecnologia"] = "2026-06-15",
                ["Hora"] = "14:35:22"
            }
        };
        var builder = new BuilderFake(TipoSnapshot.RelacionFacturas, cols, filas);
        var svc = new FacturacionSnapshotService(ctx, tenant, new[] { builder }, new FakeColumnaConfig());

        var id = await svc.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "x", "{}"), Actor);
        var archivo = await svc.ExportarExcelAsync(id);

        using var ms = new MemoryStream(archivo!.Contenido);
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var hoja = wb.Worksheets.First();
        Assert.Equal("1985-04-12", hoja.Cell(2, 1).GetString());
        Assert.Equal("2026-06-15", hoja.Cell(2, 2).GetString());
        Assert.Equal("14:35:22", hoja.Cell(2, 3).GetString());
    }

    [Fact]
    public async Task ExportarExcelAsync_NumerosSinFormatoDeMoneda()
    {
        // Spec §Fase 4C: "Numeros: enteros sin formato de moneda". Los decimales
        // deben aparecer como numero plano en la celda, sin simbolo $ ni miles.
        var (ctx, tenant) = Db(TenantA);
        var cols = new[] { "Cantidad", "Valor Unitario", "Valor Total" };
        var filas = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?>
            {
                ["Cantidad"] = 1L,
                ["Valor Unitario"] = 145000m,
                ["Valor Total"] = 145000m
            }
        };
        var builder = new BuilderFake(TipoSnapshot.RelacionFacturas, cols, filas);
        var svc = new FacturacionSnapshotService(ctx, tenant, new[] { builder }, new FakeColumnaConfig());

        var id = await svc.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "x", "{}"), Actor);
        var archivo = await svc.ExportarExcelAsync(id);

        using var ms = new MemoryStream(archivo!.Contenido);
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var hoja = wb.Worksheets.First();

        // Las celdas son numericas — el DataType lo confirma.
        Assert.Equal(ClosedXML.Excel.XLDataType.Number, hoja.Cell(2, 1).DataType);
        Assert.Equal(ClosedXML.Excel.XLDataType.Number, hoja.Cell(2, 2).DataType);
        Assert.Equal(1d, hoja.Cell(2, 1).GetDouble());
        Assert.Equal(145000d, hoja.Cell(2, 2).GetDouble());
        // Formato de numero por default de ClosedXML — cadena vacia = "General",
        // que es lo que exige el spec (sin moneda, sin miles, sin decimales fijos).
        Assert.True(string.IsNullOrEmpty(hoja.Cell(2, 2).Style.NumberFormat.Format));
    }

    [Fact]
    public async Task ExportarExcelAsync_ColumnasNulasQuedanVaciasNoCero()
    {
        // Spec §4 col 1/2/6: Consecutivo Factura, Orden, Archivo json quedan
        // vacios en el snapshot (se llenan en un proceso posterior). El export
        // no debe escribir 0 ni cadena "null" — la celda debe quedar realmente vacia.
        var (ctx, tenant) = Db(TenantA);
        var cols = new[] { "Consecutivo Factura", "Orden", "Contrato" };
        var filas = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?>
            {
                ["Consecutivo Factura"] = null,
                ["Orden"] = null,
                ["Contrato"] = "TOL-004-26-P"
            }
        };
        var builder = new BuilderFake(TipoSnapshot.RelacionFacturas, cols, filas);
        var svc = new FacturacionSnapshotService(ctx, tenant, new[] { builder }, new FakeColumnaConfig());

        var id = await svc.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "x", "{}"), Actor);
        var archivo = await svc.ExportarExcelAsync(id);

        using var ms = new MemoryStream(archivo!.Contenido);
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var hoja = wb.Worksheets.First();
        Assert.True(hoja.Cell(2, 1).IsEmpty());
        Assert.True(hoja.Cell(2, 2).IsEmpty());
        Assert.Equal("TOL-004-26-P", hoja.Cell(2, 3).GetString());
    }

    [Fact]
    public async Task ExportarCsvAsync_UsaSeparadorPuntoComaYUtf8Bom()
    {
        var (ctx, tenant) = Db(TenantA);
        var cols = new[] { "Col1", "Col2" };
        var filas = new IReadOnlyDictionary<string, object?>[]
        {
            new Dictionary<string, object?> { ["Col1"] = "a; con punto y coma", ["Col2"] = "linea normal" },
            new Dictionary<string, object?> { ["Col1"] = "sin escape", ["Col2"] = 42L }
        };
        var builder = new BuilderFake(TipoSnapshot.RelacionFacturas, cols, filas);
        var svc = new FacturacionSnapshotService(ctx, tenant, new[] { builder }, new FakeColumnaConfig());

        var id = await svc.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "x", "{}"), Actor);
        var archivo = await svc.ExportarCsvAsync(id);

        Assert.NotNull(archivo);
        Assert.EndsWith(".csv", archivo!.NombreArchivo);

        // BOM UTF-8: EF BB BF.
        Assert.True(archivo.Contenido.Length >= 3);
        Assert.Equal(0xEF, archivo.Contenido[0]);
        Assert.Equal(0xBB, archivo.Contenido[1]);
        Assert.Equal(0xBF, archivo.Contenido[2]);

        var texto = System.Text.Encoding.UTF8.GetString(archivo.Contenido);
        // Los ; dentro de un valor se escapan con comillas.
        Assert.Contains("Col1;Col2", texto);
        Assert.Contains("\"a; con punto y coma\";linea normal", texto);
        Assert.Contains("sin escape;42", texto);
    }

    [Fact]
    public async Task ExportarAsync_SnapshotInexistente_DevuelveNull()
    {
        var (ctx, tenant) = Db(TenantA);
        var svc = new FacturacionSnapshotService(ctx, tenant, Array.Empty<ISnapshotBuilder>(), new FakeColumnaConfig());

        var xlsx = await svc.ExportarExcelAsync(Guid.NewGuid());
        var csv = await svc.ExportarCsvAsync(Guid.NewGuid());

        Assert.Null(xlsx);
        Assert.Null(csv);
    }

    [Fact]
    public async Task ExportarAsync_SnapshotDeOtroTenant_DevuelveNull()
    {
        // Reproduce que aislamiento tenant se aplica tambien en el export.
        var dbName = Guid.NewGuid().ToString();
        var (ctxA, tenantA) = Db(TenantA, dbName);
        var builder = new BuilderFake(TipoSnapshot.RelacionFacturas, new[] { "X" },
            new IReadOnlyDictionary<string, object?>[] { new Dictionary<string, object?> { ["X"] = "solo A" } });
        var svcA = new FacturacionSnapshotService(ctxA, tenantA, new[] { builder }, new FakeColumnaConfig());
        var idA = await svcA.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "A", "{}"), Actor);

        var (ctxB, tenantB) = Db(TenantB, dbName);
        var svcB = new FacturacionSnapshotService(ctxB, tenantB, new[] { builder }, new FakeColumnaConfig());

        Assert.Null(await svcB.ExportarExcelAsync(idA));
        Assert.Null(await svcB.ExportarCsvAsync(idA));
    }

    [Fact]
    public async Task AislamientoTenant_SnapshotDeTenantANoEsVisibleDesdeTenantB()
    {
        // Comparten el mismo store InMemory pero usan tenants distintos.
        var dbName = Guid.NewGuid().ToString();
        var (ctxA, tenantA) = Db(TenantA, dbName);
        var builder = new BuilderFake(TipoSnapshot.RelacionFacturas, new[] { "X" },
            new IReadOnlyDictionary<string, object?>[] { new Dictionary<string, object?> { ["X"] = "solo A" } });
        var svcA = new FacturacionSnapshotService(ctxA, tenantA, new[] { builder }, new FakeColumnaConfig());

        var idA = await svcA.GenerarAsync(new GenerarSnapshotCmd(TipoSnapshot.RelacionFacturas, "A", "{}"), Actor);

        // Ahora el mismo store visto desde tenant B.
        var (ctxB, tenantB) = Db(TenantB, dbName);
        var svcB = new FacturacionSnapshotService(ctxB, tenantB, new[] { builder }, new FakeColumnaConfig());

        var listaB = await svcB.ListarAsync(EstadoSnapshot.Vigente);
        Assert.Empty(listaB);

        var detalleDesdeB = await svcB.ObtenerAsync(idA);
        Assert.Null(detalleDesdeB);

        var filasDesdeB = await svcB.ListarFilasAsync(idA, 1, 10);
        Assert.Empty(filasDesdeB.Items);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svcB.ArchivarAsync(idA, "intento cross-tenant no autorizado", Actor));
    }

    // ---- Fakes ----

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? SucursalId { get; set; }
    }

    private sealed class BuilderFake : ISnapshotBuilder
    {
        private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _filas;
        public BuilderFake(TipoSnapshot tipo, IReadOnlyList<string> cols,
            IReadOnlyList<IReadOnlyDictionary<string, object?>> filas)
        {
            TipoAplicable = tipo;
            Columnas = cols;
            _filas = filas;
        }
        public TipoSnapshot TipoAplicable { get; }
        public IReadOnlyList<string> Columnas { get; }
        public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ConstruirAsync(
            string filtrosJson,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var f in _filas)
            {
                await Task.Yield();
                yield return f;
            }
        }
    }

    private sealed class BuilderQueLanza : ISnapshotBuilder
    {
        private readonly string _msg;
        public BuilderQueLanza(TipoSnapshot tipo, string msg)
        {
            TipoAplicable = tipo;
            _msg = msg;
        }
        public TipoSnapshot TipoAplicable { get; }
        public IReadOnlyList<string> Columnas { get; } = Array.Empty<string>();
#pragma warning disable CS1998
        public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ConstruirAsync(
            string filtrosJson,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            throw new InvalidOperationException(_msg);
            yield break;
        }
#pragma warning restore CS1998
    }
}
