using System.IO;
using System.Text;
using System.Text.Json;

namespace Simulador.App;

/// <summary>Um pino declarado no .chip.json.</summary>
public sealed record PinoDoChip(string Nome, string Lado);

/// <summary>
/// Um chip feito pela pessoa: a descricao dos pinos num .chip.json e o
/// comportamento num .chip.ino.
///
/// O jeito do Wokwi -- C compilado para WebAssembly -- nao cabe aqui: o
/// programa e offline e ninguem na turma vai instalar um toolchain para
/// fazer um contador. Mas o simulador JA tem um interpretador da
/// linguagem do sketch, entao o comportamento do chip e escrito na
/// MESMA linguagem que o aluno ja escreve, com o mesmo realce e os
/// mesmos erros com numero de linha.
///
/// Os pinos sao endereçados por NOME, e nao por numero de GPIO: num chip
/// proprio quem nomeia os pinos e quem fez o chip.
/// </summary>
public sealed class ChipCustom
{
    public string Id { get; init; }
    public string Nome { get; init; }
    public string Autor { get; init; }
    public double Largura { get; init; }
    public double Altura { get; init; }
    public IReadOnlyList<PinoDoChip> Pinos { get; init; } = [];
    public string Codigo { get; set; }

    /// <summary>De onde veio: o projeto aberto ou a pasta do computador.</summary>
    public bool DoProjeto { get; init; }

    public const string Prefixo = "chip-";

    public static string PastaDosChips => Path.Combine(Armazem.Pasta, "chips");

    // =====================================================================
    //  Leitura
    // =====================================================================

    /// <summary>
    /// Le um chip do par de textos. Devolve null quando o json nao tem o
    /// minimo -- nome e pinos -- em vez de montar uma peca quebrada.
    /// </summary>
    public static ChipCustom Ler(string json, string codigo, bool doProjeto)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;

            string nome = Texto(raiz, "nome") ?? Texto(raiz, "name");
            if (string.IsNullOrWhiteSpace(nome)) return null;

