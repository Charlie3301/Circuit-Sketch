namespace Simulador;

/// <summary>
/// O DS18B20 (o sensor de temperatura de um fio so), do jeito que a ficha da
/// Maxim descreve: a ROM de 64 bits (a familia 0x28, o numero de serie e o
/// CRC), o scratchpad de 9 bytes (a temperatura, o TH, o TL, a configuracao e
/// o CRC), a resolucao de 9 a 12 bits, o tempo de cada conversao e o valor de
/// quando ele liga -- os 85 graus que aparecem quando se le sem ter pedido a
/// conversao. Ele fala bit a bit no barramento (BarramentoUmFio): cada bit que
/// passa e um intervalo de tempo do 1-Wire, e ele decide o que fazer com cada
/// um pelo estado em que esta, como o chip.
/// </summary>
public sealed class Ds18b20
{
    enum Estado { Calado, ComandoDaRom, LendoRom, CasandoRom, Buscando, ComandoDeFuncao, Mandando, Recebendo, Situacao }

    public Ds18b20(string peca, byte[] rom)
    {
        Peca = peca;
        Rom = rom;
    }

    public string Peca { get; }

    /// <summary>A ROM: a familia (0x28), os seis bytes do numero de serie e o CRC.</summary>
    public byte[] Rom { get; }

    /// <summary>A temperatura de agora em volta do sensor (o controle da peca), em graus.</summary>
    public double Temperatura { get; set; } = 25;

    /// <summary>Com o VDD ligado (ou no modo parasita, pelo pull-up do DQ).</summary>
    public bool Alimentado { get; set; } = true;

    /// <summary>O modo parasita: o VDD no GND, a energia vem do proprio DQ.</summary>
    public bool Parasita { get; set; }

    // o scratchpad e a EEPROM (o TH, o TL e a configuracao de fabrica: 75, 70 e 12 bits)
    byte th = 0x4B, tl = 0x46, configuracao = 0x7F;
    byte eepromTh = 0x4B, eepromTl = 0x46, eepromConfiguracao = 0x7F;
    short bruto = 0x0550;
    bool alarme;

    // a conversao em andamento: o que vai entrar no scratchpad e quando
    double fimDaConversaoUs = double.NegativeInfinity;
    short brutoMedido;
    bool convertendo;

    Estado estado = Estado.Calado;
    int bitDoByte, byteMontado;
    readonly List<int> paraMandar = [];
    int mandados;
    int paraReceber;
    readonly List<byte> recebidos = [];
    int comandoRecebendo;
    int bitDaRom, passoDaBusca;
    Func<double, int> situacao;

    /// <summary>A resolucao de agora (9 a 12 bits), pela configuracao.</summary>
    public int Resolucao => 9 + ((configuracao >> 5) & 3);

    /// <summary>O tempo de uma conversao (93,75 ms em 9 bits ate 750 ms em 12).</summary>
    public double TempoDeConversaoMs => 93.75 * (1 << (Resolucao - 9));

    /// <summary>A temperatura que esta no scratchpad agora (85 ao ligar).</summary>
    public double NoScratchpad => bruto / 16.0;

    /// <summary>A energia voltou (ou faltou): o chip recomeca como na hora em que liga.</summary>
    public void Religar()
    {
        bruto = 0x0550;
        th = eepromTh;
        tl = eepromTl;
        configuracao = eepromConfiguracao;
        convertendo = false;
        estado = Estado.Calado;
    }

    void Concluir(double agoraUs)
    {
        if (!convertendo || agoraUs < fimDaConversaoUs) return;
        convertendo = false;
        bruto = brutoMedido;
        double t = bruto / 16.0;
        alarme = t >= (sbyte)th || t <= (sbyte)tl;
    }

    /// <summary>O pulso de reset: com energia, responde com o pulso de presenca.</summary>
    public bool Reset(double agoraUs)
    {
        Concluir(agoraUs);
        if (!Alimentado) { estado = Estado.Calado; return false; }
        estado = Estado.ComandoDaRom;
        bitDoByte = byteMontado = 0;
        return true;
    }

    /// <summary>O que o chip poe no fio neste intervalo de bit (1: solta, 0: segura em LOW).</summary>
    public int Saida(double agoraUs)
    {
        Concluir(agoraUs);
        if (!Alimentado) return 1;
        return estado switch
        {
            Estado.LendoRom => (Rom[bitDaRom / 8] >> (bitDaRom % 8)) & 1,
            Estado.Buscando when passoDaBusca == 0 => (Rom[bitDaRom / 8] >> (bitDaRom % 8)) & 1,
            Estado.Buscando when passoDaBusca == 1 => 1 - ((Rom[bitDaRom / 8] >> (bitDaRom % 8)) & 1),
            Estado.Mandando when mandados < paraMandar.Count => paraMandar[mandados],
            Estado.Situacao => situacao?.Invoke(agoraUs) ?? 1,
            _ => 1,
        };
    }

