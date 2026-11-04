namespace Simulador.App;

/// <summary>
/// As pilhas que so compilam quando o sketch usa (OpenThread, Zigbee):
/// a rede de verdade entre duas placas da bancada.
/// </summary>
public static partial class Autoteste
{
    /// <summary>Duas placas nativas na mesma bancada, cada uma com o seu sketch.</summary>
    static (string SerialA, string SerialB, string Erro) RodarDuasNativas(string sketchA, string sketchB, string placa, int ms, Func<string, string, bool> pronto = null)
    {
        bool eraNativo = Compilador.CppNativo;
        Compilador.CppNativo = true;
        var a = new Maquina { Perfil = PerfilPlaca.De(placa), Nome = "a" };
        var b = new Maquina { Perfil = PerfilPlaca.De(placa), Nome = "b", Rede = a.Rede };
        static string Serial(Maquina m) { lock (m.Trava) return string.Join("|", m.Serial.Select(l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l)); }
        try
        {
            var interpretadores = new List<(Interpretador I, Maquina M)>();
            foreach (var (sketch, m) in new[] { (sketchA, a), (sketchB, b) })
            {
                var prog = Compilador.CompilarProjeto(new Projeto { Sketch = sketch, Placa = placa, DoSimulador = Acervo.EhDoSimulador });
                if (prog.Compilado is null) return (null, null, "nao compilou nativo");
                interpretadores.Add((new Interpretador(prog, m), m));
            }
            Exception falha = null;
            var threads = interpretadores.Select(x => new Thread(() =>
            {
                try
                {
                    x.I.Preparar();
                    x.I.Setup();
                    while (!x.M.Parar && x.I.TemLoop) x.I.Loop();
                }
                catch (ExecucaoInterrompida) { }
                catch (Exception e) { falha = e; }
            }, 64 << 20) { IsBackground = true }).ToList();
            threads.ForEach(t => t.Start());
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            while (relogio.ElapsedMilliseconds < ms && falha is null && !(pronto?.Invoke(Serial(a), Serial(b)) ?? false)) Thread.Sleep(20);
            a.Parar = b.Parar = true;
            bool parou = threads.All(t => t.Join(3000));
            a.EncerrarNativo();
            b.EncerrarNativo();
            return (Serial(a), Serial(b), parou ? falha?.Message : "a placa nao parou");
        }
        catch (Exception e) when (e is ErroDeSintaxe or ErroDeExecucao) { return (null, null, e.Message); }
        finally { Compilador.CppNativo = eraNativo; }
    }

