using System.Windows.Input;

namespace Simulador.App;

/// <summary>Uma acao do programa que tem atalho; a categoria agrupa a janela dos atalhos.</summary>
public sealed record AcaoDoPrograma(string Id, string Nome, Key Padrao, ModifierKeys Modificador, string Categoria);

public static class AtalhosDoPrograma
{
    static readonly string Simulacao = Idioma.T("Simulação e código");
    static readonly string Projeto = Idioma.T("Projeto");
    static readonly string Editar = Idioma.T("Editar");
    static readonly string Circuito = Idioma.T("Circuito");
    static readonly string Ver = Idioma.T("Ver");
    static readonly string Janelas = Idioma.T("Janelas e conexões");

    public static readonly AcaoDoPrograma[] Todas =
    [
        new("rodar", Idioma.T("Rodar ou parar a simulação"), Key.F5, ModifierKeys.None, Simulacao),
        new("pausar", Idioma.T("Pausar ou continuar"), Key.F6, ModifierKeys.None, Simulacao),
        new("pausa-na-linha", Idioma.T("Ponto de parada na linha"), Key.F9, ModifierKeys.None, Simulacao),
        new("passo", Idioma.T("Andar uma linha"), Key.F10, ModifierKeys.None, Simulacao),
        new("verificar", Idioma.T("Verificar o código"), Key.R, ModifierKeys.Control, Simulacao),
        new("mandar", Idioma.T("Mandar para a placa de verdade"), Key.U, ModifierKeys.Control, Simulacao),
        new("monitor", Idioma.T("Ir para o monitor serial"), Key.M, ModifierKeys.Control, Simulacao),

        new("novo", Idioma.T("Circuito novo"), Key.N, ModifierKeys.Control, Projeto),
        new("abrir", Idioma.T("Abrir projeto"), Key.O, ModifierKeys.Control, Projeto),
        new("salvar", Idioma.T("Salvar projeto"), Key.S, ModifierKeys.Control, Projeto),
        new("salvar-como", Idioma.T("Salvar projeto como"), Key.S, ModifierKeys.Control | ModifierKeys.Shift, Projeto),
        new("importar", Idioma.T("Importar do Wokwi"), Key.I, ModifierKeys.Control, Projeto),
        new("bibliotecas", Idioma.T("Gerenciador de bibliotecas"), Key.I, ModifierKeys.Control | ModifierKeys.Shift, Projeto),   // o mesmo da Arduino IDE

        new("desfazer", Idioma.T("Desfazer"), Key.Z, ModifierKeys.Control, Editar),
        new("refazer", Idioma.T("Refazer"), Key.Y, ModifierKeys.Control, Editar),
        new("copiar", Idioma.T("Copiar"), Key.C, ModifierKeys.Control, Editar),
        new("colar", Idioma.T("Colar"), Key.V, ModifierKeys.Control, Editar),
        new("excluir", Idioma.T("Excluir o que está selecionado"), Key.Delete, ModifierKeys.None, Editar),

        new("adicionar", Idioma.T("Adicionar componente"), Key.A, ModifierKeys.None, Circuito),
        new("girar", Idioma.T("Girar a peça"), Key.R, ModifierKeys.None, Circuito),
        new("espelhar", Idioma.T("Espelhar a peça"), Key.M, ModifierKeys.None, Circuito),
        new("grade", Idioma.T("Ligar ou desligar a grade"), Key.G, ModifierKeys.None, Circuito),

        new("ajustar", Idioma.T("Ajustar à tela"), Key.F, ModifierKeys.None, Ver),
        new("aproximar", Idioma.T("Aproximar"), Key.OemPlus, ModifierKeys.None, Ver),
        new("afastar", Idioma.T("Afastar"), Key.OemMinus, ModifierKeys.None, Ver),
        new("mover-esquerda", Idioma.T("Mover a tela para a esquerda"), Key.Left, ModifierKeys.None, Ver),
        new("mover-direita", Idioma.T("Mover a tela para a direita"), Key.Right, ModifierKeys.None, Ver),
        new("mover-cima", Idioma.T("Mover a tela para cima"), Key.Up, ModifierKeys.None, Ver),
        new("mover-baixo", Idioma.T("Mover a tela para baixo"), Key.Down, ModifierKeys.None, Ver),
        new("tela-cheia", Idioma.T("Tela cheia"), Key.Enter, ModifierKeys.Alt, Ver),
        new("modo-simulacao", Idioma.T("Aba SIMULAÇÃO"), Key.D1, ModifierKeys.Control, Ver),
        new("modo-multimetro", Idioma.T("Aba MULTÍMETRO"), Key.D2, ModifierKeys.Control, Ver),
        new("modo-componentes", Idioma.T("Aba COMPONENTES"), Key.D3, ModifierKeys.Control, Ver),
        new("modo-descricao", Idioma.T("Aba DESCRIÇÃO"), Key.D4, ModifierKeys.Control, Ver),

        new("celular", Idioma.T("Bluetooth (o celular)"), Key.B, ModifierKeys.Control, Janelas),
        new("mqtt", Idioma.T("Broker MQTT"), Key.Q, ModifierKeys.Control, Janelas),
        new("configuracoes", Idioma.T("Configurações"), Key.OemComma, ModifierKeys.Control, Janelas),
        new("atalhos", Idioma.T("Atalhos do teclado"), Key.K, ModifierKeys.Control, Janelas),
        new("ajuda", Idioma.T("Ajuda e atalhos"), Key.OemQuestion, ModifierKeys.None, Janelas),
    ];

