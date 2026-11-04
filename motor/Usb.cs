using System.Text;

namespace Simulador;

/// <summary>
/// O USB da placa (a S2 e a S3, pelo TinyUSB do core): o que o computador da
/// bancada recebe quando a placa vira teclado, mouse, controle ou MIDI. Os
/// relatorios HID chegam como na placa (o mesmo byte a byte que o
/// USBHIDKeyboard monta) e viram o que o computador faria com eles: o texto
/// digitado (no teclado americano, o padrao do HID), o mouse andando e
/// clicando, as notas MIDI. Nada disso mexe no computador de verdade -- fica
/// no registro da placa (o painel Rede, "usb") e aqui, para quem quiser ler.
/// </summary>
public sealed class UsbDaPlaca(Maquina dona)
{
    readonly object trava = new();
    readonly StringBuilder digitado = new();
    readonly List<string> eventos = [];
    readonly HashSet<byte> teclasApertadas = [];
    readonly StringBuilder linha = new();

    /// <summary>A placa apareceu no computador (o USB.begin()), com as interfaces que ela tem (HID, CDC, MIDI...).</summary>
    public bool Conectada { get; private set; }
    public string Interfaces { get; private set; } = "";

    /// <summary>Tudo o que o teclado USB da placa digitou.</summary>
    public string Digitado { get { lock (trava) return digitado.ToString(); } }

    /// <summary>Onde o mouse USB da placa levou o ponteiro (a partir de 0,0) e os cliques.</summary>
    public int MouseX { get; private set; }
    public int MouseY { get; private set; }
    public int Cliques { get; private set; }

    /// <summary>O que chegou, um por linha ("hid teclado ...", "midi nota ...").</summary>
    public List<string> Eventos { get { lock (trava) return [.. eventos]; } }

    void Anotar(string texto)
    {
        lock (trava)
        {
            eventos.Add(texto);
            if (eventos.Count > 500) eventos.RemoveRange(0, eventos.Count - 500);
        }
        dona.Rede?.Anotar(dona, "usb", texto);
    }

    public void Conectar(string interfaces)
    {
        Conectada = true;
        Interfaces = interfaces ?? "";
        Anotar($"a placa apareceu no computador pelo USB ({(Interfaces.Length > 0 ? Interfaces : "sem interfaces")})");
    }

    /// <summary>Um relatorio HID que a placa mandou: o protocolo (1 teclado, 2 mouse, 0 o resto) e os bytes.</summary>
    public void RelatorioHid(int protocolo, int id, byte[] dados)
    {
        if (protocolo == 1 || (protocolo == 0 && dados.Length == 8 && id is 0 or 1)) Teclado(dados);
        else if (protocolo == 2 || (protocolo == 0 && id == 2 && dados.Length >= 3)) Mouse(dados);
        else Anotar($"hid relatorio {id}: {Convert.ToHexString(dados)}");
    }

    /// <summary>O relatorio do teclado: o modificador, um byte reservado e ate 6 teclas.</summary>
    void Teclado(byte[] r)
    {
        if (r.Length < 3) return;
        byte mod = r[0];
        bool shift = (mod & 0x22) != 0;
        var agora = r.Skip(2).Where(k => k != 0).ToHashSet();
        foreach (byte k in agora.Where(k => !teclasApertadas.Contains(k)))
        {
            char? c = Caractere(k, shift);
            lock (trava)
            {
                if (c is '\b')
                {
                    if (digitado.Length > 0) digitado.Length--;
                    if (linha.Length > 0) linha.Length--;
                }
                else if (c is { } ch)
                {
                    digitado.Append(ch);
                    if (ch == '\n')
                    {
                        string feita = linha.ToString();
                        linha.Clear();
                        Anotar($"teclado USB digitou: {feita}");
                    }
                    else linha.Append(ch);
                }
            }
        }
        teclasApertadas.Clear();
        teclasApertadas.UnionWith(agora);
    }

    /// <summary>O uso HID da tecla no teclado americano (o que o computador entende sem trocar o layout).</summary>
    static char? Caractere(byte uso, bool shift)
    {
        if (uso is >= 0x04 and <= 0x1D) return (char)((shift ? 'A' : 'a') + uso - 0x04);
        if (uso is >= 0x1E and <= 0x27) return (shift ? "!@#$%^&*()" : "1234567890")[uso - 0x1E];
        return uso switch
        {
            0x28 or 0x58 => '\n',
            0x2A => '\b',
            0x2B => '\t',
            0x2C => ' ',
            0x2D => shift ? '_' : '-',
            0x2E => shift ? '+' : '=',
            0x2F => shift ? '{' : '[',
            0x30 => shift ? '}' : ']',
            0x31 => shift ? '|' : '\\',
            0x33 => shift ? ':' : ';',
            0x34 => shift ? '"' : '\'',
            0x35 => shift ? '~' : '`',
            0x36 => shift ? '<' : ',',
            0x37 => shift ? '>' : '.',
            0x38 => shift ? '?' : '/',
            _ => null,
        };
    }

    byte botoes;

    /// <summary>O relatorio do mouse: os botoes, o x, o y (e a roda).</summary>
    void Mouse(byte[] r)
    {
        byte novos = r[0];
        int dx = (sbyte)r[1], dy = (sbyte)r[2];
        MouseX += dx;
        MouseY += dy;
        int apertou = novos & ~botoes;
        if (apertou != 0)
        {
            Cliques++;
            string botao = (apertou & 1) != 0 ? "esquerdo" : (apertou & 2) != 0 ? "direito" : "meio";
            Anotar($"mouse USB clicou ({botao}) em {MouseX},{MouseY}");
        }
        botoes = novos;
    }

    static readonly string[] Notas = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>Um pacote USB MIDI (4 bytes: o cabo/codigo e a mensagem MIDI).</summary>
    public void Midi(byte[] p)
    {
        if (p.Length < 4) return;
        int status = p[1] & 0xF0, canal = (p[1] & 0x0F) + 1;
        string nota = $"{Notas[p[2] % 12]}{p[2] / 12 - 1}";
        if (status == 0x90 && p[3] > 0) Anotar($"midi nota {nota} ligou (velocidade {p[3]}, canal {canal})");
        else if (status == 0x80 || status == 0x90) Anotar($"midi nota {nota} desligou (canal {canal})");
        else if (status == 0xB0) Anotar($"midi controle {p[2]} = {p[3]} (canal {canal})");
        else Anotar($"midi {Convert.ToHexString(p)}");
    }

    public void Vendor(byte[] dados) => Anotar($"vendor: {Convert.ToHexString(dados)}");
}
