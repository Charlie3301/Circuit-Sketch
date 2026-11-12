using System.Text;

namespace Simulador;

/// <summary>
/// O modulo bluetooth do MicroPython (bluetooth.BLE) no radio BLE da bancada. O MicroPython
/// fala com a pilha por numeros (o handle de cada valor, o de cada conexao) e recebe tudo por
/// eventos (o irq do programa): aqui cada pedido do modbluetooth_cs.c vira o que a
/// <see cref="Rede"/> ja faz para a biblioteca BLE do Arduino, e o que acontece no radio volta
/// como texto, um evento por vez, com o numero do _IRQ_* na frente.
///
/// Os handles saem da ordem dos servicos, como no NimBLE: o servico, e para cada caracteristica
/// a declaracao, o valor, o CCCD (quando ela notifica ou indica) e os descritores. A mesma conta
/// vale para o servidor de outra placa, seja ela MicroPython ou Arduino.
/// </summary>
public partial class Interpretador
{
    RadioBle radioDoPython;
    readonly Queue<string> eventosDoPython = new();
    readonly Dictionary<ConexaoBle, int> conexoesDoPython = [];
    int celularNoPython = -1;
    string nomeGapDoPython = "MPY ESP32";
    ServicoBle servicoEmRegistro;
    CaracteristicaBle caracteristicaEmRegistro;

    bool varrendoNoPython, varreduraAtivaNoPython;
    double fimDaVarreduraNoPython;
    readonly Dictionary<RadioBle, double> proximoAnuncioNoPython = [];

    sealed class EntradaGatt(int handle, char tipo, ServicoBle servico, CaracteristicaBle car, DescritorBle desc)
    {
        public int Handle = handle, Fim = handle;

        /// <summary>S: o servico; V: o valor da caracteristica; C: o CCCD dela; X: um descritor.</summary>
        public char Tipo = tipo;
        public ServicoBle Servico = servico;
        public CaracteristicaBle Car = car;
        public DescritorBle Desc = desc;
    }

    /// <summary>Os handles do servidor GATT de um radio, na ordem em que o NimBLE os daria.</summary>
    static List<EntradaGatt> TabelaGatt(RadioBle servidor)
    {
        var tabela = new List<EntradaGatt>();
        // Os primeiros handles sao do GAP e do GATT que a pilha poe sozinha.
        int h = 16;
        foreach (var s in servidor.ServicosIniciados())
        {
            var doServico = new EntradaGatt(h++, 'S', s, null, null);
            tabela.Add(doServico);
            List<CaracteristicaBle> cars;
            lock (s.Caracteristicas) cars = [.. s.Caracteristicas];
            foreach (var c in cars)
            {
                h++;   // a declaracao da caracteristica
                var valor = new EntradaGatt(h++, 'V', s, c, null);
                tabela.Add(valor);
                if (c.Notifica || c.Indica) tabela.Add(new EntradaGatt(h++, 'C', s, c, null));
                List<DescritorBle> descritores;
                lock (c.Descritores) descritores = [.. c.Descritores];
                foreach (var d in descritores) tabela.Add(new EntradaGatt(h++, 'X', s, c, d));
                valor.Fim = h - 1;
            }
            doServico.Fim = h - 1;
        }
        return tabela;
    }

    /// <summary>O UUID como vai para o C: o de 16 bits curto ("180f"), o resto inteiro.</summary>
    static string UuidParaOPython(string uuid)
    {
        int curto = Ble.Curto(uuid);
        return curto >= 0 ? curto.ToString("x4") : Ble.Normalizar(uuid);
    }

    static string Hex(byte[] dados) => Convert.ToHexString(dados ?? []).ToLowerInvariant();

    static byte[] DeHex(string hex)
    {
        try { return Convert.FromHexString(hex ?? ""); }
        catch (FormatException) { return []; }
    }

    static string EnderecoEmHex(string mac) => (mac ?? "").Replace(":", "").ToLowerInvariant();

    int NovaConexaoDoPython()
    {
        for (int h = 0; ; h++)
            if (h != celularNoPython && !conexoesDoPython.ContainsValue(h)) return h;
    }

