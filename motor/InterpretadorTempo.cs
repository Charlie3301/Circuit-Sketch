namespace Simulador;

/// <summary>
/// O relogio de rede: <c>configTime</c>, <c>getLocalTime</c> e
/// <c>strftime</c>.
///
/// E o que faltava para os dois sketches de relogio da lista rodarem. O
/// padrao e sempre o mesmo, e vem direto dos exemplos do core da ESP32:
///
/// <code>
///   configTime(-3 * 3600, 0, "pool.ntp.org");
///   struct tm agora;
///   if (getLocalTime(&amp;agora)) lcd.println(&amp;agora, "%H:%M:%S");
/// </code>
///
/// A hora vem do RELOGIO DESTE COMPUTADOR, com o fuso que o sketch
/// pediu. Inventar uma hora seria pior do que inutil num exercicio cujo
/// ponto e mostrar a hora certa.
///
/// E getLocalTime() devolve FALSO sem WiFi conectado, como na placa de
/// verdade: e o erro numero um de quem escreve esse sketch, e o
/// simulador tem que reproduzi-lo, nao esconde-lo.
/// </summary>
public partial class Interpretador
{
    /// <summary>Deslocamento do fuso pedido no configTime, em segundos.</summary>
    long fusoEmSegundos;

    /// <summary>Horario de verao pedido no configTime, em segundos.</summary>
    long verãoEmSegundos;

    /// <summary>O sketch chamou configTime().</summary>
    bool relogioPedido;

    /// <summary>Os campos de um "struct tm", na ordem do C.</summary>
    static readonly string[] CamposDoTm =
    [
        "tm_sec", "tm_min", "tm_hour", "tm_mday", "tm_mon",
        "tm_year", "tm_wday", "tm_yday", "tm_isdst",
    ];

    /// <summary>Uma variavel declarada como "struct tm".</summary>
    internal static bool EhHorario(string classe) => classe is "tm" or "timeinfo";

    /// <summary>Monta o objeto do "struct tm", zerado.</summary>
    internal static Objeto NovoHorario()
    {
        var o = new Objeto("tm");
        foreach (string campo in CamposDoTm) o.Campos[campo] = Valor.DeInt(0);
        return o;
    }

    /// <summary>
    /// "configTime(fuso, verao, servidor)".
    ///
    /// Nao consulta servidor nenhum: o relogio do computador ja esta
    /// certo, e e dele que a hora sai. O que a chamada faz e guardar o
    /// FUSO, que e o que muda o que aparece na tela.
    /// </summary>
    Valor AjustarRelogio(List<Valor> args)
    {
        fusoEmSegundos = args.Count > 0 ? (long)args[0].Num : 0;
        verãoEmSegundos = args.Count > 1 ? (long)args[1].Num : 0;
        relogioPedido = true;

        // A placa de verdade leva um instante falando com o servidor.
        Maq.Avancar(120);
        return Valor.Vazio;
    }

    DateTime Agora()
        => DateTime.UtcNow.AddSeconds(fusoEmSegundos + verãoEmSegundos);

    /// <summary>
    /// "getLocalTime(&amp;agora)" e "getLocalTime(&amp;agora, 5000)".
    ///
    /// O argumento e o NOME de um objeto, e nao um valor -- entao esta
    /// chamada nao passa pela avaliacao normal, do mesmo jeito que o
    /// attachInterrupt.
    /// </summary>
    bool PegarHorario(Chamada c, Ambiente amb, out Valor resultado)
    {
        resultado = Valor.DeBool(false);

        var alvo = ObjetoDoArgumento(c.Args.Count > 0 ? c.Args[0] : null, amb);
        if (alvo is null)
            throw new ErroDeExecucao(
                "getLocalTime() espera o endereco de um 'struct tm', "
                + "como em getLocalTime(&agora).", c.Linha);

        // Sem WiFi nao ha NTP -- e o erro numero um deste sketch.
        bool temRede = Maq.Rede is not null && Maq.Rede.NaRede(Maq);
        if (!temRede || !relogioPedido)
        {
            Maq.Rede?.Anotar(Maq, "ntp", !relogioPedido
                ? "getLocalTime() antes de configTime(): o relogio nunca foi acertado"
                : "getLocalTime() sem WiFi conectado: a hora nao pode ser buscada");
            return true;
        }

        var agora = Agora();
        alvo.Campos["tm_sec"] = Valor.DeInt(agora.Second);
        alvo.Campos["tm_min"] = Valor.DeInt(agora.Minute);
        alvo.Campos["tm_hour"] = Valor.DeInt(agora.Hour);
        alvo.Campos["tm_mday"] = Valor.DeInt(agora.Day);

        // O C conta o mes de 0 e o ano a partir de 1900. Quem le
        // "tm_mon + 1" e "tm_year + 1900" no sketch espera isso.
        alvo.Campos["tm_mon"] = Valor.DeInt(agora.Month - 1);
        alvo.Campos["tm_year"] = Valor.DeInt(agora.Year - 1900);
        alvo.Campos["tm_wday"] = Valor.DeInt((int)agora.DayOfWeek);
        alvo.Campos["tm_yday"] = Valor.DeInt(agora.DayOfYear - 1);
        alvo.Campos["tm_isdst"] = Valor.DeInt(verãoEmSegundos != 0 ? 1 : 0);

        resultado = Valor.DeBool(true);
        return true;
    }

