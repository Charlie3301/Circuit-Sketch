namespace Simulador;

/// <summary>
/// O mesmo verbo, escrito do jeito de cada linguagem.
///
/// "Acender o pino 13" se escreve <c>digitalWrite</c> em C++,
/// <c>Digital_Write</c> em Ada, <c>digital_write</c> em Rust e
/// <c>DigitalWrite</c> em C#. É a MESMA operação, e o simulador não
/// deveria ter nove tabelas de funções dizendo isso nove vezes.
///
/// Então a tabela é esta, e ela traduz pela FORMA do nome: tira os
/// sublinhados, ignora a caixa, e procura. Nove maneiras de escrever
/// caem na única implementação que existe, que é a do Arduino.
///
/// O que esta tradução NÃO faz é inventar função: um nome que não está
/// aqui e não é do Arduino continua dando o mesmo erro de antes. A
/// tradução resolve grafia, e não ausência.
/// </summary>
public static class Vocabulario
{
    /// <summary>
    /// A forma canônica de um nome: sem sublinhado, tudo minúsculo.
    ///
    /// É o que faz <c>Digital_Write</c>, <c>digital_write</c> e
    /// <c>DIGITALWRITE</c> serem a mesma chave. Ada não tem caixa;
    /// Rust escreve com sublinhado; C# escreve em maiúscula inicial.
    /// </summary>
    public static string Achatar(string nome)
        => (nome ?? "")
            .Replace("_", "")

            // A exclamacao das macros do Rust -- println!, write! --
            // faz parte do nome, e nao do verbo. Sem tirar, "println!"
            // nao acha "println" e o programa para numa linha certa.
            .Replace("!", "")
            .ToLowerInvariant();

    static readonly Dictionary<string, string> Tabela = new()
    {
        // ---- pinos ----
        ["pinmode"] = "pinMode",
        ["setpinmode"] = "pinMode",
        ["setdirection"] = "pinMode",
        ["setasoutput"] = "pinMode",

        ["digitalwrite"] = "digitalWrite",
        ["writepin"] = "digitalWrite",
        ["setpin"] = "digitalWrite",
        ["write"] = "digitalWrite",

        ["digitalread"] = "digitalRead",
        ["readpin"] = "digitalRead",
        ["isheld"] = "digitalRead",

        ["analogread"] = "analogRead",
        ["readanalog"] = "analogRead",
        ["adcread"] = "analogRead",

        ["analogwrite"] = "analogWrite",
        ["writeanalog"] = "analogWrite",
        ["setduty"] = "analogWrite",

        // ---- tempo ----
        //
        // Cada linguagem tem o seu: Ada escreve "delay", Rust
        // "delay_ms", o SDK da Pico "sleep_ms", C# "Thread.Sleep".
        ["delay"] = "delay",
        ["delayms"] = "delay",
        ["sleepms"] = "delay",
        ["sleep"] = "delay",
        ["wait"] = "delay",
        ["waitms"] = "delay",

        ["delayus"] = "delayMicroseconds",
        ["delaymicroseconds"] = "delayMicroseconds",
        ["sleepus"] = "delayMicroseconds",
        ["waitus"] = "delayMicroseconds",

        ["millis"] = "millis",
        ["micros"] = "micros",
        ["ticksms"] = "millis",

        // ---- escrever no monitor ----
        //
        // "Put_Line" é de Ada, "println!" é de Rust, "WriteLine" é de
        // C#, "print" é de quase todo mundo.
        ["putline"] = "__println",
        ["println"] = "__println",
        ["printline"] = "__println",
        ["writeline"] = "__println",
        ["puts"] = "__println",
        ["newline"] = "__println",

        // As outras macros de escrita do Rust caem no mesmo lugar.
        ["eprintln"] = "__println",
        ["panic"] = "__println",

        ["put"] = "__print",
        ["eprint"] = "__print",
        ["format"] = "__print",
        ["print"] = "__print",
        ["printf"] = "__print",
        ["write"] = "__print",

        // ---- som ----
        ["tone"] = "tone",
        ["notone"] = "noTone",
        ["playtone"] = "tone",

        // ---- contas ----
        ["map"] = "map",
        ["constrain"] = "constrain",
        ["random"] = "random",
        ["abs"] = "abs",
        ["min"] = "min",
        ["max"] = "max",
        ["sqrt"] = "sqrt",
        ["pow"] = "pow",
    };

    /// <summary>
    /// O nome do Arduino para este nome, ou null.
    ///
    /// "write" e "print" aparecem duas vezes na tabela acima porque a
    /// mesma palavra significa coisas diferentes em linguagens
    /// diferentes. O dicionário fica com a última, e é por isso que os
    /// casos ambíguos são resolvidos antes, pelo número de argumentos.
    /// </summary>
    public static string Traduzir(string nome, int quantosArgumentos)
    {
        string chave = Achatar(nome);

        // "write(pino, valor)" é escrever num pino; "write(texto)" é
        // escrever no monitor. Só o número de argumentos separa os dois.
        if (chave == "write") return quantosArgumentos >= 2 ? "digitalWrite" : "__print";

        return Tabela.GetValueOrDefault(chave);
    }

    // =================================================================
    //  constantes
    // =================================================================
    static readonly Dictionary<string, string> Valores = new()
    {
        ["high"] = "HIGH",
        ["low"] = "LOW",
        ["on"] = "HIGH",
        ["off"] = "LOW",
        ["true"] = "HIGH",
        ["false"] = "LOW",
        ["set"] = "HIGH",
        ["clear"] = "LOW",

        ["output"] = "OUTPUT",
        ["input"] = "INPUT",
        ["inputpullup"] = "INPUT_PULLUP",
        ["pullup"] = "INPUT_PULLUP",
        ["pulldown"] = "INPUT_PULLDOWN",

        ["ledbuiltin"] = "LED_BUILTIN",
        ["ledpin"] = "LED_BUILTIN",
    };

    /// <summary>A constante do Arduino para este nome, ou null.</summary>
    public static string Constante(string nome) => Valores.GetValueOrDefault(Achatar(nome));

    /// <summary>
    /// As linguagens em que a tradução vale.
    ///
    /// Em C++ e em C ela NÃO vale, e de propósito: naquelas duas o
    /// nome é o que está escrito, e traduzir "write" para
    /// "digitalWrite" num sketch que tem o próprio "write" trocaria a
    /// função da pessoa pela do simulador sem avisar.
    /// </summary>
    public static bool Vale(Linguagem l)
        => l is Linguagem.Rust or Linguagem.Ada or Linguagem.Spark
             or Linguagem.Cowgol or Linguagem.Millipascal or Linguagem.CSharp;
}