    /// <summary>O numero da conexao para o programa (nulo: o celular); o mesmo ate ela acabar.</summary>
    int ConexaoDoPython(ConexaoBle conexao)
    {
        if (conexao is null) return celularNoPython >= 0 ? celularNoPython : celularNoPython = NovaConexaoDoPython();
        if (!conexoesDoPython.TryGetValue(conexao, out int h)) conexoesDoPython[conexao] = h = NovaConexaoDoPython();
        return h;
    }

    /// <summary>A conexao de um numero. Verdadeiro se existe; a conexao nula e a do celular.</summary>
    bool AcharConexaoDoPython(long h, out ConexaoBle conexao)
    {
        conexao = null;
        if (h == celularNoPython && radioDoPython is { CelularConectado: true }) return true;
        foreach (var par in conexoesDoPython)
            if (par.Value == h && par.Key.Ativa)
            {
                conexao = par.Key;
                return true;
            }
        return false;
    }

    /// <summary>A conexao em que esta placa e o cliente (a central), e os handles do servidor dela.</summary>
    bool ComoClienteNoPython(long h, out ConexaoBle conexao, out List<EntradaGatt> tabela)
    {
        tabela = null;
        if (!AcharConexaoDoPython(h, out conexao) || conexao is null || conexao.Cliente != radioDoPython) return false;
        tabela = TabelaGatt(conexao.Servidor);
        return true;
    }

