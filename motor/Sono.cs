namespace Simulador;

/// <summary>
/// A placa comeca de novo pelo setup(): ESP.restart(), esp_restart(), ou o
/// despertar do sono profundo.
///
/// Quem roda o sketch pega isto e monta um interpretador novo, com esta
/// excecao em <see cref="Interpretador.Origem"/> -- e dela que saem o
/// motivo do reinicio, a causa do despertar e o que estava na memoria RTC.
///
/// E uma <see cref="ExecucaoInterrompida"/> de proposito: todo lugar que ja
/// deixa a parada passar (a rede, o MQTT) deixa o reinicio passar tambem.
/// Um "if (msg == "reboot") ESP.restart();" dentro do callback do MQTT e
/// o caso mais comum de todos.
/// </summary>
public sealed class ReinicioDaPlaca(int razao, int causa, ulong pinos,
                                    IReadOnlyDictionary<string, object> memoriaRtc) : ExecucaoInterrompida
{
    /// <summary>O esp_reset_reason_t: 3 e o ESP_RST_SW, 8 o ESP_RST_DEEPSLEEP.</summary>
    public int Razao { get; } = razao;

    /// <summary>O esp_sleep_wakeup_cause_t de quem acordou a placa.</summary>
    public int Causa { get; } = causa;

    /// <summary>Os pinos do ext1 (ou do GPIO) que acordaram a placa.</summary>
    public ulong Pinos { get; } = pinos;

    /// <summary>As variaveis RTC_DATA_ATTR e RTC_NOINIT_ATTR, pelo nome.</summary>
    public IReadOnlyDictionary<string, object> MemoriaRtc { get; } = memoriaRtc;

    public const int PeloSoftware = 3;
    public const int PeloPanico = 4;

    /// <summary>ESP_RST_TASK_WDT: o watchdog de software (o do loop, o soft WDT do ESP8266).</summary>
    public const int PeloWatchdog = 6;

    /// <summary>ESP_RST_WDT: o de hardware (o do AVR e o do RP2040 armados pelo sketch, o duro do ESP8266).</summary>
    public const int PeloWatchdogDeHardware = 7;
    public const int PeloSonoProfundo = 8;

    /// <summary>A pilha encostou na heap (AVR, STM32): o simulador reinicia no lugar da corrupcao.</summary>
    public const int PelaMemoria = 90;

    /// <summary>O que o monitor serial mostraria da placa reiniciando.</summary>
    public string Resumo => Razao switch
    {
        PeloSonoProfundo => Idioma.T("acordou do sono profundo ({0})", Interpretador.NomeDaCausa(Causa)),
        PeloPanico => Idioma.T("reiniciou depois de um panico (Guru Meditation)"),
        PeloWatchdog or PeloWatchdogDeHardware => Idioma.T("reiniciou pelo watchdog"),
        PelaMemoria => Idioma.T("reiniciou: a memoria acabou (a pilha encostou na heap)"),
        _ => Idioma.T("reiniciou pelo software"),
    };
}

public partial class Interpretador
{
    /// <summary>
    /// O reinicio que comecou esta rodada. Nulo e a placa que acabou de
    /// receber energia -- o ESP_RST_POWERON.
    /// </summary>
    public ReinicioDaPlaca Origem { get; set; }

    // As fontes de despertar armadas: o que o esp_sleep_enable_* pediu.
    long despertarDoTimerUs = -1;
    int ext0Pino = -1, ext0Nivel;
    ulong ext1Mascara;
    int ext1Modo;
    ulong gpioMascara;
    int gpioModo;
    bool gpioNoSonoLeve;
    readonly Dictionary<int, int> gpioDoSonoLeve = [];
    readonly HashSet<int> pinosSegurados = [];

    /// <summary>A causa do ultimo sono leve, que o sketch le depois do esp_light_sleep_start.</summary>
    int causaDoSonoLeve;
    ulong pinosDoSonoLeve;

