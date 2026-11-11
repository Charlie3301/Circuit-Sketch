namespace Simulador;

/// <summary>
/// O sketch compilado (CompiladorNativo) dentro do interpretador da placa.
///
/// O interpretador continua sendo quem a janela, o depurador e os testes
/// conhecem; quando o programa veio compilado, o setup() e o loop() sao os da
/// DLL, e o interpretador vira o "core" dela: cada pinMode, delay ou
/// Serial.print que o C++ chama cai aqui (ChamarDoNativo), na mesma
/// implementacao das funcoes do caminho interpretado.
/// </summary>
public partial class Interpretador
{
    ISketchCompilado sketchNativo;

    /// <summary>O HD44780 do LCD paralelo que o C# fala pelos pinos (o "__lcd_hd44780").</summary>
    ControladorHd44780 lcdParalelo;

    /// <summary>O programa veio compilado de verdade (e roda nativo).</summary>
    public bool RodaNativo => prog.Compilado is not null;

    /// <summary>Carrega a DLL desta placa (uma vez por rodada), na thread da placa.</summary>
    void AbrirSketchNativo()
    {
        if (sketchNativo is not null) return;
        maq.Sketch?.Encerrar();
        // O C# (Roslyn) roda no .NET, na thread da placa; o C/C++ e a DLL.
        if (prog.Compilado is ProgramaDotNet dotnet)
        {
            var programa = new SketchDotNet(maq, ChamarDoNativo, AtenderInterrupcoes);
            sketchNativo = programa;
            maq.Sketch = programa;
            programa.Carregar(dotnet);
            return;
        }
        var nativo = new SketchNativo(maq, ChamarDoNativo, AtenderInterrupcoes, () => maq.Interrupcoes.Count > 0);
        sketchNativo = nativo;
        maq.Sketch = nativo;
        nativo.Depurador = Depurador;
        nativo.Carregar((SketchCompilado)prog.Compilado, Origem);
    }

