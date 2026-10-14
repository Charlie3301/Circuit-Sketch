namespace Simulador;

/// <summary>
/// A EEPROM e a memoria RTC do usuario do ESP8266: o que o sketch guarda em
/// BYTES, no layout da placa.
///
/// No AVR a EEPROM e de verdade: 1 KB no Uno, 4 KB no Mega, 512 bytes no
/// ATtiny85; nasce apagada (0xFF), cada escrita leva ~3,3 ms e cada celula
/// aguenta ~100 mil escritas. Na ESP32, na ESP8266 e na Pico ela e emulada na
/// flash: precisa do begin(tamanho), escreve num buffer na RAM e so vai para
/// a flash no commit() -- sem ele, o reset perde tudo, como na placa.
///
/// O put()/get() de uma struct passa pelos bytes de verdade: o int de 2
/// bytes do Uno, o double de 4 do AVR, o alinhamento dos campos na ESP32.
/// </summary>
public partial class Interpretador
{
    // ---------------- os bytes de um valor ----------------

    TipoC TipoCDe(string tipo) => TiposC.DoNome(TipoLimpo(tipo ?? ""), ModeloC);

    /// <summary>Um numero, struct ou vetor do sketch em bytes, como mora na memoria da placa (little-endian).</summary>
    void EmBytes(List<byte> saida, Valor v, string tipo, int profundidade = 0)
    {
        tipo = TipoLimpo(tipo ?? "");
        if (v.Referencia is Objeto o && CamposDoTipo(o.Classe) is not null && profundidade < 8)
        {
            EstruturaEmBytes(saida, o, profundidade);
            return;
        }
        if (v.Referencia is Valor[] vetor)
        {
            foreach (var item in vetor) EmBytes(saida, item, tipo, profundidade + 1);
            return;
        }
        if (v.EhTexto)
        {
            saida.AddRange(TextoArduino.ParaBytes(v.Texto));
            saida.Add(0);
            return;
        }

        var t = TipoCDe(tipo);
        if (t == TipoC.Nenhum) t = v.Inteiro ? TipoC.Int : TipoC.Float;
        int bytes = ModeloC.Bits(t) / 8;
        if (TiposC.EhReal(t)) saida.AddRange(bytes == 4 ? BitConverter.GetBytes((float)v.Num) : BitConverter.GetBytes(v.Num));
        else
        {
            long x = (long)v.Num;
            for (int k = 0; k < bytes; k++) saida.Add((byte)(x >> (8 * k)));
        }
    }

    void EstruturaEmBytes(List<byte> saida, Objeto o, int profundidade)
    {
        int inicio = saida.Count, maior = 1;
        foreach (var campo in CamposDoTipo(o.Classe))
        {
            if (campo is not CmdDecl d) continue;
            var (tamanho, alinhamento) = BytesDoCampo(d, profundidade + 1);
            Completar(saida, inicio + Alinhar(saida.Count - inicio, alinhamento));
            maior = Math.Max(maior, alinhamento);
            int antes = saida.Count;
            var valor = CampoDe(o, d.Nome);
            if (d.EhArray && valor.EhTexto) saida.AddRange(TextoArduino.ParaBytes(valor.Texto));
            else EmBytes(saida, valor, d.Tipo, profundidade + 1);
            if (saida.Count > antes + tamanho) saida.RemoveRange(antes + tamanho, saida.Count - antes - tamanho);
            Completar(saida, antes + tamanho);
        }
        Completar(saida, inicio + Alinhar(saida.Count - inicio, maior));
    }

    static void Completar(List<byte> saida, int ate)
    {
        while (saida.Count < ate) saida.Add(0);
    }

    static Valor CampoDe(Objeto o, string nome)
    {
        if (o.Escopo is { } escopo)
            return escopo.BuscarArrayLocal(nome, out var vetor) ? Valor.DeReferencia(vetor)
                 : escopo.BuscarLocal(nome, out var valor) ? valor : Valor.DeInt(0);
        return o.Campos.TryGetValue(nome, out var campo) ? campo : Valor.DeInt(0);
    }

