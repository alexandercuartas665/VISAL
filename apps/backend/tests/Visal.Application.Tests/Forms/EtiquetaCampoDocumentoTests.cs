using Visal.Application.Tenancy.Forms;
using Xunit;

namespace Visal.Application.Tests.Forms;

/// <summary>
/// El campo del documento del paciente tiene clave fija "cc" y etiqueta estatica "CC" en el
/// esquema. La etiqueta se debe resolver en render con el tipo real (RC/TI/CE/...), sin tocar
/// la clave ni los valores ya guardados. Fallback: la etiqueta del esquema.
/// </summary>
public sealed class EtiquetaCampoDocumentoTests
{
    [Theory]
    [InlineData("RC")]
    [InlineData("TI")]
    [InlineData("CE")]
    [InlineData("PT")]
    public void Campo_cc_usa_el_tipo_real_del_paciente(string tipo)
    {
        Assert.Equal(tipo, EtiquetaCampoDocumento.Resolver("cc", "CC", tipo));
    }

    [Fact]
    public void Campo_cc_con_tipo_desconocido_cae_a_la_etiqueta_del_esquema()
    {
        Assert.Equal("CC", EtiquetaCampoDocumento.Resolver("cc", "CC", null));
        Assert.Equal("CC", EtiquetaCampoDocumento.Resolver("cc", "CC", ""));
        Assert.Equal("CC", EtiquetaCampoDocumento.Resolver("cc", "CC", "   "));
    }

    [Fact]
    public void Clave_cc_es_insensible_a_mayusculas_y_recorta_el_tipo()
    {
        Assert.Equal("RC", EtiquetaCampoDocumento.Resolver("CC", "CC", "RC"));
        Assert.Equal("TI", EtiquetaCampoDocumento.Resolver("cc", "CC", "  TI  "));
    }

    [Theory]
    [InlineData("nombres_y_apellidos", "Nombres y Apellidos")]
    [InlineData("edad", "Edad")]
    [InlineData("fecha_nacimiento", "Fecha de Nacimiento")]
    public void Otros_campos_conservan_su_etiqueta(string nombre, string etiqueta)
    {
        // Aunque se conozca el tipo de documento, solo el campo "cc" cambia de etiqueta.
        Assert.Equal(etiqueta, EtiquetaCampoDocumento.Resolver(nombre, etiqueta, "RC"));
    }

    [Fact]
    public void Etiqueta_nula_del_esquema_devuelve_vacio()
    {
        Assert.Equal("", EtiquetaCampoDocumento.Resolver("edad", null, "RC"));
    }
}
