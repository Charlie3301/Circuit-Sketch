using System.Windows;

namespace Simulador.App;

/// <summary>
/// As rodas e helices: pecas sem fio que se encaixam no eixo de um motor
/// (soltar perto do eixo ja encaixa) e giram com ele.
///
/// Encaixadas, mudam o motor: a roda soma inercia (anda no ar, sem carga) e
/// mostra a velocidade na borda; a helice soma inercia e um arrasto que
/// cresce com o quadrado da rotacao -- o A2212 com a 1045 numa 3S para em
/// ~9.000 rpm puxando ~16 A, e nao nos 11.000 de vazio -- e mostra o empuxo,
/// em gramas-forca (~820 gf no maximo, perto dos 800-885 g das tabelas).
/// </summary>
public partial class EditorCircuito
{
    /// <summary>Onde fica o eixo de cada motor que aceita acessorio, em mm da peca.</summary>
    static readonly Dictionary<string, (double X, double Y)> EixosDosMotores = new()
    {
        ["motor-dc"] = (14.0, 12.5),
        ["motor-tt"] = (12.0, 13.0),
        ["motor-brushless"] = (18.0, 18.0),
        ["servo-360"] = (23.15, 12.0),
    };

    /// <summary>
    /// Cada acessorio: em que motores encaixa, a inercia (kg.m2), o arrasto
    /// (N.m por (rad/s)^2), o empuxo (N por (rad/s)^2) e o raio da roda (m).
    /// </summary>
    sealed record Acessorio(string[] Motores, double Inercia, double Arrasto, double Empuxo, double Raio);

    static readonly Dictionary<string, Acessorio> Acessorios = new()
    {
        // A roda de 65 mm (~25 g): 0,5 m r^2.
        ["roda-tt"] = new(["motor-tt", "servo-360"], 1.3e-5, 0, 0, 0.0325),
        // A de tres pas do 130: ~6.000 rpm e ~250 mA com 6 V, ~25 gf.
        ["helice-130"] = new(["motor-dc"], 2.0e-7, 2.47e-9, 6.3e-7, 0),
        // A 1045 (~9 g, 254 mm): ~820 gf e ~16 A no A2212 com 11,1 V.
        ["helice-1045"] = new(["motor-brushless"], 4.8e-5, 1.71e-7, 9.0e-6, 0),
        // A 5045 de tres pas (~4,5 g, 127 mm), a do drone de 5": ~1 kg de empuxo
        // a ~28.000 rpm num 2205 2300KV em 4S, puxando ~30 A.
        ["helice-5045"] = new(["motor-brushless"], 6.0e-6, 1.41e-8, 1.14e-6, 0),
    };

    public static bool EhAcessorio(string tipo) => tipo is not null && Acessorios.ContainsKey(tipo);

    /// <summary>O eixo deste motor, na tela (nulo: o motor nao aceita acessorio).</summary>
    Point? EixoNaTela(PecaNoCircuito motor)
    {
        if (!EixosDosMotores.TryGetValue(motor.Tipo, out var eixo) || Catalogo.Obter(motor.Tipo) is not { } def) return null;
        return Geometria.PosicaoLocal(motor, def, eixo.X * MmRadio, eixo.Y * MmRadio);
    }

    Point CentroDe(PecaNoCircuito peca, PecaDef def) => Geometria.PosicaoLocal(peca, def, def.Largura / 2, def.Altura / 2);

    /// <summary>O motor em cujo eixo este acessorio esta encaixado (o centro a menos de 6 px do eixo).</summary>
    PecaNoCircuito MotorDoAcessorio(PecaNoCircuito acc)
    {
        if (!Acessorios.TryGetValue(acc.Tipo, out var a) || Catalogo.Obter(acc.Tipo) is not { } def) return null;
        var centro = CentroDe(acc, def);
        foreach (var motor in Atual.Pecas)
        {
            if (!a.Motores.Contains(motor.Tipo) || EixoNaTela(motor) is not { } eixo) continue;
            if ((eixo - centro).Length < 6) return motor;
        }
        return null;
    }

    IEnumerable<(PecaNoCircuito Peca, Acessorio Dados)> AcessoriosDoMotor(PecaNoCircuito motor)
    {
        foreach (var acc in Atual.Pecas)
            if (Acessorios.TryGetValue(acc.Tipo, out var dados) && MotorDoAcessorio(acc)?.Id == motor.Id)
                yield return (acc, dados);
    }

