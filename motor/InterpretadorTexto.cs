using System.Globalization;
using System.Text;

namespace Simulador;

/// <summary>
/// Texto do jeito C: o tipo <c>char</c>, as conversoes <c>(char)x</c>, os
/// vetores de char usados como texto e as funcoes de texto da biblioteca
/// padrao (<c>snprintf</c>, <c>strcmp</c>...).
///
/// Tudo aqui existe porque os exemplos de rede usam isso o tempo todo:
///
///   for (int i = 0; i &lt; length; i++) msg += (char)payload[i];
///   char buffer[50];  snprintf(buffer, 50, "temp: %.1f", t);
///   char c = client.read();
///
/// Sem char de verdade, "msg += (char)payload[i]" escrevia "104101108108111"
/// no lugar de "hello": o motor guardava o codigo do caractere e somava
/// como numero.
/// </summary>
public partial class Interpretador
{
    static bool EhCaractere(string tipo) => tipo is "char" or "signed char" or "KeypadEvent";

    /// <summary>
    /// Os metodos de String que, no Arduino, MUDAM o proprio texto em vez
    /// de devolver um novo. La eles sao void; aqui devolvem o texto novo, e
    /// quem chamou guarda de volta na variavel.
    /// </summary>
    static readonly HashSet<string> MetodosQueMudamOTexto =
        ["trim", "toUpperCase", "toLowerCase", "replace", "remove", "setCharAt", "concat",
         // os do std::string
         "append", "push_back", "pop_back", "clear", "erase", "insert"];

    /// <summary>Vetor que vale como texto: char, byte e companhia.</summary>
    static bool EhDeBytes(string tipo)
        => tipo.TrimEnd('*') is "char" or "signed char" or "unsigned char" or "byte" or "uint8_t";

