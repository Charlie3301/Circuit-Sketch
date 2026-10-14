using System.Diagnostics;

namespace Simulador;

/// <summary>Uma rede que o radio do computador enxergou de verdade.</summary>
public sealed record RedeVista(string Ssid, int Rssi, bool Aberta);

/// <summary>
/// O WiFi e o Bluetooth DO COMPUTADOR, vistos pelo simulador.
///
/// O professor rodou um scan de WiFi e achou duas redes: "CircuitSketch-WiFi"
/// e "Wokwi-GUEST". As duas eram inventadas -- a segunda so existe porque
/// os sketches do Wokwi conectam nela. Ele perguntou, com razao, por que
/// o simulador nao parte do WiFi da maquina.
///
/// Agora parte. Duas coisas precisam ficar claras, e a interface diz as
/// duas:
///
/// 1. A placa simulada NAO tem radio. O que ela ve e o que o computador
///    ve -- o scan e de verdade, a associacao continua sendo simulada.
///    Prometer mais do que isso seria mentira: nao ha hardware.
/// 2. Quando o computador nao tem radio, ou o Windows recusa a consulta,
///    o simulador volta as redes de mentira E AVISA NA TELA, que foi
///    exatamente o que ele pediu.
///
/// A consulta sai por <c>netsh</c> e por <c>Get-PnpDevice</c>, e nao por
/// P/Invoke da WlanAPI: sao duas ferramentas que existem em qualquer
/// Windows, nao precisam de permissao especial e nao arrastam dependencia
/// nenhuma para dentro do executavel, que e single-file.
/// </summary>
public static class RadioDoComputador
{
    /// <summary>
    /// Quanto tempo a varredura vale antes de perguntar de novo.
    ///
    /// Um scan de verdade custa um ou dois segundos. Repetir isso a cada
    /// <c>WiFi.scanNetworks()</c> dentro de um loop travaria o sketch --
    /// e a placa de verdade tambem nao rescaneia tao rapido assim.
    /// </summary>
    static readonly TimeSpan Validade = TimeSpan.FromSeconds(20);

    static readonly Lock trava = new();

    static List<RedeVista> redes;
    static DateTime medidoEm = DateTime.MinValue;

    static List<string> bluetooth;
    static DateTime bluetoothEm = DateTime.MinValue;

    /// <summary>
    /// Por que o radio do computador nao pode ser usado, ou null quando
    /// pode. E o texto que a janela mostra.
    /// </summary>
    public static string MotivoDoWifi { get; private set; }

    public static string MotivoDoBluetooth { get; private set; }

    /// <summary>
    /// Desliga a consulta ao computador.
    ///
    /// Os testes ligam isto: eles rodam em maquinas sem WiFi e nao podem
    /// depender do que o Windows responde. A pessoa tambem pode desligar
    /// nas configuracoes, se nao quiser que o programa olhe a rede dela.
    /// </summary>
    public static bool Desligado { get; set; }

    /// <summary>As redes que o computador enxerga. Vazio quando nao da.</summary>
    public static List<RedeVista> Redes()
    {
        if (Desligado) { MotivoDoWifi = "a consulta ao WiFi do computador está desligada"; return []; }

        lock (trava)
        {
            if (redes is not null && DateTime.Now - medidoEm < Validade) return redes;
            redes = Varrer();
            medidoEm = DateTime.Now;
            return redes;
        }
    }

    /// <summary>Os aparelhos de Bluetooth que o computador conhece.</summary>
    public static List<string> Bluetooths()
    {
        if (Desligado)
        {
            MotivoDoBluetooth = "a consulta ao Bluetooth do computador está desligada";
            return [];
        }

        lock (trava)
        {
            if (bluetooth is not null && DateTime.Now - bluetoothEm < Validade) return bluetooth;
            bluetooth = VarrerBluetooth();
            bluetoothEm = DateTime.Now;
            return bluetooth;
        }
    }

    /// <summary>Esquece o que mediu: a proxima consulta pergunta de novo.</summary>
    public static void Esquecer()
    {
        lock (trava)
        {
            redes = null;
            bluetooth = null;
            medidoEm = bluetoothEm = DateTime.MinValue;
        }
    }

    // =====================================================================
    //  WiFi
    // =====================================================================
    static List<RedeVista> Varrer()
    {
        string saida = Rodar("netsh", "wlan show networks mode=bssid", out string erro);

        if (saida is null)
        {
            MotivoDoWifi = erro ?? "não consegui falar com o Windows sobre o WiFi";
            return [];
        }

        // Sem adaptador, o netsh responde isso -- e nao um erro.
        if (saida.Contains("not running", StringComparison.OrdinalIgnoreCase)
            || saida.Contains("não está em execução", StringComparison.OrdinalIgnoreCase)
            || saida.Contains("no interface", StringComparison.OrdinalIgnoreCase)
            || saida.Contains("nenhuma interface", StringComparison.OrdinalIgnoreCase))
        {
            MotivoDoWifi = "este computador não tem adaptador de WiFi ligado";
            return [];
        }

        var achadas = Ler(saida);
        MotivoDoWifi = achadas.Count > 0
            ? null
            : "o WiFi do computador não encontrou nenhuma rede";
        return achadas;
    }

