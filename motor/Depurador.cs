namespace Simulador;

/// <summary>
/// Pausar o sketch, andar linha a linha e olhar as variáveis.
///
/// Até aqui a única ferramenta de depuração era o `Serial.println` — e
/// ele só mostra o que a pessoa já desconfiava. O depurador mostra onde
/// o programa está e o que cada variável vale naquele instante, sem
/// mudar o sketch.
///
/// O motor avisa a cada comando (<see cref="NoComando"/>), e a pausa é
/// simplesmente **bloquear a thread do sketch** ali. A janela roda em
/// outra thread e continua livre: dá para olhar o circuito, apertar
/// botão, e mandar continuar.
///
/// Três cuidados que não aparecem no primeiro rascunho, e que o teste
/// cobra:
///
/// - **Pausar no meio de um `delay(5000)`.** A pausa só seria atendida
///   no comando seguinte, cinco segundos depois. O `delay` dorme em
///   fatias e chama <see cref="Maquina.PontoDePausa"/> quando há pedido.
/// - **O relógio congela na pausa.** Sem isso, um sketch que mede tempo
///   com `millis()` veria o tempo da pausa passar e "atrasaria" tudo ao
///   continuar.
/// - **Linha com comando aninhado** (`if (x) y = 1;`): o `if` e o
///   `y = 1` estão na mesma linha. Continuar da pausa no `if` pararia de
///   novo no `y = 1`, na mesma linha. A regra: dentro do comando em que
///   parou, não para de novo na mesma linha. A profundidade diz quando
///   ele terminou.
/// </summary>
public sealed class Depurador
{
    /// <summary>Onde parou e o que as variáveis valiam naquele instante.</summary>
    public sealed record Retrato(int Linha, IReadOnlyList<Ambiente.VariavelVista> Variaveis);

    /// <summary>O que a pausa do C/C++ compilado faz ao sair dela.</summary>
    public enum Comando { Continuar, Passo, Sair }

    /// <summary>O sketch compilado (o int3 na DLL); null no interpretador.</summary>
    internal DepuracaoNativa Nativa { get; set; }

    readonly Maquina maq;
    readonly object trava = new();
    readonly HashSet<int> pausas = [];

    /// <summary>Aberto = rodando. Fechado = a thread do sketch espera aqui.</summary>
    readonly ManualResetEventSlim liberado = new(true);

    volatile bool temPausas;
    volatile bool pausaPedida;
    volatile bool modoPasso;

    // Onde parou por último. Enquanto a execução estiver DENTRO desse
    // comando (mais fundo que ele), a mesma linha não para de novo.
    int linhaDaPausa = -1;
    int profundidadeDaPausa = int.MaxValue;

    // O último comando visto: é onde a pausa pedida no meio de um delay
    // diz que o sketch está.
    int linhaAtual = -1;
    int profundidadeAtual;
    Ambiente ambienteAtual;

    public Depurador(Maquina maq)
    {
        this.maq = maq;
        // A máquina dura mais que a rodada: um pedido de pausa que ficou
        // pendurado quando a simulação parou não pode valer na próxima.
        maq.PausaPedida = false;
        maq.PontoDePausa = PausarDentroDaEspera;
    }

    /// <summary>Disparado NA THREAD DO SKETCH ao parar. Quem mostra, despacha.</summary>
    public event Action<Retrato> Pausou;

    /// <summary>Disparado na thread do sketch ao voltar a rodar.</summary>
    public event Action Retomou;

    public bool Pausado { get; private set; }

    /// <summary>Troca as linhas marcadas. Vale até com o sketch rodando.</summary>
    public void DefinirPausas(IEnumerable<int> linhas)
    {
        lock (trava)
        {
            pausas.Clear();
            foreach (int linha in linhas) pausas.Add(linha);
            temPausas = pausas.Count > 0;
        }
        Nativa?.DefinirPausas(LinhasMarcadas);
    }

    internal int[] LinhasMarcadas
    {
        get { lock (trava) return [.. pausas]; }
    }

    /// <summary>Para no próximo comando, ou já, se estiver num delay.</summary>
    public void Pausar()
    {
        modoPasso = false;
        pausaPedida = true;
        maq.PausaPedida = true;
        Nativa?.PedirPausa();
    }

    public void Continuar()
    {
        modoPasso = false;
        pausaPedida = false;
        maq.PausaPedida = false;
        liberado.Set();
    }

    /// <summary>
    /// Anda até o próximo comando que não esteja na mesma linha, entrando
    /// em laços e funções. Não pede pausa ao delay: um passo sobre
    /// `delay(1000)` espera o segundo inteiro, como no hardware.
    /// </summary>
    public void Passo()
    {
        modoPasso = true;
        pausaPedida = true;
        liberado.Set();
    }