    /// <summary>
    /// O objeto por tras de "&amp;agora" ou de "agora".
    ///
    /// Devolve null quando o argumento nao aponta para objeto nenhum --
    /// e ai quem chamou diz o que esperava, com a linha certa.
    /// </summary>
    Objeto ObjetoDoArgumento(Expr e, Ambiente amb)
    {
        if (e is Unaria { Op: "&" } endereco) e = endereco.Alvo;
        if (e is not Variavel v) return null;
        return amb.BuscarObjeto(v.Nome, out var o) ? o : null;
    }

    /// <summary>
    /// "strftime(buffer, sizeof(buffer), "%d/%m/%Y", &amp;agora)".
    ///
    /// Escreve DENTRO do vetor de char, como a funcao do C: por isso ela
    /// tambem nao passa pela avaliacao normal dos argumentos.
    /// </summary>
    bool FormatarHorario(Chamada c, Ambiente amb, out Valor resultado)
    {
        resultado = Valor.DeInt(0);
        if (c.Args.Count < 4) return false;

        var horario = ObjetoDoArgumento(c.Args[3], amb);
        if (horario is null) return false;

        string texto = Strftime(Avaliar(c.Args[2], amb).Mostrar(), horario);

        if (c.Args[0] is Variavel destino && amb.BuscarArray(destino.Nome, out var vetor))
        {
            for (int i = 0; i < vetor.Length; i++)
                vetor[i] = Valor.DeCaractere(i < texto.Length ? texto[i] : 0);
        }
        else if (c.Args[0] is Variavel texto2) amb.Atribuir(texto2.Nome, Valor.DeTexto(texto));

        resultado = Valor.DeInt(texto.Length);
        return true;
    }

    /// <summary>
    /// O strftime do C, nos formatos que os sketches usam.
    ///
    /// Nao e a tabela inteira do padrao: e a parte que aparece em relogio
    /// de aula. O que nao for reconhecido sai como veio, para a pessoa
    /// enxergar o que o simulador nao entendeu em vez de ver um buraco.
    /// </summary>
    internal static string Strftime(string molde, Objeto horario)
    {
        int C(string campo) => horario.Campos.TryGetValue(campo, out var v) ? (int)v.Num : 0;

        var meses = new[]
        {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };
        var dias = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        var saida = new System.Text.StringBuilder();

        for (int i = 0; i < molde.Length; i++)
        {
            if (molde[i] != '%' || i + 1 >= molde.Length) { saida.Append(molde[i]); continue; }

            char tipo = molde[++i];
            int hora12 = C("tm_hour") % 12;
            if (hora12 == 0) hora12 = 12;

            saida.Append(tipo switch
            {
                'H' => C("tm_hour").ToString("00"),
                'M' => C("tm_min").ToString("00"),
                'S' => C("tm_sec").ToString("00"),
                'I' => hora12.ToString("00"),
                'p' => C("tm_hour") < 12 ? "AM" : "PM",
                'd' => C("tm_mday").ToString("00"),
                'm' => (C("tm_mon") + 1).ToString("00"),
                'Y' => (C("tm_year") + 1900).ToString(),
                'y' => ((C("tm_year") + 1900) % 100).ToString("00"),
                'j' => (C("tm_yday") + 1).ToString("000"),
                'b' or 'h' => meses[Math.Clamp(C("tm_mon"), 0, 11)],
                'B' => MesPorExtenso(C("tm_mon")),
                'a' => dias[Math.Clamp(C("tm_wday"), 0, 6)],
                'A' => DiaPorExtenso(C("tm_wday")),
                'Z' => "UTC",
                'F' => $"{C("tm_year") + 1900}-{C("tm_mon") + 1:00}-{C("tm_mday"):00}",
                'T' => $"{C("tm_hour"):00}:{C("tm_min"):00}:{C("tm_sec"):00}",
                'R' => $"{C("tm_hour"):00}:{C("tm_min"):00}",
                '%' => "%",
                _ => "%" + tipo,
            });
        }

        return saida.ToString();
    }

    static string MesPorExtenso(int mes) => new[]
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    }[Math.Clamp(mes, 0, 11)];

    static string DiaPorExtenso(int dia) => new[]
    {
        "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"
    }[Math.Clamp(dia, 0, 6)];

    /// <summary>
    /// "Serial.println(&amp;agora, "%H:%M:%S")" e o mesmo no LCD.
    ///
    /// E a forma que o proprio exemplo do core da ESP32 usa, e ela nao
    /// se parece com nenhuma outra chamada de print: o primeiro
    /// argumento e um ENDERECO e o segundo e o molde.
    /// </summary>
    bool TextoDeHorario(Chamada c, Ambiente amb, out string texto)
    {
        texto = null;
        if (c.Args.Count < 2) return false;

        var horario = ObjetoDoArgumento(c.Args[0], amb);
        if (horario is null || horario.Classe != "tm") return false;

        texto = Strftime(Avaliar(c.Args[1], amb).Mostrar(), horario);
        return true;
    }
}
