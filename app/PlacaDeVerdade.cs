using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Simulador.App;

/// <summary>
/// A placa de verdade: compilar com o compilador do Arduino e mandar pela USB.
///
/// O simulador interpreta o sketch; a placa precisa dele compilado. Quem
/// compila e o arduino-cli que vem dentro da Arduino IDE 2 -- o mesmo que o
/// botao de seta da IDE usa por baixo --, com os nucleos e as bibliotecas
/// que a pessoa ja instalou nela. Nada novo para instalar: sem a IDE, os
/// botoes dizem isso e o que fazer.
/// </summary>
public static partial class PlacaDeVerdade
{
    /// <summary>Uma porta USB com (talvez) uma placa reconhecida.</summary>
    public sealed record Porta(string Endereco, string Rotulo, string Placa, string Fqbn);

    /// <summary>O fim de uma compilacao ou envio: deu certo, o que dizer, e o uso de memoria.</summary>
    public sealed record Resultado(bool Ok, string Resumo, string Memoria);

    static string cli;
    static bool procurou;

    /// <summary>
    /// Onde fica o arduino-cli que o próprio CircuitSketch baixa (de
    /// downloads.arduino.cc) quando a Arduino IDE 2 não está instalada.
    /// </summary>
    public static string CliBaixado => Path.Combine(Armazem.Pasta, "arduino-cli", "arduino-cli.exe");

    /// <summary>Procura o arduino-cli de novo (depois de baixar, ou para o teste).</summary>
    public static void EsquecerCli() { procurou = false; cli = null; }

    /// <summary>O arduino-cli da Arduino IDE 2, o do PATH ou o que o CircuitSketch baixou; ou null.</summary>
    public static string Cli
    {
        get
        {
            if (procurou) return cli;
            procurou = true;

            foreach (string raiz in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     })
            {
                string candidato = Path.Combine(raiz, "Arduino IDE", "resources", "app", "lib", "backend",
                                                "resources", "arduino-cli.exe");
                if (File.Exists(candidato)) return cli = candidato;
            }

            // Instalado sozinho, no PATH.
            foreach (string pasta in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                try
                {
                    string candidato = Path.Combine(pasta.Trim(), "arduino-cli.exe");
                    if (pasta.Length > 0 && File.Exists(candidato)) return cli = candidato;
                }
                catch (ArgumentException) { }

            return File.Exists(CliBaixado) ? cli = CliBaixado : null;
        }
    }

