namespace Simulador;

public partial class Interpretador
{
    static readonly Dictionary<string, Valor> ConstantesDeRadio = new()
    {
        ["RF24_PA_MIN"] = Valor.DeInt(0),
        ["RF24_PA_LOW"] = Valor.DeInt(1),
        ["RF24_PA_HIGH"] = Valor.DeInt(2),
        ["RF24_PA_MAX"] = Valor.DeInt(3),
        ["RF24_250KBPS"] = Valor.DeInt(2),
        ["RF24_1MBPS"] = Valor.DeInt(0),
        ["RF24_2MBPS"] = Valor.DeInt(1),
        ["RF24_CRC_8"] = Valor.DeInt(1),
        ["RF24_CRC_16"] = Valor.DeInt(2),
        ["ESP_OK"] = Valor.DeInt(0),
        ["ESP_FAIL"] = Valor.DeInt(-1),

        // Os eventos do esp_spp_api.h, que o register_callback() recebe.
        ["ESP_SPP_INIT_EVT"] = Valor.DeInt(0),
        ["ESP_SPP_UNINIT_EVT"] = Valor.DeInt(1),
        ["ESP_SPP_DISCOVERY_COMP_EVT"] = Valor.DeInt(8),
        ["ESP_SPP_OPEN_EVT"] = Valor.DeInt(26),
        ["ESP_SPP_CLOSE_EVT"] = Valor.DeInt(27),
        ["ESP_SPP_START_EVT"] = Valor.DeInt(28),
        ["ESP_SPP_CL_INIT_EVT"] = Valor.DeInt(29),
        ["ESP_SPP_DATA_IND_EVT"] = Valor.DeInt(30),
        ["ESP_SPP_CONG_EVT"] = Valor.DeInt(31),
        ["ESP_SPP_WRITE_EVT"] = Valor.DeInt(33),
        ["ESP_SPP_SRV_OPEN_EVT"] = Valor.DeInt(34),
        ["ESP_SPP_SRV_STOP_EVT"] = Valor.DeInt(35),
        ["ESP_NOW_SEND_SUCCESS"] = Valor.DeInt(0),
        ["ESP_NOW_SEND_FAIL"] = Valor.DeInt(1),
        ["ESP_NOW_MAX_DATA_LEN"] = Valor.DeInt(250),

        // Os erros do esp_now.h, com os numeros de la: um sketch que
        // imprime o codigo do erro tem que imprimir o mesmo numero que a
        // placa imprimiria.
        ["ESP_ERR_ESPNOW_BASE"] = Valor.DeInt(0x3064),
        ["ESP_ERR_ESPNOW_NOT_INIT"] = Valor.DeInt(0x3065),
        ["ESP_ERR_ESPNOW_ARG"] = Valor.DeInt(0x3066),
        ["ESP_ERR_ESPNOW_NO_MEM"] = Valor.DeInt(0x3067),
        ["ESP_ERR_ESPNOW_FULL"] = Valor.DeInt(0x3068),
        ["ESP_ERR_ESPNOW_NOT_FOUND"] = Valor.DeInt(0x3069),
        ["ESP_ERR_ESPNOW_INTERNAL"] = Valor.DeInt(0x306A),
        ["ESP_ERR_ESPNOW_EXIST"] = Valor.DeInt(0x306B),
        ["ESP_ERR_ESPNOW_IF"] = Valor.DeInt(0x306C),
        ["WIFI_IF_STA"] = Valor.DeInt(0),
        ["WIFI_IF_AP"] = Valor.DeInt(1),
    };

    void AvisarNaPlaca(string texto)
    {
        lock (Maq.Trava) if (!Maq.Avisos.Contains(texto)) Maq.Avisos.Add(texto);
    }

    Radio RadioDoObjeto(Objeto obj)
    {
        if (obj.Rede is Radio pronto) return pronto;

        var novo = Maq.Rede.RadioDe(Maq);
        obj.Rede = novo;
        return novo;
    }