    /// <summary>Um numero do tipo dado, lido dos bytes a partir de pos (que anda).</summary>
    Valor NumeroDosBytes(byte[] b, ref int pos, string tipo)
    {
        var t = TipoCDe(tipo);
        if (t == TipoC.Nenhum) t = TipoC.Int;
        int n = ModeloC.Bits(t) / 8;
        Valor v;
        if (pos + n > b.Length) v = Valor.DeInt(0);
        else if (TiposC.EhReal(t)) v = Valor.DeReal(n == 4 ? BitConverter.ToSingle(b, pos) : BitConverter.ToDouble(b, pos));
        else
        {
            long x = 0;
            for (int k = 0; k < n; k++) x |= (long)b[pos + k] << (8 * k);
            if (n < 8 && ModeloC.ComSinal(t) && (x & (1L << (n * 8 - 1))) != 0) x -= 1L << (n * 8);
            v = EhCaractere(tipo) ? Valor.DeCaractere(x) : Valor.DeInt(x);
        }
        pos += n;
        return Coagir(v, tipo);
    }

    void EstruturaDosBytes(Objeto o, byte[] b, ref int pos, int profundidade)
    {
        int inicio = pos, maior = 1;
        foreach (var campo in CamposDoTipo(o.Classe))
        {
            if (campo is not CmdDecl d) continue;
            var (tamanho, alinhamento) = BytesDoCampo(d, profundidade + 1);
            pos = inicio + Alinhar(pos - inicio, alinhamento);
            maior = Math.Max(maior, alinhamento);
            int antes = pos;
            var atual = CampoDe(o, d.Nome);

            Valor novo;
            if (atual.Referencia is Objeto dentro && CamposDoTipo(dentro.Classe) is not null)
            {
                EstruturaDosBytes(dentro, b, ref pos, profundidade + 1);
                novo = atual;
            }
            else if (d.EhArray && (atual.EhTexto || EhCaractere(d.Tipo)))
            {
                // char nome[8]: o texto ate o '\0'.
                var letras = new List<byte>();
                for (int k = 0; k < tamanho && antes + k < b.Length && b[antes + k] != 0; k++) letras.Add(b[antes + k]);
                novo = Valor.DeTexto(TextoArduino.DeBytes([.. letras]));
            }
            else if (atual.Referencia is Valor[] vetor)
            {
                for (int k = 0; k < vetor.Length; k++) vetor[k] = NumeroDosBytes(b, ref pos, d.Tipo);
                novo = atual;
            }
            else novo = NumeroDosBytes(b, ref pos, d.Tipo);

            if (o.Escopo is { } escopo)
            {
                if (!escopo.BuscarArrayLocal(d.Nome, out _)) escopo.Atribuir(d.Nome, novo);
            }
            else o.Campos[d.Nome] = novo;
            pos = antes + tamanho;
        }
        pos = inicio + Alinhar(pos - inicio, maior);
    }

    /// <summary>O tipo declarado do que a expressao nomeia: a variavel, o campo, o item do vetor.</summary>
    string TipoDeclaradoDe(Expr e, Ambiente amb)
    {
        switch (e)
        {
            case Variavel v:
            {
                string t = amb.TipoDe(v.Nome);
                return t is null ? null : t.Contains('|') ? t[..t.IndexOf('|')] : t;
            }
            case Indice ix:
            {
                Expr raiz = ix;
                while (raiz is Indice i) raiz = i.Alvo;
                return TipoDeclaradoDe(raiz, amb)?.TrimEnd('*').Trim();
            }
            case Membro m when ObjetoDaExpressao(m.Obj, amb) is { } dono:
                return CamposDoTipo(dono.Classe)?.OfType<CmdDecl>().FirstOrDefault(d => d.Nome == m.Nome)?.Tipo;
            case Conversao c:
                return c.Tipo;
            case Literal l:
                return l.EhTexto ? "char*" : l.Inteiro ? "int" : "double";
        }
        return null;
    }

