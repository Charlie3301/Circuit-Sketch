namespace Simulador.App;

/// <summary>
/// Quanto cada eixo de um motor de passo já girou.
///
/// O motor de passo biaxial era um dos que "desenhavam e não simulavam":
/// o sketch acionava as bobinas e nada acontecia na tela. Aqui a conta é
/// feita a partir dos próprios pinos, sem depender da biblioteca — o
/// aluno pode escrever a sequência à mão com `digitalWrite`, que é
/// exatamente o exercício de motor de passo, e continua funcionando.
///
/// Não é um modelo físico: não há torque, inércia nem perda de passo. É
/// a contagem que o eixo faria se cada avanço de fase virasse um passo,
/// que é o que o aluno precisa ver para saber se a sequência dele está
/// certa — e, principalmente, para ver o motor **andar para trás** quando
/// a sequência está invertida.
/// </summary>
public sealed class EixoDePasso
{
    /// <summary>
    /// O motor de passo da peca "stepper-motor" (e cada eixo do biaxial) e o
    /// NEMA 17 bipolar do Wokwi: 200 passos por volta, 1,8 grau por passo.
    ///
    /// Era 5,625 -- o meio passo do ROTOR do 28BYJ-48, sem a reducao --, e
    /// um Stepper(200, ...) dava 3 voltas e pouco num step(200). O 28BYJ-48
    /// agora e peca propria, com a reducao de 63,68:1.
    /// </summary>
    public const double GrausPorPassoNema = 1.8;

    /// <summary>O 28BYJ-48 no eixo de saida: 32 passos no rotor, reducao de 63,68395:1.</summary>
    public const double GrausPorPasso28byj = 360 / (32 * 63.68395);

    public EixoDePasso(double grausPorPasso = GrausPorPassoNema) => GrausPorPasso = grausPorPasso;

    public double GrausPorPasso { get; }

    /// <summary>
    /// A posição em MEIOS passos: o campo das bobinas aponta em oito
    /// direções (de 45 em 45 graus elétricos), e o meio passo é uma delas.
    /// </summary>
    public int MeiosPassos { get; private set; }

    public int Passos => MeiosPassos / 2;

    public double Angulo => MeiosPassos * GrausPorPasso / 2;

    // As bobinas na ordem A+, B+, A-, B-; a direção do campo de antes (-1: sem campo).
    readonly bool[] bobinas = new bool[4];
    int direcaoAnterior = -1;
    long proximoEvento = -1;

    /// <summary>
    /// Atualiza a partir das quatro bobinas (A+, B+, A-, B-), como estão
    /// agora -- o jeito do quadro da tela, para quem não tem os eventos.
    /// </summary>
    public void Atualizar(bool[] ligadas)
    {
        for (int i = 0; i < 4 && i < ligadas.Length; i++) bobinas[i] = ligadas[i];
        Mover();
    }

    /// <summary>
    /// Segue o campo a cada mudança de pino da placa, e não a cada quadro
    /// da tela: um motor a 170 passos por segundo muda de fase três vezes
    /// entre dois quadros, e a leitura por quadro perdia esses passos.
    ///
    /// O rotor vai para onde o campo aponta: a corrente de cada bobina é a
    /// diferença entre as suas duas pontas (A+ menos A-, B+ menos B-). É
    /// isso que dá o comportamento da bancada -- inclusive o motor que só
    /// treme, sem girar, quando a ordem dos pinos no Stepper está errada (o
    /// campo vira do avesso a cada passo, e o rotor não sabe para que lado ir).
    /// </summary>
    public void Seguir(Simulador.Maquina maq, int[] gpios)
    {
        lock (maq.Trava)
        {
            var eventos = maq.Eventos;
            long inicio = maq.EventosAntesDaLista;
            if (proximoEvento < 0 || proximoEvento > inicio + eventos.Count)
            {
                // A primeira olhada, ou a placa reiniciou: comeca do estado de agora.
                for (int k = 0; k < 4; k++) bobinas[k] = gpios[k] >= 0 && maq.Digital[gpios[k]] != 0;
                direcaoAnterior = Direcao();
                proximoEvento = inicio + eventos.Count;
                return;
            }
            // As escritas de um passo caem no mesmo milissegundo: o rotor so
            // sente o estado em que elas terminam (os estados de passagem duram
            // microssegundos, e ele tem inercia).
            long msDoGrupo = long.MinValue;
            bool pendente = false;
            for (int i = (int)Math.Max(0, proximoEvento - inicio); i < eventos.Count; i++)
            {
                var e = eventos[i];
                int k = Array.IndexOf(gpios, e.Pino);
                if (k < 0) continue;
                if (pendente && e.Ms != msDoGrupo) Mover();
                bobinas[k] = e.Valor != 0;
                msDoGrupo = e.Ms;
                pendente = true;
            }
            if (pendente) Mover();
            proximoEvento = inicio + eventos.Count;

            // Mudanca sem evento (quem mexe nos pinos por fora): o estado de agora.
            bool mudou = false;
            for (int k = 0; k < 4; k++)
            {
                bool agora = gpios[k] >= 0 && (maq.Digital[gpios[k]] != 0 || maq.Pwm[gpios[k]] > 127);
                if (agora != bobinas[k]) { bobinas[k] = agora; mudou = true; }
            }
            if (mudou) Mover();
        }
    }

