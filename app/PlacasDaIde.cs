using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Simulador.App;

/// <summary>
/// As placas do plugin da Arduino IDE (plataforma\boards.txt), tiradas
/// dos mesmos perfis que o simulador usa.
///
/// O boards.txt diz "uma entrada por perfil de PerfilPlaca.cs", mas era
/// escrito à mão -- e na Alpha 18 tinha seis das trinta e quatro placas.
/// Quem escolhia a ESP32-C3 na IDE não a achava na lista. Agora a entrada
/// que falta sai daqui, com a RAM, a flash e o relógio do perfil, e o
/// autoteste reclama se alguma placa ficar sem entrada.
///
/// As seis primeiras continuam como estão: foram acertadas à mão, uma a
/// uma, contra o "Sketch uses" da IDE de verdade.
/// </summary>
public static class PlacasDaIde
{
    /// <summary>A chave da placa no boards.txt: "sim" e o id só com letras e números.</summary>
    public static string Chave(PerfilPlaca p)
        => "sim" + new string([.. p.Id.Where(char.IsLetterOrDigit)]).ToLowerInvariant();

    /// <summary>O chip, como a IDE escreve em build.mcu.</summary>
    public static string Mcu(PerfilPlaca p)
    {
        // A família ESP32 se chama pelo id: o Processador diz o núcleo
        // (Xtensa LX7, RISC-V), e a IDE quer o chip (esp32s3, esp32c3).
        if (p.Id.StartsWith("esp32"))
        {
            string[] partes = p.Id.Split('-');
            return partes.Length > 1 && partes[1].Length == 2 ? "esp32" + partes[1] : "esp32";
        }

        return new string([.. p.Processador.Where(char.IsLetterOrDigit)]).ToLowerInvariant();
    }

    /// <summary>Os ids que já têm entrada no texto do boards.txt.</summary>
    public static HashSet<string> JaListadas(string texto)
        => [.. Regex.Matches(texto, @"(?m)^\w+\.simulador\.peca=(\S+)\s*$")
                    .Select(m => m.Groups[1].Value)];

    /// <summary>As placas do simulador que ainda não aparecem na IDE.</summary>
    public static List<PerfilPlaca> Faltando(string texto)
    {
        var listadas = JaListadas(texto);
        return [.. PerfilPlaca.Conhecidos.Where(p => !listadas.Contains(p.Id))];
    }

    /// <summary>A entrada de uma placa, no formato das que já estão no arquivo.</summary>
    public static string Entrada(PerfilPlaca p)
    {
        string k = Chave(p);
        string mcu = Mcu(p);
        bool tiny = mcu.StartsWith("attiny");

        // Quanto cada byte de código-fonte pesa no programa gravado. São
        // os mesmos fatores das seis entradas feitas à mão, por família;
        // o ARM dos STM32 fica entre o AVR e o RP2040.
        var (fatorFlash, fatorRam) =
            p.De8Bits ? (tiny ? (0.8, 0.04) : (0.9, 0.05))
            : p.Id.StartsWith("esp") ? (6.4, 0.42)
            : mcu == "rp2040" ? (3.0, 0.30)
            : (1.6, 0.10);

        var inv = CultureInfo.InvariantCulture;
        var t = new StringBuilder();
        void L(string linha) => t.Append(linha).Append("\r\n");

        // Sem os parênteses do nome: "STM32F103 (Blue Pill) (CircuitSketch)"
        // lia como dois apostos seguidos no menu da IDE.
        string nomeNaIde = p.Nome.Replace("(", "").Replace(")", "");

        L($"# ---------------- {p.Nome} ----------------");
        L($"{k}.name={nomeNaIde} (CircuitSketch)");
        L($"{k}.build.core=simulador");
        L($"{k}.build.mcu={mcu}");
        L($"{k}.build.f_cpu={(long)Math.Round(p.Megahertz * 1_000_000)}L");
        L($"{k}.build.board=SIMULADOR_{k[3..].ToUpperInvariant()}");
        L($"{k}.upload.tool=enviar");
        L($"{k}.upload.tool.default=enviar");
        L($"{k}.upload.tool.serial=enviar");
        L($"{k}.upload.tool.circuitsketch=enviar");
        L($"{k}.upload.wait_for_upload_port=false");
        L($"{k}.upload.use_1200bps_touch=false");
        L($"{k}.upload.maximum_size={p.FlashBytes}");
        L($"{k}.upload.maximum_data_size={p.RamBytes}");
        L($"{k}.simulador.rotulo={p.Nome}");
        L($"{k}.simulador.peca={p.Id}");
        L($"{k}.simulador.gravador={(p.De8Bits ? "avrdude" : "esptool")}");

        // A assinatura que o avrdude "lê" do chip -- a mesma da placa real.
        string assinatura = mcu switch
        {
            "attiny85" => "0x1e930b (probably t85)",
            "atmega328p" => "0x1e950f (probably m328p)",
            "atmega2560" => "0x1e9801 (probably m2560)",
            _ => null,
        };
        if (assinatura is not null) L($"{k}.simulador.assinatura={assinatura}");

        L($"{k}.simulador.piso_flash={p.BaseDaFlash}");
        L($"{k}.simulador.piso_ram={p.BaseDaRam}");
        L($"{k}.simulador.fator_flash={fatorFlash.ToString(inv)}");
        L($"{k}.simulador.fator_ram={fatorRam.ToString(inv)}");

        return t.ToString();
    }

    /// <summary>
    /// Acrescenta ao boards.txt as placas que faltam, sem mexer nas que
    /// já estão. Devolve quantas entraram.
    /// </summary>
    public static int Completar(string caminho)
    {
        string texto = File.ReadAllText(caminho);
        var faltando = Faltando(texto);
        if (faltando.Count == 0) return 0;

        const string Marca = "#  Daqui para baixo: geradas de PerfilPlaca.cs";

        var t = new StringBuilder(texto.TrimEnd('\r', '\n'));
        t.Append("\r\n");
        if (!texto.Contains(Marca))
        {
            t.Append("\r\n# ============================================================\r\n");
            t.Append(Marca).Append("\r\n");
            t.Append("#  (CircuitSketch.exe --placas-da-ide plataforma\\boards.txt)\r\n");
            t.Append("# ============================================================\r\n");
        }

        // Na ordem das famílias da paleta: o menu da IDE lista as placas
        // na ordem do arquivo, e assim cada família fica junta.
        var ordem = Catalogo.Familias().ToList();
        int Posicao(PerfilPlaca p)
            => ordem.IndexOf(Catalogo.Familia(p.Id) ?? "Outras") is int i && i >= 0 ? i : int.MaxValue;

        foreach (var p in faltando.OrderBy(Posicao)) t.Append("\r\n").Append(Entrada(p));

        // Sem BOM: a IDE lê o boards.txt como texto puro.
        File.WriteAllText(caminho, t.ToString(), new UTF8Encoding(false));
        return faltando.Count;
    }
}
