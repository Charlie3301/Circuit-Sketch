using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Simulador.App;

/// <summary>
/// O MQTT da aula (Alpha 19): o que acontece com trinta alunos no mesmo
/// broker, com a rede da escola bloqueando a porta, com a senha errada,
/// com o JSON grande e com o broker que so fala TLS.
/// </summary>
public static partial class Autoteste
{
    /// <summary>
    /// Um broker de mentira em localhost, para o que o broker interno nao
    /// faz: recusar a senha, falar TLS. Responde o CONNECT com o codigo
    /// pedido e devolve cada PUBLISH para quem mandou (o eco basta para a
    /// mensagem dar a volta).
    /// </summary>
    sealed class BrokerDeTeste : IDisposable
    {
        readonly TcpListener ouvinte = new(IPAddress.Loopback, 0);
        readonly System.Security.Cryptography.X509Certificates.X509Certificate2 certificado;
        volatile bool fechado;

        public int Porta { get; }
        public string UltimoUsuario { get; private set; }
        public bool RecebeuTestamento { get; private set; }

        public BrokerDeTeste(byte codigoDoConnack, bool tls)
        {
            if (tls)
            {
                using var chave = System.Security.Cryptography.RSA.Create(2048);
                var pedido = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                    "CN=localhost", chave, System.Security.Cryptography.HashAlgorithmName.SHA256,
                    System.Security.Cryptography.RSASignaturePadding.Pkcs1);
                using var provisorio = pedido.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
                // O SslStream do Windows quer o certificado com a chave exportavel carregada assim.
                certificado = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
                    provisorio.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx), null);
            }

            ouvinte.Start();
            Porta = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
            new Thread(() => Atender(codigoDoConnack)) { IsBackground = true, Name = "broker de teste" }.Start();
        }

        void Atender(byte codigo)
        {
            while (!fechado)
            {
                TcpClient tcp;
                try { tcp = ouvinte.AcceptTcpClient(); }
                catch { return; }

                new Thread(() =>
                {
                    try
                    {
                        using (tcp)
                        {
                            Stream fluxo = tcp.GetStream();
                            if (certificado is not null)
                            {
                                var seguro = new System.Net.Security.SslStream(fluxo);
                                seguro.AuthenticateAsServer(certificado);
                                fluxo = seguro;
                            }

                            while (!fechado)
                            {
                                int cabecalho = fluxo.ReadByte();
                                if (cabecalho < 0) return;
                                int tamanho = 0, mult = 1;
                                for (int b = fluxo.ReadByte(); ; b = fluxo.ReadByte())
                                {
                                    tamanho += (b & 127) * mult;
                                    mult *= 128;
                                    if ((b & 128) == 0) break;
                                }
                                var corpo = new byte[tamanho];
                                int lidos = 0;
                                while (lidos < tamanho) lidos += fluxo.Read(corpo, lidos, tamanho - lidos);

                                switch (cabecalho & 0xF0)
                                {
                                    case 0x10:
                                    {
                                        // flags: usuario (0x80) e testamento (0x04)
                                        int i = 2 + corpo[1];
                                        byte flags = corpo[i + 1];
                                        RecebeuTestamento = (flags & 0x04) != 0;
                                        int k = i + 4;
                                        int n = (corpo[k] << 8) | corpo[k + 1];
                                        k += 2 + n;                                     // id
                                        if (RecebeuTestamento)
                                        {
                                            n = (corpo[k] << 8) | corpo[k + 1]; k += 2 + n;   // topico
                                            n = (corpo[k] << 8) | corpo[k + 1]; k += 2 + n;   // mensagem
                                        }
                                        if ((flags & 0x80) != 0)
                                        {
                                            n = (corpo[k] << 8) | corpo[k + 1];
                                            UltimoUsuario = System.Text.Encoding.UTF8.GetString(corpo, k + 2, n);
                                        }
                                        fluxo.Write([0x20, 2, 0, codigo]);
                                        if (codigo != 0) return;
                                        break;
                                    }
                                    case 0x30:
                                        // eco: a mensagem volta para quem publicou
                                        fluxo.Write([(byte)cabecalho, .. Tamanho(tamanho), .. corpo]);
                                        break;
                                    case 0x80:
                                        fluxo.Write([0x90, 3, corpo[0], corpo[1], 0]);
                                        break;
                                    case 0xC0:
                                        fluxo.Write([0xD0, 0]);
                                        break;
                                    case 0xE0:
                                        return;
                                }
                            }
                        }
                    }
                    catch { }
                }) { IsBackground = true }.Start();
            }
        }

        static byte[] Tamanho(int n)
        {
            var saida = new List<byte>();
            do
            {
                byte b = (byte)(n % 128);
                n /= 128;
                if (n > 0) b |= 0x80;
                saida.Add(b);
            } while (n > 0);
            return [.. saida];
        }

        public void Dispose()
        {
            fechado = true;
            try { ouvinte.Stop(); } catch { }
            certificado?.Dispose();
        }
    }

    static void Rede_MqttNaAula()
    {
        saida.AppendLine("MQTT na aula");

        static string Conectador(string id, string extra = "") => """
            #include <WiFi.h>
            #include <PubSubClient.h>
            WiFiClient espClient;
            PubSubClient client(espClient);

            void callback(char* topic, byte* payload, unsigned int length) {
              String msg;
              for (int i = 0; i < length; i++) { msg += (char)payload[i]; }
              Serial.println(String(topic) + "=" + msg);
            }

            void setup() {
              Serial.begin(115200);
              WiFi.begin("CircuitSketch-WiFi", "12345678");
              while (WiFi.status() != WL_CONNECTED) { delay(200); }
              client.setServer("broker.hivemq.com", 1883);
              client.setCallback(callback);
              Serial.println(client.connect("ID"));
              EXTRA
            }

            void loop() { client.loop(); }
            """.Replace("\"ID\"", id).Replace("EXTRA", extra);

        // ---- id repetido: o broker derruba o mais velho ----
        {
            var rede = new Rede { UsarInternet = false };
            var (maqA, interpA) = MontarPlaca(rede, "ESP32-A", Conectador("\"mesmo-id\"")
                .Replace("void loop() { client.loop(); }",
                         "void loop() { client.loop(); Serial.println(client.state()); delay(100); }"));
            var (maqB, interpB) = MontarPlaca(rede, "ESP32-B", Conectador("\"mesmo-id\"",
                "Serial.println(\"B\");"));
            interpA.Setup();
            interpB.Setup();

            var (maqC, interpC) = MontarPlaca(rede, "ESP32-C", """
                #include <WiFi.h>
                #include <PubSubClient.h>
                WiFiClient espClient;
                PubSubClient client(espClient);
                void setup() {
                  Serial.begin(115200);
                  WiFi.begin("CircuitSketch-WiFi", "12345678");
                  while (WiFi.status() != WL_CONNECTED) { delay(200); }
                  client.setServer("broker.hivemq.com", 1883);
                  client.connect("ESP32-" + WiFi.macAddress());
                }
                void loop() { Serial.println(client.state()); delay(1000); }
                """);
            interpC.Setup();
            _ = maqB; _ = maqC;

            long visto = 0;
            var eventos = rede.Novos(ref visto);
            bool avisou = eventos.Any(e => e.Texto.Contains("mesmo client id"));
            bool derrubou = eventos.Any(e => e.Texto.Contains("derrubou a conexão"));
            saida.AppendLine($"        id repetido: {string.Join(" / ", eventos.Where(e => e.Texto.Contains("id")).Select(e => e.Texto).Take(3))}");
            Conferir("mqtt: duas placas com o mesmo id -- o aviso explica e sugere o MAC", avisou,
                string.Join(" / ", eventos.Select(e => e.Texto)));
            Conferir("mqtt: o broker derruba a placa mais velha, como o de verdade", derrubou,
                string.Join(" / ", eventos.Select(e => e.Texto)));

            interpA.Loop();
            Conferir("mqtt: a placa derrubada ve state() -3",
                LinhasDoSerial(maqA).LastOrDefault() == "-3", string.Join("|", LinhasDoSerial(maqA)));
            rede.Encerrar();
        }

        // ---- testamento: a placa some e o broker avisa ----
        {
            var rede = new Rede { UsarInternet = false };
            var (maqPainel, interpPainel) = MontarPlaca(rede, "ESP32-painel",
                Conectador("\"painel\"", "client.subscribe(\"aula/status\");"));
            var (maqSensor, interpSensor) = MontarPlaca(rede, "ESP32-sensor", """
                #include <WiFi.h>
                #include <PubSubClient.h>
                WiFiClient espClient;
                PubSubClient client(espClient);
                void setup() {
                  WiFi.begin("CircuitSketch-WiFi", "12345678");
                  while (WiFi.status() != WL_CONNECTED) { delay(200); }
                  client.setServer("broker.hivemq.com", 1883);
                  client.connect("sensor", "aula/status", 0, true, "offline");
                  client.publish("aula/status", "online", true);
                }
                void loop() { client.loop(); }
                """);
            interpPainel.Setup();
            interpSensor.Setup();
            interpPainel.Loop();

            rede.EsquecerPlaca(maqSensor);      // a placa do sensor resetou
            interpPainel.Loop();

            var linhas = LinhasDoSerial(maqPainel);
            saida.AppendLine($"        testamento: {string.Join(" | ", linhas)}");
            Conferir("mqtt: o testamento sai quando a placa some sem desconectar",
                linhas.Contains("aula/status=online") && linhas.Contains("aula/status=offline"),
                string.Join("|", linhas));
            rede.Encerrar();
        }

        // ---- buffer de 256 bytes, publish com tamanho, beginPublish ----
        {
            var rede = new Rede { UsarInternet = false };
            var (maq, interp) = MontarPlaca(rede, "ESP32-buffer", Conectador("\"buffer\"", """
                client.subscribe("aula/#");
                String grande = "";
                for (int i = 0; i < 300; i++) grande += "x";
                Serial.println(client.publish("aula/grande", grande.c_str()));
                client.setBufferSize(1024);
                Serial.println(client.publish("aula/grande", grande.c_str()));
                byte dados[] = {65, 66, 67};
                client.publish("aula/um", dados, 1);
                client.beginPublish("aula/partes", 6, false);
                client.print("abc");
                client.print(123);
                client.endPublish();
                """));
            interp.Setup();
            interp.Loop();

            var linhas = LinhasDoSerial(maq);
            saida.AppendLine($"        buffer: {string.Join(" | ", linhas.Select(l => l.Length > 40 ? l[..40] + "..." : l))}");
            Conferir("mqtt: mensagem maior que o buffer de 256 nao sai (publish devolve 0)",
                linhas.Count > 2 && linhas[1] == "0", string.Join("|", linhas.Take(3)));
            Conferir("mqtt: com setBufferSize(1024) ela sai",
                linhas.Count > 2 && linhas[2] == "1" && linhas.Any(l => l.StartsWith("aula/grande=xxx")),
                string.Join("|", linhas.Take(4)));
            Conferir("mqtt: publish(topico, bytes, 1) manda UM byte, e nao uma retida",
                linhas.Contains("aula/um=A"), string.Join("|", linhas));
            Conferir("mqtt: beginPublish, print e endPublish", linhas.Contains("aula/partes=abc123"),
                string.Join("|", linhas));

            long visto = 0;
            Conferir("mqtt: o painel explica o publish que nao coube",
                rede.Novos(ref visto).Any(e => e.Texto.Contains("setBufferSize")), "sem aviso");
            rede.Encerrar();
        }

        // ---- broker que nao responde: a segunda vez cai direto no interno ----
        {
            var rede = new Rede { UsarInternet = true };
            string codigo = Conectador("\"sem-resposta\"").Replace("broker.hivemq.com", "broker.nao-existe.invalid");
            var (maq1, interp1) = MontarPlaca(rede, "ESP32-1", codigo);
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            interp1.Setup();
            long primeira = relogio.ElapsedMilliseconds;

            var (maq2, interp2) = MontarPlaca(rede, "ESP32-2", codigo.Replace("sem-resposta", "sem-resposta-2"));
            relogio.Restart();
            interp2.Setup();
            long segunda = relogio.ElapsedMilliseconds;

            long visto = 0;
            var eventos = rede.Novos(ref visto);
            saida.AppendLine($"        sem resposta: 1a {primeira} ms, 2a {segunda} ms");
            Conferir("mqtt: broker fora do ar cai no interno, e o sketch conecta",
                LinhasDoSerial(maq1).Contains("1") && LinhasDoSerial(maq2).Contains("1"),
                string.Join("|", LinhasDoSerial(maq1)) + " / " + string.Join("|", LinhasDoSerial(maq2)));
            Conferir("mqtt: a segunda tentativa nao espera de novo",
                segunda < 1000 && eventos.Any(e => e.Texto.Contains("não respondeu há pouco")),
                $"{segunda} ms");
            rede.Encerrar();
        }

        // ---- senha errada: sem broker interno para esconder ----
        using (var recusa = new BrokerDeTeste(4, tls: false))
        {
            var rede = new Rede { UsarInternet = true };
            var (maq, interp) = MontarPlaca(rede, "ESP32-senha", """
                #include <WiFi.h>
                #include <PubSubClient.h>
                WiFiClient espClient;
                PubSubClient client(espClient);
                void setup() {
                  Serial.begin(115200);
                  WiFi.begin("CircuitSketch-WiFi", "12345678");
                  while (WiFi.status() != WL_CONNECTED) { delay(200); }
                  client.setServer("127.0.0.1", PORTA);
                  Serial.println(client.connect("aluno", "aluno", "senha-errada"));
                  Serial.println(client.state());
                }
                void loop() {}
                """.Replace("PORTA", recusa.Porta.ToString()));
            interp.Setup();
            var linhas = LinhasDoSerial(maq);
            saida.AppendLine($"        senha errada: {string.Join(" | ", linhas)}");
            Conferir("mqtt: senha errada -- connect() falso e state() 4, sem cair no broker interno",
                linhas.SequenceEqual(["0", "4"]), string.Join("|", linhas));
            Conferir("mqtt: o usuario foi no CONNECT", recusa.UltimoUsuario == "aluno", recusa.UltimoUsuario);
            rede.Encerrar();
        }

        // ---- TLS: WiFiClientSecure na porta do broker seguro ----
        using (var seguro = new BrokerDeTeste(0, tls: true))
        {
            var rede = new Rede { UsarInternet = true };
            var (maq, interp) = MontarPlaca(rede, "ESP32-tls", """
                #include <WiFi.h>
                #include <WiFiClientSecure.h>
                #include <PubSubClient.h>
                WiFiClientSecure espClient;
                PubSubClient client(espClient);
                void callback(char* topic, byte* payload, unsigned int length) {
                  String msg;
                  for (int i = 0; i < length; i++) { msg += (char)payload[i]; }
                  Serial.println(String(topic) + "=" + msg);
                }
                void setup() {
                  Serial.begin(115200);
                  WiFi.begin("CircuitSketch-WiFi", "12345678");
                  while (WiFi.status() != WL_CONNECTED) { delay(200); }
                  espClient.setInsecure();
                  client.setServer("127.0.0.1", PORTA);
                  client.setCallback(callback);
                  Serial.println(client.connect("seguro", "usuario", "senha", "aula/estado", 0, true, "caiu"));
                  client.subscribe("aula/tls");
                  client.publish("aula/tls", "cifrado");
                }
                void loop() { client.loop(); }
                """.Replace("PORTA", seguro.Porta.ToString()));
            interp.Setup();
            bool voltou = EsperarPor(() =>
            {
                interp.Loop();
                return LinhasDoSerial(maq).Contains("aula/tls=cifrado");
            }, 5000);

            var linhas = LinhasDoSerial(maq);
            saida.AppendLine($"        tls: {string.Join(" | ", linhas)}");
            Conferir("mqtt: WiFiClientSecure conecta por TLS e a mensagem da a volta",
                linhas.FirstOrDefault() == "1" && voltou, string.Join("|", linhas));
            Conferir("mqtt: o testamento e o usuario foram no CONNECT",
                seguro.RecebeuTestamento && seguro.UltimoUsuario == "usuario",
                $"{seguro.RecebeuTestamento}/{seguro.UltimoUsuario}");
            rede.Encerrar();
        }
    }

    /// <summary>
    /// O register_callback() do BluetoothSerial: o "Client Connected" dos
    /// tutoriais, com o celular do painel e com um programa do computador.
    /// </summary>
    static void Rede_BluetoothNaAula()
    {
        saida.AppendLine("Bluetooth na aula");
        var rede = new Rede { UsarInternet = false };
        var (maq, interp) = MontarPlaca(rede, "ESP32-bt", """
            #include "BluetoothSerial.h"
            BluetoothSerial SerialBT;

            void callback(esp_spp_cb_event_t event, esp_spp_cb_param_t *param) {
              if (event == ESP_SPP_SRV_OPEN_EVT) { Serial.println("conectou"); digitalWrite(2, HIGH); }
              if (event == ESP_SPP_CLOSE_EVT) { Serial.println("saiu"); digitalWrite(2, LOW); }
            }

            void setup() {
              Serial.begin(115200);
              pinMode(2, OUTPUT);
              SerialBT.register_callback(callback);
              SerialBT.begin("ESP32-Aula");
            }

            void loop() {
              if (SerialBT.available()) Serial.println("leu: " + SerialBT.readString());
              delay(10);
            }
            """);
        interp.Setup();

        rede.ConectarCelular("ESP32-Aula");
        interp.Loop();
        bool acendeu = maq.Digital[2] == 1;
        rede.EnviarDoCelular("oi");
        interp.Loop();
        rede.DesconectarCelular();
        interp.Loop();

        var linhas = LinhasDoSerial(maq);
        saida.AppendLine($"        celular: {string.Join(" | ", linhas)}");
        Conferir("bt: register_callback avisa quando o celular conecta (ESP_SPP_SRV_OPEN_EVT)",
            linhas.FirstOrDefault() == "conectou" && acendeu, string.Join("|", linhas));
        Conferir("bt: o que o celular manda chega depois do aviso", linhas.Contains("leu: oi"),
            string.Join("|", linhas));
        Conferir("bt: e avisa quando ele sai (ESP_SPP_CLOSE_EVT)",
            linhas.LastOrDefault() == "saiu" && maq.Digital[2] == 0, string.Join("|", linhas));

        // Um programa do computador na porta do Bluetooth: para o sketch, e
        // um celular pareado como outro qualquer.
        long visto = 0;
        var anuncio = rede.Novos(ref visto).Select(e => e.Texto).FirstOrDefault(t => t.Contains("127.0.0.1:"));
        int porta = anuncio is null ? 0
            : int.Parse(System.Text.RegularExpressions.Regex.Match(anuncio, @"127\.0\.0\.1:(\d+)").Groups[1].Value);
        Conferir("bt: a porta do computador foi anunciada", porta > 0, anuncio);

        if (porta > 0)
        {
            using var tcp = new TcpClient();
            tcp.Connect(IPAddress.Loopback, porta);
            bool conectou = EsperarPor(() =>
            {
                interp.Loop();
                return LinhasDoSerial(maq).Count(l => l == "conectou") >= 2;
            }, 4000);
            tcp.GetStream().Write("abc"u8);
            bool leu = EsperarPor(() =>
            {
                interp.Loop();
                return LinhasDoSerial(maq).Contains("leu: abc");
            }, 4000);
            tcp.Close();
            bool saiu = EsperarPor(() =>
            {
                interp.Loop();
                return LinhasDoSerial(maq).Count(l => l == "saiu") >= 2;
            }, 4000);

            Conferir("bt: o programa do computador conectando tambem avisa, e o dado chega",
                conectou && leu && saiu, string.Join("|", LinhasDoSerial(maq)));
        }

        rede.Encerrar();

        // O radio de verdade do computador: com ele ligado, o servico abre e
        // fecha; desligado ou ausente, a placa continua e o painel diz por que.
        var comRadio = new Rede { UsarInternet = false, UsarBluetoothDoPc = true };
        var (_, interpRadio) = MontarPlaca(comRadio, "ESP32-radio", """
            #include "BluetoothSerial.h"
            BluetoothSerial SerialBT;
            void setup() { SerialBT.begin("ESP32-Radio"); }
            void loop() {}
            """);
        interpRadio.Setup();
        long vistoRadio = 0;
        string doRadio = comRadio.Novos(ref vistoRadio).Select(e => e.Texto)
            .FirstOrDefault(t => t.StartsWith("celular de verdade"));
        saida.AppendLine($"        radio do computador: {doRadio}");
        comRadio.Encerrar();
        Conferir("bt: o radio do computador abre o servico ou diz por que nao",
            doRadio is not null && (doRadio.Contains("CircuitSketch ESP32-Radio") || doRadio.Contains("não dá agora")),
            doRadio ?? "sem aviso");
    }
}
