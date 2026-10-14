// ============================================================
//  Instalador (e desinstalador) do CircuitSketch
//
//  Por que um instalador próprio, e não Inno Setup, NSIS ou MSI:
//
//  - Instala **por usuário**, em %LOCALAPPDATA%\Programs, sem pedir
//    administrador. Nos computadores do laboratório o aluno não tem
//    senha de administrador, e um instalador que pede elevação não
//    passaria da primeira tela.
//  - Compila com o csc que já vem no SDK do .NET, contra o .NET
//    Framework 4.8 que já vem no Windows 10 e 11. Nada para baixar
//    para gerar, nada para instalar antes para rodar.
//
//  O mesmo fonte vira dois executáveis (ver gerar.ps1):
//
//  - com o recurso "pacote.zip" embutido: o instalador;
//  - sem ele: o desinstalador, que o instalador copia para a pasta
//    do programa e registra em Configurações > Aplicativos.
//
//  O desinstalador só apaga o que está anotado em desinstalar.txt,
//  escrito na instalação. Nunca apaga uma pasta inteira às cegas:
//  se alguém instalar em cima da Área de Trabalho, a desinstalação
//  tira os arquivos do CircuitSketch e deixa o resto onde estava.
//
//  Opções de linha de comando (as de teste não aparecem para o aluno):
//
//    /silencioso            sem janela; código de saída 0 = deu certo
//    /destino=<pasta>       onde instalar (ou de onde desinstalar); até 180 caracteres
//    /sketchbook=<pasta>    pasta de sketches da Arduino IDE
//    /sem-plugin /sem-atalhos /sem-registro
//    /desinstalar           força o modo desinstalador
//    /pasta-atalhos=<pasta> teste: atalhos numa pasta qualquer
//    /chave-registro=<chave> teste: outra chave em HKCU
//    /captura=<png> [/captura-pronto]  teste: foto da janela
//    /limite-caminho=<n>    teste: outro limite de caminho (padrão 180; o
//                           scratchpad de teste sozinho já passa disso)
// ============================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CircuitSketch.Instalacao;

