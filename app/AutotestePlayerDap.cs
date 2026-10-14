using System.IO;
using System.Text;

namespace Simulador.App;

/// <summary>
/// O player de musica: a demo esp32s3-player-dap, com a biblioteca PlayerDAP
/// no modo nativo. A biblioteca lida de uma pasta do computador servindo de
/// cartao e de outra servindo de pendrive, as categorias pelas etiquetas, o
/// som no alto-falante, o pendrive saindo e voltando, a bateria lida pelo
/// divisor de resistores no ADC e a chave de liga/desliga, que poe a placa
/// para dormir e a acorda.
/// </summary>
public static partial class Autoteste
{
    static void PlayerDap_Pecas()
    {
        saida.AppendLine("player: o pendrive, a pasta do cartao e o ADC no divisor");

        // ---- a peca do pendrive ----
        var def = Catalogo.Obter("pendrive-usb");
        Conferir("pendrive: a peca esta no catalogo, com VBUS, D-, D+ e GND",
            def is not null && def.Pinos.Select(p => p.Nome).SequenceEqual(["VBUS", "D-", "D+", "GND"]),
            def is null ? "sem peca" : string.Join(",", def.Pinos.Select(p => p.Nome)));
        Conferir("pendrive: tem o desenho partido (o pendrive sai do conector)",
            Pecas.Existe("pendrive-usb-corpo") && Pecas.Existe("pendrive-usb-movel"));

        // ---- so a S3 (e a S2) tem USB OTG, e nos pinos dele ----
        string ProblemaCom(string tipoDaPlaca, string menos, string mais, string vbus = "5V")
        {
            var circuito = new Circuito();
            circuito.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = tipoDaPlaca, X = 100, Y = 100 });
            circuito.Pecas.Add(new PecaNoCircuito { Id = "pd", Tipo = "pendrive-usb", X = 400, Y = 100 });
            circuito.Fios.Add(new Fio { DePeca = "pd", DePino = "D-", ParaPeca = "placa", ParaPino = menos });
            circuito.Fios.Add(new Fio { DePeca = "pd", DePino = "D+", ParaPeca = "placa", ParaPino = mais });
            if (vbus is not null) circuito.Fios.Add(new Fio { DePeca = "pd", DePino = "VBUS", ParaPeca = "placa", ParaPino = vbus });
            circuito.Fios.Add(new Fio { DePeca = "pd", DePino = "GND", ParaPeca = "placa", ParaPino = "GND" });
            var editor = new EditorCircuito();
            editor.Carregar(circuito);
            var maq = new Maquina { Perfil = PerfilPlaca.De(tipoDaPlaca) };
            maq.Reiniciar();
            var bancada = Simulador.App.Bancada.DeUmaSo(maq);
            var achado = editor.PendrivesDoCircuito(bancada, new Netlist(circuito)).Single();
            return achado.Problema ?? "ok";
        }
        string certo = ProblemaCom("esp32-s3-devkit", "19", "20");
        string trocados = ProblemaCom("esp32-s3-devkit", "20", "19");
        string semVbus = ProblemaCom("esp32-s3-devkit", "19", "20", vbus: null);
        string naEsp32 = ProblemaCom("esp32-devkit-v1", "D19", "D21");
        saida.AppendLine($"        S3 certo: {certo} | trocados: {trocados} | sem VBUS: {semVbus} | ESP32: {naEsp32}");
        Conferir("pendrive: no GPIO19/20 da S3, com 5 V, a placa ve o pendrive", certo == "ok", certo);
        Conferir("pendrive: D- e D+ trocados nao funcionam, e o aviso diz os pinos", trocados.Contains("GPIO19"), trocados);
        Conferir("pendrive: sem os 5 V no VBUS nao funciona", semVbus.Contains("VBUS"), semVbus);
        Conferir("pendrive: a ESP32 comum nao tem USB OTG", naEsp32.Contains("USB OTG"), naEsp32);

        // ---- a pasta do computador vai junto na copia da peca ----
        var cartao = new PecaNoCircuito { Tipo = "microsd-card", PastaDoComputador = @"C:\Musica" };
        Conferir("cartao: a pasta do computador vai junto ao copiar a peca", cartao.Copia().PastaDoComputador == @"C:\Musica");

        // ---- analogReadMilliVolts: existe no core da ESP32, e nao no AVR ----
        const string mv = """
            void setup() {
              Serial.begin(115200);
              Serial.println(analogReadMilliVolts(34));
            }
            void loop() {}
            """;
        var (_, serialEsp, erroEsp) = RodarComPinos(mv, "esp32-devkit-v1", 2000);
        var (_, _, erroUno) = RodarComPinos(mv.Replace("34", "A0"), "arduino-uno", 2000);
        Conferir("adc: analogReadMilliVolts() roda na ESP32 (e devolve milivolts)", erroEsp is null && serialEsp.Contains("0"),
            erroEsp ?? string.Join("|", serialEsp));
        Conferir("adc: analogReadMilliVolts() nao existe no core do AVR", erroUno?.Contains("analogReadMilliVolts") == true, erroUno ?? "rodou");
    }

    /// <summary>
    /// Um WAV curto -- um tom de 0,6 s a 22 kHz -- com as etiquetas como os
    /// programas do computador gravam: o LIST/INFO (titulo, artista, album,
    /// genero, ano, faixa) e um "id3 " com o que o INFO nao tem (artista do
    /// album, compositor), em ID3v2.4 com UTF-8.
    /// </summary>
    static void GravarWavDeTeste(string caminho, double hz, Dictionary<string, string> info,
                                 Dictionary<string, string> id3 = null)
    {
        const int taxa = 22050;
        int quadros = (int)(taxa * 0.6);
        var dados = new byte[quadros * 2];
        for (int i = 0; i < quadros; i++)
        {
            short v = (short)(Math.Sin(2 * Math.PI * hz * i / taxa) * 12000);
            dados[2 * i] = (byte)v;
            dados[2 * i + 1] = (byte)(v >> 8);
        }

        using var corpo = new MemoryStream();
        void Pedaco(string id, byte[] conteudo)
        {
            corpo.Write(Encoding.ASCII.GetBytes(id));
            corpo.Write(BitConverter.GetBytes(conteudo.Length));
            corpo.Write(conteudo);
            if ((conteudo.Length & 1) != 0) corpo.WriteByte(0);
        }

        var fmt = new byte[16];
        BitConverter.GetBytes((short)1).CopyTo(fmt, 0);      // PCM
        BitConverter.GetBytes((short)1).CopyTo(fmt, 2);      // mono
        BitConverter.GetBytes(taxa).CopyTo(fmt, 4);
        BitConverter.GetBytes(taxa * 2).CopyTo(fmt, 8);
        BitConverter.GetBytes((short)2).CopyTo(fmt, 12);
        BitConverter.GetBytes((short)16).CopyTo(fmt, 14);
        Pedaco("fmt ", fmt);
        Pedaco("data", dados);

        var lista = new MemoryStream();
        lista.Write("INFO"u8);
        foreach (var (id, texto) in info)
        {
            var b = Encoding.UTF8.GetBytes(texto + "\0");
            lista.Write(Encoding.ASCII.GetBytes(id));
            lista.Write(BitConverter.GetBytes(b.Length));
            lista.Write(b);
            if ((b.Length & 1) != 0) lista.WriteByte(0);
        }
        Pedaco("LIST", lista.ToArray());

        if (id3 is { Count: > 0 })
        {
            static byte[] Seguro(int n) => [(byte)((n >> 21) & 0x7F), (byte)((n >> 14) & 0x7F), (byte)((n >> 7) & 0x7F), (byte)(n & 0x7F)];
            var molduras = new MemoryStream();
            foreach (var (id, texto) in id3)
            {
                var b = Encoding.UTF8.GetBytes(texto);
                molduras.Write(Encoding.ASCII.GetBytes(id));
                molduras.Write(Seguro(b.Length + 1));
                molduras.Write(new byte[2]);
                molduras.WriteByte(3);                       // UTF-8
                molduras.Write(b);
            }
            var tag = new MemoryStream();
            tag.Write("ID3"u8);
            tag.Write(new byte[] { 4, 0, 0 });
            tag.Write(Seguro((int)molduras.Length));
            molduras.WriteTo(tag);
            Pedaco("id3 ", tag.ToArray());
        }

        Directory.CreateDirectory(Path.GetDirectoryName(caminho));
        using var arquivo = File.Create(caminho);
        arquivo.Write("RIFF"u8);
        arquivo.Write(BitConverter.GetBytes((int)corpo.Length + 4));
        arquivo.Write("WAVE"u8);
        corpo.WriteTo(arquivo);
    }

    /// <summary>Um arquivo de teste embutido no programa (os tons de assets/testes).</summary>
    static byte[] ArquivoDeTeste(string nome)
    {
        using var s = typeof(Autoteste).Assembly.GetManifestResourceStream("testes/" + nome);
        if (s is null) return null;
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    /// <summary>
    /// Os formatos que moram fora do dr_libs: ALAC (no M4A), Opus e WavPack.
    /// Tres tons curtos, com etiquetas (as do iTunes, as do Ogg e as APEv2),
    /// tocados em sequencia. Que o decodificador acerta amostra por amostra,
    /// quem confere e o teste dos fontes no computador; aqui e a ligacao: o
    /// formato reconhecido, as etiquetas lidas e o som saindo.
    /// </summary>
    static void PlayerDap_Formatos()
    {
        saida.AppendLine("player: ALAC (M4A), Opus e WavPack");
        string raiz = Path.Combine(Path.GetTempPath(), "circuitsketch-teste-formatos-" + Guid.NewGuid().ToString("N")[..8]);
        string pasta = Path.Combine(raiz, "Formatos");
        Directory.CreateDirectory(pasta);
        foreach (string nome in new[] { "tom.m4a", "tom.opus", "tom.wv" })
        {
            byte[] bytes = ArquivoDeTeste(nome);
            Conferir($"formatos: o {nome} de teste vem com o programa", bytes is { Length: > 1000 });
            if (bytes is null) return;
            File.WriteAllBytes(Path.Combine(pasta, nome), bytes);
        }

        var janela = new MainWindow();
        try
        {
            janela.TesteAbrirDemo("esp32s3-player-dap");
            var pecas = janela.TesteEditorCircuito.Atual.Pecas;
            var placa = pecas.First(p => p.Tipo == "esp32-s3-devkit");
            var falante = pecas.First(p => p.Tipo == "alto-falante");
            pecas.First(p => p.Tipo == "microsd-card").PastaDoComputador = raiz;

            janela.TesteRodar();
            var maquina = janela.TesteMaquinaDa(placa.Id);
            string Serial() => string.Join(" | ", LinhasDoSerial(maquina).TakeLast(8));
            bool leu = EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.Contains(" artistas, ")), 40000);
            string resumo = LinhasDoSerial(maquina).LastOrDefault(l => l.Contains(" artistas, ")) ?? "";
            saida.AppendLine($"        {resumo}");
            Conferir("formatos: os tres entram na biblioteca", leu && LinhasDoSerial(maquina).Any(l => l.Contains("3 musicas na biblioteca")), Serial());
            Conferir("formatos: as etiquetas do M4A, do Opus e do WavPack (um artista, um album, um ano)",
                resumo.Contains("1 artistas, 1 albuns, 1 artistas de album, 1 generos, 1 anos"), resumo);

            // A PAUSA toca todas, em ordem de titulo: ALAC, Opus e WavPack (0,6 s cada).
            janela.TesteEditorCircuito.BotoesPressionados.Add("pausa");
            EsperarPor(() => false, 150);
            janela.TesteEditorCircuito.BotoesPressionados.Remove("pausa");
            double pico = 0;
            EsperarPor(() =>
            {
                var (audio, _) = janela.TesteEditorCircuito.SomDoAltoFalante(falante, janela.TesteBancada, janela.TesteRede);
                pico = Math.Max(pico, audio?.Nivel ?? 0);
                return false;
            }, 4000);
            saida.AppendLine($"        som: pico {pico:0.000}");
            Conferir("formatos: o som dos tres sai no alto-falante", pico > 0.005, $"{pico:0.000}");
            var erros = LinhasDoSerial(maquina).Where(l => l.StartsWith("DAP: /")).ToList();
            Conferir("formatos: nenhum dos tres falhou ao abrir", erros.Count == 0, string.Join(" | ", erros));
        }
        finally
        {
            janela.TesteParar();
            try { janela.Close(); } catch (InvalidOperationException) { }
            try { Directory.Delete(raiz, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    static void Demo_PlayerDap()
    {
        saida.AppendLine("demo PLAYER DE MUSICA (DAP)");

        // As musicas do teste: tons curtos com etiquetas, numa pasta do
        // computador que vira o cartao, e outra que vira o pendrive.
        string raiz = Path.Combine(Path.GetTempPath(), "circuitsketch-teste-dap-" + Guid.NewGuid().ToString("N")[..8]);
        string pastaDoCartao = Path.Combine(raiz, "cartao"), pastaDoPendrive = Path.Combine(raiz, "pendrive");
        string album = Path.Combine(pastaDoCartao, "Musicas", "Banda Azul", "Primeiro Disco");
        GravarWavDeTeste(Path.Combine(album, "01 Abertura.wav"), 440,
            new() { ["INAM"] = "Abertura", ["IART"] = "Banda Azul", ["IPRD"] = "Primeiro Disco", ["IGNR"] = "Rock", ["ICRD"] = "2019", ["ITRK"] = "1" },
            new() { ["TPE2"] = "Banda Azul", ["TCOM"] = "Ana Lima" });
        GravarWavDeTeste(Path.Combine(album, "02 Segunda.wav"), 523,
            new() { ["INAM"] = "Segunda", ["IART"] = "Banda Azul", ["IPRD"] = "Primeiro Disco", ["IGNR"] = "Rock", ["ICRD"] = "2019", ["ITRK"] = "2" },
            new() { ["TPE2"] = "Banda Azul", ["TCOM"] = "Bruno Reis" });
        GravarWavDeTeste(Path.Combine(pastaDoCartao, "Musicas", "Coletânea", "03 Convidada.wav"), 659,
            new() { ["INAM"] = "Convidada", ["IART"] = "Cantora Verde", ["IPRD"] = "Coletânea", ["IGNR"] = "Jazz", ["ICRD"] = "2021", ["ITRK"] = "3" },
            new() { ["TPE2"] = "Vários Artistas", ["TCOM"] = "Ana Lima" });
        GravarWavDeTeste(Path.Combine(pastaDoPendrive, "Avulsas", "04 Do Pendrive.wav"), 784,
            new() { ["INAM"] = "Do Pendrive", ["IART"] = "Banda Azul", ["IPRD"] = "Avulsas", ["IGNR"] = "Rock", ["ICRD"] = "2020" });
        Directory.CreateDirectory(Path.Combine(pastaDoCartao, "Playlists"));
        File.WriteAllText(Path.Combine(pastaDoCartao, "Playlists", "Viagem.m3u8"),
            "#EXTM3U\r\n..\\Musicas\\Coletânea\\03 Convidada.wav\r\n..\\Musicas\\Banda Azul\\Primeiro Disco\\01 Abertura.wav\r\n",
            new UTF8Encoding(true));

        var janela = new MainWindow();
        try
        {
            janela.TesteAbrirDemo("esp32s3-player-dap");
            var pecas = janela.TesteEditorCircuito.Atual.Pecas;
            var placa = pecas.First(p => p.Tipo == "esp32-s3-devkit");
            var cartao = pecas.First(p => p.Tipo == "microsd-card");
            var pendrive = pecas.First(p => p.Tipo == "pendrive-usb");
            var bateria = pecas.First(p => p.Tipo == "bateria");
            var chave = pecas.First(p => p.Id == "chave");
            var falante = pecas.First(p => p.Tipo == "alto-falante");
            cartao.PastaDoComputador = pastaDoCartao;
            pendrive.PastaDoComputador = pastaDoPendrive;

            janela.TesteRodar();
            var maquina = janela.TesteMaquinaDa(placa.Id);
            string Serial() => string.Join(" | ", LinhasDoSerial(maquina).TakeLast(8));
            int Leituras() => LinhasDoSerial(maquina).Count(l => l.Contains("musicas na biblioteca"));

            // ---- a biblioteca: o cartao e o pendrive, pelas etiquetas ----
            bool leu = EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.Contains(" artistas, ")), 40000);
            string resumo = LinhasDoSerial(maquina).LastOrDefault(l => l.Contains(" artistas, ")) ?? "";
            saida.AppendLine($"        {string.Join(" | ", LinhasDoSerial(maquina).Take(6))}");
            Conferir("player: le o cartao e o pendrive (as quatro musicas)",
                leu && LinhasDoSerial(maquina).Any(l => l.Contains("4 musicas na biblioteca")),
                Serial() + " | console: " + string.Join(" / ", janela.TesteConsole.Split('\n').TakeLast(6))
                + " | avisos: " + string.Join(" / ", maquina.Avisos.ToList()));
            Conferir("player: o pendrive no USB OTG da S3 e reconhecido",
                LinhasDoSerial(maquina).Any(l => l.Contains("pendrive de")), Serial());
            Conferir("player: as categorias vem das etiquetas (INFO e ID3 dentro do WAV)",
                resumo.Contains("2 artistas, 3 albuns, 2 artistas de album, 2 generos, 3 anos, 3 compositores, 1 playlists"), resumo);

            // A tela desenhada: a lista do menu, em varias cores.
            EsperarPor(() => (maquina.Tela?.Pixels.Distinct().Count() ?? 0) > 20, 8000);
            int cores = maquina.Tela?.Pixels.Distinct().Count() ?? 0;
            Conferir("player: o menu aparece na tela ILI9341 em pe", cores > 20 && maquina.Tela.Largura == 240, $"{cores} cores");

            // ---- a bateria: 3,9 V no divisor de 100k + 100k no GPIO 2 ----
            EsperarPor(() => maquina.Analogico[2] > 0, 3000);
            int esperado = (int)Math.Round(3.9 / 2 / 3.3 * 4095);
            Conferir("bateria: o ADC le a metade da bateria pelo divisor de resistores",
                Math.Abs(maquina.Analogico[2] - esperado) <= 12, $"{maquina.Analogico[2]} (esperado ~{esperado})");

            // ---- a PAUSA, sem nada tocando, toca todas: o som no alto-falante ----
            void Apertar(string id, int ms = 120)
            {
                janela.TesteEditorCircuito.BotoesPressionados.Add(id);
                EsperarPor(() => false, ms);
                janela.TesteEditorCircuito.BotoesPressionados.Remove(id);
                EsperarPor(() => false, 400);
            }
            Apertar("pausa");
            double pico = 0;
            EsperarPor(() =>
            {
                var (audio, _) = janela.TesteEditorCircuito.SomDoAltoFalante(falante, janela.TesteBancada, janela.TesteRede);
                pico = Math.Max(pico, audio?.Nivel ?? 0);
                return pico > 0.005;
            }, 10000);
            saida.AppendLine($"        som no alto-falante: pico {pico:0.000}");
            Conferir("player: a PAUSA toca a biblioteca, e o som sai pelo MAX98357A", pico > 0.005, $"{pico:0.000}");

            // ---- a roda: um clique muda a tela ----
            int antes = maquina.Tela.Pixels.Aggregate(17, (h, p) => h * 31 + p);
            var roda = pecas.First(p => p.Id == "roda");
            Apertar("voltar");
            janela.TesteEditorCircuito.DefinirGiro(roda, roda.Giro + 0.1);
            EsperarPor(() => false, 800);
            int depois = maquina.Tela.Pixels.Aggregate(17, (h, p) => h * 31 + p);
            Conferir("player: VOLTAR e a roda mexem na tela", antes != depois);

            // ---- o pendrive sai: a biblioteca e lida de novo, sem ele ----
            janela.TesteEditorCircuito.DefinirGiro(pendrive, 1);
            bool saiu = EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.Contains("o pendrive saiu"))
                                         && LinhasDoSerial(maquina).Any(l => l.Contains("3 musicas na biblioteca")), 15000);
            Conferir("pendrive: tirado com o player ligado, a biblioteca fica so com o cartao", saiu, Serial());
            janela.TesteEditorCircuito.DefinirGiro(pendrive, 0);
            bool voltou = EsperarPor(() => LinhasDoSerial(maquina).Count(l => l.Contains("4 musicas na biblioteca")) >= 2, 15000);
            Conferir("pendrive: posto de volta, as musicas dele voltam", voltou, Serial());

            // ---- a bateria acaba: o player guarda onde parou e a placa dorme ----
            bateria.Volts = 3.2;
            bool vazia = EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.Contains("Bateria vazia")), 8000);
            Conferir("bateria: abaixo de 3,3 V o player desliga e a placa dorme", vazia, Serial());

            // ---- carregada, desliga e liga a chave: a placa acorda e o player volta ----
            bateria.Volts = 4.0;
            EsperarPor(() => false, 1500);
            janela.TesteEditorCircuito.DefinirGiro(chave, 0);
            bool dormiu = EsperarPor(() => LinhasDoSerial(maquina).Any(l => l.Contains("dorme ate a chave ligar")), 15000);
            janela.TesteEditorCircuito.DefinirGiro(chave, 1);
            int leiturasAntes = Leituras();
            bool acordou = EsperarPor(() => Leituras() > leiturasAntes || LinhasDoSerial(maquina).Any(l => l.Contains("musicas na biblioteca")), 40000);
            Conferir("chave: desligada a placa dorme, e ligada ela acorda e o player volta", dormiu && acordou, Serial());
        }
        finally
        {
            janela.TesteParar();
            try { janela.Close(); } catch (InvalidOperationException) { }
            try { Directory.Delete(raiz, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
