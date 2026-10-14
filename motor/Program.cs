using Simulador;

// ============================================================
//  Spike do motor de execucao.
//
//  Roda um sketch .ino de verdade sem compilar C++ e imprime a
//  linha do tempo dos pinos. O relogio e VIRTUAL: delay() adianta
//  o tempo em vez de dormir, entao a execucao e instantanea e
//  deterministica -- que e o que torna isto util como teste.
//
//  Vale para os dois: .ino em C++ do Arduino e .py em MicroPython.
//  Quem decide e o Compilador, pelo proprio conteudo.
//
//  uso: motor <arquivo.ino | arquivo.py | pasta> [ms]
//             [--placa id] [--bibliotecas pasta] [--tela arquivo.png]
//             [--cartao pasta] [--pendrive pasta[@sai_ms[@volta_ms]]]
//
//  --bibliotecas aponta para uma pasta como a "libraries" da Arduino
//  IDE: cada subpasta e uma biblioteca, e o #include acha os .h dela.
//  --tela grava o display TFT como ficou no fim, do jeito que o sketch
//  o ve (ja girado pelo setRotation). --cartao poe um cartao microSD
//  ligado na placa, com os arquivos da pasta (lidos so quando o sketch
//  abre; a pasta nunca muda). --pendrive faz o mesmo com um pendrive no
//  USB da placa, e pode tirar e por de volta no meio da rodada.
// ============================================================

if (args.Length == 0)
{
    Console.Error.WriteLine("uso: motor <arquivo.ino | arquivo.py | pasta> [ms a simular] "
                            + "[--placa id] [--bibliotecas pasta] [--tela arquivo.png] [--cartao pasta] "
                            + "[--pendrive pasta[@sai_ms[@volta_ms]]]");
    return 1;
}

string placa = null, pastaDeBibliotecas = null, arquivoDaTela = null, pastaDoCartao = null, aparelhos = null;
string serialDeEntrada = null, apertos = null, arquivoDeAudio = null, pastaDoPendrive = null;
bool aoVivo = false, internet = false;
var posicionais = new List<string>();
for (int k = 0; k < args.Length; k++)
{
    if (args[k] == "--placa" && k + 1 < args.Length) placa = args[++k];
    else if (args[k] == "--bibliotecas" && k + 1 < args.Length) pastaDeBibliotecas = args[++k];
    else if (args[k] == "--tela" && k + 1 < args.Length) arquivoDaTela = args[++k];
    else if (args[k] == "--cartao" && k + 1 < args.Length) pastaDoCartao = args[++k];
    else if (args[k] == "--pendrive" && k + 1 < args.Length) pastaDoPendrive = args[++k];
    else if (args[k] == "--aparelhos" && k + 1 < args.Length) aparelhos = args[++k];
    else if (args[k] == "--nativo" && k + 1 < args.Length) ModuloNativo.Pasta = args[++k];
    else if (args[k] == "--digitar" && k + 1 < args.Length) serialDeEntrada = args[++k];
    else if (args[k] == "--apertar" && k + 1 < args.Length) apertos = args[++k];
    else if (args[k] == "--audio" && k + 1 < args.Length) arquivoDeAudio = args[++k];
    else if (args[k] == "--ao-vivo") aoVivo = true;
    else if (args[k] == "--internet") internet = true;
    else posicionais.Add(args[k]);
}

string alvo = posicionais[0];
long msTotal = posicionais.Count > 1 && long.TryParse(posicionais[1], out var m) ? m : 10_000;

string arquivo = Directory.Exists(alvo)
    ? Directory.GetFiles(alvo, "*.ino").FirstOrDefault()
      ?? Directory.GetFiles(alvo, "*.py").FirstOrDefault()
    : alvo;

if (arquivo is null || !File.Exists(arquivo))
{
    Console.Error.WriteLine($"nao achei nenhum .ino nem .py em: {alvo}");
    return 1;
}