    static readonly Dictionary<string, Valor> ConstantesDoSono = new()
    {
        // esp_sleep_source_t, na ordem do esp_sleep.h.
        ["ESP_SLEEP_WAKEUP_UNDEFINED"] = Valor.DeInt(0),
        ["ESP_SLEEP_WAKEUP_ALL"] = Valor.DeInt(1),
        ["ESP_SLEEP_WAKEUP_EXT0"] = Valor.DeInt(2),
        ["ESP_SLEEP_WAKEUP_EXT1"] = Valor.DeInt(3),
        ["ESP_SLEEP_WAKEUP_TIMER"] = Valor.DeInt(4),
        ["ESP_SLEEP_WAKEUP_TOUCHPAD"] = Valor.DeInt(5),
        ["ESP_SLEEP_WAKEUP_ULP"] = Valor.DeInt(6),
        ["ESP_SLEEP_WAKEUP_GPIO"] = Valor.DeInt(7),
        ["ESP_SLEEP_WAKEUP_UART"] = Valor.DeInt(8),
        ["ESP_SLEEP_WAKEUP_WIFI"] = Valor.DeInt(9),
        ["ESP_SLEEP_WAKEUP_COCPU"] = Valor.DeInt(10),
        ["ESP_SLEEP_WAKEUP_COCPU_TRAP_TRIG"] = Valor.DeInt(11),
        ["ESP_SLEEP_WAKEUP_BT"] = Valor.DeInt(12),

        // esp_sleep_ext1_wakeup_mode_t. O ALL_LOW da ESP32 virou ANY_LOW
        // nas mais novas; o nome velho continua valendo zero.
        ["ESP_EXT1_WAKEUP_ALL_LOW"] = Valor.DeInt(0),
        ["ESP_EXT1_WAKEUP_ANY_LOW"] = Valor.DeInt(0),
        ["ESP_EXT1_WAKEUP_ANY_HIGH"] = Valor.DeInt(1),
        ["ESP_GPIO_WAKEUP_GPIO_LOW"] = Valor.DeInt(0),
        ["ESP_GPIO_WAKEUP_GPIO_HIGH"] = Valor.DeInt(1),
        ["GPIO_INTR_LOW_LEVEL"] = Valor.DeInt(4),
        ["GPIO_INTR_HIGH_LEVEL"] = Valor.DeInt(5),

        // esp_reset_reason_t.
        ["ESP_RST_UNKNOWN"] = Valor.DeInt(0),
        ["ESP_RST_POWERON"] = Valor.DeInt(1),
        ["ESP_RST_EXT"] = Valor.DeInt(2),
        ["ESP_RST_SW"] = Valor.DeInt(3),
        ["ESP_RST_PANIC"] = Valor.DeInt(4),
        ["ESP_RST_INT_WDT"] = Valor.DeInt(5),
        ["ESP_RST_TASK_WDT"] = Valor.DeInt(6),
        ["ESP_RST_WDT"] = Valor.DeInt(7),
        ["ESP_RST_DEEPSLEEP"] = Valor.DeInt(8),
        ["ESP_RST_BROWNOUT"] = Valor.DeInt(9),
        ["ESP_RST_SDIO"] = Valor.DeInt(10),

        // esp_sleep_pd_config: o que fica ligado durante o sono.
        ["ESP_PD_DOMAIN_RTC_PERIPH"] = Valor.DeInt(0),
        ["ESP_PD_DOMAIN_RTC_SLOW_MEM"] = Valor.DeInt(1),
        ["ESP_PD_DOMAIN_RTC_FAST_MEM"] = Valor.DeInt(2),
        ["ESP_PD_DOMAIN_XTAL"] = Valor.DeInt(3),
        ["ESP_PD_OPTION_OFF"] = Valor.DeInt(0),
        ["ESP_PD_OPTION_ON"] = Valor.DeInt(1),
        ["ESP_PD_OPTION_AUTO"] = Valor.DeInt(2),

        ["ESP_ERR_INVALID_ARG"] = Valor.DeInt(0x102),
        ["ESP_ERR_INVALID_STATE"] = Valor.DeInt(0x103),
        ["ESP_ERR_NOT_SUPPORTED"] = Valor.DeInt(0x106),
        ["GPIO_NUM_NC"] = Valor.DeInt(-1),
    };

    /// <summary>A placa e da familia ESP32 (ESP32, S2, S3, C3, C6, H2)?</summary>
    bool EhDaFamiliaEsp32 => maq.Perfil?.Id?.StartsWith("esp32", StringComparison.Ordinal) ?? false;

