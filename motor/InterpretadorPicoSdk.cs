namespace Simulador;

/// <summary>
/// O SDK em C da Raspberry Pi Pico.
///
/// Alpha 18 pediu a Pico em três sabores -- Arduino, MicroPython e "Pico
/// SDK (Avançado)" --, e pediu suporte a C. É a mesma coisa: o SDK da
/// Raspberry é C puro, e escrever para ele é o jeito de programar a Pico
/// sem o Arduino no meio.
///
/// Duas coisas mudam em relação a um sketch:
///
///   1. o programa começa em <c>int main()</c>, e não em setup/loop;
///   2. os pinos se mexem com <c>gpio_put</c> e <c>gpio_set_dir</c>,
///      e não com digitalWrite e pinMode.
///
/// A primeira é resolvida na <see cref="Compilador"/>, que parte o main
/// em setup e loop. A segunda é este arquivo: cada função do SDK cai na
/// mesma <see cref="Maquina"/> que o digitalWrite usa. Não há dois
/// simuladores -- há dois vocabulários para o mesmo circuito, que é o
/// que permite ligar um LED do SDK num pino e lê-lo de um sketch
/// Arduino na placa ao lado.
/// </summary>
public partial class Interpretador
{
    /// <summary>
    /// A função é do SDK da Pico? Devolve o resultado por <paramref name="saida"/>.
    ///
    /// Fica antes da tabela do Arduino porque alguns nomes são curtos e
    /// genéricos -- <c>sleep_ms</c>, <c>gpio_put</c> --, e misturá-los
    /// com o resto daquele switch tornaria os dois ilegíveis.
    /// </summary>
    bool ChamarPicoSdk(string nome, List<Valor> args, out Valor saida)
    {
        saida = Valor.Vazio;
        double A(int i) => i < args.Count ? args[i].Num : 0;

        switch (nome)
        {
            // ---- GPIO ----
            //
            // gpio_init deixa o pino como entrada, que é o padrão do
            // chip; quem quer saída chama gpio_set_dir depois.
            case "gpio_init": maq.PinMode((int)A(0), 0); return true;
            case "gpio_deinit": return true;

            case "gpio_set_dir":
                maq.PinMode((int)A(0), A(1) != 0 ? 1 : 0);
                return true;

            case "gpio_put": maq.DigitalWrite((int)A(0), A(1) != 0 ? 1 : 0); return true;
            case "gpio_get": saida = Valor.DeInt(maq.DigitalRead((int)A(0))); return true;

            case "gpio_xor_mask":
            case "gpio_set_mask":
            case "gpio_clr_mask":
                MexerNaMascara(nome, (long)A(0));
                return true;

            // O pull-up interno: é ele que faz o botão ligado no GND
            // funcionar sem resistor de fora.
            case "gpio_pull_up": maq.PinMode((int)A(0), 2); return true;
            case "gpio_pull_down":
            case "gpio_disable_pulls":
            case "gpio_set_input_enabled":
                return true;

            // Qual periferico manda no pino. So o PWM importa aqui: sem
            // "gpio_set_function(pino, GPIO_FUNC_PWM)" o sinal do PWM NAO
            // chega no pino, e o LED fica apagado -- na placa tambem.
            case "gpio_set_function":
            {
                int pino = (int)A(0);
                if ((int)A(1) == FuncaoPwm)
                {
                    pinosEmPwm.Add(pino);
                    maq.PinMode(pino, 1);
                    AplicarPwmDaPico(pino);
                }
                else pinosEmPwm.Remove(pino);
                return true;
            }

            // ---- tempo ----
            //
            // O SDK conta em MICROssegundos desde o boot, e o
            // absolute_time_t e esse numero. Antes o get_absolute_time
            // devolvia milissegundos e o to_ms_since_boot ignorava o que
            // recebia -- medir quanto uma coisa demorou dava sempre "agora".
            case "sleep_ms":
                maq.Avancar((long)A(0));
                AtenderInterrupcoes();
                passos = 0;
                return true;

            case "sleep_us":
            case "busy_wait_us":
            case "busy_wait_us_32":
                maq.EsperarMicros(A(0));
                return true;

            case "busy_wait_ms": maq.Avancar((long)A(0)); return true;

            case "time_us_32":
            case "time_us_64":
            case "get_absolute_time":
                saida = Valor.DeInt((long)maq.AgoraUs());
                return true;

            case "to_ms_since_boot": saida = Valor.DeInt((long)A(0) / 1000); return true;
            case "to_us_since_boot": saida = Valor.DeInt((long)A(0)); return true;
            case "absolute_time_diff_us": saida = Valor.DeInt((long)A(1) - (long)A(0)); return true;
            case "delayed_by_ms": saida = Valor.DeInt((long)A(0) + (long)A(1) * 1000); return true;
            case "delayed_by_us": saida = Valor.DeInt((long)A(0) + (long)A(1)); return true;
            case "make_timeout_time_ms": saida = Valor.DeInt((long)maq.AgoraUs() + (long)A(0) * 1000); return true;
            case "make_timeout_time_us": saida = Valor.DeInt((long)maq.AgoraUs() + (long)A(0)); return true;
            case "time_reached": saida = Valor.DeBool(maq.AgoraUs() >= A(0)); return true;
            case "sleep_until": maq.EsperarMicros(A(0) - maq.AgoraUs()); return true;

            // ---- saída ----
            //
            // stdio_init_all é o Serial.begin do SDK: sem ele o printf
            // não sai em lugar nenhum na placa de verdade.
            case "stdio_init_all":
            case "stdio_usb_init":
            case "stdio_uart_init":
                return true;

            // ---- ADC ----
            case "adc_init":
            case "adc_gpio_init":
                return true;

            case "adc_set_temp_sensor_enabled": sensorDeTemperaturaLigado = A(0) != 0; return true;
            case "adc_select_input": canalDoAdc = (int)A(0); return true;
            case "adc_get_selected_input": saida = Valor.DeInt(canalDoAdc); return true;

            // O ADC da Pico é o GP26 no canal 0, e daí para cima; o canal 4
            // é o sensor de temperatura de dentro do chip.
            case "adc_read":
                saida = Valor.DeInt(canalDoAdc == 4 ? LeituraDaTemperaturaDoSdk()
                                                   : maq.AnalogRead(26 + canalDoAdc));
                return true;

            // ---- PWM ----
            //
            // Como no chip: oito fatias, cada uma com o seu contador (o
            // "wrap"), o seu divisor de relogio e dois canais, A e B. O
            // GPIO par da fatia e o canal A, o impar e o B -- e a mesma
            // fatia serve outra vez aos GPIO 16 a 29. A frequencia sai de
            // 125 MHz / (divisor x (wrap + 1)), e o brilho de nivel / (wrap + 1).
            case "pwm_gpio_to_slice_num": saida = Valor.DeInt(((int)A(0) >> 1) & 7); return true;
            case "pwm_gpio_to_channel": saida = Valor.DeInt((int)A(0) & 1); return true;
            case "pwm_get_default_config": return true;

            case "pwm_set_wrap": MudarFatia((int)A(0), f => f with { Topo = A(1) }); return true;
            case "pwm_set_clkdiv":
                MudarFatia((int)A(0), f => f with { Divisor = Math.Max(1, A(1)) });
                return true;
            case "pwm_set_clkdiv_int_frac":
                MudarFatia((int)A(0), f => f with { Divisor = Math.Max(1, A(1) + A(2) / 16.0) });
                return true;
            case "pwm_set_enabled": MudarFatia((int)A(0), f => f with { Ligada = A(1) != 0 }); return true;
            case "pwm_set_mask_enabled":
                for (int s = 0; s < 8; s++)
                {
                    bool ligada = ((long)A(0) & (1L << s)) != 0;
                    MudarFatia(s, f => f with { Ligada = ligada });
                }
                return true;

            case "pwm_set_chan_level":
                MudarNivel((int)A(0) & 7, (int)A(1) & 1, A(2));
                return true;

            case "pwm_set_gpio_level":
            {
                int pino = (int)A(0);
                if (!pinosEmPwm.Contains(pino) && avisosDePwm.Add(pino))
                    maq.Avisar(Idioma.T("pwm_set_gpio_level no GP{0}, mas o pino nao foi passado para o PWM: falta gpio_set_function({1}, GPIO_FUNC_PWM). Na placa, o sinal nao sai no pino.", pino, pino));
                MudarNivel((pino >> 1) & 7, pino & 1, A(1));
                return true;
            }

            default: return false;
        }
    }