string fonte = File.ReadAllText(arquivo);
var maq = new Maquina();
var serialDeAntes = new List<string>();
var eventosDeAntes = new List<EventoPino>();
long gasto = 0;
if (placa is not null)
{
    // Um id errado rodava na ESP32 sem avisar -- e o teste media a placa errada.
    if (PerfilPlaca.De(placa) is not { } perfil)
    {
        Console.Error.WriteLine($"placa desconhecida: '{placa}'. As que existem: "
                                + string.Join(", ", PerfilPlaca.Conhecidos.Select(p => p.Id)));
        return 2;
    }
    maq.Perfil = perfil;
}
// O cartao e a pasta, com as subpastas: lida do disco quando o sketch abre, e
// o que ele grava fica so no cartao simulado.
if (pastaDoCartao is not null && Directory.Exists(pastaDoCartao))
{
    maq.Cartao.Presente = true;
    maq.Cartao.PastaDoComputador = Path.GetFullPath(pastaDoCartao);
}

// Um pendrive na USB host da placa, com a pasta dentro. "--pendrive
// pasta@8000@12000" tira o pendrive aos 8 s e poe de volta aos 12 s (no
// relogio da rodada); sem o segundo numero, ele nao volta.
if (pastaDoPendrive is not null)
{
    var partes = pastaDoPendrive.Split('@');
    long sai = partes.Length > 1 && long.TryParse(partes[1], out long s) ? s : long.MaxValue;
    long volta = partes.Length > 2 && long.TryParse(partes[2], out long v) ? v : long.MaxValue;
    if (Directory.Exists(partes[0]))
    {
        maq.Pendrive.PastaDoComputador = Path.GetFullPath(partes[0]);
        maq.PendriveConectado = () => gasto + maq.Ms < sai || gasto + maq.Ms >= volta;
    }
    else Console.WriteLine($"pendrive  : a pasta {partes[0]} nao existe");
}

