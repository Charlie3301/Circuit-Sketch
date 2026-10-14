using System.Text;

namespace Simulador;

/// <summary>
/// Bytes que chegam de fora para o sketch, na ordem em que chegaram.
///
/// É o lado de leitura do <c>Stream</c> do Arduino, e o mesmo para os
/// três lugares que entregam dados à placa: o monitor serial, o
/// Bluetooth e uma conexão de rede. Por isso <c>Serial.read()</c>,
/// <c>SerialBT.read()</c> e <c>client.read()</c> se comportam igual.
///
/// Guarda BYTES, e não caracteres, como a placa: um "ç" digitado no
/// monitor chega como dois <c>read()</c>, do mesmo jeito que chegaria
/// pela USB.
/// </summary>
public sealed class FluxoDeEntrada
{
    readonly Queue<byte> bytes = new();
    readonly object trava = new();

    /// <summary>Teto de memória: um sketch que nunca lê não pode crescer sem fim.</summary>
    const int Teto = 1 << 20;

    public void Adicionar(string texto) => Adicionar(Encoding.UTF8.GetBytes(texto ?? ""));

    public void Adicionar(byte[] dados)
    {
        lock (trava)
            foreach (byte b in dados)
                if (bytes.Count < Teto) bytes.Enqueue(b);
    }

    public int Disponivel { get { lock (trava) return bytes.Count; } }

    public int Ler() { lock (trava) return bytes.Count > 0 ? bytes.Dequeue() : -1; }

    public int Espiar() { lock (trava) return bytes.Count > 0 ? bytes.Peek() : -1; }

    public void Limpar() { lock (trava) bytes.Clear(); }

    /// <summary>O que esta no buffer, sem tirar nada.</summary>
    public byte[] Copia() { lock (trava) return [.. bytes]; }

    /// <summary>O byte ja esta no buffer? (o readStringUntil espera por ele)</summary>
    public bool Contem(int b) { lock (trava) return bytes.Contains((byte)b); }

    /// <summary>A sequencia ja esta no buffer? (o find() espera por ela)</summary>
    public bool ContemTexto(string alvo)
    {
        var procurado = Encoding.UTF8.GetBytes(alvo ?? "");
        lock (trava)
        {
            var dados = bytes.ToArray();
            for (int i = 0; i + procurado.Length <= dados.Length; i++)
                if (dados.AsSpan(i, procurado.Length).SequenceEqual(procurado)) return true;
            return procurado.Length == 0;
        }
    }

    /// <summary>
    /// <c>readString()</c>. Texto do jeito que foi escrito: UTF-8
    /// decodificado, para <c>s == "ligar"</c> funcionar com acento.
    /// </summary>
    public string LerTudo()
    {
        lock (trava)
        {
            var dados = bytes.ToArray();
            bytes.Clear();
            return Encoding.UTF8.GetString(dados);
        }
    }

    /// <summary><c>readStringUntil('\n')</c>: consome o terminador, não o devolve.</summary>
    public string LerAte(int terminador)
    {
        lock (trava)
        {
            var lidos = new List<byte>();
            while (bytes.Count > 0)
            {
                byte b = bytes.Dequeue();
                if (b == (byte)terminador) break;
                lidos.Add(b);
            }
            return Encoding.UTF8.GetString([.. lidos]);
        }
    }

    /// <summary>
    /// <c>parseInt()</c>: pula o que não é número, lê o número e para no
    /// primeiro caractere que não serve. Sem número nenhum, 0 -- como no
    /// Arduino depois do tempo de espera.
    /// </summary>
    public long LerInteiro()
    {
        lock (trava)
        {
            while (bytes.Count > 0 && !char.IsAsciiDigit((char)bytes.Peek()) && bytes.Peek() != '-')
                bytes.Dequeue();

            bool negativo = false;
            if (bytes.Count > 0 && bytes.Peek() == '-') { negativo = true; bytes.Dequeue(); }

            long n = 0;
            while (bytes.Count > 0 && char.IsAsciiDigit((char)bytes.Peek()))
                n = n * 10 + (bytes.Dequeue() - '0');
            return negativo ? -n : n;
        }
    }

