namespace Simulador.App;

/// <summary>
/// Os "Outros" redesenhados (Alpha 25): o RTC DS1307 e o microSD saem dos
/// arquivos de projeto da Adafruit e da SparkFun (sem os logotipos, que sao
/// marcas registradas), e o receptor infravermelho, o controle e o pendrive
/// sao desenhados a mao. O LED do receptor acende enquanto chega o sinal.
/// </summary>
public static partial class Autoteste
{
    static void Outros_Desenho()
    {
        saida.AppendLine("outros: DS1307, microSD, infravermelho e pendrive");

        var semMarca = new List<string>();
        foreach (string nome in new[] { "ds1307", "microsd-card", "ir-receiver", "ir-remote", "pendrive-usb", "pendrive-usb-corpo", "pendrive-usb-movel" })
        {
            var svg = Pecas.TodosOsSvgs().FirstOrDefault(s => s.nome == nome).texto;
            if (svg is null || svg.Contains("adafruit", StringComparison.OrdinalIgnoreCase) || svg.Contains("sparkfun", StringComparison.OrdinalIgnoreCase))
                semMarca.Add(nome);
        }
        Conferir("os desenhos existem, sem o nome nem o logotipo da Adafruit e da SparkFun", semMarca.Count == 0, string.Join(", ", semMarca));

        // o LED de sinal do receptor: apagado parado, aceso com uma tecla do controle apertada
        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true });
        c.Pecas.Add(new PecaNoCircuito { Id = "ir", Tipo = "ir-receiver", X = 300, Y = 0 });
        var controle = new PecaNoCircuito { Id = "ctl", Tipo = "ir-remote", X = 500, Y = 0 };
        c.Pecas.Add(controle);
        c.Fios.Add(new Fio { DePeca = "esp", DePino = "3V3", ParaPeca = "ir", ParaPino = "VCC" });
        c.Fios.Add(new Fio { DePeca = "esp", DePino = "GND.1", ParaPeca = "ir", ParaPino = "GND" });
        c.Fios.Add(new Fio { DePeca = "esp", DePino = "D15", ParaPeca = "ir", ParaPino = "DAT" });
        var ed = new EditorCircuito { EmSimulacao = true };
        ed.Carregar(c);
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maq.Reiniciar();
        var b = Simulador.App.Bancada.DeUmaSo(maq);
        var rede = new Netlist(c);
        ed.AvancarEnergiaParaTeste(b, rede, 0.04);
        bool parado = ed.LuzDeModuloAcesaParaTeste("ir", 0);
        ed.ApertarNoControle(controle, "1");
        ed.AvancarEnergiaParaTeste(b, rede, 0.04);
        bool apertado = ed.LuzDeModuloAcesaParaTeste("ir", 0);
        ed.ApertarNoControle(controle, null);
        ed.AvancarEnergiaParaTeste(b, rede, 0.04);
        bool solto = ed.LuzDeModuloAcesaParaTeste("ir", 0);
        Conferir("receptor IR: o LED de sinal acende enquanto a tecla do controle esta apertada", !parado && apertado && !solto,
            $"{parado}/{apertado}/{solto}");

        // sem alimentacao, o receptor nao acende nada
        c.Fios.RemoveAll(f => f.ParaPino == "VCC");
        ed.Carregar(c);
        rede = new Netlist(c);
        ed.ApertarNoControle(controle, "1");
        ed.AvancarEnergiaParaTeste(b, rede, 0.04);
        Conferir("receptor IR sem o VCC: o LED fica apagado", !ed.LuzDeModuloAcesaParaTeste("ir", 0));
    }
}
