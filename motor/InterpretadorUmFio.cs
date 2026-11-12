namespace Simulador;

/// <summary>
/// O OneWire e o DallasTemperature do sketch: os metodos da biblioteca sobre o
/// fio de 1-Wire da placa (BarramentoUmFio), bit a bit, como a biblioteca faz
/// nos pinos -- o reset com a presenca, os bytes do bit menos significativo
/// para o mais, a busca da ROM (o algoritmo da Maxim, o da OneWire) e o CRC.
/// O C++ compilado chega aqui pelo OneWire.h; o DallasTemperature dele e o
/// do cabecalho (o mesmo codigo da biblioteca, sobre este OneWire). O deste
/// arquivo e o do sketch interpretado.
/// </summary>
public partial class Interpretador
{
    /// <summary>O que a busca da OneWire guarda entre um search() e o outro.</summary>
    sealed class BuscaUmFio
    {
        public readonly byte[] Rom = new byte[8];
        public int UltimaDiscrepancia, UltimaDaFamilia;
        public bool UltimoAparelho;
    }

    /// <summary>O DallasTemperature do sketch interpretado.</summary>
    sealed class EstadoDallas
    {
        public readonly List<byte[]> Achados = [];
        public bool EsperarConversao = true, Parasita;
        public int Resolucao = 9;
    }

    const double UsDoReset = 960, UsDoBit = 65;

    BarramentoUmFio FioDe(Objeto obj) => obj.Pino >= 0 ? maq.UmFio(obj.Pino) : null;

    bool ResetUmFio(BarramentoUmFio fio)
    {
        if (fio is null) return false;
        bool presenca = fio.Reset(maq.AgoraUs());
        maq.EsperarMicros(UsDoReset);
        return presenca;
    }

    int BitUmFio(BarramentoUmFio fio, int mestre)
    {
        if (fio is null) return 0;
        int fiou = fio.Bit(mestre, maq.AgoraUs());
        maq.EsperarMicros(UsDoBit);
        return fiou;
    }

    void EscreverUmFio(BarramentoUmFio fio, int valor)
    {
        for (int i = 0; i < 8; i++) BitUmFio(fio, (valor >> i) & 1);
    }

    int LerUmFio(BarramentoUmFio fio)
    {
        int v = 0;
        for (int i = 0; i < 8; i++) v |= BitUmFio(fio, 1) << i;
        return v;
    }

    /// <summary>
    /// O vetor do argumento: o que veio pronto do C++ compilado (cs_vetor), ou o vetor
    /// do sketch interpretado pelo nome ("DeviceAddress a; sensores.getAddress(a, 0)").
    /// </summary>
    Valor[] VetorUmFio(List<Valor> args, Chamada chamada, Ambiente amb, int i)
    {
        if (i < args.Count && args[i].Referencia is Valor[] pronto) return pronto;
        return chamada is not null && i < chamada.Args.Count && VetorDoArgumento(chamada.Args[i], amb, out var vetor) ? vetor : null;
    }

    byte[] BytesDoArgumento(List<Valor> args, Chamada chamada, Ambiente amb, int i, int n)
    {
        var b = new byte[n];
        if (VetorUmFio(args, chamada, amb, i) is { } v)
            for (int k = 0; k < n && k < v.Length; k++) b[k] = (byte)((long)v[k].Num & 0xFF);
        return b;
    }

    void BytesNoArgumento(List<Valor> args, Chamada chamada, Ambiente amb, int i, IReadOnlyList<byte> dados)
    {
        if (VetorUmFio(args, chamada, amb, i) is not { } v) return;
        for (int k = 0; k < dados.Count && k < v.Length; k++) v[k] = NoLugar(v[k], Valor.DeC(dados[k], TipoC.UChar));
    }

    /// <summary>As contas estaticas: "OneWire::crc8(endereco, 7)", "OneWire::crc16(...)".</summary>
    Valor ChamarUmFioEstatico(string metodo, Chamada chamada, Ambiente amb)
        => ContasDoUmFio(metodo, [.. chamada.Args.Select(a => Avaliar(a, amb))], chamada, amb);

