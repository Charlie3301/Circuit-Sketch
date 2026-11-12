namespace Simulador;

/// <summary>
/// Um aparelho ligado no barramento SPI da placa, como o circuito o liga:
/// o tipo ("ili9341", "sd"...) e os GPIOs de cada pino dele (-1 = solto).
/// O 74HC595 entra aqui tambem, que e o SPI mais simples que ha: o STCP no
/// lugar do CS, o MR no do reset, o DS no do MOSI e o SHCP no do SCK. Peca e
/// a peca do circuito; SerDe, o 74HC595 cujo Q7S chega no DS deste (a fila).
/// </summary>
public sealed record AparelhoNoSpi(string Tipo, int Cs, int Dc = -1, int Rst = -1, int Mosi = -1,
                                   int Miso = -1, int Sck = -1, int Luz = -1,
                                   string Peca = null, string SerDe = null);

/// <summary>
/// O SPI do modo nativo: os barramentos que o C iniciou, os dispositivos
/// que ele pendurou, e o aparelho que responde em cada CS. O tempo do
/// barramento conta: a 40 MHz, um quadro inteiro do ILI9341 leva uns 30 ms,
/// e a tarefa do display espera por isso como na placa.
/// </summary>
public sealed unsafe partial class ModuloNativo
{
    sealed class BarramentoSpi
    {
        public int Mosi, Miso, Sclk;
        public double OcupadoAteUs;
    }

    sealed class DispositivoSpi
    {
        public int Host, Cs, Hz;
        public BarramentoSpi Barramento;
        public IChipSpi Chip;
        public AparelhoNoSpi Aparelho;
    }

    readonly BarramentoSpi[] barramentos = new BarramentoSpi[4];

    internal int SpiBarramento(int host, int mosi, int miso, int sclk)
    {
        if (host < 1 || host > 3) return 0x102;   // ESP_ERR_INVALID_ARG
        foreach (int p in new[] { mosi, miso, sclk })
            if (p >= Maquina.Perfil.NumPinos)
            {
                Maquina.Avisar($"SPI: o GPIO {p} nao existe no {Maquina.Perfil.Nome}.");
                return 0x102;
            }
        lock (barramentos) barramentos[host] = new BarramentoSpi { Mosi = mosi, Miso = miso, Sclk = sclk };
        return 0;
    }

    internal int SpiLiberar(int host)
    {
        if (host < 1 || host > 3) return 0x102;
        lock (barramentos) barramentos[host] = null;
        return 0;
    }

    internal long SpiDispositivo(int host, int cs, int hz, int modo, int flags)
    {
        BarramentoSpi bus;
        lock (barramentos) bus = host is >= 1 and <= 3 ? barramentos[host] : null;
        var d = new DispositivoSpi { Host = host, Cs = cs, Hz = Math.Max(1, hz), Barramento = bus };

        // Quem esta no CS, com o MOSI e o SCK chegando nele.
        var aparelho = (Maquina.AparelhosSpi?.Invoke() ?? []).FirstOrDefault(a => a.Cs == cs && cs >= 0);
        if (aparelho is not null && bus is not null)
        {
            bool mosiOk = aparelho.Mosi < 0 || aparelho.Mosi == bus.Mosi;
            bool sckOk = aparelho.Sck < 0 || aparelho.Sck == bus.Sclk;
            if (!mosiOk || !sckOk)
            {
                Maquina.Avisar($"SPI: o {Nome(aparelho.Tipo)} no CS {cs} nao esta no barramento que o codigo iniciou "
                               + $"(o codigo usa MOSI {bus.Mosi} e SCK {bus.Sclk}; o fio do aparelho vai no "
                               + $"{(mosiOk ? "" : "MOSI " + aparelho.Mosi + " ")}{(sckOk ? "" : "SCK " + aparelho.Sck)}). "
                               + "Nada chega nele.");
                aparelho = null;
            }
        }
        d.Aparelho = aparelho;

        // O chip do aparelho (o controlador do display) e o mesmo que escuta
        // os fios: aqui os bytes chegam prontos, sem passar pelos pinos, e o
        // RESET dele continua vindo do pino.
        if (aparelho is not null) d.Chip = Maquina.EscutaDoSpi?.ChipDe(aparelho);
        return Guardar(d);
    }

    static string Nome(string tipo) => tipo switch
    {
        "ili9341" => "display ILI9341",
        "st7789" => "display ST7789",
        "st7735" => "display ST7735",
        "ssd1306" => "display OLED",
        "sd" => "modulo microSD",
        "74hc595" => "74HC595",
        _ => tipo,
    };

    internal void SpiRemover(long id) => Apagar(id);

    internal int SpiTransferir(long id, byte* tx, byte* rx, int bitsTx, int bitsRx)
    {
        var d = Obj<DispositivoSpi>(id);
        int bytesTx = (bitsTx + 7) / 8, bytesRx = (bitsRx + 7) / 8;
        if (rx != null && bytesRx > 0) new Span<byte>(rx, bytesRx).Fill(0xFF);   // MISO solto: tudo 1
        if (d is null) return 0;

        if (d.Chip is not null && tx != null && bytesTx > 0)
        {
            int dc = d.Aparelho.Dc;
            bool dado = dc < 0 || Maquina.DigitalRead(dc) != 0;
            lock (d.Chip)
            {
                d.Chip.Receber(new ReadOnlySpan<byte>(tx, bytesTx), dado);
                d.Chip.FimDaTransacao();
            }
            if (rx != null && bytesRx > 0) new Span<byte>(rx, bytesRx).Clear();
        }

        // O barramento leva o tempo dos bits: quem manda muito espera.
        var bus = d.Barramento;
        if (bus is not null)
        {
            double agora = Micros();
            double duracao = Math.Max(bitsTx, bitsRx) * 1e6 / RelogioDeVerdade(d.Hz);
            double fim;
            lock (bus)
            {
                double comeco = Math.Max(agora, bus.OcupadoAteUs);
                fim = comeco + duracao;
                bus.OcupadoAteUs = fim;
            }
            if (fim - agora > 2000 && Dormir((long)(fim - agora - 1000)) < 0) return -1;
        }
        return 0;
    }

    /// <summary>O relogio que o chip consegue: 80 MHz dividido por um inteiro.</summary>
    static double RelogioDeVerdade(int hz)
    {
        const int apb = 80_000_000;
        if (hz >= apb) return apb;
        int divisor = (apb + hz - 1) / hz;
        return apb / (double)divisor;
    }
}
