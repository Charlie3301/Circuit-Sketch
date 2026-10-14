namespace Simulador.App;

/// <summary>
/// Os drivers de motor de passo de STEP/DIR (A4988 e DRV8825): os pinos,
/// a tabela de micropasso, a faixa do VMOT e como cada um acorda.
///
/// Os dois ficam parados de um jeito que pega todo iniciante, e o
/// simulador pega igual: o RST solto deixa o chip em reset (a Pololu manda
/// ligar o RST no SLP, que a placa ja puxa para cima), e com 5 V no VMOT
/// ele nao liga -- o minimo e 8 V.
/// </summary>
public sealed record DriverDePasso(
    string Tipo, string Nome,
    string[] Bobina1, string[] Bobina2,
    string Step, string Dir, string Enable, string Reset, string Sleep,
    string[] Micropasso, Func<int, int, int, int> Divisao,
    string Vmot, string Vdd, double VmotMinima, double VmotMaxima, int MenorPasso)
{
    public static readonly IReadOnlyDictionary<string, DriverDePasso> Todos = new[]
    {
        // MS1 MS2 MS3: LLL cheio, HLL 1/2, LHL 1/4, HHL 1/8, HHH 1/16 (datasheet Allegro).
        new DriverDePasso("a4988", "A4988", ["1A", "1B"], ["2A", "2B"], "STEP", "DIR", "EN", "RST", "SLP",
            ["MS1", "MS2", "MS3"],
            (a, b, c) => (a, b, c) switch
            {
                (1, 0, 0) => 2, (0, 1, 0) => 4, (1, 1, 0) => 8, (1, 1, 1) => 16, _ => 1,
            },
            "VMOT", "VDD", 8, 35, 16),

        // M0 M1 M2: 000 cheio, 100 1/2, 010 1/4, 110 1/8, 001 1/16, o resto 1/32 (datasheet TI).
        new DriverDePasso("drv8825", "DRV8825", ["A1", "A2"], ["B1", "B2"], "STEP", "DIR", "EN", "RST", "SLP",
            ["M0", "M1", "M2"],
            (a, b, c) => (a, b, c) switch
            {
                (0, 0, 0) => 1, (1, 0, 0) => 2, (0, 1, 0) => 4, (1, 1, 0) => 8, (0, 0, 1) => 16, _ => 32,
            },
            "VMOT", null, 8.2, 45, 32),
    }.ToDictionary(d => d.Tipo);

    public static DriverDePasso De(string tipo) => tipo is null ? null : Todos.GetValueOrDefault(tipo);

    public IEnumerable<string> Saidas => Bobina1.Concat(Bobina2);
}