    /// <summary>A configuracao da IDE: e nela que estao as URLs dos nucleos e as pastas.</summary>
    static string ConfigDaIde
    {
        get
        {
            string arquivo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                          ".arduinoIDE", "arduino-cli.yaml");
            return File.Exists(arquivo) ? arquivo : null;
        }
    }

    /// <summary>
    /// O FQBN da placa -- o nome que o compilador do Arduino usa para ela --,
    /// ou null quando nao ha nucleo do Arduino para ela.
    /// </summary>
    public static string FqbnDe(string idDaPlaca) => idDaPlaca switch
    {
        "arduino-uno" => "arduino:avr:uno",
        "arduino-nano" => "arduino:avr:nano",
        "arduino-mega" => "arduino:avr:mega",
        // A DOIT DevKit V1, e nao a "ESP32 Dev Module" generica: so a variante
        // dela define LED_BUILTIN (GPIO 2), que o sketch em branco usa.
        "esp32-devkit-v1" => "esp32:esp32:esp32doit-devkit-v1",
        "esp32-s2-devkit" => "esp32:esp32:esp32s2",
        "esp32-s3-devkit" => "esp32:esp32:esp32s3",
        "esp32-c3-devkit" => "esp32:esp32:esp32c3",
        "esp32-c6-devkit" => "esp32:esp32:esp32c6",
        "esp32-h2-devkit" => "esp32:esp32:esp32h2",
        "esp8266-nodemcu" => "esp8266:esp8266:nodemcuv2",
        "pi-pico" => "rp2040:rp2040:rpipico",
        "pi-pico-w" => "rp2040:rp2040:rpipicow",
        "nano-rp2040-connect" => "rp2040:rp2040:arduino_nano_connect",
        "stm32-bluepill" => "STMicroelectronics:stm32:GenF1:pnum=BLUEPILL_F103C8",
        "st-nucleo-l031k6" => "STMicroelectronics:stm32:Nucleo_32:pnum=NUCLEO_L031K6",
        "st-nucleo-c031c6" => "STMicroelectronics:stm32:Nucleo_64:pnum=NUCLEO_C031C6",
        "attiny85" => "ATTinyCore:avr:attinyx5:chip=85,clock=8internal",
        "franzininho" => "ATTinyCore:avr:attinyx5micr:chip=85",
        _ when idDaPlaca?.StartsWith("esp-", StringComparison.Ordinal) == true => "esp8266:esp8266:generic",
        _ => null,
    };

    /// <summary>"esp32:esp32:esp32s3" vira "esp32:esp32", o nucleo que precisa estar instalado.</summary>
    public static string NucleoDe(string fqbn)
    {
        if (fqbn is null) return null;
        var partes = fqbn.Split(':');
        return partes.Length >= 2 ? partes[0] + ":" + partes[1] : null;
    }

    /// <summary>De onde a IDE baixa cada nucleo que nao e da propria Arduino.</summary>
    public static string EnderecoDoNucleo(string nucleo) => nucleo switch
    {
        "esp32:esp32" => "https://espressif.github.io/arduino-esp32/package_esp32_index.json",
        "esp8266:esp8266" => "https://arduino.esp8266.com/stable/package_esp8266com_index.json",
        "rp2040:rp2040" => "https://github.com/earlephilhower/arduino-pico/releases/download/global/package_rp2040_index.json",
        "STMicroelectronics:stm32" => "https://github.com/stm32duino/BoardManagerFiles/raw/main/package_stmicroelectronics_index.json",
        "ATTinyCore:avr" => "https://drazzy.com/package_drazzy.com_index.json",
        _ => null,
    };

    // =================================================================
    //  consultas
    // =================================================================

    /// <summary>
    /// As portas seriais: as do Windows na hora (com o nome pelo VID/PID) e,
    /// com o arduino-cli, a placa que ele reconheceu em cada uma.
    /// </summary>
    public static List<Porta> Portas()
    {
        var doWindows = PortasDoWindows();
        if (Cli is null) return doWindows;

        var saida = RodarJson(["board", "list"], 30_000);
        if (saida is null) return doWindows;

        // O arduino-cli sabe a placa (com o FQBN); o Windows sabe as portas
        // que ele as vezes ainda nao listou. Fica a uniao, pela ordem do COM.
        var juntas = LerPortas(saida).ToDictionary(p => p.Endereco, StringComparer.OrdinalIgnoreCase);
        foreach (var p in doWindows)
            if (!juntas.TryGetValue(p.Endereco, out var doCli))
                juntas[p.Endereco] = p;
            else if (doCli.Placa is null && p.Placa is not null)
                juntas[p.Endereco] = doCli with { Placa = p.Placa };
        return [.. juntas.Values.OrderBy(p => NumeroDoCom(p.Endereco)).ThenBy(p => p.Endereco)];
    }

    static int NumeroDoCom(string endereco)
        => endereco?.StartsWith("COM", StringComparison.OrdinalIgnoreCase) == true
           && int.TryParse(endereco[3..], out int n) ? n : int.MaxValue;

    /// <summary>
    /// As portas COM que existem agora, como o Windows as lista
    /// (HARDWARE\DEVICEMAP\SERIALCOMM): muda quando uma placa é ligada ou
    /// tirada, e ler é instantâneo -- serve para vigiar a USB.
    /// </summary>
    public static string AssinaturaDasPortas()
    {
        try
        {
            using var chave = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (chave is null) return "";
            // As COM do Bluetooth (\Device\BthModem0) nao sao placa na USB.
            return string.Join(",", chave.GetValueNames()
                                         .Where(n => !n.Contains("BthModem", StringComparison.OrdinalIgnoreCase))
                                         .Select(n => chave.GetValue(n) as string)
                                         .Where(v => !string.IsNullOrEmpty(v)).OrderBy(v => v));
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return "";
        }
    }

    /// <summary>
    /// As portas COM com o VID/PID de quem está do outro lado, pelo registro
    /// (Enum\USB e Enum\FTDIBUS). Funciona sem a Arduino IDE: a lista de
    /// placas aparece mesmo antes de haver um compilador.
    /// </summary>
    public static List<Porta> PortasDoWindows()
    {
        var existentes = AssinaturaDasPortas().Split(',', StringSplitOptions.RemoveEmptyEntries)
                                             .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var vidPid = new Dictionary<string, (string Vid, string Pid)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (string barramento in new[] { "USB", "FTDIBUS" })
            {
                using var raiz = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + barramento);
                if (raiz is null) continue;
                foreach (string aparelho in raiz.GetSubKeyNames())
                {
                    var m = System.Text.RegularExpressions.Regex.Match(aparelho, @"VID_([0-9A-F]{4}).PID_([0-9A-F]{4})",
                                                                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    using var chaveDoAparelho = raiz.OpenSubKey(aparelho);
                    if (chaveDoAparelho is null) continue;
                    foreach (string instancia in chaveDoAparelho.GetSubKeyNames())
                    {
                        using var parametros = chaveDoAparelho.OpenSubKey(instancia + @"\Device Parameters");
                        if (parametros?.GetValue("PortName") is string com && existentes.Contains(com))
                            vidPid[com] = ("0x" + m.Groups[1].Value.ToUpperInvariant(), "0x" + m.Groups[2].Value.ToUpperInvariant());
                    }
                }
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }

        return [.. existentes.OrderBy(NumeroDoCom).ThenBy(c => c)
                             .Select(com => vidPid.TryGetValue(com, out var v)
                                 ? new Porta(com, "Serial Port (USB)", NomePeloUsb(v.Vid, v.Pid), null)
                                 : new Porta(com, "Serial Port", null, null))];
    }

    /// <summary>A placa (ou o conversor USB-serial) pelo VID/PID.</summary>
    public static string NomePeloUsb(string vid, string pid) => (vid?.ToUpperInvariant(), pid?.ToUpperInvariant()) switch
    {
        ("0X2341", "0X0043" or "0X0001" or "0X0243") => "Arduino Uno",
        ("0X2341", "0X0010" or "0X0042" or "0X0242") => "Arduino Mega 2560",
        ("0X2341", "0X005E" or "0X805E") => "Arduino Nano RP2040 Connect",
        ("0X2E8A", _) => "Raspberry Pi Pico",
        _ => ConversorDe(vid, pid),
    };

    /// <summary>
    /// Roda pedindo JSON: "--json" nos arduino-cli 1.x; os da IDE 2.0 (0.x)
    /// so conhecem "--format json".
    /// </summary>
    static string RodarJson(List<string> args, int prazoMs)
    {
        foreach (var jeito in new[] { new[] { "--json" }, new[] { "--format", "json" } })
        {
            string saida = Rodar([.. args, .. jeito], prazoMs, out _);
            if (saida?.TrimStart() is { } s && (s.StartsWith('{') || s.StartsWith('[')))
                return saida;
        }
        return null;
    }

    /// <summary>O JSON do "board list" -- separado para o teste ler um exemplo.</summary>
    public static List<Porta> LerPortas(string json)
    {
        var portas = new List<Porta>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var lista = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("detected_ports", out var dp) ? dp : default;
            if (lista.ValueKind != JsonValueKind.Array) return portas;

            foreach (var item in lista.EnumerateArray())
            {
                if (!item.TryGetProperty("port", out var porta)) continue;
                string protocolo = porta.TryGetProperty("protocol", out var p) ? p.GetString() : "";
                if (protocolo != "serial") continue;           // a porta "circuitsketch" do plugin nao e placa

                string endereco = porta.GetProperty("address").GetString();
                string rotulo = porta.TryGetProperty("protocol_label", out var r) ? r.GetString() : "";

                string placa = null, fqbn = null;
                if (item.TryGetProperty("matching_boards", out var placas) && placas.ValueKind == JsonValueKind.Array)
                    foreach (var b in placas.EnumerateArray())
                    {
                        placa = b.TryGetProperty("name", out var n) ? n.GetString() : null;
                        fqbn = b.TryGetProperty("fqbn", out var f) ? f.GetString() : null;
                        break;
                    }

                // Sem placa reconhecida: a maioria das ESP32 e NodeMCU chega
                // por um conversor USB-serial generico (CP2102, CH340).
                if (placa is null && porta.TryGetProperty("properties", out var props)
                    && props.TryGetProperty("vid", out var vid))
                    placa = NomePeloUsb(vid.GetString(), props.TryGetProperty("pid", out var pid) ? pid.GetString() : "");

                portas.Add(new Porta(endereco, rotulo, placa, fqbn));
            }
        }
        catch (JsonException) { }
        return portas;
    }

    /// <summary>O conversor USB-serial pelo VID/PID -- o que da para dizer de uma ESP32 generica.</summary>
    static string ConversorDe(string vid, string pid) => (vid?.ToUpperInvariant(), pid?.ToUpperInvariant()) switch
    {
        ("0X10C4", _) => Idioma.T("conversor CP210x (ESP32, NodeMCU...)"),
        ("0X1A86", _) => Idioma.T("conversor CH340 (ESP32, NodeMCU, Nano...)"),
        ("0X0403", _) => Idioma.T("conversor FTDI"),
        ("0X303A", _) => Idioma.T("ESP32 com USB nativo (S2, S3, C3...)"),
        ("0X2E8A", _) => "Raspberry Pi Pico",
        _ => null,
    };

    /// <summary>Os nucleos instalados na IDE ("esp32:esp32").</summary>
    public static HashSet<string> Nucleos()
    {
        var instalados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var saida = RodarJson(["core", "list"], 60_000);
        if (saida is null) return instalados;

        try
        {
            using var doc = JsonDocument.Parse(saida);
            var lista = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("platforms", out var p) ? p : default;
            if (lista.ValueKind == JsonValueKind.Array)
                foreach (var item in lista.EnumerateArray())
                    if (item.TryGetProperty("id", out var id)
                        && (!item.TryGetProperty("installed_version", out var v) || !string.IsNullOrEmpty(v.GetString())))
                        instalados.Add(id.GetString());
        }
        catch (JsonException) { }
        return instalados;
    }

    // =================================================================
    //  compilar e mandar
    // =================================================================

    /// <summary>
    /// Escreve o projeto numa pasta como a IDE espera: "nome/nome.ino", com
    /// as abas do lado. Devolve a pasta.
    /// </summary>
    public static string PrepararPasta(string nome, string sketch, IDictionary<string, string> abas)
    {
        string limpo = new([.. (string.IsNullOrWhiteSpace(nome) ? "sketch" : nome)
            .Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_')]);
        if (limpo.Length == 0 || char.IsDigit(limpo[0])) limpo = "sketch_" + limpo;

        string pasta = Path.Combine(Path.GetTempPath(), "circuitsketch-placa", limpo);
        if (Directory.Exists(pasta))
            foreach (string velho in Directory.GetFiles(pasta))
                try { File.Delete(velho); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Directory.CreateDirectory(pasta);

        File.WriteAllText(Path.Combine(pasta, limpo + ".ino"), sketch ?? "", new UTF8Encoding(false));
        if (abas is not null)
            foreach (var (arquivo, texto) in abas)
            {
                string nomeDoArquivo = Path.GetFileName(arquivo);
                if (string.IsNullOrWhiteSpace(nomeDoArquivo) || nomeDoArquivo.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;
                File.WriteAllText(Path.Combine(pasta, nomeDoArquivo), texto ?? "", new UTF8Encoding(false));
            }

        return pasta;
    }

    /// <summary>
    /// Compila (e manda, com uma porta) pelo arduino-cli, repassando cada
    /// linha que ele escreve. Pode demorar: a primeira compilacao da ESP32
    /// compila o nucleo inteiro.
    /// </summary>
    public static Resultado Compilar(string pasta, string fqbn, string porta, Action<string> linha,
                                     CancellationToken cancelar)
    {
        if (Cli is null)
            return new Resultado(false, Idioma.T("a Arduino IDE 2 nao esta instalada neste computador"), null);

        var args = new List<string> { "compile", "--fqbn", fqbn };
        if (Directory.Exists(Acervo.Pasta)) { args.Add("--libraries"); args.Add(Acervo.Pasta); }
        if (porta is not null) { args.Add("--upload"); args.Add("-p"); args.Add(porta); }
        args.Add(pasta);

        var texto = new StringBuilder();
        int codigo = RodarAoVivo(args, l => { texto.AppendLine(l); linha?.Invoke(l); }, cancelar);
        string tudo = texto.ToString();
        string memoria = Memoria(tudo);

        if (cancelar.IsCancellationRequested) return new Resultado(false, "cancelado", memoria);
        if (codigo == 0)
            return new Resultado(true, porta is null ? "compilou" : Idioma.T("mandou para a placa em {0}", porta), memoria);
        return new Resultado(false, Explicar(tudo, porta, fqbn), memoria);
    }

    /// <summary>"Sketch uses 2222 bytes (6%)..." e "Global variables use..."</summary>
    static string Memoria(string saida)
    {
        var linhas = saida.Replace("\r\n", "\n").Split('\n')
            .Where(l => l.Contains("bytes", StringComparison.Ordinal)
                        && (l.Contains("Sketch uses", StringComparison.Ordinal)
                            || l.Contains("Global variables", StringComparison.Ordinal)
                            || l.Contains("O sketch usa", StringComparison.Ordinal)
                            || l.Contains("Variáveis globais", StringComparison.Ordinal)))
            .Select(l => l.Trim());
        string junto = string.Join(" ", linhas);
        return junto.Length > 0 ? junto : null;
    }

    static string FaltaNaIde(string biblioteca, string cabecalho)
        => Idioma.T("falta a biblioteca \"{0}\" ({1}) na Arduino IDE. No simulador ela ja vem junto; para a placa, instale pelo Gerenciador de Bibliotecas da IDE (ou pelo botao ao lado).", biblioteca, cabecalho);

    /// <summary>
    /// A biblioteca que o <see cref="Explicar"/> disse faltar na IDE, ou null.
    /// Compara com a frase montada, e nao com um pedaço do texto: assim vale
    /// em qualquer idioma.
    /// </summary>
    public static string BibliotecaQueFalta(string resumo)
        => string.IsNullOrEmpty(resumo) ? null
           : Acervo.Todas().FirstOrDefault(b => resumo.Contains(FaltaNaIde(b.Nome, b.Cabecalho), StringComparison.Ordinal))?.Nome;

    /// <summary>O que deu errado, do jeito que a pessoa entende, a partir do que o compilador disse.</summary>
    public static string Explicar(string saida, string porta, string fqbn)
    {
        string s = saida ?? "";

        var semArquivo = System.Text.RegularExpressions.Regex.Match(s, @"fatal error: ([\w./\\-]+\.h(?:pp)?): No such file");
        if (semArquivo.Success)
        {
            string cabecalho = semArquivo.Groups[1].Value;
            string biblioteca = Acervo.Todas().FirstOrDefault(b => b.Cabecalho == cabecalho)?.Nome;
            return biblioteca is not null
                ? FaltaNaIde(biblioteca, cabecalho)
                : Idioma.T("o compilador nao achou {0}: a biblioteca dele nao esta instalada na Arduino IDE.", cabecalho);
        }

        if (s.Contains("Platform '", StringComparison.Ordinal) && s.Contains("not found", StringComparison.Ordinal)
            || s.Contains("platform not installed", StringComparison.OrdinalIgnoreCase))
            return Idioma.T("o nucleo {0} nao esta instalado na Arduino IDE (Ferramentas > Placa > Gerenciador de Placas).", NucleoDe(fqbn));

        if (porta is not null)
        {
            if (s.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
                || s.Contains("could not open port", StringComparison.OrdinalIgnoreCase)
                || s.Contains("PermissionError", StringComparison.Ordinal)
                || s.Contains("Acesso negado", StringComparison.OrdinalIgnoreCase))
                return Idioma.T("a porta {0} esta ocupada: feche o monitor serial da Arduino IDE (ou outro programa usando a porta) e tente de novo.", porta);

            if (s.Contains("Failed to connect to ESP32", StringComparison.Ordinal)
                || s.Contains("No serial data received", StringComparison.Ordinal)
                || s.Contains("Wrong boot mode", StringComparison.Ordinal))
                return Idioma.T("a ESP32 nao entrou no modo de gravacao: segure o botao BOOT da placa enquanto aparece \"Connecting...\" e solte quando comecar a gravar.");

            if (s.Contains("not in sync", StringComparison.Ordinal) || s.Contains("programmer is not responding", StringComparison.Ordinal))
                return Idioma.T("a placa em {0} nao respondeu ao gravador: confira se e mesmo um {1} (o Nano antigo usa o \"Old Bootloader\") e se a porta e a certa.", porta, fqbn);
        }

        var erro = System.Text.RegularExpressions.Regex.Match(s, @"\.ino:(\d+):(\d+): error: (.+)");
        if (erro.Success)
            return Idioma.T("linha {0}: {1}", erro.Groups[1].Value, erro.Groups[3].Value.Trim());

        var ultima = s.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim())
            .LastOrDefault(l => l.Length > 0 && (l.Contains("rror", StringComparison.Ordinal) || l.Contains("falh", StringComparison.OrdinalIgnoreCase)));
        return ultima ?? Idioma.T("o compilador do Arduino recusou (veja a saida completa na aba PLACA)");
    }

    /// <summary>Instala um nucleo pela IDE: "esp32:esp32".</summary>
    public static bool InstalarNucleo(string nucleo, Action<string> linha, CancellationToken cancelar)
    {
        var atualizar = new List<string> { "core", "update-index" };
        var args = new List<string> { "core", "install", nucleo };
        if (EnderecoDoNucleo(nucleo) is { } url)
        {
            atualizar.Add("--additional-urls"); atualizar.Add(url);
            args.Add("--additional-urls"); args.Add(url);
        }
        RodarAoVivo(atualizar, linha, cancelar);
        return RodarAoVivo(args, linha, cancelar) == 0;
    }

    /// <summary>Instala uma biblioteca pela IDE: "DHT sensor library".</summary>
    public static bool InstalarBiblioteca(string nome, Action<string> linha, CancellationToken cancelar)
        => RodarAoVivo(["lib", "install", nome], linha, cancelar) == 0;

    // =================================================================
    //  o processo
    // =================================================================

    static ProcessStartInfo Inicio(IEnumerable<string> args)
    {
        var inicio = new ProcessStartInfo(Cli)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string a in args) inicio.ArgumentList.Add(a);
        // Sem as cores do terminal: na aba PLACA elas viravam "[92m" no meio do texto.
        inicio.ArgumentList.Add("--no-color");
        if (ConfigDaIde is { } config)
        {
            inicio.ArgumentList.Add("--config-file");
            inicio.ArgumentList.Add(config);
        }
        return inicio;
    }

    /// <summary>
    /// Tira os códigos de cor do terminal ("\x1b[92m"): o gcc e as
    /// ferramentas das placas os escrevem mesmo com o --no-color.
    /// </summary>
    public static string SemCores(string texto)
        => texto is null || texto.IndexOf('\u001b') < 0 ? texto
           : System.Text.RegularExpressions.Regex.Replace(texto, @"\u001b\[[0-9;?]*[ -/]*[@-~]", "");

    /// <summary>Roda e devolve tudo o que ele escreveu, ou null se nao deu.</summary>
    static string Rodar(IEnumerable<string> args, int prazoMs, out string erro)
    {
        erro = null;
        if (Cli is null) { erro = Idioma.T("sem arduino-cli"); return null; }
        try
        {
            using var processo = Process.Start(Inicio(args));
            var saida = processo.StandardOutput.ReadToEndAsync();
            var falha = processo.StandardError.ReadToEndAsync();
            if (!processo.WaitForExit(prazoMs))
            {
                try { processo.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                erro = Idioma.T("o arduino-cli demorou demais");
                return null;
            }
            erro = falha.Result;
            return processo.ExitCode == 0 ? saida.Result : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            erro = e.Message;
            return null;
        }
    }

    /// <summary>Roda repassando cada linha na hora -- a compilacao da ESP32 leva minutos.</summary>
    static int RodarAoVivo(IEnumerable<string> args, Action<string> linha, CancellationToken cancelar)
    {
        if (Cli is null) return -1;
        try
        {
            using var processo = new Process { StartInfo = Inicio(args), EnableRaisingEvents = true };
            processo.OutputDataReceived += (_, e) => { if (e.Data is not null) linha?.Invoke(SemCores(e.Data)); };
            processo.ErrorDataReceived += (_, e) => { if (e.Data is not null) linha?.Invoke(SemCores(e.Data)); };
            processo.Start();
            processo.BeginOutputReadLine();
            processo.BeginErrorReadLine();

            while (!processo.WaitForExit(200))
            {
                if (!cancelar.IsCancellationRequested) continue;
                try { processo.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return -2;
            }
            processo.WaitForExit();
            return processo.ExitCode;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            linha?.Invoke(Idioma.T("nao consegui rodar o arduino-cli: {0}", e.Message));
            return -1;
        }
    }
}