    Valor ContasDoUmFio(string metodo, List<Valor> args, Chamada chamada, Ambiente amb)
    {
        int A(int i) => i < args.Count ? (int)args[i].Num : 0;
        switch (metodo)
        {
            case "crc8": return Valor.DeInt(UmFio.Crc8(BytesDoArgumento(args, chamada, amb, 0, A(1)), A(1)));
            case "crc16": return Valor.DeInt(UmFio.Crc16(BytesDoArgumento(args, chamada, amb, 0, A(1)), A(1), (ushort)A(2)));
            case "check_crc16":
            {
                int crc = ~UmFio.Crc16(BytesDoArgumento(args, chamada, amb, 0, A(1)), A(1), (ushort)A(3)) & 0xFFFF;
                var invertido = BytesDoArgumento(args, chamada, amb, 2, 2);
                return Valor.DeBool((crc & 0xFF) == invertido[0] && crc >> 8 == invertido[1]);
            }
        }
        throw new ErroDeExecucao($"OneWire::{metodo}() ainda nao e suportado", chamada?.Linha ?? 0);
    }

    /// <summary>O search() da OneWire: a proxima ROM do fio, pelo algoritmo da nota de aplicacao 187 da Maxim.</summary>
    bool BuscarUmFio(BarramentoUmFio fio, BuscaUmFio b, byte[] achada, int comando = 0xF0)
    {
        if (b.UltimoAparelho) return Recomecar();
        if (!ResetUmFio(fio)) return Recomecar();
        EscreverUmFio(fio, comando);
        int ultimoZero = 0;
        for (int i = 1; i <= 64; i++)
        {
            int bitDoId = BitUmFio(fio, 1), complemento = BitUmFio(fio, 1);
            if (bitDoId == 1 && complemento == 1) return Recomecar();
            int k = (i - 1) / 8, mascara = 1 << ((i - 1) % 8);
            int direcao;
            if (bitDoId != complemento) direcao = bitDoId;
            else
            {
                direcao = i < b.UltimaDiscrepancia ? ((b.Rom[k] & mascara) != 0 ? 1 : 0) : i == b.UltimaDiscrepancia ? 1 : 0;
                if (direcao == 0)
                {
                    ultimoZero = i;
                    if (ultimoZero < 9) b.UltimaDaFamilia = ultimoZero;
                }
            }
            if (direcao == 1) b.Rom[k] |= (byte)mascara;
            else b.Rom[k] &= (byte)~mascara;
            BitUmFio(fio, direcao);
        }
        b.UltimaDiscrepancia = ultimoZero;
        if (b.UltimaDiscrepancia == 0) b.UltimoAparelho = true;
        if (b.Rom[0] == 0) return Recomecar();
        Array.Copy(b.Rom, achada, 8);
        return true;

        bool Recomecar()
        {
            b.UltimaDiscrepancia = 0;
            b.UltimoAparelho = false;
            b.UltimaDaFamilia = 0;
            return false;
        }
    }

