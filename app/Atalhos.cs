using System.Windows.Input;

namespace Simulador.App;

public sealed record AcaoDoPrograma(string Id, string Nome, Key Padrao, ModifierKeys Modificador);

public static class AtalhosDoPrograma
{
    public static readonly AcaoDoPrograma[] Todas =
    [
        new("rodar", Idioma.T("Rodar ou parar a simulação"), Key.F5, ModifierKeys.None),
        new("pausar", Idioma.T("Pausar ou continuar"), Key.F6, ModifierKeys.None),
        new("pausa-na-linha", Idioma.T("Ponto de parada na linha"), Key.F9, ModifierKeys.None),
        new("passo", Idioma.T("Andar uma linha"), Key.F10, ModifierKeys.None),
        new("verificar", Idioma.T("Verificar o código"), Key.R, ModifierKeys.Control),
        new("mandar", Idioma.T("Mandar para a placa de verdade"), Key.U, ModifierKeys.Control),

        new("novo", Idioma.T("Circuito novo"), Key.N, ModifierKeys.Control),
        new("salvar", Idioma.T("Salvar projeto"), Key.S, ModifierKeys.Control),
        new("salvar-como", Idioma.T("Salvar projeto como"), Key.S, ModifierKeys.Control | ModifierKeys.Shift),
        new("abrir", Idioma.T("Abrir projeto"), Key.O, ModifierKeys.Control),
        new("importar", Idioma.T("Importar do Wokwi"), Key.I, ModifierKeys.Control),
        new("bibliotecas", Idioma.T("Gerenciador de bibliotecas"), Key.I, ModifierKeys.Control | ModifierKeys.Shift),   // o mesmo da Arduino IDE
        new("monitor", Idioma.T("Ir para o monitor serial"), Key.M, ModifierKeys.Control),
        new("desfazer", Idioma.T("Desfazer"), Key.Z, ModifierKeys.Control),
        new("refazer", Idioma.T("Refazer"), Key.Y, ModifierKeys.Control),
        new("copiar", Idioma.T("Copiar"), Key.C, ModifierKeys.Control),
        new("colar", Idioma.T("Colar"), Key.V, ModifierKeys.Control),

        new("adicionar", Idioma.T("Adicionar componente"), Key.A, ModifierKeys.None),
        new("ajustar", Idioma.T("Ajustar à tela"), Key.F, ModifierKeys.None),
        new("grade", Idioma.T("Ligar ou desligar a grade"), Key.G, ModifierKeys.None),
        new("girar", Idioma.T("Girar a peça"), Key.R, ModifierKeys.None),
        new("espelhar", Idioma.T("Espelhar a peça"), Key.M, ModifierKeys.None),
        new("excluir", Idioma.T("Excluir o que está selecionado"), Key.Delete, ModifierKeys.None),
        new("ajuda", Idioma.T("Ajuda e atalhos"), Key.OemQuestion, ModifierKeys.None),
    ];

    public static AcaoDoPrograma Acao(string id) => Todas.FirstOrDefault(a => a.Id == id);

    public static (Key Tecla, ModifierKeys Modificador) Atalho(string id)
    {
        var acao = Acao(id);
        if (acao is null) return (Key.None, ModifierKeys.None);

        string gravado = Preferencias.Texto(Chave(id));
        if (gravado.Length > 0 && Ler(gravado) is { } lido) return lido;

        return (acao.Padrao, acao.Modificador);
    }

    public static void Definir(string id, Key tecla, ModifierKeys modificador)
        => Preferencias.Definir(Chave(id), Escrever(tecla, modificador));

    public static void Esquecer(string id) => Preferencias.Definir(Chave(id), "");

    public static string Id(Key tecla, ModifierKeys modificador)
    {
        foreach (var acao in Todas)
        {
            var (t, m) = Atalho(acao.Id);
            if (t == tecla && m == modificador) return acao.Id;
        }
        return null;
    }

    public static string Rotulo(string id)
    {
        var (tecla, modificador) = Atalho(id);
        return Rotulo(tecla, modificador);
    }

    public static string Rotulo(Key tecla, ModifierKeys modificador)
    {
        if (tecla == Key.None) return "—";

        string nome = Vinculos.NomeDaTecla(tecla);
        if (modificador.HasFlag(ModifierKeys.Control)) nome = "Ctrl+" + nome;
        if (modificador.HasFlag(ModifierKeys.Alt)) nome = "Alt+" + nome;
        if (modificador.HasFlag(ModifierKeys.Shift)) nome = "Shift+" + nome;
        return nome;
    }

    public static string EmUso(string id, Key tecla, ModifierKeys modificador)
    {
        foreach (var acao in Todas)
        {
            if (acao.Id == id) continue;
            var (t, m) = Atalho(acao.Id);
            if (t == tecla && m == modificador) return acao.Nome;
        }
        return null;
    }

    static string Chave(string id) => "atalho-" + id;

    /// <summary>O atalho do jeito que se le: "Ctrl+C", "Del", "R".</summary>
    public static string Escrever(Key tecla, ModifierKeys modificador)
        => $"{(int)modificador}:{tecla}";

    static (Key, ModifierKeys)? Ler(string texto)
    {
        int corte = texto.IndexOf(':');
        if (corte <= 0) return null;

        if (!int.TryParse(texto[..corte], out int mods)) return null;
        if (!Enum.TryParse<Key>(texto[(corte + 1)..], out var tecla)) return null;

        return (tecla, (ModifierKeys)mods);
    }
}
