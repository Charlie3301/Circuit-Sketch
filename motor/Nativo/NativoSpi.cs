namespace Simulador;

/// <summary>
/// Um aparelho ligado no barramento SPI da placa, como o circuito o liga:
/// o tipo ("ili9341", "sd"...) e os GPIOs de cada pino dele (-1 = solto).
/// </summary>
public sealed record AparelhoNoSpi(string Tipo, int Cs, int Dc = -1, int Rst = -1, int Mosi = -1,
                                   int Miso = -1, int Sck = -1, int Luz = -1);

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
        public Ili9341 Tela;
        public AparelhoNoSpi Aparelho;
    }

    readonly BarramentoSpi[] barramentos = new BarramentoSpi[4];
    readonly List<Ili9341> displays = [];

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
        if (aparelho?.Tipo is "ili9341")
        {
            d.Tela = new Ili9341(Maquina.CriarTela(240, 320), aparelho.Dc, aparelho.Rst);
            lock (displays) displays.Add(d.Tela);
        }
        return Guardar(d);
    }

    static string Nome(string tipo) => tipo switch
    {
        "ili9341" => "display ILI9341",
        "st7789" => "display ST7789",
        "sd" => "modulo microSD",
        _ => tipo,
    };

    internal void SpiRemover(long id)
    {
        var d = Obj<DispositivoSpi>(id);
        if (d?.Tela is not null) lock (displays) displays.Remove(d.Tela);
        Apagar(id);
    }

    internal int SpiTransferir(long id, byte* tx, byte* rx, int bitsTx, int bitsRx)
    {
        var d = Obj<DispositivoSpi>(id);
        int bytesTx = (bitsTx + 7) / 8, bytesRx = (bitsRx + 7) / 8;
        if (rx != null && bytesRx > 0) new Span<byte>(rx, bytesRx).Fill(0xFF);   // MISO solto: tudo 1
        if (d is null) return 0;

        if (d.Tela is not null && tx != null && bytesTx > 0)
        {
            bool dado = d.Tela.Dc < 0 || Maquina.DigitalRead(d.Tela.Dc) != 0;
            d.Tela.Receber(new ReadOnlySpan<byte>(tx, bytesTx), dado);
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

    /// <summary>O RESET de um display em LOW zera o controlador dele.</summary>
    void PinoMudou(int pino, int nivel)
    {
        if (nivel != 0) return;
        lock (displays)
            foreach (var t in displays)
                if (t.Rst == pino) t.Resetar();
    }
}

/// <summary>
/// O controlador do ILI9341, byte a byte: comando com o D/C em LOW, dados
/// com ele em HIGH. CASET e PASET abrem a janela, RAMWR enche de pixels
/// (RGB565, o byte alto primeiro), MADCTL gira e espelha -- as mesmas contas
/// que a Adafruit usa no setRotation.
/// </summary>
public sealed class Ili9341
{
    readonly TelaGrafica tela;
    public int Dc { get; }
    public int Rst { get; }

    int comando = -1;
    readonly byte[] parametros = new byte[16];
    int nParametros;
    int xs, xe = 239, ys, ye = 319;
    int x, y;
    int madctl;
    bool escrevendo;
    int pendente = -1;
    int bytesPorPixel = 2;
    readonly byte[] pixel3 = new byte[3];
    int nPixel3;

    static readonly int[] Rgb565 = MontarTabela();

    static int[] MontarTabela()
    {
        var t = new int[65536];
        for (int c = 0; c < 65536; c++) t[c] = TelaGrafica.DeRgb565(c);
        return t;
    }

    public Ili9341(TelaGrafica tela, int dc, int rst)
    {
        this.tela = tela;
        Dc = dc;
        Rst = rst;
    }

    public void Resetar()
    {
        comando = -1;
        nParametros = 0;
        xs = 0; xe = 239; ys = 0; ye = 319;
        madctl = 0;
        escrevendo = false;
        pendente = -1;
        bytesPorPixel = 2;
        tela.Comando(0x01);
    }

    public void Receber(ReadOnlySpan<byte> dados, bool ehDado)
    {
        if (!ehDado)
        {
            foreach (byte b in dados) Comando(b);
            return;
        }

        if (escrevendo)
        {
            Pixels(dados);
            return;
        }

        foreach (byte b in dados)
        {
            if (nParametros < parametros.Length) parametros[nParametros++] = b;
            Aplicar();
            if (escrevendo) return;
        }
    }

    void Comando(byte c)
    {
        comando = c;
        nParametros = 0;
        escrevendo = false;
        pendente = -1;
        nPixel3 = 0;
        switch (c)
        {
            case 0x2C:   // RAMWR
                x = xs;
                y = ys;
                escrevendo = true;
                break;
            case 0x3C:   // RAMWRC: continua de onde parou
                escrevendo = true;
                break;
            case 0x01: case 0x10: case 0x11: case 0x28: case 0x29: case 0x20: case 0x21:
                if (c == 0x01) { madctl = 0; xs = 0; xe = 239; ys = 0; ye = 319; bytesPorPixel = 2; }
                tela.Comando(c);
                break;
        }
    }

    void Aplicar()
    {
        switch (comando)
        {
            case 0x2A when nParametros == 4:   // CASET
                xs = (parametros[0] << 8) | parametros[1];
                xe = (parametros[2] << 8) | parametros[3];
                break;
            case 0x2B when nParametros == 4:   // PASET
                ys = (parametros[0] << 8) | parametros[1];
                ye = (parametros[2] << 8) | parametros[3];
                break;
            case 0x36 when nParametros == 1:   // MADCTL
                madctl = parametros[0];
                break;
            case 0x3A when nParametros == 1:   // COLMOD: 0x55 16 bits, 0x66 18 bits
                bytesPorPixel = (parametros[0] & 0x07) == 0x06 ? 3 : 2;
                break;
        }
    }

    void Pixels(ReadOnlySpan<byte> dados)
    {
        bool mudou = false;
        int i = 0;
        if (bytesPorPixel == 3)
        {
            for (; i < dados.Length; i++)
            {
                pixel3[nPixel3++] = dados[i];
                if (nPixel3 < 3) continue;
                nPixel3 = 0;
                int c565 = ((pixel3[0] >> 3) << 11) | ((pixel3[1] >> 2) << 5) | (pixel3[2] >> 3);
                Pintar(c565);
                mudou = true;
            }
        }
        else
        {
            if (pendente >= 0 && dados.Length > 0)
            {
                Pintar((pendente << 8) | dados[0]);
                pendente = -1;
                i = 1;
                mudou = true;
            }
            for (; i + 1 < dados.Length; i += 2)
            {
                Pintar((dados[i] << 8) | dados[i + 1]);
                mudou = true;
            }
            if (i < dados.Length) pendente = dados[i];
        }
        if (mudou) tela.Mudou();
    }

    void Pintar(int c565)
    {
        // Onde a coluna e a pagina caem no painel (240x320 em pe).
        bool mv = (madctl & 0x20) != 0, mx = (madctl & 0x40) != 0, my = (madctl & 0x80) != 0;
        int c = mx ? (mv ? 319 : 239) - x : x;
        int p = my ? (mv ? 239 : 319) - y : y;
        int px, py;
        if (mv) { px = 239 - p; py = c; }
        else { px = 239 - c; py = p; }

        if ((uint)px < 240 && (uint)py < 320)
        {
            int argb = Rgb565[c565 & 0xFFFF];
            if ((madctl & 0x08) == 0)   // sem o BGR do MADCTL, o painel troca vermelho e azul
                argb = (argb & unchecked((int)0xFF00FF00)) | ((argb >> 16) & 0xFF) | ((argb & 0xFF) << 16);
            tela.Pixels[py * 240 + px] = argb;
        }
        tela.PixelsEscritos++;

        if (++x > xe)
        {
            x = xs;
            if (++y > ye) y = ys;
        }
    }
}
