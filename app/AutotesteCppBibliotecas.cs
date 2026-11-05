using System.IO;
using System.Text.RegularExpressions;

namespace Simulador.App;

public static partial class Autoteste
{
    /// <summary>
    /// A varredura das bibliotecas de verdade: compila os exemplos de cada biblioteca da pasta
    /// CS_LIBS (uma subpasta por biblioteca, como a IDE) e conta o que nao compilou, com o
    /// primeiro erro de cada exemplo. So compila e liga, nao roda nada. Sem CS_LIBS fica de fora:
    /// as bibliotecas nao vem com o simulador. CS_LIBS_SAIDA recebe o relatorio linha a linha;
    /// CS_LIBS_SO="a,b" e CS_LIBS_PLACA escolhem as bibliotecas e a placa.
    /// </summary>
    static void Cpp_Bibliotecas()
    {
        saida.AppendLine("C++ compilado: a varredura das bibliotecas");
        string raiz = Environment.GetEnvironmentVariable("CS_LIBS");
        if (string.IsNullOrEmpty(raiz) || !Directory.Exists(raiz))
        {
            saida.AppendLine("        (sem CS_LIBS: fica de fora)");
            return;
        }
        string relatorio = Environment.GetEnvironmentVariable("CS_LIBS_SAIDA");
        string placa = Environment.GetEnvironmentVariable("CS_LIBS_PLACA") ?? "esp32-devkit-v1";
        string[] so = Environment.GetEnvironmentVariable("CS_LIBS_SO")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        void Linha(string t)
        {
            saida.AppendLine(t);
            if (relatorio is not null) File.AppendAllText(relatorio, t + Environment.NewLine);
        }

        var todas = Directory.GetDirectories(raiz).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Select(Acervo.Carregar).ToList();
        int exemplos = 0, compilaram = 0;
        foreach (var lib in todas)
        {
            if (so is { Length: > 0 } && !so.Any(s => lib.Nome.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            string pastaDeExemplos = Path.Combine(lib.Pasta, "examples");
            var inos = Directory.Exists(pastaDeExemplos)
                ? Directory.GetFiles(pastaDeExemplos, "*.ino", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList() : [];
            // Ate 3, espalhados pela lista: costumam mostrar partes diferentes da biblioteca.
            // CS_LIBS_TODOS=1: todos (para fechar uma biblioteca so).
            var escolhidos = inos.Count <= 3 || Environment.GetEnvironmentVariable("CS_LIBS_TODOS") == "1"
                ? inos : [inos[0], inos[inos.Count / 2], inos[^1]];
            if (escolhidos.Count == 0)
            {
                Linha($"{lib.Nome}: SEM EXEMPLO");
                continue;
            }
            int bons = 0;
            foreach (string ino in escolhidos)
            {
                exemplos++;
                string exemplo = Path.GetFileNameWithoutExtension(ino);
                var abas = Directory.GetFiles(Path.GetDirectoryName(ino)!)
                    .Where(f => !string.Equals(f, ino, StringComparison.OrdinalIgnoreCase) && Path.GetExtension(f).ToLowerInvariant() is ".h" or ".hpp" or ".cpp" or ".c" or ".ino")
                    .ToDictionary(f => Path.GetFileName(f), File.ReadAllText, StringComparer.OrdinalIgnoreCase);
                var projeto = new Projeto { Sketch = File.ReadAllText(ino), Placa = placa, Arquivos = abas, Bibliotecas = todas, DoSimulador = Acervo.EhDoSimulador };
                var relogio = System.Diagnostics.Stopwatch.StartNew();
                CompiladorNativo.ErroComAsInstaladas = null;
                try
                {
                    CompiladorNativo.Compilar(projeto);
                    compilaram++;
                    bons++;
                    Linha($"{lib.Nome}/{exemplo}: OK {relogio.ElapsedMilliseconds / 1000} s");
                }
                catch (Exception e)
                {
                    string motivo = Regex.Replace(e is ErroDeSintaxe sintaxe ? CompiladorNativo.ErroLegivel(sintaxe).Message : e.Message, @"\s+", " ");
                    if (CompiladorNativo.ErroComAsInstaladas is { } segundo)
                        motivo += " || COM AS INSTALADAS: " + Regex.Replace(CompiladorNativo.ErroLegivel(segundo).Message, @"\s+", " ");
                    Linha($"{lib.Nome}/{exemplo}: ERRO {motivo[..Math.Min(700, motivo.Length)]}");
                }
            }
        }
        Linha($"TOTAL: {compilaram} de {exemplos} exemplos compilaram");

        // A FastLED, se estiver no acervo, tambem roda: o driver dela (o RMT do IDF) acende a fita.
        if (todas.FirstOrDefault(l => l.Nome == "FastLED") is not null && (so is not { Length: > 0 } || so.Any(s => "FastLED".Contains(s, StringComparison.OrdinalIgnoreCase))))
        {
            const string fastled = """
                #include <FastLED.h>
                CRGB leds[4];
                void setup() {
                  Serial.begin(115200);
                  FastLED.addLeds<WS2812B, 5, GRB>(leds, 4);
                  leds[1] = CRGB(10, 20, 30);
                  FastLED.show();
                  Serial.println("mostrou");
                }
                void loop() { delay(50); }
                """;
            var (serial, erro, nativo, m) = RodarNativo(fastled, 3000, bibliotecas: todas);
            int cor = m.Fitas.TryGetValue(5, out var fita) ? fita.Cores[1] : -1;
            Conferir("bibliotecas: a FastLED roda -- o addLeds com a WS2812B acende o LED 1 da fita no pino 5",
                nativo && erro is null && serial.Contains("mostrou") && cor == Fita.Montar(10, 20, 30), $"{nativo} {erro} | {serial} | cor {cor:X6}");
        }
    }
}