    /// <summary>Qual canal do ADC o adc_select_input escolheu.</summary>
    int canalDoAdc;

    /// <summary>adc_set_temp_sensor_enabled: sem ele, o canal 4 nao le nada.</summary>
    bool sensorDeTemperaturaLigado;

    bool avisouDoSensorDesligado;

    /// <summary>A placa e uma das do RP2040 (Pico, Pico W, Nano RP2040).</summary>
    bool EhRp2040 => maq.Perfil?.Processador == "RP2040";

    /// <summary>
    /// O canal 4 do SDK: a temperatura de dentro do chip -- mas so com o
    /// sensor ligado, como na placa. Desligado, ele nao tem corrente e a
    /// leitura fica no chao.
    /// </summary>
    int LeituraDaTemperaturaDoSdk()
    {
        if (sensorDeTemperaturaLigado) return maq.LeituraDoSensorDeTemperatura;

        if (!avisouDoSensorDesligado)
        {
            avisouDoSensorDesligado = true;
            maq.Avisar(Idioma.T("adc_read() no canal 4 com o sensor de temperatura desligado: chame adc_set_temp_sensor_enabled(true) antes. Na placa a leitura fica em zero."));
        }
        return 0;
    }

    // ---------------- o PWM do RP2040 ----------------

    /// <summary>GPIO_FUNC_PWM, como no SDK.</summary>
    const int FuncaoPwm = 4;

