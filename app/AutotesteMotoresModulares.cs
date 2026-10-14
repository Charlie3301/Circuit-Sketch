namespace Simulador.App;

/// <summary>
/// Os motores pela ficha (RS-540, 2205 2300KV, a ficha digitada), a bateria
/// que cede sob carga, a ESC com a corrente e as celulas dela (e queimando
/// quando passa delas), a ESC 4-em-1 e as setas da lista de componentes.
/// </summary>
public static partial class Autoteste
{
    static void Motores_Modulares()
    {
        saida.AppendLine("motores pela ficha, ESC por corrente, 4-em-1");

        static void Ligar(Circuito c, PecaNoCircuito a, string pa, PecaNoCircuito b, string pb)
            => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb });

        // ---- motor de escovas pela ficha, direto na bateria ----
        (double Rpm, double Amperes, List<Estouro> Avisos) NaBateria(string modelo, double volts, double mA, FichaPersonalizada ficha = null, double segundos = 3)
        {
            var c = new Circuito();
            var bat = new PecaNoCircuito { Id = "bat", Tipo = "bateria", X = 400, Y = 300, Volts = volts, MiliAmperes = mA };
            var motor = new PecaNoCircuito { Id = "motor", Tipo = "motor-dc", X = 100, Y = 100, Modelo = modelo, Ficha = ficha };
            c.Pecas.AddRange([bat, motor]);
            Ligar(c, bat, "+", motor, "+");
            Ligar(c, bat, "-", motor, "-");
            var ed = new EditorCircuito { PassoFixoParaTeste = 0.02 };
            ed.Carregar(c);
            var b = Simulador.App.Bancada.DeUmaSo(new Maquina { Perfil = PerfilPlaca.Esp32 });
            var rede = new Netlist(c);
            var avisos = new List<Estouro>();
            for (int i = 0; i < segundos / 0.02; i++)
            {
                ed.AplicarEntradas(b, rede);
                avisos.AddRange(ed.EstourosPelaEnergia());
            }
            return (ed.RpmDoMotor("motor"), Math.Abs(ed.MotorDcParaTeste("motor").Corrente), avisos);
        }

        var (rpm540, a540, _) = NaBateria("rs-540", 7.2, 60000);
        saida.AppendLine($"        RS-540 em 7,2 V (bateria forte): {rpm540:0} rpm, {a540:0.0} A");
        Conferir("ficha RS-540: perto dos 23.400 rpm do catalogo com 7,2 V, puxando ~1,6 A sem carga",
            rpm540 is > 20000 and < 24000 && a540 is > 1.0 and < 2.5, $"{rpm540:0} rpm {a540:0.0} A");

        var (rpmFraca, _, _) = NaBateria("rs-540", 7.2, 1500);
        saida.AppendLine($"        a mesma em 7,2 V com uma bateria fraca (1,5 A): {rpmFraca:0} rpm");
        Conferir("bateria fraca (pouca corrente maxima) cede sob a carga: o motor gira mais devagar",
            rpmFraca < rpm540 * 0.95, $"{rpmFraca:0} x {rpm540:0}");

        var (rpm130, _, _) = NaBateria(null, 6, 2000);
        var (rpm775, _, _) = NaBateria("rs-775", 12, 60000);
        saida.AppendLine($"        130 em 6 V: {rpm130:0} rpm; RS-775 em 12 V: {rpm775:0} rpm");
        Conferir("o padrao continua o 130 de sempre (~9.000 rpm em 6 V), e o RS-775 anda nos ~12.000 da ficha",
            rpm130 is > 8000 and < 9500 && rpm775 is > 10000 and < 12500, $"{rpm130:0} / {rpm775:0}");

        // A ficha digitada, com uma corrente maxima baixa: o aviso aparece.
        var propria = FichaPersonalizada.De(FichasDeMotor.Achar("motor-dc", "rs-540"));
        propria.CorrenteMaxima = 0.5;
        var (_, _, avisosProprios) = NaBateria("personalizado", 7.2, 60000, propria, 3);
        var aviso = avisosProprios.FirstOrDefault(e => e.Peca == "motor");
        saida.AppendLine($"        ficha propria com 0,5 A de maximo: {aviso?.Motivo ?? "(nenhum aviso)"}");
        Conferir("ficha personalizada vale: acima da corrente maxima dela por 2 s, o motor avisa que esquenta",
            aviso is { Queimou: false } && aviso.Motivo.Contains("0,5 A"), aviso?.Motivo ?? "(nada)");

        // ---- sem escova pela ESC: o KV manda na velocidade ----
        (double Rpm, string Fase, List<Estouro> Avisos, double Bateria) PelaEsc(string modelo, double amperesDoEsc, double volts,
            double us, bool helice = false, double segundos = 4, string tipoDoEsc = "esc", int motores = 1)
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true };
            var esc = new PecaNoCircuito { Id = "esc", Tipo = tipoDoEsc, X = 300, Y = 0, Amperes = amperesDoEsc };
            var lipo = new PecaNoCircuito { Id = "lipo", Tipo = "bateria", X = 600, Y = 300, Volts = volts, MiliAmperes = 150000 };
            c.Pecas.AddRange([placa, esc, lipo]);
            Ligar(c, lipo, "+", esc, "BAT+");
            Ligar(c, lipo, "-", esc, "BAT-");
            Ligar(c, placa, "GND.2", esc, "GND");
            string[] gpios = ["D13", "D12", "D14", "D27"];
            for (int k = 0; k < motores; k++)
            {
                var motor = new PecaNoCircuito { Id = k == 0 ? "motor" : $"motor{k + 1}", Tipo = "motor-brushless", X = 200 + 200 * k, Y = 400, Modelo = modelo };
                c.Pecas.Add(motor);
                string sinal = tipoDoEsc == "esc-4em1" ? $"S{k + 1}" : "SINAL";
                Ligar(c, placa, gpios[k], esc, sinal);
                foreach (string f in new[] { "A", "B", "C" })
                    Ligar(c, esc, tipoDoEsc == "esc-4em1" ? $"M{k + 1}{f}" : f, motor, f);
                if (helice && k == 0)
                {
                    double mm = 96 / 25.4;
                    c.Pecas.Add(new PecaNoCircuito { Id = "helice", Tipo = "helice-1045", X = 200 + 18 * mm - 128 * mm, Y = 400 + 18 * mm - 17 * mm });
                }
            }
            var ed = new EditorCircuito { PassoFixoParaTeste = 0.02 };
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            int[] numeros = [13, 12, 14, 27];
            for (int k = 0; k < motores; k++) { maq.PinMode(numeros[k], 1); maq.EscreverServo(numeros[k], 0, 1000); }
            maq.Avancar(25);
            var b = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);
            for (int i = 0; i < 75; i++) ed.AplicarEntradas(b, rede);          // arma (1,5 s embaixo)
            for (int k = 0; k < motores; k++) maq.EscreverServo(numeros[k], 0, us);
            maq.Avancar(25);
            var avisos = new List<Estouro>();
            for (int i = 0; i < segundos / 0.02; i++)
            {
                ed.AplicarEntradas(b, rede);
                avisos.AddRange(ed.EstourosPelaEnergia());
            }
            double rpm = Enumerable.Range(0, motores).Min(k => ed.RpmDoMotor(k == 0 ? "motor" : $"motor{k + 1}"));
            string chave = tipoDoEsc == "esc-4em1" ? "esc#1" : "esc";
            return (rpm, ed.EscParaTeste(chave).Fase, avisos, ed.EnergiaDoQuadro?.CorrenteDaBateria("lipo") ?? 0);
        }

        var a2212 = PelaEsc(null, 30, 14.8, 1500);
        var r2205 = PelaEsc("2205-2300", 30, 14.8, 1500);
        saida.AppendLine($"        4S, acelerador 1500 us: A2212 1000KV {a2212.Rpm:0} rpm; 2205 2300KV {r2205.Rpm:0} rpm");
        Conferir("o KV manda: o 2205 2300KV gira ~2,3x o A2212 1000KV na mesma bateria e acelerador",
            r2205.Rpm / Math.Max(1, a2212.Rpm) is > 2.0 and < 2.6 && a2212.Rpm is > 6500 and < 9000,
            $"{r2205.Rpm:0} / {a2212.Rpm:0}");

        var pequeno = PelaEsc(null, 12, 11.1, 2000, helice: true);
        var queimou = pequeno.Avisos.FirstOrDefault(e => e.Peca == "esc" && e.Queimou);
        saida.AppendLine($"        A2212 + helice 1045 numa ESC de 12 A: {queimou?.Motivo ?? "(nao queimou)"}; motor a {pequeno.Rpm:0} rpm no fim");
        Conferir("ESC de 12 A com a 1045 no maximo (~15 A): queima depois de um segundo, e o motor para",
            queimou is not null && queimou.Motivo.Contains("12 A") && pequeno.Rpm < 3000, queimou?.Motivo ?? "(nada)");

        var acima = PelaEsc(null, 12, 14.8, 1000, segundos: 0.5);
        var deCelulas = acima.Avisos.FirstOrDefault(e => e.Peca == "esc" && e.Queimou);
        Conferir("ESC de 12 A (2S a 3S) numa 4S: queima pela tensao",
            deCelulas is not null && deCelulas.Motivo.Contains("3S"), deCelulas?.Motivo ?? "(nada)");

        var grande = PelaEsc(null, 40, 11.1, 2000, helice: true);
        Conferir("a mesma helice numa ESC de 40 A: sem queimar",
            !grande.Avisos.Any(e => e.Peca == "esc" && e.Queimou) && grande.Rpm > 8000, $"{grande.Rpm:0} rpm");

        // ---- a 4-em-1: quatro motores, uma bateria ----
        var quatro = PelaEsc(null, 45, 14.8, 1300, tipoDoEsc: "esc-4em1", motores: 4);
        var um = PelaEsc(null, 30, 14.8, 1300);
        saida.AppendLine($"        4-em-1 com 4 A2212 em 4S a 1300 us: o mais lento a {quatro.Rpm:0} rpm, "
                         + $"{quatro.Bateria:0.0} A da bateria (um motor sozinho: {um.Bateria:0.0} A)");
        Conferir("ESC 4-em-1: os quatro canais armam e giram, e a bateria entrega a soma dos quatro",
            quatro.Fase == "Armado" && quatro.Rpm > 3000 && quatro.Bateria > um.Bateria * 3.3 && quatro.Bateria < um.Bateria * 4.7,
            $"{quatro.Fase} {quatro.Rpm:0} rpm {quatro.Bateria:0.0}/{um.Bateria:0.0} A");

        // ---- o tamanho: o desenho e os pinos do tamanho da ficha ----
        {
            var def = Catalogo.Obter("motor-dc");
            var p130 = new PecaNoCircuito { Tipo = "motor-dc", X = 0, Y = 0 };
            var p775 = new PecaNoCircuito { Tipo = "motor-dc", X = 0, Y = 0, Modelo = "rs-775" };
            double Vao(PecaNoCircuito p)
            {
                var a = Geometria.PosicaoPino(p, def, def.Pinos.First(x => x.Nome == "+"));
                var b = Geometria.PosicaoPino(p, def, def.Pinos.First(x => x.Nome == "-"));
                return (b - a).Length;
            }
            double e775 = Geometria.Escala(p775);
            saida.AppendLine($"        RS-775: escala {e775:0.00} (lata de 42 mm contra os 20 do 130); vao dos pinos {Vao(p775):0.0} x {Vao(p130):0.0} px");
            Conferir("tamanho: o RS-775 sai 2,1x o 130 (lata de 42 mm), e os pinos se afastam junto",
                Math.Abs(e775 - 2.1) < 0.01 && Math.Abs(Vao(p775) / Vao(p130) - 2.1) < 0.01, $"{e775:0.00}");

            var p1105 = new PecaNoCircuito { Tipo = "motor-brushless", Modelo = "1105-5000" };
            var p2814 = new PecaNoCircuito { Tipo = "motor-brushless", Modelo = "2814-900" };
            double e1105 = Geometria.Escala(p1105), e2814 = Geometria.Escala(p2814);
            saida.AppendLine($"        sem escova: 1105 a {e1105:0.00}x, 2205/A2212 a 1,00x, 2814 a {e2814:0.00}x");
            Conferir("tamanho: o sino do 1105 sai pouco mais da metade do 2212, e o do 2814 maior",
                e1105 is > 0.5 and < 0.6 && e2814 is > 1.2 and < 1.35, $"{e1105:0.00}/{e2814:0.00}");

            foreach (string nome in new[] { "motor-dc-rs-775-corpo", "motor-dc-personalizado-corpo", "motor-brushless-1105-5000-movel", "motor-brushless-2814-900-movel" })
                Conferir($"desenho: {nome}.svg existe (o rotulo do modelo)", Pecas.Existe(nome));
        }

        // ---- a ficha pela medida: as leis de escala dos motores de verdade ----
        {
            var f2205 = FichasDeMotor.Estimar(true, new FichaPersonalizada { RpmOuKv = 2300, MedidaA = 22, MedidaB = 5 });
            var f2814 = FichasDeMotor.Estimar(true, new FichaPersonalizada { RpmOuKv = 2300, MedidaA = 28, MedidaB = 14 });
            var f540 = FichasDeMotor.Estimar(false, new FichaPersonalizada { Volts = 7.2, RpmOuKv = 23400, MedidaA = 35.8, MedidaB = 50 });
            saida.AppendLine($"        estimado: 2205 2300KV R={f2205.Resistencia} ohm, {f2205.CorrenteMaxima} A, I0={f2205.CorrenteVazio} A; "
                             + $"2814 2300KV R={f2814.Resistencia} ohm, {f2814.CorrenteMaxima} A; RS-540 R={f540.Resistencia} ohm, {f540.CorrenteMaxima} A");
            Conferir("medida: o 2205 2300KV estimado fica perto da ficha (R ~0,075 ohm, ~25 A, ~1 A sem carga)",
                f2205.Resistencia is > 0.06 and < 0.09 && f2205.CorrenteMaxima is > 20 and < 30 && f2205.CorrenteVazio is > 0.8 and < 1.3,
                $"{f2205.Resistencia}/{f2205.CorrenteMaxima}/{f2205.CorrenteVazio}");
            Conferir("medida: mesmo KV num estator maior (2814) da menos resistencia e mais corrente",
                f2814.Resistencia < f2205.Resistencia / 2 && f2814.CorrenteMaxima > f2205.CorrenteMaxima * 1.5,
                $"{f2814.Resistencia}/{f2814.CorrenteMaxima}");
            Conferir("medida: o RS-540 estimado pela lata fica na ordem da ficha (R 0,1 ohm, 12 A)",
                f540.Resistencia is > 0.05 and < 0.2 && f540.CorrenteMaxima is > 7 and < 20, $"{f540.Resistencia}/{f540.CorrenteMaxima}");
        }

        // ---- a helice acompanha o eixo do motor maior ----
        {
            var c = new Circuito();
            var motor = new PecaNoCircuito { Id = "motor", Tipo = "motor-brushless", X = 100, Y = 100, Modelo = "2814-900" };
            double mm = 96 / 25.4, s = Geometria.Escala(motor);
            var helice = new PecaNoCircuito { Id = "helice", Tipo = "helice-1045", X = 100 + 18 * mm * s - 128 * mm, Y = 100 + 18 * mm * s - 17 * mm };
            c.Pecas.AddRange([motor, helice]);
            var ed = new EditorCircuito();
            ed.Carregar(c);
            Conferir("tamanho: a helice no eixo do 2814 (desenho 1,2x maior) encaixa nele",
                ed.MotorDoAcessorioParaTeste("helice") == "motor", ed.MotorDoAcessorioParaTeste("helice") ?? "(nada)");
        }

        // ---- o desenho: a ESC pinta na cor e na corrente escolhidas ----
        foreach (string nome in new[] { "esc-60a-rosa", "esc-12a-preto", "esc-4em1", "esc-4em1-35a-verde" })
            Conferir($"desenho: {nome}.svg existe", Pecas.Obter(nome) is not null);

        // ---- a lista de componentes: a seta recolhe a categoria ----
        // A lista pede a janela de dono ja exibida: fora da tela, sem roubar o foco.
        var janela = new MainWindow { ShowActivated = false, WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        try
        {
            janela.Show();
            var paleta = new JanelaPecas(janela) { ShowActivated = false };
            paleta.TesteTrocar("Tudo");
            int antes = paleta.TesteLinhasNaLista;
            paleta.TesteAlternarCategoria("MOTORES");
            int recolhida = paleta.TesteLinhasNaLista;
            paleta.TesteBuscar("esc");
            int buscando = paleta.TesteLinhasNaLista;
            paleta.TesteBuscar("");
            paleta.TesteAlternarCategoria("MOTORES");
            int aberta = paleta.TesteLinhasNaLista;
            saida.AppendLine($"        lista: {antes} linhas; MOTORES recolhida {recolhida}; buscando 'esc' {buscando}; aberta de novo {aberta}");
            Conferir("lista de componentes: a seta recolhe a categoria (some ~30 linhas) e abre de novo; buscando, mostra tudo",
                recolhida < antes - 20 && aberta == antes && buscando >= 3, $"{antes}/{recolhida}/{aberta}/{buscando}");
            paleta.Close();
        }
        finally
        {
            try { janela.Close(); } catch (InvalidOperationException) { }
        }
    }
}