    /// <summary>O intervalo de bit terminou: o nivel que ficou no fio e o bit que passou.</summary>
    public void Bit(int nivel, double agoraUs)
    {
        if (!Alimentado) return;
        switch (estado)
        {
            case Estado.ComandoDaRom:
            case Estado.ComandoDeFuncao:
                byteMontado |= nivel << bitDoByte;
                if (++bitDoByte < 8) return;
                int comando = byteMontado;
                bitDoByte = byteMontado = 0;
                if (estado == Estado.ComandoDaRom) ComandoDaRom(comando);
                else ComandoDeFuncao(comando, agoraUs);
                return;

            case Estado.LendoRom:
                if (++bitDaRom == 64) estado = Estado.ComandoDeFuncao;
                return;

            case Estado.CasandoRom:
                if (nivel != ((Rom[bitDaRom / 8] >> (bitDaRom % 8)) & 1)) { estado = Estado.Calado; return; }
                if (++bitDaRom == 64) estado = Estado.ComandoDeFuncao;
                return;

            case Estado.Buscando:
                if (passoDaBusca < 2) { passoDaBusca++; return; }
                // O mestre escreveu o caminho: quem tem o outro bit sai da busca.
                if (nivel != ((Rom[bitDaRom / 8] >> (bitDaRom % 8)) & 1)) { estado = Estado.Calado; return; }
                passoDaBusca = 0;
                if (++bitDaRom == 64) estado = Estado.ComandoDeFuncao;
                return;

            case Estado.Mandando:
                if (++mandados >= paraMandar.Count) estado = Estado.Calado;
                return;

            case Estado.Recebendo:
                byteMontado |= nivel << bitDoByte;
                if (++bitDoByte < 8) return;
                recebidos.Add((byte)byteMontado);
                bitDoByte = byteMontado = 0;
                if (recebidos.Count < paraReceber) return;
                if (comandoRecebendo == 0x4E)
                {
                    th = recebidos[0];
                    tl = recebidos[1];
                    configuracao = (byte)(0x1F | (recebidos[2] & 0x60));
                }
                estado = Estado.Calado;
                return;
        }
    }

    void ComandoDaRom(int comando)
    {
        bitDaRom = passoDaBusca = 0;
        estado = comando switch
        {
            0x33 => Estado.LendoRom,                          // READ ROM
            0x55 => Estado.CasandoRom,                        // MATCH ROM
            0xCC => Estado.ComandoDeFuncao,                   // SKIP ROM
            0xF0 => Estado.Buscando,                          // SEARCH ROM
            0xEC => alarme ? Estado.Buscando : Estado.Calado, // ALARM SEARCH
            _ => Estado.Calado,
        };
    }

    void ComandoDeFuncao(int comando, double agoraUs)
    {
        switch (comando)
        {
            case 0x44:      // CONVERT T: a medida entra no scratchpad no fim da conversao
            {
                int descartados = 12 - Resolucao;
                double t = Math.Clamp(Temperatura, -55, 125);
                int valor = (int)Math.Floor(t * 16 + 0.5);
                valor &= ~((1 << descartados) - 1);
                brutoMedido = (short)valor;
                convertendo = true;
                fimDaConversaoUs = agoraUs + TempoDeConversaoMs * 1000;
                // No modo parasita o chip nao segura o fio (precisa dele em HIGH): le-se 1.
                situacao = Parasita ? null : agora => agora >= fimDaConversaoUs ? 1 : 0;
                estado = Estado.Situacao;
                return;
            }
            case 0xBE:      // READ SCRATCHPAD
            {
                var bytes = new byte[9];
                bytes[0] = (byte)(bruto & 0xFF);
                bytes[1] = (byte)((bruto >> 8) & 0xFF);
                bytes[2] = th;
                bytes[3] = tl;
                bytes[4] = configuracao;
                bytes[5] = 0xFF;
                bytes[6] = 0x0C;
                bytes[7] = 0x10;
                bytes[8] = UmFio.Crc8(bytes, 8);
                Mandar(bytes);
                return;
            }
            case 0x4E:      // WRITE SCRATCHPAD: o TH, o TL e a configuracao
                comandoRecebendo = 0x4E;
                paraReceber = 3;
                recebidos.Clear();
                bitDoByte = byteMontado = 0;
                estado = Estado.Recebendo;
                return;
            case 0x48:      // COPY SCRATCHPAD: para a EEPROM (10 ms)
            {
                (eepromTh, eepromTl, eepromConfiguracao) = (th, tl, configuracao);
                double fim = agoraUs + 10_000;
                situacao = Parasita ? null : agora => agora >= fim ? 1 : 0;
                estado = Estado.Situacao;
                return;
            }
            case 0xB8:      // RECALL E2
                (th, tl, configuracao) = (eepromTh, eepromTl, eepromConfiguracao);
                situacao = _ => 1;
                estado = Estado.Situacao;
                return;
            case 0xB4:      // READ POWER SUPPLY: o parasita segura o fio em LOW
                situacao = _ => Parasita ? 0 : 1;
                estado = Estado.Situacao;
                return;
        }
        estado = Estado.Calado;
    }

