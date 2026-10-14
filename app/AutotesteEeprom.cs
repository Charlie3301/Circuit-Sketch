namespace Simulador.App;

/// <summary>
/// A EEPROM (a de verdade do AVR e a emulada na flash da ESP32) e a memoria
/// RTC do usuario do ESP8266: os bytes no layout da placa, o tempo de cada
/// escrita, o commit() e o que sobrevive ao reinicio.
/// </summary>
public static partial class Autoteste
{
    /// <summary>Roda o setup() seguindo os reinicios (ESP.restart, deepSleep), na MESMA placa, ate o teto.</summary>
    static (Maquina Maq, List<string> Serial, string Erro) RodarNaMesmaPlaca(string sketch, string placa, long tetoMs, Maquina maq = null)
    {
        var prog = Compilador.CompilarProjeto(new Projeto { Sketch = sketch, Placa = placa, DoSimulador = Acervo.EhDoSimulador });
        maq ??= new Maquina { Perfil = PerfilPlaca.De(placa) };
        maq.Reiniciar();
        var linhas = new List<string>();
        ReinicioDaPlaca origem = null;
        string erro = null;
        for (int rodada = 0; rodada < 20; rodada++)
        {
            maq.LimiteDoRelogioVirtual = tetoMs;
            var interp = new Interpretador(prog, maq) { LimitePassos = long.MaxValue, Origem = origem };
            try
            {
                interp.Preparar();
                interp.Setup();
                break;
            }
            catch (ReinicioDaPlaca r)
            {
                linhas.AddRange(maq.Serial.Select(SemHora));
                linhas.Add("--- " + r.Resumo);
                origem = r;
                maq.Reiniciar();
                continue;
            }
            catch (ErroDeExecucao e) { erro = e.Message; break; }
            catch (ExecucaoInterrompida) { break; }
        }
        linhas.AddRange(maq.Serial.Select(SemHora));
        return (maq, linhas, erro);

        static string SemHora(string l) => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l;
    }