    /// <summary>
    /// Le a saida do netsh.
    ///
    /// Os ROTULOS sao traduzidos ("Sinal", "Autenticacao"), mas a palavra
    /// SSID e o numero nao sao -- entao o corte e por elas, e o sinal sai
    /// do unico numero com "%" dentro do bloco. Assim a leitura funciona
    /// no Windows em portugues, ingles ou espanhol sem lista de traducao.
    /// </summary>
    internal static List<RedeVista> Ler(string saida)
    {
        var achadas = new List<RedeVista>();
        string ssid = null;
        int sinal = 0;
        bool aberta = false;

        void Fechar()
        {
            if (string.IsNullOrWhiteSpace(ssid)) return;
            achadas.Add(new RedeVista(ssid, PorCentoParaRssi(sinal), aberta));
        }

        foreach (string bruta in saida.Replace("\r\n", "\n").Split('\n'))
        {
            string linha = bruta.Trim();

            if (linha.StartsWith("SSID ", StringComparison.OrdinalIgnoreCase)
                && linha.Contains(':'))
            {
                Fechar();
                ssid = linha[(linha.IndexOf(':') + 1)..].Trim();
                sinal = 0;
                aberta = false;
                continue;
            }

            if (ssid is null) continue;

            // O primeiro "%" do bloco e o sinal do BSSID mais forte.
            int porcento = linha.IndexOf('%');
            if (porcento > 0 && sinal == 0)
            {
                string numero = new([.. linha[..porcento].Reverse()
                    .TakeWhile(char.IsDigit).Reverse()]);
                if (int.TryParse(numero, out int lido)) sinal = lido;
            }

            // Rede sem senha: o VALOR nao e traduzido em ingles, e em
            // portugues vira "Aberta". As duas formas cabem aqui.
            if (linha.Contains("Open", StringComparison.OrdinalIgnoreCase)
                || linha.Contains("Abert", StringComparison.OrdinalIgnoreCase))
                aberta = true;
        }

        Fechar();
        return [.. achadas.DistinctBy(r => r.Ssid)];
    }

    /// <summary>
    /// O netsh da a forca em porcento; o sketch espera dBm.
    ///
    /// A conta e a que a propria Microsoft documenta: 0% = -100 dBm,
    /// 100% = -50 dBm. Nao e exata -- nenhuma conversao de porcento para
    /// dBm e --, mas poe o numero na faixa certa, que e o que o sketch
    /// compara.
    /// </summary>
    static int PorCentoParaRssi(int porcento)
        => porcento <= 0 ? -90 : (porcento / 2) - 100;

    // =====================================================================
    //  Bluetooth
    // =====================================================================
    static List<string> VarrerBluetooth()
    {
        string saida = Rodar("powershell",
            "-NoProfile -NonInteractive -Command "
            + "\"Get-PnpDevice -Class Bluetooth -Status OK -ErrorAction SilentlyContinue "
            + "| Select-Object -ExpandProperty FriendlyName\"",
            out string erro);

        if (saida is null)
        {
            MotivoDoBluetooth = erro ?? "não consegui falar com o Windows sobre o Bluetooth";
            return [];
        }

        var nomes = saida.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Distinct()
            .ToList();

        if (nomes.Count == 0)
        {
            MotivoDoBluetooth = "este computador não tem Bluetooth ligado";
            return [];
        }

        // O adaptador nao e um aparelho pareado: e o radio do proprio
        // computador. Separa-lo evita dizer que ha um fone conectado
        // quando o que existe e so a antena.
        var aparelhos = nomes
            .Where(n => !n.Contains("Wireless Bluetooth", StringComparison.OrdinalIgnoreCase)
                        && !n.Contains("Bluetooth Radio", StringComparison.OrdinalIgnoreCase)
                        && !n.Contains("Enumerator", StringComparison.OrdinalIgnoreCase))
            .ToList();

        MotivoDoBluetooth = aparelhos.Count > 0
            ? null
            : "o computador tem Bluetooth, mas nenhum aparelho pareado";
        return aparelhos;
    }

    // =====================================================================
    //  o processo
    // =====================================================================
    /// <summary>
    /// Roda um comando do Windows e devolve o que ele escreveu.
    ///
    /// Sem janela e com prazo: uma consulta que travasse levaria junto a
    /// thread do sketch, e o aluno veria o simulador congelar sem motivo.
    /// </summary>
    static string Rodar(string programa, string argumentos, out string erro)
    {
        erro = null;

        try
        {
            using var processo = new Process
            {
                StartInfo = new ProcessStartInfo(programa, argumentos)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                }
            };

            processo.Start();
            string saida = processo.StandardOutput.ReadToEnd();

            if (!processo.WaitForExit(8000))
            {
                try { processo.Kill(true); } catch (InvalidOperationException) { }
                erro = "a consulta ao Windows demorou demais";
                return null;
            }

            return saida;
        }
        catch (Exception e)
        {
            erro = e.Message;
            return null;
        }
    }
}
