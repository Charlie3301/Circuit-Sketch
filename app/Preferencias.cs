using System.IO;

namespace Simulador.App;

/// <summary>
/// As escolhas que ficam de uma abertura para a outra.
///
/// Fica em %APPDATA%, do lado da lista de recentes e pelo mesmo motivo:
/// no laboratório o aluno costuma não ter escrita na pasta do programa,
/// e preferência que some a cada aula não é preferência.
///
/// É um arquivo de "chave = valor", uma linha cada. Nada de JSON: isso
/// aqui é para ser aberto no bloco de notas e consertado à mão quando
/// alguém escolher um tema que não enxerga.
/// </summary>
public static class Preferencias
{
    static string Pasta => Armazem.Pasta;

    static string Arquivo => Path.Combine(Pasta, "preferencias.txt");

    static Dictionary<string, string> valores;

    static Dictionary<string, string> Tudo()
    {
        if (valores is not null) return valores;

        valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(Arquivo)) return valores;

            foreach (string linha in File.ReadAllLines(Arquivo))
            {
                int igual = linha.IndexOf('=');
                if (igual <= 0) continue;
                valores[linha[..igual].Trim()] = linha[(igual + 1)..].Trim();
            }
        }
        catch { /* arquivo ilegível: vale o padrão */ }

        return valores;
    }

    static void Gravar()
    {
        try
        {
            Directory.CreateDirectory(Pasta);
            File.WriteAllLines(Arquivo,
                Tudo().OrderBy(x => x.Key).Select(x => $"{x.Key} = {x.Value}"));
        }
        catch
        {
            // Sem escrita em %APPDATA% o programa segue: perder a
            // preferência não é motivo para atrapalhar a aula.
        }
    }

    public static string Texto(string chave, string padrao = "")
        => Tudo().TryGetValue(chave, out var v) && v.Length > 0 ? v : padrao;

    /// <summary>
    /// Chave vazia vale como chave que não existe.
    ///
    /// Sem isto, apagar o valor de "grade" no arquivo (ou gravar vazio)
    /// desligava a grade em vez de voltar ao padrão -- e "vazio" é
    /// exatamente o que alguém escreve quando quer desfazer a escolha.
    /// </summary>
    public static bool Ligado(string chave, bool padrao)
        => Tudo().TryGetValue(chave, out var v) && v.Length > 0
            ? v is "1" or "sim" or "true" or "verdadeiro"
            : padrao;

    public static void Definir(string chave, string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) Tudo().Remove(chave);
        else Tudo()[chave] = valor.Trim();

        Gravar();
    }

    public static void Definir(string chave, bool ligado)
        => Definir(chave, ligado ? "sim" : "nao");

    // ---------------- as chaves que existem ----------------
    public const string Grade = "grade";
    /// <summary>O desenho do fundo com a grade ligada: "pontilhado", "grade" ou "quadriculado".</summary>
    public const string EstiloDoFundo = "estilo-do-fundo";
    /// <summary>O "author" dos diagram.json (vazio: o usuario do Windows).</summary>
    public const string Autor = "autor-dos-projetos";

    /// <summary>O idioma da interface ("pt-BR", "en"...): vale ao reabrir o programa.</summary>
    public const string Idioma = "idioma";
    public const string EstiloDoFio = "estilo-do-fio";
    public const string UsarInternet = "usar-internet";
    public const string MostrarAbertura = "mostrar-abertura";
    public const string ProcurarAtualizacoes = "procurar-atualizacoes";
    public const string UltimaPlaca = "ultima-placa";
    public const string UltimoProjeto = "ultimo-projeto";
    public const string CorDoFio = "cor-do-fio";
    public const string Tema = "tema";
    public const string LembrarJanela = "lembrar-janela";
    public const string AbrirMaximizado = "abrir-maximizado";
    public const string JanelaLargura = "janela-largura";
    public const string JanelaAltura = "janela-altura";
    public const string JanelaEsquerda = "janela-esquerda";
    public const string JanelaTopo = "janela-topo";
    public const string UsarRadioDoPc = "usar-radio-do-pc";
    public const string AbrirServidor = "abrir-servidor-no-navegador";

    // ---- editor de código (Alpha 19) ----
    public const string FonteDoCodigo = "fonte-do-codigo";
    public const string TamanhoDoCodigo = "tamanho-do-codigo";
    public const string NumerosDeLinha = "numeros-de-linha";
    public const string QuebrarLinhas = "quebrar-linhas";
    public const string DestacarLinha = "destacar-linha";
    public const string TamanhoDoRecuo = "tamanho-do-recuo";
    public const string TabViraEspacos = "tab-vira-espacos";
    public const string MostrarEspacos = "mostrar-espacos";
    public const string FecharPares = "fechar-pares";
    public const string ZoomComRoda = "zoom-com-roda";

    // ---- monitor serial ----
    public const string TempoNoSerial = "tempo-no-serial";
    public const string LinhasNoSerial = "linhas-no-serial";
    public const string RolarSerial = "rolar-serial";
    public const string FimDeLinha = "fim-de-linha";
    public const string LimparSerialAoRodar = "limpar-serial-ao-rodar";
    public const string TamanhoDoSerial = "tamanho-do-serial";
    public const string AvisosNoMonitor = "avisos-no-monitor";

    // ---- simulação ----
    public const string RodarAoAbrirProjeto = "rodar-ao-abrir-projeto";
    public const string RodarAoAbrirDemo = "rodar-ao-abrir-demo";
    public const string SimularQueima = "simular-queima";

    /// <summary>Os selos de aviso em cima das pecas (com o X para fechar cada um). Vem ligado.</summary>
    public const string SelosDeAviso = "selos-de-aviso";
    public const string SomDoBuzzer = "som-do-buzzer";
    public const string VolumeDoBuzzer = "volume-do-buzzer";

    /// <summary>
    /// O SerialBT da placa aparece no radio Bluetooth do computador, para o
    /// celular de verdade conectar pelo app de terminal.
    /// </summary>
    public const string BluetoothDoComputador = "bluetooth-do-computador";

    /// <summary>A janela do celular abre sozinha quando uma placa liga o Bluetooth (uma vez por rodada).</summary>
    public const string AbrirCelularSozinho = "abrir-celular-sozinho";

    // ---- arquivos ----
    public const string SalvarSozinho = "salvar-sozinho";
    public const string CopiaDeSeguranca = "copia-de-seguranca";
    public const string QuantosRecentes = "quantos-recentes";
    public const string ReabrirUltimo = "reabrir-ultimo";
    public const string PlacaDoCircuitoNovo = "placa-do-circuito-novo";

    // ---- aparência e circuito ----
    public const string EscalaDaInterface = "escala-da-interface";
    public const string EspessuraDoFio = "espessura-do-fio";

    // O broker MQTT do computador (a janela do MQTT).
    public const string MqttPorta = "mqtt-porta";
    public const string MqttNaRede = "mqtt-na-rede";
    public const string MqttSoNoComputador = "mqtt-so-no-computador";

    // Os controles do celular: o que cada botao manda (JSON numa linha).
    public const string CelularControleRc = "celular-controle-rc";
    public const string CelularControleVideogame = "celular-controle-videogame";

    /// <summary>
    /// Um número guardado. Fora da faixa, ou escrito errado no arquivo,
    /// vale o padrão -- e não o número mais perto: quem digitou 0 na
    /// escala da janela não quer a menor escala possível.
    /// </summary>
    public static int Numero(string chave, int padrao, int minimo, int maximo)
        => int.TryParse(Texto(chave), out int n) && n >= minimo && n <= maximo ? n : padrao;

    /// <summary>
    /// Volta tudo ao que vem de fábrica. A lista de recentes fica: ela é
    /// histórico, e não preferência -- e mora em outro arquivo.
    /// </summary>
    public static void Restaurar()
    {
        Tudo().Clear();
        Gravar();
    }

    /// <summary>
    /// O scan de WiFi parte do radio deste computador.
    ///
    /// Ligado por padrao: foi o que o professor pediu. Desligar volta as
    /// redes simuladas -- e a saida para quem nao quer que o programa
    /// leia a lista de redes da casa.
    /// </summary>
    public static bool UsarRadioDoComputador => Ligado(UsarRadioDoPc, true);

    /// <summary>
    /// Um servidor que sobe na placa abre a pagina no navegador.
    ///
    /// Tambem ligado por padrao, e tambem por pedido dele: e o que
    /// acontece quando se liga uma ESP de verdade e ela anuncia o
    /// endereco. Quem achar invasivo desliga nas configuracoes.
    /// </summary>
    public static bool AbrirServidorNoNavegador => Ligado(AbrirServidor, true);

    /// <summary>Para o teste: esquece o que já foi lido do disco.</summary>
    internal static void Recarregar() => valores = null;
}
