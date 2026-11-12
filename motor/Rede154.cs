namespace Simulador;

/// <summary>
/// O radio IEEE 802.15.4 da bancada (a C6 e a H2): os quadros que o OpenThread
/// de uma placa manda chegam nas outras placas com o radio ligado. O filtro de
/// canal, de PAN e de endereco e o ACK sao os do chip, e ficam na plataforma
/// nativa (nativo/openthread/src/cs_openthread.cpp).
/// </summary>
public sealed partial class Rede
{
    readonly Dictionary<Maquina, Queue<byte[]>> radios154 = [];

    public void Ligar154(Maquina placa, bool ligar)
    {
        lock (radios154)
        {
            if (ligar) radios154.TryAdd(placa, new Queue<byte[]>());
            else radios154.Remove(placa);
        }
        Anotar(placa, "802.15.4", ligar ? Idioma.T("radio ligado") : Idioma.T("radio desligado"));
    }

    /// <summary>O quadro vai ao ar: cada outra placa ligada recebe o canal (1 byte) e os bytes.</summary>
    public void Enviar154(Maquina placa, int canal, byte[] quadro)
    {
        var pacote = new byte[quadro.Length + 1];
        pacote[0] = (byte)canal;
        quadro.CopyTo(pacote, 1);
        lock (radios154)
        {
            foreach (var (outra, fila) in radios154)
            {
                if (outra == placa) continue;
                fila.Enqueue(pacote);
                while (fila.Count > 256) fila.Dequeue();
            }
        }
    }

    /// <summary>O proximo quadro que chegou nesta placa (o canal e os bytes), ou null.</summary>
    public byte[] Receber154(Maquina placa)
    {
        lock (radios154) return radios154.TryGetValue(placa, out var fila) && fila.Count > 0 ? fila.Dequeue() : null;
    }

    void Esquecer154(Maquina placa)
    {
        lock (radios154) radios154.Remove(placa);
    }
}
