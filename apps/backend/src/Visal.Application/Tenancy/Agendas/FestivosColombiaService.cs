namespace Visal.Application.Tenancy.Agendas;

/// <summary>Un festivo nacional de Colombia en una fecha concreta.</summary>
public sealed record FestivoColombiaDto(DateOnly Fecha, string Nombre);

/// <summary>
/// Calcula los festivos nacionales de Colombia para cualquier anio, de forma
/// determinista y sin dependencias externas (Ola 3 del modulo de Agendas):
///   - Fijos: no se mueven.
///   - Ley Emiliani (Ley 51 de 1983): se trasladan al lunes siguiente si no caen
///     en lunes.
///   - Basados en Pascua: se calculan desde el Domingo de Resurreccion (Computus).
/// Es la fuente para marcar dias festivos en el Calendario y (a futuro) para que
/// la asignacion por agendas cierre esos dias. Servicio puro/stateless.
/// </summary>
public interface IFestivosColombiaService
{
    /// <summary>Festivos del anio, ordenados por fecha.</summary>
    IReadOnlyList<FestivoColombiaDto> DeAnio(int anio);

    /// <summary>Mapa fecha -> nombre del festivo, para lookups rapidos en una vista.</summary>
    IReadOnlyDictionary<DateOnly, string> MapaDeAnio(int anio);
}

public sealed class FestivosColombiaService : IFestivosColombiaService
{
    public IReadOnlyList<FestivoColombiaDto> DeAnio(int anio)
    {
        var lista = new List<FestivoColombiaDto>();

        // ---- Fijos (no se trasladan) ----
        lista.Add(new(new(anio, 1, 1), "Ano Nuevo"));
        lista.Add(new(new(anio, 5, 1), "Dia del Trabajo"));
        lista.Add(new(new(anio, 7, 20), "Dia de la Independencia"));
        lista.Add(new(new(anio, 8, 7), "Batalla de Boyaca"));
        lista.Add(new(new(anio, 12, 8), "Inmaculada Concepcion"));
        lista.Add(new(new(anio, 12, 25), "Navidad"));

        // ---- Ley Emiliani: se trasladan al lunes siguiente ----
        lista.Add(new(Emiliani(new(anio, 1, 6)), "Reyes Magos"));
        lista.Add(new(Emiliani(new(anio, 3, 19)), "San Jose"));
        lista.Add(new(Emiliani(new(anio, 6, 29)), "San Pedro y San Pablo"));
        lista.Add(new(Emiliani(new(anio, 8, 15)), "Asuncion de la Virgen"));
        lista.Add(new(Emiliani(new(anio, 10, 12)), "Dia de la Raza"));
        lista.Add(new(Emiliani(new(anio, 11, 1)), "Todos los Santos"));
        lista.Add(new(Emiliani(new(anio, 11, 11)), "Independencia de Cartagena"));

        // ---- Basados en Pascua ----
        var pascua = DomingoDePascua(anio);
        lista.Add(new(pascua.AddDays(-3), "Jueves Santo"));     // no se traslada
        lista.Add(new(pascua.AddDays(-2), "Viernes Santo"));    // no se traslada
        lista.Add(new(Emiliani(pascua.AddDays(39)), "Ascension del Senor"));   // 39 dias -> lunes
        lista.Add(new(Emiliani(pascua.AddDays(60)), "Corpus Christi"));        // 60 dias -> lunes
        lista.Add(new(Emiliani(pascua.AddDays(68)), "Sagrado Corazon"));       // 68 dias -> lunes

        return lista.OrderBy(f => f.Fecha).ToList();
    }

    public IReadOnlyDictionary<DateOnly, string> MapaDeAnio(int anio)
    {
        var mapa = new Dictionary<DateOnly, string>();
        foreach (var f in DeAnio(anio)) { mapa[f.Fecha] = f.Nombre; }
        return mapa;
    }

    /// <summary>Traslada la fecha al lunes siguiente si no cae ya en lunes (Ley Emiliani).</summary>
    private static DateOnly Emiliani(DateOnly fecha)
    {
        if (fecha.DayOfWeek == DayOfWeek.Monday) { return fecha; }
        var dias = ((int)DayOfWeek.Monday - (int)fecha.DayOfWeek + 7) % 7;
        return fecha.AddDays(dias);
    }

    /// <summary>Domingo de Resurreccion (algoritmo anonimo gregoriano / Computus).</summary>
    private static DateOnly DomingoDePascua(int anio)
    {
        int a = anio % 19;
        int b = anio / 100;
        int c = anio % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int mes = (h + l - 7 * m + 114) / 31;
        int dia = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(anio, mes, dia);
    }
}
