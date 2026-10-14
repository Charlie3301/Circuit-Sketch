using System.Windows.Markup;

namespace Simulador.App;

/// <summary>
/// A traducao dentro do XAML (Alpha 25): <c>Header="{local:T 'Salvar projeto'}"</c>.
/// O texto continua sendo o portugues original -- ele e a chave do catalogo
/// (ver <see cref="Idioma"/>). A janela le o texto uma vez, ao ser montada:
/// o idioma novo vale ao reabrir o programa.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string texto) : MarkupExtension
{
    public string Texto { get; set; } = texto;

    public TExtension() : this("") { }

    public override object ProvideValue(IServiceProvider servicos) => Idioma.T(Texto);
}