    /// <summary>
    /// Uma chamada do C++ ao core: vira a mesma chamada que o sketch
    /// interpretado faria ("delay(500)", "Serial.print(...)"), com os valores
    /// prontos no lugar das expressoes.
    /// </summary>
    Valor ChamarDoNativo(string alvo, string nome, List<Valor> args, List<bool> nomes)
    {
        // O objeto de uma biblioteca nasce como nasceria no sketch interpretado
        // ("Servo direcao;"), com um nome so dele, e o nome volta para o C++.
        if (alvo is null && nome == "__criar")
        {
            string novo = $"__obj{++objetosNativos}";
            var argsDoConstrutor = new List<Expr>();
            for (int i = 1; i < args.Count; i++)
                argsDoConstrutor.Add(nomes[i] ? new Variavel(args[i].Texto, 0) : new Pronto(args[i], 0));
            Exec(new CmdDeclObj(args[0].Texto, novo, argsDoConstrutor, 0), global);
            return Valor.DeTexto(novo);
        }
        // "__ler(objeto, "acceleration.x")": um campo do objeto (o evento do
        // sensor que o getEvent preencheu).
        if (alvo is null && nome == "__ler" && args.Count >= 2)
        {
            // "messages.0.text": o numero e o indice do vetor.
            Expr caminho = new Variavel(args[0].Texto, 0);
            foreach (string parte in args[1].Texto.Split('.'))
                caminho = int.TryParse(parte, out int indice)
                    ? new Indice(caminho, new Literal(indice, null, true, false, 0), 0)
                    : new Membro(caminho, parte, 0);
            var lido = Avaliar(caminho, global);
            return lido.Referencia is Objeto dentro ? Valor.DeTexto(NomeDoObjeto(dentro)) : lido;
        }

        // Os callbacks de um objeto do C++ (a classe que deriva de
        // BLECharacteristicCallbacks): um objeto daqui que o simulador chama
        // como chamaria o do sketch interpretado (ver ChamarCallbackNativo).
        if (alvo is null && nome == "__callbacks" && args.Count >= 1)
        {
            var cb = new Objeto(ClasseDeCallbackNativo);
            cb.Campos["_indice"] = args[0];
            return Valor.DeTexto(NomeDoObjeto(cb));
        }
        // O download em bytes (o MP3 da fala do Audio.h): a resposta inteira
        // guardada aqui, e o C++ copia em pedacos.
        if (alvo is null && nome.StartsWith("__baixad", StringComparison.Ordinal) || alvo is null && nome == "__baixar")
            return BaixarDoNativo(nome, args);

        // O cartao pelo SDMMC (SD_MMC.h): o mesmo cartao, sem o SPI.
        if (alvo is null && nome == "__sdmmc_begin")
        {
            if (maq.Cartao.Montar()) return Valor.DeBool(true);
            maq.Avisar("SD_MMC.begin() devolveu false: nao ha cartao microSD ligado nesta placa.");
            return Valor.DeBool(false);
        }

        // O I2S do C++ compilado (cs_i2s.h): o som no amplificador do circuito.
        if (alvo is null && nome.StartsWith("__i2s_", StringComparison.Ordinal)) return I2sDoNativo(nome, args);

        // O UDP do C++ compilado (WiFiUdp.h, AsyncUDP.h).
        if (alvo is null && nome.StartsWith("__udp_", StringComparison.Ordinal)) return UdpDoNativo(nome, args);

        // O objeto rp2040 do core da Pico (RP2040Support.h): o que ele le e faz na placa da bancada.
        if (alvo is null && nome == "__rp2040" && args.Count >= 1)
        {
            string metodo = args[0].Texto ?? "";
            if (WatchdogDoRp2040(metodo, [.. args.Skip(1)])) return Valor.Vazio;
            return metodo switch
            {
                "f_cpu" => Valor.DeInt((long)((maq.Perfil?.Megahertz ?? 133) * 1_000_000)),
                "getFreeHeap" => Valor.DeInt(Ram.HeapLivre),
                "getUsedHeap" => Valor.DeInt(Ram.HeapUsada),
                "getTotalHeap" => Valor.DeInt(Ram.HeapTotal),
                "getBootselButton" => Valor.DeBool(maq.BootselApertado),
                "isPicoW" => Valor.DeBool(maq.Perfil?.Id == "pi-pico-w"),
                "reboot" => throw Reiniciar(ReinicioDaPlaca.PeloSoftware, 0, 0),
                _ => Valor.Vazio,
            };
        }

        // O bluetooth do MicroPython (modbluetooth_cs.c): os pedidos e os eventos do radio BLE.
        if (alvo is null && nome == "__mpble") return BleDoPython(args);

        // O LCD paralelo do C# (LcdInterface.CreateGpio do nanoFramework): "__lcd_hd44780"(rs, byte,
        // colunas, linhas) -- o comando ou o dado que o driver poria nos pinos D0 a D7.
        if (alvo is null && nome == "__lcd_hd44780" && args.Count >= 4)
        {
            lcdParalelo ??= new ControladorHd44780(maq.CriarLcd, (int)args[2].Num, (int)args[3].Num);
            if (args[0].Num != 0) lcdParalelo.Dado((byte)args[1].Num);
            else lcdParalelo.Comando((byte)args[1].Num);
            return Valor.Vazio;
        }

        // O HAL da ESP32 (cs_hal.cpp): o toque e a fita do RMT.
        if (alvo is null && nome is "__toque" or "__fita" or "__usb" or "__erro_python"
            or "__nvs_gravar" or "__nvs_ler" or "__nvs_tipo" or "__nvs_tamanho" or "__nvs_apagar" or "__nvs_limpar" or "__nvs_chaves" or "__nvs_contar" or "__nvs_espacos"
            or "__154_ligar" or "__154_enviar" or "__154_receber" or "__rmaker_rede" or "__rmaker_conectou" or "__rmaker_relatar" or "__rmaker_proxima" or "__rmaker_aviso"
            or "__matter_comissionada" or "__matter_proxima" or "__matter_relatar" or "__matter_descomissionar" or "__matter_evento" or "__abort") return HalDoNativo(nome, args);
        if (alvo is null && nome is "__dns" or "__wifi_estacoes" or "__mdns" or "__mdns_servico" or "__mdns_buscar"
            or "__eth" or "__eth_ip" or "__eth_roteador" or "__eth_mac" or "__eth_config") return NomesDoNativo(nome, args);

        // O tom que o pino chaveado pelo registrador forma (cs_timer.cpp): o
        // buzzer toca, como no tone().
        if (alvo is null && nome == "__tom" && args.Count >= 2)
        {
            int pino = (int)args[0].Num, hz = (int)args[1].Num;
            if (hz > 0) maq.Tone(pino, hz);
            else maq.NoTone(pino);
            return Valor.Vazio;
        }
        if (alvo is null && nome == "__destruir")
        {
            if (args.Count > 0) global.TirarObjeto(args[0].Texto, out _);
            return Valor.Vazio;
        }

        var exprs = new List<Expr>(args.Count);
        for (int i = 0; i < args.Count; i++)
        {
            // A funcao do sketch passada para a biblioteca: ela existe para o
            // interpretador, que a acha pelo nome e a chama de volta.
            if (nomes[i] && RotinaNativa(args[i].Texto, out _) && !funcoes.ContainsKey(args[i].Texto))
                funcoes[args[i].Texto] = new Funcao("void", args[i].Texto, [], new CmdBloco([], 0), 0);
            exprs.Add(nomes[i] ? new Variavel(args[i].Texto, 0) : new Pronto(args[i], 0));
        }
        Expr funcao = alvo is null ? new Variavel(nome, 0) : new Membro(new Variavel(alvo, 0), nome, 0);
        var volta = Avaliar(new Chamada(funcao, exprs, 0), global);

        // Um objeto devolvido (o DateTime do RTC.now()): ganha um nome, que o
        // C++ usa para chamar os metodos dele (e que ele solta com __destruir
        // quando o objeto era so de passagem, como o DateTime).
        if (volta.Referencia is Objeto devolvido) return Valor.DeTexto(NomeDoObjeto(devolvido));
        return volta;
    }

