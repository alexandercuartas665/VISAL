using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Visal.Domain.Entities;

namespace Visal.Infrastructure.Geo;

/// <summary>
/// Sembrador de geografia colombiana usando la API publica https://api-colombia.com.
/// Idempotente: solo carga si COLOMBIA no tiene departamentos cargados.
/// En caso de fallo de red, registra warning y deja la base como esta (la app sigue
/// funcionando, los selects de geografia quedaran vacios hasta el siguiente arranque).
/// </summary>
public sealed class ApiColombiaSeeder
{
    private const string BaseUrl = "https://api-colombia.com/api/v1/";
    private readonly Persistence.VisalDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<ApiColombiaSeeder> _log;

    public ApiColombiaSeeder(Persistence.VisalDbContext db, IHttpClientFactory http, ILogger<ApiColombiaSeeder> log)
    {
        _db = db;
        _http = http;
        _log = log;
    }

    public async Task EnsureColombiaAsync(CancellationToken ct = default)
    {
        // 1) Pais Colombia (idempotente).
        var colombia = await _db.Paises.FirstOrDefaultAsync(p => p.Codigo == "CO", ct);
        if (colombia is null)
        {
            colombia = new Pais { Codigo = "CO", Nombre = "Colombia", Activo = true };
            _db.Paises.Add(colombia);
            await _db.SaveChangesAsync(ct);
        }

        // 2) Tambien agregar paises vecinos comunes como pais de origen (sin departamentos).
        (string c, string n)[] paisesExtra = { ("VE", "Venezuela"), ("EC", "Ecuador"), ("PA", "Panama"), ("PE", "Peru"), ("BR", "Brasil"), ("US", "Estados Unidos"), ("ES", "España") };
        foreach (var (c, n) in paisesExtra)
        {
            if (!await _db.Paises.AnyAsync(p => p.Codigo == c, ct))
            {
                _db.Paises.Add(new Pais { Codigo = c, Nombre = n, Activo = true });
            }
        }
        await _db.SaveChangesAsync(ct);

        // 3) Si Colombia ya tiene departamentos, asumimos que la siembra ya corrio.
        var tieneDeptos = await _db.Departamentos.AnyAsync(d => d.PaisId == colombia.Id, ct);
        if (tieneDeptos) { return; }

        // 4) Cargar departamentos de api-colombia.com.
        var client = _http.CreateClient("api-colombia");
        client.BaseAddress = new Uri(BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(20);

        List<DepartmentDto>? deptos;
        try
        {
            deptos = await client.GetFromJsonAsync<List<DepartmentDto>>("Department", ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo cargar geografia desde api-colombia.com. Se reintentara en el proximo arranque.");
            return;
        }
        if (deptos is null || deptos.Count == 0)
        {
            _log.LogWarning("api-colombia.com devolvio sin departamentos.");
            return;
        }

        var deptoEntities = new Dictionary<int, Departamento>();
        foreach (var d in deptos.OrderBy(x => x.Name))
        {
            var entity = new Departamento
            {
                PaisId = colombia.Id,
                ExternalId = d.Id,
                Nombre = d.Name,
                Activo = true
            };
            _db.Departamentos.Add(entity);
            deptoEntities[d.Id] = entity;
        }
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Geografia: {N} departamentos de Colombia cargados.", deptoEntities.Count);

        // 5) Cargar municipios por departamento.
        int totalMunis = 0;
        foreach (var (externalId, deptoEntity) in deptoEntities)
        {
            List<CityDto>? cities;
            try
            {
                cities = await client.GetFromJsonAsync<List<CityDto>>($"Department/{externalId}/cities", ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Error cargando municipios del departamento {Id} ({Nombre}). Se omite.", externalId, deptoEntity.Nombre);
                continue;
            }
            if (cities is null) { continue; }
            foreach (var c in cities)
            {
                _db.Municipios.Add(new Municipio
                {
                    DepartamentoId = deptoEntity.Id,
                    ExternalId = c.Id,
                    Nombre = c.Name,
                    Activo = true
                });
                totalMunis++;
            }
            await _db.SaveChangesAsync(ct);
        }
        _log.LogInformation("Geografia: {N} municipios de Colombia cargados.", totalMunis);
    }

    private sealed record DepartmentDto([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("name")] string Name);
    private sealed record CityDto([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("name")] string Name);
}