// "--aparelhos ili9341:cs=10,dc=9,rst=8,mosi=11,sck=12;sd:cs=14": o que o
// circuito teria no barramento SPI.
if (aparelhos is not null)
{
    var lista = new List<AparelhoNoSpi>();
    foreach (string item in aparelhos.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        var partes = item.Split(':', 2);
        var campos = (partes.Length > 1 ? partes[1] : "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Split('='))
            .Where(c => c.Length == 2)
            .ToDictionary(c => c[0].Trim(), c => int.Parse(c[1].Trim()));
        int C(string n) => campos.TryGetValue(n, out int v) ? v : -1;
        lista.Add(new AparelhoNoSpi(partes[0].Trim(), C("cs"), C("dc"), C("rst"), C("mosi"), C("miso"), C("sck"), C("luz")));
    }
    maq.AparelhosSpi = () => lista;
}
// "--internet": a placa sai pela internet do computador (broker, HTTP). Sem
// isto, como nos testes, nada sai daqui.
if (internet) maq.Rede.UsarInternet = true;
if (serialDeEntrada is not null) maq.EntradaSerial.Adicionar(serialDeEntrada.Replace("\\n", "\n"));

// "--apertar 6000:17:6300,10000:4:11500": um botao ligado ao GND, apertado
// do ms tal ate o ms tal (no relogio da rodada) -- os botoes sem a janela.
List<long[]> listaDeApertos = apertos is null ? [] : apertos.Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(a => a.Split(':').Select(long.Parse).ToArray())
    .Where(a => a.Length == 3)
    .ToList();
if (listaDeApertos.Count > 0)
{
    var lista = listaDeApertos;
    new Thread(() =>
    {
        var feito = new int[lista.Count];   // 0: esperando, 1: apertado, 2: solto
        while (feito.Any(f => f < 2))
        {
            long agora = gasto + maq.Ms;
            for (int i = 0; i < lista.Count; i++)
            {
                int pino = (int)lista[i][1];
                if (feito[i] == 0 && agora >= lista[i][0]) { maq.NivelExterno[pino] = 0; feito[i] = 1; }
                if (feito[i] == 1 && agora >= lista[i][2]) { maq.NivelExterno[pino] = -1; feito[i] = 2; }
            }
            Thread.Sleep(2);
        }
    }) { IsBackground = true }.Start();
}

// "--audio som.wav": grava o que a placa tocou pelo I2S, lendo da mesma
// saida que a janela toca.
var somGravado = new List<short>();
int taxaDoSom = 0;
if (arquivoDeAudio is not null)
    new Thread(() =>
    {
        var bloco = new short[4096];
        while (true)
        {
            if (maq.Audio is { } saida)
                for (int n; (n = saida.Ler(bloco)) > 0;)
                    lock (somGravado)
                    {
                        for (int i = 0; i < n; i++) somGravado.Add(bloco[i]);
                        taxaDoSom = saida.Taxa;
                    }
            Thread.Sleep(20);
        }
    }) { IsBackground = true }.Start();

// "--ao-vivo": o monitor serial sai enquanto roda (para quem acompanha um
// programa longo, ou um que derruba o processo).
if (aoVivo)
    new Thread(() =>
    {
        int visto = 0;
        long vistoNaRede = 0;
        while (true)
        {
            lock (maq.Trava)
            {
                if (visto > maq.Serial.Count) visto = 0;
                for (; visto < maq.Serial.Count; visto++) Console.Error.WriteLine("  | " + maq.Serial[visto]);
            }
            // E o que o painel Rede mostraria (a porta do servidor, o WiFi).
            foreach (var ev in maq.Rede.Novos(ref vistoNaRede)) Console.Error.WriteLine($"  ~ {ev.Tipo}: {ev.Texto}");
            Thread.Sleep(30);
        }
    }) { IsBackground = true }.Start();

Console.WriteLine($"sketch    : {Path.GetFileName(arquivo)}");
Console.WriteLine($"simulando : {msTotal} ms virtuais");
Console.WriteLine(new string('-', 62));

try
{
    var linguagem = Compilador.Detectar(fonte);
    var prog = Compilador.CompilarProjeto(new Projeto
    {
        Sketch = fonte,
        Placa = placa,
        Arquivos = AbasDaPasta(arquivo),
        Bibliotecas = pastaDeBibliotecas is null ? [] : BibliotecasDaPasta(pastaDeBibliotecas),
    });
    if (prog.Nativos.Count > 0)
        Console.WriteLine($"nativo    : {prog.Nativos.Count(f => f.Nome.EndsWith(".c"))} arquivo(s) .c compilados pelo TinyCC");
    foreach (string aviso in prog.Avisos) Console.WriteLine("aviso     : " + aviso);

    Console.WriteLine($"linguagem : {Compilador.Nome(linguagem)}");
    Console.WriteLine($"globais   : {prog.Globais.Count}   funcoes: " +
                      string.Join(", ", prog.Funcoes.Select(f => f.Nome + "()")));
    Console.WriteLine(new string('-', 62));

    // ESP.restart() e o despertar do sono profundo comecam uma rodada
    // nova, do setup(). O millis() volta a zero, como na placa -- entao o
    // tempo das rodadas anteriores fica guardado a parte, e a serial e os
    // pinos delas tambem.
    ReinicioDaPlaca origem = null;
    while (true)
    {
        var interp = new Interpretador(prog, maq) { Origem = origem };
        maq.LimiteDoRelogioVirtual = Math.Max(1, msTotal - gasto);
        try
        {
            interp.Preparar();

            // Codigo nativo roda em threads de verdade: o relogio e o real, e
            // o tempo pedido e contado no relogio da parede.
            if (maq.Nativo is not null)
            {
                maq.TempoReal = true;
                long resta = Math.Max(1, msTotal - gasto);
                var alarme = new System.Threading.Timer(_ => maq.Parar = true, null, resta, Timeout.Infinite);
            }
            interp.Setup();

            if (!interp.TemLoop)
            {
                if (origem is null) Console.WriteLine("aviso: sketch sem loop(), rodei so o setup().");
                break;
            }

            long antes = maq.Ms;
            while (maq.Ms + gasto < msTotal && !maq.Parar)
            {
                interp.Loop();
                // Sketch no padrao millis() nao adianta o relogio sozinho:
                // cada volta do loop custa 1 ms virtual.
                if (maq.Ms == antes) maq.Avancar(1);
                antes = maq.Ms;
            }

            // O codigo nativo pediu para reiniciar entre uma volta e outra.
            if (maq.ReinicioPedido is { } pedido) throw pedido;
            break;
        }
        catch (ReinicioDaPlaca r) when (gasto + maq.Ms < msTotal)
        {
            serialDeAntes.AddRange(maq.Serial);
            eventosDeAntes.AddRange(maq.Eventos.Select(e => e with { Ms = e.Ms + gasto }));

            // A placa leva o tempo do boot para chegar ao setup() de novo --
            // e e isso que faz um ciclo de reinicios andar no relogio.
            gasto += maq.Ms + maq.Perfil.MsDoBoot;
            serialDeAntes.Add($"[{gasto,7}ms] --- {r.Resumo} ---");
            if (gasto >= msTotal) break;
            origem = r;
            maq.Reiniciar();

            // O chip reiniciou; o circuito, nao: o botao que estava apertado
            // (a chave que acordou a placa) continua apertado no setup() novo.
            foreach (var a in listaDeApertos)
                if (gasto >= a[0] && gasto < a[2]) maq.NivelExterno[(int)a[1]] = 0;
        }
        // O tempo pedido acabou no meio de uma espera.
        catch (ExecucaoInterrompida) { break; }
    }
    maq.EncerrarNativo();
    foreach (string aviso in maq.Avisos.ToList()) { }
}
catch (ErroDeSintaxe e) { return Falhar("ERRO DE SINTAXE", e.Message); }
catch (ErroDeExecucao e) { return Falhar("ERRO DE EXECUCAO", e.Message); }

// ---------------- resultados ----------------
var eventos = eventosDeAntes.Concat(maq.Eventos.Select(e => e with { Ms = e.Ms + gasto })).ToList();
var serial = serialDeAntes.Concat(maq.Serial).ToList();

Console.WriteLine();
Console.WriteLine("LINHA DO TEMPO DOS PINOS");
if (eventos.Count == 0)
    Console.WriteLine("  (nenhuma mudanca de pino)");
else
    foreach (var ev in eventos.Take(40))
        Console.WriteLine($"  {ev.Ms,7} ms   GPIO {ev.Pino,2}  ->  {(ev.Valor == 1 ? "LIGA" : "desliga")}");

if (eventos.Count > 40)
    Console.WriteLine($"  ... mais {eventos.Count - 40} eventos");

if (maq.Lcd is { Ligado: true } lcd)
{
    Console.WriteLine();
    Console.WriteLine("LCD 16x2 (estado final)");
    Console.WriteLine("  +" + new string('-', lcd.Colunas) + "+");
    for (int i = 0; i < lcd.Linhas; i++)
        Console.WriteLine($"  |{lcd.LinhaTexto(i)}|");
    Console.WriteLine("  +" + new string('-', lcd.Colunas) + "+");
}

Console.WriteLine();
Console.WriteLine("MONITOR SERIAL");
if (serial.Count == 0) Console.WriteLine("  (vazio)");
else foreach (var l in serial.Take(40)) Console.WriteLine("  " + l);
if (serial.Count > 40) Console.WriteLine($"  ... mais {serial.Count - 40} linhas");

// O que o painel Rede mostraria: WiFi, broker, servidores.
long desdeOComeco = 0;
var daRede = maq.Rede.Novos(ref desdeOComeco);
if (daRede.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("REDE");
    foreach (var ev in daRede.Take(40)) Console.WriteLine($"  {ev.Tipo,-8} {ev.Texto}");
    if (daRede.Count > 40) Console.WriteLine($"  ... mais {daRede.Count - 40}");
}

if (maq.Avisos.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"AVISOS ({maq.Perfil.Nome})");
    foreach (var a in maq.Avisos) Console.WriteLine("  ! " + a);
}