    /// <summary>
    /// "__mpble"(pedido, texto, a, b, c) -> texto. Os bytes vao e voltam em hexadecimal; "" e
    /// "nao deu" (ou "nao ha mais evento"), "0" e "feito".
    /// </summary>
    Valor BleDoPython(List<Valor> args)
    {
        string op = args.Count > 0 ? args[0].Texto ?? "" : "";
        string texto = args.Count > 1 ? args[1].Texto ?? "" : "";
        long N(int i) => args.Count > i ? (long)args[i].Num : 0;
        long a = N(2), b = N(3), c = N(4);
        var rede = maq.Rede;
        if (rede is null) return Valor.DeTexto("");

        if (op == "ligar")
        {
            string id = maq.Perfil?.Id ?? "";
            if (!id.StartsWith("esp32", StringComparison.Ordinal) || ModuloNativo.Chip(id) == "esp32s2")
            {
                maq.Avisar(Idioma.T("bluetooth.BLE().active(True): a {0} nao tem Bluetooth -- no MicroPython dela o modulo bluetooth nem existe.", maq.Perfil?.Nome ?? id));
                return Valor.DeTexto("");
            }
            if (!LigarRadio("ble", 0)) return Valor.DeTexto("");
            // Cada active(True) comeca com o nome de fabrica (o config(gap_name=...) vem depois).
            nomeGapDoPython = "MPY ESP32";
            radioDoPython = rede.IniciarBle(maq, nomeGapDoPython, nimble: true);
            radioDoPython.TemServidor = true;
            eventosDoPython.Clear();
            conexoesDoPython.Clear();
            celularNoPython = -1;
            varrendoNoPython = false;
            proximoAnuncioNoPython.Clear();
            servicoEmRegistro = null;
            caracteristicaEmRegistro = null;
            return Valor.DeTexto(radioDoPython.Endereco);
        }

        if (radioDoPython is not { } radio) return Valor.DeTexto("");
        switch (op)
        {
            case "desligar":
                rede.EsquecerBle(maq);
                DesligarRadio("ble");
                radioDoPython = null;
                eventosDoPython.Clear();
                conexoesDoPython.Clear();
                return Valor.DeTexto("0");

            case "nome":
                nomeGapDoPython = texto;
                if (!NomeNoAnuncio(radio)) radio.Nome = texto;
                return Valor.DeTexto("0");

            case "anunciar":
            {
                // "-": o que ja estava (o gap_advertise sem adv_data repete o anuncio de antes).
                var partes = texto.Split('|');
                if (partes[0] != "-") radio.CargaDoAnuncio = DeHex(partes[0]);
                if (partes.Length > 1 && partes[1] != "-") radio.RespostaDoAnuncio = DeHex(partes[1]);
                radio.Conectavel = a != 0;
                LerOAnuncioDoPython(radio);
                rede.AnunciarBle(radio, true);
                return Valor.DeTexto("0");
            }
            case "parar_anuncio":
                rede.AnunciarBle(radio, false);
                return Valor.DeTexto("0");

            // ---- o registro dos servicos (gatts_register_services) ----
            case "reg_inicio":
                if (a == 0) lock (radio.Servicos) radio.Servicos.Clear();
                servicoEmRegistro = null;
                caracteristicaEmRegistro = null;
                return Valor.DeTexto("0");
            case "reg_servico":
                servicoEmRegistro = new ServicoBle(radio, Ble.Normalizar(texto)) { Iniciado = true };
                lock (radio.Servicos) radio.Servicos.Add(servicoEmRegistro);
                return Valor.DeTexto("0");
            case "reg_caracteristica":
            {
                if (servicoEmRegistro is not { } servico) return Valor.DeTexto("");
                // Os bits do MicroPython sao os do NimBLE (e os do padrao): ler 2, escrever sem resposta 4...
                var car = new CaracteristicaBle(servico, Ble.Normalizar(texto), le: (a & 0x02) != 0, escreve: (a & 0x08) != 0,
                                                escreveSemResposta: (a & 0x04) != 0, notifica: (a & 0x10) != 0,
                                                indica: (a & 0x20) != 0, propriedades: (int)a);
                lock (servico.Caracteristicas) servico.Caracteristicas.Add(car);
                caracteristicaEmRegistro = car;
                var entrada = TabelaGatt(radio).First(e => e.Tipo == 'V' && e.Car == car);
                return Valor.DeTexto(entrada.Handle.ToString());
            }
            case "reg_descritor":
            {
                if (caracteristicaEmRegistro is not { } car) return Valor.DeTexto("");
                var d = new DescritorBle(car, Ble.Normalizar(texto), le: (a & 0x02) != 0, escreve: (a & 0x0C) != 0);
                lock (car.Descritores) car.Descritores.Add(d);
                return Valor.DeTexto(TabelaGatt(radio).First(e => e.Desc == d).Handle.ToString());
            }

            // ---- os valores daqui e o que sai para quem esta conectado ----
            case "valor":
            {
                var entrada = TabelaGatt(radio).FirstOrDefault(e => e.Handle == a);
                if (entrada is { Tipo: 'V' }) entrada.Car.Valor = DeHex(texto);
                else if (entrada is { Tipo: 'X' }) entrada.Desc.Valor = DeHex(texto);
                else return Valor.DeTexto("");
                return Valor.DeTexto("0");
            }
            case "atualizar":
            {
                // O gatts_write(..., send_update=True): vai para quem assinou.
                var entrada = TabelaGatt(radio).FirstOrDefault(e => e.Handle == a && e.Tipo == 'V');
                if (entrada is null) return Valor.DeTexto("");
                rede.NotificarDaPlaca(entrada.Car, indicacao: !entrada.Car.Notifica && entrada.Car.Indica);
                return Valor.DeTexto("0");
            }
            case "notificar":
            {
                var entrada = TabelaGatt(radio).FirstOrDefault(e => e.Handle == b && e.Tipo == 'V');
                if (entrada is null || !AcharConexaoDoPython(a, out var conexao)) return Valor.DeTexto("");
                if (conexao is not null && conexao.Servidor != radio) return Valor.DeTexto("");
                byte[] dados = texto == "-" ? entrada.Car.Valor : DeHex(texto);
                if (!rede.NotificarPara(entrada.Car, conexao, dados, indicacao: c != 0)) return Valor.DeTexto("");
                // A indicacao tem resposta do outro lado: o _IRQ_GATTS_INDICATE_DONE.
                if (c != 0) eventosDoPython.Enqueue($"20|{a}|{b}|0");
                return Valor.DeTexto("0");
            }

            case "desconectar":
            {
                if (!AcharConexaoDoPython(a, out var conexao)) return Valor.DeTexto("");
                if (conexao is null) rede.DesconectarBleDoCelular(pelaPlaca: true);
                else rede.DesconectarBlePlaca(conexao);
                return Valor.DeTexto("0");
            }
            case "pareou":
                // O pareamento da bancada da sempre certo: a conexao passa a cifrada (b: com vinculo).
                if (!AcharConexaoDoPython(a, out _)) return Valor.DeTexto("");
                eventosDoPython.Enqueue($"28|{a}|{b}");
                return Valor.DeTexto("0");
            case "mtu":
            {
                if (!AcharConexaoDoPython(a, out var conexao)) return Valor.DeTexto("");
                int mtu = (int)Math.Clamp(b, 23, 517);
                if (conexao is not null) conexao.Mtu = mtu;
                eventosDoPython.Enqueue($"21|{a}|{mtu}");
                return Valor.DeTexto("0");
            }

            // ---- a central: procurar e conectar ----
            case "varrer":
                varrendoNoPython = true;
                varreduraAtivaNoPython = b != 0;
                fimDaVarreduraNoPython = a > 0 ? maq.AgoraUs() + a * 1000.0 : double.MaxValue;
                proximoAnuncioNoPython.Clear();
                return Valor.DeTexto("0");
            case "parar_varredura":
                if (varrendoNoPython)
                {
                    varrendoNoPython = false;
                    eventosDoPython.Enqueue("6");
                }
                return Valor.DeTexto("0");
            case "conectar":
            {
                // A conexao leva o seu tempo no ar (~50 ms).
                maq.Avancar(50);
                string mac = string.Join(":", Enumerable.Range(0, texto.Length / 2).Select(i => texto.Substring(2 * i, 2)));
                var nova = rede.ConectarBlePlaca(radio, rede.RadioBleDe(mac));
                // Sem ninguem la, o NimBLE avisa a desconexao de uma conexao que nao houve (0xffff).
                if (nova is null) eventosDoPython.Enqueue($"8|65535|{a}|{texto}");
                else eventosDoPython.Enqueue($"7|{ConexaoDoPython(nova)}|{a}|{texto}");
                return Valor.DeTexto("0");
            }

            // ---- o cliente GATT: os resultados voltam como eventos ----
            case "desc_servicos":
            {
                if (!ComoClienteNoPython(a, out _, out var tabela)) return Valor.DeTexto("");
                string filtro = texto.Length > 0 ? Ble.Normalizar(texto) : null;
                foreach (var e in tabela.Where(e => e.Tipo == 'S' && (filtro is null || e.Servico.Uuid == filtro)))
                    eventosDoPython.Enqueue($"9|{a}|{e.Handle}|{e.Fim}|{UuidParaOPython(e.Servico.Uuid)}");
                eventosDoPython.Enqueue($"10|{a}|0");
                return Valor.DeTexto("0");
            }
            case "desc_caracteristicas":
            {
                if (!ComoClienteNoPython(a, out _, out var tabela)) return Valor.DeTexto("");
                string filtro = texto.Length > 0 ? Ble.Normalizar(texto) : null;
                foreach (var e in tabela.Where(e => e.Tipo == 'V' && e.Handle >= b && e.Handle <= c && (filtro is null || e.Car.Uuid == filtro)))
                {
                    int props = (e.Car.Le ? 0x02 : 0) | (e.Car.EscreveSemResposta ? 0x04 : 0) | (e.Car.Escreve ? 0x08 : 0)
                                | (e.Car.Notifica ? 0x10 : 0) | (e.Car.Indica ? 0x20 : 0);
                    eventosDoPython.Enqueue($"11|{a}|{e.Fim}|{e.Handle}|{props}|{UuidParaOPython(e.Car.Uuid)}");
                }
                eventosDoPython.Enqueue($"12|{a}|0");
                return Valor.DeTexto("0");
            }
            case "desc_descritores":
            {
                if (!ComoClienteNoPython(a, out _, out var tabela)) return Valor.DeTexto("");
                // O pedido do ATT (Find Information) devolve tudo o que ha depois do primeiro handle ate o
                // ultimo: com a faixa de uma caracteristica so (do valor ao fim dela), sao os descritores;
                // com a faixa larga vem tambem as declaracoes (0x2800, 0x2803) e os valores, como na placa.
                foreach (var e in tabela)
                {
                    if (e.Tipo == 'V' && e.Handle - 1 > b && e.Handle - 1 <= c) eventosDoPython.Enqueue($"13|{a}|{e.Handle - 1}|2803");
                    if (e.Handle <= b || e.Handle > c) continue;
                    string uuid = e.Tipo switch
                    {
                        'S' => "2800",
                        'V' => UuidParaOPython(e.Car.Uuid),
                        'C' => "2902",
                        _ => UuidParaOPython(e.Desc.Uuid),
                    };
                    eventosDoPython.Enqueue($"13|{a}|{e.Handle}|{uuid}");
                }
                eventosDoPython.Enqueue($"14|{a}|0");
                return Valor.DeTexto("0");
            }
            case "ler":
            {
                if (!ComoClienteNoPython(a, out var conexao, out var tabela)) return Valor.DeTexto("");
                var e = tabela.FirstOrDefault(x => x.Handle == b);
                byte[] dados = e switch
                {
                    { Tipo: 'V' } => rede.LerDaPlaca(conexao, e.Car),
                    { Tipo: 'C' } => [(byte)(conexao.Assinou(e.Car) ? (e.Car.Notifica ? 1 : 2) : 0), 0],
                    { Tipo: 'X' } => e.Desc.Le ? e.Desc.Valor : null,
                    _ => null,
                };
                if (dados is not null) eventosDoPython.Enqueue($"15|{a}|{b}|{Hex(dados)}");
                // 2: o "read not permitted" do ATT; 1: o handle nao existe.
                eventosDoPython.Enqueue($"16|{a}|{b}|{(dados is not null ? 0 : e is null ? 1 : 2)}");
                return Valor.DeTexto("0");
            }
            case "escrever":
            {
                if (!ComoClienteNoPython(a, out var conexao, out var tabela)) return Valor.DeTexto("");
                var e = tabela.FirstOrDefault(x => x.Handle == b);
                byte[] dados = DeHex(texto);
                bool ok = e switch
                {
                    { Tipo: 'V' } => rede.EscreverDaPlaca(conexao, e.Car, dados, comResposta: c == 1),
                    // O CCCD: 1 liga as notificacoes, 2 as indicacoes, 0 desliga.
                    { Tipo: 'C' } => rede.AssinarDaPlaca(conexao, e.Car, ligar: dados.Length > 0 && (dados[0] & 3) != 0,
                                                         indicacao: dados.Length > 0 && (dados[0] & 3) == 2),
                    { Tipo: 'X' } => rede.EscreverDescritorDaPlaca(conexao, e.Desc, dados),
                    _ => false,
                };
                if (c == 1) eventosDoPython.Enqueue($"17|{a}|{b}|{(ok ? 0 : e is null ? 1 : 3)}");
                return Valor.DeTexto("0");
            }

            case "evento":
                ColherOsEventosDoRadioDoPython(radio);
                AndarComAVarreduraDoPython(radio);
                return Valor.DeTexto(eventosDoPython.TryDequeue(out string proximo) ? proximo : "");
        }
        return Valor.DeTexto("");
    }

