namespace Simulador;

/// <summary>
/// Um pedaco de um vetor de varias dimensoes: "cubo[1]" e o plano 1,
/// "cubo[1][2]" a linha 2 dele. Nao e copia -- le e escreve no vetor de
/// verdade, como o endereco que o C passa para a funcao.
/// </summary>
public sealed class PedacoDeVetor(Valor[] vetor, int inicio, int[] dimensoes)
{
    public Valor[] Vetor { get; } = vetor;
    public int Inicio { get; } = inicio;

    /// <summary>As dimensoes do pedaco: [3, 4] no plano de um int[2][3][4].</summary>
    public int[] Dimensoes { get; } = dimensoes;
}

/// <summary>
/// Os vetores de varias dimensoes: "byte quadros[10][8][8]" das animacoes
/// de matriz de LED, "bool cubo[4][4][4]" do cubo de LEDs. Guardados como o
/// C guarda -- um bloco so, esticado --, com as dimensoes de dentro ao lado.
/// </summary>
public partial class Interpretador
{
    static int Produto(IEnumerable<int> dimensoes) => dimensoes.Aggregate(1, (a, b) => a * b);

    /// <summary>
    /// "cubo[i][j][k]", "teclas[l][c]", "plano[j][k]" (o plano e um pedaco):
    /// desce a corrente de colchetes ate o vetor e devolve o vetor esticado,
    /// a posicao do primeiro item e as dimensoes que ainda faltam indexar.
    /// Falso: nao e vetor de varias dimensoes (nem pedaco de um).
    /// </summary>
    bool PosicaoNoVetor(Indice ix, Ambiente amb, out Valor[] vetor, out int posicao, out int[] faltam, out string nome)
    {
        vetor = null;
        posicao = 0;
        faltam = null;
        nome = null;

        var indices = new List<Expr>();
        Expr e = ix;
        while (e is Indice i)
        {
            indices.Add(i.Idx);
            e = i.Alvo;
        }
        if (e is not Variavel v) return false;
        nome = v.Nome;

        if (!DimensoesDoNome(v.Nome, amb, out vetor, out posicao, out var dims) || indices.Count > dims.Length) return false;

        indices.Reverse();
        int passo = Produto(dims);
        for (int k = 0; k < indices.Count; k++)
        {
            passo /= dims[k];
            posicao += (int)Avaliar(indices[k], amb).Num * passo;
        }
        faltam = dims[indices.Count..];
        return true;
    }

    /// <summary>O vetor de varias dimensoes com este nome, ou o pedaco guardado nele, com todas as dimensoes.</summary>
    bool DimensoesDoNome(string nome, Ambiente amb, out Valor[] vetor, out int inicio, out int[] dims)
    {
        inicio = 0;
        dims = null;
        if (!amb.Buscar(nome, out var guardado))
        {
            if (!amb.BuscarArray(nome, out vetor) || !amb.DimensoesDe(nome, out var internas)) return false;
            dims = [Math.Max(1, vetor.Length / Math.Max(1, Produto(internas))), .. internas];
            return true;
        }
        vetor = null;
        if (guardado.Referencia is not PedacoDeVetor f) return false;
        vetor = f.Vetor;
        inicio = f.Inicio;
        dims = f.Dimensoes;
        return true;
    }

    /// <summary>
    /// A leitura: o item, quando vieram todos os indices; senao o pedaco --
    /// e a linha de um vetor de char e o texto dela ("nomes[2]").
    /// </summary>
    bool LerDoVetorDeDimensoes(Indice ix, Ambiente amb, out Valor valor)
    {
        valor = Valor.Vazio;
        if (!PosicaoNoVetor(ix, amb, out var vetor, out int posicao, out var faltam, out string nome)) return false;
        int tamanho = Produto(faltam);
        if (posicao < 0 || posicao + tamanho > vetor.Length)
            throw new ErroDeExecucao($"indice fora do vetor '{nome}' (ele tem {vetor.Length} itens ao todo)", ix.Linha);

        if (faltam.Length == 0) valor = vetor[posicao];
        else if (faltam.Length == 1 && vetor.Length > 0 && vetor[0].Caractere)
            valor = Valor.DeTexto(TextoDoVetor(vetor[posicao..(posicao + tamanho)]));
        else valor = Valor.DeReferencia(new PedacoDeVetor(vetor, posicao, faltam));
        return true;
    }