{
    var ram = maq.Memoria;
    Console.WriteLine();
    Console.WriteLine($"memoria   : {ram.Estatica:N0} bytes de globais (com o core e os .c); heap {ram.HeapLivre:N0} livres "
                      + $"de {ram.HeapTotal:N0} (a menor foi {ram.MenorHeapLivre:N0}); a pilha do sketch chegou a {ram.MaiorPilha:N0} bytes");
}

if (arquivoDaTela is not null)
{
    // O OLED: o que o ultimo display() mandou para o vidro.
    var telaDoSketch = maq.Tela ?? maq.PainelOled;
    if (telaDoSketch is null) Console.WriteLine("tela      : o sketch nao usou display (TFT ou OLED)");
    else if (maq.Tela is null)
    {
        GravarPng(arquivoDaTela, maq.PainelOled);
        Console.WriteLine($"tela      : OLED {maq.PainelOled.Largura}x{maq.PainelOled.Altura} gravado em {arquivoDaTela}");
    }
    else
    {
        GravarPng(arquivoDaTela, maq.Tela);
        Console.WriteLine($"tela      : {maq.Tela.Largura}x{maq.Tela.Altura} gravada em {arquivoDaTela}"
                          + (maq.Tela.Mostrando ? "" : " (o vidro esta apagado: DISPOFF ou SLPIN; o PNG e a memoria dele)"));
        double quadros = maq.Tela.PixelsEscritos / (double)(maq.Tela.Largura * maq.Tela.Altura);
        if (quadros >= 1)
            Console.WriteLine($"quadros   : {quadros:0} telas inteiras de pixels em {msTotal / 1000.0:0.#} s "
                              + $"({quadros * 1000.0 / msTotal:0.0} por segundo)");
    }
}

