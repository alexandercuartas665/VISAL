using Visal.Application.Common;
using Xunit;

namespace Visal.Application.Tests;

public class TelefonoPrincipalTests
{
    [Theory]
    [InlineData("3156896006-3113116200", "3156896006")]
    [InlineData("3118359029 y 3135158931", "3118359029")]
    [InlineData("3113116200; 3163154563", "3113116200")]
    [InlineData("3015620429-316181650-3174276204", "3015620429")]
    [InlineData("3156198041 - 3204954243-3134612097 - 3182349353", "3156198041")]
    [InlineData("304-472-05-39", "3044720539")]
    [InlineData("314 7183076; 301 4317920", "3147183076")]
    [InlineData("6023410667 3205674040", "6023410667")]
    [InlineData("3243186676, 3246209166", "3243186676")]
    [InlineData("3226560287.", "3226560287")]
    [InlineData("3184391893 PACIENTE", "3184391893")]
    [InlineData("2710102 Ext 503 WhatsApp: 57 321 2049545", "2710102")]
    [InlineData("2710102 - 3204954243", "2710102")]
    [InlineData("57 321 2049545", "3212049545")]
    [InlineData("317 2721260", "3172721260")]
    [InlineData("3117885632", "3117885632")]
    [InlineData("(2)-3108460384", "3108460384")]
    [InlineData("226338858; 3156761016", "3156761016")]
    [InlineData("315055665- 3107026553", "3107026553")]
    [InlineData("314 6758725 - 3114987632", "3146758725")]
    [InlineData("CALLE 5E#47-57 APARTAMENTO 401 EDIFICIO X   3167406098 - 3183244445 JOSE", "3167406098")]
    [InlineData("310394926", "310394926")]
    [InlineData("0", "0")]
    [InlineData("NO TIENE", "NO TIENE")]
    [InlineData("  SD ", "SD")]
    public void Extrae_un_solo_telefono(string entrada, string esperado)
        => Assert.Equal(esperado, TelefonoPrincipal.Primero(entrada));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Vacio_se_respeta(string? entrada)
        => Assert.Equal(entrada, TelefonoPrincipal.Primero(entrada));
}
