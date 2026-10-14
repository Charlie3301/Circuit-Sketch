namespace Simulador;

/// <summary>
/// Quanto o sketch ocupa da placa, e se cabe.
///
/// Alpha 18: "limitadores de placas, para RAM de cada placa,
/// processador, cores, e etc. para ter uma experiência mais acurada".
///
/// É a conta que a IDE do Arduino imprime depois de compilar, e que o
/// simulador nunca fazia: um `int tabela[600]` num Uno são 1.200 bytes
/// de 2.048, e mais dois vetores assim a placa reinicia sozinha, sem
/// mensagem nenhuma. Aqui isso vira um número antes de rodar.
///
/// A conta é uma ESTIMATIVA, e é honesta sobre isso: soma o que as
/// declarações globais do sketch ocupam e acrescenta o que o core gasta
/// por conta própria. Não é o mapa de memória do avr-gcc -- que também
/// depende de otimização, de biblioteca e da versão do compilador --,
/// mas erra para o mesmo lado e pela mesma ordem de grandeza, que é o
/// que faz a diferença entre "cabe" e "não cabe".
/// </summary>
public readonly record struct Orcamento(PerfilPlaca Placa, int Ram, int Flash)
{
    public double UsoDaRam => Placa.RamBytes <= 0 ? 0 : (double)Ram / Placa.RamBytes;
    public double UsoDoFlash => Placa.FlashBytes <= 0 ? 0 : (double)Flash / Placa.FlashBytes;

    public bool RamEstourou => Ram > Placa.RamBytes;
    public bool FlashEstourou => Flash > Placa.FlashBytes;
    public bool Estourou => RamEstourou || FlashEstourou;

    /// <summary>Sobra pouca RAM: a partir daqui o programa fica instável.</summary>
    public bool RamApertada => !RamEstourou && UsoDaRam >= 0.85;

    /// <summary>As duas linhas que a IDE imprime depois de compilar.</summary>
    public string EmTexto()
        => $"programa: {Flash:N0} bytes ({UsoDoFlash:P0}) de {Placa.FlashBytes:N0} de flash"
           + Environment.NewLine
           + $"variáveis: {Ram:N0} bytes ({UsoDaRam:P0}) de {Placa.RamBytes:N0} de RAM"
           + $" — restam {Math.Max(0, Placa.RamBytes - Ram):N0}";

    /// <summary>O motivo de não caber, ou null.</summary>
    public string PorQueNaoCabe()
    {
        if (RamEstourou)
            return $"as variáveis do sketch ocupam {Ram:N0} bytes e a {Placa.Nome} "
                   + $"só tem {Placa.RamBytes:N0} de RAM.";

        if (FlashEstourou)
            return $"o programa ocupa {Flash:N0} bytes e só cabem {Placa.FlashBytes:N0} "
                   + $"na flash da {Placa.Nome}.";

        return null;
    }
}

public static class Memoria
{
    /// <summary>
    /// Quanto uma declaração ocupa nesta placa.
    ///
    /// O tamanho do "int" é o que separa uma placa da outra: 2 bytes no
    /// AVR, 4 nas de 32 bits. Num Uno é literalmente a diferença entre o
    /// vetor caber e não caber.
    /// </summary>
    public static int Tamanho(string tipo, PerfilPlaca placa)
    {
        tipo = (tipo ?? "").Replace("unsigned", "").Replace("const", "").Trim();

        // Ponteiro é o endereço, e não o que está do outro lado.
        if (tipo.EndsWith('*')) return placa.De8Bits ? 2 : 4;

        return tipo switch
        {
            "bool" or "boolean" or "char" or "byte" or "uint8_t" or "int8_t" => 1,
            "short" or "int16_t" or "uint16_t" or "word" => 2,
            "int" => placa.De8Bits ? 2 : 4,
            "long" or "int32_t" or "uint32_t" or "float" => 4,
            "double" => placa.De8Bits ? 4 : 8,
            "long long" or "int64_t" or "uint64_t" => 8,

            // String é um objeto com ponteiro, tamanho e capacidade, e o
            // texto vai para a heap. Este é o número que assusta no
            // ATtiny, e com razão.
            "String" => placa.De8Bits ? 6 : 16,

            // Tipo que não conhecemos -- uma classe de biblioteca, um
            // struct. Uma palavra é o chute honesto: menos seria fingir
            // que não ocupa nada.
            _ => placa.De8Bits ? 2 : 4,
        };
    }