    /// <summary>
    /// O nome de um objeto do simulador para o C++ -- sempre o mesmo para o
    /// mesmo objeto (o servidor BLE que volta de dois lugares e um so).
    /// </summary>
    string NomeDoObjeto(Objeto o)
    {
        if (nomesDosObjetos.TryGetValue(o, out string nome) && global.BuscarObjeto(nome, out var ainda) && ReferenceEquals(ainda, o))
            return nome;
        nome = $"__volta{objetosDevolvidos++}";
        global.DeclararObjeto(nome, o);
        nomesDosObjetos[o] = nome;
        return nome;
    }

    readonly Dictionary<Objeto, string> nomesDosObjetos = new(ReferenceEqualityComparer.Instance);

    const string ClasseDeCallbackNativo = "__callbacks_do_cpp";

    /// <summary>
    /// O simulador chamando um metodo do objeto de callbacks do C++: o nome do
    /// metodo e os argumentos (objetos pelo nome) vao para o despachante dele.
    /// </summary>
    bool ChamarCallbackNativo(Objeto cb, string metodo, params Valor[] args)
    {
        if (cb?.Classe != ClasseDeCallbackNativo || sketchNativo is null) return false;
        var lista = new List<object> { Valor.DeTexto(metodo) };
        foreach (var a in args) lista.Add(ParaONativo(a));
        sketchNativo.Rotina((int)cb.Campos["_indice"].Num, [.. lista]);
        return true;
    }

    /// <summary>O argumento de um callback do C++: o objeto vai pelo nome, o vetor pelos bytes.</summary>
    object ParaONativo(object a) => a switch
    {
        Valor { Referencia: Objeto o } => Valor.DeTexto(NomeDoObjeto(o)),
        Valor { Referencia: Valor[] v } => v,
        _ => a,
    };

    int objetosNativos, objetosDevolvidos;

    // ---- os downloads do C++ compilado ----
    readonly List<byte[]> baixados = [];

    Valor BaixarDoNativo(string nome, List<Valor> args)
    {
        byte[] Dados(int i) => i >= 0 && i < baixados.Count ? baixados[i] : null;
        switch (nome)
        {
            case "__baixar":
            {
                var dados = maq.Rede?.Baixar(maq, args.Count > 0 ? args[0].Texto ?? "" : "", 20000);
                if (dados is null) return Valor.DeInt(-1);
                baixados.Add(dados);
                return Valor.DeInt(baixados.Count - 1);
            }
            case "__baixado_tamanho": return Valor.DeInt(Dados((int)args[0].Num)?.Length ?? 0);
            case "__baixado_ler":
            {
                var dados = Dados((int)args[0].Num);
                long de = (long)args[1].Num;
                if (dados is null || args.Count < 3 || args[2].Referencia is not Valor[] destino) return Valor.DeInt(0);
                int n = (int)Math.Clamp(dados.Length - de, 0, destino.Length);
                for (int i = 0; i < n; i++) destino[i] = Valor.DeC(dados[de + i], TipoC.UChar);
                return Valor.DeInt(n);
            }
            case "__baixado_soltar":
            {
                int i = (int)args[0].Num;
                if (i >= 0 && i < baixados.Count) baixados[i] = null;
                return Valor.Vazio;
            }
        }
        return Valor.Vazio;
    }