    /// <summary>
    /// Constantes das bibliotecas de texto e de rede. Os valores sao os
    /// mesmos das bibliotecas do Arduino: um sketch que imprime
    /// <c>client.state()</c> mostra o mesmo numero que mostraria na placa.
    /// </summary>
    static readonly Dictionary<string, Valor> ConstantesDeTextoERede = new()
    {
        ["HEX"] = Valor.DeInt(16),
        ["DEC"] = Valor.DeInt(10),
        ["BIN"] = Valor.DeInt(2),
        ["OCT"] = Valor.DeInt(8),
        ["NULL"] = Valor.DeInt(0),
        ["nullptr"] = Valor.DeInt(0),

        // WiFi.status()
        ["WL_IDLE_STATUS"] = Valor.DeInt(0),
        ["WL_NO_SSID_AVAIL"] = Valor.DeInt(1),
        ["WL_SCAN_COMPLETED"] = Valor.DeInt(2),
        ["WL_CONNECTED"] = Valor.DeInt(3),
        ["WL_CONNECT_FAILED"] = Valor.DeInt(4),
        ["WL_CONNECTION_LOST"] = Valor.DeInt(5),
        ["WL_DISCONNECTED"] = Valor.DeInt(6),
        ["WL_NO_SHIELD"] = Valor.DeInt(255),
        ["WIFI_OFF"] = Valor.DeInt(0),
        ["WIFI_STA"] = Valor.DeInt(1),
        ["WIFI_AP"] = Valor.DeInt(2),
        ["WIFI_AP_STA"] = Valor.DeInt(3),
        // A seguranca de uma rede da varredura. A ESP32 usa o wifi_auth_mode_t
        // do ESP-IDF; o ESP8266, os ENC_TYPE_ do SDK dele -- numeros
        // diferentes para a mesma coisa, e por isso o encryptionType()
        // devolve um ou outro conforme a placa.
        ["WIFI_AUTH_OPEN"] = Valor.DeInt(0),
        ["WIFI_AUTH_WEP"] = Valor.DeInt(1),
        ["WIFI_AUTH_WPA_PSK"] = Valor.DeInt(2),
        ["WIFI_AUTH_WPA2_PSK"] = Valor.DeInt(3),
        ["WIFI_AUTH_WPA_WPA2_PSK"] = Valor.DeInt(4),
        ["WIFI_AUTH_WPA2_ENTERPRISE"] = Valor.DeInt(5),
        ["WIFI_AUTH_WPA3_PSK"] = Valor.DeInt(6),
        ["WIFI_AUTH_WPA2_WPA3_PSK"] = Valor.DeInt(7),
        ["ENC_TYPE_TKIP"] = Valor.DeInt(2),
        ["ENC_TYPE_CCMP"] = Valor.DeInt(4),
        ["ENC_TYPE_WEP"] = Valor.DeInt(5),
        ["ENC_TYPE_NONE"] = Valor.DeInt(7),
        ["ENC_TYPE_AUTO"] = Valor.DeInt(8),

        // WebServer e HTTPClient
        ["HTTP_DELETE"] = Valor.DeInt(0),
        ["HTTP_GET"] = Valor.DeInt(1),
        ["HTTP_HEAD"] = Valor.DeInt(2),
        ["HTTP_POST"] = Valor.DeInt(3),
        ["HTTP_PUT"] = Valor.DeInt(4),
        ["HTTP_OPTIONS"] = Valor.DeInt(6),
        ["HTTP_PATCH"] = Valor.DeInt(28),
        ["HTTP_ANY"] = Valor.DeInt(127),
        ["HTTP_CODE_OK"] = Valor.DeInt(200),
        ["HTTP_CODE_CREATED"] = Valor.DeInt(201),
        ["HTTP_CODE_NO_CONTENT"] = Valor.DeInt(204),
        ["HTTP_CODE_MOVED_PERMANENTLY"] = Valor.DeInt(301),
        ["HTTP_CODE_FOUND"] = Valor.DeInt(302),
        ["HTTP_CODE_BAD_REQUEST"] = Valor.DeInt(400),
        ["HTTP_CODE_UNAUTHORIZED"] = Valor.DeInt(401),
        ["HTTP_CODE_FORBIDDEN"] = Valor.DeInt(403),
        ["HTTP_CODE_NOT_FOUND"] = Valor.DeInt(404),
        ["HTTP_CODE_INTERNAL_SERVER_ERROR"] = Valor.DeInt(500),
        ["HTTPC_ERROR_CONNECTION_REFUSED"] = Valor.DeInt(-1),
        ["HTTPC_ERROR_SEND_HEADER_FAILED"] = Valor.DeInt(-2),
        ["HTTPC_ERROR_SEND_PAYLOAD_FAILED"] = Valor.DeInt(-3),
        ["HTTPC_ERROR_NOT_CONNECTED"] = Valor.DeInt(-4),
        ["HTTPC_ERROR_CONNECTION_LOST"] = Valor.DeInt(-5),
        ["HTTPC_ERROR_NO_HTTP_SERVER"] = Valor.DeInt(-7),
        ["HTTPC_ERROR_READ_TIMEOUT"] = Valor.DeInt(-11),
        ["CONTENT_LENGTH_UNKNOWN"] = Valor.DeInt(-1),

        // PubSubClient.state()
        ["MQTT_CONNECTION_TIMEOUT"] = Valor.DeInt(-4),
        ["MQTT_CONNECTION_LOST"] = Valor.DeInt(-3),
        ["MQTT_CONNECT_FAILED"] = Valor.DeInt(-2),
        ["MQTT_DISCONNECTED"] = Valor.DeInt(-1),
        ["MQTT_CONNECTED"] = Valor.DeInt(0),
        ["MQTT_CONNECT_BAD_PROTOCOL"] = Valor.DeInt(1),
        ["MQTT_CONNECT_BAD_CLIENT_ID"] = Valor.DeInt(2),
        ["MQTT_CONNECT_UNAVAILABLE"] = Valor.DeInt(3),
        ["MQTT_CONNECT_BAD_CREDENTIALS"] = Valor.DeInt(4),
        ["MQTT_CONNECT_UNAUTHORIZED"] = Valor.DeInt(5),
    };

    /// <summary>
    /// Nomes que nao sao variaveis declaradas, mas valem alguma coisa: as
    /// constantes de biblioteca, o proprio <c>Serial</c>, um vetor de char
    /// usado como texto e um objeto usado como valor ("if (client)").
    /// </summary>
    bool ValorDeNomeEspecial(Variavel v, Ambiente amb, out Valor valor)
    {
        if (ConstantesDeTextoERede.TryGetValue(v.Nome, out valor)) return true;
        if (ConstantesDeRadio.TryGetValue(v.Nome, out valor)) return true;
        if (ConstanteDoSono(v.Nome, out valor)) return true;

        switch (v.Nome)
        {
            // "while (!Serial) {}": na placa espera a USB abrir; aqui ja abriu.
            case "Serial":
                valor = Valor.DeBool(true);
                return true;
            case "BUILTIN_LED":
                valor = Valor.DeInt(Maq.Perfil.LedInterno);
                return true;
        }

        if (amb.BuscarArray(v.Nome, out var vetor))
        {
            if (vetor.Length == 0 || vetor[0].Caractere || amb.EhVetorDeBytes(v.Nome))
            {
                valor = Valor.DeTexto(TextoDoVetor(vetor));
                return true;
            }
            throw new ErroDeExecucao(
                $"o vetor '{v.Nome}' foi usado inteiro onde se espera um valor. "
                + $"Use um elemento, como {v.Nome}[0], ou passe o vetor para uma funcao sua.", v.Linha);
        }

        if (amb.BuscarObjeto(v.Nome, out var objeto))
        {
            valor = ValorDoObjeto(objeto);
            return true;
        }

        valor = Valor.Vazio;
        return false;
    }

