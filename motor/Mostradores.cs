namespace Simulador;

/// <summary>
/// O TM1637 (o modulo de 4 digitos com os dois pontos do relogio): seis
/// posicoes de segmentos -- o modulo usa as quatro primeiras --, o brilho (0
/// a 7) e o liga. Cada byte e um digito: o bit 0 e o segmento A, ..., o bit 6
/// o G e o bit 7 o ponto (no digito 1, os dois pontos do meio).
/// </summary>
public sealed class Tm1637
{
    public byte[] Segmentos { get; } = new byte[6];
    public int Brilho { get; private set; } = 7;
    public bool Ligado { get; private set; } = true;
    public int Versao { get; private set; }

    public void Definir(int posicao, byte segmentos)
    {
        if (posicao is < 0 or > 5) return;
        Segmentos[posicao] = segmentos;
        Versao++;
    }

    public void Luz(int brilho, bool ligado)
    {
        Brilho = brilho & 7;
        Ligado = ligado;
        Versao++;
    }
}

/// <summary>
/// O MAX7219 da matriz 8 x 8 (a LedControl): ate oito em cadeia, cada um com
/// as oito linhas (o bit 7 e a coluna 0, a da esquerda), o brilho (0 a 15) e o
/// desligado -- que e como ele comeca: o construtor da LedControl poe todos
/// em shutdown, e e o lc.shutdown(0, false) do sketch que acende.
/// </summary>
public sealed class Max7219(int quantidade)
{
    public int Quantidade { get; } = Math.Clamp(quantidade, 1, 8);
    public byte[][] Linhas { get; } = [.. Enumerable.Range(0, 8).Select(_ => new byte[8])];
    public int[] Brilho { get; } = [.. Enumerable.Repeat(15, 8)];
    public bool[] Desligado { get; } = [.. Enumerable.Repeat(true, 8)];
    public int[] Varredura { get; } = [.. Enumerable.Repeat(7, 8)];
    public int Versao { get; private set; }

    public void Mudou() => Versao++;
}