    Valor ChamarRf24(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var radio = RadioDoObjeto(obj);

        switch (metodo)
        {
            case "begin":
            {
                if (!Maq.RadioPresente)
                {
                    AvisarNaPlaca(
                        "radio.begin() devolveu false: nao ha modulo NRF24L01 ligado nesta placa. "
                        + "Ponha a peca no circuito e ligue o CE, o CSN, o SCK, o MOSI, o MISO "
                        + "e a alimentacao dela.");
                    radio.Ligado = false;
                    return Valor.DeBool(false);
                }

                radio.Ligado = true;
                AvisarNaPlaca(
                    "radio nRF24 simulado: o alcance e a bancada inteira, e so recebe quem "
                    + "estiver no mesmo canal e com o mesmo endereco de tubo.");
                return Valor.DeBool(true);
            }

            case "isChipConnected":
            case "isValid":
                return Valor.DeBool(Maq.RadioPresente);

            case "openWritingPipe":
                radio.Escrita = EnderecoDoTubo(c, amb, 0);
                return Valor.Vazio;

            case "openReadingPipe":
            {
                string endereco = EnderecoDoTubo(c, amb, c.Args.Count > 1 ? 1 : 0);
                if (endereco.Length > 0) radio.Leitura.Add(endereco);
                return Valor.Vazio;
            }

            case "closeReadingPipe":
                radio.Leitura.Clear();
                return Valor.Vazio;

            case "startListening":
                radio.Ouvindo = true;
                return Valor.Vazio;

            case "stopListening":
                radio.Ouvindo = false;
                return Valor.Vazio;

            case "setChannel":
                radio.Canal = c.Args.Count > 0 ? (int)Avaliar(c.Args[0], amb).Num : 76;
                return Valor.Vazio;

            case "getChannel":
                return Valor.DeInt(radio.Canal);

            case "available":
                return Valor.DeBool(Maq.Rede.TemNoRadio(radio));

            case "write":
            {
                if (!radio.Ligado) return Valor.DeBool(false);

                string carga = TextoDoArgumento(c.Args.Count > 0 ? c.Args[0] : null, amb);
                if (radio.Ouvindo) radio.Ouvindo = false;
                return Valor.DeBool(Maq.Rede.EnviarNoRadio(radio, carga));
            }

            case "read":
            {
                string chegou = Maq.Rede.ReceberDoRadio(radio) ?? "";
                GuardarNoArgumento(c.Args.Count > 0 ? c.Args[0] : null, amb, chegou);
                return Valor.Vazio;
            }

            case "setPALevel":
            case "setDataRate":
            case "setRetries":
            case "setAutoAck":
            case "setCRCLength":
            case "setPayloadSize":
            case "enableAckPayload":
            case "enableDynamicPayloads":
            case "disableDynamicPayloads":
            case "powerUp":
            case "powerDown":
            case "flush_tx":
            case "printDetails":
            case "printPrettyDetails":
            case "maskIRQ":
                return Valor.Vazio;

            case "getPayloadSize":
                return Valor.DeInt(32);

            case "testCarrier":
            case "testRPD":
                return Valor.DeBool(false);
        }

        throw new ErroDeExecucao($"radio.{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }

    string EnderecoDoTubo(Chamada c, Ambiente amb, int i)
    {
        if (i >= c.Args.Count) return "";

        var arg = SemEndereco(c.Args[i]);
        if (arg is Variavel v && amb.BuscarArray(v.Nome, out var vetor)) return TextoDoVetor(vetor);

        return Avaliar(arg, amb).Mostrar();
    }

    string TextoDoArgumento(Expr e, Ambiente amb)
    {
        if (e is null) return "";

        var arg = SemEndereco(e);
        if (arg is Variavel v)
        {
            if (amb.BuscarArray(v.Nome, out var vetor)) return TextoDoVetor(vetor);
            if (amb.BuscarObjeto(v.Nome, out var obj) && obj.Campos.Count > 0)
                return TextoDosCampos(obj);
        }

        return Avaliar(arg, amb).Mostrar();
    }

    void GuardarNoArgumento(Expr e, Ambiente amb, string texto)
    {
        if (e is null) return;

        var arg = SemEndereco(e);
        if (arg is Variavel v)
        {
            if (amb.BuscarArray(v.Nome, out var vetor)) { EscreverNoVetor(vetor, texto); return; }
            if (amb.BuscarObjeto(v.Nome, out var obj)) { PreencherCampos(obj, texto); return; }
            if (amb.Buscar(v.Nome, out _)) { amb.Atribuir(v.Nome, Valor.DeTexto(texto)); return; }
        }

        throw new ErroDeExecucao(
            "radio.read() espera uma variavel para guardar o que chegou, "
            + "como em radio.read(&texto, sizeof(texto)).", e.Linha);
    }

    static readonly string Separador = ((char)31).ToString();

    static string TextoDosCampos(Objeto obj)
        => string.Join(Separador, obj.Campos.Select(x => $"{x.Key}={x.Value.Mostrar()}"));

    static void PreencherCampos(Objeto obj, string texto)
    {
        foreach (string pedaco in (texto ?? "").Split(Separador))
        {
            int corte = pedaco.IndexOf('=');
            if (corte <= 0) continue;

            string campo = pedaco[..corte];
            string valor = pedaco[(corte + 1)..];
            if (!obj.Campos.TryGetValue(campo, out var antigo)) continue;

            obj.Campos[campo] = antigo.EhTexto || !double.TryParse(
                valor, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double numero)
                ? Valor.DeTexto(valor)
                : antigo.Inteiro ? Valor.DeInt(numero) : Valor.DeReal(numero);
        }
    }

    PlacaEspNow EspNow => Maq.Rede.EspNowDe(Maq);

    bool ChamarEspNow(string nome, Chamada c, Ambiente amb, out Valor resultado)
    {
        resultado = Valor.Vazio;

        switch (nome)
        {
            case "esp_now_init":
                ExigirWifi(c.Linha, "esp_now_init()");
                EspNow.Ligado = true;
                AvisarNaPlaca(
                    "ESP-Now simulado: as mensagens chegam nas outras placas da bancada que "
                    + "tambem chamaram esp_now_init().");
                resultado = Valor.DeInt(0);
                return true;

            case "esp_now_deinit":
                EspNow.Ligado = false;
                resultado = Valor.DeInt(0);
                return true;

            case "esp_now_register_recv_cb":
                EspNow.AoReceber = NomeDeFuncao(c, 0, "esp_now_register_recv_cb()");
                resultado = Valor.DeInt(0);
                return true;

            case "esp_now_register_send_cb":
                EspNow.AoEnviar = NomeDeFuncao(c, 0, "esp_now_register_send_cb()");
                resultado = Valor.DeInt(0);
                return true;

            case "esp_now_unregister_recv_cb":
                EspNow.AoReceber = null;
                resultado = Valor.DeInt(0);
                return true;

            case "esp_now_unregister_send_cb":
                EspNow.AoEnviar = null;
                resultado = Valor.DeInt(0);
                return true;

            case "esp_now_add_peer":
            {
                // Como na placa: antes do init nao ha lista de pares, e o
                // mesmo par duas vezes e erro -- o sketch que confere o
                // retorno ("!= ESP_OK") tem que ver o que veria la.
                if (!EspNow.Ligado)
                {
                    AvisarNaPlaca("esp_now_add_peer() antes de esp_now_init(): na placa o par nao "
                                  + "entra (ESP_ERR_ESPNOW_NOT_INIT). Chame esp_now_init() primeiro.");
                    resultado = Valor.DeInt(0x3065);
                    return true;
                }

                string mac = MacDoArgumento(c, 0, amb);
                resultado = Valor.DeInt(EspNow.Pares.Add(mac) ? 0 : 0x306B);
                return true;
            }

            case "esp_now_del_peer":
                EspNow.Pares.Remove(MacDoArgumento(c, 0, amb));
                resultado = Valor.DeInt(0);
                return true;

            case "esp_now_is_peer_exist":
                resultado = Valor.DeBool(EspNow.Pares.Contains(MacDoArgumento(c, 0, amb)));
                return true;

            case "esp_now_set_pmk":
            case "esp_now_peer_num":
                resultado = Valor.DeInt(0);
                return true;

            case "esp_now_send":
            {
                if (!EspNow.Ligado)
                    throw new ErroDeExecucao(
                        "esp_now_send() antes de esp_now_init(). Chame esp_now_init() no setup().",
                        c.Linha);

                var carga = CargaDoArgumento(c, 1, amb);

                // esp_now_send(NULL, ...) manda para cada par da lista.
                var destinos = ParaTodosOsPares(c)
                    ? [.. EspNow.Pares]
                    : new List<string> { MacDoArgumento(c, 0, amb) };

                if (destinos.Count == 0)
                {
                    AvisarNaPlaca("esp_now_send(NULL, ...) sem nenhum par: nada saiu "
                                  + "(ESP_ERR_ESPNOW_NOT_FOUND).");
                    resultado = Valor.DeInt(0x3069);
                    return true;
                }

                foreach (string destino in destinos)
                {
                    // Na placa, mandar para quem nao foi adicionado com
                    // esp_now_add_peer() falha e NADA sai -- inclusive a
                    // difusao, que precisa do FF:FF:FF:FF:FF:FF na lista.
                    // Aqui funcionava assim mesmo, e o erro so aparecia na
                    // bancada.
                    if (!EspNow.Pares.Contains(destino))
                    {
                        AvisarNaPlaca($"esp_now_send() para {destino}, que nao foi adicionado com "
                                      + "esp_now_add_peer(): na placa de verdade o envio falha "
                                      + "(ESP_ERR_ESPNOW_NOT_FOUND) e nada sai. Para mandar para "
                                      + "todas, adicione FF:FF:FF:FF:FF:FF como par.");
                        resultado = Valor.DeInt(0x3069);
                        return true;
                    }

                    int quantas = Maq.Rede.EnviarEspNow(EspNow, destino, carga);

                    // A difusao nao tem confirmacao de ninguem: o callback
                    // diz SUCESSO se o quadro saiu, com ou sem quem ouvisse.
                    // Para um endereco so, sucesso e a outra placa ter
                    // recebido -- e o ACK que o radio espera.
                    bool difusao = string.Equals(destino, Rede.MacDeBroadcast,
                                                 StringComparison.OrdinalIgnoreCase);
                    int estado = difusao || quantas > 0 ? 0 : 1;

                    if (EspNow.AoEnviar is not null && funcoes.TryGetValue(EspNow.AoEnviar, out var aviso))
                        ChamarRotina(new Rotina { Funcao = aviso, Captura = Global },
                            Valor.DeTexto(destino), Valor.DeInt(estado));
                }

                resultado = Valor.DeInt(0);
                return true;
            }
        }

        return false;
    }

    /// <summary>O primeiro argumento e NULL (ou 0): "para todos os pares".</summary>
    bool ParaTodosOsPares(Chamada c)
    {
        if (c.Args.Count == 0) return false;
        var arg = SemEndereco(c.Args[0]);
        if (arg is Conversao cv) arg = SemEndereco(cv.Alvo);

        return arg is Variavel { Nome: "NULL" or "nullptr" }
               || arg is Literal { Valor: 0, EhTexto: false };
    }

    string NomeDeFuncao(Chamada c, int i, string ondeUsou)
    {
        if (i < c.Args.Count && SemEndereco(c.Args[i]) is Variavel v && funcoes.ContainsKey(v.Nome))
            return v.Nome;

        throw new ErroDeExecucao(
            $"{ondeUsou} espera o nome de uma funcao deste sketch.", c.Linha);
    }

    string MacDoArgumento(Chamada c, int i, Ambiente amb)
    {
        if (i >= c.Args.Count) return Rede.MacDeBroadcast;

        var arg = SemEndereco(c.Args[i]);

        if (arg is Variavel v)
        {
            if (amb.BuscarArray(v.Nome, out var vetor)) return MacDoVetor(vetor);
            if (amb.BuscarObjeto(v.Nome, out var obj)
                && obj.Campos.TryGetValue("peer_addr", out var guardado))
                return guardado.Mostrar();
        }

        if (arg is Membro m && m.Obj is Variavel dono && amb.BuscarObjeto(dono.Nome, out var par)
            && par.Campos.TryGetValue(m.Nome, out var campo))
            return campo.Mostrar();

        return Rede.MacDeBroadcast;
    }

    static string MacDoVetor(Valor[] vetor)
        => string.Join(":", vetor.Take(6).Select(v => ((long)v.Num & 0xFF).ToString("X2")));

    CargaEspNow CargaDoArgumento(Chamada c, int i, Ambiente amb)
    {
        if (i >= c.Args.Count)
            return new CargaEspNow { Origem = Maq.Rede.MacDe(Maq), Texto = "", Tamanho = 0 };

        var arg = SemEndereco(c.Args[i]);
        if (arg is Conversao cv) arg = SemEndereco(cv.Alvo);

        string origem = Maq.Rede.MacDe(Maq);

        if (arg is Variavel v)
        {
            if (amb.BuscarObjeto(v.Nome, out var obj) && obj.Campos.Count > 0)
                return new CargaEspNow
                {
                    Origem = origem,
                    Texto = TextoDosCampos(obj),
                    Campos = new Dictionary<string, Valor>(obj.Campos),
                    Tamanho = obj.Campos.Count * 4,
                };

            if (amb.BuscarArray(v.Nome, out var vetor))
            {
                string texto = TextoDoVetor(vetor);
                return new CargaEspNow { Origem = origem, Texto = texto, Tamanho = vetor.Length };
            }
        }

        var lido = Avaliar(arg, amb);

        // "esp_now_send(mac, &apertado, 1)" com um numero: vai a memoria
        // dele, do tamanho pedido (little-endian, como na ESP32).
        if (!lido.EhTexto && lido.Referencia is null)
        {
            int tamanho = c.Args.Count > i + 1 ? Math.Clamp((int)Avaliar(c.Args[i + 1], amb).Num, 1, 250) : 4;
            byte[] memoria = !lido.Inteiro && tamanho == 4 ? BitConverter.GetBytes((float)lido.Num)
                           : !lido.Inteiro && tamanho >= 8 ? BitConverter.GetBytes(lido.Num)
                           : BitConverter.GetBytes((long)lido.Num);
            var bytes = new byte[tamanho];
            Array.Copy(memoria, bytes, Math.Min(tamanho, memoria.Length));
            return new CargaEspNow { Origem = origem, Texto = lido.Mostrar(), Tamanho = tamanho, Bytes = bytes };
        }

        string valor = lido.Mostrar();
        return new CargaEspNow { Origem = origem, Texto = valor, Tamanho = valor.Length + 1 };
    }

    CargaEspNow ultimaCargaEspNow;

    void EntregarEspNow()
    {
        var minha = Maq.Rede.EspNowDe(Maq);
        if (!minha.Ligado) return;

        while (Maq.Rede.ReceberEspNow(minha) is { } carga)
        {
            ultimaCargaEspNow = carga;

            if (minha.AoReceber is null || !funcoes.TryGetValue(minha.AoReceber, out var f)) continue;

            var bytes = carga.Bytes ?? TextoArduino.ParaBytes(carga.Texto);
            var vetor = new Valor[bytes.Length + 1];
            for (int i = 0; i < bytes.Length; i++) vetor[i] = Valor.DeCaractere(bytes[i]);
            vetor[^1] = Valor.DeCaractere(0);

            ChamarRotina(new Rotina { Funcao = f, Captura = Global },
                Valor.DeTexto(carga.Origem), vetor, Valor.DeInt(carga.Tamanho));
        }
    }

    bool CopiarMemoria(Chamada c, Ambiente amb, out Valor resultado)
    {
        resultado = Valor.Vazio;
        if (c.Args.Count < 2) return false;

        var destino = SemEndereco(c.Args[0]);
        var origem = SemEndereco(c.Args[1]);
        if (origem is Conversao conv) origem = SemEndereco(conv.Alvo);

        if (destino is Variavel dv && amb.BuscarObjeto(dv.Nome, out var obj))
        {
            if (origem is Variavel ov && amb.BuscarObjeto(ov.Nome, out var outro))
            {
                foreach (var (nome, valor) in outro.Campos) obj.Campos[nome] = valor;
                return true;
            }

            if (origem is Variavel od && amb.BuscarArray(od.Nome, out var vetor)
                && ultimaCargaEspNow is null)
            {
                PreencherCampos(obj, TextoDoVetor(vetor));
                return true;
            }

            if (ultimaCargaEspNow?.Campos is { } campos)
            {
                foreach (var (nome, valor) in campos) obj.Campos[nome] = valor;
                return true;
            }

            if (ultimaCargaEspNow is not null)
            {
                PreencherCampos(obj, ultimaCargaEspNow.Texto);
                return true;
            }

            return true;
        }

        if (destino is Variavel av && amb.BuscarArray(av.Nome, out var alvo))
        {
            EscreverNoVetor(alvo, TextoDoArgumento(origem, amb));
            return true;
        }

        if (destino is Membro md && md.Obj is Variavel dono
            && amb.BuscarObjeto(dono.Nome, out var estrutura))
        {
            // "memcpy(par.peer_addr, mac, 6)": o endereco vira o MAC
            // escrito como o resto do simulador escreve -- e e assim que
            // o esp_now_add_peer(&par) o compara depois.
            if (md.Nome.EndsWith("addr", StringComparison.OrdinalIgnoreCase)
                && origem is Variavel om && amb.BuscarArray(om.Nome, out var bytesDoMac))
            {
                estrutura.Campos[md.Nome] = Valor.DeTexto(MacDoVetor(bytesDoMac));
                return true;
            }

            estrutura.Campos[md.Nome] = Valor.DeTexto(TextoDoArgumento(origem, amb));
            return true;
        }

        return false;
    }

    /// <summary>
    /// sizeof: o tamanho em BYTES, nesta placa.
    ///
    /// Devolvia a quantidade de itens do vetor. Parecia o mesmo numero ate
    /// o sketch fazer a conta que todo tutorial ensina,
    /// "sizeof(pinos) / sizeof(pinos[0])": 3 / 4 dava ZERO, e o for que
    /// devia passar pelos tres LEDs nao passava por nenhum. Agora e a
    /// conta do compilador: o "int" tem 2 bytes no Uno e 4 na ESP32, a
    /// struct ganha o preenchimento de alinhamento das placas de 32 bits,
    /// e o vetor que chega como parametro e um ponteiro.
    /// </summary>
    Valor TamanhoDe(Chamada c, Ambiente amb)
    {
        if (c.Args.Count == 0) return Valor.DeInt(0);

        var arg = c.Args[0];
        if (arg is NomeDeTipo nomeDoTipo) return Valor.DeInt(BytesDoTipo(nomeDoTipo.Tipo));

        // "sizeof(&x)" e o endereco.
        if (arg is Unaria { Op: "&" }) return Valor.DeInt(BytesDoPonteiro);

        if (arg is Variavel v)
        {
            string tipo = amb.TipoDe(v.Nome);

            if (amb.BuscarArray(v.Nome, out var vetor))
            {
                // Vetor que entrou como parametro: dentro da funcao ele e um
                // ponteiro. Escrito "int v[]", o compilador avisa -- e o
                // aviso vale aqui tambem.
                if (tipo is not null && tipo.Contains('|'))
                {
                    if (tipo.EndsWith("|[]", StringComparison.Ordinal))
                        maq.Avisar($"sizeof({v.Nome}) num parametro da funcao da o tamanho do PONTEIRO "
                                   + $"({BytesDoPonteiro} bytes), e nao o do vetor: dentro da funcao o vetor "
                                   + "vira endereco. Passe a quantidade de itens junto, como parametro.");
                    return Valor.DeInt(BytesDoPonteiro);
                }
                return Valor.DeInt(vetor.Length * BytesDoItem(tipo, vetor));
            }

            if (tipo is not null) return Valor.DeInt(BytesDoTipo(tipo));

            if (amb.BuscarObjeto(v.Nome, out var obj))
            {
                if (estruturas.ContainsKey(obj.Classe) || classes.ContainsKey(obj.Classe))
                    return Valor.DeInt(BytesDoTipo(obj.Classe));
                if (obj.Campos.Count > 0) return Valor.DeInt(obj.Campos.Values.Sum(TamanhoDoValor));
            }

            if (amb.Buscar(v.Nome, out var valor)) return Valor.DeInt(TamanhoDoValor(valor));
        }

        // "sizeof(v[0])" -- e, num vetor de varias dimensoes, "sizeof(m[0])"
        // e a linha inteira, "sizeof(cubo[0])" o plano, "sizeof(cubo[0][0])" a linha dele.
        if (arg is Indice indexado && BytesDoPedaco(indexado, amb) is int doPedaco) return Valor.DeInt(doPedaco);

        // "sizeof(msg.texto)": o campo, pela declaracao da struct.
        if (arg is Membro membro && ObjetoDaExpressao(membro.Obj, amb) is { } doCampo
            && CamposDoTipo(doCampo.Classe)?.OfType<CmdDecl>().FirstOrDefault(d => d.Nome == membro.Nome) is { } campo)
            return Valor.DeInt(BytesDoCampo(campo).Tamanho);

        return Valor.DeInt(TamanhoDoValor(Avaliar(arg, amb)));
    }

    PerfilPlaca PerfilDoSizeof => maq.Perfil ?? PerfilPlaca.Esp32;

    int BytesDoPonteiro => PerfilDoSizeof.De8Bits ? 2 : 4;

    /// <summary>O tamanho de um item do vetor: pelo tipo declarado, ou pelo valor.</summary>
    int BytesDoItem(string tipo, Valor[] vetor)
    {
        if (tipo is not null) return BytesDoTipo(tipo.Contains('|') ? tipo[..tipo.IndexOf('|')] : tipo);
        if (vetor.Length == 0) return 1;
        var primeiro = vetor[0];
        if (primeiro.Caractere) return 1;
        if (primeiro.Referencia is Objeto o && (estruturas.ContainsKey(o.Classe) || classes.ContainsKey(o.Classe)))
            return BytesDoTipo(o.Classe);
        return Memoria.Tamanho(primeiro.EhTexto ? "char*" : primeiro.Inteiro ? "int" : "float", PerfilDoSizeof);
    }

    static string TipoLimpo(string tipo)
    {
        tipo = (tipo ?? "").Trim();
        foreach (string palavra in new[] { "const ", "volatile ", "static ", "struct ", "class ", "enum ", "union " })
            while (tipo.StartsWith(palavra, StringComparison.Ordinal)) tipo = tipo[palavra.Length..].TrimStart();
        return tipo.Replace(" *", "*").Trim();
    }

    /// <summary>O sizeof de um tipo nesta placa.</summary>
    int BytesDoTipo(string tipo) => MedirTipo(TipoLimpo(tipo), 0).Tamanho;

    /// <summary>
    /// Tamanho e alinhamento. No AVR nada se alinha (a struct e a soma
    /// dos campos); nas de 32 bits cada campo comeca num multiplo do seu
    /// tamanho, ate 8, e a struct termina num multiplo do maior deles --
    /// "struct { uint8_t a; int b; }" tem 3 bytes no Uno e 8 na ESP32.
    /// </summary>
    (int Tamanho, int Alinhamento) MedirTipo(string tipo, int profundidade)
    {
        var perfil = PerfilDoSizeof;
        int palavra = perfil.De8Bits ? 1 : 4;

        if (tipo.EndsWith('*')) return (BytesDoPonteiro, palavra);

        if (profundidade < 8 && CamposDoTipo(tipo) is { } campos)
        {
            int deslocamento = 0, maior = 1;

            // A classe com metodo virtual carrega o ponteiro da tabela.
            if (classes.TryGetValue(tipo, out var info))
            {
                foreach (var baseDaClasse in info.Bases)
                {
                    var (tb, ab) = MedirTipo(baseDaClasse.Nome, profundidade + 1);
                    deslocamento = Alinhar(deslocamento, ab) + tb;
                    maior = Math.Max(maior, ab);
                }
                if (info.Bases.Count == 0 && (info.Decl?.Metodos.Any(m => m.Virtual) ?? false))
                {
                    deslocamento += BytesDoPonteiro;
                    maior = Math.Max(maior, palavra);
                }
            }

            foreach (var campo in campos)
            {
                var (t, a) = campo switch
                {
                    CmdDecl d => BytesDoCampo(d, profundidade + 1),
                    CmdDeclObj o => MedirTipo(TipoLimpo(o.Classe), profundidade + 1),
                    _ => (0, 1),
                };
                deslocamento = Alinhar(deslocamento, a) + t;
                maior = Math.Max(maior, a);
            }

            // A struct vazia ocupa 1 byte no C++: dois objetos nunca moram
            // no mesmo endereco.
            return (Math.Max(1, Alinhar(deslocamento, maior)), maior);
        }

        int bytes = Memoria.Tamanho(tipo, perfil);
        if (perfil.De8Bits) return (bytes, 1);
        return (bytes, tipo == "String" ? 4 : Math.Clamp(bytes, 1, 8));
    }

    static int Alinhar(int deslocamento, int alinhamento)
        => alinhamento <= 1 ? deslocamento : (deslocamento + alinhamento - 1) / alinhamento * alinhamento;

    /// <summary>Os campos declarados de uma struct ou classe do sketch.</summary>
    IEnumerable<Cmd> CamposDoTipo(string tipo)
    {
        if (tipo is null) return null;
        if (estruturas.TryGetValue(tipo, out var molde)) return molde;
        if (classes.TryGetValue(tipo, out var info) && info.Decl is not null) return info.Decl.Campos;
        return null;
    }

    /// <summary>Um campo: o tipo dele, vezes os itens se for vetor.</summary>
    (int Tamanho, int Alinhamento) BytesDoCampo(CmdDecl campo, int profundidade = 0)
    {
        var (item, alinhamento) = MedirTipo(TipoLimpo(campo.Tipo), profundidade);
        if (!campo.EhArray) return (item, alinhamento);

        int Contar(Expr e)
        {
            try { return e is null ? 0 : (int)Avaliar(e, global).Num; }
            catch (ErroDeExecucao) { return 0; }
        }

        int linhas = campo.Tamanho is not null ? Contar(campo.Tamanho)
                   : campo.Itens?.Count ?? 0;
        int colunas = campo.Colunas is not null ? Math.Max(1, Contar(campo.Colunas)) : 1;
        return (Math.Max(0, linhas) * colunas * item, alinhamento);
    }

    static int TamanhoDoValor(Valor v)
        => v.EhTexto ? v.Texto.Length + 1 : v.Caractere ? 1 : v.Inteiro ? 4 : 4;

    static int TamanhoDoTipo(string tipo) => tipo switch
    {
        "char" or "bool" or "boolean" or "byte" or "uint8_t" or "int8_t" => 1,
        "short" or "uint16_t" or "int16_t" => 2,
        "double" or "int64_t" or "uint64_t" or "long long" => 8,
        _ => 4,
    };
}
