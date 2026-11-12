namespace Simulador.App;

/// <summary>
/// As pecas do fim da fila: o sensor de obstaculo infravermelho (FC-51), o
/// seguidor de linha (TCRT5000) e o modulo Bluetooth HC-05 -- o desenho, os
/// pinos e o que cada um faz rodando.
/// </summary>
public static partial class Autoteste
{
    static List<string> RegistroDaRede(MainWindow janela)
    {
        long visto = 0;
        return [.. janela.TesteRedeSimulada.Novos(ref visto).Select(e => $"{e.Placa}: {e.Texto}")];
    }

    static void Pecas_IrEBluetooth()
    {
        saida.AppendLine("pecas: obstaculo IR, seguidor de linha e HC-05");
        static void Ligar(Circuito c, string a, string pa, string b, string pb)
            => c.Fios.Add(new Fio { DePeca = a, DePino = pa, ParaPeca = b, ParaPino = pb });

        // ---- o catalogo: os pinos na ordem da placa, a categoria, o desenho ----
        foreach (var (tipo, pinos, categoria) in new[]
        {
            ("ir-obstacle-sensor", new[] { "VCC", "GND", "OUT" }, "Sensores"),
            ("tcrt5000", new[] { "S", "V+", "GND" }, "Sensores"),
            ("hc-05", new[] { "STATE", "RXD", "TXD", "GND", "VCC", "EN" }, "Modulos"),
        })
        {
            var d = Catalogo.Obter(tipo);
            Conferir($"{tipo}: o desenho, os pinos na ordem da placa ({string.Join(", ", pinos)}) e a categoria {categoria}",
                d is not null && Pecas.Existe(tipo) && d.Pinos.Select(p => p.Nome).SequenceEqual(pinos) && d.Categoria == categoria,
                d is null ? "(sem a peca)" : $"{string.Join(",", d.Pinos.Select(p => p.Nome))} / {d.Categoria}");
        }

        // ---- os sensores IR lidos por um Uno ----
        {
            const string sketch = """
                int antes2 = -1, antes3 = -1;
                void setup() {
                  Serial.begin(9600);
                  pinMode(2, INPUT);
                  pinMode(3, INPUT);
                  Serial.println("pronto");
                }
                void loop() {
                  int a = digitalRead(2), b = digitalRead(3);
                  if (a != antes2) { Serial.print("obstaculo "); Serial.println(a); antes2 = a; }
                  if (b != antes3) { Serial.print("linha "); Serial.println(b); antes3 = b; }
                  delay(20);
                }
                """;
            var c = new Circuito();
            var uno = new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true, Sketch = sketch };
            var fc = new PecaNoCircuito { Id = "fc", Tipo = "ir-obstacle-sensor", X = 500, Y = 0 };
            var tc = new PecaNoCircuito { Id = "tc", Tipo = "tcrt5000", X = 500, Y = 200 };
            c.Pecas.AddRange([uno, fc, tc]);
            Ligar(c, "fc", "VCC", "uno", "5V"); Ligar(c, "fc", "GND", "uno", "GND.1"); Ligar(c, "fc", "OUT", "uno", "2");
            Ligar(c, "tc", "V+", "uno", "5V"); Ligar(c, "tc", "GND", "uno", "GND.2"); Ligar(c, "tc", "S", "uno", "3");
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(c, sketch);
                janela.TesteRodar();
                var maq = janela.TesteMaquinaDa("uno");
                var ed = janela.TesteEditorCircuito;
                bool Viu(string linha) => LinhasDoSerial(maq).Contains(linha);
                EsperarPor(() => Viu("obstaculo 1") && Viu("linha 1"), 15000);
                bool livre = Viu("obstaculo 1") && Viu("linha 1");
                bool apagado = !ed.LuzDeModuloAcesaParaTeste("fc", 1) && ed.LuzDeModuloAcesaParaTeste("fc", 0);
                fc.Giro = 1;
                tc.Giro = 1;
                EsperarPor(() => Viu("obstaculo 0") && Viu("linha 0"), 6000);
                EsperarPor(() => ed.LuzDeModuloAcesaParaTeste("fc", 1) && ed.LuzDeModuloAcesaParaTeste("tc", 0), 3000);
                bool detectou = Viu("obstaculo 0") && Viu("linha 0");
                bool acesos = ed.LuzDeModuloAcesaParaTeste("fc", 1) && ed.LuzDeModuloAcesaParaTeste("tc", 0);
                string tudo = string.Join(" | ", LinhasDoSerial(maq));
                Conferir("FC-51: o OUT fica em HIGH sem obstaculo e vai a LOW com ele; o LED de energia acende e o de saida so com o obstaculo",
                    livre && detectou && apagado && acesos, tudo);
                Conferir("TCRT5000: o S fica em HIGH sem reflexo (a linha preta) e vai a LOW sobre a superficie clara, com o LED de sinal aceso",
                    Viu("linha 1") && Viu("linha 0") && ed.LuzDeModuloAcesaParaTeste("tc", 0), tudo);
            }
            finally { janela.TesteParar(); }
        }

        // ---- o HC-05: o Uno conversa com ele pela SoftwareSerial (10 RX, 11 TX) ----
        (List<string> Serial, MainWindow Janela, Circuito C) Hc05(string sketch, bool enAlto, bool comState = false)
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true, Sketch = sketch });
            c.Pecas.Add(new PecaNoCircuito { Id = "bt", Tipo = "hc-05", X = 500, Y = 0 });
            Ligar(c, "bt", "VCC", "uno", "5V"); Ligar(c, "bt", "GND", "uno", "GND.1");
            Ligar(c, "bt", "TXD", "uno", "10"); Ligar(c, "bt", "RXD", "uno", "11");
            if (enAlto) Ligar(c, "bt", "EN", "uno", "3.3V");
            if (comState) Ligar(c, "bt", "STATE", "uno", "7");
            var janela = new MainWindow();
            janela.TesteAbrir(c, sketch);
            janela.TesteRodar();
            return (LinhasDoSerial(janela.TesteMaquinaDa("uno")), janela, c);
        }

        string At(long baud) => $$"""
            #include <SoftwareSerial.h>
            SoftwareSerial bt(10, 11);
            void comando(const char *c) {
              bt.print(c);
              bt.print("\r\n");
              delay(250);
              while (bt.available()) Serial.write(bt.read());
            }
            void setup() {
              Serial.begin(9600);
              bt.begin({{baud}});
              delay(400);
              comando("AT");
              comando("AT+VERSION?");
              comando("AT+NAME=Robo");
              comando("AT+NAME?");
              comando("AT+UART?");
              comando("AT+XYZ");
              Serial.println("pronto");
            }
            void loop() {}
            """;
        {
            var (_, janela, _) = Hc05(At(38400), enAlto: true);
            try
            {
                var maq = janela.TesteMaquinaDa("uno");
                EsperarPor(() => LinhasDoSerial(maq).Contains("pronto"), 15000);
                var linhas = LinhasDoSerial(maq);
                string tudo = string.Join(" | ", linhas);
                Conferir("HC-05 com o EN em HIGH ao ligar: o modo AT a 38400 baud responde OK, a versao 2.0 e o nome trocado",
                    linhas.Count(l => l == "OK") >= 4 && linhas.Contains("+VERSION:2.0-20100601") && linhas.Contains("+NAME:Robo")
                    && linhas.Contains("+UART:9600,0,0") && linhas.Contains("ERROR:(0)"), tudo);
                Conferir("HC-05 no modo AT: o LED pisca devagar (2 s aceso, 2 s apagado)",
                    janela.TesteEditorCircuito.Hc05.De("bt") is { ModoAt: true, Alimentado: true }, tudo);

                // O que o AT mudou vai para a peca, e dela para o arquivo do projeto.
                var peca = janela.TesteEditorCircuito.Atual.Achar("bt");
                string json = janela.TesteEditorCircuito.Atual.ParaJson();
                var volta = Circuito.DeJson(json).Achar("bt");
                Conferir("HC-05: o AT+NAME fica na peca e vai no arquivo do projeto (a flash do modulo)",
                    peca?.Hc05?.Nome == "Robo" && json.Contains("\"Hc05\"") && volta?.Hc05?.Nome == "Robo" && volta.Hc05.Baud == 9600,
                    $"{peca?.Hc05?.Nome} / {volta?.Hc05?.Nome}");
            }
            finally { janela.TesteParar(); }
        }
        {
            var (_, janela, _) = Hc05(At(9600), enAlto: true);
            try
            {
                var maq = janela.TesteMaquinaDa("uno");
                EsperarPor(() => LinhasDoSerial(maq).Contains("pronto"), 15000);
                var linhas = LinhasDoSerial(maq);
                Conferir("HC-05: no modo AT com a placa a 9600 (e nao 38400) os bytes chegam trocados e nada responde",
                    !linhas.Contains("OK") && !linhas.Any(l => l.StartsWith("+VERSION")), string.Join(" | ", linhas));
            }
            finally { janela.TesteParar(); }
        }

        // O botao da placa (o KEY) segurado ao dar o F5: liga no modo AT, sem o EN.
        {
            var c = new Circuito();
            string sketch = At(38400);
            c.Pecas.Add(new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true, Sketch = sketch });
            c.Pecas.Add(new PecaNoCircuito { Id = "bt", Tipo = "hc-05", X = 500, Y = 0 });
            Ligar(c, "bt", "VCC", "uno", "5V"); Ligar(c, "bt", "GND", "uno", "GND.1");
            Ligar(c, "bt", "TXD", "uno", "10"); Ligar(c, "bt", "RXD", "uno", "11");
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(c, sketch);
                var ed = janela.TesteEditorCircuito;
                var bt = ed.Atual.Achar("bt");
                var (bx, by, _, _) = EditorCircuito.BotoesPorPlaca["hc-05"][0];
                bool pegou = ed.Apertar(Geometria.PosicaoLocal(bt, Catalogo.Obter("hc-05"), bx, by));
                janela.TesteRodar();
                EsperarPor(() => ed.Hc05.De("bt") is { Alimentado: true }, 6000);
                ed.Soltar();
                var maq = janela.TesteMaquinaDa("uno");
                EsperarPor(() => LinhasDoSerial(maq).Contains("pronto"), 15000);
                var linhas = LinhasDoSerial(maq);
                Conferir("HC-05: o botao da placa segurado ao ligar poe o modulo no modo AT (38400), e ele fica la depois de solto",
                    pegou && ed.Hc05.De("bt") is { ModoAt: true } && linhas.Contains("+VERSION:2.0-20100601"),
                    $"pegou={pegou} | {string.Join(" | ", linhas)}");
            }
            finally { janela.TesteParar(); }
        }

        // O modo de dados: o celular do painel conecta, manda, e o eco volta.
        {
            const string eco = """
                #include <SoftwareSerial.h>
                SoftwareSerial bt(10, 11);
                int estado = -1;
                void setup() {
                  Serial.begin(9600);
                  bt.begin(9600);
                  pinMode(7, INPUT);
                  Serial.println("pronto");
                }
                void loop() {
                  int e = digitalRead(7);
                  if (e != estado) { Serial.print("state "); Serial.println(e); estado = e; }
                  if (bt.available()) {
                    String s = bt.readStringUntil('\n');
                    s.trim();
                    Serial.print("recebi: ");
                    Serial.println(s);
                    bt.print("eco ");
                    bt.println(s);
                  }
                  delay(10);
                }
                """;
            var (_, janela, _) = Hc05(eco, enAlto: false, comState: true);
            try
            {
                var maq = janela.TesteMaquinaDa("uno");
                var rede = janela.TesteRedeSimulada;
                var celular = new System.Text.StringBuilder();
                rede.ChegouNoCelular += (_, dados) => { lock (celular) celular.Append(System.Text.Encoding.ASCII.GetString(dados)); };
                EsperarPor(() => LinhasDoSerial(maq).Contains("pronto") && rede.DispositivosVisiveis().Contains("HC-05"), 15000);
                bool visivel = rede.DispositivosVisiveis().Contains("HC-05");
                bool conectou = rede.ConectarCelular("HC-05");
                EsperarPor(() => LinhasDoSerial(maq).Contains("state 1"), 4000);
                rede.EnviarDoCelular("oi robo\n");
                EsperarPor(() => { lock (celular) return celular.ToString().Contains("eco oi robo"); }, 6000);
                string noCelular;
                lock (celular) noCelular = celular.ToString();
                string tudo = string.Join(" | ", LinhasDoSerial(maq));
                Conferir("HC-05 no modo de dados: aparece como \"HC-05\", o celular conecta e o STATE vai a HIGH",
                    visivel && conectou && LinhasDoSerial(maq).Contains("state 0") && LinhasDoSerial(maq).Contains("state 1"), tudo);
                Conferir("HC-05: o que o celular manda sai pelo TXD na placa, e o que a placa manda pelo RXD chega no celular",
                    LinhasDoSerial(maq).Contains("recebi: oi robo") && noCelular.Contains("eco oi robo"), $"{tudo} || celular: {noCelular.Trim()}");
            }
            finally { janela.TesteParar(); }
        }

        // Uma ESP32 mestre (SerialBT.connect("HC-05")) conversa com o Uno pelo HC-05.
        {
            const string mestre = """
                #include "BluetoothSerial.h"
                BluetoothSerial SerialBT;
                void setup() {
                  Serial.begin(115200);
                  SerialBT.begin("ESP32", true);
                  delay(500);
                  while (!SerialBT.connect("HC-05")) delay(300);
                  Serial.println("conectou");
                }
                void loop() {
                  SerialBT.println("ola do esp32");
                  delay(500);
                }
                """;
            const string escravo = """
                #include <SoftwareSerial.h>
                SoftwareSerial bt(10, 11);
                void setup() {
                  Serial.begin(9600);
                  bt.begin(9600);
                }
                void loop() {
                  if (bt.available()) {
                    String s = bt.readStringUntil('\n');
                    s.trim();
                    Serial.print("recebi: ");
                    Serial.println(s);
                  }
                }
                """;
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true, Sketch = escravo });
            c.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 0, Y = 700, Usb = true, Sketch = mestre });
            c.Pecas.Add(new PecaNoCircuito { Id = "bt", Tipo = "hc-05", X = 500, Y = 0 });
            Ligar(c, "bt", "VCC", "uno", "5V"); Ligar(c, "bt", "GND", "uno", "GND.1");
            Ligar(c, "bt", "TXD", "uno", "10"); Ligar(c, "bt", "RXD", "uno", "11");
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(c, escravo);
                janela.TesteRodar();
                var uno = janela.TesteMaquinaDa("uno");
                var esp = janela.TesteMaquinaDa("esp");
                EsperarPor(() => LinhasDoSerial(uno).Contains("recebi: ola do esp32"), 20000);
                Conferir("HC-05: uma ESP32 mestre acha o HC-05 pelo nome, conecta, e o que ela manda chega no Uno pelo TXD",
                    LinhasDoSerial(esp).Contains("conectou") && LinhasDoSerial(uno).Contains("recebi: ola do esp32"),
                    $"esp: {string.Join(" | ", LinhasDoSerial(esp))} || uno: {string.Join(" | ", LinhasDoSerial(uno))}");
            }
            finally { janela.TesteParar(); }
        }

        // O par de dois HC-05: o mestre configurado no modo AT (ROLE=1, CMODE=1) guarda a
        // configuracao, e na rodada seguinte, sem o EN, conecta sozinho no outro.
        {
            const string configura = """
                #include <SoftwareSerial.h>
                SoftwareSerial bt(10, 11);
                void comando(const char *c) {
                  bt.print(c);
                  bt.print("\r\n");
                  delay(250);
                  while (bt.available()) Serial.write(bt.read());
                }
                void setup() {
                  Serial.begin(9600);
                  bt.begin(38400);
                  delay(400);
                  comando("AT+ROLE=1");
                  comando("AT+CMODE=1");
                  comando("AT+ROLE?");
                  Serial.println("pronto");
                }
                void loop() {}
                """;
            const string manda = """
                #include <SoftwareSerial.h>
                SoftwareSerial bt(10, 11);
                void setup() { bt.begin(9600); }
                void loop() { bt.println("ping"); delay(300); }
                """;
            const string recebe = """
                #include <SoftwareSerial.h>
                SoftwareSerial bt(10, 11);
                void setup() { Serial.begin(9600); bt.begin(9600); }
                void loop() {
                  if (bt.available()) {
                    String s = bt.readStringUntil('\n');
                    s.trim();
                    Serial.print("recebi: ");
                    Serial.println(s);
                  }
                }
                """;
            static Circuito Par(string sketchMestre, string sketchEscravo, bool enDoMestre)
            {
                var c = new Circuito();
                c.Pecas.Add(new PecaNoCircuito { Id = "m", Tipo = "arduino-uno", X = 0, Y = 0, Usb = true, Sketch = sketchMestre });
                c.Pecas.Add(new PecaNoCircuito { Id = "btm", Tipo = "hc-05", X = 500, Y = 0 });
                Ligar(c, "btm", "VCC", "m", "5V"); Ligar(c, "btm", "GND", "m", "GND.1");
                Ligar(c, "btm", "TXD", "m", "10"); Ligar(c, "btm", "RXD", "m", "11");
                if (enDoMestre) Ligar(c, "btm", "EN", "m", "3.3V");
                if (sketchEscravo is not null)
                {
                    c.Pecas.Add(new PecaNoCircuito { Id = "s", Tipo = "arduino-uno", X = 0, Y = 700, Usb = true, Sketch = sketchEscravo });
                    c.Pecas.Add(new PecaNoCircuito { Id = "bts", Tipo = "hc-05", X = 500, Y = 700 });
                    Ligar(c, "bts", "VCC", "s", "5V"); Ligar(c, "bts", "GND", "s", "GND.1");
                    Ligar(c, "bts", "TXD", "s", "10"); Ligar(c, "bts", "RXD", "s", "11");
                }
                return c;
            }
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(Par(configura, null, enDoMestre: true), configura);
                janela.TesteRodar();
                var m = janela.TesteMaquinaDa("m");
                EsperarPor(() => LinhasDoSerial(m).Contains("pronto"), 15000);
                string config = string.Join(" | ", LinhasDoSerial(m));
                janela.TesteParar();

                // O mesmo modulo na rodada seguinte: a flash dele (a peca) vai junto, sem o fio do EN.
                var flash = janela.TesteEditorCircuito.Atual.Achar("btm")?.Hc05;
                var par = Par(manda, recebe, enDoMestre: false);
                par.Achar("btm").Hc05 = flash?.Copia();
                janela.TesteAbrir(par, manda);
                janela.TesteRodar();
                var s = janela.TesteMaquinaDa("s");
                EsperarPor(() => LinhasDoSerial(s).Count(l => l == "recebi: ping") >= 2, 20000);
                Conferir("HC-05 em par: o mestre configurado por AT (ROLE=1, CMODE=1) guarda isso e, na rodada seguinte, conecta sozinho no outro HC-05",
                    config.Contains("+ROLE:1") && flash is { Papel: 1, Cmode: 1 } && LinhasDoSerial(s).Count(l => l == "recebi: ping") >= 2,
                    $"config: {config} || escravo: {string.Join(" | ", LinhasDoSerial(s).Take(6))} || rede: "
                    + string.Join(" / ", RegistroDaRede(janela).TakeLast(10)));
            }
            finally { janela.TesteParar(); }
        }
    }
}
