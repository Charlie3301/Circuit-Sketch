namespace Simulador.App;

/// <summary>
/// As cores das entradas da Alpha 25 (o modelo da peca):
///
///  - a capa redonda do pushbutton de 12 mm, as mesmas cores do "color" do
///    Wokwi (o vermelho e o de fabrica);
///  - o corpo da chave DIP: o vermelho, o azul ou o preto.
///
/// Cada cor e um desenho proprio (pecas-entradas.py); o pushbutton tem o
/// solto e o apertado de cada uma.
/// </summary>
public partial class EditorCircuito
{
    static readonly string[] CoresDoPushbutton = ["vermelho", "verde", "azul", "amarelo", "branco", "preto", "cinza"];
    static readonly string[] CoresDoDipSwitch = ["vermelho", "azul", "preto"];

    /// <summary>A cor da capa do pushbutton de 12 mm.</summary>
    public static string CorDoBotao(PecaNoCircuito peca) => CoresDoPushbutton.Contains(peca.Modelo) ? peca.Modelo : "vermelho";

    public static string[] OpcoesDeCorDoBotao => CoresDoPushbutton;

    /// <summary>A cor do corpo da chave DIP.</summary>
    public static string CorDoDip(PecaNoCircuito peca) => CoresDoDipSwitch.Contains(peca.Modelo) ? peca.Modelo : "vermelho";

    public static string[] OpcoesDeCorDoDip => CoresDoDipSwitch;

    /// <summary>O desenho do pushbutton de 12 mm: a cor da capa, solto ou apertado.</summary>
    static string NomeDoPushbutton(PecaNoCircuito peca, bool apertado)
    {
        string cor = CorDoBotao(peca);
        return $"pushbutton{(cor == "vermelho" ? "" : "-" + cor)}-{(apertado ? "pressionado" : "solto")}";
    }

    /// <summary>A cor do Wokwi ("color" do pushbutton) para o modelo daqui, e de volta.</summary>
    public static string CorDoBotaoDoWokwi(string cor) => cor?.ToLowerInvariant() switch
    {
        "green" => "verde",
        "blue" => "azul",
        "yellow" => "amarelo",
        "white" => "branco",
        "black" => "preto",
        "gray" or "grey" => "cinza",
        _ => null,
    };

    public static string CorDoBotaoParaWokwi(string modelo) => modelo switch
    {
        "verde" => "green",
        "azul" => "blue",
        "amarelo" => "yellow",
        "branco" => "white",
        "preto" => "black",
        "cinza" => "gray",
        _ => "red",
    };
}