    Valor ChamarUmFio(Objeto obj, string metodo, List<Valor> args, Chamada chamada = null, Ambiente amb = null)
    {
        var fio = FioDe(obj);
        var busca = obj.Rede as BuscaUmFio ?? (BuscaUmFio)(obj.Rede = new BuscaUmFio());
        int A(int i) => i < args.Count ? (int)args[i].Num : 0;
        switch (metodo)
        {
            case "begin": obj.Pino = A(0); return Valor.Vazio;
            case "reset": return Valor.DeInt(ResetUmFio(fio) ? 1 : 0);
            case "write": EscreverUmFio(fio, A(0)); return Valor.Vazio;
            case "write_bytes":
                foreach (byte v in BytesDoArgumento(args, chamada, amb, 0, A(1))) EscreverUmFio(fio, v);
                return Valor.Vazio;
            case "read": return Valor.DeInt(LerUmFio(fio));
            case "read_bytes":
            {
                var lidos = new byte[Math.Max(0, A(1))];
                for (int i = 0; i < lidos.Length; i++) lidos[i] = (byte)LerUmFio(fio);
                BytesNoArgumento(args, chamada, amb, 0, lidos);
                return Valor.Vazio;
            }
            case "write_bit": BitUmFio(fio, A(0) & 1); return Valor.Vazio;
            case "read_bit": return Valor.DeInt(BitUmFio(fio, 1));
            case "select":
                EscreverUmFio(fio, 0x55);
                foreach (byte v in BytesDoArgumento(args, chamada, amb, 0, 8)) EscreverUmFio(fio, v);
                return Valor.Vazio;
            case "skip": EscreverUmFio(fio, 0xCC); return Valor.Vazio;
            case "depower": return Valor.Vazio;
            case "reset_search":
                busca.UltimaDiscrepancia = busca.UltimaDaFamilia = 0;
                busca.UltimoAparelho = false;
                Array.Clear(busca.Rom);
                return Valor.Vazio;
            case "target_search":
                Array.Clear(busca.Rom);
                busca.Rom[0] = (byte)A(0);
                busca.UltimaDiscrepancia = 64;
                busca.UltimaDaFamilia = 0;
                busca.UltimoAparelho = false;
                return Valor.Vazio;
            case "search":
            {
                var achada = new byte[8];
                bool ok = BuscarUmFio(fio, busca, achada, args.Count > 1 && args[1].Num == 0 ? 0xEC : 0xF0);
                if (ok) BytesNoArgumento(args, chamada, amb, 0, achada);
                return Valor.DeBool(ok);
            }
            case "crc8":
            case "crc16":
            case "check_crc16":
                return ContasDoUmFio(metodo, args, chamada, amb);
        }
        throw new ErroDeExecucao($"OneWire.{metodo}() ainda nao e suportado", 0);
    }

    // =====================================================================
    //  o DallasTemperature do sketch interpretado
    // =====================================================================

    static bool FamiliaDeTermometro(byte f) => f is 0x10 or 0x22 or 0x28 or 0x3B or 0x42;

    static int MsDaConversao(int bits) => bits switch { 9 => 94, 10 => 188, 11 => 375, _ => 750 };

    bool LerScratchpad(BarramentoUmFio fio, byte[] rom, byte[] sp)
    {
        if (!ResetUmFio(fio)) return false;
        EscreverUmFio(fio, 0x55);
        foreach (byte v in rom) EscreverUmFio(fio, v);
        EscreverUmFio(fio, 0xBE);
        for (int i = 0; i < 9; i++) sp[i] = (byte)LerUmFio(fio);
        bool todosZero = sp.All(b => b == 0);
        return !todosZero && UmFio.Crc8(sp, 8) == sp[8];
    }