    /// <summary>Os bytes do que o sketch passou: "EEPROM.put(0, cfg)", "(uint32_t*)&amp;dados".</summary>
    byte[] BytesDaExpressao(Expr e, Ambiente amb, int linha)
    {
        var sem = SemEndereco(e);
        while (sem is Conversao conv) sem = SemEndereco(conv.Alvo);
        var saida = new List<byte>();

        if (ObjetoDaExpressao(sem, amb) is { } o && CamposDoTipo(o.Classe) is not null)
        {
            EstruturaEmBytes(saida, o, 0);
            return [.. saida];
        }
        if (VetorDoArgumento(sem, amb, out var vetor))
        {
            string tipoDoItem = TipoDeclaradoDe(sem, amb)?.TrimEnd('*').Trim();
            if (vetor.Length > 0 && vetor[0].Caractere) tipoDoItem ??= "char";
            foreach (var item in vetor) EmBytes(saida, item, tipoDoItem);
            return [.. saida];
        }

        var valor = Avaliar(sem, amb);
        string tipo = TipoDeclaradoDe(sem, amb);
        if (tipo == "String" || valor.EhTexto && tipo is null)
        {
            Avisar("EEPROM: put() de uma String grava o OBJETO String (o endereco do texto, que muda a cada boot), e nao as "
                   + "letras. Guarde num char[] -- ou use writeString()/readString() na ESP32.");
            Completar(saida, BytesDoTipo("String"));
            return [.. saida];
        }
        EmBytes(saida, valor, tipo);
        return [.. saida];
    }

    /// <summary>Os bytes viram o destino: struct, vetor ou numero ("EEPROM.get(0, cfg)").</summary>
    void GuardarDosBytes(Expr destino, byte[] b, Ambiente amb)
    {
        var sem = SemEndereco(destino);
        while (sem is Conversao conv) sem = SemEndereco(conv.Alvo);
        int pos = 0;

        if (ObjetoDaExpressao(sem, amb) is { } o && CamposDoTipo(o.Classe) is not null)
        {
            EstruturaDosBytes(o, b, ref pos, 0);
            return;
        }
        if (VetorDoArgumento(sem, amb, out var vetor))
        {
            string tipoDoItem = TipoDeclaradoDe(sem, amb)?.TrimEnd('*').Trim() ?? (vetor.Length > 0 && vetor[0].Caractere ? "char" : "int");
            for (int k = 0; k < vetor.Length; k++) vetor[k] = NumeroDosBytes(b, ref pos, tipoDoItem);
            return;
        }
        string tipo = TipoDeclaradoDe(sem, amb) ?? "int";
        if (tipo == "String")
        {
            Guardar(sem, Valor.DeTexto(""), amb);
            return;
        }
        Guardar(sem, NumeroDosBytes(b, ref pos, tipo), amb);
    }

    // ---------------- a EEPROM ----------------

    /// <summary>A EEPROM de verdade de cada chip (0: emulada na flash, pede begin(tamanho)).</summary>
    int EepromDoChip => Chip switch
    {
        "ATmega328P" => 1024,
        "ATmega2560" => 4096,
        "ATtiny85" => 512,
        "STM32F103C8" or "STM32L031K6" or "STM32C031C6" => 1024,
        _ => 0,
    };

    bool EepromNaFlash => EhFamiliaEsp32 || Chip is "ESP8266" or "RP2040";

    const string ChaveDaEeprom = "eeprom\0eeprom";

    byte[] eepromNaRam;
    bool eepromSuja;
    double eepromOcupadaAteUs;

    /// <summary>A memoria em que o read/write mexe: a EEPROM do chip, ou o buffer do begin() na RAM.</summary>
    byte[] MemoriaDaEeprom => EepromNaFlash ? eepromNaRam : maq.EepromDoChip(EepromDoChip);