    string ChipDoSono => maq.Perfil?.Id switch
    {
        "esp32-s2-devkit" => "ESP32-S2",
        "esp32-s3-devkit" => "ESP32-S3",
        "esp32-c3-devkit" => "ESP32-C3",
        "esp32-c6-devkit" => "ESP32-C6",
        "esp32-h2-devkit" => "ESP32-H2",
        _ => "ESP32",
    };

    /// <summary>"GPIO_NUM_33" e as constantes do esp_sleep.h -- so na familia ESP32.</summary>
    bool ConstanteDoSono(string nome, out Valor valor)
    {
        valor = Valor.Vazio;
        if (!EhDaFamiliaEsp32) return false;
        if (ConstantesDoSono.TryGetValue(nome, out valor)) return true;

        if (nome.StartsWith("GPIO_NUM_", StringComparison.Ordinal)
            && int.TryParse(nome.AsSpan(9), out int gpio) && gpio >= 0 && gpio <= MaiorGpio)
        {
            valor = Valor.DeInt(gpio);
            return true;
        }
        return false;
    }

    int MaiorGpio => ChipDoSono switch
    {
        "ESP32-S2" => 46,
        "ESP32-S3" => 48,
        "ESP32-C3" => 21,
        "ESP32-C6" => 30,
        "ESP32-H2" => 27,
        _ => 39,
    };

    /// <summary>
    /// Os pinos que o dominio RTC enxerga durante o sono -- os unicos que
    /// o ext0 e o ext1 aceitam. Na ESP32 sao espalhados; na S2 e na S3,
    /// do 0 ao 21.
    /// </summary>
    bool EhPinoRtc(int gpio) => ChipDoSono switch
    {
        "ESP32" => gpio is 0 or 2 or 4 or (>= 12 and <= 15) or (>= 25 and <= 27) or (>= 32 and <= 39),
        "ESP32-S2" or "ESP32-S3" => gpio is >= 0 and <= 21,
        "ESP32-C6" => gpio is >= 0 and <= 7,
        "ESP32-H2" => gpio is >= 7 and <= 14,
        "ESP32-C3" => gpio is >= 0 and <= 5,
        _ => false,
    };

    static bool EhDoSono(string nome)
        => nome.StartsWith("esp_sleep_", StringComparison.Ordinal)
           || nome.StartsWith("esp_deep_sleep", StringComparison.Ordinal)
           || nome.StartsWith("rtc_gpio_", StringComparison.Ordinal)
           || nome is "esp_light_sleep_start" or "esp_restart" or "esp_reset_reason"
               or "gpio_hold_en" or "gpio_hold_dis" or "gpio_deep_sleep_hold_en" or "gpio_deep_sleep_hold_dis"
               or "gpio_wakeup_enable" or "gpio_wakeup_disable";

    /// <summary>Nome da causa, para o aviso e o monitor.</summary>
    public static string NomeDaCausa(int causa) => causa switch
    {
        2 => Idioma.T("EXT0, pelo pino"),
        3 => Idioma.T("EXT1, pelos pinos"),
        4 => Idioma.T("pelo timer"),
        7 => Idioma.T("pelo GPIO"),
        _ => Idioma.T("sem causa de despertar"),
    };