    void Mandar(byte[] bytes)
    {
        paraMandar.Clear();
        foreach (byte b in bytes)
            for (int i = 0; i < 8; i++) paraMandar.Add((b >> i) & 1);
        mandados = 0;
        estado = Estado.Mandando;
    }
}

/// <summary>
/// O fio de dados do 1-Wire num pino da placa: os DS18B20 pendurados nele, o
/// pull-up que o fio precisa (sem ele o fio nunca sobe e ninguem responde) e
/// os intervalos de bit. O fio e um E: quem segura em LOW ganha.
/// </summary>
public sealed class BarramentoUmFio
{
    readonly Dictionary<string, Ds18b20> aparelhos = [];
    readonly object trava = new();

    /// <summary>Ha um resistor do fio para a alimentacao (o 4,7 kohm).</summary>
    public bool Puxado { get; set; } = true;

    /// <summary>O aparelho desta peca no fio (criado na primeira vez).</summary>
    public Ds18b20 Aparelho(string peca, Func<byte[]> rom)
    {
        lock (trava)
        {
            if (!aparelhos.TryGetValue(peca, out var a)) aparelhos[peca] = a = new Ds18b20(peca, rom());
            return a;
        }
    }

    /// <summary>Tira do fio quem nao esta mais nele (a peca apagada, o fio mudado).</summary>
    public void Manter(IEnumerable<string> pecas)
    {
        lock (trava)
            foreach (string id in aparelhos.Keys.Except(pecas).ToList()) aparelhos.Remove(id);
    }

    public int Quantos { get { lock (trava) return aparelhos.Count; } }

    /// <summary>A placa reiniciou a rodada: os chips voltam ao estado de quando ligam.</summary>
    public void Religar()
    {
        lock (trava)
            foreach (var a in aparelhos.Values) a.Religar();
    }

    /// <summary>O pulso de reset e a presenca: algum aparelho respondeu.</summary>
    public bool Reset(double agoraUs)
    {
        lock (trava)
        {
            bool alguem = false;
            foreach (var a in aparelhos.Values) alguem |= a.Reset(agoraUs);
            return Puxado && alguem;
        }
    }

    /// <summary>
    /// Um intervalo de bit: o mestre solta o fio (1, a leitura ou o bit 1) ou o
    /// segura (0); o que fica no fio e o E de todos. Sem o pull-up, o fio nao sobe.
    /// </summary>
    public int Bit(int mestre, double agoraUs)
    {
        lock (trava)
        {
            int fio = Puxado ? mestre & 1 : 0;
            foreach (var a in aparelhos.Values) fio &= a.Saida(agoraUs);
            foreach (var a in aparelhos.Values) a.Bit(fio, agoraUs);
            return fio;
        }
    }
}

/// <summary>As contas do 1-Wire.</summary>
public static class UmFio
{
    /// <summary>O CRC-8 da Maxim (X^8 + X^5 + X^4 + 1), o da ROM e do scratchpad.</summary>
    /// <summary>O CRC-16 do 1-Wire (o do DS2450 e dos contadores), como o OneWire::crc16.</summary>
    public static ushort Crc16(IReadOnlyList<byte> dados, int n, ushort crc = 0)
    {
        ReadOnlySpan<byte> paridade = [0, 1, 1, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 1, 1, 0];
        for (int i = 0; i < n && i < dados.Count; i++)
        {
            int c = (dados[i] ^ crc) & 0xFF;
            crc >>= 8;
            if ((paridade[c & 0x0F] ^ paridade[c >> 4]) != 0) crc ^= 0xC001;
            c <<= 6;
            crc ^= (ushort)c;
            c <<= 1;
            crc ^= (ushort)c;
        }
        return crc;
    }

    public static byte Crc8(IReadOnlyList<byte> dados, int n)
    {
        byte crc = 0;
        for (int i = 0; i < n && i < dados.Count; i++)
        {
            byte b = dados[i];
            for (int k = 0; k < 8; k++)
            {
                bool mistura = ((crc ^ b) & 1) != 0;
                crc >>= 1;
                if (mistura) crc ^= 0x8C;
                b >>= 1;
            }
        }
        return crc;
    }

    /// <summary>A ROM de um DS18B20 da bancada: a familia 0x28, o numero pela peca (o mesmo sempre), o CRC.</summary>
    public static byte[] RomDe(string peca)
    {
        uint h = 2166136261;
        foreach (char c in peca ?? "") h = (h ^ c) * 16777619;
        uint h2 = h * 2654435761u ^ 0x5bd1e995;
        var rom = new byte[8];
        rom[0] = 0x28;
        rom[1] = (byte)h; rom[2] = (byte)(h >> 8); rom[3] = (byte)(h >> 16); rom[4] = (byte)(h >> 24);
        rom[5] = (byte)h2; rom[6] = 0x00;
        rom[7] = Crc8(rom, 7);
        return rom;
    }
}