    static void Cpp_Pilhas()
    {
        saida.AppendLine("C++ compilado: as pilhas sob demanda (OpenThread, Zigbee, Matter)");
        if (!CompiladorNativo.Disponivel)
        {
            Conferir("pilhas: o compilador (nativo/zig) esta instalado", false, "faltou nativo/zig/zig.exe");
            return;
        }

        // ---- o OpenThread: a pilha de verdade, com o radio 802.15.4 entre as placas ----
        const string redeThread = """
            #include <OThread.h>
            #include <OThreadCLI.h>
            #include <OThreadCoAP.h>
            #include <OThreadUDP.h>
            DataSet rede;
            OThreadUDP udp;
            void ligar(bool novaRede) {
              OThread.begin(false);
              // Quem cria a rede sorteia o resto do dataset; quem entra recebe dela.
              if (novaRede) {
                rede.initNew();
                rede.setNetworkName("bancada");
              } else rede.clear();
              rede.setChannel(20);
              rede.setPanId(0x1234);
              uint8_t chave[16] = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16};
              rede.setNetworkKey(chave);
              uint8_t ext[8] = {1, 1, 2, 2, 3, 3, 4, 4};
              rede.setExtendedPanId(ext);
              OThread.commitDataSet(rede);
              OThread.networkInterfaceUp();
              OThread.start();
              udp.begin(5000);
            }
            const char *papel = "";
            void mostrarPapel() {
              const char *r = OThread.otGetStringDeviceRole();
              if (strcmp(r, papel)) { Serial.println(r); papel = r; }
            }

            """;
        string lider = redeThread + """
            void temperatura(OThreadCoAPRequest &pedido, OThreadCoAPResponse &resposta, void *) {
              Serial.println("coap " + pedido.payloadString());
              resposta.setPayload("25.5");
              resposta.send();
            }
            void setup() {
              Serial.begin(115200);
              ligar(true);
              while (OThread.otGetDeviceRole() != OT_ROLE_LEADER) delay(50);
              OThreadCoAPServer.begin();
              OThreadCoAPServer.on("temp", OT_COAP_METHOD_GET | OT_COAP_METHOD_PUT, temperatura);
              Serial.println("lider");
              OThreadCLI.begin();
              OThreadCLI.println("networkname");
            }
            void loop() {
              mostrarPapel();
              while (OThreadCLI.available()) Serial.write(OThreadCLI.read());
              int n = udp.parsePacket();
              if (n) {
                char t[32] = {0};
                udp.read(t, 31);
                Serial.println(String("recebi ") + t);
                udp.beginPacket(udp.remoteIP(), udp.remotePort());
                udp.print(String("eco:") + t);
                udp.endPacket();
              }
              delay(10);
            }
            """;
        string filho = redeThread + """
            void setup() {
              Serial.begin(115200);
              delay(9000);
              ligar(false);
            }
            unsigned long proximo = 0;
            bool perguntou = false;
            void loop() {
              mostrarPapel();
              ot_device_role_t r = OThread.otGetDeviceRole();
              if ((r == OT_ROLE_CHILD || r == OT_ROLE_ROUTER) && millis() > proximo) {
                proximo = millis() + 500;
                IPAddress todos;
                todos.fromString("ff03::1");
                udp.beginPacket(todos, 5000);
                udp.print("oi");
                udp.endPacket();
              }
              int n = udp.parsePacket();
              if (n) {
                char t[32] = {0};
                udp.read(t, 31);
                Serial.println(String("resposta ") + t);
                if (!perguntou) {
                  perguntou = true;
                  OThreadCoAPClient cliente;
                  int codigo = cliente.PUT(udp.remoteIP(), "temp", "qual?");
                  Serial.println(String("coap ") + codigo + " " + cliente.getString());
                }
              }
              delay(10);
            }
            """;
        var (serialA, serialB, erro) = RodarDuasNativas(lider, filho, "esp32-c6-devkit", 45000, (a, b) => a.Contains("bancada") && b.Contains("coap "));
        Conferir("OpenThread: a pilha de verdade na C6 -- a primeira placa vira Leader da rede do dataset",
            erro is null && serialA?.Contains("Leader") == true && serialA.Contains("lider"), $"{erro} | {serialA}");
        Conferir("OpenThread: a segunda placa entra na rede pelo radio 802.15.4 (Child ou Router) e o UDP vai e volta pelo multicast ff03::1",
            serialB?.Contains("Child") == true && serialA?.Contains("recebi oi") == true && serialB.Contains("resposta eco:oi"), $"{erro} | {serialA} || {serialB}");
        Conferir("OpenThread: o CoAP do core -- o PUT chega no servidor da outra placa e a resposta volta",
            serialA?.Contains("coap qual?") == true && serialB?.Contains("25.5") == true, $"{serialA} || {serialB}");
        Conferir("OpenThread: a CLI de verdade responde (networkname)", serialA?.Contains("bancada") == true, serialA);

        // ---- o Zigbee: a biblioteca do core de verdade, a rede e a da bancada ----
        const string luz = """
            #include "Zigbee.h"
            ZigbeeLight lampada(10);
            void mudou(bool ligada) { Serial.println(ligada ? "luz ligada" : "luz desligada"); }
            void setup() {
              Serial.begin(115200);
              lampada.onLightChange(mudou);
              lampada.setManufacturerAndModel("Espressif", "Lampada");
              Zigbee.addEndpoint(&lampada);
              Serial.println(Zigbee.begin() ? "comecou" : "falhou");
              while (!Zigbee.connected()) delay(50);
              Serial.println("entrou na rede");
            }
            void loop() { delay(10); }
            """;
        const string chave = """
            #include "Zigbee.h"
            ZigbeeSwitch interruptor(5);
            bool leu = false;
            void setup() {
              Serial.begin(115200);
              Zigbee.addEndpoint(&interruptor);
              Zigbee.setRebootOpenNetwork(180);
              Serial.println(Zigbee.begin(ZIGBEE_COORDINATOR) ? "coordenador" : "falhou");
              while (!interruptor.bound()) delay(50);
              Serial.println("ligado a lampada");
              interruptor.lightOn();
              delay(300);
              interruptor.lightToggle();
              delay(300);
              auto aparelhos = interruptor.getBoundDevices();
              for (auto a : aparelhos) {
                Serial.println(String("fabricante ") + interruptor.readManufacturer(a->endpoint, a->short_addr, a->ieee_addr));
                Serial.println(String("modelo ") + interruptor.readModel(a->endpoint, a->short_addr, a->ieee_addr));
              }
              Serial.println("fim");
            }
            void loop() { delay(10); }
            """;
        (serialA, serialB, erro) = RodarDuasNativas(chave, luz, "esp32-c6-devkit", 30000, (a, b) => a.Contains("fim"));
        Conferir("Zigbee: o coordenador forma a rede, a lampada entra e o core liga as duas (find + bind)",
            erro is null && serialA?.Contains("coordenador") == true && serialA.Contains("ligado a lampada") && serialB?.Contains("entrou na rede") == true, $"{erro} | {serialA} || {serialB}");
        Conferir("Zigbee: o interruptor manda on e toggle, e o callback da lampada recebe",
            serialB?.Contains("luz ligada") == true && serialB.Contains("luz desligada") && serialB.IndexOf("luz ligada") < serialB.IndexOf("luz desligada"), $"{serialA} || {serialB}");
        Conferir("Zigbee: a leitura do cluster Basic pela rede traz o fabricante e o modelo",
            serialA?.Contains("fabricante Espressif") == true && serialA.Contains("modelo Lampada"), serialA);

        const string sensorZb = """
            #include "Zigbee.h"
            ZigbeeTempSensor sensor(10);
            void setup() {
              Serial.begin(115200);
              sensor.setMinMaxValue(10, 50);
              sensor.setTolerance(1);
              Zigbee.addEndpoint(&sensor);
              Zigbee.begin();
              while (!Zigbee.connected()) delay(50);
              sensor.setReporting(0, 0, 1);
              Serial.println("sensor na rede");
            }
            float t = 20;
            void loop() {
              delay(200);
              t += 2;
              sensor.setTemperature(t);
            }
            """;
        const string termostato = """
            #include "Zigbee.h"
            ZigbeeThermostat aparelho(5);
            int chegaram = 0;
            float ultimo = 0;
            void recebeu(float t) { chegaram++; ultimo = t; }
            float tmin = 0, tmax = 0;
            void config(float mn, float mx, float tol) { tmin = mn; tmax = mx; }
            void setup() {
              Serial.begin(115200);
              aparelho.onTempReceive(recebeu);
              aparelho.onTempConfigReceive(config);
              Zigbee.addEndpoint(&aparelho);
              Zigbee.setRebootOpenNetwork(180);
              Zigbee.begin(ZIGBEE_COORDINATOR);
              while (!aparelho.bound()) delay(50);
              Serial.println("ligado ao sensor");
              aparelho.getTemperatureSettings();
              delay(500);
              Serial.println(String("faixa ") + tmin + " " + tmax);
              aparelho.getTemperature();
            }
            bool disse = false;
            void loop() {
              delay(50);
              if (chegaram >= 3 && !disse) {
                disse = true;
                Serial.println(String("relatorios ") + (ultimo > 20 ? "subindo" : "parados"));
              }
            }
            """;
        (serialA, serialB, erro) = RodarDuasNativas(termostato, sensorZb, "esp32-c6-devkit", 30000, (a, b) => a.Contains("relatorios"));
        Conferir("Zigbee: o sensor do core manda relatorios pelo binding e o termostato recebe; a faixa vem pela leitura dos atributos",
            erro is null && serialA?.Contains("ligado ao sensor") == true && serialA.Contains("faixa 10.00 50.00") && serialA.Contains("relatorios subindo"), $"{erro} | {serialA} || {serialB}");

        // ---- o Matter: os endpoints de verdade, com a bancada no papel do aplicativo ----
        const string matter = """
            #include <Matter.h>
            #include <WiFi.h>
            MatterOnOffLight luz;
            MatterTemperatureSensor termometro;
            MatterContactSensor porta;
            MatterGenericSwitch botao;
            MatterDimmableLight abajur;
            void setup() {
              Serial.begin(115200);
              Serial.println(Matter.isDeviceCommissioned() ? "ja comissionada" : "ainda nao");
              WiFi.begin("CircuitSketch-WiFi");
              while (WiFi.status() != WL_CONNECTED) delay(50);
              luz.begin(false);
              luz.onChange([](bool v) { Serial.printf("luz mudou %d\n", v); return true; });
              termometro.begin(20.0);
              porta.begin();
              botao.begin();
              abajur.begin(false, 10);
              abajur.onChangeBrightness([](uint8_t b) { Serial.printf("brilho %d\n", b); return b != 99; });
              Matter.onEvent([](matterEvent_t e, const chip::DeviceLayer::ChipDeviceEvent *) {
                if (e == MATTER_COMMISSIONING_COMPLETE) Serial.println("evento comissionou");
              });
              Matter.begin();
              Serial.println("manual " + Matter.getManualPairingCode());
              Serial.println("qr " + Matter.getOnboardingQRCodeUrl());
              Serial.println("esperando");
            }
            void loop() {
              static bool feito = false;
              if (Matter.isDeviceCommissioned() && !feito) {
                feito = true;
                Serial.printf("ids %d %d %d %d %d\n", luz.getEndPointId(), termometro.getEndPointId(), porta.getEndPointId(), botao.getEndPointId(), abajur.getEndPointId());
                termometro.setTemperature(31.5);
                porta.setContact(true);
                botao.click();
                Serial.println("pronta");
              }
              static bool visto = false;
              if (luz && !visto) { visto = true; Serial.println("sketch viu a luz"); }
              delay(20);
            }
            """;
        int passoMatter = 0;
        string valorTemperatura = null, valorPorta = null;
        var eventosMatter = new List<string>();
        (var serialM, var erroM, var nativoM, var placaM) = RodarNativo(matter, 6000, "esp32-c6-devkit", durante: (Maquina m) =>
        {
            if (passoMatter == 0 && SerialTem(m, "esperando")) { passoMatter = 1; m.Matter.Escrever(1, 6, 0, 1); m.Matter.Comissionar(); }
            else if (passoMatter == 1 && SerialTem(m, "pronta"))
            {
                passoMatter = 2;
                m.Matter.Escrever(1, 6, 0, 1);
                m.Matter.Escrever(5, 8, 0, 200);
                m.Matter.Escrever(5, 8, 0, 99);
            }
        });
        if (placaM is not null)
        {
            valorTemperatura = placaM.Matter.Valor(2, 0x402, 0);
            valorPorta = placaM.Matter.Valor(3, 0x45, 0);
            eventosMatter = placaM.Matter.Eventos;
        }
        Conferir("Matter: o codigo de pareamento manual e o do QR saem como na especificacao (34970112332 com 3840/20202021)",
            nativoM && erroM is null && serialM?.Contains("manual 34970112332") == true && serialM.Contains("MT%3AY.K9042C00KA0648G00"), $"nativo={nativoM} erro={erroM} serial=[{serialM}]");
        Conferir("Matter: a bancada comissiona, o evento chega e cada endpoint de verdade ganha o seu numero",
            serialM?.Contains("ainda nao") == true && serialM.Contains("evento comissionou") && serialM.Contains("ids 1 2 3 4 5"), $"serial=[{serialM}]");
        Conferir("Matter: o aplicativo escreve e chega no onChange; a escrita antes de comissionar nao vale; o brilho recusado nao muda",
            serialM?.Contains("luz mudou 1") == true && serialM.Contains("sketch viu a luz") && serialM.Contains("brilho 200") && serialM.Contains("brilho 99") && placaM?.Matter.Valor(5, 8, 0) == "200"
            && (serialM.IndexOf("luz mudou 1", StringComparison.Ordinal) > serialM.IndexOf("pronta", StringComparison.Ordinal)), $"serial=[{serialM}] brilho={placaM?.Matter.Valor(5, 8, 0)}");
        Conferir("Matter: o sensor reporta em centesimos de grau, o contato vira o atributo do BooleanState e o clique manda os eventos do interruptor",
            valorTemperatura == "3150" && valorPorta == "1" && eventosMatter.Contains("4 InitialPress 1 0") && eventosMatter.Contains("4 ShortRelease 1 0"), $"temperatura={valorTemperatura} porta={valorPorta} eventos=[{string.Join(";", eventosMatter)}]");
    }
}
