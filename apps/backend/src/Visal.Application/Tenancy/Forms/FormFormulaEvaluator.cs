using System.Globalization;
using System.Text.RegularExpressions;

namespace Visal.Application.Tenancy.Forms;

/// <summary>
/// Motor de formulas de formularios. Extraido del FormViewer para poder
/// reusarlo tanto a nivel TOP-LEVEL (FormNode.Formula, resolviendo
/// identificadores contra el diccionario de valores del formulario) como a
/// nivel de CELDA dentro de una tabla repetible (FormColumn.Formula,
/// resolviendo identificadores contra las celdas de la MISMA fila). Es una
/// funcion pura: (formula, valores) -> texto. No persiste, no loggea, no
/// tiene estado.
///
/// Sintaxis soportada (identica en ambos contextos):
///   sum(a, b, c, ...)                 -> suma numerica de los campos por nombre
///   sumprod(a:peso1, b:peso2, ...)    -> Sum(valor * peso)
///   cases(ref, "lo-hi=Etiqueta;...")  -> mapeo por rango
///   edad(fecha_nacimiento)            -> anios desde la fecha a hoy
///   imc(pesoName, tallaName)          -> IMC (kg / m^2), un decimal
///   imcClass(imcRefName)              -> clasificacion OMS del IMC
///   tensionClass(sisName, diaName)    -> clasificacion HTA ESH/ESC 2018
///   perimetroRiesgo(perimName, sexoName) -> riesgo cardiometabolico
///
/// Regla comun: si un operando referenciado esta vacio o no es numerico
/// valido, se devuelve "" (nunca NaN ni 0) para que el viewer no muestre nada.
/// </summary>
public static class FormFormulaEvaluator
{
    /// <summary>Evalua la formula resolviendo cada identificador contra
    /// <paramref name="values"/>. Devuelve "" si la formula no se reconoce o
    /// si algun operando falta / no es numerico.</summary>
    public static string Evaluate(string formula, IReadOnlyDictionary<string, string?> values)
    {
        var f = formula.Trim();
        var ci = CultureInfo.InvariantCulture;

        // sum(...)
        var mSum = Regex.Match(f, @"^sum\s*\(([^)]*)\)\s*$", RegexOptions.IgnoreCase);
        if (mSum.Success)
        {
            double total = 0;
            // Para extraer el numero al principio de strings tipo "10 - Independiente..."
            var numFirst = new Regex(@"-?\d+(?:[.,]\d+)?");
            foreach (var raw in mSum.Groups[1].Value.Split(','))
            {
                var name = raw.Trim();
                if (string.IsNullOrEmpty(name)) { continue; }
                if (!values.TryGetValue(name, out var v) || string.IsNullOrWhiteSpace(v)) { continue; }

                // Primero intentamos parse directo (campos number puros).
                if (double.TryParse(v, NumberStyles.Any, ci, out var nDirect))
                {
                    total += nDirect;
                    continue;
                }
                // Fallback: extraer el primer numero del string (selects tipo "10 - texto...").
                var m = numFirst.Match(v);
                if (m.Success &&
                    double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Any, ci, out var nExtract))
                {
                    total += nExtract;
                }
            }
            return (total == Math.Floor(total) ? ((long)total).ToString(ci) : total.ToString(ci));
        }

