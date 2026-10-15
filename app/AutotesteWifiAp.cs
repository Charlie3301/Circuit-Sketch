namespace Simulador.App;

/// <summary>
/// O ponto de acesso redesenhado (Alpha 25, pecas-wifi-ap.py): um roteador de
/// verdade, com as quatro luzes funcionando -- a energia com a simulacao, o
/// WiFi com a rede nomeada, os clientes com uma placa conectada nela, e a
/// internet com o "Usar internet".
/// </summary>
public static partial class Autoteste
{
    static void Rede_Roteador()
    {
        saida.AppendLine("rede: o roteador (wifi-ap) e as luzes dele");

        var def = Catalogo.Obter("wifi-ap");
        Conferir("wifi-ap: a caixa do roteador (151x113 px), sem pinos", def.Largura == 151 && def.Altura == 113 && def.Pinos.Count == 0,
            $"{def.Largura}x{def.Altura}");

        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
        var ap = new PecaNoCircuito { Id = "ap", Tipo = "wifi-ap", X = 400, Y = 0, Ssid = "Casa", Senha = "" };
        c.Pecas.Add(ap);
        var rede = new Simulador.Rede { UsarInternet = false };
        rede.DefinirPontosDeAcesso([new PontoDeAcesso("Casa", "")]);
        var ed = new EditorCircuito { EmSimulacao = false, RedeDoRoteador = rede };
        ed.Carregar(c);
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maq.Reiniciar();
        var b = Simulador.App.Bancada.DeUmaSo(maq);
        var netlist = new Netlist(c);
        bool[] Luzes()
        {
            ed.AvancarEnergiaParaTeste(b, netlist, 0.04);
            return [.. Enumerable.Range(0, 4).Select(i => ed.LuzDeModuloAcesaParaTeste("ap", i))];
        }
        string Ver(bool[] l) => string.Concat(l.Select(x => x ? '1' : '0'));

        var parado = Luzes();
        ed.EmSimulacao = true;
        var ligado = Luzes();
        rede.ConectarWifi(maq, "Casa");
        maq.Avancar(Simulador.Rede.TempoParaConectar + 10);
        var cliente = Luzes();
        rede.UsarInternet = true;
        var internet = Luzes();
        // Com CS_FOTOS apontando uma pasta, o roteador aceso sai em foto.
        if (Environment.GetEnvironmentVariable("CS_FOTOS") is { Length: > 0 } fotos && System.IO.Directory.Exists(fotos))
            FotografarEditor(ed, System.IO.Path.Combine(fotos, "roteador-aceso.png"), 1400, 700);
        ap.Ssid = "";
        var semNome = Luzes();
        Conferir("wifi-ap: parado tudo apagado; rodando, energia e WiFi; com a placa conectada, clientes; com internet, o globo",
            Ver(parado) == "0000" && Ver(ligado) == "1100" && Ver(cliente) == "1110" && Ver(internet) == "1111" && Ver(semNome) == "1001",
            $"{Ver(parado)} {Ver(ligado)} {Ver(cliente)} {Ver(internet)} {Ver(semNome)}");
    }
}