    /// <summary>O anuncio tem o nome dentro (a estrutura 0x08 ou 0x09)?</summary>
    static bool NomeNoAnuncio(RadioBle radio)
        => Estruturas(radio.CargaDoAnuncio).Concat(Estruturas(radio.RespostaDoAnuncio)).Any(e => e.Tipo is 0x08 or 0x09);

    /// <summary>As estruturas de um anuncio: o tamanho, o tipo e os dados de cada uma.</summary>
    static IEnumerable<(byte Tipo, byte[] Dados)> Estruturas(byte[] carga)
    {
        if (carga is null) yield break;
        for (int i = 0; i + 1 < carga.Length && carga[i] > 0 && i + 1 + carga[i] <= carga.Length; i += 1 + carga[i])
            yield return (carga[i + 1], carga[(i + 2)..(i + 1 + carga[i])]);
    }

    /// <summary>
    /// O que o anuncio em bytes diz, nos campos que o celular e a varredura das outras placas
    /// leem: o nome, os servicos, o dado do fabricante e o dado de servico.
    /// </summary>
    void LerOAnuncioDoPython(RadioBle radio)
    {
        radio.Nome = nomeGapDoPython;
        radio.UuidsAnunciados.Clear();
        radio.FabricanteHex = "";
        radio.ServicoUuid = "";
        radio.ServicoHex = "";
        foreach (var (tipo, d) in Estruturas(radio.CargaDoAnuncio).Concat(Estruturas(radio.RespostaDoAnuncio)))
        {
            switch (tipo)
            {
                case 0x08 or 0x09:
                    radio.Nome = Encoding.UTF8.GetString(d);
                    break;
                case 0x02 or 0x03:
                    for (int i = 0; i + 1 < d.Length; i += 2) radio.UuidsAnunciados.Add(Ble.Normalizar($"{d[i + 1]:x2}{d[i]:x2}"));
                    break;
                case 0x04 or 0x05:
                    for (int i = 0; i + 3 < d.Length; i += 4)
                        radio.UuidsAnunciados.Add(Ble.Normalizar($"{d[i + 3]:x2}{d[i + 2]:x2}{d[i + 1]:x2}{d[i]:x2}"));
                    break;
                case 0x06 or 0x07:
                    for (int i = 0; i + 15 < d.Length; i += 16)
                        radio.UuidsAnunciados.Add(Ble.Normalizar(Hex([.. Enumerable.Reverse(d[i..(i + 16)])])));
                    break;
                case 0x16 when d.Length >= 2:
                    radio.ServicoUuid = $"{d[1]:x2}{d[0]:x2}";
                    radio.ServicoHex = Hex(d[2..]);
                    break;
                case 0xFF:
                    radio.FabricanteHex = Hex(d);
                    break;
            }
        }
    }

