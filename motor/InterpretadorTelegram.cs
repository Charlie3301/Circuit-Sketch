namespace Simulador;

/// <summary>
/// A UniversalTelegramBot, sobre o HTTP que ja existe.
///
/// Entrou por causa de um sketch da turma: um alarme que avisa no
/// Telegram. Ate a Alpha 17 o simulador recusava a classe inteira, e o
/// aluno nao tinha como sequer ver o programa dele rodar.
///
/// **A conversa e de verdade.** Nao ha Telegram simulado aqui: o
/// sendMessage vai para api.telegram.org pelo mesmo caminho que um
/// HTTPClient do sketch usaria. Com um token de verdade, a mensagem
/// chega no celular; com o token de exemplo que vem no sketch, a API
/// responde 401 e o simulador diz isso -- que e exatamente o que a
/// placa de verdade faria.
///
/// Simular o Telegram seria pior do que nao ter: o aluno veria
/// "Notificación enviada" sem nada chegar em lugar nenhum, e passaria a
/// tarde procurando o erro no celular.
/// </summary>
public partial class Interpretador
{
    /// <summary>Uma mensagem recebida, do jeito que o sketch a le.</summary>
    sealed class MensagemDeTelegram
    {
        public string ChatId = "";
        public string Texto = "";
        public string DeNome = "";
        public long Id;
    }

    sealed class EstadoTelegram
    {
        public string Token = "";
        public readonly List<MensagemDeTelegram> Recebidas = [];

        /// <summary>O id da ultima mensagem lida: o sketch soma 1 e pede as proximas.</summary>
        public long Ultima;
    }

    /// <summary>
    /// "UniversalTelegramBot bot(TOKEN, cliente)".
    /// </summary>
    internal static Objeto NovoTelegram(string token)
    {
        var o = new Objeto("UniversalTelegramBot");
        o.Rede = new EstadoTelegram { Token = token ?? "" };
        o.Campos["last_message_received"] = Valor.DeInt(0);
        return o;
    }

    Valor ChamarTelegram(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var estado = (EstadoTelegram)obj.Rede;
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string S(int i) => i < args.Count ? args[i].Mostrar() : "";

        switch (metodo)
        {
            case "sendMessage":
            case "sendMessageWithReplyKeyboard":
            case "sendMessageWithInlineKeyboard":
                return Valor.DeBool(EnviarNoTelegram(estado, S(0), S(1), S(2), c.Linha));

            case "getUpdates":
            {
                long deOnde = args.Count > 0 ? (long)args[0].Num : estado.Ultima + 1;
                int quantas = BuscarNoTelegram(estado, deOnde, c.Linha);
                obj.Campos["last_message_received"] = Valor.DeInt(estado.Ultima);
                return Valor.DeInt(quantas);
            }

            // A biblioteca de verdade tem mais coisa (foto, teclado,
            // documento). O que nao esta aqui e recusado pelo nome,
            // em vez de responder qualquer coisa.
            case "longPoll": case "setMyCommands": case "updateToken":
                return Valor.DeBool(true);
        }

        throw new ErroDeExecucao(
            Idioma.T("bot.{0}() ainda nao e suportado pelo simulador. Por enquanto valem sendMessage() e getUpdates().", metodo), c.Linha);
    }

    bool EnviarNoTelegram(EstadoTelegram estado, string chatId, string texto,
                          string modo, int linha)
    {
        if (Maq.Rede is null) return false;

        if (!Maq.Rede.WifiConectado(Maq))
        {
            Maq.Rede.Anotar(Maq, "telegram",
                Idioma.T("bot.sendMessage() antes de o WiFi conectar: nao foi enviado"));
            return false;
        }

        string url = $"https://api.telegram.org/bot{estado.Token}/sendMessage";
        string corpo = "chat_id=" + Uri.EscapeDataString(chatId ?? "")
                       + "&text=" + Uri.EscapeDataString(texto ?? "");
        if (!string.IsNullOrEmpty(modo)) corpo += "&parse_mode=" + Uri.EscapeDataString(modo);

        var cabecalhos = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/x-www-form-urlencoded",
        };

        var resposta = Maq.Rede.Requisitar(Maq, "POST", url, cabecalhos, corpo, 8000, false);

        if (resposta.Codigo == 200) return true;

