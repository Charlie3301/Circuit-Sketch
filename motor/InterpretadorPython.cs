namespace Simulador;

/// <summary>
/// O que o MicroPython tem e o C do Arduino nao tinha.
///
/// O interpretador e UM so para as duas linguagens -- e o que faz um
/// LED aceso por "digitalWrite" e um aceso por "pino.value(1)"
/// acenderem exatamente do mesmo jeito na tela. O que muda de uma para
/// a outra esta todo aqui:
///
///   - lista, tupla e dicionario, que em C eram vetor com nome proprio;
///   - o "for x in sequencia", que nao conta indice;
///   - atribuicao que DECLARA a variavel, em vez de exigir o tipo antes;
///   - divisao que sempre da real, e "//" para a inteira;
///   - "%" e .format() montando texto;
///   - as embutidas (print, len, range, str...).
///
/// As pontes com as PECAS ficam no arquivo vizinho
/// (InterpretadorMicroPython.cs): aqui e so a linguagem.
/// </summary>
public partial class Interpretador
{
    /// <summary>
    /// Modulos que este sketch importou: nome local -> modulo de verdade.
    ///
    /// "import utime as t" deixa "t" apontando para "utime"; sem o mapa,
    /// "t.sleep(1)" nao acharia ninguem.
    /// </summary>
    readonly Dictionary<string, string> modulosImportados = [];

    /// <summary>
    /// As tres palavras que o Python escreve com maiuscula.
    ///
    /// None nao e zero: uma funcao sem return devolve None, e o sketch
    /// que escreve "if resposta is None" precisa que os dois sejam
    /// coisas diferentes.
    /// </summary>
    static bool ConstantePython(string nome, out Valor v)
    {
        switch (nome)
        {
            case "True": v = Valor.DeBool(true); return true;
            case "False": v = Valor.DeBool(false); return true;
            case "None": v = Valor.Nenhum; return true;
            default: v = Valor.Vazio; return false;
        }
    }

    // =====================================================================
    //  comandos
    // =====================================================================
    void ExecImporta(CmdImporta c, Ambiente amb)
    {
        if (!ModulosPy.Existe(c.Modulo))
            throw new ErroDeExecucao(
                $"o modulo '{c.Modulo}' ainda nao existe no simulador. "
                + "Os que existem sao: " + string.Join(", ", ModulosPy.Conhecidos), c.Linha);

        if (c.Nomes.Count == 0)
        {
            string local = c.Apelido ?? c.Modulo.Split('.')[0];
            modulosImportados[local] = c.Modulo;
            amb.Declarar(local, Valor.DeReferencia(new ModuloPy(c.Modulo)));
            return;
        }

        foreach (string nome in c.Nomes)
        {
            if (nome == "*")
            {
                foreach (string n in ModulosPy.NomesDe(c.Modulo))
                    amb.Declarar(n, Valor.DeReferencia(new MembroDeModuloPy(c.Modulo, n)));
                continue;
            }

            amb.Declarar(nome, Valor.DeReferencia(new MembroDeModuloPy(c.Modulo, nome)));
        }
    }

    void ExecParaCada(CmdParaCada c, Ambiente amb)
    {
        var sequencia = Avaliar(c.Sequencia, amb);

        foreach (var item in Percorrer(sequencia, c.Linha))
        {
            if (Maq.Parar) throw new ExecucaoInterrompida();

            // Um nome so: "for p in lista". Varios: "for i, v in
            // enumerate(lista)", que desempacota cada item.
            if (c.Nomes.Count == 1) DeclararOuAtribuir(c.Nomes[0], item, amb);
            else
            {
                var partes = item.Referencia as ListaPy;
                if (partes is null || partes.Itens.Count != c.Nomes.Count)
                    throw new ErroDeExecucao(
                        $"este for espera {c.Nomes.Count} valores por volta, "
                        + "mas a sequencia nao os entrega em grupos desse tamanho", c.Linha);

                for (int i = 0; i < c.Nomes.Count; i++)
                    DeclararOuAtribuir(c.Nomes[i], partes.Itens[i], amb);
            }

            Exec(c.Corpo, amb);
            if (SaiuDoLaco()) break;
        }
    }

    /// <summary>
    /// A atribuicao do Python: se o nome nao existe, ele passa a existir.
    ///
    /// E a diferenca de fundo entre as duas linguagens. Em C o sketch
    /// escreve "int x = 0;" e o tipo fica; aqui "x = 0" cria x na hora,
    /// e "x = 'oi'" logo depois troca o conteudo sem reclamar.
    /// </summary>
    void DeclararOuAtribuir(string nome, Valor v, Ambiente amb)
    {
        if (!amb.Atribuir(nome, v)) amb.Declarar(nome, v);
    }