    bool ChamarSono(string nome, Chamada c, Ambiente amb, out Valor resultado)
    {
        resultado = Valor.DeInt(0);   // ESP_OK
        if (!EhDaFamiliaEsp32)
            throw new ErroDeExecucao(Idioma.T("{0}() e do ESP-IDF da ESP32: esta placa nao tem.", nome), c.Linha);

        double A(int i) => i < c.Args.Count ? Avaliar(c.Args[i], amb).Num : 0;
        string chip = ChipDoSono;

        switch (nome)
        {
            case "esp_sleep_enable_timer_wakeup":
                despertarDoTimerUs = (long)A(0);
                return true;

            case "esp_sleep_enable_ext0_wakeup":
            {
                // O ext0 so existe onde o RTC tem o seu proprio leitor de
                // pino: ESP32, S2 e S3. Na C3 a funcao nem compila.
                if (chip is not ("ESP32" or "ESP32-S2" or "ESP32-S3"))
                    throw new ErroDeExecucao(
                        Idioma.T("esp_sleep_enable_ext0_wakeup() nao existe na {0}: {1}", chip, (chip == "ESP32-C3"
                            ? Idioma.T("use esp_deep_sleep_enable_gpio_wakeup(mascara, ESP_GPIO_WAKEUP_GPIO_HIGH).")
                            : Idioma.T("use esp_sleep_enable_ext1_wakeup(mascara, modo)."))), c.Linha);

                int gpio = (int)A(0);
                if (!EhPinoRtc(gpio))
                {
                    maq.Avisar(Idioma.T("esp_sleep_enable_ext0_wakeup({0}): o GPIO {1} nao e do RTC na {2}, e a placa nao acordaria por ele (a funcao devolve ESP_ERR_INVALID_ARG).", gpio, gpio, chip));
                    resultado = Valor.DeInt(0x102);
                    return true;
                }
                ext0Pino = gpio;
                ext0Nivel = A(1) != 0 ? 1 : 0;
                return true;
            }

            case "esp_sleep_enable_ext1_wakeup":
            case "esp_sleep_enable_ext1_wakeup_io":
            {
                if (chip == "ESP32-C3")
                    throw new ErroDeExecucao(
                        Idioma.T("esp_sleep_enable_ext1_wakeup() nao existe na ESP32-C3: use esp_deep_sleep_enable_gpio_wakeup(mascara, ESP_GPIO_WAKEUP_GPIO_HIGH)."), c.Linha);

                ulong mascara = (ulong)A(0);
                var foraDoRtc = PinosDaMascara(mascara).Where(g => !EhPinoRtc(g)).ToList();
                if (foraDoRtc.Count > 0)
                {
                    maq.Avisar(foraDoRtc.Count == 1
                        ? Idioma.T("esp_sleep_enable_ext1_wakeup: o GPIO {0} nao e do RTC na {1} (a funcao devolve ESP_ERR_INVALID_ARG).", foraDoRtc[0], chip)
                        : Idioma.T("esp_sleep_enable_ext1_wakeup: os GPIO {0} nao sao do RTC na {1} (a funcao devolve ESP_ERR_INVALID_ARG).", string.Join(", ", foraDoRtc), chip));
                    resultado = Valor.DeInt(0x102);
                    return true;
                }
                ext1Mascara = nome.EndsWith("_io", StringComparison.Ordinal) ? ext1Mascara | mascara : mascara;
                ext1Modo = A(1) != 0 ? 1 : 0;
                return true;
            }

            case "esp_deep_sleep_enable_gpio_wakeup":
            {
                if (chip is not ("ESP32-C3" or "ESP32-C6" or "ESP32-H2"))
                    throw new ErroDeExecucao(
                        Idioma.T("esp_deep_sleep_enable_gpio_wakeup() e das ESP32-C3, C6 e H2: na {0} use esp_sleep_enable_ext0_wakeup() ou esp_sleep_enable_ext1_wakeup().", chip), c.Linha);
                ulong mascara = (ulong)A(0);
                if (PinosDaMascara(mascara).Any(g => !EhPinoRtc(g)))
                {
                    maq.Avisar(Idioma.T("esp_deep_sleep_enable_gpio_wakeup: na {0} so os pinos do RTC acordam a placa (a funcao devolve ESP_ERR_INVALID_ARG).", chip));
                    resultado = Valor.DeInt(0x102);
                    return true;
                }
                gpioMascara = mascara;
                gpioModo = A(1) != 0 ? 1 : 0;
                return true;
            }

            // Sono leve: "gpio_wakeup_enable(GPIO_NUM_0, GPIO_INTR_LOW_LEVEL)"
            // mais "esp_sleep_enable_gpio_wakeup()".
            case "gpio_wakeup_enable":
                gpioDoSonoLeve[(int)A(0)] = (int)A(1) == 5 ? 1 : 0;
                return true;
            case "gpio_wakeup_disable":
                gpioDoSonoLeve.Remove((int)A(0));
                return true;
            case "esp_sleep_enable_gpio_wakeup":
                gpioNoSonoLeve = true;
                return true;

            case "esp_sleep_disable_wakeup_source":
            {
                int fonte = (int)A(0);
                if (fonte is 1 or 4) despertarDoTimerUs = -1;
                if (fonte is 1 or 2) ext0Pino = -1;
                if (fonte is 1 or 3) ext1Mascara = 0;
                if (fonte is 1 or 7) { gpioMascara = 0; gpioNoSonoLeve = false; }
                return true;
            }

            case "esp_sleep_get_wakeup_cause":
                resultado = Valor.DeInt(causaDoSonoLeve != 0 ? causaDoSonoLeve
                                        : Origem?.Razao == ReinicioDaPlaca.PeloSonoProfundo ? Origem.Causa : 0);
                return true;

            case "esp_sleep_get_ext1_wakeup_status":
            case "esp_sleep_get_gpio_wakeup_status":
                resultado = Valor.DeInt(pinosDoSonoLeve != 0 ? pinosDoSonoLeve
                                        : Origem?.Razao == ReinicioDaPlaca.PeloSonoProfundo ? Origem.Pinos : 0);
                return true;

            case "esp_reset_reason":
                resultado = Valor.DeInt(Origem?.Razao ?? 1);
                return true;

            case "esp_restart":
                throw Reiniciar(ReinicioDaPlaca.PeloSoftware, 0, 0);

            case "esp_deep_sleep":
                despertarDoTimerUs = (long)A(0);
                DormirProfundo(c.Linha);
                return true;

            case "esp_deep_sleep_start":
                DormirProfundo(c.Linha);
                return true;

            case "esp_light_sleep_start":
                DormirLeve();
                return true;

            // O que fica segurado durante o sono: o pino mantem o nivel.
            case "gpio_hold_en":
            case "rtc_gpio_hold_en":
                pinosSegurados.Add((int)A(0));
                return true;
            case "gpio_hold_dis":
            case "rtc_gpio_hold_dis":
                pinosSegurados.Remove((int)A(0));
                return true;

            // Resistores do RTC, isolamento, dominios de energia e o
            // "segurar todos": configuram o hardware do sono e nao mudam
            // nada que o simulador desenhe.
            case "gpio_deep_sleep_hold_en":
            case "gpio_deep_sleep_hold_dis":
            case "esp_sleep_pd_config":
                return true;
        }

        if (nome.StartsWith("rtc_gpio_", StringComparison.Ordinal)) return true;

        throw new ErroDeExecucao(Idioma.T("{0}() ainda nao e suportado pelo simulador.", nome), c.Linha);
    }

