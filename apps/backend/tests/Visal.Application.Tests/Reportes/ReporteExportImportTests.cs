using System.Text.Json;
using System.Text.Json.Serialization;
using Visal.Application.Tenancy.Reportes;
using Xunit;

namespace Visal.Application.Tests.Reportes;

/// <summary>
/// Cubre las piezas PORTABLES del export/import de plantillas (sin tocar la BD): el sobre JSON
/// round-trip y la validacion de referencias de campo que protege el import entre entornos
/// (dev -> prod). El upsert y la persistencia viven en integracion.
/// </summary>
public sealed class ReporteExportImportTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void El_sobre_de_export_round_trip_preserva_hojas_y_parametros()
    {
        var req = CensoBlueprint.Crear();
        var sobre = new ReportePlantillaExportDto(
            "visal.reporte.plantilla", 1, DateTime.UtcNow,
            req.Nombre, req.Descripcion, req.Categoria, req.Orden, req.Activo, req.Hojas,
            req.MostrarEncabezado, req.Codigo, req.Version, req.Subtitulo, req.Parametros);

        var json = JsonSerializer.Serialize(sobre, Json);
        // Los enums viajan como texto (legible y estable), no como numeros.
        Assert.Contains("RangoFecha", json);
        Assert.DoesNotContain("\"Tipo\":4", json);

        var vuelto = JsonSerializer.Deserialize<ReportePlantillaExportDto>(json, Json);
        Assert.NotNull(vuelto);
        Assert.Equal(req.Nombre, vuelto!.Nombre);
        Assert.Equal(req.Hojas.Count, vuelto.Hojas.Count);
        Assert.NotNull(vuelto.Parametros);
        Assert.Contains(vuelto.Parametros!, p => p.Clave == "sede" && p.Fuente == ReporteParametroFuente.Sedes);
        Assert.Contains(vuelto.Parametros!, p => p.Clave == "rango_fecha" && p.Tipo == ReporteParametroTipo.RangoFecha);
        // El pivote y el modo agregado sobreviven la ida y vuelta.
        var consolidado = vuelto.Hojas.Single(h => h.Nombre == "Consolidado Encuesta");
        Assert.Equal(ReporteHojaModo.Agregado, consolidado.Modo);
        Assert.NotNull(consolidado.Pivote);
    }

    [Fact]
    public void Referencias_no_resueltas_detecta_campo_desconocido_y_respeta_prefijo_de_cruce()
    {
        var hoja = ReporteHojaDef.Detalle("H", "paciente",
            columnas:
            [
                new("numero_documento"),
                new("campo_fantasma"),
            ],
            filtros:
            [
                new("paciente.sede_id", ReporteFiltroOp.Eq), // "rel.campo" -> clave final sede_id
            ]);

        var conocidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "numero_documento", "sede_id" };
        var faltantes = ReporteBuilderService.ReferenciasNoResueltas([hoja], conocidas);

        Assert.Equal(["campo_fantasma"], faltantes);
    }

    [Fact]
    public void Referencias_no_resueltas_ignora_el_orden_por_alias_de_agregado()
    {
        // El orden apunta al TITULO del agregado ("Cantidad"), no a un campo: no debe marcarse.
        var hoja = ReporteHojaDef.Agregado("H", "paciente",
            groupBy: [new("cie10")],
            agregados: [new(ReporteAgregadoFunc.Count, null, "Cantidad")],
            orden: [new("Cantidad", Desc: true)]);

        var conocidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cie10" };
        var faltantes = ReporteBuilderService.ReferenciasNoResueltas([hoja], conocidas);

        Assert.Empty(faltantes);
    }

    [Fact]
    public void El_censo_no_tiene_referencias_fuera_del_modelo_semantico()
    {
        // El CENSO solo usa campos del modelo semantico (ningun campo dinamico), por eso es
        // portable a cualquier entorno sin preparar datos previos.
        var modelo = new ReportSemanticModel();
        var conocidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in modelo.Entities)
        {
            foreach (var f in e.Fields) { conocidas.Add(f.Key); }
        }

        var faltantes = ReporteBuilderService.ReferenciasNoResueltas(CensoBlueprint.Crear().Hojas, conocidas);
        Assert.Empty(faltantes);
    }
}
