// dotnet run --project ferramentas/fontes-dap
//
// As letras do PlayerDAP: a Noto Sans (OFL, ao lado) rasterizada pelo WPF em
// 4 bits de cobertura por pixel, nos tamanhos da tela de 240x320, e gravada
// como C em bibliotecas/PlayerDAP/src/dap_fontes.c. Latin-1 e Latin
// Extended-A (o portugues e os nomes europeus das musicas), mais as aspas e
// os tracos tipograficos que aparecem nas etiquetas.
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

static class Program
{
    record Tamanho(string Nome, string Arquivo, double Pixels, bool Icones = false);

    /// <summary>Os icones da Material Icons (Apache 2.0) que a tela usa: nome em C e o nome na fonte.</summary>
    static readonly (string C, string Material)[] Icones =
    [
        ("TOCAR", "play_arrow"), ("PAUSAR", "pause"), ("PARAR", "stop"), ("PROXIMA", "skip_next"),
        ("ANTERIOR", "skip_previous"), ("ALEATORIO", "shuffle"), ("REPETIR", "repeat"), ("REPETIR_UMA", "repeat_one"),
        ("FAVORITA", "favorite"), ("NAO_FAVORITA", "favorite_border"), ("EQUALIZADOR", "equalizer"),
        ("VOLUME", "volume_up"), ("MUDO", "volume_off"), ("BATERIA", "battery_full"), ("BATERIA_FRACA", "battery_alert"),
        ("CARTAO", "sd_card"), ("USB", "usb"), ("PASTA", "folder"), ("NOTA", "music_note"), ("ARTISTA", "person"),
        ("ALBUM", "album"), ("GENERO", "category"), ("ANO", "calendar_today"), ("COMPOSITOR", "edit"),
        ("PLAYLIST", "queue_music"), ("CONFIGURAR", "settings"), ("VOLTAR", "arrow_back"), ("CERTO", "check"),
        ("NOVA", "add"), ("BIBLIOTECA", "library_music"), ("ONDA", "graphic_eq"), ("FONE", "headphones"),
        ("ADICIONAR_LISTA", "playlist_add"), ("ESTRELA", "star"), ("DESLIGAR", "power_settings_new"),
        ("ATUALIZAR", "refresh"), ("ARMAZENAMENTO", "storage"), ("INFO", "info"), ("AVISO", "warning"),
        ("REPETIR_UMA_VEZ", "replay"), ("TROCAR", "swap_vert"), ("TOCANDO", "headphones"),
    ];