    static IEnumerable<int> PinosDaMascara(ulong mascara)
    {
        for (int g = 0; g < 64; g++)
            if ((mascara & (1UL << g)) != 0) yield return g;
    }

    /// <summary>Monta o reinicio, guardando o que mora na memoria RTC.</summary>
    ReinicioDaPlaca Reiniciar(int razao, int causa, ulong pinos)
    {
        var rtc = new Dictionary<string, object>();
        foreach (var decl in DeclaracoesGlobais())
        {
            if (!NaMemoriaRtc(decl)) continue;
            if (global.BuscarArrayLocal(decl.Nome, out var vetor)) rtc[decl.Nome] = vetor.ToArray();
            // O objeto vai como esta: esta rodada acaba aqui, e ninguem mais
            // mexe nele. A rodada nova copia os campos para o objeto dela.
            else if (global.BuscarObjetoLocal(decl.Nome, out var objeto)) rtc[decl.Nome] = objeto;
            else if (global.BuscarLocal(decl.Nome, out var valor)) rtc[decl.Nome] = valor;
        }
        return new ReinicioDaPlaca(razao, causa, pinos, rtc);
    }

    IEnumerable<CmdDecl> DeclaracoesGlobais()
    {
        foreach (var g in prog.Globais)
        {
            if (g is CmdDecl d) yield return d;
            else if (g is CmdVarios v)
                foreach (var dentro in v.Corpo.OfType<CmdDecl>()) yield return dentro;
        }
    }

    static bool NaMemoriaRtc(CmdDecl d) => d.Secao?.StartsWith(".rtc", StringComparison.Ordinal) == true;