    /// <summary><c>parseFloat()</c>, com a mesma regra do <see cref="LerInteiro"/>.</summary>
    public double LerReal()
    {
        lock (trava)
        {
            while (bytes.Count > 0 && !char.IsAsciiDigit((char)bytes.Peek())
                   && bytes.Peek() != '-' && bytes.Peek() != '.')
                bytes.Dequeue();

            var texto = new StringBuilder();
            while (bytes.Count > 0)
            {
                char c = (char)bytes.Peek();
                bool serve = char.IsAsciiDigit(c) || c == '.' || (c == '-' && texto.Length == 0);
                if (!serve) break;
                texto.Append(c);
                bytes.Dequeue();
            }
            return double.TryParse(texto.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
        }
    }

    /// <summary><c>find("OK")</c>: consome até achar, e diz se achou.</summary>
    public bool Procurar(string alvo)
    {
        var procurado = Encoding.UTF8.GetBytes(alvo ?? "");
        if (procurado.Length == 0) return true;

        lock (trava)
        {
            int casados = 0;
            while (bytes.Count > 0)
            {
                byte b = bytes.Dequeue();
                casados = b == procurado[casados] ? casados + 1 : (b == procurado[0] ? 1 : 0);
                if (casados == procurado.Length) return true;
            }
            return false;
        }
    }
}

/// <summary>
/// A ponte entre o texto do .NET e os bytes da placa.
///
/// O sketch monta texto de dois jeitos: com literal (<c>"ação"</c>, que
/// aqui já é Unicode) e byte a byte (<c>s += (char)c</c>, em que cada
/// byte vira um caractere de 0 a 255). O segundo, impresso como veio,
/// mostraria "aÃ§Ã£o" para um "ação" que chegou pela rede -- que é o
/// que o monitor de verdade NÃO mostra, porque lá os bytes seguem crus.
/// </summary>
public static class TextoArduino
{
    static readonly Encoding Utf8Estrito = new UTF8Encoding(false, true);

    /// <summary>
    /// Os bytes que a placa mandaria. Texto que é, na verdade, uma fileira
    /// de bytes UTF-8 volta a ser esses bytes; o resto vai em UTF-8.
    /// </summary>
    public static byte[] ParaBytes(string texto)
    {
        texto ??= "";
        if (ComoBytesUtf8(texto) is { } crus) return crus;
        return Encoding.UTF8.GetBytes(texto);
    }

    /// <summary>Texto para mostrar: desfaz o "aÃ§Ã£o" quando ele era UTF-8 em bytes.</summary>
    public static string Corrigir(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return texto ?? "";
        return ComoBytesUtf8(texto) is { } crus ? Encoding.UTF8.GetString(crus) : texto;
    }

    /// <summary>Bytes como a placa guardaria num char[]: um caractere por byte.</summary>
    public static string DeBytes(byte[] dados) => Encoding.Latin1.GetString(dados ?? []);

    /// <summary>
    /// O print(double, casas) do Arduino -- o Print::printFloat, passo a
    /// passo: "nan", "inf", "ovf" acima de 4294967040, o arredondamento
    /// somando meio da ultima casa, e as casas tiradas uma a uma. No AVR o
    /// double e float, e a conta inteira e feita em float
    /// (<paramref name="emFloat"/>).
    /// </summary>
    public static string Real(double numero, int casas = 2, bool emFloat = false)
    {
        if (double.IsNaN(numero)) return "nan";
        if (double.IsInfinity(numero)) return "inf";
        if (numero > 4294967040.0 || numero < -4294967040.0) return "ovf";
        casas = Math.Clamp(casas, 0, 30);

        var saida = new StringBuilder();
        if (numero < 0.0)
        {
            saida.Append('-');
            numero = -numero;
        }

        if (emFloat)
        {
            float n = (float)numero, arredondar = 0.5f;
            for (int i = 0; i < casas; i++) arredondar /= 10.0f;
            n += arredondar;
            uint parte = (uint)n;
            float resto = n - parte;
            saida.Append(parte);
            if (casas > 0) saida.Append('.');
            while (casas-- > 0)
            {
                resto *= 10.0f;
                uint digito = (uint)resto;
                saida.Append(digito);
                resto -= digito;
            }
            return saida.ToString();
        }

        double arredonda = 0.5;
        for (int i = 0; i < casas; i++) arredonda /= 10.0;
        numero += arredonda;
        ulong inteira = (ulong)numero;
        double sobra = numero - inteira;
        saida.Append(inteira);
        if (casas > 0) saida.Append('.');
        while (casas-- > 0)
        {
            sobra *= 10.0;
            uint digito = (uint)sobra;
            saida.Append(digito);
            sobra -= digito;
        }
        return saida.ToString();
    }

    static byte[] ComoBytesUtf8(string texto)
    {
        bool algumAlto = false;
        foreach (char c in texto)
        {
            if (c > 0xFF) return null;
            if (c >= 0x80) algumAlto = true;
        }
        if (!algumAlto) return null;

        var latin = Encoding.Latin1.GetBytes(texto);
        try { Utf8Estrito.GetString(latin); return latin; }
        catch (DecoderFallbackException) { return null; }
    }
}