if (arquivoDeAudio is not null)
{
    short[] amostras;
    lock (somGravado) amostras = [.. somGravado];
    if (amostras.Length == 0) Console.WriteLine("audio     : a placa nao tocou nada pelo I2S");
    else
    {
        GravarWav(arquivoDeAudio, amostras, taxaDoSom);
        int pico = amostras.Max(a => Math.Abs((int)a));
        Console.WriteLine($"audio     : {amostras.Length / (double)taxaDoSom:0.00} s a {taxaDoSom} Hz (pico {pico}) gravados em {arquivoDeAudio}");
    }
}

Console.WriteLine();
Console.WriteLine($"total: {eventos.Count} eventos de pino, {serial.Count} linhas de serial");
return 0;

// Um WAV de 16 bits, mono.
static void GravarWav(string caminho, short[] amostras, int taxa)
{
    using var w = new BinaryWriter(File.Create(caminho));
    w.Write("RIFF"u8);
    w.Write(36 + amostras.Length * 2);
    w.Write("WAVEfmt "u8);
    w.Write(16);
    w.Write((short)1);          // PCM
    w.Write((short)1);          // mono
    w.Write(taxa);
    w.Write(taxa * 2);
    w.Write((short)2);
    w.Write((short)16);
    w.Write("data"u8);
    w.Write(amostras.Length * 2);
    foreach (short a in amostras) w.Write(a);
}