static class Programa
{
    [STAThread]
    static int Main(string[] args)
    {
        var opcoes = Opcoes.Ler(args);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool temPacote = Assembly.GetExecutingAssembly().GetManifestResourceInfo("pacote.zip") != null;
        Diario.Anotar((temPacote ? "instalador " : "desinstalador ") + Versao.Completa
                      + " | " + string.Join(" ", args));

        try
        {
            if (temPacote && !opcoes.Desinstalar) return Instalador.Rodar(opcoes);
            return Desinstalador.Rodar(opcoes);
        }
        catch (Exception ex)
        {
            Diario.Anotar("falha: " + ex);
            if (!opcoes.Silencioso)
                MessageBox.Show("Algo deu errado:\n\n" + ex.Message + "\n\nDetalhes em " + Diario.Arquivo,
                    "CircuitSketch", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}

static class Produto
{
    public const string Exe = "CircuitSketch.exe";
    public const string Desinstalador = "Desinstalar CircuitSketch.exe";
    public const string Manifesto = "desinstalar.txt";
    public const string Chave = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CircuitSketch";

    public static string PastaPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CircuitSketch");
}

sealed class Opcoes
{
    public bool Silencioso, Desinstalar, SemAtalhos, SemRegistro, SemPlugin, CapturaPronto;
    public string Destino, Sketchbook, PastaAtalhos, Captura;
    public string ChaveRegistro = Produto.Chave;
    public int LimiteCaminho = Instalacao.MaiorPasta;

    public static Opcoes Ler(string[] args)
    {
        var o = new Opcoes();
        foreach (string a in args)
        {
            string nome = a, valor = null;
            int i = a.IndexOf('=');
            if (i > 0) { nome = a.Substring(0, i); valor = a.Substring(i + 1).Trim('"'); }

            switch (nome.TrimStart('/', '-').ToLowerInvariant())
            {
                case "silencioso": o.Silencioso = true; break;
                case "desinstalar": o.Desinstalar = true; break;
                case "sem-atalhos": o.SemAtalhos = true; break;
                case "sem-registro": o.SemRegistro = true; break;
                case "sem-plugin": o.SemPlugin = true; break;
                case "destino": o.Destino = valor; break;
                case "sketchbook": o.Sketchbook = valor; break;
                case "pasta-atalhos": o.PastaAtalhos = valor; break;
                case "chave-registro": o.ChaveRegistro = valor; break;
                case "captura": o.Captura = valor; break;
                case "captura-pronto": o.CapturaPronto = true; break;
                case "limite-caminho": if (int.TryParse(valor, out int limite)) o.LimiteCaminho = limite; break;
            }
        }
        return o;
    }
}

static class Diario
{
    public static readonly string Arquivo = Path.Combine(Path.GetTempPath(), "circuitsketch-instalador.log");

    public static void Anotar(string texto)
    {
        try
        {
            File.AppendAllText(Arquivo, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + texto
                               + Environment.NewLine, new UTF8Encoding(false));
        }
        catch { }
    }
}

sealed class Plano
{
    public string Destino;
    public bool AtalhoNaAreaDeTrabalho = true;
    public bool Plugin = true;
    public string Sketchbook;
}

sealed class Resultado
{
    public string Exe;
    public string PastaDoPlugin;
}

// ================= a Arduino IDE =================

static class Arduino
{
    /// <summary>
    /// Onde a IDE guarda os sketches. É dentro dessa pasta, em
    /// hardware\, que ela procura plataformas instaladas à mão.
    /// </summary>
    public static string AcharSketchbook()
    {
        // Arduino IDE 2: directories.user em ~\.arduinoIDE\arduino-cli.yaml.
        // Só existe se a pessoa trocou a pasta nas preferências.
        string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string achado = LerYaml(Path.Combine(perfil, ".arduinoIDE", "arduino-cli.yaml"));
        if (!string.IsNullOrEmpty(achado)) return achado;

        // Arduino IDE 1.x
        string pref = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arduino15", "preferences.txt");
        if (File.Exists(pref))
            foreach (string linha in File.ReadAllLines(pref))
                if (linha.StartsWith("sketchbook.path=", StringComparison.Ordinal))
                {
                    string v = linha.Substring(16).Trim();
                    if (v.Length > 0) return v;
                }

        // O padrão das duas: Documentos\Arduino. GetFolderPath já resolve
        // os Documentos que o OneDrive levou para outro lugar.
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Arduino");
    }

    static string LerYaml(string arquivo)
    {
        if (!File.Exists(arquivo)) return null;
        bool dentro = false;
        foreach (string bruta in File.ReadAllLines(arquivo))
        {
            if (bruta.Trim().Length == 0 || bruta.TrimStart().StartsWith("#")) continue;

            string linha = bruta.Trim();
            if (!char.IsWhiteSpace(bruta[0])) { dentro = linha.StartsWith("directories:"); continue; }
            if (!dentro || !linha.StartsWith("user:")) continue;

            string v = linha.Substring(5).Trim();
            if (v.StartsWith("\"")) v = v.Trim('"').Replace(@"\\", @"\");
            else v = v.Trim('\'');
            return v.Length > 0 ? v : null;
        }
        return null;
    }

    public static string PastaDoPlugin(string sketchbook)
        => Path.Combine(sketchbook, "hardware", "circuitsketch", "esp32");

    public static bool EhPluginNosso(string pasta)
    {
        string platform = Path.Combine(pasta, "platform.txt");
        return File.Exists(platform)
               && File.ReadAllText(platform).IndexOf("CircuitSketch", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static void ApagarPlugin(string pasta)
    {
        if (!EhPluginNosso(pasta))
            throw new IOException("A pasta " + pasta + " já existe, mas não é do CircuitSketch. Nada foi apagado.");
        Directory.Delete(pasta, true);
    }

    /// <summary>
    /// A instalação de quando o programa se chamava Simulador ESP32.
    /// Sem tirar, a IDE listaria as placas duas vezes.
    /// </summary>
    public static void RemoverAntigo(string sketchbook)
    {
        string antiga = Path.Combine(sketchbook, "hardware", "simulador");
        if (!Directory.Exists(antiga)) return;

        bool nossa = Directory.GetFiles(antiga, "platform.txt", SearchOption.AllDirectories).Any(f =>
        {
            string texto = File.ReadAllText(f);
            return texto.IndexOf("Simulador", StringComparison.OrdinalIgnoreCase) >= 0
                   || texto.IndexOf("CircuitSketch", StringComparison.OrdinalIgnoreCase) >= 0;
        });
        if (nossa) Directory.Delete(antiga, true);
    }

    public static bool IdeAberta()
    {
        foreach (string nome in new[] { "Arduino IDE", "arduino" })
        {
            var processos = Process.GetProcessesByName(nome);
            foreach (var p in processos) p.Dispose();
            if (processos.Length > 0) return true;
        }
        return false;
    }
}

// ================= instalar =================

static class Instalador
{
    public static int Rodar(Opcoes o)
    {
        if (!o.Silencioso)
        {
            Application.Run(new JanelaInstalador(o));
            return 0;
        }

        var plano = new Plano
        {
            Destino = Path.GetFullPath(o.Destino ?? Instalacao.PastaInstalada(o) ?? Produto.PastaPadrao),
            AtalhoNaAreaDeTrabalho = !o.SemAtalhos,
            Plugin = !o.SemPlugin,
            Sketchbook = o.Sketchbook ?? Arduino.AcharSketchbook(),
        };

        if (!Instalacao.CaminhoCabe(plano.Destino, o.LimiteCaminho, out string aviso))
        {
            Diario.Anotar(aviso.Replace('\n', ' '));
            return 5;
        }

        if (Instalacao.ProgramaAberto(plano.Destino))
        {
            Diario.Anotar("o CircuitSketch esta aberto nessa pasta");
            return 4;
        }

        Instalacao.Executar(plano, o, (_, _) => { });
        return 0;
    }
}

static class Instalacao
{
    sealed class Copia
    {
        public ZipArchiveEntry Entrada;
        public string Alvo;
        public string Relativo;     // nulo para os arquivos do plugin
    }

    public static string ExeEm(string pasta) => Path.Combine(pasta, Produto.Exe);

    /// <summary>
    /// Caminho comprido quebra a instalação sem mensagem nenhuma: passando
    /// de 260 caracteres, o .NET Framework não acha o .config do
    /// desinstalador e o WPF não carrega as DLLs nativas. Medido: pasta com
    /// 224 caracteres quebra, com 198 funciona. O limite fica com folga.
    /// </summary>
    public const int MaiorPasta = 180;

    public static bool CaminhoCabe(string pasta, int limite, out string aviso)
    {
        aviso = null;
        if (pasta.Length <= limite) return true;
        aviso = "O caminho da pasta de instalação é comprido demais ("
                + pasta.Length + " caracteres; o máximo é " + limite + ").\n\n"
                + "Escolha uma pasta mais curta, como a padrão:\n" + Produto.PastaPadrao;
        return false;
    }

    /// <summary>
    /// O CircuitSketch aberto trava o próprio executável: copiar por cima
    /// falharia no meio. Só conta o que roda da pasta de destino -- outra
    /// cópia aberta em outro lugar não atrapalha.
    /// </summary>
    public static bool ProgramaAberto(string pasta)
    {
        string alvo = Path.GetFullPath(ExeEm(pasta));
        foreach (var p in Process.GetProcessesByName("CircuitSketch"))
        {
            try
            {
                if (string.Equals(p.MainModule.FileName, alvo, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { return true; }      // sem acesso ao caminho: melhor pedir para fechar
            finally { p.Dispose(); }
        }
        return false;
    }

    public static string PastaInstalada(Opcoes o)
    {
        if (o.SemRegistro) return null;
        try
        {
            using var chave = Registry.CurrentUser.OpenSubKey(o.ChaveRegistro);
            string pasta = chave?.GetValue("InstallLocation") as string;
            return pasta != null && File.Exists(ExeEm(pasta)) ? pasta : null;
        }
        catch { return null; }
    }

    public static Resultado Executar(Plano plano, Opcoes o, Action<double, string> progresso)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string destino = plano.Destino;
        Directory.CreateDirectory(destino);
        Diario.Anotar("destino: " + destino + (plano.Plugin ? " | sketchbook: " + plano.Sketchbook : " | sem plugin"));

        var manifesto = new Manifesto(Path.Combine(destino, Produto.Manifesto));
        string pastaDoPlugin = plano.Plugin ? Arduino.PastaDoPlugin(plano.Sketchbook) : null;

        using var pacote = assembly.GetManifestResourceStream("pacote.zip");
        using var zip = new ZipArchive(pacote, ZipArchiveMode.Read);

        var copias = new List<Copia>();
        foreach (var e in zip.Entries)
        {
            // O ZipFile do .NET Framework antigo grava "\" nos nomes.
            string nome = e.FullName.Replace('\\', '/');
            if (nome.EndsWith("/")) continue;

            if (nome.StartsWith("app/", StringComparison.Ordinal))
                copias.Add(new Copia { Entrada = e, Alvo = Dentro(destino, nome.Substring(4)), Relativo = nome.Substring(4) });
            else if (nome.StartsWith("plugin/", StringComparison.Ordinal) && pastaDoPlugin != null)
                copias.Add(new Copia { Entrada = e, Alvo = Dentro(pastaDoPlugin, nome.Substring(7)) });
        }

        long total = Math.Max(1, copias.Sum(c => c.Entrada.Length)), feito = 0;
        int ultimo = -1;
        void Avancou(long bytes, string texto)
        {
            feito += bytes;
            int porMil = (int)(feito * 900 / total);      // os 10% finais são atalhos e registro
            if (porMil == ultimo) return;
            ultimo = porMil;
            progresso(porMil / 1000.0, texto);
        }

        // ---- 1. o programa ----
        const string CopiandoPrograma = "Copiando o CircuitSketch...";
        progresso(0, CopiandoPrograma);
        foreach (var c in copias.Where(c => c.Relativo != null))
        {
            Extrair(c.Entrada, c.Alvo, b => Avancou(b, CopiandoPrograma));
            manifesto.Arquivo(c.Relativo.Replace('/', '\\'));
        }

        string desinstalador = Path.Combine(destino, Produto.Desinstalador);
        using (var origem = assembly.GetManifestResourceStream("desinstalador.exe"))
        using (var saida = File.Create(desinstalador))
            origem.CopyTo(saida);
        manifesto.Arquivo(Produto.Desinstalador);

        string exe = ExeEm(destino);
        var resultado = new Resultado { Exe = exe };

        // ---- 2. o plugin da Arduino IDE ----
        if (pastaDoPlugin != null)
        {
            const string CopiandoPlugin = "Instalando o plugin da Arduino IDE...";
            progresso(feito * 0.9 / total, CopiandoPlugin);

            Arduino.RemoverAntigo(plano.Sketchbook);
            if (Directory.Exists(pastaDoPlugin)) Arduino.ApagarPlugin(pastaDoPlugin);

            foreach (var c in copias.Where(c => c.Relativo == null))
                Extrair(c.Entrada, c.Alvo, b => Avancou(b, CopiandoPlugin));

            // É por este arquivo que o plugin acha o programa. UTF-8 com BOM:
            // o PowerShell 5 lê arquivo sem BOM como ANSI, e um "Usuários"
            // no caminho viraria lixo.
            File.WriteAllText(Path.Combine(pastaDoPlugin, "tools", "caminho.txt"), exe, new UTF8Encoding(true));
            manifesto.Plugin(pastaDoPlugin);
            resultado.PastaDoPlugin = pastaDoPlugin;
        }

        // ---- 3. atalhos ----
        if (!o.SemAtalhos)
        {
            progresso(0.93, "Criando os atalhos...");
            string menu = o.PastaAtalhos != null
                ? Path.Combine(o.PastaAtalhos, "menu")
                : Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            manifesto.Atalho(Atalhos.Criar(menu, exe));

            if (plano.AtalhoNaAreaDeTrabalho)
            {
                string mesa = o.PastaAtalhos != null
                    ? Path.Combine(o.PastaAtalhos, "area-de-trabalho")
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                manifesto.Atalho(Atalhos.Criar(mesa, exe));
            }
        }

        // ---- 4. Configurações > Aplicativos ----
        if (!o.SemRegistro)
        {
            progresso(0.96, "Registrando em Aplicativos instalados...");
            long kb = new DirectoryInfo(destino).GetFiles().Sum(f => f.Length) / 1024;
            using (var chave = Registry.CurrentUser.CreateSubKey(o.ChaveRegistro))
            {
                chave.SetValue("DisplayName", "CircuitSketch");
                chave.SetValue("DisplayVersion", Versao.Completa);
                chave.SetValue("Publisher", "CircuitSketch");
                chave.SetValue("Comments", "Simulador offline de ESP32 e Arduino");
                chave.SetValue("DisplayIcon", exe);
                chave.SetValue("InstallLocation", destino);
                chave.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                chave.SetValue("UninstallString", "\"" + desinstalador + "\"");
                chave.SetValue("QuietUninstallString", "\"" + desinstalador + "\" /silencioso");
                chave.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, kb), RegistryValueKind.DWord);
                chave.SetValue("NoModify", 1, RegistryValueKind.DWord);
                chave.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            manifesto.Registro(o.ChaveRegistro);
        }

        manifesto.Gravar();
        progresso(1, "Pronto.");
        Diario.Anotar("instalado: " + exe + (pastaDoPlugin != null ? " | plugin: " + pastaDoPlugin : ""));
        return resultado;
    }

    /// <summary>Caminho dentro da pasta, sem deixar um ".." sair dela.</summary>
    public static string Dentro(string pasta, string relativo)
    {
        string raiz = Path.GetFullPath(pasta).TrimEnd('\\') + "\\";
        string alvo = Path.GetFullPath(Path.Combine(raiz, relativo.Replace('/', '\\')));
        if (!alvo.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("caminho fora da pasta: " + relativo);
        return alvo;
    }

    static void Extrair(ZipArchiveEntry entrada, string alvo, Action<long> avancou)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(alvo));
        using var origem = entrada.Open();
        using var saida = new FileStream(alvo, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[1 << 16];
        int lidos;
        while ((lidos = origem.Read(buffer, 0, buffer.Length)) > 0)
        {
            saida.Write(buffer, 0, lidos);
            avancou(lidos);
        }
    }
}

/// <summary>
/// O que a instalação pôs no computador, uma coisa por linha. É a única
/// fonte do desinstalador.
/// </summary>
sealed class Manifesto
{
    readonly string arquivo;
    readonly List<string> linhas = new();

    public Manifesto(string arquivo)
    {
        this.arquivo = arquivo;

        // Instalar por cima junta com o que já estava: o desinstalador da
        // versão nova também precisa tirar o que a antiga pôs.
        foreach (var (tipo, valor) in Ler(arquivo)) Adicionar(tipo + "=" + valor);
    }

    void Adicionar(string linha)
    {
        if (!linhas.Contains(linha, StringComparer.OrdinalIgnoreCase)) linhas.Add(linha);
    }

    public void Arquivo(string relativo) => Adicionar("arquivo=" + relativo);
    public void Plugin(string pasta) => Adicionar("plugin=" + pasta);
    public void Atalho(string lnk) => Adicionar("atalho=" + lnk);
    public void Registro(string chave) => Adicionar("registro=" + chave);

    public void Gravar()
    {
        var tudo = new List<string> { "# CircuitSketch " + Versao.Completa + " - o desinstalador le este arquivo. Nao apague." };
        tudo.AddRange(linhas);
        File.WriteAllLines(arquivo, tudo, new UTF8Encoding(true));
    }

    public static List<(string Tipo, string Valor)> Ler(string arquivo)
    {
        var itens = new List<(string, string)>();
        if (!File.Exists(arquivo)) return itens;
        foreach (string linha in File.ReadAllLines(arquivo))
        {
            int i = linha.IndexOf('=');
            if (linha.StartsWith("#") || i <= 0) continue;
            itens.Add((linha.Substring(0, i).Trim(), linha.Substring(i + 1).Trim()));
        }
        return itens;
    }
}

static class Atalhos
{
    /// <summary>
    /// Atalho .lnk pelo WScript.Shell, chamado por reflexão: assim não
    /// precisa de referência a biblioteca COM nenhuma para compilar.
    /// </summary>
    public static string Criar(string pasta, string exe)
    {
        Directory.CreateDirectory(pasta);
        string lnk = Path.Combine(pasta, "CircuitSketch.lnk");

        Type tipo = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(tipo);
        try
        {
            object atalho = tipo.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            try
            {
                Definir(atalho, "TargetPath", exe);
                Definir(atalho, "WorkingDirectory", Path.GetDirectoryName(exe));
                Definir(atalho, "Description", "Simulador offline de ESP32 e Arduino");
                Definir(atalho, "IconLocation", exe + ",0");
                atalho.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, atalho, null);
            }
            finally { Marshal.FinalReleaseComObject(atalho); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        return lnk;
    }

    static void Definir(object alvo, string propriedade, object valor)
        => alvo.GetType().InvokeMember(propriedade, BindingFlags.SetProperty, null, alvo, new[] { valor });
}

// ================= desinstalar =================

static class Desinstalador
{
    const string Titulo = "Desinstalar CircuitSketch";

    public static int Rodar(Opcoes o)
    {
        string eu = Assembly.GetExecutingAssembly().Location;
        string pasta = Path.GetFullPath(o.Destino ?? Path.GetDirectoryName(eu));
        string manifesto = Path.Combine(pasta, Produto.Manifesto);

        if (!File.Exists(manifesto))
        {
            if (!o.Silencioso)
                MessageBox.Show("Não achei uma instalação do CircuitSketch em:\n" + pasta,
                    Titulo, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 2;
        }

        if (!o.Silencioso && MessageBox.Show(
                "Remover o CircuitSketch deste computador?\n\n"
                + "Saem o programa, os atalhos e o plugin da Arduino IDE. "
                + "Os circuitos e sketches que você salvou não são apagados.",
                Titulo, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return 3;

        while (Instalacao.ProgramaAberto(pasta))
        {
            if (o.Silencioso) return 4;
            if (MessageBox.Show("Feche o CircuitSketch para continuar.", Titulo,
                    MessageBoxButtons.RetryCancel, MessageBoxIcon.Information) != DialogResult.Retry)
                return 4;
        }

        foreach (var (tipo, valor) in Manifesto.Ler(manifesto))
        {
            try
            {
                switch (tipo)
                {
                    case "atalho":
                        if (valor.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) File.Delete(valor);
                        break;

                    case "plugin":
                        // Só se ainda for o nosso: a pasta pode ter sido trocada depois.
                        if (Directory.Exists(valor) && Arduino.EhPluginNosso(valor))
                        {
                            Directory.Delete(valor, true);
                            ApagarSeVazia(Path.GetDirectoryName(valor));      // hardware\circuitsketch
                        }
                        break;

                    case "registro":
                        if (valor.IndexOf("CircuitSketch", StringComparison.OrdinalIgnoreCase) >= 0)
                            Registry.CurrentUser.DeleteSubKeyTree(valor, false);
                        break;

                    case "arquivo":
                        string arquivo = Instalacao.Dentro(pasta, valor);
                        if (!string.Equals(arquivo, eu, StringComparison.OrdinalIgnoreCase)) File.Delete(arquivo);
                        break;
                }
            }
            catch (Exception ex) { Diario.Anotar("nao consegui tirar " + tipo + "=" + valor + ": " + ex.Message); }
        }
        File.Delete(manifesto);

        // As subpastas do programa (nativo\, bibliotecas\, licencas\) ficam
        // vazias sem os arquivos: saem de baixo para cima. Pasta com algo que
        // nao e nosso continua onde esta.
        try
        {
            foreach (string sub in Directory.GetDirectories(pasta, "*", SearchOption.AllDirectories)
                                            .OrderByDescending(d => d.Length))
                ApagarSeVazia(sub);
        }
        catch (Exception ex) { Diario.Anotar("nao consegui olhar as subpastas: " + ex.Message); }

        if (string.Equals(Path.GetDirectoryName(eu), pasta, StringComparison.OrdinalIgnoreCase))
        {
            // Um executável não se apaga enquanto roda. Quem apaga é um cmd
            // que espera este processo sair; o "rd" sem /s só tira a pasta se
            // ela tiver ficado vazia.
            Process.Start(new ProcessStartInfo("cmd.exe",
                "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + eu + "\" & rd \"" + pasta + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        else ApagarSeVazia(pasta);

        Diario.Anotar("desinstalado: " + pasta);
        if (!o.Silencioso)
            MessageBox.Show("O CircuitSketch foi removido.", Titulo, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    static void ApagarSeVazia(string pasta)
    {
        try
        {
            if (Directory.Exists(pasta) && !Directory.EnumerateFileSystemEntries(pasta).Any())
                Directory.Delete(pasta);
        }
        catch { }
    }
}

// ================= a janela =================

sealed class JanelaInstalador : Form
{
    static readonly Color Escuro = Color.FromArgb(0x1B, 0x1F, 0x24);
    static readonly Color Azul = Color.FromArgb(0x16, 0x68, 0xC1);
    static readonly Color Verde = Color.FromArgb(0x2F, 0xA8, 0x4F);
    static readonly Color Vermelho = Color.FromArgb(0xC0, 0x39, 0x2B);
    static readonly Color Cinza = Color.FromArgb(0x6B, 0x72, 0x7A);

    readonly Opcoes opcoes;
    readonly TextBox txtDestino, txtSketchbook;
    readonly Button btDestino, btSketchbook, btPrincipal, btSecundario;
    readonly CheckBox chkAtalho, chkPlugin;
    readonly Label lblSketchbook, lblNota, lblStatus;
    readonly ProgressBar barra;
    Resultado resultado;
    bool instalando;

    public JanelaInstalador(Opcoes opcoes)
    {
        this.opcoes = opcoes;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        Text = "Instalar CircuitSketch " + Versao.Curta;
        ClientSize = new Size(560, 444);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        // ---- topo ----
        var topo = new Panel { Location = new Point(0, 0), Size = new Size(560, 96), BackColor = Escuro };
        topo.Controls.Add(new PictureBox
        {
            Image = Logo(), SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(22, 16), Size = new Size(64, 64),
        });
        topo.Controls.Add(new Label
        {
            Text = "CircuitSketch", Font = new Font("Segoe UI Semibold", 18F),
            ForeColor = Color.White, AutoSize = true, Location = new Point(98, 15),
        });
        topo.Controls.Add(new Label
        {
            Text = "Versão " + Versao.Curta + "   ·   simulador offline de ESP32 e Arduino",
            ForeColor = Color.FromArgb(0xAE, 0xB6, 0xBF), AutoSize = true, Location = new Point(101, 56),
        });
        Controls.Add(topo);

        // ---- onde ----
        Controls.Add(new Label { Text = "Instalar em:", AutoSize = true, Location = new Point(24, 114) });
        txtDestino = new TextBox { Location = new Point(24, 136), Size = new Size(410, 25) };
        btDestino = Botao("Procurar...", new Point(442, 134), new Size(94, 28));
        btDestino.Click += (_, _) => EscolherDestino();

        chkAtalho = new CheckBox
        {
            Text = "Criar atalho na Área de Trabalho", Checked = !opcoes.SemAtalhos,
            AutoSize = true, Location = new Point(24, 176),
        };

        // ---- plugin ----
        chkPlugin = new CheckBox
        {
            Text = "Instalar o plugin da Arduino IDE (as placas CircuitSketch dentro da IDE)",
            Checked = !opcoes.SemPlugin, AutoSize = true, Location = new Point(24, 204),
        };
        chkPlugin.CheckedChanged += (_, _) => AtualizarPlugin();

        lblSketchbook = new Label
        {
            Text = "Pasta de sketches da Arduino IDE:", AutoSize = true,
            ForeColor = Cinza, Location = new Point(42, 230),
        };
        txtSketchbook = new TextBox { Location = new Point(42, 252), Size = new Size(392, 25) };
        btSketchbook = Botao("Procurar...", new Point(442, 250), new Size(94, 28));
        btSketchbook.Click += (_, _) => EscolherSketchbook();
        lblNota = new Label
        {
            Text = "O plugin vai em hardware\\circuitsketch dessa pasta. Feche a Arduino IDE antes:\n"
                   + "as placas aparecem quando ela for aberta de novo.",
            AutoSize = false, ForeColor = Cinza, Location = new Point(42, 282), Size = new Size(494, 36),
        };

        // ---- andamento ----
        barra = new ProgressBar { Location = new Point(24, 334), Size = new Size(512, 14), Maximum = 1000, Visible = false };
        lblStatus = new Label { Location = new Point(24, 354), Size = new Size(512, 38), AutoSize = false, ForeColor = Cinza };

        btPrincipal = Botao("Instalar", new Point(282, 400), new Size(150, 32));
        btPrincipal.UseVisualStyleBackColor = false;
        btPrincipal.FlatStyle = FlatStyle.Flat;
        btPrincipal.FlatAppearance.BorderSize = 0;
        btPrincipal.BackColor = Azul;
        btPrincipal.ForeColor = Color.White;
        btPrincipal.Click += (_, _) => Principal();

        btSecundario = Botao("Cancelar", new Point(442, 400), new Size(94, 32));
        btSecundario.Click += (_, _) => Close();
        AcceptButton = btPrincipal;
        CancelButton = btSecundario;

        Controls.AddRange(new Control[]
        {
            txtDestino, btDestino, chkAtalho, chkPlugin, lblSketchbook, txtSketchbook,
            btSketchbook, lblNota, barra, lblStatus, btPrincipal, btSecundario,
        });

        // ---- valores ----
        string anterior = Instalacao.PastaInstalada(opcoes);
        txtDestino.Text = opcoes.Destino ?? anterior ?? Produto.PastaPadrao;
        txtSketchbook.Text = opcoes.Sketchbook ?? Arduino.AcharSketchbook();
        if (anterior != null && opcoes.Destino == null)
        {
            lblStatus.Text = "O CircuitSketch já está instalado nessa pasta. Ele será atualizado para a versão "
                             + Versao.Curta + ".";
            btPrincipal.Text = "Atualizar";
        }
        AtualizarPlugin();

        // Foco no botão, e não no campo: a janela abriria com o caminho
        // todo selecionado, pronto para ser apagado com uma tecla.
        ActiveControl = btPrincipal;

        ResumeLayout(false);
        PerformLayout();

        FormClosing += (_, e) => { if (instalando) e.Cancel = true; };
        Shown += (_, _) => { if (opcoes.Captura != null) BeginInvoke((Action)Capturar); };
    }

    static Button Botao(string texto, Point onde, Size tamanho)
        => new() { Text = texto, Location = onde, Size = tamanho, UseVisualStyleBackColor = true };

    static Image Logo()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png");
        if (s == null) return null;
        using var imagem = Image.FromStream(s);
        return new Bitmap(imagem);
    }

    void AtualizarPlugin()
    {
        bool ligado = chkPlugin.Checked && chkPlugin.Enabled;
        lblSketchbook.Enabled = txtSketchbook.Enabled = btSketchbook.Enabled = lblNota.Enabled = ligado;
    }

    void EscolherDestino()
    {
        using var janela = new FolderBrowserDialog
        {
            Description = "Onde instalar? Uma pasta CircuitSketch é criada dentro da pasta escolhida.",
            SelectedPath = ExistenteMaisProxima(txtDestino.Text),
        };
        if (janela.ShowDialog(this) != DialogResult.OK) return;

        // Nunca solto numa pasta que já tem outras coisas, como a Área de
        // Trabalho: o programa sempre ganha uma pasta só dele.
        string pasta = janela.SelectedPath;
        if (!string.Equals(Path.GetFileName(pasta.TrimEnd('\\')), "CircuitSketch", StringComparison.OrdinalIgnoreCase))
            pasta = Path.Combine(pasta, "CircuitSketch");
        txtDestino.Text = pasta;
    }

    void EscolherSketchbook()
    {
        using var janela = new FolderBrowserDialog
        {
            Description = "A pasta de sketches da Arduino IDE (Arquivo > Preferências > Local do Sketchbook).",
            SelectedPath = ExistenteMaisProxima(txtSketchbook.Text),
        };
        if (janela.ShowDialog(this) == DialogResult.OK) txtSketchbook.Text = janela.SelectedPath;
    }

    static string ExistenteMaisProxima(string caminho)
    {
        try
        {
            for (string p = caminho; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
                if (Directory.Exists(p)) return p;
        }
        catch { }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    void Principal()
    {
        if (resultado != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(resultado.Exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(resultado.Exe),
                });
            }
            catch (Exception ex) { Diario.Anotar("nao abriu: " + ex.Message); }
            Close();
            return;
        }

        string textoDestino = txtDestino.Text.Trim();
        string sketchbook = txtSketchbook.Text.Trim();
        if (textoDestino.Length == 0 || !Path.IsPathRooted(textoDestino))
        {
            MessageBox.Show(this, "Escolha a pasta onde instalar.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (chkPlugin.Checked && (sketchbook.Length == 0 || !Path.IsPathRooted(sketchbook)))
        {
            MessageBox.Show(this, "Escolha a pasta de sketches da Arduino IDE, ou desmarque o plugin.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string destino = Path.GetFullPath(textoDestino);
        if (!Instalacao.CaminhoCabe(destino, opcoes.LimiteCaminho, out string aviso))
        {
            MessageBox.Show(this, aviso, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        while (Instalacao.ProgramaAberto(destino))
            if (MessageBox.Show(this, "O CircuitSketch está aberto. Feche-o para continuar.", Text,
                    MessageBoxButtons.RetryCancel, MessageBoxIcon.Information) != DialogResult.Retry)
                return;

        if (chkPlugin.Checked && Arduino.IdeAberta() && MessageBox.Show(this,
                "A Arduino IDE está aberta.\n\nO plugin é instalado mesmo assim, mas as placas só aparecem "
                + "depois de fechar a IDE e abrir de novo.",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            return;

        var plano = new Plano
        {
            Destino = destino,
            AtalhoNaAreaDeTrabalho = chkAtalho.Checked,
            Plugin = chkPlugin.Checked,
            Sketchbook = sketchbook,
        };

        instalando = true;
        Travar(true);
        barra.Value = 0;
        barra.Visible = true;
        lblStatus.ForeColor = Color.Black;

        var trabalho = new Thread(() =>
        {
            try
            {
                var r = Instalacao.Executar(plano, opcoes, (fracao, texto) => BeginInvoke((Action)(() =>
                {
                    barra.Value = Math.Max(0, Math.Min(1000, (int)(fracao * 1000)));
                    lblStatus.Text = texto;
                })));
                BeginInvoke((Action)(() => Terminou(r, null)));
            }
            catch (Exception ex)
            {
                Diario.Anotar("falha: " + ex);
                BeginInvoke((Action)(() => Terminou(null, ex)));
            }
        });
        // STA: os atalhos passam por COM.
        trabalho.SetApartmentState(ApartmentState.STA);
        trabalho.IsBackground = true;
        trabalho.Start();
    }

    void Travar(bool travado)
    {
        foreach (var c in new Control[] { txtDestino, btDestino, chkAtalho, chkPlugin, btPrincipal, btSecundario })
            c.Enabled = !travado;
        AtualizarPlugin();
    }

    void Terminou(Resultado r, Exception erro)
    {
        instalando = false;

        if (erro != null)
        {
            Travar(false);
            barra.Visible = false;
            lblStatus.ForeColor = Vermelho;
            lblStatus.Text = "Não deu para instalar: " + erro.Message;
            MessageBox.Show(this, "Não deu para instalar:\n\n" + erro.Message + "\n\nDetalhes em " + Diario.Arquivo,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        resultado = r;
        barra.Value = 1000;
        lblStatus.ForeColor = Verde;
        lblStatus.Text = r.PastaDoPlugin != null
            ? "Pronto! O CircuitSketch " + Versao.Curta + " foi instalado.\nNa Arduino IDE, escolha Ferramentas > Placa > CircuitSketch."
            : "Pronto! O CircuitSketch " + Versao.Curta + " foi instalado.";

        btPrincipal.Text = "Abrir o CircuitSketch";
        btPrincipal.BackColor = Verde;
        btPrincipal.Enabled = true;
        btSecundario.Text = "Concluir";
        btSecundario.Enabled = true;
        chkAtalho.Enabled = chkPlugin.Enabled = btDestino.Enabled = txtDestino.Enabled = false;
        AtualizarPlugin();
    }

    void Capturar()
    {
        if (opcoes.CapturaPronto)
        {
            Travar(true);
            barra.Visible = true;
            Terminou(new Resultado { Exe = Instalacao.ExeEm(txtDestino.Text), PastaDoPlugin = txtSketchbook.Text }, null);
        }
        Refresh();
        using var imagem = new Bitmap(Width, Height);
        DrawToBitmap(imagem, new Rectangle(0, 0, Width, Height));
        imagem.Save(opcoes.Captura, System.Drawing.Imaging.ImageFormat.Png);
        Close();
    }
}