    /// <summary>"desenhar(quadros[2])", ou o pedaco que a funcao recebeu e repassa: o parametro e o pedaco.</summary>
    PedacoDeVetor PedacoDoArgumento(Expr arg, Ambiente amb)
    {
        var sem = SemEndereco(arg);
        if (sem is Indice ix && PosicaoNoVetor(ix, amb, out var vetor, out int posicao, out var faltam, out _)
            && faltam.Length > 0 && !(faltam.Length == 1 && vetor.Length > 0 && vetor[0].Caractere))
            return new PedacoDeVetor(vetor, posicao, faltam);
        if (sem is Variavel v && amb.Buscar(v.Nome, out var guardado) && guardado.Referencia is PedacoDeVetor f) return f;
        return null;
    }

    /// <summary>
    /// O que um "for (x : seq)" percorre, quando seq tem varias dimensoes
    /// (ou e um pedaco): o vetor, onde comeca e as dimensoes. Nulo: o for de sempre.
    /// </summary>
    (Valor[] Vetor, int Inicio, int[] Dims)? DimensoesDaSequencia(Expr seq, Ambiente amb)
    {
        var sem = SemEndereco(seq);
        if (sem is Variavel v && DimensoesDoNome(v.Nome, amb, out var vetor, out int inicio, out var dims)) return (vetor, inicio, dims);
        if (sem is Indice ix && PosicaoNoVetor(ix, amb, out var doIndice, out int posicao, out var faltam, out _) && faltam.Length > 0)
            return (doIndice, posicao, faltam);
        return null;
    }

    /// <summary>
    /// "for (auto &amp;plano : cubo)": cada volta e o pedaco seguinte (que le e
    /// escreve no vetor de verdade); na ultima dimensao, o item -- por
    /// referencia com o &amp;. A linha de um vetor de char e o texto dela.
    /// </summary>
    void PercorrerDimensoes(CmdParaCadaC pc, (Valor[] Vetor, int Inicio, int[] Dims) seq, Ambiente amb)
    {
        var (vetor, inicio, dims) = seq;
        int passo = Produto(dims[1..]);
        for (int i = 0; i < dims[0]; i++)
        {
            if (maq.Parar) throw maq.MotivoDaParada();
            var escopo = new Ambiente(amb);
            int pos = inicio + i * passo;
            if (pos + passo > vetor.Length) break;

            if (dims.Length == 1)
            {
                if (pc.PorReferencia)
                    escopo.Declarar(pc.Nome, Valor.DeReferencia(new Apelido(
                        () => vetor[pos],
                        novo => vetor[pos] = vetor[pos].Caractere ? Valor.ComoCaractere(novo) : novo)));
                else if (EhCaractere(pc.Tipo)) escopo.DeclararCaractere(pc.Nome, vetor[pos]);
                else escopo.Declarar(pc.Nome, Coagir(vetor[pos], pc.Tipo));
            }
            else if (dims.Length == 2 && vetor.Length > 0 && vetor[0].Caractere)
                escopo.Declarar(pc.Nome, Valor.DeTexto(TextoDoVetor(vetor[pos..(pos + passo)])));
            else escopo.Declarar(pc.Nome, Valor.DeReferencia(new PedacoDeVetor(vetor, pos, dims[1..])));

            Exec(pc.Corpo, escopo);
            if (SaiuDoLaco()) break;
        }
    }