    /// <summary>
    /// A global que mora na memoria RTC volta com o valor de antes do
    /// reinicio. RTC_DATA_ATTR so sobrevive ao sono profundo -- o reset por
    /// software recarrega a memoria RTC com o valor do codigo; a
    /// RTC_NOINIT_ATTR sobrevive aos dois.
    /// </summary>
    void RestaurarDaRtc(CmdDecl d, Ambiente amb)
    {
        if (Origem is null || !ReferenceEquals(amb, global) || !NaMemoriaRtc(d)) return;
        if (!Origem.MemoriaRtc.TryGetValue(d.Nome, out var guardado)) return;

        bool semIniciar = d.Secao.Contains("noinit", StringComparison.Ordinal);
        if (Origem.Razao != ReinicioDaPlaca.PeloSonoProfundo && !semIniciar) return;

        switch (guardado)
        {
            case Valor[] vetor when amb.BuscarArrayLocal(d.Nome, out var novo):
                Array.Copy(vetor, novo, Math.Min(vetor.Length, novo.Length));
                break;
            case Objeto antigo when amb.BuscarObjetoLocal(d.Nome, out var novo):
                if (antigo.Escopo is not null && novo.Escopo is not null)
                    antigo.Escopo.CopiarConteudo(novo.Escopo, o => o);
                foreach (var (campo, valor) in antigo.Campos)
                    novo.Campos[campo] = valor.Referencia is Valor[] dentro ? Valor.DeReferencia(dentro.ToArray()) : valor;
                break;
            case Valor valor:
                if (amb.BuscarLocal(d.Nome, out var atual) && atual.Caractere) amb.DeclararCaractere(d.Nome, valor);
                else amb.Declarar(d.Nome, valor);
                break;
        }
    }

    /// <summary>
    /// esp_deep_sleep_start(): a placa desliga quase inteira e so o RTC
    /// fica olhando o timer e os pinos. Os pinos soltam (menos os
    /// segurados), o WiFi cai, a serial cala -- e quando uma fonte de
    /// despertar dispara, a placa comeca do setup() de novo.
    ///
    /// Sem fonte nenhuma armada, ela dorme para sempre: e o que acontece
    /// na bancada, e so o RESET a acorda.
    /// </summary>
    void DormirProfundo(int linha)
    {
        string espera = DescreverDespertar();
        if (espera is null)
            maq.Avisar(Idioma.T("esp_deep_sleep_start() sem nenhuma fonte de despertar: a placa dorme para sempre, e so o botao RESET a acorda."));

        maq.Dormir(pinosSegurados, espera ?? Idioma.T("para sempre (so o RESET acorda)"));

        double inicio = maq.AgoraUs();
        var (causa, pinos) = EsperarDespertar(inicio, profundo: true);
        _ = linha;
        throw Reiniciar(ReinicioDaPlaca.PeloSonoProfundo, causa, pinos);
    }

    /// <summary>
    /// esp_light_sleep_start(): a CPU para e os pinos ficam como estavam.
    /// Quando acorda, o sketch continua da linha seguinte.
    /// </summary>
    void DormirLeve()
    {
        if (DescreverDespertar() is null && !(gpioNoSonoLeve && gpioDoSonoLeve.Count > 0))
            maq.Avisar(Idioma.T("esp_light_sleep_start() sem nenhuma fonte de despertar: a placa nao acorda mais."));

        var (causa, pinos) = EsperarDespertar(maq.AgoraUs(), profundo: false);
        causaDoSonoLeve = causa;
        pinosDoSonoLeve = pinos;
    }

    /// <summary>
    /// ESP.deepSleep(us) do ESP8266. Quem acorda a placa e o RST: no fim
    /// do tempo o timer puxa o GPIO16 (D0) para LOW, e so com o fio do D0
    /// no RST isso vira um reset. Sem o fio -- ou com zero microssegundos
    /// -- ela dorme ate alguem apertar o RESET.
    /// </summary>
    void DormirComoEsp8266(long us)
    {
        bool comFio = maq.D0NoReset?.Invoke() ?? true;
        if (us > 0 && !comFio)
            maq.Avisar(Idioma.T("ESP.deepSleep(): o ESP8266 so acorda sozinho com o D0 (GPIO16) ligado no RST. Sem esse fio a placa dorme ate alguem apertar o RESET."));

        bool acorda = us > 0 && comFio;
        maq.Dormir(pinosSegurados, acorda
            ? Idioma.T("acorda em {0:0.###} s, pelo D0 ligado no RST", us / 1_000_000.0)
            : Idioma.T("para sempre (so o RESET acorda)"));

        despertarDoTimerUs = acorda ? us : -1;
        ext0Pino = -1;
        ext1Mascara = 0;
        gpioMascara = 0;
        var (causa, _) = EsperarDespertar(maq.AgoraUs(), profundo: true);
        throw Reiniciar(ReinicioDaPlaca.PeloSonoProfundo, causa, 0);
    }