    Valor ChamarEeprom(string metodo, Chamada c, Ambiente amb)
    {
        if (maq.Perfil?.Id == "nano-rp2040-connect")
            throw new ErroDeExecucao("EEPROM.h: No such file or directory -- o core mbed da Nano RP2040 Connect nao tem EEPROM. "
                                     + "Guarde na flash (KVStore ou LittleFS).", c.Linha);
        if (EepromDoChip == 0 && !EepromNaFlash)
            throw new ErroDeExecucao($"a {maq.Perfil?.Nome} nao tem EEPROM no simulador.", c.Linha);

        int A(int i) => i < c.Args.Count ? (int)Avaliar(c.Args[i], amb).Num : 0;
        var memoria = MemoriaDaEeprom;

        switch (metodo)
        {
            case "begin":
            {
                if (!EepromNaFlash)
                {
                    if (c.Args.Count == 0) return Valor.DeInt(0);   // o iterador do AVR
                    throw new ErroDeExecucao("no matching function for call to 'EEPROMClass::begin(int)' -- no "
                                             + $"{maq.Perfil?.Nome} a EEPROM e de verdade: nao tem begin(tamanho) nem commit().", c.Linha);
                }
                int pedido = A(0);
                if (pedido <= 0) return Valor.DeBool(false);
                // ESP8266 e Pico: um setor de 4 KB, em multiplos de 4.
                int tamanho = EhFamiliaEsp32 ? pedido : Math.Min(4096, (pedido + 3) & ~3);
                var novo = new byte[tamanho];
                Array.Fill(novo, (byte)0xFF);
                lock (maq.Nvs)
                    if (maq.Nvs.TryGetValue(ChaveDaEeprom, out var gravado))
                        Array.Copy(gravado.Dados, novo, Math.Min(gravado.Dados.Length, tamanho));
                eepromNaRam = novo;
                eepromSuja = false;
                return Valor.DeBool(true);
            }

            case "length":
                return Valor.DeInt(memoria?.Length ?? 0);

            case "read":
            case "readByte":
            {
                int endereco = A(0);
                EsperarEeprom();
                return Valor.DeC(memoria is not null && endereco >= 0 && endereco < memoria.Length ? memoria[endereco] : 0, TipoC.UChar);
            }

            case "write":
            case "writeByte":
            case "update":
            {
                int endereco = A(0);
                byte valor = (byte)((long)(c.Args.Count > 1 ? Avaliar(c.Args[1], amb).Num : 0) & 0xFF);
                EscreverNaEeprom(endereco, [valor], metodo == "update", c.Linha);
                return Valor.Vazio;
            }

            // put(endereco, valor): os bytes do valor (o AVR grava so os que mudaram).
            case "put":
            {
                int endereco = A(0);
                var bytes = c.Args.Count > 1 ? BytesDaExpressao(c.Args[1], amb, c.Linha) : [];
                EscreverNaEeprom(endereco, bytes, true, c.Linha);
                return c.Args.Count > 1 ? Avaliar(c.Args[1], amb) : Valor.Vazio;
            }

            case "get":
            {
                int endereco = A(0);
                if (c.Args.Count < 2) return Valor.Vazio;
                int tamanho = BytesDaExpressao(c.Args[1], amb, c.Linha).Length;
                EsperarEeprom();
                var bytes = new byte[tamanho];
                for (int k = 0; k < tamanho; k++)
                    bytes[k] = memoria is not null && endereco + k >= 0 && endereco + k < memoria.Length ? memoria[endereco + k] : (byte)0;
                GuardarDosBytes(c.Args[1], bytes, amb);
                return Avaliar(c.Args[1], amb);
            }

            case "commit":
            {
                if (!EepromNaFlash)
                    throw new ErroDeExecucao($"'class EEPROMClass' has no member named 'commit' -- no {maq.Perfil?.Nome} a EEPROM grava na hora.", c.Linha);
                return Valor.DeBool(GravarEepromNaFlash());
            }

            case "end":
                if (EepromNaFlash)
                {
                    GravarEepromNaFlash();
                    eepromNaRam = null;
                }
                return Valor.Vazio;

            // As da ESP32: readInt/writeFloat/readString...
            case "writeString":
            case "readString":
            {
                if (!EhFamiliaEsp32) break;
                int endereco = A(0);
                if (metodo == "readString")
                {
                    var letras = new List<byte>();
                    for (int k = endereco; memoria is not null && k >= 0 && k < memoria.Length && memoria[k] != 0; k++) letras.Add(memoria[k]);
                    return Valor.DeTexto(TextoArduino.DeBytes([.. letras]));
                }
                var texto = TextoArduino.ParaBytes(c.Args.Count > 1 ? Avaliar(c.Args[1], amb).Mostrar() : "");
                EscreverNaEeprom(endereco, [.. texto, 0], false, c.Linha);
                return Valor.DeInt(texto.Length + 1);
            }
        }

        // readInt(endereco), writeFloat(endereco, valor)...: o tipo vem do nome.
        if (EhFamiliaEsp32 && (metodo.StartsWith("read", StringComparison.Ordinal) || metodo.StartsWith("write", StringComparison.Ordinal))
            && TipoDoMetodoDaEeprom(metodo[(metodo.StartsWith("read", StringComparison.Ordinal) ? 4 : 5)..]) is { } tipoDoMetodo)
        {
            int endereco = A(0);
            if (metodo.StartsWith("read", StringComparison.Ordinal))
            {
                int n = ModeloC.Bits(TipoCDe(tipoDoMetodo)) / 8, pos = 0;
                var bytes = new byte[n];
                for (int k = 0; k < n; k++) bytes[k] = memoria is not null && endereco + k < memoria.Length ? memoria[endereco + k] : (byte)0;
                return NumeroDosBytes(bytes, ref pos, tipoDoMetodo);
            }
            var saida = new List<byte>();
            EmBytes(saida, c.Args.Count > 1 ? Avaliar(c.Args[1], amb) : Valor.DeInt(0), tipoDoMetodo);
            EscreverNaEeprom(endereco, [.. saida], false, c.Linha);
            return Valor.DeInt(saida.Count);
        }

        throw new ErroDeExecucao($"EEPROM.{metodo}() ainda nao e suportado pelo simulador. A EEPROM tem read, write, update, "
                                 + "get, put, length" + (EepromNaFlash ? ", begin, commit e end." : " e o EEPROM[i]."), c.Linha);
    }