    /// <summary>
    /// O vetor de tres ou mais dimensoes, esticado. Cada chave e um pedaco
    /// da dimensao seguinte e o pedaco curto completa com zeros; numero
    /// solto segue enchendo, como o C deixa; texto enche uma linha de char.
    /// </summary>
    Valor[] MontarVetorDeDimensoes(CmdDecl d, int primeira, Ambiente amb)
    {
        int[] internas = [.. d.Dimensoes.Select(x => Math.Max(1, (int)Avaliar(x, amb).Num))];
        int passo = Produto(internas);
        var itens = d.Itens ?? [];
        if (primeira <= 0)
            primeira = itens.All(x => x is ListaLiteral)
                ? itens.Count
                : (Contar(itens) + passo - 1) / passo;
        int[] dims = [Math.Max(1, primeira), .. internas];

        var vetor = new Valor[Produto(dims)];
        Array.Fill(vetor, ZeroDe(d.Tipo));
        Encher(itens, 0, 0);
        return vetor;

        static int Contar(List<Expr> lista) => lista.Sum(x => x is ListaLiteral l ? Contar(l.Itens) : 1);

        // Enche o pedaco do nivel (dims[nivel..]) que comeca em "inicio".
        void Encher(List<Expr> lista, int nivel, int inicio)
        {
            int tamanho = Produto(dims[nivel..]);
            int filho = nivel + 1 < dims.Length ? Produto(dims[(nivel + 1)..]) : 1;
            int pos = inicio;
            void Alinhar()
            {
                int rel = pos - inicio;
                if (rel % filho != 0) pos += filho - rel % filho;
            }
            void Exigir(int quantos)
            {
                if (pos + quantos > inicio + tamanho)
                    throw new ErroDeExecucao($"itens demais na inicializacao de '{d.Nome}' (excess elements in array initializer)", d.Linha);
            }

            foreach (var item in lista)
            {
                if (item is ListaLiteral sub)
                {
                    if (nivel + 1 < dims.Length)
                    {
                        Alinhar();
                        Exigir(filho);
                        Encher(sub.Itens, nivel + 1, pos);
                        pos += filho;
                    }
                    else
                    {
                        // "{5}" no lugar de um numero: o numero.
                        Exigir(1);
                        vetor[pos] = sub.Itens.Count > 0 ? Coagir(Avaliar(sub.Itens[0], amb), d.Tipo) : ZeroDe(d.Tipo);
                        pos++;
                    }
                    continue;
                }

                var valor = Avaliar(item, amb);
                if (valor.EhTexto && EhDeBytes(d.Tipo))
                {
                    // O texto enche a proxima linha de char (a ultima dimensao),
                    // com o '\0' no fim -- o C++ recusa o que nao cabe com ele.
                    int linha = dims[^1];
                    if (pos % linha != 0) pos += linha - pos % linha;
                    var bytes = TextoArduino.ParaBytes(valor.Texto);
                    if (bytes.Length >= linha)
                        throw new ErroDeExecucao($"o texto \"{valor.Texto}\" tem {bytes.Length} letras e nao cabe numa linha de "
                                                 + $"{linha} (sobra lugar para o '\\0' so ate {linha - 1})", d.Linha);
                    Exigir(linha);
                    bool deCaractere = EhCaractere(d.Tipo);
                    for (int k = 0; k < bytes.Length; k++)
                        vetor[pos + k] = Coagir(deCaractere ? Valor.DeCaractere(bytes[k]) : Valor.DeInt(bytes[k]), d.Tipo);
                    pos += linha;
                    continue;
                }

                Exigir(1);
                vetor[pos++] = Coagir(valor, d.Tipo);
            }
        }
    }

    /// <summary>O sizeof de "cubo[0]", "cubo[0][0]": o pedaco que sobra, em bytes. Nulo: nao e vetor de varias dimensoes.</summary>
    int? BytesDoPedaco(Indice ix, Ambiente amb)
    {
        int profundidade = 0;
        Expr raiz = ix;
        while (raiz is Indice r)
        {
            profundidade++;
            raiz = r.Alvo;
        }
        if (raiz is not Variavel dono || !amb.BuscarArray(dono.Nome, out var vetor)) return null;
        int item = BytesDoItem(amb.TipoDe(dono.Nome), vetor);
        if (!amb.DimensoesDe(dono.Nome, out var internas)) return item;
        return Produto(internas.Skip(profundidade - 1)) * item;
    }

    /// <summary>"int m[][4]" na funcao: as dimensoes do parametro, ou as do vetor que chegou.</summary>
    void DimensoesDoParametro(Param par, Expr arg, Ambiente amb, Ambiente local)
    {
        if (par.Dimensoes is { Count: > 0 })
            local.DeclararDimensoes(par.Nome, [.. par.Dimensoes.Select(x => Math.Max(1, (int)Avaliar(x, amb).Num))]);
        else if (SemEndereco(arg) is Variavel v && !amb.Buscar(v.Nome, out _) && amb.DimensoesDe(v.Nome, out var internas))
            local.DeclararDimensoes(par.Nome, internas);
    }
}
