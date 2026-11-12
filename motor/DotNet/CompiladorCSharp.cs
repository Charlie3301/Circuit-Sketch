using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace Simulador;

/// <summary>O programa em C# compilado pelo Roslyn: a API das placas e o programa, em bytes.</summary>
public sealed record ProgramaDotNet(byte[] Api, byte[] Programa, string Placa) : ProgramaCompilado;

/// <summary>
/// O C# de verdade: o compilador do proprio .NET (Roslyn), com a linguagem
/// inteira (propriedade, evento, genericos, LINQ, async, record...).
///
/// O programa compila contra a API das placas (DotNet/Api: o nanoFramework,
/// o .NET IoT e a do Arduino), que tambem e fonte compilada aqui, uma vez por
/// execucao do simulador. Antes de gerar o codigo, o compilador poe as
/// guardas que a placa precisa e o .NET nao tem: um ponto em cada laco (o
/// Parar tira a placa ate de um "while (true) {}", e as interrupcoes chegam),
/// a conferencia da pilha em cada metodo (a recursao sem fim vira erro, em vez
/// de derrubar o simulador), o catch que nao engole a parada, e o
/// Thread.Sleep, o Console e as threads que passam pela placa.
/// </summary>
public static class CompiladorCSharp
{
    static readonly CSharpParseOptions Analise = new(LanguageVersion.Latest);

    /// <summary>
    /// As bibliotecas do .NET para compilar contra: as de referencia do .NET
    /// 10, que vem dentro do motor (no executavel unico as do runtime nem
    /// ficam no disco).
    /// </summary>
    /// O System.Net.Http fica de fora: o HttpClient do programa e o da API (DotNet/Api/Rede.cs), que
    /// passa pelo WiFi da placa, e nao o do computador, que sairia direto para a internet.
    static readonly Lazy<List<MetadataReference>> doNet = new(() =>
        [.. Basic.Reference.Assemblies.Net100.References.All.Where(r => !(r.Display ?? "").Contains("System.Net.Http", StringComparison.OrdinalIgnoreCase))]);

    /// <summary>As mesmas referencias do .NET, para quem mais compila C# no programa (os plugins).</summary>
    internal static IReadOnlyList<MetadataReference> ReferenciasDoNet => doNet.Value;