    static void Memoria_EepromERtc()
    {
        saida.AppendLine("eeprom e memoria rtc");

        // ---- Uno: a EEPROM de verdade ----
        var (_, uno, erroUno) = RodarComPinos("""
            #include <EEPROM.h>
            struct Config { int brilho; float limite; char nome[8]; };
            void setup() {
              Serial.begin(9600);
              Serial.println(EEPROM.length());
              Serial.println(EEPROM.read(0));
              Config c = {200, 3.5, "sala"};
              EEPROM.put(10, c);
              Config d;
              EEPROM.get(10, d);
              Serial.println(d.brilho);
              Serial.println(d.limite);
              Serial.println(d.nome);
              Serial.println(sizeof(Config));
              Serial.println(EEPROM.read(10) + EEPROM.read(11) * 256);
              EEPROM[5] = 7;
              EEPROM[5] += 1;
              Serial.println(EEPROM[5]);
              unsigned long t0 = millis();
              for (int i = 0; i < 10; i++) EEPROM.write(100 + i, i);
              Serial.println(millis() - t0);
              t0 = millis();
              for (int i = 0; i < 10; i++) EEPROM.update(100 + i, i);
              Serial.println(millis() - t0);
              float vazio;
              EEPROM.get(500, vazio);
              Serial.println(vazio);
            }
            void loop() {}
            """, "arduino-uno", 2000);
        string u = string.Join(" | ", uno);
        saida.AppendLine($"        uno: {u} {erroUno}");
        Conferir("eeprom: o Uno tem 1024 bytes e a EEPROM nasce apagada (255)",
            erroUno is null && uno.Count > 1 && uno[0] == "1024" && uno[1] == "255", $"{u} {erroUno}");
        Conferir("eeprom: put/get de uma struct volta igual",
            uno.Count > 4 && uno[2] == "200" && uno[3] == "3.50" && uno[4] == "sala", u);
        Conferir("eeprom: no Uno a struct {int, float, char[8]} tem 14 bytes, e o int ocupa 2 (little-endian)",
            uno.Count > 6 && uno[5] == "14" && uno[6] == "200", u);
        Conferir("eeprom: EEPROM[i] le e escreve, e o += funciona", uno.Count > 7 && uno[7] == "8", u);
        Conferir("eeprom: 10 escritas levam ~30 ms (3,3 ms cada, uma espera a outra)",
            uno.Count > 8 && int.TryParse(uno[8], out int ms) && ms is >= 28 and <= 40, u);
        Conferir("eeprom: o update() nao reescreve o que nao mudou (0 ms)", uno.Count > 9 && uno[9] == "0", u);
        Conferir("eeprom: float lido da EEPROM apagada e nan, como na placa", uno.Count > 10 && uno[10] == "nan", u);

        var (_, _, erroBegin) = RodarComPinos("#include <EEPROM.h>\nvoid setup() { EEPROM.begin(512); }\nvoid loop() {}", "arduino-uno", 500);
        Conferir("eeprom: EEPROM.begin(512) no Uno nao compila (a EEPROM e de verdade)",
            erroBegin?.Contains("no matching function") == true, erroBegin ?? "rodou");
        var (_, _, erroCommit) = RodarComPinos("#include <EEPROM.h>\nvoid setup() { EEPROM.commit(); }\nvoid loop() {}", "arduino-uno", 500);
        Conferir("eeprom: EEPROM.commit() no Uno nao compila", erroCommit?.Contains("no member named 'commit'") == true, erroCommit ?? "rodou");

        // ---- ESP32: emulada na flash, com begin() e commit() ----
        var (_, esp, erroEsp) = RodarNaMesmaPlaca("""
            #include <EEPROM.h>
            struct Par { uint8_t a; int b; };
            void setup() {
              Serial.begin(115200);
              Serial.println(EEPROM.read(0));
              EEPROM.begin(64);
              byte boots = EEPROM.read(0);
              Serial.print("boot ");
              Serial.print(boots);
              Serial.print(" ");
              Serial.println(EEPROM.read(1));
              if (boots == 255) boots = 0;
              EEPROM.write(0, boots + 1);
              Par p = {7, 0x01020304};
              EEPROM.put(8, p);
              EEPROM.writeFloat(20, 2.5);
              EEPROM.commit();
              EEPROM.write(1, 77);
              Serial.println(sizeof(Par));
              Serial.println(EEPROM.read(12));
              Serial.println(EEPROM.readFloat(20));
              if (boots < 2) ESP.restart();
              Serial.println("fim");
            }
            void loop() {}
            """, "esp32-devkit-v1", 3000);
        string e = string.Join(" | ", esp);
        saida.AppendLine($"        esp32: {e} {erroEsp}");
        Conferir("eeprom: na ESP32, antes do begin() o read() devolve 0", erroEsp is null && esp.FirstOrDefault() == "0", $"{e} {erroEsp}");
        Conferir("eeprom: o que teve commit() sobrevive ao ESP.restart(); o que nao teve, some",
            esp.Contains("boot 255 255") && esp.Contains("boot 1 255") && esp.Contains("boot 2 255") && esp.Contains("fim"), e);
        Conferir("eeprom: na ESP32 a struct {uint8_t, int} tem 8 bytes (o int alinhado em 4)",
            esp.Contains("8") && esp.Contains("4") && esp.Contains("2.50"), e);

        // ---- ESP8266: a memoria RTC do usuario sobrevive ao deepSleep ----
        var (_, rtc, erroRtc) = RodarNaMesmaPlaca("""
            struct Dados { uint32_t contagem; float ultimo; } dados;
            void setup() {
              Serial.begin(115200);
              ESP.rtcUserMemoryRead(0, (uint32_t*)&dados, sizeof(dados));
              if (dados.contagem > 100) dados.contagem = 0;
              Serial.print("contagem ");
              Serial.println(dados.contagem);
              dados.contagem++;
              ESP.rtcUserMemoryWrite(0, (uint32_t*)&dados, sizeof(dados));
              Serial.println(ESP.rtcUserMemoryWrite(127, (uint32_t*)&dados, sizeof(dados)));
              if (dados.contagem < 3) ESP.deepSleep(1000000);
              Serial.println("fim");
            }
            void loop() {}
            """, "esp8266-nodemcu", 5000);
        string r = string.Join(" | ", rtc);
        saida.AppendLine($"        esp8266: {r} {erroRtc}");
        Conferir("rtc: a memoria RTC do usuario do ESP8266 sobrevive ao deepSleep",
            erroRtc is null && rtc.Contains("contagem 1") && rtc.Contains("contagem 2") && rtc.Contains("fim"), $"{r} {erroRtc}");
        Conferir("rtc: escrever alem dos 512 bytes devolve false", rtc.Contains("0"), r);

        // ---- a EEPROM do Uno nao esquece entre uma rodada e outra (a mesma placa) ----
        var placa = new Maquina { Perfil = PerfilPlaca.De("arduino-uno") };
        const string contador = """
            #include <EEPROM.h>
            void setup() {
              Serial.begin(9600);
              byte n = EEPROM.read(0);
              if (n == 255) n = 0;
              EEPROM.write(0, n + 1);
              Serial.println(n + 1);
            }
            void loop() {}
            """;
        RodarNaMesmaPlaca(contador, "arduino-uno", 500, placa);
        placa.Desligar();
        var (_, segunda, _) = RodarNaMesmaPlaca(contador, "arduino-uno", 500, placa);
        Conferir("eeprom: a EEPROM do Uno guarda o contador depois de cortar a energia",
            segunda.FirstOrDefault() == "2", string.Join(" | ", segunda));
    }
}