    /// <summary>Uma fatia de PWM: o contador vai de 0 ao Topo.</summary>
    record FatiaPwm(double Topo, double Divisor, bool Ligada);

    static readonly FatiaPwm FatiaDeFabrica = new(0xFFFF, 1, false);

    readonly Dictionary<int, FatiaPwm> fatiasPwm = [];
    readonly Dictionary<(int Fatia, int Canal), double> niveisPwm = [];
    readonly HashSet<int> pinosEmPwm = [];
    readonly HashSet<int> avisosDePwm = [];

    void MudarFatia(int fatia, Func<FatiaPwm, FatiaPwm> mudanca)
    {
        fatia &= 7;
        fatiasPwm[fatia] = mudanca(fatiasPwm.GetValueOrDefault(fatia, FatiaDeFabrica));
        foreach (int pino in pinosEmPwm)
            if (((pino >> 1) & 7) == fatia) AplicarPwmDaPico(pino);
    }

    void MudarNivel(int fatia, int canal, double nivel)
    {
        niveisPwm[(fatia, canal)] = Math.Max(0, nivel);
        foreach (int pino in pinosEmPwm)
            if (((pino >> 1) & 7) == fatia && (pino & 1) == canal) AplicarPwmDaPico(pino);
    }

    /// <summary>O que o pino mostra agora: a conta da fatia e do canal dele.</summary>
    void AplicarPwmDaPico(int pino)
    {
        if (!pinosEmPwm.Contains(pino)) return;

        int fatia = (pino >> 1) & 7;
        var f = fatiasPwm.GetValueOrDefault(fatia, FatiaDeFabrica);
        double nivel = niveisPwm.GetValueOrDefault((fatia, pino & 1));

        double ciclo = f.Ligada ? Math.Clamp(nivel / (f.Topo + 1), 0, 1) : 0;
        double hz = 125_000_000 / (f.Divisor * (f.Topo + 1));
        maq.AnalogWrite(pino, (int)Math.Round(ciclo * 255), hz);
    }

