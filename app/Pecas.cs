using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace Simulador.App;

/// <summary>
/// Carrega os SVGs dos componentes (embutidos no assembly) e devolve
/// imagens prontas para o WPF.
///
/// Arte: wokwi-elements, MIT (c) 2020 Uri Shaked -- ver
/// assets/LICENSE-wokwi-elements.txt. Regenerar os arquivos com
/// ferramentas/extrair-svg.
///
/// Cada estado (LED aceso/apagado, botao solto/pressionado) e um arquivo
/// separado: trocar a imagem custa quase nada e evita mexer no SVG em
/// tempo de execucao. Tudo fica em cache -- converter SVG e caro e a
/// tela redesenha 25 vezes por segundo.
/// </summary>
public static class Pecas
{
    static readonly Dictionary<string, ImageSource> cache = [];

    /// <summary>
    /// Onde a arte de cada SVG caiu de verdade, em pixels da peca.
    ///
    /// E a caixa da TINTA -- do desenho -- e nao a da viewport. Serve
    /// para o autoteste conferir se a arte esta no lugar onde os pinos
    /// dizem que ela esta: o LED RGB e os botoes desenhavam torto e os
    /// pinos ficavam fora das pernas, e olhar a tela e o unico jeito de
    /// pegar isso sem uma medida como esta.
    /// </summary>
    static readonly Dictionary<string, Rect> tintas = [];

    /// <summary>
    /// A arte que passa da caixa da peca, por igual dos dois lados (o
    /// data-sobra="x y" do SVG, em pixels): o USB-B da placa Arduino sai
    /// 6,35 mm da placa. O editor desenha a imagem maior, com a sobra para
    /// fora da caixa -- o centro (o giro) e os pinos ficam onde estavam.
    /// </summary>
    static readonly Dictionary<string, Vector> sobras = [];