    /// <summary>As categorias, na ordem da janela.</summary>
    public static IEnumerable<string> Categorias => Todas.Select(a => a.Categoria).Distinct();

    /// <summary>
    /// A tecla do teclado numerico vale pela de cima: o "+" do numerico e o
    /// do lado do backspace sao o mesmo atalho para quem aperta.
    /// </summary>
    public static Key Normalizar(Key tecla) => tecla switch
    {
        Key.Add => Key.OemPlus,
        Key.Subtract => Key.OemMinus,
        Key.NumPad0 => Key.D0, Key.NumPad1 => Key.D1, Key.NumPad2 => Key.D2, Key.NumPad3 => Key.D3,
        Key.NumPad4 => Key.D4, Key.NumPad5 => Key.D5, Key.NumPad6 => Key.D6, Key.NumPad7 => Key.D7,
        Key.NumPad8 => Key.D8, Key.NumPad9 => Key.D9,
        _ => tecla,
    };

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

    /// <summary>Tira o atalho da acao: ela fica sem tecla (e nao volta ao padrao).</summary>
    public static void Limpar(string id) => Definir(id, Key.None, ModifierKeys.None);

    public static string Id(Key tecla, ModifierKeys modificador)
    {
        tecla = Normalizar(tecla);
        if (tecla == Key.None) return null;
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

        string nome = NomeDaTecla(tecla);
        if (modificador.HasFlag(ModifierKeys.Control)) nome = "Ctrl+" + nome;
        if (modificador.HasFlag(ModifierKeys.Alt)) nome = "Alt+" + nome;
        if (modificador.HasFlag(ModifierKeys.Shift)) nome = "Shift+" + nome;
        return nome;
    }

    public static string EmUso(string id, Key tecla, ModifierKeys modificador)
        => Acao(QuemUsa(id, tecla, modificador))?.Nome;

    /// <summary>A outra acao que ja tem este atalho, ou null.</summary>
    public static string QuemUsa(string id, Key tecla, ModifierKeys modificador)
    {
        tecla = Normalizar(tecla);
        if (tecla == Key.None) return null;
        foreach (var acao in Todas)
        {
            if (acao.Id == id) continue;
            var (t, m) = Atalho(acao.Id);
            if (t == tecla && m == modificador) return acao.Id;
        }
        return null;
    }

    /// <summary>
    /// A dica do botao com o atalho de agora no fim: "Rodar a simulação (F5)"
    /// vira "(Ctrl+F5)" quando a pessoa troca, e perde o parenteses quando
    /// ela tira o atalho.
    /// </summary>
    public static string ComAtalho(string dica, string id)
    {
        string sem = System.Text.RegularExpressions.Regex.Replace(dica ?? "", @"\s*[(（][^()（）]*[)）]\s*$", "");
        var (tecla, _) = Atalho(id);
        return tecla == Key.None ? sem : $"{sem} ({Rotulo(id)})";
    }

    static string Chave(string id) => "atalho-" + id;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint MapVirtualKeyW(uint codigo, uint tipo);

    /// <summary>
    /// A tecla como esta escrita nela: as de pontuacao ("OemComma",
    /// "OemQuestion") pelo caractere que o teclado de agora poe ali -- no
    /// ABNT2 e no americano a mesma tecla tem simbolos diferentes.
    /// </summary>
    static string NomeDaTecla(Key tecla)
    {
        if (tecla == Key.Delete) return "Del";
        if (tecla.ToString().StartsWith("Oem", StringComparison.Ordinal) || tecla is Key.Add or Key.Subtract)
        {
            try
            {
                uint letra = MapVirtualKeyW((uint)KeyInterop.VirtualKeyFromKey(tecla), 2) & 0x7FFFFFFF;
                if (letra > 32) return ((char)letra).ToString();
            }
            catch (Exception) { }
        }
        return Vinculos.NomeDaTecla(tecla);
    }

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