    /// <summary>
    /// O anuncio de uma placa como chega no ar. A que anuncia pelo MicroPython ja o tem em bytes;
    /// a do Arduino tem os campos, e os bytes saem deles (o que nao cabe nos 31 vai na resposta).
    /// </summary>
    static (byte[] Anuncio, byte[] Resposta) AnuncioNoAr(RadioBle r)
    {
        if (r.CargaDoAnuncio is not null) return (r.CargaDoAnuncio, r.RespostaDoAnuncio);
        var anuncio = new List<byte> { 2, 0x01, 0x06 };
        var resposta = new List<byte>();
        void Por(int tipo, byte[] dados)
        {
            var onde = anuncio.Count + dados.Length + 2 <= 31 ? anuncio : resposta;
            if (onde.Count + dados.Length + 2 > 31) return;
            onde.Add((byte)(dados.Length + 1));
            onde.Add((byte)tipo);
            onde.AddRange(dados);
        }
        foreach (string u in r.UuidsAnunciados.ToArray())
        {
            int curto = Ble.Curto(u);
            if (curto >= 0) Por(0x03, [(byte)curto, (byte)(curto >> 8)]);
            else Por(0x07, [.. Enumerable.Reverse(DeHex(Ble.Normalizar(u).Replace("-", "")))]);
        }
        if (r.ServicoHex is { Length: > 0 } && r.ServicoUuid is { Length: 4 } su)
            Por(0x16, [Convert.ToByte(su[2..4], 16), Convert.ToByte(su[..2], 16), .. DeHex(r.ServicoHex)]);
        if (r.FabricanteHex is { Length: > 0 }) Por(0xFF, DeHex(r.FabricanteHex));
        if (!string.IsNullOrEmpty(r.Nome)) Por(0x09, Encoding.UTF8.GetBytes(r.Nome));
        return ([.. anuncio], resposta.Count > 0 ? [.. resposta] : null);
    }

