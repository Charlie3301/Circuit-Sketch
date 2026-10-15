namespace Simulador.App;

/// <summary>
/// O Texto (a anotacao) e a juncao de fios, revistos na Alpha 25.
/// </summary>
public static partial class Autoteste
{
    static void Anotacao_TextoEJuncao()
    {
        saida.AppendLine("anotacao: o texto e a juncao de fios");

        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 40, Y = 60, Usb = true });
        c.Pecas.Add(new PecaNoCircuito { Id = "t1", Tipo = "texto", X = 260, Y = 40 });
        c.Pecas.Add(new PecaNoCircuito { Id = "t2", Tipo = "texto", X = 260, Y = 90, Nome = "Sensor de luz da sala" });
        c.Pecas.Add(new PecaNoCircuito { Id = "j", Tipo = "junction", X = 320, Y = 200 });
        c.Pecas.Add(new PecaNoCircuito { Id = "l1", Tipo = "led", X = 420, Y = 140 });
        c.Pecas.Add(new PecaNoCircuito { Id = "l2", Tipo = "led", X = 420, Y = 240 });
        c.Fios.Add(new Fio { DePeca = "esp", DePino = "D2", ParaPeca = "j", ParaPino = "4" });
        c.Fios.Add(new Fio { DePeca = "j", DePino = "1", ParaPeca = "l1", ParaPino = "A" });
        c.Fios.Add(new Fio { DePeca = "j", DePino = "3", ParaPeca = "l2", ParaPino = "A" });
        var ed = new EditorCircuito();
        ed.Carregar(c);
        if (Environment.GetEnvironmentVariable("CS_FOTOS") is { Length: > 0 } fotos && System.IO.Directory.Exists(fotos))
            FotografarEditor(ed, System.IO.Path.Combine(fotos, "anotacao.png"), 1400, 900);

        var vazio = ed.CaixaDoTextoParaTeste("t1");
        var umaLinha = ed.CaixaDoTextoParaTeste("t2");
        var t2 = c.Achar("t2");
        ed.DefinirNome(t2, "Sensor de luz\r\nda sala", avisar: false);
        var duas = ed.CaixaDoTextoParaTeste("t2");
        Conferir("texto: Shift+Enter quebra a linha (a caixa dobra de altura) e o \\r some",
            t2.Nome == "Sensor de luz\nda sala" && duas.Height > umaLinha.Height * 1.6 && vazio.Width > 40,
            $"{umaLinha.Height:0} -> {duas.Height:0}");

        var rede = new Netlist(c);
        Conferir("juncao: o fio que chega num lado sai pelos outros tres (os dois LEDs no D2)",
            rede.GpioDe(c, "l1", "A") == 2 && rede.GpioDe(c, "l2", "A") == 2);
    }
}
