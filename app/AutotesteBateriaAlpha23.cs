namespace Simulador.App;

/// <summary>
/// Os projetos da bateria com as pecas da Alpha 23, abertos e rodando na
/// janela de verdade: o motor tem que girar, o passo andar, o servo mexer, o
/// ESC armar -- e nenhuma peca reclamar. O Bateria_Completa so prova que eles
/// compilam e rodam; este prova que o circuito montado funciona.
/// </summary>
public static partial class Autoteste
{
    static void Bateria_PecasDaAlpha23()
    {
        saida.AppendLine("bateria: as pecas da Alpha 23 funcionando no circuito");
        string pasta = AcharABateria();
        if (pasta is null)
        {
            Conferir("bateria (Alpha 23): a pasta existe", false, "nao achei bateria/");
            return;
        }

        // Arquivo, a peca que mexe, como medir, e quantos segundos rodar.
        (string Arquivo, string Mexe, string Como, double Segundos)[] casos =
        [
            ("l298n", "motor", "rpm:60", 2.6),
            ("l293d", "motor", "rpm:800", 2.6),
            ("l9110s", "motor", "rpm:60", 2.6),
            ("tb6612fng", "motor", "rpm:60", 2.6),
            ("drv8833", "motor", "rpm:60", 2.6),
            ("bts7960", "motor", "rpm:60", 2.6),
            ("motor-dc", "peca", "rpm:800", 2.6),
            ("motor-tt", "peca", "rpm:60", 2.6),
            ("roda-tt", "motor", "rpm:60", 2.6),
            ("helice-130", "motor", "rpm:800", 2.6),
            ("a4988", "motor", "passo:90", 1.6),
            ("drv8825", "motor", "passo:90", 1.6),
            ("stepper-motor", "peca", "passo:90", 1.6),
            ("uln2003", "motor", "passo:30", 1.8),
            ("motor-28byj48", "peca", "passo:30", 1.8),
            ("servo-mg996r", "peca", "servo:60", 2.6),
            ("servo-360", "peca", "giro", 1.4),
            ("esc", "motor", "rpm:1500", 6.8),
            ("motor-brushless", "peca", "rpm:1500", 6.8),
            ("helice-1045", "motor", "rpm:1500", 6.8),
            ("helice-5045", "motor", "rpm:1500", 6.8),
            ("esc-4em1", "m1", "rpm:1500", 6.8),
            ("receptor-rc", "peca", "serial:receptor pronto", 1.2),
            ("radio-controle", "rx", "serial:receptor pronto", 1.2),
            ("micro-switch", "peca", "serial:botao: 1", 1.2),
        ];

        foreach (var (arquivo, mexe, como, segundos) in casos)
        {
            string caminho = System.IO.Path.Combine(pasta, "componentes", arquivo + ".sktch");
            if (!System.IO.File.Exists(caminho))
            {
                Conferir($"bateria (Alpha 23): {arquivo}.sktch existe", false, caminho);
                continue;
            }

            var (circuito, codigo) = Sktch.Abrir(caminho);
            var janela = new MainWindow();
            try
            {
                janela.TesteAbrir(circuito, codigo);
                janela.TesteRodar();
                var ed = janela.TesteEditorCircuito;
                var maquina = janela.TesteMaquinaDa("placa");

                double maiorRpm = 0, menorAngulo = double.MaxValue, maiorAngulo = double.MinValue, maiorGiro = 0, empuxo = 0;
                var avisos = new HashSet<string>();
                string[] partes = como.Split(':', 2);
                double limite = partes.Length > 1 && double.TryParse(partes[1], System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double l) ? l : 0;
                string serial = "";
                bool ok = false;
                string visto = "";

                void Medir()
                {
                    serial = string.Join(" | ", LinhasDoSerial(maquina));
                    (ok, visto) = partes[0] switch
                    {
                        "rpm" => (maiorRpm >= limite, $"{maiorRpm:0} rpm"),
                        "passo" => (Math.Abs(maiorAngulo) >= limite || Math.Abs(menorAngulo) >= limite, $"eixo de {menorAngulo:0} a {maiorAngulo:0} graus"),
                        "servo" => (maiorAngulo - menorAngulo >= limite, $"braco de {menorAngulo:0} a {maiorAngulo:0} graus"),
                        "giro" => (maiorGiro > 0, $"{maiorGiro:0} graus/s"),
                        "serial" => (serial.Contains(partes[1]), serial.Length > 160 ? serial[..160] : serial),
                        _ => (false, "medida desconhecida: " + como),
                    };
                }

                // Pelo menos o tempo do caso (o aviso que aparece no meio tambem
                // conta); e, num computador lento, ate o triplo esperando a medida
                // chegar -- o sketch roda no relogio de parede, e um PC sem folga
                // (na bateria, a 1 GHz) anda devagar sem nada estar errado.
                var relogio = System.Diagnostics.Stopwatch.StartNew();
                while (relogio.Elapsed.TotalSeconds < segundos || (!ok && relogio.Elapsed.TotalSeconds < 3 * segundos))
                {
                    EsperarPor(() => false, 120);
                    maiorRpm = Math.Max(maiorRpm, Math.Abs(ed.RpmDoMotor(mexe)));
                    double angulo = como.StartsWith("passo") ? ed.AnguloDoPassoParaTeste(mexe) : ed.AnguloDoServoParaTeste(mexe);
                    menorAngulo = Math.Min(menorAngulo, angulo);
                    maiorAngulo = Math.Max(maiorAngulo, angulo);
                    maiorGiro = Math.Max(maiorGiro, Math.Abs(ed.VelocidadeDoServoParaTeste(mexe)));
                    empuxo = Math.Max(empuxo, ed.EmpuxoParaTeste(mexe));
                    foreach (var estouro in ed.EstourosPelaEnergia()) avisos.Add($"{estouro.Peca}: {estouro.Motivo}");
                    Medir();
                }

                // Com CS_FOTOS apontando uma pasta, cada circuito sai em foto, rodando.
                if (Environment.GetEnvironmentVariable("CS_FOTOS") is { Length: > 0 } fotos && System.IO.Directory.Exists(fotos))
                    FotografarEditor(ed, System.IO.Path.Combine(fotos, arquivo + ".png"), 1100, 620);

                // Roda e helice: encaixadas no eixo do motor ao abrir o projeto.
                string encaixe = arquivo is "roda-tt" or "helice-130" or "helice-1045" or "helice-5045"
                    ? ed.MotorDoAcessorioParaTeste("peca") : null;
                if (arquivo is "roda-tt" or "helice-130" or "helice-1045" or "helice-5045")
                {
                    ok &= encaixe == "motor";
                    visto += $"; encaixada em '{encaixe}'";
                }
                if (arquivo is "helice-130" or "helice-1045" or "helice-5045")
                {
                    ok &= empuxo > 0;
                    visto += $", {empuxo:0} gf";
                }

                saida.AppendLine($"        {arquivo,-16} {visto}" + (avisos.Count > 0 ? "  AVISOS: " + string.Join(" / ", avisos) : ""));
                Conferir($"bateria (Alpha 23): {arquivo} funciona no circuito, sem aviso nenhum",
                    ok && avisos.Count == 0, visto + (avisos.Count > 0 ? " | " + string.Join(" / ", avisos) : ""));
            }
            finally
            {
                janela.TesteParar();
                try { janela.Close(); } catch (InvalidOperationException) { }
            }
        }
    }
}