    string DescreverDespertar()
    {
        var partes = new List<string>();
        if (despertarDoTimerUs >= 0)
            partes.Add(despertarDoTimerUs >= 1_000_000 && despertarDoTimerUs % 1_000_000 == 0
                ? Idioma.T("o timer, em {0} s", despertarDoTimerUs / 1_000_000)
                : Idioma.T("o timer, em {0:0.###} ms", despertarDoTimerUs / 1000.0));
        if (ext0Pino >= 0) partes.Add(Idioma.T("o GPIO {0} em {1}", ext0Pino, (ext0Nivel == 1 ? "HIGH" : "LOW")));
        if (ext1Mascara != 0)
            partes.Add(Idioma.T("os GPIO {0} ", string.Join(", ", PinosDaMascara(ext1Mascara)))
                       + (ext1Modo == 1 ? Idioma.T("(qualquer um em HIGH)") : ChipDoSono == "ESP32" ? Idioma.T("(todos em LOW)") : Idioma.T("(qualquer um em LOW)")));
        if (gpioMascara != 0)
            partes.Add(Idioma.T("os GPIO {0} em {1}", string.Join(", ", PinosDaMascara(gpioMascara)), (gpioModo == 1 ? "HIGH" : "LOW")));
        return partes.Count == 0 ? null : Idioma.T("acorda com {0}", string.Join(Idioma.T(" ou "), partes));
    }

    /// <summary>
    /// Espera ate uma fonte armada disparar. O relogio anda como num
    /// delay(): de verdade na janela, virtual no teste -- e o timer que
    /// sozinho mandaria esperar muito pula direto para a hora dele.
    /// </summary>
    (int Causa, ulong Pinos) EsperarDespertar(double inicioUs, bool profundo)
    {
        bool temPino = ext0Pino >= 0 || ext1Mascara != 0 || gpioMascara != 0
                       || (!profundo && gpioNoSonoLeve && gpioDoSonoLeve.Count > 0);

        while (true)
        {
            if (maq.Parar) throw maq.MotivoDaParada();

            if (ext0Pino >= 0 && maq.DigitalRead(ext0Pino) == ext0Nivel) return (2, 0);

            if (ext1Mascara != 0)
            {
                var pinos = PinosDaMascara(ext1Mascara).ToList();
                ulong altos = 0;
                foreach (int g in pinos) if (maq.DigitalRead(g) == 1) altos |= 1UL << g;

                // A ESP32 original so tem "todos em LOW"; as outras,
                // "qualquer um em LOW". O ANY_HIGH e igual em todas.
                bool acordou = ext1Modo == 1 ? altos != 0
                             : ChipDoSono == "ESP32" ? altos == 0
                             : pinos.Any(g => (altos & (1UL << g)) == 0);
                if (acordou)
                    return (3, ext1Modo == 1 ? altos : ext1Mascara & ~altos);
            }

            if (gpioMascara != 0)
            {
                ulong quais = 0;
                foreach (int g in PinosDaMascara(gpioMascara))
                    if (maq.DigitalRead(g) == gpioModo) quais |= 1UL << g;
                if (quais != 0) return (7, quais);
            }

            if (!profundo && gpioNoSonoLeve)
                foreach (var (g, nivel) in gpioDoSonoLeve)
                    if (maq.DigitalRead(g) == nivel) return (7, 1UL << g);

            double passouUs = maq.AgoraUs() - inicioUs;
            if (despertarDoTimerUs >= 0 && passouUs >= despertarDoTimerUs) return (4, 0);

            // Sem pino para olhar, no relogio virtual, e so o timer que
            // falta: pula direto para ele.
            long passo = 1;
            if (!temPino && despertarDoTimerUs >= 0 && !maq.TempoReal)
                passo = Math.Max(1, (long)Math.Ceiling((despertarDoTimerUs - passouUs) / 1000.0));
            else if (maq.TempoReal)
                passo = 5;

            maq.Avancar(passo);
            passos = 0;
        }
    }
}