    /// <summary>
    /// A configuracao por struct do SDK: pwm_config_set_wrap(&amp;cfg, ...),
    /// pwm_config_set_clkdiv(&amp;cfg, ...) e pwm_init(fatia, &amp;cfg, ligar).
    /// </summary>
    bool ConfigurarPwmDaPico(string nome, Chamada c, Ambiente amb, out Valor saida)
    {
        saida = Valor.Vazio;
        if (c.Args.Count == 0) return false;

        if (nome == "pwm_init")
        {
            if (c.Args.Count < 2) return false;
            int fatia = (int)Avaliar(c.Args[0], amb).Num;
            var cfg = ObjetoDoArgumento(c.Args[1], amb);
            if (cfg is null) return false;

            bool ligar = c.Args.Count > 2 && Avaliar(c.Args[2], amb).Verdadeiro;
            double topo = cfg.Campos.GetValueOrDefault("top", Valor.DeInt(0xFFFF)).Num;
            double divisor = cfg.Campos.GetValueOrDefault("div", Valor.DeInt(16)).Num / 16.0;
            MudarFatia(fatia, _ => new FatiaPwm(topo, Math.Max(1, divisor), ligar));
            return true;
        }

        var config = ObjetoDoArgumento(c.Args[0], amb);
        if (config is null) return false;
        double valor = c.Args.Count > 1 ? Avaliar(c.Args[1], amb).Num : 0;

        switch (nome)
        {
            case "pwm_config_set_wrap":
                config.Campos["top"] = Valor.DeInt((long)valor & 0xFFFF);
                return true;

            // O divisor mora em ponto fixo 8.4 no struct: 1,0 e 16.
            case "pwm_config_set_clkdiv":
            case "pwm_config_set_clkdiv_int":
                config.Campos["div"] = Valor.DeInt((long)Math.Round(Math.Max(1, valor) * 16));
                return true;

            case "pwm_config_set_phase_correct":
            case "pwm_config_set_output_polarity":
            case "pwm_config_set_clkdiv_mode":
                return true;
        }
        return false;
    }

    /// <summary>
    /// As máscaras de GPIO: mexem em vários pinos de uma vez.
    ///
    /// É o jeito do SDK de acender oito LEDs no mesmo instante, e a
    /// razão de ele existir -- fazer um por um dá um degrau visível
    /// numa barra de LEDs.
    /// </summary>
    void MexerNaMascara(string nome, long mascara)
    {
        for (int pino = 0; pino < 32; pino++)
        {
            if ((mascara & (1L << pino)) == 0) continue;

            int agora = maq.DigitalRead(pino);
            maq.DigitalWrite(pino, nome switch
            {
                "gpio_set_mask" => 1,
                "gpio_clr_mask" => 0,
                _ => agora != 0 ? 0 : 1,
            });
        }
    }

    /// <summary>
    /// As constantes do SDK.
    ///
    /// GPIO_OUT vale 1 e GPIO_IN vale 0 -- é isso que o gpio_set_dir
    /// espera, e trocar os dois é o erro que deixa o LED apagado sem
    /// nenhuma mensagem.
    /// </summary>
    internal static bool ConstanteDoPicoSdk(string nome, out Valor valor)
    {
        valor = Valor.Vazio;

        switch (nome)
        {
            case "GPIO_OUT": valor = Valor.DeInt(1); return true;
            case "GPIO_IN": valor = Valor.DeInt(0); return true;

            // O LED de bordo da Pico é o GP25. Na Pico W ele está no
            // rádio, e o SDK define o pino como 32 para não colidir.
            case "PICO_DEFAULT_LED_PIN": valor = Valor.DeInt(25); return true;

            case "PICO_DEFAULT_I2C_SDA_PIN": valor = Valor.DeInt(4); return true;
            case "PICO_DEFAULT_I2C_SCL_PIN": valor = Valor.DeInt(5); return true;

            // As funcoes de um pino (enum gpio_function do RP2040).
            case "GPIO_FUNC_XIP": valor = Valor.DeInt(0); return true;
            case "GPIO_FUNC_SPI": valor = Valor.DeInt(1); return true;
            case "GPIO_FUNC_UART": valor = Valor.DeInt(2); return true;
            case "GPIO_FUNC_I2C": valor = Valor.DeInt(3); return true;
            case "GPIO_FUNC_PWM": valor = Valor.DeInt(4); return true;
            case "GPIO_FUNC_SIO": valor = Valor.DeInt(5); return true;
            case "GPIO_FUNC_PIO0": valor = Valor.DeInt(6); return true;
            case "GPIO_FUNC_PIO1": valor = Valor.DeInt(7); return true;
            case "GPIO_FUNC_NULL": valor = Valor.DeInt(0x1f); return true;
            case "PWM_CHAN_A": valor = Valor.DeInt(0); return true;
            case "PWM_CHAN_B": valor = Valor.DeInt(1); return true;

            default: return false;
        }
    }
}
