namespace Simulador;

/// <summary>
/// A Stepper.h do Arduino: o setSpeed() vira o intervalo entre passos
/// (60 s / passos por volta / rpm), e o step() BLOQUEIA ate o ultimo passo
/// -- cada um esperando o seu intervalo, com a sequencia de duas bobinas da
/// biblioteca (1010, 0110, 0101, 1001). Antes o step() voltava na hora e
/// acendia uma bobina so. A espera e de processador (a biblioteca gira em
/// volta do micros()): num ESP8266, um step() longo derruba a placa pelo
/// watchdog, como na bancada.
/// </summary>
public partial class Interpretador
{
    /// <summary>A sequencia de cada passo, pino 1 primeiro (o stepMotor() da biblioteca).</summary>
    static readonly int[][] SequenciaDe4Fios = [[1, 0, 1, 0], [0, 1, 1, 0], [0, 1, 0, 1], [1, 0, 0, 1]];
    static readonly int[][] SequenciaDe2Fios = [[0, 1], [1, 1], [1, 0], [0, 0]];

    /// <summary>Stepper(passosPorVolta, p1, p2[, p3, p4]): os pinos viram saida, como no construtor.</summary>
    void MontarMotorDePasso(Objeto obj, List<Valor> args)
    {
        obj.Pinos = [.. args.Skip(1).Select(a => (int)a.Num)];
        obj.Campos["_passosPorVolta"] = Valor.DeInt(args.Count > 0 ? Math.Max(1, (int)args[0].Num) : 200);
        obj.Campos["_atrasoUs"] = Valor.DeInt(0);
        obj.Campos["_numero"] = Valor.DeInt(0);
        foreach (int p in obj.Pinos) maq.PinMode(p, 1);
    }

    Valor ChamarMotorDePasso(Objeto obj, string metodo, Chamada c, Ambiente amb)
    {
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        int passosPorVolta = (int)obj.Campos.GetValueOrDefault("_passosPorVolta", Valor.DeInt(200)).Num;
        switch (metodo)
        {
            case "setSpeed":
            {
                double rpm = args.Count > 0 ? args[0].Num : 0;
                // step_delay = 60L * 1000L * 1000L / number_of_steps / whatSpeed (em long: trunca).
                long atraso = rpm > 0 ? 60L * 1000 * 1000 / passosPorVolta / (long)rpm : 0;
                obj.Campos["_atrasoUs"] = Valor.DeInt(atraso);
                return Valor.Vazio;
            }

            case "step":
            {
                long pedidos = args.Count > 0 ? (long)args[0].Num : 0;
                long atraso = (long)obj.Campos["_atrasoUs"].Num;
                int numero = (int)obj.Campos["_numero"].Num;
                int sentido = pedidos >= 0 ? 1 : -1;
                for (long feitos = 0; feitos < Math.Abs(pedidos); feitos++)
                {
                    if (maq.Parar) throw maq.MotivoDaParada();
                    if (atraso > 0) maq.EsperarMicros(atraso);
                    ConferirWatchdogs();

                    if (sentido > 0)
                    {
                        numero++;
                        if (numero == passosPorVolta) numero = 0;
                    }
                    else
                    {
                        if (numero == 0) numero = passosPorVolta;
                        numero--;
                    }
                    var fase = obj.Pinos.Length >= 4 ? SequenciaDe4Fios[numero % 4]
                             : obj.Pinos.Length >= 2 ? SequenciaDe2Fios[numero % 4] : [];
                    for (int k = 0; k < fase.Length && k < obj.Pinos.Length; k++)
                        maq.DigitalWrite(obj.Pinos[k], fase[k]);
                    obj.Passo += sentido;
                }
                obj.Campos["_numero"] = Valor.DeInt(numero);
                return Valor.Vazio;
            }

            case "version": return Valor.DeInt(5);
        }
        throw new ErroDeExecucao($"Stepper::{metodo}() ainda nao e suportado pelo simulador.", c.Linha);
    }
}