    static void LerSobra(string nome, string svg)
    {
        sobras.Remove(nome);
        if (Atributo(svg[..Math.Max(0, svg.IndexOf('>'))], "data-sobra") is { } sobra
            && sobra.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: 2 } partes
            && Numero(partes[0], out double sx) && Numero(partes[1], out double sy))
            sobras[nome] = new Vector(sx, sy);
    }

    public static Vector SobraDe(string nome)
    {
        if (!cache.ContainsKey(nome)) Obter(nome);
        return sobras.GetValueOrDefault(nome);
    }

    internal static Rect TintaDeParaTeste(string nome) => TintaDe(nome);

    internal static Rect TintaDe(string nome)
    {
        Obter(nome);
        return tintas.TryGetValue(nome, out var r) ? r : Rect.Empty;
    }

    /// <summary>
    /// Onde a arte de um SVG qualquer cai, sem passar pelo cache.
    ///
    /// O autoteste usa com um SVG de sonda -- um retangulo que cobre
    /// exatamente o viewBox -- para medir a conta que o renderizador faz
    /// com o viewBox. E a unica forma de provar que a arte cai no lugar
    /// sem alguem olhar a tela.
    /// </summary>
    internal static Rect TintaDeTexto(string svg)
    {
        var desenho = Ler(svg);
        return desenho is null ? Rect.Empty : Endireitar(desenho, svg, null).Bounds;
    }

    static readonly Assembly assembly = typeof(Pecas).Assembly;

    /// <summary>Existe SVG com este nome embutido?</summary>
    public static bool Existe(string nome)
        => desenhosSoltos.ContainsKey(nome) || AcharRecurso(nome) is not null;

    /// <summary>
    /// SVGs que nao vem embutidos: o desenho de cada chip proprio, que
    /// so existe depois de alguem escrever o .chip.json.
    /// </summary>
    static readonly Dictionary<string, string> desenhosSoltos = [];

    /// <summary>Guarda (ou troca) o desenho de uma peca criada em execucao.</summary>
    public static void DefinirDesenho(string nome, string svg)
    {
        desenhosSoltos[nome] = svg;
        cache.Remove(nome);
        tintas.Remove(nome);
    }

    public static void EsquecerDesenhos(string prefixo)
    {
        foreach (string nome in desenhosSoltos.Keys.Where(
                     k => k.StartsWith(prefixo, StringComparison.Ordinal)).ToList())
        {
            desenhosSoltos.Remove(nome);
            cache.Remove(nome);
            tintas.Remove(nome);
        }
    }

    /// <summary>O desenho, ou nada quando a peca nao tem SVG.</summary>
    public static ImageSource Talvez(string nome) => Existe(nome) ? Obter(nome) : null;

    /// <summary>
    /// O mesmo desenho em preto e branco, para a Visao de Circuito.
    ///
    /// Fica em cache pela fonte: converter para cinza custa um render
    /// inteiro, e a tela pede o mesmo desenho 25 vezes por segundo.
    /// </summary>
    static readonly Dictionary<ImageSource, ImageSource> cinzas = [];

    public static ImageSource Cinza(ImageSource fonte, double largura, double altura)
    {
        if (fonte is null) return null;
        if (cinzas.TryGetValue(fonte, out var pronta)) return pronta;
        if (largura <= 0 || altura <= 0) return fonte;

        try
        {
            int l = (int)Math.Ceiling(largura * 2), a = (int)Math.Ceiling(altura * 2);
            var alvo = new System.Windows.Media.Imaging.RenderTargetBitmap(
                l, a, 192, 192, PixelFormats.Pbgra32);

            var desenho = new DrawingVisual();
            using (var ctx = desenho.RenderOpen())
                ctx.DrawImage(fonte, new Rect(0, 0, largura, altura));

            alvo.Render(desenho);

            var cinza = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                alvo, PixelFormats.Gray32Float, null, 0);
            cinza.Freeze();

            cinzas[fonte] = cinza;
            return cinza;
        }
        catch
        {
            return fonte;
        }
    }

    public static ImageSource Obter(string nome)
    {
        if (cache.TryGetValue(nome, out var pronta)) return pronta;

        var imagem = Carregar(nome);
        cache[nome] = imagem;
        return imagem;
    }

    /// <summary>
    /// Nomes de recurso usam o RootNamespace do projeto, nao o AssemblyName.
    /// Procurar pelo sufixo evita que renomear qualquer um dos dois quebre
    /// tudo em silencio -- foi exatamente o que aconteceu antes.
    /// </summary>
    static string AcharRecurso(string nome)
    {
        string sufixo = $".assets.{nome}.svg";
        foreach (var r in assembly.GetManifestResourceNames())
            if (r.EndsWith(sufixo, StringComparison.OrdinalIgnoreCase)) return r;
        return null;
    }

    /// <summary>
    /// Texto cru de cada SVG embutido. O autoteste usa para conferir o
    /// que o renderizador nao aceita -- e mais barato achar no arquivo do
    /// que descobrir olhando a tela.
    /// </summary>
    internal static IEnumerable<(string nome, string texto)> TodosOsSvgs()
    {
        foreach (string r in assembly.GetManifestResourceNames())
        {
            if (!r.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;

            using var f = assembly.GetManifestResourceStream(r);
            using var leitor = new StreamReader(f);

            int corte = r.LastIndexOf(".assets.", StringComparison.OrdinalIgnoreCase);
            string nome = corte < 0 ? r : r[(corte + ".assets.".Length)..^".svg".Length];

            yield return (nome, leitor.ReadToEnd());
        }
    }

    /// <summary>
    /// O leitor do SharpVectors. Uma instancia por chamada: a
    /// biblioteca guarda estado do documento anterior e misturar dois
    /// SVGs no mesmo leitor traz geometria de um para o outro.
    /// </summary>
    static DrawingGroup Ler(Stream fluxo)
    {
        var ajustes = new SharpVectors.Renderers.Wpf.WpfDrawingSettings
        {
            IncludeRuntime = false,
            TextAsGeometry = true,
        };

        using var leitor = new SharpVectors.Converters.FileSvgReader(ajustes);
        return leitor.Read(fluxo);
    }

    static DrawingGroup Ler(string svg)
    {
        using var fluxo = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svg));
        try { return Ler(fluxo); }
        catch { return null; }
    }

    /// <summary>O ajuste medido desta peca, ou nada.</summary>
    static Vector AjusteDe(string nome)
    {
        // A sonda do autoteste passa sem nome: ela mede a conta do
        // renderizador, e nao uma peca do catalogo.
        if (nome is null) return default;

        // As variantes ("led-vermelho-on", "pushbutton-solto") herdam o
        // ajuste da peca: e o mesmo desenho com outra cor ou estado.
        if (AjustesDeArte.TryGetValue(nome, out var certo)) return certo;

        foreach (var (id, ajuste) in AjustesDeArte)
            if (nome.StartsWith(id + "-", StringComparison.Ordinal)) return ajuste;

        return default;
    }

    static ImageSource Carregar(string nome)
    {
        if (desenhosSoltos.TryGetValue(nome, out string proprio))
        {
            try
            {
                LerSobra(nome, proprio);
                var feito = Ler(proprio);
                if (feito is null) { Falhou(nome, "SVG proprio nao pode ser lido"); return null; }

                var quadroProprio = Enquadrar(feito, proprio, nome);
                tintas[nome] = quadroProprio.Children.Count > 1
                    ? quadroProprio.Children[^1].Bounds : quadroProprio.Bounds;

                var pronta = new DrawingImage(quadroProprio);
                pronta.Freeze();
                return pronta;
            }
            catch (Exception e)
            {
                Falhou(nome, e.Message);
                return null;
            }
        }

        string recurso = AcharRecurso(nome);
        if (recurso is null)
        {
            Falhou(nome, "recurso embutido nao encontrado");
            return null;
        }

        using Stream original = assembly.GetManifestResourceStream(recurso);
        if (original is null)
        {
            Falhou(nome, "stream nulo");
            return null;
        }

        // Le tudo: o texto do SVG ainda e preciso depois, para a viewport.
        using var memoria = new MemoryStream();
        original.CopyTo(memoria);
        string texto = System.Text.Encoding.UTF8.GetString(memoria.ToArray());
        memoria.Position = 0;
        LerSobra(nome, texto);

        try
        {
            DrawingGroup desenho = Ler(memoria);
            if (desenho is null)
            {
                Falhou(nome, "SharpVectors devolveu desenho nulo");
                return null;
            }

            var quadro = Enquadrar(desenho, texto, nome);
            tintas[nome] = quadro.Children.Count > 1
                ? quadro.Children[^1].Bounds : quadro.Bounds;

            var img = new DrawingImage(quadro);
            img.Freeze();                        // compartilhavel entre threads
            return img;
        }
        catch (Exception e)
        {
            Falhou(nome, e.Message);
            return null;
        }
    }

    /// <summary>
    /// Encaixa o desenho na viewport declarada pelo proprio SVG.
    ///
    /// Sem isto, o tamanho natural da imagem e o retangulo do CONTEUDO
    /// desenhado, nao o da viewport. Quando os dois batem nao muda nada
    /// -- e o caso da maioria das pecas. Mas o LED tem viewBox
    /// "-10 -5 35,456 39,618": dez unidades de folga a esquerda e cinco
    /// em cima, reservadas para o halo. O conteudo dele mede 22,6x34,1
    /// numa caixa de 40x50, e o Stretch.Uniform esticava isso em 1,46x e
    /// recentralizava -- as pernas saiam de onde PosicaoPino diz que os
    /// pinos estao, e o LED nao encostava nos furos.
    ///
    /// Um retangulo transparente do tamanho da viewport traz o limite de
    /// volta para o lugar; o recorte reproduz o overflow:hidden que o
    /// navegador aplica no SVG.
    /// </summary>
    static DrawingGroup Enquadrar(DrawingGroup desenho, string svg, string nome)
    {
        var viewport = ViewportDe(svg);
        if (viewport is null) return desenho;

        var caixa = new Rect(0, 0, viewport.Value.larg, viewport.Value.alt);
        var grupo = new DrawingGroup { ClipGeometry = new RectangleGeometry(caixa) };
        grupo.Children.Add(new GeometryDrawing(
            Brushes.Transparent, null, new RectangleGeometry(caixa)));
        grupo.Children.Add(Endireitar(desenho, svg, nome));
        return grupo;
    }

    /// <summary>
    /// Desfaz o deslocamento que o renderizador poe no desenho.
    ///
    /// O SharpVectors normaliza o desenho para o canto (0, 0) e conta
    /// nisso coisas que nao deviam contar. Duas ficaram provadas com
    /// sondas:
    ///
    /// - viewBox que nao comeca em (0 0): ele aplica a escala e a
    ///   centralizacao certas e ainda soma -minX e -minY. Pegava o LED e
    ///   o LED RGB (-10 -5 e -17 -10), os dois botoes (-3 0) e as tres
    ///   placas Arduino;
    ///
    /// - defs com coordenada negativa: um circulo de raio 20 guardado no
    ///   defs empurra TODO o desenho em 20 px, mesmo que ele so apareca
    ///   depois, por use, em cima de um ponto positivo. Era o caso do
    ///   controle remoto, cujos botoes sao um circulo de raio 16,3 no
    ///   defs: a arte saia 17 px fora da caixa e a coluna da direita
    ///   ficava cortada pelo limite da peca.
    ///
    /// Em vez de deduzir a conta, aqui ela e MEDIDA: uma sonda com o
    /// mesmo cabecalho e o mesmo defs, e um retangulo no canto do
    /// viewBox. Onde esse retangulo cai e o deslocamento -- seja qual
    /// for a causa, inclusive uma que ainda nao apareceu.
    /// </summary>
    /// <summary>
    /// Quanto a arte de cada peca precisa andar para cair em cima dos
    /// pinos, em pixels da caixa da peca.
    ///
    /// Existe porque em algumas pecas o desenho e o pinInfo do Wokwi nao
    /// concordam: o SVG tem o proprio conteudo deslocado dentro do
    /// viewBox, ou o viewBox tem folga de um lado so. O renderizador
    /// aqui esta certo -- a prova esta em Pecas_ArteNoLugar, que compara
    /// com a conta do navegador -- e por isso a correcao e da PECA, e
    /// nao da conta.
    ///
    /// Os numeros sao medidos, nao chutados: o bloco Diag_Arte do
    /// autoteste imprime onde a tinta cai e onde os pinos estao, e a
    /// diferenca entre os dois e o que entra aqui. Mexer numa peca pede
    /// rodar o Diag_Arte de novo.
    ///
    /// Fica em C# e nao no pecas.json de proposito: o catalogo e gerado
    /// de novo pelo extrator dos wokwi-elements, e um campo a mais no
    /// json sumiria na proxima geracao sem ninguem perceber.
    /// </summary>
    static readonly Dictionary<string, Vector> AjustesDeArte = new()
    {
        // (o motor de passo do Wokwi precisava de 4 px para cima; o NEMA 17
        // da Alpha 25 e desenho proprio, com os pinos no lugar)
    };

    static Drawing Endireitar(DrawingGroup desenho, string svg, string nome)
    {
        var desvio = DesvioDe(svg);

        Drawing pronto = desenho;
        if (Math.Abs(desvio.X) >= 0.01 || Math.Abs(desvio.Y) >= 0.01)
        {
            var certo = new DrawingGroup
            {
                Transform = new TranslateTransform(-desvio.X, -desvio.Y)
            };
            certo.Children.Add(desenho);
            pronto = certo;
        }

        // O ajuste da peca vem ANTES do corte: o que ele traz de volta
        // para dentro da caixa nao pode ter sido jogado fora antes.
        var ajuste = AjusteDe(nome);
        if (ajuste.X != 0 || ajuste.Y != 0)
        {
            var movido = new DrawingGroup
            {
                Transform = new TranslateTransform(ajuste.X, ajuste.Y)
            };
            movido.Children.Add(pronto);
            pronto = movido;
        }

        return Recortar(pronto, svg);
    }


    /// <summary>
    /// Corta o que sai da viewport, como o navegador faz.
    ///
    /// Um &lt;svg&gt; tem overflow:hidden por padrao: o que o desenho
    /// poe fora do width/height simplesmente nao aparece. O
    /// renderizador daqui desenha tudo, e por isso o joystick vinha com
    /// quadrados soltos atras dos circulos (a arte dele e duas vezes
    /// maior que a propria caixa) e a Mega vazava um pedaco de placa
    /// para fora da direita.
    ///
    /// Sem o corte nao adianta acertar a posicao: a peca continua
    /// ocupando mais espaco na tela do que os pinos dizem que ela tem, e
    /// o clique e o encaixe na protoboard usam a caixa dos pinos.
    /// </summary>
    static Drawing Recortar(Drawing desenho, string svg)
    {
        var viewport = ViewportDe(svg);
        if (viewport is null) return desenho;

        var caixa = new Rect(0, 0, viewport.Value.larg, viewport.Value.alt);
        if (caixa.Width <= 0 || caixa.Height <= 0) return desenho;

        // Sobra de meio pixel: traco na borda desenha metade para fora e
        // cortar rente deixaria a linha do contorno pela metade.
        caixa.Inflate(0.5, 0.5);
        if (caixa.Contains(desenho.Bounds)) return desenho;

        var cortado = new DrawingGroup { ClipGeometry = new RectangleGeometry(caixa) };
        cortado.Children.Add(desenho);
        return cortado;
    }

    /// <summary>Quanto o renderizador deslocou este desenho, em pixels.</summary>
    static Vector DesvioDe(string svg)
    {
        var viewport = ViewportDe(svg);
        if (viewport is null) return default;

        var (mx, my, vl, va) = ViewBoxDe(svg);
        if (vl <= 0 || va <= 0) { vl = viewport.Value.larg; va = viewport.Value.alt; }

        int cabeca = svg.IndexOf('>');
        if (cabeca < 0) return default;

        var cultura = System.Globalization.CultureInfo.InvariantCulture;
        // O defs sozinho nao desloca nada: e o <use> que faz o
        // renderizador contar a geometria guardada. Por isso a sonda
        // leva os dois, e so depois o retangulo do canto.
        string sonda = svg[..(cabeca + 1)] + Defs(svg) + Usos(svg)
            + $"<rect x=\"{mx.ToString(cultura)}\" y=\"{my.ToString(cultura)}\" "
            + "width=\"1\" height=\"1\" fill=\"#000\"/></svg>";

        var marca = Ler(sonda)?.Bounds ?? Rect.Empty;
        if (marca.IsEmpty) return default;

        // Onde o canto do viewBox DEVIA cair: escala uniforme (meet) com
        // a sobra dividida em dois, que e o que o navegador faz.
        double escala = Math.Min(viewport.Value.larg / vl, viewport.Value.alt / va);
        double x0 = (viewport.Value.larg - vl * escala) / 2;
        double y0 = (viewport.Value.alt - va * escala) / 2;

        return new Vector(marca.X - x0, marca.Y - y0);
    }

    /// <summary>Todos os blocos defs do SVG, na ordem em que aparecem.</summary>
    static string Defs(string svg)
    {
        var junto = new System.Text.StringBuilder();
        int i = 0;
        while (true)
        {
            int abre = svg.IndexOf("<defs", i, StringComparison.OrdinalIgnoreCase);
            if (abre < 0) break;

            int fecha = svg.IndexOf("</defs>", abre, StringComparison.OrdinalIgnoreCase);
            if (fecha < 0) break;

            junto.Append(svg.AsSpan(abre, fecha + 7 - abre));
            i = fecha + 7;
        }
        return junto.ToString();
    }

    /// <summary>Todos os elementos use do SVG, na ordem em que aparecem.</summary>
    static string Usos(string svg)
    {
        var junto = new System.Text.StringBuilder();
        int i = 0;
        while (true)
        {
            int abre = svg.IndexOf("<use", i, StringComparison.OrdinalIgnoreCase);
            if (abre < 0) break;

            int fecha = svg.IndexOf('>', abre);
            if (fecha < 0) break;

            junto.Append(svg.AsSpan(abre, fecha + 1 - abre));

            // "<use ...></use>" existe; fechar sempre e mais simples do
            // que descobrir se ja estava fechado.
            if (svg[fecha - 1] != '/') junto.Append("</use>");
            i = fecha + 1;
        }
        return junto.ToString();
    }

    /// <summary>O viewBox: canto e tamanho. Zeros quando nao ha.</summary>
    static (double x, double y, double larg, double alt) ViewBoxDe(string svg)
    {
        int fim = svg.IndexOf('>');
        if (fim < 0) return (0, 0, 0, 0);

        string vb = Atributo(svg[..fim], "viewBox");
        if (vb is null) return (0, 0, 0, 0);

        var n = vb.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (n.Length < 4) return (0, 0, 0, 0);

        return (Numero(n[0], out double x) ? x : 0, Numero(n[1], out double y) ? y : 0,
                Numero(n[2], out double l) ? l : 0, Numero(n[3], out double a) ? a : 0);
    }

    /// <summary>
    /// Tamanho da viewport em pixels de 96 dpi, do width/height do SVG.
    /// Sem eles, o proprio viewBox -- e o que o navegador faz.
    /// </summary>
    static (double larg, double alt)? ViewportDe(string svg)
    {
        int fim = svg.IndexOf('>');
        if (fim < 0) return null;
        string cabeca = svg[..fim];

        double? l = Medida(Atributo(cabeca, "width"));
        double? a = Medida(Atributo(cabeca, "height"));
        if (l is > 0 && a is > 0) return (l.Value, a.Value);

        string vb = Atributo(cabeca, "viewBox");
        if (vb is null) return null;
        var n = vb.Split([' ', ',', '	'], StringSplitOptions.RemoveEmptyEntries);
        if (n.Length < 4) return null;
        if (!Numero(n[2], out double vl) || !Numero(n[3], out double va)) return null;
        return vl > 0 && va > 0 ? (vl, va) : null;
    }

    static string Atributo(string cabeca, string nome)
    {
        int i = cabeca.IndexOf($" {nome}=\"", StringComparison.Ordinal);
        if (i < 0) return null;
        i += nome.Length + 3;
        int f = cabeca.IndexOf('"', i);
        return f < 0 ? null : cabeca[i..f];
    }

    /// <summary>"40", "17.802mm", "12pt"... em pixels de 96 dpi.</summary>
    static double? Medida(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        valor = valor.Trim();
        if (valor.EndsWith("%", StringComparison.Ordinal)) return null;

        double fator = 1;
        foreach (var (sufixo, f) in new[]
                 { ("mm", 96 / 25.4), ("cm", 96 / 2.54), ("in", 96.0),
                   ("pt", 96 / 72.0), ("pc", 16.0), ("px", 1.0) })
        {
            if (!valor.EndsWith(sufixo, StringComparison.OrdinalIgnoreCase)) continue;
            valor = valor[..^2];
            fator = f;
            break;
        }
        return Numero(valor, out double v) ? v * fator : null;
    }

    static bool Numero(string t, out double v)
        => double.TryParse(t, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out v);

    /// <summary>Peca que nao carrega some da tela sem avisar. Registrar.</summary>
    static void Falhou(string nome, string motivo)
    {
        try
        {
            string arquivo = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "circuitsketch-erros.log");
            File.AppendAllText(arquivo,
                $"[{DateTime.Now:HH:mm:ss}] peca '{nome}' nao carregou: {motivo}{Environment.NewLine}");
        }
        catch { }
    }

    public const string Esp32 = "esp32-devkit-v1";
    public const string BotaoSolto = "botao-solto";
    public const string BotaoPressionado = "botao-pressionado";
    public const string Potenciometro = "potenciometro";
    public const string PotCorpo = "potenciometro-corpo";
    public const string PotBotao = "potenciometro-movel";
    public const string Buzzer = "buzzer";
    public const string Lcd = "lcd1602";
}
