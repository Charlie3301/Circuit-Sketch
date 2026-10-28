using System.IO;
using System.Text.Json;

namespace Simulador.App;

/// <summary>
/// A varredura do C++ nativo: cada placa de cada demo e de cada projeto de
/// teste, com o que o core nativo ja cobre, tem que compilar no clang. O que
/// ainda fica com o interpretador (Servo, WiFi, LCD... -- as bibliotecas que
/// o simulador imita) aparece contado pelo motivo.
/// </summary>
public static partial class Autoteste
{
    static void Cpp_NativoVarredura()
    {
        saida.AppendLine("C++ nativo: varredura das demos e dos projetos");
        if (!CompiladorNativo.Disponivel)
        {
            Conferir("varredura: o compilador nativo esta instalado", false);
            return;
        }

        var arquivos = new List<string>();
        string demos = Path.Combine(AppContext.BaseDirectory, "assets", "demos");
        if (Directory.Exists(demos)) arquivos.AddRange(Directory.GetFiles(demos, "*.sktch"));
        if (AcharOsProjetos() is { } projetos) arquivos.AddRange(Directory.GetFiles(projetos, "*.sktch"));

        int total = 0, cobertos = 0;
        var motivos = new Dictionary<string, int>();
        var falhas = new List<string>();
        foreach (string arquivo in arquivos.Order(StringComparer.Ordinal))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllText(arquivo)); }
            catch { continue; }
            using var _ = doc;
            var raiz = doc.RootElement;
            string geral = raiz.TryGetProperty("codigo", out var c) && c.ValueKind == JsonValueKind.Array
                ? string.Join("\n", c.EnumerateArray().Select(l => l.GetString())) : null;
            if (!raiz.TryGetProperty("circuito", out var circ) || !circ.TryGetProperty("Pecas", out var pecas)) continue;
            foreach (var peca in pecas.EnumerateArray())
            {
                string tipo = peca.TryGetProperty("Tipo", out var t) ? t.GetString() : null;
                if (tipo is null || !PerfilPlaca.EhPlaca(tipo)) continue;
                string codigo = peca.TryGetProperty("Codigo", out var pc) && pc.ValueKind == JsonValueKind.Array
                    ? string.Join("\n", pc.EnumerateArray().Select(l => l.GetString())) : geral;
                if (string.IsNullOrWhiteSpace(codigo) || Compilador.Detectar(codigo) != Linguagem.Arduino) continue;
                total++;
                var projeto = new Projeto { Sketch = codigo, Placa = tipo, DoSimulador = Acervo.EhDoSimulador };
                string porque = CompiladorNativo.PorQueNao(projeto);
                if (porque is not null)
                {
                    motivos[porque] = motivos.GetValueOrDefault(porque) + 1;
                    continue;
                }
                cobertos++;
                try { CompiladorNativo.Compilar(projeto); }
                catch (Exception e) when (e is ErroDeSintaxe or ErroDeExecucao)
                {
                    falhas.Add($"{Path.GetFileNameWithoutExtension(arquivo)} ({tipo}): {e.Message}");
                }
            }
        }

        saida.AppendLine($"        {total} sketches em C++; {cobertos} so com o que o core nativo ja tem; "
                         + $"{cobertos - falhas.Count} compilaram");
        foreach (var (motivo, n) in motivos.OrderByDescending(m => m.Value).Take(15))
            saida.AppendLine($"        ainda no interpretador ({n}): {motivo}");
        foreach (string f in falhas.Take(20)) saida.AppendLine("        nao compilou: " + f);
        Conferir($"varredura: todo sketch que o core nativo cobre compila no clang ({cobertos - falhas.Count}/{cobertos})",
                 falhas.Count == 0, falhas.FirstOrDefault() ?? "");
    }
}