    /// <summary>
    /// O que "for", "in" e as embutidas percorrem.
    ///
    /// Texto entrega letra por letra e dicionario entrega as CHAVES --
    /// as duas regras do Python que costumam pegar quem vem do C.
    /// </summary>
    IEnumerable<Valor> Percorrer(Valor v, int linha)
    {
        switch (v.Referencia)
        {
            case ListaPy l:
                // Copia: "for x in lista: lista.append(x)" nao pode virar
                // laco infinito dentro do simulador.
                foreach (var item in l.Itens.ToList()) yield return item;
                yield break;

            case DicionarioPy d:
                foreach (string chave in d.Ordem.ToList()) yield return Valor.DeTexto(chave);
                yield break;
        }

        if (v.EhTexto)
        {
            foreach (char ch in v.Texto) yield return Valor.DeTexto(ch.ToString());
            yield break;
        }

        throw new ErroDeExecucao(
            "este valor nao e uma sequencia: so lista, tupla, dicionario, "
            + "texto e range() podem ser percorridos", linha);
    }

    // =====================================================================
    //  expressoes
    // =====================================================================
    Valor MontarLista(ListaLiteral l, Ambiente amb)
        => Valor.DeReferencia(new ListaPy([.. l.Itens.Select(i => Avaliar(i, amb))], l.EhTupla));

    Valor MontarDicionario(DicionarioLiteral d, Ambiente amb)
    {
        var dic = new DicionarioPy();
        for (int i = 0; i < d.Chaves.Count; i++)
            dic.Por(Avaliar(d.Chaves[i], amb).Mostrar(), Avaliar(d.Valores[i], amb));
        return Valor.DeReferencia(dic);
    }

    Valor Fatiar(Fatia f, Ambiente amb)
    {
        var alvo = Avaliar(f.Alvo, amb);

        int total = alvo.Referencia is ListaPy l ? l.Itens.Count
                  : alvo.EhTexto ? alvo.Texto.Length
                  : throw new ErroDeExecucao("so lista e texto podem ser fatiados", f.Linha);

        int inicio = f.Inicio is null ? 0 : Limitar((int)Avaliar(f.Inicio, amb).Num, total);
        int fim = f.Fim is null ? total : Limitar((int)Avaliar(f.Fim, amb).Num, total);
        int quantos = Math.Max(0, fim - inicio);

        return alvo.EhTexto
            ? Valor.DeTexto(alvo.Texto.Substring(inicio, quantos))
            : Valor.DeReferencia(new ListaPy(
                ((ListaPy)alvo.Referencia).Itens.GetRange(inicio, quantos)));
    }

    /// <summary>
    /// O limite de uma fatia: negativo conta do fim, e passar do
    /// tamanho nao e erro -- e assim que "linha[:-1]" tira o ultimo
    /// caractere e "x[0:99]" devolve o que houver.
    /// </summary>
    static int Limitar(int i, int total)
    {
        if (i < 0) i += total;
        return Math.Clamp(i, 0, total);
    }

    /// <summary>Indexacao de lista, dicionario ou texto do Python.</summary>
    bool LerIndicePython(Indice ix, Ambiente amb, out Valor v)
    {
        v = Valor.Vazio;
        var alvo = Avaliar(ix.Alvo, amb);

        switch (alvo.Referencia)
        {
            case ListaPy lista:
            {
                int i = lista.Posicao((int)Avaliar(ix.Idx, amb).Num);
                if (i < 0)
                    throw new ErroDeExecucao(
                        $"indice fora da lista, que tem {lista.Itens.Count} itens", ix.Linha);
                v = lista.Itens[i];
                return true;
            }

            case DicionarioPy dic:
            {
                string chave = Avaliar(ix.Idx, amb).Mostrar();
                if (!dic.Itens.TryGetValue(chave, out v))
                    throw new ErroDeExecucao(
                        $"a chave '{chave}' nao esta no dicionario"
                        + (dic.Ordem.Count == 0 ? ", que esta vazio"
                           : ". Tem: " + string.Join(", ", dic.Ordem)), ix.Linha);
                return true;
            }
        }

        if (alvo.EhTexto)
        {
            string s = alvo.Texto;
            int i = (int)Avaliar(ix.Idx, amb).Num;
            if (i < 0) i += s.Length;
            if (i < 0 || i >= s.Length)
                throw new ErroDeExecucao(
                    $"indice fora do texto, que tem {s.Length} letras", ix.Linha);
            v = Valor.DeTexto(s[i].ToString());
            return true;
        }

        return false;
    }