            var pinos = new List<PinoDoChip>();
            if (raiz.TryGetProperty("pinos", out var lista)
                || raiz.TryGetProperty("pins", out lista))
            {
                foreach (var p in lista.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.String)
                    {
                        pinos.Add(new PinoDoChip(p.GetString(), "esquerda"));
                        continue;
                    }

                    string nomeDoPino = Texto(p, "nome") ?? Texto(p, "name");
                    if (string.IsNullOrWhiteSpace(nomeDoPino)) continue;

                    pinos.Add(new PinoDoChip(nomeDoPino, Texto(p, "lado") ?? "esquerda"));
                }
            }

            if (pinos.Count == 0) return null;

            // Sem lado declarado, metade de cada lado: e como um DIP.
            if (pinos.All(p => p.Lado == "esquerda") && pinos.Count > 3)
            {
                int metade = (pinos.Count + 1) / 2;
                pinos = [.. pinos.Select((p, i) =>
                    i < metade ? p : p with { Lado = "direita" })];
            }

            return new ChipCustom
            {
                Id = Identificador(nome),
                Nome = nome,
                Autor = Texto(raiz, "autor") ?? Texto(raiz, "author") ?? "",
                Largura = Numero(raiz, "largura") ?? Numero(raiz, "width") ?? 0,
                Altura = Numero(raiz, "altura") ?? Numero(raiz, "height") ?? 0,
                Pinos = pinos,
                Codigo = codigo ?? "",
                DoProjeto = doProjeto,
            };
        }
        catch (JsonException) { return null; }
    }

    static string Texto(JsonElement e, string nome)
        => e.ValueKind == JsonValueKind.Object
           && e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    static double? Numero(JsonElement e, string nome)
        => e.ValueKind == JsonValueKind.Object
           && e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : null;

    /// <summary>"Contador BCD" -> "chip-contador-bcd".</summary>
    public static string Identificador(string nome)
    {
        var limpo = new StringBuilder(Prefixo);
        bool traco = false;

        foreach (char c in (nome ?? "").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                limpo.Append(c);
                traco = false;
            }
            else if (!traco && limpo.Length > Prefixo.Length)
            {
                limpo.Append('-');
                traco = true;
            }
        }

        return limpo.ToString().TrimEnd('-');
    }

    // =====================================================================
    //  Medidas e desenho
    // =====================================================================

    /// <summary>Passo entre pinos, em pixels de 0,1".</summary>
    public const double Passo = 9.6;

    public int NaEsquerda => Pinos.Count(p => p.Lado != "direita"
                                              && p.Lado != "cima" && p.Lado != "baixo");
    public int NaDireita => Pinos.Count(p => p.Lado == "direita");

    public double LarguraFinal => Largura > 0 ? Largura : 108;

    public double AlturaFinal
        => Altura > 0 ? Altura : Math.Max(48, (Math.Max(NaEsquerda, NaDireita) + 1) * Passo);

    /// <summary>Onde cada pino cai na caixa da peca.</summary>
    public List<PinoDef> PinosNaCaixa()
    {
        var saida = new List<PinoDef>();
        double alt = AlturaFinal, larg = LarguraFinal;

        int esquerda = 0, direita = 0;
        double sobraE = (alt - (NaEsquerda - 1) * Passo) / 2;
        double sobraD = (alt - (NaDireita - 1) * Passo) / 2;

        foreach (var pino in Pinos)
        {
            bool naDireita = pino.Lado == "direita";
            double y = naDireita
                ? sobraD + direita++ * Passo
                : sobraE + esquerda++ * Passo;

            saida.Add(new PinoDef(pino.Nome, naDireita ? larg : 0, y,
                SinaisDe(pino.Nome), ""));
        }

        return saida;
    }

    static string[] SinaisDe(string nome)
        => nome is "VCC" or "VDD" or "GND" or "VSS" or "5V" or "3V3" ? ["power"] : [];

    /// <summary>
    /// O desenho: um DIP preto com os nomes dos pinos por dentro, que e
    /// o que o Wokwi faz quando o chip nao traz SVG proprio.
    /// </summary>
    public string Svg()
    {
        var cultura = System.Globalization.CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("0.##", cultura);

        double larg = LarguraFinal, alt = AlturaFinal;
        var corpo = new StringBuilder();

        corpo.Append($"""
            <svg xmlns="http://www.w3.org/2000/svg" version="1.1"
                 width="{N(larg)}" height="{N(alt)}" viewBox="0 0 {N(larg)} {N(alt)}"
                 font-family="monospace">
              <rect x="6" y="2" width="{N(larg - 12)}" height="{N(alt - 4)}" rx="3"
                    fill="#22252a" stroke="#101216" stroke-width="1"/>
              <rect x="8" y="4" width="{N(larg - 16)}" height="{N(alt - 8)}" rx="2"
                    fill="none" stroke="#31353c" stroke-width="0.8"/>
              <circle cx="14" cy="11" r="3" fill="#15171b"/>

            """);

        foreach (var pino in PinosNaCaixa())
        {
            bool naDireita = pino.X > larg / 2;
            double x = naDireita ? larg - 6 : 0;

            corpo.Append($"""
                  <rect x="{N(x)}" y="{N(pino.Y - 1.6)}" width="6" height="3.2" rx="0.6"
                        fill="#c9ccd1"/>

                """);

            corpo.Append($"""
                  <text x="{N(naDireita ? larg - 11 : 11)}" y="{N(pino.Y + 2.4)}"
                        fill="#9aa3ad" font-size="6px"
                        text-anchor="{(naDireita ? "end" : "start")}">{Escapar(pino.Nome)}</text>

                """);
        }

        corpo.Append($"""
              <text x="{N(larg / 2)}" y="{N(alt - 6)}" fill="#6f7681" font-size="6px"
                    text-anchor="middle">{Escapar(Nome)}</text>
            </svg>

            """);

        return corpo.ToString();
    }

    static string Escapar(string texto)
        => (texto ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // =====================================================================
    //  Modelo de arquivo
    // =====================================================================

    /// <summary>O .chip.json de exemplo, para quem esta comecando um chip.</summary>
    public static string ModeloJson(string nome) => $$"""
        {
          "nome": "{{nome}}",
          "autor": "",
          "pinos": [
            { "nome": "CLK", "lado": "esquerda" },
            { "nome": "RST", "lado": "esquerda" },
            { "nome": "OUT", "lado": "direita" },
            { "nome": "VCC", "lado": "direita" }
          ]
        }
        """;

    /// <summary>O .chip.ino de exemplo: um divisor por dois, comentado.</summary>
    public const string ModeloCodigo = """
        // O comportamento do chip, na mesma linguagem do sketch.
        //
        // chipInit() roda uma vez, quando a simulacao comeca.
        // chipLoop() roda a cada quadro, como o loop() da placa.
        //
        // Os pinos sao pelo NOME que esta no .chip.json:
        //   modoDoPino("OUT", OUTPUT);
        //   lerPino("CLK")        devolve 0 ou 1
        //   escreverPino("OUT", 1)
        //   lerAnalogico("CLK")   devolve 0..4095
        //   aoMudarPino("CLK", RISING, nomeDaFuncao)

        int conta = 0;

        void chipInit() {
          modoDoPino("CLK", INPUT);
          modoDoPino("RST", INPUT);
          modoDoPino("OUT", OUTPUT);
          aoMudarPino("CLK", RISING, contar);
        }

        void contar() {
          if (lerPino("RST")) { conta = 0; }
          else { conta = conta + 1; }
          escreverPino("OUT", conta % 2);
        }

        void chipLoop() {
        }
        """;
}