    /// <summary>A API das placas (DotNet/Api/*.cs, dentro do motor), compilada uma vez.</summary>
    static readonly Lazy<(byte[] Imagem, MetadataReference Referencia)> api = new(() =>
    {
        var motor = typeof(CompiladorCSharp).Assembly;
        var arvores = new List<SyntaxTree>();
        foreach (string recurso in motor.GetManifestResourceNames().Where(n => n.StartsWith("api/", StringComparison.Ordinal)).Order())
        {
            using var s = motor.GetManifestResourceStream(recurso)!;
            using var r = new StreamReader(s);
            arvores.Add(CSharpSyntaxTree.ParseText(r.ReadToEnd(), Analise, recurso));
        }
        var comp = CSharpCompilation.Create("CircuitSketch.Api", arvores, doNet.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, allowUnsafe: true,
                                         nullableContextOptions: NullableContextOptions.Disable));
        using var ms = new MemoryStream();
        var r2 = comp.Emit(ms);
        if (!r2.Success)
            throw new ErroDeExecucao(Idioma.T("a API das placas para o C# nao compilou: {0}", string.Join(" | ", r2.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(5))), 0);
        byte[] imagem = ms.ToArray();
        return (imagem, MetadataReference.CreateFromImage(imagem));
    });

    /// <summary>Compila em segundo plano o que leva tempo na primeira vez (a API e o proprio Roslyn).</summary>
    public static void AquecerEmSegundoPlano()
        => Task.Run(() =>
        {
            Thread.CurrentThread.Priority = ThreadPriority.Lowest;
            try { Compilar(new Projeto { Sketch = "System.Console.WriteLine();", Placa = "esp32-devkit-v1" }); }
            catch { }
            Thread.CurrentThread.Priority = ThreadPriority.Normal;
        });

    /// <summary>
    /// Compila o projeto (o programa e as abas .cs). O erro vem na linha do
    /// editor, com o codigo do compilador ("CS1002: ; expected").
    /// </summary>
    public static ProgramaDotNet Compilar(Projeto projeto)
    {
        string placa = projeto.Placa ?? "esp32-devkit-v1";
        var arvores = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(projeto.Sketch ?? "", Analise, "Program.cs", Encoding.UTF8) };
        foreach (var (nome, texto) in (projeto.Arquivos ?? new Dictionary<string, string>())
                     .Where(a => a.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).OrderBy(a => a.Key, StringComparer.Ordinal))
            arvores.Add(CSharpSyntaxTree.ParseText(texto ?? "", Analise, nome, Encoding.UTF8));
        int doUsuario = arvores.Count;
        // A API do Arduino sem "using" so no programa que nao usa a do .NET IoT:
        // o PinMode(pino, modo) dela e o enum PinMode de la nao convivem.
        // So o codigo conta (o comentario que cita o nanoFramework, nao).
        bool comArduino = !arvores.Take(doUsuario).Any(a => a.GetRoot().DescendantTokens()
            .Any(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText is "nanoFramework" or "Iot"
                      || t.ValueText == "Device" && t.GetPreviousToken().GetPreviousToken().ValueText == "System"));
        arvores.Add(CSharpSyntaxTree.ParseText(GlobaisDaPlaca(placa, comArduino), Analise, "cs_placa.cs", Encoding.UTF8));
        // O Buffer do Bluetooth do nanoFramework: la nao existe o System.Buffer, aqui os dois
        // nomes brigariam no "using System; using nanoFramework.Device.Bluetooth;" dos exemplos.
        if (arvores.Take(doUsuario).Any(a => a.GetRoot().DescendantTokens().Any(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == "Bluetooth")))
            arvores.Add(CSharpSyntaxTree.ParseText("global using Buffer = global::nanoFramework.Device.Bluetooth.Buffer;\n", Analise, "cs_bluetooth.cs", Encoding.UTF8));

        var opcoes = new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Debug,
                                                  allowUnsafe: true, nullableContextOptions: NullableContextOptions.Disable,
                                                  concurrentBuild: true);
        var (imagemDaApi, referenciaDaApi) = api.Value;
        var comp = CSharpCompilation.Create("programa", arvores, [.. doNet.Value, referenciaDaApi], opcoes);

        var erros = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (erros.Count > 0) throw Erro(erros);

        // As guardas da placa; se algo der errado nelas, vale o programa como veio.
        var guardado = comp;
        try
        {
            foreach (var arvore in arvores.Take(doUsuario))
            {
                var nova = new Guardas(comp.GetSemanticModel(arvore)).Visit(arvore.GetRoot());
                guardado = guardado.ReplaceSyntaxTree(arvore, arvore.WithRootAndOptions(nova, arvore.Options));
            }
        }
        catch (Exception) { guardado = comp; }

        byte[] programa = Emitir(guardado) ?? Emitir(comp) ?? throw Erro(comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList());
        return new ProgramaDotNet(imagemDaApi, programa, placa);
    }

    static byte[] Emitir(CSharpCompilation comp)
    {
        using var ms = new MemoryStream();
        var r = comp.Emit(ms, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded));
        return r.Success ? ms.ToArray() : null;
    }

    /// <summary>O primeiro erro, na linha do editor (a aba, pelo nome).</summary>
    static ErroDeSintaxe Erro(List<Diagnostic> erros)
    {
        var d = erros.OrderBy(e => e.Location.SourceTree?.FilePath == "Program.cs" ? 0 : 1)
                     .ThenBy(e => e.Location.GetLineSpan().StartLinePosition.Line).First();
        var span = d.Location.GetLineSpan();
        int linha = span.StartLinePosition.Line + 1;
        string mais = erros.Count > 1 ? Idioma.T(" (e mais {0} erro{1})", erros.Count - 1, (erros.Count > 2 ? "s" : "")) : "";
        string texto = $"{d.Id}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}{mais}";
        string arquivo = span.Path ?? "Program.cs";
        if (arquivo == "Program.cs" || !d.Location.IsInSource) return new ErroDeSintaxe(texto, d.Location.IsInSource ? linha : 0);
        int codigo = Origens.Codificar(Origens.Registrar("dotnet:" + arquivo, arquivo), linha);
        return new ErroDeSintaxe(texto, codigo);
    }

    static readonly Regex Definicao = new(@"^#define\s+([A-Za-z_]\w*)\s+(-?(?:0x[0-9A-Fa-f]+|\d+))[uUlL]*\s*$", RegexOptions.Multiline);
    static readonly Regex Constante = new(@"static const \w+ ([A-Za-z_]\w*) = (\d+);");

    /// <summary>
    /// O que o programa ve sem "using": os namespaces de sempre do .NET, a API
    /// do Arduino, e as constantes da placa (HIGH, OUTPUT, LED_BUILTIN, A0...),
    /// as mesmas do cs_placa.h do C++.
    /// </summary>

    static string GlobaisDaPlaca(string placa, bool comArduino)
    {
        var sb = new StringBuilder();
        sb.Append("global using System;\nglobal using System.Collections.Generic;\nglobal using System.Linq;\n");
        sb.Append("global using System.Threading;\nglobal using System.Threading.Tasks;\n");
        if (comArduino) sb.Append("global using static CircuitSketch.Arduino;\n");
        sb.Append("global using static CircuitSketch.Pinos;\n");
        sb.Append("namespace CircuitSketch {\n/// <summary>As constantes da placa ").Append(placa).Append(".</summary>\npublic static class Pinos {\n");
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        void Por(string nome, string valor)
        {
            if (!vistos.Add(nome) || SyntaxFacts.GetKeywordKind(nome) != SyntaxKind.None) return;
            long v = valor.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || valor.StartsWith("-0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt64(valor.Replace("0x", "").Replace("0X", ""), 16) : long.Parse(valor);
            sb.Append(v is >= int.MinValue and <= int.MaxValue ? $"public const int {nome} = {v};\n" : $"public const long {nome} = {v}L;\n");
        }
        Por("HIGH", "1");
        Por("LOW", "0");
        Por("LSBFIRST", "0");
        Por("MSBFIRST", "1");
        string cabecalho = CompiladorNativo.CabecalhoDaPlaca(placa);
        foreach (Match m in Definicao.Matches(cabecalho)) Por(m.Groups[1].Value, m.Groups[2].Value);
        foreach (Match m in Constante.Matches(cabecalho)) Por(m.Groups[1].Value, m.Groups[2].Value);
        foreach (string nome in new[] { "SERIAL_8N1", "SERIAL_8E1", "SERIAL_8O1", "SERIAL_7E1" })
            if (Interpretador.ConstanteDoCore(nome) is { } v) Por(nome, v);
        sb.Append("}\n}\n");
        return sb.ToString();
    }

    /// <summary>As guardas da placa, postas no programa antes de gerar o codigo (ver o resumo da classe).</summary>
    sealed class Guardas(SemanticModel modelo) : CSharpSyntaxRewriter
    {
        static StatementSyntax Volta() => SyntaxFactory.ParseStatement("global::CircuitSketch.Ponte.Volta();");
        static StatementSyntax Pilha() => SyntaxFactory.ParseStatement("global::CircuitSketch.Ponte.Pilha();");

        static StatementSyntax ComVolta(StatementSyntax corpo)
            => corpo is BlockSyntax b ? b.WithStatements(b.Statements.Insert(0, Volta()))
                                      : SyntaxFactory.Block(Volta(), corpo);

        public override SyntaxNode VisitWhileStatement(WhileStatementSyntax node)
        {
            var v = (WhileStatementSyntax)base.VisitWhileStatement(node);
            return v.WithStatement(ComVolta(v.Statement));
        }

        public override SyntaxNode VisitDoStatement(DoStatementSyntax node)
        {
            var v = (DoStatementSyntax)base.VisitDoStatement(node);
            return v.WithStatement(ComVolta(v.Statement));
        }

        public override SyntaxNode VisitForStatement(ForStatementSyntax node)
        {
            var v = (ForStatementSyntax)base.VisitForStatement(node);
            return v.WithStatement(ComVolta(v.Statement));
        }

        public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
        {
            var v = (ForEachStatementSyntax)base.VisitForEachStatement(node);
            return v.WithStatement(ComVolta(v.Statement));
        }

        public override SyntaxNode VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            var v = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node);
            return v.WithStatement(ComVolta(v.Statement));
        }

        // ---- a pilha em cada metodo (a recursao sem fim vira erro) ----
        BlockSyntax ComPilha(BlockSyntax corpo) => corpo?.WithStatements(corpo.Statements.Insert(0, Pilha()));

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var v = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
            if (v.Body is not null) return v.WithBody(ComPilha(v.Body));
            var corpo = CorpoDaExpressao(v.ExpressionBody, modelo.GetDeclaredSymbol(node) as IMethodSymbol);
            return corpo is null ? v : v.WithExpressionBody(null).WithSemicolonToken(default)
                                        .WithBody(corpo.WithTrailingTrivia(v.SemicolonToken.TrailingTrivia));
        }

        public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            var v = (LocalFunctionStatementSyntax)base.VisitLocalFunctionStatement(node);
            if (v.Body is not null) return v.WithBody(ComPilha(v.Body));
            var corpo = CorpoDaExpressao(v.ExpressionBody, modelo.GetDeclaredSymbol(node) as IMethodSymbol);
            return corpo is null ? v : v.WithExpressionBody(null).WithSemicolonToken(default)
                                        .WithBody(corpo.WithTrailingTrivia(v.SemicolonToken.TrailingTrivia));
        }

        /// <summary>"int F(int n) => ...": vira bloco, para caber a conferencia da pilha.</summary>
        BlockSyntax CorpoDaExpressao(ArrowExpressionClauseSyntax seta, IMethodSymbol m)
        {
            if (seta is null || m is null || m.IsAsync || m.ReturnsByRef || m.ReturnsByRefReadonly) return null;
            StatementSyntax corpo = m.ReturnsVoid ? SyntaxFactory.ExpressionStatement(seta.Expression)
                                                  : SyntaxFactory.ReturnStatement(seta.Expression);
            return SyntaxFactory.Block(Pilha(), corpo);
        }

        public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            var v = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node);
            return v.Body is not null ? v.WithBody(ComPilha(v.Body)) : v;
        }

        // ---- o catch que nao engole a parada da placa ----
        public override SyntaxNode VisitCatchClause(CatchClauseSyntax node)
        {
            var v = (CatchClauseSyntax)base.VisitCatchClause(node);
            ExpressionSyntax livre = SyntaxFactory.ParseExpression("!global::CircuitSketch.Ponte.Saindo");
            ExpressionSyntax filtro = v.Filter is null ? livre
                : SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression, livre,
                                                 SyntaxFactory.ParenthesizedExpression(v.Filter.FilterExpression));
            return v.WithFilter(SyntaxFactory.CatchFilterClause(filtro));
        }

        // ---- o Thread.Sleep, o Task.Delay, o Console e o Debug pela placa ----
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var simbolo = modelo.GetSymbolInfo(node).Symbol as IMethodSymbol;
            var v = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);
            if (simbolo is null || !simbolo.IsStatic) return v;
            string tipo = simbolo.ContainingType?.ToDisplayString() ?? "";
            string novo = (tipo, simbolo.Name) switch
            {
                ("System.Threading.Thread", "Sleep") => "global::CircuitSketch.Ponte.Dormir",
                ("System.Threading.Tasks.Task", "Delay") => "global::CircuitSketch.Ponte.Esperar",
                ("System.Console", "Write" or "WriteLine" or "Read" or "ReadLine" or "Clear") => "global::CircuitSketch.ConsoleDaPlaca." + simbolo.Name,
                ("System.Diagnostics.Debug" or "System.Diagnostics.Trace", "Write" or "WriteLine" or "Print" or "WriteIf" or "WriteLineIf" or "Assert" or "Flush")
                    => "global::CircuitSketch.DebugDaPlaca." + simbolo.Name,
                _ => null,
            };
            if (novo is null) return v;
            return v.WithExpression(SyntaxFactory.ParseExpression(novo).WithTriviaFrom(v.Expression));
        }

        // ---- as threads e os temporizadores do programa, protegidos ----
        public override SyntaxNode VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var tipo = modelo.GetTypeInfo(node).Type?.ToDisplayString();
            var v = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node);
            return Trocar(tipo, v, v.ArgumentList, v.Initializer);
        }

        public override SyntaxNode VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            var tipo = modelo.GetTypeInfo(node).Type?.ToDisplayString();
            var v = (ImplicitObjectCreationExpressionSyntax)base.VisitImplicitObjectCreationExpression(node);
            return Trocar(tipo, v, v.ArgumentList, v.Initializer);
        }

        static SyntaxNode Trocar(string tipo, ExpressionSyntax v, ArgumentListSyntax args, InitializerExpressionSyntax inicial)
        {
            string fabrica = tipo switch
            {
                "System.Threading.Thread" => "global::CircuitSketch.Ponte.NovaThread",
                "System.Threading.Timer" => "global::CircuitSketch.Ponte.NovoTimer",
                _ => null,
            };
            if (fabrica is null || inicial is not null || args is null) return v;
            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression(fabrica), args).WithTriviaFrom(v);
        }
    }
}