    // ---- o I2S do C++ compilado ----
    SaidaDeAudio somDoI2s;
    int bitsDoI2s = 16, canaisDoI2s = 2;
    double inicioDoI2sUs;
    long quadrosDoI2s;
    static int geracaoDoI2s = 1 << 20;

    /// <summary>
    /// "__i2s_abrir"(taxa, bits, canais, bclk, ws, dout), "__i2s_escrever"(bytes),
    /// "__i2s_fechar": a mesma saida de audio do player DAP (SaidaDeAudio), que a
    /// janela toca no alto-falante ligado ao amplificador nesses pinos. Quem
    /// escreve espera o som tocar, como no DMA da placa (100 ms de folga).
    /// </summary>
    Valor I2sDoNativo(string nome, List<Valor> args)
    {
        switch (nome)
        {
            case "__i2s_abrir":
            {
                int taxa = (int)args[0].Num;
                bitsDoI2s = (int)args[1].Num is 8 or 16 or 24 or 32 ? (int)args[1].Num : 16;
                canaisDoI2s = (int)args[2].Num == 1 ? 1 : 2;
                somDoI2s = new SaidaDeAudio(taxa, (int)args[3].Num, (int)args[4].Num, (int)args[5].Num, Interlocked.Increment(ref geracaoDoI2s));
                maq.Audio = somDoI2s;
                quadrosDoI2s = 0;
                return Valor.DeBool(true);
            }
            case "__i2s_fechar":
                if (maq.Audio == somDoI2s) maq.Audio = null;
                somDoI2s = null;
                return Valor.Vazio;
            case "__i2s_escrever":
            {
                if (somDoI2s is null || args.Count == 0 || args[0].Referencia is not Valor[] bytes) return Valor.DeInt(0);
                int porAmostra = bitsDoI2s / 8, quadro = porAmostra * canaisDoI2s;
                int quadros = bytes.Length / quadro;
                var estereo = new short[quadros * 2];
                int pico = 0;
                for (int i = 0; i < quadros; i++)
                    for (int lado = 0; lado < 2; lado++)
                    {
                        int k = i * quadro + Math.Min(lado, canaisDoI2s - 1) * porAmostra;
                        int B(int j) => (int)((long)bytes[k + j].Num & 0xFF);
                        int v = porAmostra switch
                        {
                            1 => (B(0) - 128) << 8,
                            2 => (short)(B(0) | B(1) << 8),
                            3 => (short)(B(1) | B(2) << 8),
                            _ => (short)(B(2) | B(3) << 8),
                        };
                        estereo[2 * i + lado] = (short)v;
                        pico = Math.Max(pico, Math.Abs(v));
                    }
                somDoI2s.EscreverEstereo(estereo);
                somDoI2s.Nivel = pico / 32768.0;

                // O relogio do DMA: espera ate faltar 100 ms para o fim do que ja foi.
                double agora = maq.AgoraUs();
                if (quadrosDoI2s == 0 || inicioDoI2sUs + quadrosDoI2s * 1e6 / somDoI2s.Taxa < agora - 50_000)
                {
                    inicioDoI2sUs = agora;
                    quadrosDoI2s = 0;
                }
                quadrosDoI2s += quadros;
                double tocaAte = inicioDoI2sUs + quadrosDoI2s * 1e6 / somDoI2s.Taxa;
                while (tocaAte - maq.AgoraUs() > 100_000)
                {
                    if (maq.Parar) throw maq.MotivoDaParada();
                    Thread.Sleep((int)Math.Clamp((tocaAte - maq.AgoraUs() - 100_000) / 1000, 1, 10));
                }
                return Valor.DeInt(quadros * quadro);
            }
        }
        return Valor.Vazio;
    }


    /// <summary>"__nativa_3": a rotina de interrupcao 3 da DLL.</summary>
    static bool RotinaNativa(string nome, out int indice)
    {
        indice = -1;
        return nome is not null && nome.StartsWith("__nativa_", StringComparison.Ordinal)
               && int.TryParse(nome.AsSpan(9), out indice);
    }

    /// <summary>
    /// A constante do core com o numero que o simulador usa ("RISING" e 1):
    /// o cabecalho da placa do C++ compilado define as mesmas.
    /// </summary>
    public static string ConstanteDoCore(string nome)
        => Constantes.TryGetValue(nome, out var v) && !v.EhTexto ? ((long)v.Num).ToString() : null;
}