    /// <summary>Atribuicao do Python: cria variavel, escreve em lista e em dicionario.</summary>
    bool GuardarPython(Expr alvo, Valor v, Ambiente amb)
    {
        switch (alvo)
        {
            case Variavel va:
                DeclararOuAtribuir(va.Nome, v, amb);
                return true;

            case Indice ix:
            {
                var dono = Avaliar(ix.Alvo, amb);

                if (dono.Referencia is ListaPy lista)
                {
                    int i = lista.Posicao((int)Avaliar(ix.Idx, amb).Num);
                    if (i < 0)
                        throw new ErroDeExecucao(
                            $"indice fora da lista, que tem {lista.Itens.Count} itens", ix.Linha);
                    lista.Itens[i] = v;
                    return true;
                }

                if (dono.Referencia is DicionarioPy dic)
                {
                    dic.Por(Avaliar(ix.Idx, amb).Mostrar(), v);
                    return true;
                }

                // "rgb[3] = (148, 0, 211)": a fita de LEDs se comporta
                // como lista, mas quem guarda a cor e a peca.
                if (dono.Referencia is Objeto peca)
                {
                    EscreverNoObjeto(peca, (int)Avaliar(ix.Idx, amb).Num, v, ix.Linha);
                    return true;
                }

                return false;
            }

            // "tupla[0], tupla[1] = ..." nao existe aqui, mas
            // "objeto.campo = x" sim.
            case Membro mb:
            {
                var dono = Avaliar(mb.Obj, amb);
                if (dono.Referencia is not Objeto o) return false;
                o.Campos[mb.Nome] = v;
                return true;
            }
        }

        return false;
    }

    /// <summary>Leitura de "objeto.campo" e de constante de modulo.</summary>
    bool MembroPython(Membro m, Ambiente amb, out Valor v)
    {
        v = Valor.Vazio;

        // "machine.Pin" e "Pin.OUT": nao e um objeto, e o caminho para
        // dentro de um modulo.
        var dono = AvaliarSeDer(m.Obj, amb);
        if (dono is null) return false;

        switch (dono.Value.Referencia)
        {
            case ModuloPy mod:
                v = ValorDeModulo(mod.Nome, m.Nome, m.Linha);
                return true;

            case MembroDeModuloPy membro:
                v = ValorDeModulo(membro.Modulo, membro.Nome + "." + m.Nome, m.Linha);
                return true;

            case Objeto o when o.Campos.TryGetValue(m.Nome, out var campo):
                v = campo;
                return true;
        }

        return false;
    }

    /// <summary>
    /// Avalia sem estourar quando o nome ainda nao existe.
    ///
    /// Serve para distinguir "Pin.OUT" (modulo) de "resultado.valor"
    /// (objeto) sem transformar todo erro de nome numa excecao engolida.
    /// </summary>
    Valor? AvaliarSeDer(Expr e, Ambiente amb)
    {
        if (e is Variavel v)
            return amb.Buscar(v.Nome, out var achado) ? achado : null;

        try { return Avaliar(e, amb); }
        catch (ErroDeExecucao) { return null; }
    }