        // sumprod(field1:peso1, field2:peso2, ...)  -> Sum(valorField * peso)
        // Util para escalas donde cada item tiene un ponderador distinto
        // (ej: Escala Visal de requerimiento de enfermeria).
        var mSp = Regex.Match(f, @"^sumprod\s*\(([^)]*)\)\s*$", RegexOptions.IgnoreCase);
        if (mSp.Success)
        {
            double total = 0;
            var numFirst = new Regex(@"-?\d+(?:[.,]\d+)?");
            foreach (var raw in mSp.Groups[1].Value.Split(','))
            {
                var pair = raw.Split(':');
                if (pair.Length != 2) { continue; }
                var name = pair[0].Trim();
                if (!double.TryParse(pair[1].Trim().Replace(',', '.'),
                        NumberStyles.Any, ci, out var peso)) { continue; }
                if (!values.TryGetValue(name, out var v) || string.IsNullOrWhiteSpace(v)) { continue; }

                double n;
                if (!double.TryParse(v, NumberStyles.Any, ci, out n))
                {
                    var m = numFirst.Match(v);
                    if (!m.Success ||
                        !double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Any, ci, out n))
                    { continue; }
                }
                total += (n * peso);
            }
            // Mostrar con un decimal cuando no es entero (Enfermeria da fraccionarios).
            return (total == Math.Floor(total)
                ? ((long)total).ToString(ci)
                : total.ToString("0.0", ci));
        }

        // cases(refName, "0-19=DEPENDENCIA TOTAL;20-35=DEPENDENCIA SEVERA;...")
        var mCases = Regex.Match(f, @"^cases\s*\(\s*([A-Za-z0-9_]+)\s*,\s*""(.+)""\s*\)\s*$");
        if (mCases.Success)
        {
            var refName = mCases.Groups[1].Value;
            var rules = mCases.Groups[2].Value;
            if (!values.TryGetValue(refName, out var refVal) || string.IsNullOrWhiteSpace(refVal))
            {
                return "";
            }
            double num;
            if (!double.TryParse(refVal, NumberStyles.Any, ci, out num))
            {
                // Mismo fallback que sum(): extraer el primer numero del string.
                var m = Regex.Match(refVal, @"-?\d+(?:[.,]\d+)?");
                if (!m.Success ||
                    !double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Any, ci, out num))
                {
                    return "";
                }
            }
            foreach (var rule in rules.Split(';'))
            {
                var parts = rule.Split(new[] { '=' }, 2);
                if (parts.Length != 2) { continue; }
                var range = parts[0].Trim();
                var label = parts[1].Trim();
                if (string.Equals(range, "default", StringComparison.OrdinalIgnoreCase)) { return label; }
                var mr = Regex.Match(range, @"^(-?\d+(?:\.\d+)?)\s*-\s*(-?\d+(?:\.\d+)?)$");
                if (mr.Success)
                {
                    var lo = double.Parse(mr.Groups[1].Value, ci);
                    var hi = double.Parse(mr.Groups[2].Value, ci);
                    if (num >= lo && num <= hi) { return label; }
                }
            }
            return "";
        }

        // edad(fecha_nacimiento) - util en HC.
        var mEdad = Regex.Match(f, @"^edad\s*\(\s*([A-Za-z0-9_]+)\s*\)\s*$", RegexOptions.IgnoreCase);
        if (mEdad.Success)
        {
            if (values.TryGetValue(mEdad.Groups[1].Value, out var dv) && !string.IsNullOrWhiteSpace(dv) &&
                DateTime.TryParse(dv, ci, DateTimeStyles.None, out var dob))
            {
                var today = DateTime.Today;
                var age = today.Year - dob.Year;
                if (dob.Date > today.AddYears(-age)) { age--; }
                return age.ToString(ci);
            }
            return "";
        }

        // -- Calculadores clinicos (IMC + clasificacion, tension, perimetro abdominal) --
        // Helper local que lee el valor de un field y extrae el primer numero
        // (mismo fallback que sum/sumprod/cases: parse directo, sino primer match
        // de numero embebido en strings tipo "120 - Normal"). Devuelve null si
        // no hay valor o no contiene numero valido.
        double? LeerNum(string fieldName)
        {
            if (!values.TryGetValue(fieldName, out var v) || string.IsNullOrWhiteSpace(v)) { return null; }
            if (double.TryParse(v, NumberStyles.Any, ci, out var nDirect)) { return nDirect; }
            var mNum = Regex.Match(v, @"-?\d+(?:[.,]\d+)?");
            if (mNum.Success &&
                double.TryParse(mNum.Value.Replace(',', '.'), NumberStyles.Any, ci, out var nExtract))
            {
                return nExtract;
            }
            return null;
        }

        // imc(pesoName, tallaName) -> peso (kg) / talla (m)^2, un decimal.
        // Heuristica de unidades: si talla > 3 asumimos cm y dividimos por 100;
        // si <= 3 asumimos que ya viene en metros (ej "1.70").
        var mImc = Regex.Match(f, @"^imc\s*\(\s*([A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_]+)\s*\)\s*$",
            RegexOptions.IgnoreCase);
        if (mImc.Success)
        {
            var peso = LeerNum(mImc.Groups[1].Value);
            var talla = LeerNum(mImc.Groups[2].Value);
            if (peso is null || talla is null || peso.Value <= 0 || talla.Value <= 0) { return ""; }
            var tallaM = talla.Value > 3 ? talla.Value / 100.0 : talla.Value;
            if (tallaM <= 0) { return ""; }
            var imc = peso.Value / (tallaM * tallaM);
            return imc.ToString("0.00", ci);
        }

        // imcClass(imcRefName) -> clasificacion OMS sobre el campo IMC calculado.
        var mImcClass = Regex.Match(f, @"^imcClass\s*\(\s*([A-Za-z0-9_]+)\s*\)\s*$", RegexOptions.IgnoreCase);
        if (mImcClass.Success)
        {
            var imcVal = LeerNum(mImcClass.Groups[1].Value);
            if (imcVal is null) { return ""; }
            var v = imcVal.Value;
            if (v < 18.5) { return "Bajo peso"; }
            if (v < 25.0) { return "Normal"; }
            if (v < 30.0) { return "Sobrepeso"; }
            if (v < 35.0) { return "Obesidad grado I"; }
            if (v < 40.0) { return "Obesidad grado II"; }
            return "Obesidad grado III";
        }

        // tensionClass(sisName, diaName) -> clasificacion ESH/ESC 2018.
        // Regla: "la categoria mas alta gana" => evaluamos sis y dia por separado,
        // tomamos el peor de los dos. Sufijo "(sistolica aislada)" cuando sis>=140
        // y dia<90 (hipertension sistolica aislada del adulto mayor).
        var mTen = Regex.Match(f, @"^tensionClass\s*\(\s*([A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_]+)\s*\)\s*$",
            RegexOptions.IgnoreCase);
        if (mTen.Success)
        {
            var sis = LeerNum(mTen.Groups[1].Value);
            var dia = LeerNum(mTen.Groups[2].Value);
            if (sis is null || dia is null) { return ""; }
            var s = sis.Value;
            var d = dia.Value;
            int GradoSis(double x) => x >= 180 ? 5 : x >= 160 ? 4 : x >= 140 ? 3 : x >= 130 ? 2 : x >= 120 ? 1 : 0;
            int GradoDia(double x) => x >= 110 ? 5 : x >= 100 ? 4 : x >=  90 ? 3 : x >=  85 ? 2 : x >=  80 ? 1 : 0;
            var peor = Math.Max(GradoSis(s), GradoDia(d));
            var clase = peor switch
            {
                5 => "HTA grado 3",
                4 => "HTA grado 2",
                3 => "HTA grado 1",
                2 => "Normal alta",
                1 => "Normal",
                _ => "Optima"
            };
            if (s >= 140 && d < 90) { clase += " (sistolica aislada)"; }
            return clase;
        }

        // perimetroRiesgo(perimetroName, sexoName) -> riesgo cardiometabolico
        // segun perimetro abdominal (cm) y sexo. Sin sexo legible -> "".
        var mPer = Regex.Match(f, @"^perimetroRiesgo\s*\(\s*([A-Za-z0-9_]+)\s*,\s*([A-Za-z0-9_]+)\s*\)\s*$",
            RegexOptions.IgnoreCase);
        if (mPer.Success)
        {
            var per = LeerNum(mPer.Groups[1].Value);
            if (per is null) { return ""; }
            if (!values.TryGetValue(mPer.Groups[2].Value, out var sexoVal) || string.IsNullOrWhiteSpace(sexoVal)) { return ""; }
            var sexo = sexoVal.Trim().ToUpperInvariant();
            var p = per.Value;
            var esHombre = sexo == "MASCULINO" || sexo == "HOMBRE" || sexo == "M";
            var esMujer  = sexo == "FEMENINO"  || sexo == "MUJER"  || sexo == "F";
            if (esHombre)
            {
                if (p < 94) { return "Bajo riesgo"; }
                if (p <= 102) { return "Riesgo aumentado"; }
                return "Riesgo alto";
            }
            if (esMujer)
            {
                if (p < 80) { return "Bajo riesgo"; }
                if (p <= 88) { return "Riesgo aumentado"; }
                return "Riesgo alto";
            }
            return "";
        }

        return ""; // formula no reconocida -> dejamos vacio
    }

    /// <summary>Evalua la formula de una celda calculada dentro de una tabla
    /// repetible. Identico a <see cref="Evaluate"/>, pero semanticamente
    /// <paramref name="rowValuesByColumnName"/> es el diccionario
    /// Name-de-columna -> valor de esa celda en la fila actual, de modo que
    /// los identificadores de la formula (peso, talla, ...) resuelven a las
    /// celdas hermanas de la MISMA fila.</summary>
    public static string EvaluateRow(string formula, IReadOnlyDictionary<string, string?> rowValuesByColumnName)
        => Evaluate(formula, rowValuesByColumnName);
}