    /// <summary>Soltou um acessorio perto de um eixo que serve: encaixa no centro do eixo.</summary>
    void EncaixarAcessorio(string idDaPeca)
    {
        var acc = Atual.Achar(idDaPeca);
        if (acc is null || !Acessorios.TryGetValue(acc.Tipo, out var a) || Catalogo.Obter(acc.Tipo) is not { } def) return;
        var centro = CentroDe(acc, def);
        Vector? melhor = null;
        foreach (var motor in Atual.Pecas)
        {
            if (!a.Motores.Contains(motor.Tipo) || EixoNaTela(motor) is not { } eixo) continue;
            var falta = eixo - centro;
            if (falta.Length < 40 && (melhor is null || falta.Length < melhor.Value.Length)) melhor = falta;
        }
        if (melhor is not { } desvio || desvio.Length < 0.01) return;
        acc.X += desvio.X;
        acc.Y += desvio.Y;
        Reposicionar(acc);
        RedesenharFios();
    }

    /// <summary>A cada quadro, antes da fisica: o que cada motor carrega no eixo.</summary>
    void AplicarCargasDosAcessorios()
    {
        foreach (var motor in Atual.Pecas)
        {
            MotorDc eixo = motor.Tipo switch
            {
                "motor-dc" or "motor-tt" => MotorDcDe(motor),
                "motor-brushless" => MotorSemEscovaDe(motor).Equivalente,
                _ => null,
            };
            if (eixo is null) continue;
            double inercia = 0, arrasto = 0;
            foreach (var (_, dados) in AcessoriosDoMotor(motor))
            {
                inercia += dados.Inercia;
                arrasto += dados.Arrasto;
            }
            eixo.InerciaExtra = inercia;
            eixo.CargaQuadratica = arrasto;
        }
    }

    /// <summary>O que vai depois do rpm no rotulo: a velocidade na borda da roda, o empuxo da helice.</summary>
    string ExtrasDoGiro(PecaNoCircuito motor, double rpm)
    {
        double w = Math.Abs(rpm) * 2 * Math.PI / 60;
        string extras = "";
        foreach (var (_, dados) in AcessoriosDoMotor(motor))
        {
            if (dados.Raio > 0) extras += " · " + (w * dados.Raio).ToString("0.00", Brasil) + " m/s";
            if (dados.Empuxo > 0) extras += " · " + (dados.Empuxo * w * w / 9.80665 * 1000).ToString("#,0", Brasil) + " gf";
        }
        return extras;
    }

    /// <summary>O angulo do desenho do eixo de um motor (o acessorio gira junto).</summary>
    double AnguloDoMotor(PecaNoCircuito motor) => motor.Tipo == "servo-360" ? AnguloDoServo(motor) : AnguloDoEixo(motor);

    double RpmDoEixo(PecaNoCircuito motor) => motor.Tipo == "servo-360"
        ? (servos.TryGetValue(motor.Id, out var e) ? e.Velocidade / 6 : 0)
        : RpmDoMotor(motor.Id);

    /// <summary>Para o teste: o empuxo (gf) da helice encaixada neste motor.</summary>
    internal double EmpuxoParaTeste(string idDoMotor)
    {
        var motor = Atual.Achar(idDoMotor);
        if (motor is null) return 0;
        double w = Math.Abs(RpmDoEixo(motor)) * 2 * Math.PI / 60;
        return AcessoriosDoMotor(motor).Sum(a => a.Dados.Empuxo * w * w / 9.80665 * 1000);
    }

    /// <summary>Para o teste: em que motor este acessorio esta encaixado.</summary>
    internal string MotorDoAcessorioParaTeste(string idDaPeca)
        => Atual.Achar(idDaPeca) is { } p ? MotorDoAcessorio(p)?.Id : null;

    /// <summary>Para o teste: soltar um acessorio (encaixa no eixo mais perto).</summary>
    internal void SoltarAcessorioParaTeste(string idDaPeca) => EncaixarAcessorio(idDaPeca);

    /// <summary>Para o teste: onde fica o eixo do motor na tela (onde a roda encaixa).</summary>
    internal Point? EixoDoMotorParaTeste(string idDoMotor) => Atual.Achar(idDoMotor) is { } motor ? EixoNaTela(motor) : null;
}
