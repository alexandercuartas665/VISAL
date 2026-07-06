using System.IO.Compression;
using System.Xml.Linq;

namespace Visal.Application.Common;

/// <summary>
/// Lee imagenes insertadas "en celda" desde xlsx (funcionalidad moderna de Excel
/// que ClosedXML no soporta). Devuelve un mapa (row, col) -> bytes.
///
/// Los xlsx modernos guardan la imagen como "picture in cell" usando este pipeline:
///   sheet.xml celda con vm="N"
///     -> metadata.xml valueMetadata[N-1] con rc t="1" v="X"
///        -> rdrichvalue.xml rv[X] con primer valor = indice de relacion Y
///           -> richValueRel.xml rel[Y] con r:id="rIdN"
///              -> _rels/richValueRel.xml.rels rIdN target ../media/imageXX.png
/// Este reader camina esas 5 capas para devolver bytes por celda.
/// Si el xlsx no usa este formato, el metodo devuelve dict vacio (no lanza).
/// </summary>
public static class XlsxCellImageReader
{
    private static readonly XNamespace NsSs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace NsRv = "http://schemas.microsoft.com/office/spreadsheetml/2017/richdata";
    private static readonly XNamespace NsRvRel = "http://schemas.microsoft.com/office/spreadsheetml/2022/richvaluerel";
    private static readonly XNamespace NsPkgRels = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace NsOfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Lee la primera hoja del xlsx y devuelve las imagenes insertadas en celdas.</summary>
    public static Dictionary<(int Row, int Col), byte[]> ReadFirstSheet(byte[] xlsxBytes)
    {
        var result = new Dictionary<(int Row, int Col), byte[]>();
        using var ms = new MemoryStream(xlsxBytes, writable: false);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        // (1) Celdas con vm en sheet1.
        var cellsWithVm = new Dictionary<(int Row, int Col), int>();
        var sheet1 = zip.GetEntry("xl/worksheets/sheet1.xml");
        if (sheet1 is null) { return result; }
        using (var s = sheet1.Open())
        {
            var doc = XDocument.Load(s);
            foreach (var c in doc.Descendants(NsSs + "c"))
            {
                var vm = c.Attribute("vm")?.Value;
                if (string.IsNullOrEmpty(vm)) { continue; }
                var a1 = c.Attribute("r")?.Value;
                if (string.IsNullOrEmpty(a1)) { continue; }
                if (!int.TryParse(vm, out var vmIdx)) { continue; }
                var (row, col) = ParseA1(a1);
                if (row > 0 && col > 0) { cellsWithVm[(row, col)] = vmIdx; }
            }
        }
        if (cellsWithVm.Count == 0) { return result; }

        // (2) metadata.xml : vm (1-based) -> rich value index.
        var vmToRv = new Dictionary<int, int>();
        var meta = zip.GetEntry("xl/metadata.xml");
        if (meta is not null)
        {
            using var s = meta.Open();
            var doc = XDocument.Load(s);
            var vmParent = doc.Descendants(NsSs + "valueMetadata").FirstOrDefault();
            if (vmParent is not null)
            {
                var i = 0;
                foreach (var bk in vmParent.Elements(NsSs + "bk"))
                {
                    i++;
                    var rc = bk.Element(NsSs + "rc");
                    if (rc is null) { continue; }
                    var v = rc.Attribute("v")?.Value;
                    if (int.TryParse(v, out var rvIdx)) { vmToRv[i] = rvIdx; }
                }
            }
        }

        // (3) rdrichvalue.xml : rv index (0-based) -> indice de relacion (primer <v>).
        var rvToRelIdx = new Dictionary<int, int>();
        var rvXml = zip.GetEntry("xl/richData/rdrichvalue.xml");
        if (rvXml is not null)
        {
            using var s = rvXml.Open();
            var doc = XDocument.Load(s);
            var i = 0;
            foreach (var rv in doc.Descendants(NsRv + "rv"))
            {
                var vs = rv.Elements(NsRv + "v").Select(x => x.Value).ToList();
                if (vs.Count > 0 && int.TryParse(vs[0], out var idx)) { rvToRelIdx[i] = idx; }
                i++;
            }
        }

        // (4) richValueRel.xml : indice (0-based) -> rId.
        var relIdxToRid = new Dictionary<int, string>();
        var rvRelXml = zip.GetEntry("xl/richData/richValueRel.xml");
        if (rvRelXml is not null)
        {
            using var s = rvRelXml.Open();
            var doc = XDocument.Load(s);
            var i = 0;
            foreach (var rel in doc.Descendants(NsRvRel + "rel"))
            {
                var rId = rel.Attribute(NsOfficeRel + "id")?.Value;
                if (!string.IsNullOrEmpty(rId)) { relIdxToRid[i] = rId; }
                i++;
            }
        }

        // (5) _rels/richValueRel.xml.rels : rId -> media path.
        var ridToMedia = new Dictionary<string, string>();
        var relsXml = zip.GetEntry("xl/richData/_rels/richValueRel.xml.rels");
        if (relsXml is not null)
        {
            using var s = relsXml.Open();
            var doc = XDocument.Load(s);
            foreach (var rel in doc.Descendants(NsPkgRels + "Relationship"))
            {
                var rid = rel.Attribute("Id")?.Value;
                var target = rel.Attribute("Target")?.Value;
                if (string.IsNullOrEmpty(rid) || string.IsNullOrEmpty(target)) { continue; }
                var norm = target.StartsWith("../") ? "xl/" + target.Substring(3) : target;
                ridToMedia[rid] = norm;
            }
        }

        // Ensamblar (row, col) -> bytes.
        foreach (var kv in cellsWithVm)
        {
            var vmIdx = kv.Value;
            if (!vmToRv.TryGetValue(vmIdx, out var rvIdx)) { continue; }
            if (!rvToRelIdx.TryGetValue(rvIdx, out var relIdx)) { continue; }
            if (!relIdxToRid.TryGetValue(relIdx, out var rId)) { continue; }
            if (!ridToMedia.TryGetValue(rId, out var mediaPath)) { continue; }
            var mediaEntry = zip.GetEntry(mediaPath);
            if (mediaEntry is null) { continue; }
            using var mms = new MemoryStream();
            using (var s2 = mediaEntry.Open()) { s2.CopyTo(mms); }
            result[kv.Key] = mms.ToArray();
        }
        return result;
    }

    private static (int Row, int Col) ParseA1(string a1)
    {
        var i = 0;
        var col = 0;
        while (i < a1.Length && char.IsLetter(a1[i]))
        {
            col = col * 26 + (char.ToUpperInvariant(a1[i]) - 'A' + 1);
            i++;
        }
        if (i == 0 || i >= a1.Length) { return (0, 0); }
        return int.TryParse(a1.AsSpan(i), out var row) ? (row, col) : (0, 0);
    }
}
