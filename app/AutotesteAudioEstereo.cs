namespace Simulador.App;

/// <summary>
/// O som em estereo (Alpha 24): a saida do I2S guarda os dois canais; o
/// alto-falante (no MAX98357A) toca a media, o PCM5102A toca os dois no fone
/// -- e so quando esta ligado nos pinos em que o sketch abriu o I2S.
/// </summary>
public static partial class Autoteste
{
    static void Audio_Estereo()
    {
        saida.AppendLine("audio estereo e o PCM5102A");

        // ---- a saida guarda os dois canais ----
        var som = new SaidaDeAudio(44100, 26, 25, 22, 1);
        som.EscreverEstereo([1000, -1000, 3000, 1000]);
        var estereo = new short[4];
        int quadros = som.LerEstereo(estereo);
        Conferir("estereo: os dois canais saem separados (E, D)", quadros == 2 && estereo.SequenceEqual(new short[] { 1000, -1000, 3000, 1000 }),
            string.Join(",", estereo));
        som.EscreverEstereo([1000, -1000, 3000, 1000]);
        var mono = new short[2];
        som.Ler(mono);
        Conferir("estereo: quem toca em mono recebe a media dos dois (como o MAX98357A de fabrica)", mono[0] == 0 && mono[1] == 2000,
            string.Join(",", mono));
        som.Escrever([500]);
        var dobrado = new short[2];
        som.LerEstereo(dobrado);
        Conferir("estereo: o som mono vai igual para os dois lados", dobrado[0] == 500 && dobrado[1] == 500, string.Join(",", dobrado));

        // ---- o PCM5102A: so toca ligado direito ----
        SaidaDeAudio Tocando(Action<Circuito> mexer)
        {
            var c = new Circuito();
            c.Pecas.AddRange([
                new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Usb = true },
                new PecaNoCircuito { Id = "dac", Tipo = "pcm5102a", X = 400, Y = 0 }]);
            void Ligar(string pd, string pp) => c.Fios.Add(new Fio { DePeca = "dac", DePino = pd, ParaPeca = "placa", ParaPino = pp });
            Ligar("VIN", "3V3"); Ligar("GND", "GND.1"); Ligar("SCK", "GND.2");
            Ligar("BCK", "D26"); Ligar("LCK", "D25"); Ligar("DIN", "D22");
            mexer?.Invoke(c);
            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.Audio = new SaidaDeAudio(44100, 26, 25, 22, 1);
            return ed.SomDoPcm5102a(Simulador.App.Bancada.DeUmaSo(maq), new Netlist(c));
        }
        Conferir("PCM5102A: VIN, GND, SCK no GND e o BCK/LCK/DIN nos pinos do I2S -- toca", Tocando(null) is not null);
        Conferir("PCM5102A: sem o VIN, nao toca", Tocando(c => c.Fios.RemoveAll(f => f.DePino == "VIN")) is null);
        Conferir("PCM5102A: o DIN num pino que nao e o do I2S, nao toca",
            Tocando(c => c.Fios.Single(f => f.DePino == "DIN").ParaPino = "D21") is null);
        Conferir("PCM5102A: o SCK no 3V3 (sem o clock), nao toca",
            Tocando(c => c.Fios.Single(f => f.DePino == "SCK").ParaPino = "3V3") is null);
        Conferir("PCM5102A: esta no catalogo, com desenho, na categoria dos modulos",
            Catalogo.Obter("pcm5102a") is { Pinos.Count: 6 } && Pecas.Existe("pcm5102a"));
    }
}
