namespace Simulador;

/// <summary>
/// Os .h das bibliotecas que o simulador executa por dentro, so com o que
/// eles DEFINEM: as cores e os comandos do display.
///
/// O "#include &lt;Adafruit_ILI9341.h&gt;" nao carregava arquivo nenhum -- a
/// biblioteca e do simulador --, e com isso o ILI9341_BLACK do exemplo
/// da propria Adafruit dava "variavel nao declarada" na primeira linha.
/// Os valores sao os dos cabecalhos de verdade, e so existem depois do
/// #include, como no compilador: um sketch que esquece a linha recebe o
/// mesmo erro que receberia na IDE.
/// </summary>
public static class Cabecalhos
{
    const string Ili9341 = """
        #pragma once
        #define ILI9341_TFTWIDTH 240
        #define ILI9341_TFTHEIGHT 320
        #define ILI9341_NOP 0x00
        #define ILI9341_SWRESET 0x01
        #define ILI9341_RDDID 0x04
        #define ILI9341_RDDST 0x09
        #define ILI9341_SLPIN 0x10
        #define ILI9341_SLPOUT 0x11
        #define ILI9341_PTLON 0x12
        #define ILI9341_NORON 0x13
        #define ILI9341_RDMODE 0x0A
        #define ILI9341_RDMADCTL 0x0B
        #define ILI9341_RDPIXFMT 0x0C
        #define ILI9341_RDIMGFMT 0x0D
        #define ILI9341_RDSELFDIAG 0x0F
        #define ILI9341_INVOFF 0x20
        #define ILI9341_INVON 0x21
        #define ILI9341_GAMMASET 0x26
        #define ILI9341_DISPOFF 0x28
        #define ILI9341_DISPON 0x29
        #define ILI9341_CASET 0x2A
        #define ILI9341_PASET 0x2B
        #define ILI9341_RAMWR 0x2C
        #define ILI9341_RAMRD 0x2E
        #define ILI9341_PTLAR 0x30
        #define ILI9341_VSCRDEF 0x33
        #define ILI9341_MADCTL 0x36
        #define ILI9341_VSCRSADD 0x37
        #define ILI9341_PIXFMT 0x3A
        #define ILI9341_FRMCTR1 0xB1
        #define ILI9341_FRMCTR2 0xB2
        #define ILI9341_FRMCTR3 0xB3
        #define ILI9341_INVCTR 0xB4
        #define ILI9341_DFUNCTR 0xB6
        #define ILI9341_PWCTR1 0xC0
        #define ILI9341_PWCTR2 0xC1
        #define ILI9341_VMCTR1 0xC5
        #define ILI9341_VMCTR2 0xC7
        #define ILI9341_RDID1 0xDA
        #define ILI9341_RDID2 0xDB
        #define ILI9341_RDID3 0xDC
        #define ILI9341_GMCTRP1 0xE0
        #define ILI9341_GMCTRN1 0xE1
        #define ILI9341_BLACK 0x0000
        #define ILI9341_NAVY 0x000F
        #define ILI9341_DARKGREEN 0x03E0
        #define ILI9341_DARKCYAN 0x03EF
        #define ILI9341_MAROON 0x7800
        #define ILI9341_PURPLE 0x780F
        #define ILI9341_OLIVE 0x7BE0
        #define ILI9341_LIGHTGREY 0xC618
        #define ILI9341_DARKGREY 0x7BEF
        #define ILI9341_BLUE 0x001F
        #define ILI9341_GREEN 0x07E0
        #define ILI9341_CYAN 0x07FF
        #define ILI9341_RED 0xF800
        #define ILI9341_MAGENTA 0xF81F
        #define ILI9341_YELLOW 0xFFE0
        #define ILI9341_WHITE 0xFFFF
        #define ILI9341_ORANGE 0xFD20
        #define ILI9341_GREENYELLOW 0xAFE5
        #define ILI9341_PINK 0xFC18
        """;

    const string St77xx = """
        #pragma once
        #define ST77XX_NOP 0x00
        #define ST77XX_SWRESET 0x01
        #define ST77XX_RDDID 0x04
        #define ST77XX_RDDST 0x09
        #define ST77XX_SLPIN 0x10
        #define ST77XX_SLPOUT 0x11
        #define ST77XX_PTLON 0x12
        #define ST77XX_NORON 0x13
        #define ST77XX_INVOFF 0x20
        #define ST77XX_INVON 0x21
        #define ST77XX_DISPOFF 0x28
        #define ST77XX_DISPON 0x29
        #define ST77XX_CASET 0x2A
        #define ST77XX_RASET 0x2B
        #define ST77XX_RAMWR 0x2C
        #define ST77XX_RAMRD 0x2E
        #define ST77XX_PTLAR 0x30
        #define ST77XX_TEOFF 0x34
        #define ST77XX_TEON 0x35
        #define ST77XX_MADCTL 0x36
        #define ST77XX_COLMOD 0x3A
        #define ST77XX_BLACK 0x0000
        #define ST77XX_WHITE 0xFFFF
        #define ST77XX_RED 0xF800
        #define ST77XX_GREEN 0x07E0
        #define ST77XX_BLUE 0x001F
        #define ST77XX_CYAN 0x07FF
        #define ST77XX_MAGENTA 0xF81F
        #define ST77XX_YELLOW 0xFFE0
        #define ST77XX_ORANGE 0xFC00
        """;

    /// <summary>
    /// O avr/wdt.h da avr-libc: os prazos do watchdog. So existe no AVR -- nas
    /// outras placas o #include nem acha o arquivo, e aqui tambem nao passa.
    /// </summary>
    static readonly string WdtDoAvr = $$"""
        #pragma once
        #ifndef __AVR__
        #error "{{Idioma.T("avr/wdt.h so existe nas placas AVR (Uno, Nano, Mega, ATtiny): nesta placa o compilador da 'No such file or directory'.")}}"
        #endif
        #define WDTO_15MS 0
        #define WDTO_30MS 1
        #define WDTO_60MS 2
        #define WDTO_120MS 3
        #define WDTO_250MS 4
        #define WDTO_500MS 5
        #define WDTO_1S 6
        #define WDTO_2S 7
        #define WDTO_4S 8
        #define WDTO_8S 9
        """;

    static readonly Dictionary<string, string> textos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Adafruit_ILI9341.h"] = Ili9341,
        ["Adafruit_ST77xx.h"] = St77xx,
        ["Adafruit_ST7789.h"] = "#pragma once\n#include <Adafruit_ST77xx.h>\n",
        ["Adafruit_ST7735.h"] = "#pragma once\n#include <Adafruit_ST77xx.h>\n",
        ["wdt.h"] = WdtDoAvr,
    };

    /// <summary>O .h do simulador com esse nome, ou null.</summary>
    public static ArquivoDeCodigo DoSimulador(string nome)
    {
        if (nome is null || !textos.TryGetValue(nome.Replace('\\', '/').Split('/')[^1], out var texto)) return null;
        string limpo = nome.Replace('\\', '/').Split('/')[^1];
        return new ArquivoDeCodigo(Origens.Registrar("simulador|" + limpo, limpo), limpo, texto);
    }
}