        Maq.Rede.Anotar(Maq, "telegram",
            resposta.Codigo == 401
                ? Idioma.T("o Telegram recusou o token (401). Confira o BOT_TOKEN do sketch.")
                : Idioma.T("o Telegram respondeu {0}", resposta.Codigo));
        return false;
    }

    /// <summary>
    /// "bot.getUpdates(offset)": pergunta a API o que chegou.
    /// </summary>
    int BuscarNoTelegram(EstadoTelegram estado, long deOnde, int linha)
    {
        estado.Recebidas.Clear();
        if (Maq.Rede is null || !Maq.Rede.WifiConectado(Maq)) return 0;

        string url = $"https://api.telegram.org/bot{estado.Token}/getUpdates"
                     + $"?offset={deOnde}&limit=5&timeout=0";

        var resposta = Maq.Rede.Requisitar(Maq, "GET", url, null, null, 8000, false);

        if (resposta.Codigo != 200)
        {
            Maq.Rede.Anotar(Maq, "telegram",
                resposta.Codigo == 401
                    ? Idioma.T("o Telegram recusou o token (401) no getUpdates()")
                    : Idioma.T("getUpdates() respondeu {0}", resposta.Codigo));
            return 0;
        }

        LerMensagensDoTelegram(estado, resposta.Corpo);
        return estado.Recebidas.Count;
    }

    /// <summary>
    /// Le a resposta do getUpdates.
    ///
    /// So os tres campos que o sketch usa: o id da atualizacao, o chat e
    /// o texto. Um analisador completo de JSON nao acrescentaria nada
    /// aqui -- o resto da resposta nunca e lido.
    /// </summary>
    internal static void LerMensagensDoTelegram(object alvo, string json)
    {
        var estado = (EstadoTelegram)alvo;
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var lista)) return;

            foreach (var item in lista.EnumerateArray())
            {
                var mensagem = new MensagemDeTelegram();

                if (item.TryGetProperty("update_id", out var id)) mensagem.Id = id.GetInt64();
                if (!item.TryGetProperty("message", out var corpo)) continue;

                if (corpo.TryGetProperty("text", out var texto))
                    mensagem.Texto = texto.GetString() ?? "";

                if (corpo.TryGetProperty("chat", out var chat)
                    && chat.TryGetProperty("id", out var chatId))
                    mensagem.ChatId = chatId.ToString();

                if (corpo.TryGetProperty("from", out var de)
                    && de.TryGetProperty("first_name", out var nome))
                    mensagem.DeNome = nome.GetString() ?? "";

                estado.Recebidas.Add(mensagem);
                estado.Ultima = Math.Max(estado.Ultima, mensagem.Id);
            }
        }
        catch (System.Text.Json.JsonException) { }
    }

    /// <summary>
    /// "bot.messages[i].chat_id" e "bot.messages[i].text".
    ///
    /// A biblioteca expoe as mensagens como vetor de struct. Aqui elas
    /// viram objetos criados na hora da leitura -- o sketch so olha, e
    /// nunca escreve nelas.
    /// </summary>
    bool MensagemDoTelegram(Indice ix, Ambiente amb, out Valor v)
    {
        v = Valor.Vazio;

        if (ix.Alvo is not Membro m || m.Nome != "messages") return false;
        if (ObjetoDaExpressao(m.Obj, amb) is not { Classe: "UniversalTelegramBot" } bot)
            return false;

        var estado = (EstadoTelegram)bot.Rede;
        int i = (int)Avaliar(ix.Idx, amb).Num;
        if (i < 0 || i >= estado.Recebidas.Count) return false;

        var mensagem = estado.Recebidas[i];
        var visto = new Objeto("telegramMessage");
        visto.Campos["chat_id"] = Valor.DeTexto(mensagem.ChatId);
        visto.Campos["text"] = Valor.DeTexto(mensagem.Texto);
        visto.Campos["from_name"] = Valor.DeTexto(mensagem.DeNome);
        visto.Campos["update_id"] = Valor.DeInt(mensagem.Id);
        visto.Campos["type"] = Valor.DeTexto("message");

        v = Valor.DeReferencia(visto);
        return true;
    }

    /// <summary>
    /// O Preferences da ESP32: memoria que sobrevive ao reset.
    ///
    /// Guarda na propria placa simulada, e nao em disco. Vale enquanto o
    /// programa estiver aberto, e o RESET nao apaga -- que e justamente
    /// o ponto da biblioteca. Fechar o simulador apaga, e esta certo:
    /// e outra placa da proxima vez.
    /// </summary>
    Valor ChamarPreferencias(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var guardado = (Dictionary<string, Valor>)obj.Rede;
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string chave = args.Count > 0 ? args[0].Mostrar() : "";

        switch (metodo)
        {
            case "begin": return Valor.DeBool(true);
            case "end": return Valor.Vazio;

            case "clear": guardado.Clear(); return Valor.DeBool(true);
            case "remove": return Valor.DeBool(guardado.Remove(chave));
            case "isKey": return Valor.DeBool(guardado.ContainsKey(chave));
            case "freeEntries": return Valor.DeInt(Math.Max(0, 500 - guardado.Count));
        }

        // putX(chave, valor) e getX(chave, padrao). O X e o tipo, e para
        // o simulador os tipos sao dois: numero e texto.
        if (metodo.StartsWith("put", StringComparison.Ordinal))
        {
            if (args.Count < 2) return Valor.DeInt(0);
            guardado[chave] = args[1];
            return Valor.DeInt(1);
        }

        if (metodo.StartsWith("get", StringComparison.Ordinal))
        {
            if (guardado.TryGetValue(chave, out var achado)) return achado;

            // Sem o valor guardado, devolve o padrao que o sketch passou
            // -- e a razao de existir do segundo argumento.
            if (args.Count > 1) return args[1];

            return metodo.Contains("String", StringComparison.Ordinal)
                ? Valor.DeTexto("")
                : metodo.Contains("Bool", StringComparison.Ordinal)
                    ? Valor.DeBool(false)
                    : Valor.DeInt(0);
        }

        throw new ErroDeExecucao(
            Idioma.T("preferences.{0}() ainda nao e suportado pelo simulador", metodo), c.Linha);
    }
}