    [STAThread]
    static void Main()
    {
        string aqui = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(aqui, "NotoSans-Regular.ttf"))) aqui = Path.GetDirectoryName(aqui.TrimEnd('\\', '/'));
        string saida = Path.GetFullPath(Path.Combine(aqui, "..", "..", "bibliotecas", "PlayerDAP", "src"));
        Directory.CreateDirectory(saida);

        var tamanhos = new[]
        {
            new Tamanho("pequena", "NotoSans-Regular.ttf", 11),
            new Tamanho("normal", "NotoSans-Regular.ttf", 14),
            new Tamanho("forte", "NotoSans-Bold.ttf", 14),
            new Tamanho("titulo", "NotoSans-Bold.ttf", 18),
            new Tamanho("grande", "NotoSans-Bold.ttf", 28),
            new Tamanho("icones", "MaterialIcons-Regular.ttf", 16, true),
            new Tamanho("icones_medios", "MaterialIcons-Regular.ttf", 24, true),
            new Tamanho("icones_grandes", "MaterialIcons-Regular.ttf", 48, true),
        };

        // Os codigos dos icones, pela tabela da propria fonte.
        var daTabela = File.ReadAllLines(Path.Combine(aqui, "MaterialIcons-Regular.codepoints"))
            .Select(l => l.Split(' ')).Where(p => p.Length == 2)
            .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => Convert.ToInt32(g.First()[1], 16));
        var codigosDosIcones = Icones.Select(i => daTabela[i.Material]).Distinct().OrderBy(c => c).ToList();

        var codigos = new List<int>();
        for (int c = 0x20; c <= 0x7E; c++) codigos.Add(c);
        for (int c = 0xA0; c <= 0x17F; c++) codigos.Add(c);
        codigos.AddRange([0x2018, 0x2019, 0x201C, 0x201D, 0x2013, 0x2014, 0x2026, 0x2022, 0x20AC, 0x2122]);
        // o player acha a letra por busca binaria: a tabela vai em ordem
        codigos.Sort();

        var c_ = new StringBuilder();
        c_.AppendLine("/* dap_fontes.c -- GERADO por ferramentas/fontes-dap (nao edite a mao).");
        c_.AppendLine(" *");
        c_.AppendLine(" * As letras da tela: a Noto Sans, rasterizada em 4 bits de cobertura por");
        c_.AppendLine(" * pixel (16 tons, dois pixels por byte, o da esquerda no nibble de cima).");
        c_.AppendLine(" * Noto Sans: Copyright 2022 The Noto Project Authors, SIL Open Font");
        c_.AppendLine(" * License 1.1 (o texto vai em LICENCAS.txt da biblioteca). */");
        c_.AppendLine("#include \"dap_fontes.h\"");
        c_.AppendLine();

        foreach (var t in tamanhos)
        {
            var tipo = new GlyphTypeface(new Uri(Path.Combine(aqui, t.Arquivo)));
            if (t.Icones) Console.WriteLine($"{t.Nome}: baseline {tipo.Baseline:0.###}, height {tipo.Height:0.###}");
            double em = t.Pixels;
            int ascendente = (int)Math.Round(tipo.Baseline * em);
            int altura = (int)Math.Round(tipo.Height * em);
            var dados = new List<byte>();
            var glifos = new List<string>();

            foreach (int codigo in t.Icones ? codigosDosIcones : codigos)
            {
                if (!tipo.CharacterToGlyphMap.TryGetValue(codigo, out ushort indice)) continue;
                double avanco = tipo.AdvanceWidths[indice] * em;
                var (larg, alt, dx, dy, pixels) = Rasterizar(tipo, indice, em, ascendente);
                int pos = dados.Count;
                for (int y = 0; y < alt; y++)
                    for (int x = 0; x < larg; x += 2)
                    {
                        int a = pixels[y * larg + x];
                        int b = x + 1 < larg ? pixels[y * larg + x + 1] : 0;
                        dados.Add((byte)((a << 4) | b));
                    }
                glifos.Add($"    {{0x{codigo:X4}, {larg}, {alt}, {dx}, {dy}, {(int)Math.Round(avanco)}, {pos}}},");
            }

            c_.AppendLine($"/* {t.Arquivo}, {t.Pixels} px: {glifos.Count} letras, {dados.Count} bytes. */");
            c_.AppendLine($"static const uint8_t dados_{t.Nome}[] = {{");
            for (int i = 0; i < dados.Count; i += 24)
                c_.AppendLine("    " + string.Join(", ", dados.Skip(i).Take(24).Select(b => $"0x{b:X2}")) + ",");
            c_.AppendLine("};");
            c_.AppendLine($"static const dap_glifo_t glifos_{t.Nome}[] = {{");
            foreach (string g in glifos) c_.AppendLine(g);
            c_.AppendLine("};");
            c_.AppendLine($"const dap_fonte_t dap_fonte_{t.Nome} = {{ glifos_{t.Nome}, {glifos.Count}, dados_{t.Nome}, {altura}, {ascendente} }};");
            c_.AppendLine();
            Console.WriteLine($"{t.Nome}: {t.Pixels} px, {glifos.Count} letras, {dados.Count} bytes, altura {altura}, ascendente {ascendente}");
        }

        File.WriteAllText(Path.Combine(saida, "dap_fontes.c"), c_.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));

        // Os nomes dos icones em C.
        var h = new StringBuilder();
        h.AppendLine("/* dap_icones.h -- GERADO por ferramentas/fontes-dap: os icones da Material Icons");
        h.AppendLine(" * (Apache 2.0), desenhados com as fontes dap_fonte_icones* como se fossem letras. */");
        h.AppendLine("#ifndef DAP_ICONES_H");
        h.AppendLine("#define DAP_ICONES_H");
        h.AppendLine();
        foreach (var (nome, material) in Icones) h.AppendLine($"#define ICO_{nome} 0x{daTabela[material]:X4}   /* {material} */");
        h.AppendLine();
        h.AppendLine("#endif");
        File.WriteAllText(Path.Combine(saida, "dap_icones.h"), h.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Console.WriteLine("gravado em " + Path.Combine(saida, "dap_fontes.c"));
    }

    /// <summary>Uma letra, branca no transparente: a cobertura de cada pixel (0..15), recortada.</summary>
    static (int Larg, int Alt, int Dx, int Dy, byte[] Pixels) Rasterizar(GlyphTypeface tipo, ushort indice, double em, int ascendente)
    {
        int margem = (int)Math.Ceiling(em * 0.6);
        int lado = (int)Math.Ceiling(em * 2.2) + margem * 2;
        var origem = new Point(margem, margem + ascendente);
        var corrida = new GlyphRun(tipo, 0, false, em, 1.0f, [indice], origem, [tipo.AdvanceWidths[indice] * em],
                                   null, null, null, null, null, XmlLanguage());
        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        TextOptions.SetTextHintingMode(visual, TextHintingMode.Fixed);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
        using (var dc = visual.RenderOpen()) dc.DrawGlyphRun(Brushes.White, corrida);

        var bitmap = new RenderTargetBitmap(lado, lado, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var bgra = new byte[lado * lado * 4];
        bitmap.CopyPixels(bgra, lado * 4, 0);

        int x0 = lado, y0 = lado, x1 = -1, y1 = -1;
        for (int y = 0; y < lado; y++)
            for (int x = 0; x < lado; x++)
                if (bgra[(y * lado + x) * 4 + 3] >= 8)
                {
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                }
        if (x1 < 0) return (0, 0, 0, 0, []);

        int larg = x1 - x0 + 1, alt = y1 - y0 + 1;
        var pixels = new byte[larg * alt];
        for (int y = 0; y < alt; y++)
            for (int x = 0; x < larg; x++)
                pixels[y * larg + x] = (byte)((bgra[((y + y0) * lado + x + x0) * 4 + 3] * 15 + 127) / 255);
        return (larg, alt, x0 - margem, y0 - (margem + ascendente), pixels);
    }

    static System.Windows.Markup.XmlLanguage XmlLanguage() => System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.InvariantCulture.IetfLanguageTag);
}
