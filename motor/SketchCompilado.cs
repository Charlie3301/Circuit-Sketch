namespace Simulador;

/// <summary>
/// O programa compilado de verdade (o C/C++ pelo clang, o C# pelo Roslyn):
/// o que o Compilador entrega no Programa.Compilado.
/// </summary>
public abstract record ProgramaCompilado;

/// <summary>
/// O programa compilado rodando numa placa: o interpretador chama o setup()
/// e o loop() dele, e as rotinas que ele deu ao simulador ("__nativa_3": a
/// interrupcao, o callback do MQTT) pelo numero.
/// </summary>
public interface ISketchCompilado
{
    void Setup();
    void Loop();
    void Rotina(int indice);
    void Rotina(int indice, object[] argumentos);
    void Encerrar();
}