    Valor ChamarDallas(Objeto obj, string metodo, List<Valor> args, Chamada chamada = null, Ambiente amb = null)
    {
        var fio = FioDe(obj);
        var d = obj.Rede as EstadoDallas ?? (EstadoDallas)(obj.Rede = new EstadoDallas());
        int A(int i) => i < args.Count ? (int)args[i].Num : 0;

        float TempC(byte[] rom)
        {
            var sp = new byte[9];
            if (!LerScratchpad(fio, rom, sp)) return -127f;
            short bruto = (short)(sp[0] | sp[1] << 8);
            return rom[0] == 0x10 ? bruto / 2f : bruto / 16f;
        }

        void Converter()
        {
            ResetUmFio(fio);
            EscreverUmFio(fio, 0xCC);
            EscreverUmFio(fio, 0x44);
            if (!d.EsperarConversao) return;
            long ms = MsDaConversao(d.Resolucao);
            if (d.Parasita) { maq.Avancar(ms); Ceder(); AtenderInterrupcoes(); return; }
            // Como a biblioteca: le o fio ate a conversao acabar (ele vai a 1). O tempo da
            // resolucao numa espera so -- 750 esperas de 1 ms no tempo real passavam do ponto
            // cada uma (o Sleep do Windows), e a de 12 bits levava 1,3 s.
            maq.Avancar(ms);
            AtenderInterrupcoes();
            long ate = maq.Ms + 50;
            while (BitUmFio(fio, 1) == 0 && maq.Ms < ate && !maq.Parar) { maq.Avancar(1); AtenderInterrupcoes(); }
            Ceder();
        }

        switch (metodo)
        {
            case "begin":
            {
                d.Achados.Clear();
                d.Parasita = false;
                var b = new BuscaUmFio();
                var rom = new byte[8];
                while (BuscarUmFio(fio, b, rom))
                    if (UmFio.Crc8(rom, 7) == rom[7])
                    {
                        d.Achados.Add([.. rom]);
                        // A alimentacao (READ POWER SUPPLY) e a resolucao de cada um.
                        ResetUmFio(fio);
                        EscreverUmFio(fio, 0x55);
                        foreach (byte v in rom) EscreverUmFio(fio, v);
                        EscreverUmFio(fio, 0xB4);
                        if (BitUmFio(fio, 1) == 0) d.Parasita = true;
                        var sp = new byte[9];
                        if (FamiliaDeTermometro(rom[0]) && LerScratchpad(fio, rom, sp))
                            d.Resolucao = Math.Max(d.Resolucao, rom[0] == 0x10 ? 12 : 9 + ((sp[4] >> 5) & 3));
                    }
                return Valor.Vazio;
            }
            case "getDeviceCount": return Valor.DeInt(d.Achados.Count);
            case "getDS18Count": return Valor.DeInt(d.Achados.Count(r => FamiliaDeTermometro(r[0])));
            case "isParasitePowerMode": return Valor.DeBool(d.Parasita);
            case "setWaitForConversion": d.EsperarConversao = A(0) != 0; return Valor.Vazio;
            case "getWaitForConversion": return Valor.DeBool(d.EsperarConversao);
            case "getResolution": return Valor.DeInt(d.Resolucao);
            case "millisToWaitForConversion": return Valor.DeInt(MsDaConversao(args.Count > 0 ? A(0) : d.Resolucao));
            case "isConversionComplete": return Valor.DeBool(BitUmFio(fio, 1) == 1);
            case "setResolution":
            {
                int bits = Math.Clamp(A(args.Count > 1 ? 1 : 0), 9, 12);
                d.Resolucao = bits;
                foreach (var rom in d.Achados.Where(r => r[0] != 0x10))
                {
                    var sp = new byte[9];
                    if (!LerScratchpad(fio, rom, sp)) continue;
                    ResetUmFio(fio);
                    EscreverUmFio(fio, 0x55);
                    foreach (byte v in rom) EscreverUmFio(fio, v);
                    EscreverUmFio(fio, 0x4E);
                    EscreverUmFio(fio, sp[2]);
                    EscreverUmFio(fio, sp[3]);
                    EscreverUmFio(fio, ((bits - 9) << 5) | 0x1F);
                }
                return Valor.DeBool(true);
            }
            case "requestTemperatures":
            case "requestTemperaturesByIndex":
            case "requestTemperaturesByAddress":
                Converter();
                return Valor.DeBool(true);
            case "getAddress":
            {
                int i = A(1);
                if (i < 0 || i >= d.Achados.Count) return Valor.DeBool(false);
                BytesNoArgumento(args, chamada, amb, 0, d.Achados[i]);
                return Valor.DeBool(true);
            }
            case "getTempCByIndex":
            case "getTempFByIndex":
            {
                int i = A(0);
                float c = i >= 0 && i < d.Achados.Count ? TempC(d.Achados[i]) : -127f;
                return Valor.DeReal(metodo == "getTempFByIndex" ? (c <= -127f ? -196.6f : c * 1.8f + 32) : c);
            }
            case "getTempC":
            case "getTempF":
            {
                float c = TempC(BytesDoArgumento(args, chamada, amb, 0, 8));
                return Valor.DeReal(metodo == "getTempF" ? (c <= -127f ? -196.6f : c * 1.8f + 32) : c);
            }
            case "isConnected": return Valor.DeBool(LerScratchpad(fio, BytesDoArgumento(args, chamada, amb, 0, 8), new byte[9]));
            case "toFahrenheit": return Valor.DeReal(args.Count > 0 ? args[0].Num * 1.8 + 32 : 32);
            case "toCelsius": return Valor.DeReal(args.Count > 0 ? (args[0].Num - 32) / 1.8 : 0);
        }
        throw new ErroDeExecucao($"DallasTemperature.{metodo}() ainda nao e suportado", 0);
    }
}
