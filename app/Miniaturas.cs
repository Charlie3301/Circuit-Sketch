using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Simulador.App;

/// <summary>
/// A foto do circuito de cada projeto salvo.
///
/// Serve para a tela de abertura: lista de nomes de arquivo é igual a
/// lista nenhuma quando os cinco últimos se chamam "projeto (2)". Com a
/// imagem dá para reconhecer o circuito de longe.
///
/// As imagens ficam em %APPDATA%, e não do lado do .sktch: a pasta do
/// aluno não vira um monte de PNG solto, e apagar o cache não perde
/// nada -- da próxima vez que salvar, ele volta.
/// </summary>
public static class Miniaturas
{
    public const int Largura = 320;
    public const int Altura = 200;

    static string Pasta => Path.Combine(Armazem.Pasta, "miniaturas");

    /// <summary>
    /// Nome de arquivo previsível a partir do caminho do projeto.
    ///
    /// É um hash do caminho inteiro: dois projetos com o mesmo nome em
    /// pastas diferentes não podem dividir a mesma foto.
    /// </summary>
    public static string CaminhoDe(string projeto)
    {
        if (string.IsNullOrWhiteSpace(projeto)) return null;

        string cheio = Path.GetFullPath(projeto).ToLowerInvariant();
        byte[] digest = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(cheio));

        return Path.Combine(Pasta, Convert.ToHexString(digest)[..16] + ".png");
    }

    /// <summary>
    /// Guarda o que está na tela do editor como foto deste projeto.
    ///
    /// Falha em silêncio de propósito: não conseguir gravar uma
    /// miniatura não pode atrapalhar o salvamento do projeto, que é o
    /// que importa.
    /// </summary>
    public static void Guardar(string projeto, FrameworkElement circuito)
    {
        string destino = CaminhoDe(projeto);
        if (destino is null || circuito is null) return;

        int larg = (int)Math.Round(circuito.ActualWidth);
        int alt = (int)Math.Round(circuito.ActualHeight);
        if (larg < 8 || alt < 8) return;

        try
        {
            var cheia = new RenderTargetBitmap(larg, alt, 96, 96, PixelFormats.Pbgra32);
            cheia.Render(circuito);

            // Reduz mantendo a proporção da tela, e depois corta o que
            // sobra: uma foto esticada faz o circuito parecer torto.
            double escala = Math.Max((double)Largura / larg, (double)Altura / alt);
            var menor = new TransformedBitmap(cheia, new ScaleTransform(escala, escala));

            int cortaX = Math.Max(0, (menor.PixelWidth - Largura) / 2);
            int cortaY = Math.Max(0, (menor.PixelHeight - Altura) / 2);
            var corte = new CroppedBitmap(menor, new Int32Rect(
                cortaX, cortaY,
                Math.Min(Largura, menor.PixelWidth),
                Math.Min(Altura, menor.PixelHeight)));

            Directory.CreateDirectory(Pasta);

            var codificador = new PngBitmapEncoder();
            codificador.Frames.Add(BitmapFrame.Create(corte));

            using var arquivo = File.Create(destino);
            codificador.Save(arquivo);
        }
        catch { /* sem foto, a lista mostra só o nome */ }
    }

    /// <summary>A foto deste projeto, ou nulo se ainda não houver.</summary>
    public static BitmapImage Ler(string projeto)
    {
        string origem = CaminhoDe(projeto);
        if (origem is null || !File.Exists(origem)) return null;

        try
        {
            var imagem = new BitmapImage();
            imagem.BeginInit();
            imagem.CacheOption = BitmapCacheOption.OnLoad;   // solta o arquivo
            imagem.UriSource = new Uri(origem);
            imagem.EndInit();
            imagem.Freeze();
            return imagem;
        }
        catch { return null; }
    }
}