    /// <summary>
    /// Quantos itens um vetor tem, ou 1 quando não dá para saber.
    ///
    /// Só conta quando o tamanho é um número escrito no código ou a
    /// lista de valores está ali. Tamanho vindo de uma variável fica em
    /// 1: errar para menos deixa passar um sketch grande, e errar para
    /// mais recusaria um sketch que funciona -- que é bem pior.
    /// </summary>
    static int Quantos(CmdDecl decl)
    {
        if (!decl.EhArray) return 1;

        int linhas = decl.Tamanho is Literal { EhTexto: false } n && n.Valor > 0
            ? (int)n.Valor
            : decl.Itens is { Count: > 0 } ? decl.Itens.Count : 1;

        int colunas = decl.Colunas is Literal { EhTexto: false } c && c.Valor > 0
            ? (int)c.Valor : 1;

        return linhas * colunas;
    }

    /// <summary>O que este sketch ocupa nesta placa.</summary>
    public static Orcamento Estimar(Programa programa, PerfilPlaca placa)
    {
        if (placa is null) return default;

        // O que o core gasta antes de o sketch existir: o número da
        // própria IDE compilando um sketch vazio (9 bytes no Uno, 22.100
        // na ESP32) e os objetos do core que o sketch usa -- no AVR o
        // Serial só entra no programa se for chamado.
        int ram = (int)MemoriaDaPlaca.De(placa).EstaticaVazia + ObjetosDoCore(programa, placa);
        int flash = placa.BaseDaFlash;

        foreach (var cmd in programa?.Globais ?? [])
        {
            switch (cmd)
            {
                case CmdDecl decl:
                    ram += Tamanho(decl.Tipo, placa) * Quantos(decl);

                    // Texto literal de um "char msg[] = "..."" mora na
                    // RAM também: é o F() que o tira de lá, e quem não
                    // usa o F() paga.
                    if (decl.Inicial is Literal { EhTexto: true } t)
                        ram += (t.Texto?.Length ?? 0) + 1;
                    break;

                // "Servo meu;", "LiquidCrystal lcd(...)": o objeto da
                // biblioteca ocupa, e não pouco.
                case CmdDeclObj:
                    ram += placa.De8Bits ? 12 : 32;
                    break;

                case CmdVarios varios:
                    foreach (var dentro in varios.Corpo)
                        if (dentro is CmdDecl d) ram += Tamanho(d.Tipo, placa) * Quantos(d);
                    break;
            }
        }

        // O programa em si: cada comando vira algumas instruções. O
        // fator sai de comparar o tamanho compilado dos sketches de
        // exemplo com o número de comandos deles.
        flash += Comandos(programa) * (placa.De8Bits ? 26 : 34);

        return new Orcamento(placa, ram, flash);
    }

    /// <summary>
    /// O que os objetos do core que o sketch usa ocupam de RAM. No AVR, medido
    /// na IDE (core 1.8): o Serial, 179 bytes -- os dois buffers de 64 e os
    /// campos --; o Wire, 216 (os buffers do TWI). Nas de 32 bits o core liga
    /// tudo sempre, e isso ja esta no sketch vazio.
    /// </summary>
    public static int ObjetosDoCore(Programa programa, PerfilPlaca placa)
    {
        if (placa is null || !placa.De8Bits || programa?.Fonte is not { } texto) return 0;
        int total = 0;
        if (texto.Contains("Serial.", StringComparison.Ordinal)) total += 179;
        if (texto.Contains("Wire.", StringComparison.Ordinal)) total += 216;
        return total;
    }

    /// <summary>Quantos comandos o programa tem, contando os de dentro.</summary>
    static int Comandos(Programa programa)
    {
        int total = programa?.Globais?.Count ?? 0;

        foreach (var funcao in programa?.Funcoes ?? [])
            total += Contar(funcao.Corpo);

        return total;
    }

    static int Contar(Cmd cmd) => cmd switch
    {
        null => 0,
        CmdBloco b => 1 + b.Corpo.Sum(Contar),
        CmdVarios v => 1 + v.Corpo.Sum(Contar),
        CmdSe se => 1 + Contar(se.Entao) + Contar(se.Senao),
        CmdEnquanto e => 1 + Contar(e.Corpo),
        CmdPara p => 1 + Contar(p.Ini) + Contar(p.Corpo),
        CmdFacaEnquanto f => 1 + Contar(f.Corpo),
        CmdParaCada pc => 1 + Contar(pc.Corpo),
        CmdEscolha es => 1 + es.Casos.Sum(c => c.Corpo.Sum(Contar)),
        _ => 1,
    };
}
