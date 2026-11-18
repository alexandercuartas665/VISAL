using System.IO.Compression;

namespace Visal.Application.Common;

/// <summary>
/// Normaliza las imagenes JPEG embebidas en un .xlsx para que ClosedXML pueda abrir el libro.
///
/// ClosedXML (0.104) identifica el formato de cada imagen por su cabecera y SOLO reconoce un
/// JPEG si el primer segmento despues de SOI es APP0 "JFIF" o APP1 "Exif". Hay JPEG validos
/// (p.ej. firmas pegadas desde el celular/escaner) que empiezan con APP0 "JFXX" u otros APPn;
/// con uno solo de esos el constructor <c>new XLWorkbook(stream)</c> revienta con
/// "Unable to determine the format of the image." y no se puede leer NADA del Excel.
///
/// El arreglo es SIN PERDIDA: si entre los APPn iniciales hay un APP0 JFIF / APP1 Exif, se MUEVE
/// al frente; si no hay ninguno, se antepone un APP0 JFIF estandar. No se elimina ningun segmento
/// (ICC, Adobe, etc. se conservan) y los datos de la imagen (tablas, SOF, scan) no se tocan. Los JPEG que ya son estandar quedan identicos y,
/// si ninguna imagen necesita arreglo, se devuelve el mismo arreglo de bytes de entrada.
/// </summary>
public static class XlsxImagenesSaneador
{
    private const byte Marca = 0xFF;
    private const byte Soi = 0xD8;
    private const byte App0 = 0xE0;
    private const byte App1 = 0xE1;
    private const byte App15 = 0xEF;

    /// <summary>APP0 JFIF 1.01 sin miniatura, densidad 1x1 (sin unidades).</summary>
    private static readonly byte[] App0JfifEstandar =
    {
        0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
    };

    /// <summary>Devuelve el .xlsx con los JPEG embebidos normalizados. Si no hubo nada que
    /// arreglar (o el archivo no es un zip valido) devuelve <paramref name="xlsx"/> tal cual.</summary>
    public static byte[] Sanear(byte[] xlsx) => Sanear(xlsx, out _);

    /// <param name="arregladas">Rutas internas (xl/media/...) de las imagenes normalizadas.</param>
    public static byte[] Sanear(byte[] xlsx, out IReadOnlyList<string> arregladas)
    {
        var lista = new List<string>();
        arregladas = lista;
        try
        {
            // 1) Detectar: solo se reescribe el zip si alguna imagen lo necesita.
            var reemplazos = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var zin = new ZipArchive(new MemoryStream(xlsx, writable: false), ZipArchiveMode.Read))
            {
                foreach (var e in zin.Entries)
                {
                    if (!EsJpegEmbebido(e.FullName)) { continue; }
                    var datos = Leer(e);
                    var nuevo = NormalizarJpeg(datos);
                    if (nuevo is not null) { reemplazos[e.FullName] = nuevo; }
                }
            }
            if (reemplazos.Count == 0) { return xlsx; }

            // 2) Reescribir el paquete copiando todas las entradas en el mismo orden.
            using var salida = new MemoryStream();
            using (var zin = new ZipArchive(new MemoryStream(xlsx, writable: false), ZipArchiveMode.Read))
            using (var zout = new ZipArchive(salida, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var e in zin.Entries)
                {
                    var destino = zout.CreateEntry(e.FullName, CompressionLevel.Optimal);
                    destino.LastWriteTime = e.LastWriteTime;
                    using var ws = destino.Open();
                    if (reemplazos.TryGetValue(e.FullName, out var nuevo))
                    {
                        ws.Write(nuevo, 0, nuevo.Length);
                        lista.Add(e.FullName);
                    }
                    else
                    {
                        using var rs = e.Open();
                        rs.CopyTo(ws);
                    }
                }
            }
            return salida.ToArray();
        }
        catch (InvalidDataException)
        {
            // No es un zip valido: que ClosedXML reporte su propio error.
            return xlsx;
        }
    }

    /// <summary>Normaliza la cabecera de un JPEG para que el primer segmento tras SOI sea APP0
    /// JFIF o APP1 Exif. Devuelve null si ya es estandar o si no es un JPEG reconocible.</summary>
    public static byte[]? NormalizarJpeg(byte[] d)
    {
        if (d.Length < 4 || d[0] != Marca || d[1] != Soi) { return null; }

        if (d.Length < 4 || d[2] != Marca) { return null; }

        // Recorre los APPn iniciales buscando el primer APP0 JFIF / APP1 Exif.
        var pos = 2;
        while (pos + 4 <= d.Length && d[pos] == Marca)
        {
            var m = d[pos + 1];
            if (m < App0 || m > App15) { break; } // primer segmento que no es APPn
            var largo = (d[pos + 2] << 8) | d[pos + 3];
            if (largo < 2 || pos + 2 + largo > d.Length) { return null; } // corrupto: no tocar

            if (EsCabeceraReconocida(d, pos, m, largo))
            {
                if (pos == 2) { return null; } // ya es estandar: no se toca
                // MOVER ese segmento al frente, conservando todos los demas (ICC, Adobe...).
                var seg = d.AsSpan(pos, 2 + largo);
                var r = new byte[d.Length];
                d.AsSpan(0, 2).CopyTo(r);
                seg.CopyTo(r.AsSpan(2));
                d.AsSpan(2, pos - 2).CopyTo(r.AsSpan(2 + seg.Length));
                d.AsSpan(pos + 2 + largo).CopyTo(r.AsSpan(pos + 2 + largo));
                return r;
            }
            pos += 2 + largo;
        }

        // Ningun JFIF/Exif al inicio: se antepone un APP0 JFIF estandar (no se quita nada).
        var res = new byte[d.Length + App0JfifEstandar.Length];
        d.AsSpan(0, 2).CopyTo(res);
        App0JfifEstandar.CopyTo(res.AsSpan(2));
        d.AsSpan(2).CopyTo(res.AsSpan(2 + App0JfifEstandar.Length));
        return res;
    }

    private static bool EsCabeceraReconocida(byte[] d, int pos, byte m, int largo)
    {
        var id = pos + 4;
        if (m == App0 && largo >= 7)
        {
            return d[id] == 'J' && d[id + 1] == 'F' && d[id + 2] == 'I' && d[id + 3] == 'F' && d[id + 4] == 0;
        }
        if (m == App1 && largo >= 7)
        {
            return d[id] == 'E' && d[id + 1] == 'x' && d[id + 2] == 'i' && d[id + 3] == 'f' && d[id + 4] == 0;
        }
        return false;
    }

    private static bool EsJpegEmbebido(string ruta) =>
        ruta.Contains("/media/", StringComparison.OrdinalIgnoreCase)
        && (ruta.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ruta.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));

    private static byte[] Leer(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