    /// <summary>
    /// A varredura do gap_scan: cada placa anunciando aparece de novo a cada anuncio dela (o
    /// MicroPython nao filtra os repetidos), ate o tempo acabar.
    /// </summary>
    void AndarComAVarreduraDoPython(RadioBle radio)
    {
        if (!varrendoNoPython) return;
        double agora = maq.AgoraUs();
        if (eventosDoPython.Count < 64)
        {
            foreach (var r in maq.Rede.AnunciosBleVistosDe(maq))
            {
                if (proximoAnuncioNoPython.TryGetValue(r, out double quando) && agora < quando) continue;
                proximoAnuncioNoPython[r] = agora + 100_000;
                var (anuncio, resposta) = AnuncioNoAr(r);
                // O tipo do anuncio: 0 o que aceita conexao (ADV_IND), 2 o que so se deixa varrer, 4 a resposta.
                eventosDoPython.Enqueue($"5|0|{EnderecoEmHex(r.Endereco)}|{(r.Conectavel ? 0 : 2)}|{r.Rssi}|{Hex(anuncio)}");
                if (varreduraAtivaNoPython && resposta is { Length: > 0 })
                    eventosDoPython.Enqueue($"5|0|{EnderecoEmHex(r.Endereco)}|4|{r.Rssi}|{Hex(resposta)}");
            }
        }
        if (agora < fimDaVarreduraNoPython) return;
        varrendoNoPython = false;
        eventosDoPython.Enqueue("6");
    }