    static string TipoDoMetodoDaEeprom(string sufixo) => sufixo switch
    {
        "Char" => "char", "UChar" => "unsigned char", "Short" => "int16_t", "UShort" => "uint16_t",
        "Int" => "int32_t", "UInt" => "uint32_t", "Long" => "int32_t", "ULong" => "uint32_t",
        "Long64" => "int64_t", "ULong64" => "uint64_t", "Float" => "float", "Double" => "double", "Bool" => "bool",
        _ => null,
    };

    /// <summary>"EEPROM[i]": le o byte. No AVR espera a escrita que estiver em andamento.</summary>
    Valor LerEepromNoIndice(Indice ix, Ambiente amb)
    {
        int endereco = (int)Avaliar(ix.Idx, amb).Num;
        var memoria = MemoriaDaEeprom;
        EsperarEeprom();
        return Valor.DeC(memoria is not null && endereco >= 0 && endereco < memoria.Length ? memoria[endereco] : 0, TipoC.UChar);
    }

    /// <summary>"EEPROM[i] = v": o operator= do EERef escreve sempre (nao e o update).</summary>
    void EscreverEepromNoIndice(Indice ix, Valor v, Ambiente amb)
        => EscreverNaEeprom((int)Avaliar(ix.Idx, amb).Num, [(byte)((long)v.Num & 0xFF)], false, ix.Linha);

    /// <summary>A escrita do AVR espera a anterior acabar (~3,3 ms cada byte); a leitura tambem.</summary>
    void EsperarEeprom()
    {
        if (EepromNaFlash) return;
        double agora = maq.AgoraUs();
        if (agora < eepromOcupadaAteUs) maq.EsperarNoChip(eepromOcupadaAteUs - agora);
    }