    /// <summary>Chamado pelo motor antes de cada comando.</summary>
    internal void NoComando(Cmd c, Ambiente amb, int profundidade)
    {
        // Voltou para a altura do comando em que parou (ou acima): ele
        // terminou, e a regra da "mesma linha" deixa de valer.
        if (profundidade <= profundidadeDaPausa)
        {
            linhaDaPausa = -1;
            profundidadeDaPausa = int.MaxValue;
        }

        // Linha de outro arquivo -- uma biblioteca instalada, uma aba .cpp
        // -- nao e lugar de parar: o editor mostra o sketch, e a pausa
        // apontaria uma linha dele que nao tem nada a ver. O passo a passo
        // atravessa a biblioteca como se ela fosse uma funcao pronta.
        if (!Origens.DoSketch(c.Linha)) return;

        linhaAtual = c.Linha;
        profundidadeAtual = profundidade;
        ambienteAtual = amb;

        // Bloco e comando vazio não são lugar de parar: seria uma pausa
        // numa chave "{" sem nada acontecendo.
        if (c is CmdBloco or CmdVazio) return;

        bool mesmoLugar = c.Linha == linhaDaPausa && profundidade > profundidadeDaPausa;

        if (pausaPedida && (!modoPasso || !mesmoLugar))
        {
            EntrarNaPausa(c.Linha, amb, profundidade);
            return;
        }

        if (!temPausas || mesmoLugar) return;

        bool marcada;
        lock (trava) marcada = pausas.Contains(c.Linha);
        if (marcada) EntrarNaPausa(c.Linha, amb, profundidade);
    }

    /// <summary>Chamado pela Máquina de dentro de um delay quando há pedido.</summary>
    void PausarDentroDaEspera()
    {
        if (!pausaPedida || modoPasso)
        {
            maq.PausaPedida = false;
            return;
        }
        if (Nativa is { } nativa)
        {
            // O compilado: a linha e as variaveis vem da pilha da thread
            // (a entrada da ponte). Sem linha do sketch nela, o int3 em
            // todas as linhas para na proxima.
            maq.PausaPedida = false;
            switch (nativa.PausarNaEspera())
            {
                case Comando.Sair: throw maq.MotivoDaParada();
                case Comando.Continuar: nativa.Seguir(); break;
            }
            return;
        }
        EntrarNaPausa(linhaAtual, ambienteAtual, profundidadeAtual);
    }

    /// <summary>
    /// A pausa do compilado (o int3, ou o Pausar no delay): na thread que
    /// parou, devolve o que fazer -- o C++ nao recebe excecao do .NET.
    /// </summary>
    internal Comando EntrarNaPausaNativa(int linha, IReadOnlyList<Ambiente.VariavelVista> variaveis)
    {
        pausaPedida = false;
        modoPasso = false;
        maq.PausaPedida = false;
        if (maq.Parar) return Comando.Sair;

        liberado.Reset();
        Pausado = true;
        maq.CongelarRelogio();
        try
        {
            Pausou?.Invoke(new Retrato(linha, variaveis));
            while (!liberado.Wait(50))
                if (maq.Parar) break;
        }
        finally
        {
            Pausado = false;
            maq.DescongelarRelogio();
        }

        if (maq.Parar) return Comando.Sair;
        bool passo = modoPasso;
        pausaPedida = false;
        modoPasso = false;
        Retomou?.Invoke();
        return passo ? Comando.Passo : Comando.Continuar;
    }

    void EntrarNaPausa(int linha, Ambiente amb, int profundidade)
    {
        pausaPedida = false;
        modoPasso = false;
        maq.PausaPedida = false;

        linhaDaPausa = linha;
        profundidadeDaPausa = profundidade;

        // O retrato é tirado AQUI, na thread do sketch e com ela parada:
        // a janela recebe uma cópia, e não fica lendo dicionários que a
        // outra thread poderia estar mexendo.
        var retrato = new Retrato(linha, amb?.Listar() ?? []);

        liberado.Reset();
        Pausado = true;
        maq.CongelarRelogio();
        try
        {
            Pausou?.Invoke(retrato);

            // Espera em fatias para ver o Parar: parar a simulação com o
            // sketch pausado não pode deixar a thread presa aqui.
            while (!liberado.Wait(50))
                if (maq.Parar) break;
        }
        finally
        {
            Pausado = false;
            maq.DescongelarRelogio();
        }

        if (maq.Parar) throw maq.MotivoDaParada();
        Retomou?.Invoke();
    }
}