    int Direcao()
    {
        int x = (bobinas[0] ? 1 : 0) - (bobinas[2] ? 1 : 0);
        int y = (bobinas[1] ? 1 : 0) - (bobinas[3] ? 1 : 0);
        if (x == 0 && y == 0) return -1;
        double graus = Math.Atan2(y, x) * 180 / Math.PI;
        return (int)Math.Round((graus + 360) % 360 / 45) % 8;
    }

    void Mover()
    {
        int d = Direcao();
        if (d < 0) return;   // sem campo: o rotor fica onde esta
        if (direcaoAnterior >= 0)
        {
            int dif = ((d - direcaoAnterior) % 8 + 8) % 8;
            if (dif is >= 1 and <= 3) MeiosPassos += dif;
            else if (dif is >= 5 and <= 7) MeiosPassos -= 8 - dif;
            // 4: o campo virou do avesso -- o rotor treme no lugar.
        }
        direcaoAnterior = d;
    }

    public void Zerar()
    {
        MeiosPassos = 0;
        direcaoAnterior = -1;
        proximoEvento = -1;
        Array.Clear(bobinas);
    }
}

/// <summary>
/// Os passos que um driver de motor de passo (A4988, DRV8825) da: cada
/// borda de subida no STEP anda um micropasso, para o lado do DIR naquele
/// instante. Conta pelos eventos da placa, e nao pelo quadro da tela -- um
/// motor a 1000 passos por segundo da 40 passos entre dois quadros.
///
/// Guarda tudo em 1/32 de passo (o menor micropasso do DRV8825): um passo
/// cheio sao 32 unidades, um 1/16 do A4988 sao 2.
/// </summary>
public sealed class ContadorDePassos
{
    public long Unidades { get; private set; }

    public double Passos => Unidades / 32.0;

    long proximoEvento = -1;
    int stepAntes, dirAgora;

    /// <param name="divisao">O micropasso agora (1, 2, 4, 8, 16 ou 32).</param>
    /// <param name="habilitado">O driver esta acordado, sem reset e com energia.</param>
    /// <param name="dirInvertido">O DIR chega por um caminho que inverte (nao acontece com fio).</param>
    public void Seguir(Simulador.Maquina maq, int gpioStep, int gpioDir, int divisao, bool habilitado)
    {
        if (gpioStep < 0) return;
        int passo = 32 / Math.Clamp(divisao, 1, 32);
        lock (maq.Trava)
        {
            var eventos = maq.Eventos;
            long inicio = maq.EventosAntesDaLista;
            if (proximoEvento < 0 || proximoEvento > inicio + eventos.Count)
            {
                stepAntes = maq.Digital[gpioStep];
                dirAgora = gpioDir >= 0 ? maq.Digital[gpioDir] : 0;
                proximoEvento = inicio + eventos.Count;
                return;
            }
            for (int i = (int)Math.Max(0, proximoEvento - inicio); i < eventos.Count; i++)
            {
                var e = eventos[i];
                if (e.Pino == gpioDir) { dirAgora = e.Valor != 0 ? 1 : 0; continue; }
                if (e.Pino != gpioStep) continue;
                int nivel = e.Valor != 0 ? 1 : 0;
                if (nivel == 1 && stepAntes == 0 && habilitado) Unidades += dirAgora == 1 ? passo : -passo;
                stepAntes = nivel;
            }
            proximoEvento = inicio + eventos.Count;
        }
    }

    /// <summary>Passos por um STEP que o hardware gera sozinho (PWM no pino), neste intervalo.</summary>
    public void SomarPeriodico(double hz, double segundos, int dir, int divisao, bool habilitado)
    {
        if (!habilitado || hz <= 0 || segundos <= 0) return;
        fracao += hz * segundos;
        long inteiros = (long)Math.Floor(fracao);
        fracao -= inteiros;
        Unidades += (dir == 1 ? 1 : -1) * inteiros * (32 / Math.Clamp(divisao, 1, 32));
    }

    double fracao;

    public void Zerar()
    {
        Unidades = 0;
        proximoEvento = -1;
        fracao = 0;
    }
}