    // =====================================================================
    //  operadores
    // =====================================================================
    /// <summary>
    /// As contas em que o Python difere do C.
    ///
    /// Devolve false quando a conta e a mesma nas duas linguagens, e
    /// entao quem resolve e o <c>Operar</c> de sempre.
    /// </summary>
    bool OperarPython(string op, Valor x, Valor y, int linha, out Valor r)
    {
        r = Valor.Vazio;

        // Lista + lista junta as duas; lista * n repete.
        if (x.Referencia is ListaPy a)
        {
            if (op == "+" && y.Referencia is ListaPy b)
            {
                r = Valor.DeReferencia(new ListaPy([.. a.Itens, .. b.Itens], a.EhTupla));
                return true;
            }
            if (op == "*")
            {
                var itens = new List<Valor>();
                for (int i = 0; i < (int)y.Num; i++) itens.AddRange(a.Itens);
                r = Valor.DeReferencia(new ListaPy(itens, a.EhTupla));
                return true;
            }
            if (op is "==" or "!=")
            {
                bool igual = y.Referencia is ListaPy c && MesmaLista(a, c);
                r = Valor.DeBool(op == "==" ? igual : !igual);
                return true;
            }
        }

        // "is None" chega aqui como "== None".
        if (op is "==" or "!=" && (x.EhNenhum || y.EhNenhum))
        {
            bool igual = x.EhNenhum && y.EhNenhum;
            r = Valor.DeBool(op == "==" ? igual : !igual);
            return true;
        }

        switch (op)
        {
            // "texto" * 3 repete o texto.
            case "*" when x.EhTexto && !y.EhTexto:
                r = Valor.DeTexto(string.Concat(Enumerable.Repeat(x.Texto, Math.Max(0, (int)y.Num))));
                return true;

            // "%": em Python monta texto quando a esquerda e texto.
            case "%" when x.EhTexto:
                r = Valor.DeTexto(FormatarPorcento(
                    x.Texto,
                    y.Referencia is ListaPy lista ? lista.Itens : [y],
                    linha));
                return true;

            // A divisao do Python SEMPRE da real -- 7/2 e 3.5, e nao 3.
            // E a pegadinha numero um de quem vem do C.
            case "/":
                if (y.Num == 0) throw new ErroDeExecucao("divisao por zero", linha);
                r = Valor.DeReal(x.Num / y.Num);
                return true;

            // "//": a inteira, arredondando para baixo (inclusive negativo).
            case "//":
                if (y.Num == 0) throw new ErroDeExecucao("divisao por zero", linha);
                r = x.Inteiro && y.Inteiro
                    ? Valor.DeInt(Math.Floor(x.Num / y.Num))
                    : Valor.DeReal(Math.Floor(x.Num / y.Num));
                return true;

            // O resto do Python acompanha o sinal do DIVISOR: -1 % 3 e 2.
            case "%":
            {
                if (y.Num == 0) throw new ErroDeExecucao("resto por zero", linha);
                double resto = x.Num - Math.Floor(x.Num / y.Num) * y.Num;
                r = x.Inteiro && y.Inteiro ? Valor.DeInt(resto) : Valor.DeReal(resto);
                return true;
            }
        }

        return false;
    }

    static bool MesmaLista(ListaPy a, ListaPy b)
        => a.Itens.Count == b.Itens.Count
           && a.Itens.Zip(b.Itens).All(par => par.First.Mostrar() == par.Second.Mostrar());

    /// <summary>
    /// O "%s" e "%d" do Python, que e o mesmo do printf.
    /// </summary>
    static string FormatarPorcento(string molde, List<Valor> args, int linha)
    {
        var saida = new System.Text.StringBuilder();
        int proximo = 0;

        for (int i = 0; i < molde.Length; i++)
        {
            if (molde[i] != '%') { saida.Append(molde[i]); continue; }
            if (i + 1 < molde.Length && molde[i + 1] == '%') { saida.Append('%'); i++; continue; }

            int j = i + 1;
            while (j < molde.Length && !char.IsLetter(molde[j])) j++;
            if (j >= molde.Length)
                throw new ErroDeExecucao("faltou a letra depois do '%'", linha);

            string largura = molde[(i + 1)..j];
            char tipo = molde[j];
            var v = proximo < args.Count ? args[proximo++] : Valor.DeTexto("");

            saida.Append(UmCampo(tipo, largura, v, linha));
            i = j;
        }

        return saida.ToString();
    }

    static string UmCampo(char tipo, string largura, Valor v, int linha)
    {
        string texto = tipo switch
        {
            's' => TextoPy.De(v),
            'd' or 'i' => ((long)v.Num).ToString(),
            'f' or 'F' => v.Num.ToString(
                "F" + (largura.Contains('.') ? largura.Split('.')[1] : "6"),
                System.Globalization.CultureInfo.InvariantCulture),
            'x' => ((long)v.Num).ToString("x"),
            'X' => ((long)v.Num).ToString("X"),
            'o' => Convert.ToString((long)v.Num, 8),
            'b' => Convert.ToString((long)v.Num, 2),
            'c' => ((char)(int)v.Num).ToString(),
            'e' => v.Num.ToString("e6", System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new ErroDeExecucao($"'%{tipo}' nao e um formato conhecido", linha)
        };

        string so = largura.Split('.')[0];
        if (so.Length == 0) return texto;

        bool aEsquerda = so.StartsWith('-');
        bool comZero = so.TrimStart('-').StartsWith('0');
        if (!int.TryParse(so.TrimStart('-', '0'), out int n)) return texto;

        return aEsquerda ? texto.PadRight(n)
             : comZero ? texto.PadLeft(n, '0')
             : texto.PadLeft(n);
    }
}