// O display como o sketch o ve, num PNG de 8 bits por cor. Sem biblioteca
// de imagem: o PNG e um cabecalho, as linhas comprimidas com zlib e um CRC.
static void GravarPng(string caminho, TelaGrafica tela)
{
    int largura = tela.Largura, altura = tela.Altura;
    var cru = new MemoryStream();
    for (int y = 0; y < altura; y++)
    {
        cru.WriteByte(0);   // sem filtro na linha
        for (int x = 0; x < largura; x++)
        {
            int argb = tela.CorEm(x, y);
            cru.WriteByte((byte)(argb >> 16));
            cru.WriteByte((byte)(argb >> 8));
            cru.WriteByte((byte)argb);
        }
    }

    var comprimido = new MemoryStream();
    using (var z = new System.IO.Compression.ZLibStream(comprimido, System.IO.Compression.CompressionLevel.Optimal, true))
        cru.WriteTo(z);

    using var saida = File.Create(caminho);
    saida.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

    var cabecalho = new byte[13];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(cabecalho, largura);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(cabecalho.AsSpan(4), altura);
    cabecalho[8] = 8;       // 8 bits por cor
    cabecalho[9] = 2;       // RGB
    Pedaco(saida, "IHDR", cabecalho);
    Pedaco(saida, "IDAT", comprimido.ToArray());
    Pedaco(saida, "IEND", []);

    static void Pedaco(Stream s, string tipo, byte[] dados)
    {
        var tamanho = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(tamanho, dados.Length);
        s.Write(tamanho);
        var comTipo = new byte[4 + dados.Length];
        System.Text.Encoding.ASCII.GetBytes(tipo).CopyTo(comTipo, 0);
        dados.CopyTo(comTipo, 4);
        s.Write(comTipo);
        var crc = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(comTipo));
        s.Write(crc);
    }

    static uint Crc32(byte[] dados)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in dados)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }
}

// As outras abas do projeto: os arquivos de codigo ao lado do sketch (e
// nas subpastas, como a src/ de um sketch da IDE).
static Dictionary<string, string> AbasDaPasta(string sketch)
{
    var abas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    string pasta = Path.GetDirectoryName(Path.GetFullPath(sketch));
    string[] codigo = [".h", ".hpp", ".c", ".cpp", ".cc", ".inl", ".inc"];
    foreach (string a in Directory.EnumerateFiles(pasta, "*.*", SearchOption.AllDirectories))
    {
        string nome = Path.GetRelativePath(pasta, a).Replace('\\', '/');
        bool eDeCodigo = codigo.Contains(Path.GetExtension(a).ToLowerInvariant());
        if (!eDeCodigo && !nome.Equals("partitions.csv", StringComparison.OrdinalIgnoreCase)) continue;
        if (Path.GetFullPath(a) == Path.GetFullPath(sketch)) continue;
        abas[nome] = File.ReadAllText(a);
    }
    return abas;
}

// Cada subpasta e uma biblioteca: o formato novo (src/ e
// library.properties) ou o antigo (os .h na propria pasta).
static List<BibliotecaDeFora> BibliotecasDaPasta(string raiz)
{
    var lista = new List<BibliotecaDeFora>();
    foreach (string pasta in Directory.GetDirectories(raiz))
    {
        string[] codigo = [".h", ".hpp", ".c", ".cpp", ".cc", ".inl"];
        var arquivos = Directory.EnumerateFiles(pasta, "*.*", SearchOption.AllDirectories)
            .Where(a => codigo.Contains(Path.GetExtension(a).ToLowerInvariant()))
            .Select(a => Path.GetRelativePath(pasta, a).Replace('\\', '/'))
            .Where(r => !r.StartsWith("examples/", StringComparison.OrdinalIgnoreCase)
                        && !r.StartsWith("extras/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        bool novo = Directory.Exists(Path.Combine(pasta, "src"))
                    && (File.Exists(Path.Combine(pasta, "library.properties"))
                        || !Directory.EnumerateFiles(pasta, "*.h").Any());
        lista.Add(new BibliotecaDeFora
        {
            Nome = Path.GetFileName(pasta),
            Pasta = pasta,
            Arquivos = arquivos,
            Raiz = novo ? "src/" : "",
            Ler = r => File.ReadAllText(Path.Combine(pasta, r)),
        });
    }
    return lista;
}

static int Falhar(string titulo, string msg)
{
    Console.WriteLine();
    Console.WriteLine($"{titulo}: {msg}");
    return 1;
}