    /// <summary>O texto de um vetor de char, ate o zero do fim, como em C.</summary>
    static string TextoDoVetor(Valor[] vetor)
    {
        var bytes = new List<byte>();
        foreach (var item in vetor)
        {
            long codigo = (long)item.Num;
            if (codigo == 0) break;
            bytes.Add((byte)(codigo & 0xFF));
        }
        return TextoArduino.Corrigir(TextoArduino.DeBytes([.. bytes]));
    }

    /// <summary>
    /// Escreve texto num vetor de char, com o zero do fim. Estourar o
    /// vetor aqui CORTA o texto; em C invadiria a memoria do lado, que e
    /// justamente o que ninguem quer explicar numa aula de rede.
    /// </summary>
    static void EscreverNoVetor(Valor[] vetor, string texto, int limite = int.MaxValue)
    {
        int maximo = Math.Min(vetor.Length, limite);
        if (maximo <= 0) return;

        var bytes = TextoArduino.ParaBytes(texto);
        int quantos = Math.Min(bytes.Length, maximo - 1);
        for (int i = 0; i < quantos; i++) vetor[i] = Valor.DeCaractere(bytes[i]);
        vetor[quantos] = Valor.DeCaractere(0);
    }

    /// <summary>A conversao "(tipo)valor".</summary>
    Valor Converter(Conversao cv, Ambiente amb)
    {
        // "(char*)payload": o vetor lido como texto. Sem ponteiro de
        // verdade no motor, e exatamente o que o sketch quer dali.
        if (cv.Tipo.EndsWith('*'))
        {
            if (cv.Alvo is Variavel nome && amb.BuscarArray(nome.Nome, out var vetor))
                return Valor.DeTexto(TextoDoVetor(vetor));

            // "(int*)malloc(40)": o bloco de bytes vira 10 int.
            return TiparBloco(Avaliar(cv.Alvo, amb), cv.Tipo);
        }

        // "(int)&v" no AVR: o endereco da variavel (o freeRam() dos tutoriais).
        if (EnderecoNoAvr(cv.Alvo, amb, out var endereco)) return Coagir(endereco, cv.Tipo);

        var x = Avaliar(cv.Alvo, amb);
        CobrarConversao(x, cv.Tipo);

        // "(Base*)obj", "static_cast<Sensor&>(s)": o objeto continua o mesmo.
        if (x.Referencia is Objeto or Valor[]) return x;

        double n = x.EhTexto
            ? (double.TryParse(x.Texto, NumberStyles.Float, CultureInfo.InvariantCulture, out double lido) ? lido : 0)
            : x.Num;

        return cv.Tipo switch
        {
            "char" or "signed char" => Valor.ComoCaractere(x),
            "float" or "double" or "long double" => Valor.DeReal(n),
            "bool" or "boolean" => Valor.DeBool(x.Verdadeiro),
            "String" => Valor.DeTexto(x.Mostrar()),
            "byte" or "uint8_t" or "unsigned char" => Valor.DeInt((long)n & 0xFF),
            "int8_t" => Valor.DeInt((sbyte)(long)n),
            "uint16_t" or "unsigned short" or "word" => Valor.DeInt((long)n & 0xFFFF),
            "int16_t" or "short" => Valor.DeInt((short)(long)n),
            "uint32_t" or "unsigned long" or "unsigned int" or "unsigned" => Valor.DeInt((long)n & 0xFFFFFFFF),
            _ => Valor.DeInt(Math.Truncate(n)),
        };
    }

