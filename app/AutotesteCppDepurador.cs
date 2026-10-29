using System.Collections.Concurrent;

namespace Simulador.App;

/// <summary>
/// O depurador no C/C++ compilado: a pausa marcada na linha, o passo, as
/// variaveis (locais e globais, cada uma pelo tipo), o Pausar no meio de um
/// delay, a tarefa do FreeRTOS, e o Parar com o sketch pausado.
/// </summary>
public static partial class Autoteste
{
    static void Cpp_Depurador()
    {
        saida.AppendLine("C++ compilado: o depurador (pausa, passo, variaveis)");
        if (!CompiladorNativo.Disponivel)
        {
            Conferir("depurador: o compilador (nativo/zig) esta instalado", false, "faltou nativo/zig/zig.exe");
            return;
        }

        // ---- a pausa marcada, o passo, e as variaveis pelo tipo ----
        const string sketch = """
            int contador = 0;
            float temperatura = 21.5;
            char nome[] = "placa";
            String texto = "oi";
            struct Ponto { int x; int y; };
            Ponto p = {3, 4};
            int leituras[3] = {7, 8, 9};
            void setup() { Serial.begin(115200); }
            void loop() {
              int dobro = contador * 2;
              contador++;
              delay(5);
            }
            """;
        var retratos = new BlockingCollection<Depurador.Retrato>();
        Depurador dep = null;
        var vistos = new List<Depurador.Retrato>();
        int etapa = 0;
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        var (serial, erro, nativo, _) = RodarNativo(sketch, 4000, antes: (interp, maquina) =>
        {
            dep = new Depurador(maquina);
            dep.DefinirPausas([11]);
            dep.Pausou += r => retratos.Add(r);
            interp.Depurador = dep;
        }, durante: maq =>
        {
            if (!retratos.TryTake(out var r)) return;
            vistos.Add(r);
            switch (etapa++)
            {
                case 0: dep.Passo(); break;                                  // na 11: anda uma linha
                case 1: dep.Continuar(); break;                              // na 12: volta a marcada
                case 2: dep.DefinirPausas([]); dep.Continuar(); relogio.Restart(); break;
            }
        });
        string V(Depurador.Retrato r, string nome) => r?.Variaveis.FirstOrDefault(v => v.Nome == nome)?.Valor;
        var primeiro = vistos.ElementAtOrDefault(0);
        var passo = vistos.ElementAtOrDefault(1);
        var volta = vistos.ElementAtOrDefault(2);
        string Mostrar(Depurador.Retrato r) => r is null ? "-" : $"{r.Linha}: " + string.Join(", ", r.Variaveis.Select(v => $"{v.Nome}={v.Valor}"));
        Conferir("depurador: a pausa marcada para na linha certa, com a local e as globais do sketch",
            nativo && erro is null && primeiro?.Linha == 11 && V(primeiro, "dobro") == "0" && V(primeiro, "contador") == "0",
            $"{nativo} {erro} | {Mostrar(primeiro)}");
        Conferir("depurador: cada variavel pelo tipo -- float, vetor de char, String, struct e vetor de int",
            V(primeiro, "temperatura") == "21.5" && V(primeiro, "nome") == "\"placa\"" && V(primeiro, "texto") == "\"oi\""
            && V(primeiro, "p") == "{x: 3, y: 4}" && V(primeiro, "leituras") == "{7, 8, 9}",
            Mostrar(primeiro));
        Conferir("depurador: o passo vai para a linha seguinte (o contador ja somou)",
            passo?.Linha == 12 && V(passo, "contador") == "1", Mostrar(passo));
        Conferir("depurador: continuar roda o laco e para de novo na marcada, com os valores novos",
            volta?.Linha == 11 && V(volta, "contador") == "1" && V(volta, "dobro") == "2", Mostrar(volta));
        Conferir("depurador: sem pausas marcadas o sketch roda solto (nao para mais)",
            vistos.Count == 3 && etapa == 3, $"{vistos.Count} paradas");

        // ---- o Pausar no meio de um delay longo, e o Parar com ele pausado ----
        const string longo = """
            int voltas = 0;
            void setup() { Serial.begin(115200); }
            void loop() {
              voltas++;
              delay(3000);
            }
            """;
        retratos = new BlockingCollection<Depurador.Retrato>();
        Depurador.Retrato pausado = null;
        bool pediu = false;
        relogio.Restart();
        (serial, erro, nativo, _) = RodarNativo(longo, 1500, antes: (interp, maquina) =>
        {
            dep = new Depurador(maquina);
            dep.Pausou += r => retratos.Add(r);
            interp.Depurador = dep;
        }, durante: maq =>
        {
            if (!pediu && relogio.ElapsedMilliseconds > 400) { pediu = true; dep.Pausar(); }
            if (retratos.TryTake(out var r)) pausado = r;
        });
        Conferir("depurador: o Pausar no meio do delay(3000) para ja, na linha do delay, e o Parar com o sketch pausado encerra a placa",
            nativo && erro is null && pausado?.Linha == 5 && V(pausado, "voltas") == "1", $"{nativo} {erro} | {Mostrar(pausado)}");

        // ---- a pausa numa tarefa do FreeRTOS ----
        const string tarefa = """
            volatile int giros = 0;
            void piscar(void *arg) {
              for (;;) {
                giros++;
                vTaskDelay(10);
              }
            }
            void setup() { xTaskCreate(piscar, "piscar", 4096, NULL, 1, NULL); }
            void loop() { delay(20); }
            """;
        retratos = new BlockingCollection<Depurador.Retrato>();
        pausado = null;
        (serial, erro, nativo, _) = RodarNativo(tarefa, 1200, antes: (interp, maquina) =>
        {
            dep = new Depurador(maquina);
            dep.DefinirPausas([4]);
            dep.Pausou += r => retratos.Add(r);
            interp.Depurador = dep;
        }, durante: maq =>
        {
            if (pausado is null && retratos.TryTake(out var r)) { pausado = r; dep.Continuar(); }
        });
        Conferir("depurador: a pausa marcada dentro de uma tarefa do FreeRTOS para na linha dela",
            nativo && erro is null && pausado?.Linha == 4 && pausado.Variaveis.Any(v => v.Nome == "giros"), $"{nativo} {erro} | {Mostrar(pausado)}");
    }
}