    void EscreverNaEeprom(int endereco, byte[] bytes, bool soOQueMudou, int linha)
    {
        var memoria = MemoriaDaEeprom;
        if (memoria is null)
        {
            Avisar("EEPROM: escrita antes do EEPROM.begin(tamanho) -- na " + maq.Perfil?.Nome + " a EEPROM e emulada na flash "
                   + "e, sem o begin(), o write() nao faz nada.");
            return;
        }
        for (int k = 0; k < bytes.Length; k++)
        {
            int e = endereco + k;
            // Fora da EEPROM: no AVR o endereco da a volta (os bits de cima nao existem); na ESP, nada acontece.
            if (!EepromNaFlash) e &= memoria.Length - 1;
            if (e < 0 || e >= memoria.Length) continue;
            if (soOQueMudou && memoria[e] == bytes[k]) continue;

            if (EepromNaFlash)
            {
                memoria[e] = bytes[k];
                eepromSuja = true;
                continue;
            }

            EsperarEeprom();
            memoria[e] = bytes[k];
            eepromOcupadaAteUs = maq.AgoraUs() + 3400;
            if (maq.ContarEscritaNaEeprom(e) == 100_000)
                Avisar($"EEPROM: a celula {e} ja foi escrita 100 mil vezes -- e o que o datasheet garante. Na placa ela "
                       + "comeca a falhar; escreva so quando o valor muda (update/put), e nunca a cada volta do loop().");
        }
        _ = linha;
    }

    /// <summary>O commit(): o buffer vai para a flash (a NVS da ESP32, o setor da ESP8266 e da Pico).</summary>
    bool GravarEepromNaFlash()
    {
        if (eepromNaRam is null) return false;
        if (!eepromSuja) return true;
        lock (maq.Nvs) maq.Nvs[ChaveDaEeprom] = (0x42, [.. eepromNaRam]);
        eepromSuja = false;
        // Apagar e gravar o setor: a NVS da ESP32 e rapida; a ESP8266 e a Pico apagam 4 KB inteiros.
        maq.EsperarNoChip(EhFamiliaEsp32 ? 8_000 : 45_000);
        if (maq.ContarCommitDaEeprom() == 10_000)
            Avisar("EEPROM: 10 mil commit() -- o setor da flash aguenta isso, nao muito mais. Grave so quando o valor muda.");
        return true;
    }

    // ---------------- a memoria RTC do usuario (ESP8266) ----------------

    /// <summary>
    /// ESP.rtcUserMemoryRead/Write(bloco, dados, bytes): 512 bytes em blocos de
    /// 4 que sobrevivem ao deepSleep e ao reset -- e nascem com lixo quando a
    /// placa liga, por isso todo exemplo confere um CRC antes de usar.
    /// </summary>
    Valor ChamarMemoriaRtcDoUsuario(bool ler, Chamada c, Ambiente amb)
    {
        int bloco = c.Args.Count > 0 ? (int)Avaliar(c.Args[0], amb).Num : 0;
        int tamanho = c.Args.Count > 2 ? (int)Avaliar(c.Args[2], amb).Num : 0;
        if (bloco < 0 || bloco * 4 + tamanho > 512 || tamanho <= 0 || c.Args.Count < 2) return Valor.DeBool(false);

        var memoria = maq.MemoriaRtcDoUsuario();
        if (ler)
        {
            var bytes = new byte[tamanho];
            Array.Copy(memoria, bloco * 4, bytes, 0, tamanho);
            GuardarDosBytes(c.Args[1], bytes, amb);
        }
        else
        {
            var bytes = BytesDaExpressao(c.Args[1], amb, c.Linha);
            Array.Copy(bytes, 0, memoria, bloco * 4, Math.Min(tamanho, bytes.Length));
        }
        return Valor.DeBool(true);
    }
}