    /// <summary>
    /// O texto de um <c>print()</c>. Com dois argumentos, o segundo e a
    /// base (HEX, BIN) para inteiro, ou as casas decimais para real --
    /// como no Arduino.
    /// </summary>
    static string Imprimivel(List<Valor> args)
    {
        if (args.Count == 0) return "";
        var v = args[0];
        if (args.Count < 2 || v.EhTexto) return v.Mostrar();

        int b = (int)args[1].Num;
        if (!v.Inteiro) return TextoArduino.Real(v.Num, b, v.Tipo == TipoC.Float);

        long n = (long)v.Num;
        if (n < 0 && b != 10) n &= 0xFFFFFFFF;          // como o int de 32 bits da placa
        return b switch
        {
            16 => Convert.ToString(n, 16).ToUpperInvariant(),
            8 => Convert.ToString(n, 8),
            2 => Convert.ToString(n, 2),
            _ => n.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// <c>write()</c>: numero e um BYTE, nao um numero escrito. E o que
    /// faz "Serial.write(SerialBT.read())" repetir a letra em vez de
    /// imprimir "104".
    /// </summary>
    static string ComoEscrita(List<Valor> args)
    {
        if (args.Count == 0) return "";
        var v = args[0];
        if (v.EhTexto)
        {
            // write(buffer, tamanho)
            int limite = args.Count > 1 ? (int)args[1].Num : v.Texto.Length;
            return v.Texto[..Math.Clamp(limite, 0, v.Texto.Length)];
        }
        long codigo = (long)v.Num & 0xFF;
        return codigo == 0 ? "" : ((char)codigo).ToString();
    }

    static string Formatar(List<Valor> args)
        => args.Count == 0 ? "" : Formatar(args[0].Mostrar(), args.Skip(1).ToList());

    /// <summary>
    /// O <c>printf</c> do C: o bastante para os sketches de aula --
    /// <c>%d %i %u %ld %x %X %o %c %s %f %.2f %5d %-8s %%</c>.
    /// </summary>
    static string Formatar(string formato, List<Valor> args)
    {
        var saida = new StringBuilder();
        int proximo = 0;
        Valor Pegar() => proximo < args.Count ? args[proximo++] : Valor.DeInt(0);

        for (int i = 0; i < formato.Length; i++)
        {
            if (formato[i] != '%') { saida.Append(formato[i]); continue; }
            if (i + 1 < formato.Length && formato[i + 1] == '%') { saida.Append('%'); i++; continue; }

            int j = i + 1;
            bool esquerda = false, zeros = false, mais = false, espaco = false;
            for (; j < formato.Length && "-0+ #".Contains(formato[j]); j++)
            {
                if (formato[j] == '-') esquerda = true;
                else if (formato[j] == '0') zeros = true;
                else if (formato[j] == '+') mais = true;
                else if (formato[j] == ' ') espaco = true;
            }

            int largura = 0;
            if (j < formato.Length && formato[j] == '*') { largura = (int)Pegar().Num; j++; }
            else while (j < formato.Length && char.IsAsciiDigit(formato[j])) largura = largura * 10 + (formato[j++] - '0');

            int precisao = -1;
            if (j < formato.Length && formato[j] == '.')
            {
                j++;
                precisao = 0;
                if (j < formato.Length && formato[j] == '*') { precisao = (int)Pegar().Num; j++; }
                else while (j < formato.Length && char.IsAsciiDigit(formato[j])) precisao = precisao * 10 + (formato[j++] - '0');
            }

            while (j < formato.Length && "hlLzjt".Contains(formato[j])) j++;
            if (j >= formato.Length) { saida.Append(formato[i..]); break; }

            char conversao = formato[j];
            i = j;

            string sinal = "", corpo;
            switch (conversao)
            {
                case 'd':
                case 'i':
                {
                    long n = (long)Pegar().Num;
                    sinal = n < 0 ? "-" : mais ? "+" : espaco ? " " : "";
                    corpo = Math.Abs(n).ToString(CultureInfo.InvariantCulture);
                    if (precisao >= 0) corpo = corpo.PadLeft(precisao, '0');
                    break;
                }
                case 'u':
                {
                    long n = (long)Pegar().Num;
                    corpo = (n < 0 ? (ulong)(uint)n : (ulong)n).ToString(CultureInfo.InvariantCulture);
                    break;
                }
                case 'x':
                case 'X':
                case 'o':
                case 'p':
                {
                    long n = (long)Pegar().Num;
                    if (n < 0) n &= 0xFFFFFFFF;
                    corpo = Convert.ToString(n, conversao == 'o' ? 8 : 16);
                    if (conversao == 'X') corpo = corpo.ToUpperInvariant();
                    if (precisao >= 0) corpo = corpo.PadLeft(precisao, '0');
                    break;
                }
                case 'c':
                {
                    var v = Pegar();
                    corpo = v.EhTexto
                        ? (v.Texto.Length > 0 ? v.Texto[..1] : "")
                        : ((char)((long)v.Num & 0xFF)).ToString();
                    break;
                }
                case 's':
                {
                    corpo = Pegar().Mostrar();
                    if (precisao >= 0 && corpo.Length > precisao) corpo = corpo[..precisao];
                    break;
                }
                case 'f':
                case 'F':
                {
                    double n = Pegar().Num;
                    sinal = n < 0 ? "-" : mais ? "+" : espaco ? " " : "";
                    corpo = Math.Abs(n).ToString("F" + (precisao < 0 ? 6 : Math.Clamp(precisao, 0, 15)),
                                                CultureInfo.InvariantCulture);
                    break;
                }
                case 'e':
                case 'E':
                case 'g':
                case 'G':
                {
                    double n = Pegar().Num;
                    int casas = precisao < 0 ? 6 : Math.Clamp(precisao, 0, 15);
                    string molde = casas == 0 ? "0e+00" : "0." + new string('0', casas) + "e+00";
                    corpo = n.ToString(molde, CultureInfo.InvariantCulture);
                    if (char.IsUpper(conversao)) corpo = corpo.ToUpperInvariant();
                    break;
                }
                default:
                    // Conversao que nao conhecemos: sai como estava, e o
                    // argumento fica para a proxima.
                    saida.Append('%').Append(conversao);
                    continue;
            }

            if (zeros && !esquerda && precisao < 0 && corpo.Length + sinal.Length < largura)
                corpo = corpo.PadLeft(largura - sinal.Length, '0');

            string texto = sinal + corpo;
            saida.Append(esquerda ? texto.PadRight(largura) : texto.PadLeft(largura));
        }
        return saida.ToString();
    }

    /// <summary>
    /// O inteiro do comeco do texto, do jeito do strtol do C: pula os
    /// espacos, aceita o sinal, e na base 16 (ou 0) o "0x" na frente. Para
    /// no primeiro caractere que nao e digito daquela base.
    /// </summary>
    static long InteiroDoC(string texto, int baseDoNumero)
    {
        int i = 0;
        texto ??= "";
        while (i < texto.Length && char.IsWhiteSpace(texto[i])) i++;

        bool negativo = false;
        if (i < texto.Length && texto[i] is '+' or '-') negativo = texto[i++] == '-';

        bool temPrefixo = i + 1 < texto.Length && texto[i] == '0' && texto[i + 1] is 'x' or 'X';
        if (baseDoNumero == 0) baseDoNumero = temPrefixo ? 16 : i < texto.Length && texto[i] == '0' ? 8 : 10;
        if (baseDoNumero == 16 && temPrefixo) i += 2;
        if (baseDoNumero is < 2 or > 36) return 0;

        long valor = 0;
        for (; i < texto.Length; i++)
        {
            int digito = char.IsAsciiDigit(texto[i]) ? texto[i] - '0'
                       : char.IsAsciiLetter(texto[i]) ? char.ToLowerInvariant(texto[i]) - 'a' + 10
                       : 99;
            if (digito >= baseDoNumero) break;
            valor = unchecked(valor * baseDoNumero + digito);
        }

        return negativo ? -valor : valor;
    }

    /// <summary>O numero real do comeco do texto, como o atof e o strtod do C.</summary>
    static double RealDoC(string texto)
    {
        var achado = System.Text.RegularExpressions.Regex.Match(
            texto ?? "", @"^\s*[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?");
        return achado.Success && double.TryParse(achado.Value.Trim(), NumberStyles.Float,
                                                  CultureInfo.InvariantCulture, out double d) ? d : 0;
    }

    /// <summary>
    /// As funcoes de texto do C e o construtor String(). Sao atendidas
    /// ANTES da avaliacao dos argumentos porque a primeira coisa que
    /// recebem costuma ser o VETOR de destino: "snprintf(msg, 50, ...)".
    /// Avaliado como valor, esse vetor viraria texto e a escrita se perdia.
    /// </summary>
    List<(Objeto Dono, string Campo, Valor[] Vetor)> camposDeDestino;

    bool FuncaoDeTexto(string nome, Chamada c, Ambiente amb, out Valor resultado)
    {
        var anteriores = camposDeDestino;
        var meus = new List<(Objeto, string, Valor[])>();
        camposDeDestino = meus;

        try { return FuncaoDeTextoInterna(nome, c, amb, out resultado); }
        finally
        {
            camposDeDestino = anteriores;
            foreach (var (dono, campo, vetor) in meus)
                dono.Campos[campo] = Valor.DeTexto(TextoDoVetor(vetor));
        }
    }

    bool FuncaoDeTextoInterna(string nome, Chamada c, Ambiente amb, out Valor resultado)
    {
        resultado = Valor.Vazio;
        List<Valor> Args() => c.Args.Select(a => Avaliar(a, amb)).ToList();
        static string S(List<Valor> args, int i) => i < args.Count ? args[i].Mostrar() : "";
        static int N(List<Valor> args, int i) => i < args.Count ? (int)args[i].Num : 0;

        switch (nome)
        {
            case "String":
            {
                var args = Args();
                resultado = args.Count == 0 ? Valor.DeTexto("") : Valor.DeTexto(Imprimivel(args));
                return true;
            }

            // F("texto") guarda o literal na flash; aqui e o proprio texto.
            case "F":
            case "PSTR":
                resultado = c.Args.Count > 0 ? Avaliar(c.Args[0], amb) : Valor.DeTexto("");
                return true;

            case "sprintf":
            case "snprintf":
            {
                if (c.Args.Count < 2) return false;
                var destino = VetorDeDestino(c.Args[0], amb, nome);
                bool comLimite = nome == "snprintf";
                int limite = comLimite ? (int)Avaliar(c.Args[1], amb).Num : int.MaxValue;
                int iFormato = comLimite ? 2 : 1;
                if (iFormato >= c.Args.Count) return false;

                string formato = Avaliar(c.Args[iFormato], amb).Mostrar();
                var valores = c.Args.Skip(iFormato + 1).Select(a => Avaliar(a, amb)).ToList();

                string texto = Formatar(formato, valores);
                EscreverNoVetor(destino, texto, limite);
                resultado = Valor.DeInt(TextoArduino.ParaBytes(texto).Length);
                return true;
            }

            case "strcpy":
            case "strncpy":
            {
                if (c.Args.Count < 2) return false;
                var destino = VetorDeDestino(c.Args[0], amb, nome);
                string texto = Avaliar(c.Args[1], amb).Mostrar();
                if (nome == "strncpy" && c.Args.Count > 2)
                {
                    int quantos = Math.Max((int)Avaliar(c.Args[2], amb).Num, 0);
                    if (texto.Length > quantos) texto = texto[..quantos];
                }
                EscreverNoVetor(destino, texto);
                resultado = Valor.DeTexto(texto);
                return true;
            }

            case "strcat":
            case "strncat":
            {
                if (c.Args.Count < 2) return false;
                var destino = VetorDeDestino(c.Args[0], amb, nome);
                string juntar = Avaliar(c.Args[1], amb).Mostrar();
                if (nome == "strncat" && c.Args.Count > 2)
                {
                    int quantos = Math.Max((int)Avaliar(c.Args[2], amb).Num, 0);
                    if (juntar.Length > quantos) juntar = juntar[..quantos];
                }
                string texto = TextoDoVetor(destino) + juntar;
                EscreverNoVetor(destino, texto);
                resultado = Valor.DeTexto(texto);
                return true;
            }

            case "dtostrf":
            {
                if (c.Args.Count < 4) return false;
                var args = new List<Valor>();
                for (int i = 0; i < 3; i++) args.Add(Avaliar(c.Args[i], amb));
                var destino = VetorDeDestino(c.Args[3], amb, nome);

                int largura = N(args, 1), casas = N(args, 2);
                string texto = args[0].Num.ToString("F" + Math.Clamp(casas, 0, 10), CultureInfo.InvariantCulture);
                texto = largura < 0 ? texto.PadRight(-largura) : texto.PadLeft(largura);
                EscreverNoVetor(destino, texto);
                resultado = Valor.DeTexto(texto);
                return true;
            }

            case "itoa":
            case "ltoa":
            case "utoa":
            {
                if (c.Args.Count < 2) return false;
                long valor = (long)Avaliar(c.Args[0], amb).Num;
                var destino = VetorDeDestino(c.Args[1], amb, nome);
                int baseNum = c.Args.Count > 2 ? (int)Avaliar(c.Args[2], amb).Num : 10;
                string texto = baseNum == 10
                    ? valor.ToString(CultureInfo.InvariantCulture)
                    : Convert.ToString(valor < 0 ? valor & 0xFFFFFFFF : valor, baseNum);
                EscreverNoVetor(destino, texto);
                resultado = Valor.DeTexto(texto);
                return true;
            }

            case "memset":
            {
                if (c.Args.Count < 3) return false;
                var destino = VetorDeDestino(c.Args[0], amb, nome);
                long valor = (long)Avaliar(c.Args[1], amb).Num;
                int quantos = Math.Clamp((int)Avaliar(c.Args[2], amb).Num, 0, destino.Length);
                for (int i = 0; i < quantos; i++) destino[i] = Valor.DeCaractere(valor);
                resultado = Valor.DeInt(quantos);
                return true;
            }

            case "strlen":
                resultado = Valor.DeInt(TextoArduino.ParaBytes(S(Args(), 0)).Length);
                return true;

            case "strcmp":
            case "strncmp":
            case "strcasecmp":
            {
                var args = Args();
                string a = S(args, 0), b = S(args, 1);
                if (nome == "strncmp" && args.Count > 2)
                {
                    int quantos = Math.Max(N(args, 2), 0);
                    a = a[..Math.Min(a.Length, quantos)];
                    b = b[..Math.Min(b.Length, quantos)];
                }
                int comparacao = nome == "strcasecmp"
                    ? string.Compare(a, b, StringComparison.OrdinalIgnoreCase)
                    : string.CompareOrdinal(a, b);
                resultado = Valor.DeInt(Math.Sign(comparacao));
                return true;
            }

            case "strstr":
            {
                var args = Args();
                string onde = S(args, 0);
                int achou = onde.IndexOf(S(args, 1), StringComparison.Ordinal);
                // Devolve um "ponteiro": o resto do texto, ou 0 (NULL).
                resultado = achou < 0 ? Valor.DeInt(0) : Valor.DeTexto(onde[achou..]);
                return true;
            }

            // Como no C: le o numero do COMECO do texto e para no primeiro
            // caractere que nao serve -- atoi("12abc") e 12, e nao 0.
            case "atoi":
            case "atol":
            case "atoll":
                resultado = Valor.DeInt(InteiroDoC(S(Args(), 0), 10));
                return true;

            // strtol(texto, fim, base): a base 16 aceita o "0x" na frente, e
            // a base 0 descobre sozinha ("0x1F", "017", "42"). O ponteiro do
            // fim nao e preenchido -- quase todo sketch passa NULL.
            case "strtol":
            case "strtoul":
            case "strtoll":
            case "strtoull":
            {
                var args = Args();
                resultado = Valor.DeInt(InteiroDoC(S(args, 0), args.Count > 2 ? N(args, 2) : 10));
                return true;
            }

            case "atof":
            case "strtod":
            case "strtof":
                resultado = Valor.DeReal(RealDoC(S(Args(), 0)));
                return true;

            case "isDigit" or "isdigit" or "isAlpha" or "isalpha" or "isAlphaNumeric" or "isalnum"
                or "isSpace" or "isspace" or "isWhitespace" or "isUpperCase" or "isupper"
                or "isLowerCase" or "islower" or "isHexadecimalDigit" or "isxdigit"
                or "isPunct" or "ispunct" or "toupper" or "tolower":
            {
                var args = Args();
                char ch = (char)(args.Count > 0 ? CodigoDe(args[0]) : 0);
                resultado = nome switch
                {
                    "isDigit" or "isdigit" => Valor.DeBool(char.IsAsciiDigit(ch)),
                    "isAlpha" or "isalpha" => Valor.DeBool(char.IsAsciiLetter(ch)),
                    "isAlphaNumeric" or "isalnum" => Valor.DeBool(char.IsAsciiLetterOrDigit(ch)),
                    "isSpace" or "isspace" or "isWhitespace" => Valor.DeBool(char.IsWhiteSpace(ch)),
                    "isUpperCase" or "isupper" => Valor.DeBool(char.IsAsciiLetterUpper(ch)),
                    "isLowerCase" or "islower" => Valor.DeBool(char.IsAsciiLetterLower(ch)),
                    "isHexadecimalDigit" or "isxdigit" => Valor.DeBool(char.IsAsciiHexDigit(ch)),
                    "isPunct" or "ispunct" => Valor.DeBool(char.IsPunctuation(ch) || char.IsSymbol(ch)),
                    "toupper" => Valor.DeCaractere(char.ToUpperInvariant(ch)),
                    _ => Valor.DeCaractere(char.ToLowerInvariant(ch)),
                };
                return true;
            }
        }
        return false;
    }

    static int CodigoDe(Valor v)
        => v.EhTexto ? (v.Texto.Length > 0 ? v.Texto[0] : 0) : (int)v.Num;

    Valor[] VetorDeDestino(Expr e, Ambiente amb, string funcao)
    {
        if (SemEndereco(e) is Variavel v && amb.BuscarArray(v.Nome, out var vetor)) return vetor;

        if (SemEndereco(e) is Membro m && m.Obj is Variavel dono
            && amb.BuscarObjeto(dono.Nome, out var estrutura)
            && estrutura.Campos.TryGetValue(m.Nome, out var agora))
        {
            string texto = agora.Mostrar();
            var campo = new Valor[Math.Max(64, texto.Length + 1)];
            EscreverNoVetor(campo, texto);
            camposDeDestino?.Add((estrutura, m.Nome, campo));
            return campo;
        }

        throw new ErroDeExecucao(
            $"{funcao}() precisa de um vetor de char como destino, "
            + $"como em: char msg[50]; {funcao}(msg, ...);", e.Linha);
    }

    /// <summary>
    /// Os metodos de leitura do Stream do Arduino. Sao os mesmos para o
    /// Serial, o SerialBT e o client -- por isso moram num lugar so.
    /// </summary>
    static bool ChamarFluxo(FluxoDeEntrada fluxo, string metodo, List<Valor> args, out Valor resultado)
    {
        resultado = Valor.Vazio;
        switch (metodo)
        {
            case "available": resultado = Valor.DeInt(fluxo.Disponivel); return true;
            case "read": resultado = Valor.DeInt(fluxo.Ler()); return true;
            case "peek": resultado = Valor.DeInt(fluxo.Espiar()); return true;
            case "readString": resultado = Valor.DeTexto(fluxo.LerTudo()); return true;
            case "readStringUntil":
                resultado = Valor.DeTexto(fluxo.LerAte(args.Count > 0 ? CodigoDe(args[0]) : '\n'));
                return true;
            case "parseInt": resultado = Valor.DeInt(fluxo.LerInteiro()); return true;
            case "parseFloat": resultado = Valor.DeReal(fluxo.LerReal()); return true;
            case "find": resultado = Valor.DeBool(fluxo.Procurar(args.Count > 0 ? args[0].Mostrar() : "")); return true;
            case "setTimeout": case "flush": return true;
        }
        return false;
    }

    // =====================================================================
    //  Funcoes guardadas para depois (callbacks)
    // =====================================================================

    /// <summary>
    /// Uma funcao que a biblioteca guarda para chamar depois: a do
    /// <c>server.on("/", trataRaiz)</c> ou do <c>client.setCallback(...)</c>.
    /// Pode ser uma funcao do sketch ou uma funcao anonima.
    /// </summary>
    sealed class Rotina
    {
        public Funcao Funcao;
        public FuncaoAnonima Anonima;
        public Ambiente Captura;
    }

    Rotina RotinaDe(Expr e, Ambiente amb, string ondeUsou)
    {
        switch (SemEndereco(e))
        {
            case FuncaoAnonima anonima:
                return new Rotina { Anonima = anonima, Captura = amb };

            case Variavel v when funcoes.TryGetValue(v.Nome, out var f):
                return new Rotina { Funcao = f, Captura = Global };

            case Variavel v:
                throw new ErroDeExecucao(
                    $"{ondeUsou} aponta para '{v.Nome}()', que nao existe neste sketch.", e.Linha);

            default:
                throw new ErroDeExecucao(
                    $"{ondeUsou} espera o nome de uma funcao do sketch, "
                    + "ou uma funcao anonima: []() { ... }", e.Linha);
        }
    }

    /// <summary>
    /// Chama a rotina. Um argumento Valor vira variavel; um Valor[] vira
    /// vetor -- e como o "byte* payload" do callback do MQTT chega inteiro.
    /// </summary>
    Valor ChamarRotina(Rotina r, params object[] args)
    {
        var parametros = r.Funcao?.Params ?? r.Anonima.Params;
        var corpo = r.Funcao?.Corpo ?? r.Anonima.Corpo;
        var local = new Ambiente(r.Funcao is not null ? Global : r.Captura);

        for (int i = 0; i < parametros.Count; i++)
        {
            var p = parametros[i];
            object argumento = i < args.Length ? args[i] : null;
            switch (argumento)
            {
                case Valor[] vetor:
                    local.DeclararArray(p.Nome, vetor);
                    local.MarcarVetorDeBytes(p.Nome);
                    break;
                case Valor v when EhCaractere(p.Tipo):
                    local.DeclararCaractere(p.Nome, v);
                    break;
                case Valor v:
                    local.Declarar(p.Nome, Coagir(v, p.Tipo));
                    break;
                // O argumento que nao veio: o padrao ("lambda a, b=10: a + b"), ou zero.
                default:
                    local.Declarar(p.Nome, p.Padrao is not null ? Avaliar(p.Padrao, local) : Valor.DeInt(0));
                    break;
            }
        }

        try { return ExecCorpoDeFuncao(corpo, local, out var saiu) ? saiu : Valor.Vazio; }
        finally { FecharEscopo(local); }
    }

    /// <summary>
    /// Chamada de funcao do sketch com um VETOR entre os argumentos. O
    /// caminho normal avalia cada argumento como valor, e o vetor se
    /// perderia; aqui ele entra por referencia, como o ponteiro do C.
    /// </summary>
    bool ChamarFuncaoComVetores(string nome, Chamada c, Ambiente amb, out Valor resultado)
    {
        resultado = Valor.Vazio;
        if (!funcoes.TryGetValue(nome, out var f)) return false;

        bool Vetor(Expr e, out Valor[] vetor)
        {
            vetor = null;
            return SemEndereco(e) is Variavel v && !amb.Buscar(v.Nome, out _)
                   && amb.BuscarArray(v.Nome, out vetor);
        }

        if (!c.Args.Any(a => Vetor(a, out _))) return false;

        var args = new object[c.Args.Count];
        for (int i = 0; i < c.Args.Count; i++)
            args[i] = Vetor(c.Args[i], out var vetor) ? vetor : Avaliar(c.Args[i], amb);

        resultado = ChamarRotina(new Rotina { Funcao = f }, args);
        return true;
    }
}