    /// <summary>O que o celular e as outras placas fizeram neste radio desde a ultima vez.</summary>
    void ColherOsEventosDoRadioDoPython(RadioBle radio)
    {
        for (int volta = 0; volta < 50 && radio.ProximoEvento(out var ev); volta++)
        {
            switch (ev.Tipo)
            {
                case TipoDeEventoBle.Conectou:
                {
                    // O celular usa endereco aleatorio (tipo 1); a placa, o publico dela.
                    string endereco = ev.Conexao is null ? Hex(EnderecoDoCelular) : EnderecoEmHex(ev.Conexao.Cliente.Endereco);
                    eventosDoPython.Enqueue($"1|{ConexaoDoPython(ev.Conexao)}|{(ev.Conexao is null ? 1 : 0)}|{endereco}");
                    break;
                }
                case TipoDeEventoBle.Desconectou:
                {
                    string endereco = ev.Conexao is null ? Hex(EnderecoDoCelular) : EnderecoEmHex(ev.Conexao.Cliente.Endereco);
                    eventosDoPython.Enqueue($"2|{ConexaoDoPython(ev.Conexao)}|{(ev.Conexao is null ? 1 : 0)}|{endereco}");
                    if (ev.Conexao is null) celularNoPython = -1;
                    else conexoesDoPython.Remove(ev.Conexao);
                    break;
                }
                case TipoDeEventoBle.Caiu when ev.Conexao is not null:
                    eventosDoPython.Enqueue($"8|{ConexaoDoPython(ev.Conexao)}|0|{EnderecoEmHex(ev.Conexao.Servidor.Endereco)}");
                    conexoesDoPython.Remove(ev.Conexao);
                    break;

                case TipoDeEventoBle.Escreveu:
                case TipoDeEventoBle.Leu:
                {
                    var entrada = TabelaGatt(radio).FirstOrDefault(e => e.Car == ev.Caracteristica
                                                                        && (ev.Descritor is null ? e.Tipo == 'V' : e.Desc == ev.Descritor));
                    if (entrada is null) break;
                    int conexao = ConexaoDoPython(ev.Conexao);
                    if (ev.Tipo == TipoDeEventoBle.Leu) eventosDoPython.Enqueue($"4|{conexao}|{entrada.Handle}");
                    else eventosDoPython.Enqueue($"3|{conexao}|{entrada.Handle}|{Hex(ev.Dados ?? ev.Caracteristica.Valor)}");
                    break;
                }

                case TipoDeEventoBle.Notificou when ev.Conexao is not null:
                {
                    var entrada = TabelaGatt(ev.Conexao.Servidor).FirstOrDefault(e => e.Tipo == 'V' && e.Car == ev.Caracteristica);
                    if (entrada is null) break;
                    // 18 e a notificacao; 19, a indicacao.
                    eventosDoPython.Enqueue($"{(ev.Valor == 1 ? 18 : 19)}|{ConexaoDoPython(ev.Conexao)}|{entrada.Handle}|{Hex(ev.Dados)}");
                    break;
                }
            }
        }
    }
}
