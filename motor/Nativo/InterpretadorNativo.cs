using System.Runtime.InteropServices;

namespace Simulador;

/// <summary>
/// O sketch interpretado chamando o C nativo: "doom_iniciar();" no .ino
/// chama a funcao compilada pelo TinyCC, com os argumentos convertidos pelo
/// prototipo que o .h declarou (extern "C" void doom_iniciar(void);).
/// </summary>
public partial class Interpretador
{
    readonly Dictionary<string, IntPtr> funcoesNativas = [];

    /// <summary>Carrega o C do programa, se ele tem .c, antes das globais.</summary>
    void PrepararNativo()
    {
        if (prog.Nativos is not { Count: > 0 } || !prog.Nativos.Any(f => f.Nome.EndsWith(".c", StringComparison.OrdinalIgnoreCase)))
            return;
        maq.EncerrarNativo();
        maq.Nativo = ModuloNativo.Carregar(maq, prog, Origem?.Razao ?? 1);
    }

    /// <summary>A funcao existe no C desta placa?</summary>
    bool TemNativa(string nome, out IntPtr endereco)
    {
        endereco = IntPtr.Zero;
        var nativo = maq.Nativo;
        if (nativo is null) return false;
        if (!funcoesNativas.TryGetValue(nome, out endereco))
        {
            endereco = nativo.Endereco(nome);
            funcoesNativas[nome] = endereco;
        }
        return endereco != IntPtr.Zero;
    }

    static string TipoNativo(string tipo)
        => (tipo ?? "int").Replace("const ", "").Replace("volatile ", "").Replace("unsigned ", "u")
                          .Replace("signed ", "").Replace(" ", "");

    static bool EhReal(string t) => t is "float" or "double" or "longdouble";
    static bool EhPonteiro(string t) => t.EndsWith('*');

    Valor ChamarNativa(string nome, IntPtr endereco, List<Valor> args, int linha)
    {
        var nativo = maq.Nativo ?? throw maq.MotivoDaParada();

        Funcao prototipo = null;
        if (sobrecargas.TryGetValue(nome, out var lista))
            prototipo = lista.FirstOrDefault(f => f.Params.Count == args.Count) ?? lista.FirstOrDefault();

        var slots = new ulong[args.Count];
        var soltar = new List<IntPtr>();
        try
        {
            for (int i = 0; i < args.Count; i++)
            {
                var v = args[i];
                string tipo = TipoNativo(prototipo is not null && i < prototipo.Params.Count
                    ? prototipo.Params[i].Tipo
                    : v.EhTexto ? "char*" : v.Inteiro ? "int" : "double");

                if (tipo == "float") slots[i] = BitConverter.SingleToUInt32Bits((float)v.Num);
                else if (EhReal(tipo)) slots[i] = (ulong)BitConverter.DoubleToInt64Bits(v.Num);
                else if (EhPonteiro(tipo) && v.EhTexto)
                {
                    IntPtr p = nativo.TextoNoC(v.Texto);
                    soltar.Add(p);
                    slots[i] = (ulong)p;
                }
                else if (v.EhTexto)
                {
                    throw new ErroDeExecucao(Idioma.T("{0}(): o argumento {1} e um texto, mas o C espera {2}.", nome, i + 1, tipo), linha);
                }
                else slots[i] = unchecked((ulong)(long)v.Num);
            }

            var (status, inteiro, real) = nativo.Chamar(endereco, slots);
            if (status != 0) throw maq.MotivoDaParada();

            string retorno = TipoNativo(prototipo?.Retorno ?? "int");
            return retorno switch
            {
                "void" => Valor.Vazio,
                "float" => Valor.DeReal(BitConverter.Int32BitsToSingle((int)(BitConverter.DoubleToInt64Bits(real) & 0xFFFFFFFF))),
                "double" or "longdouble" => Valor.DeReal(real),
                "bool" or "_Bool" => Valor.DeBool((inteiro & 0xFF) != 0),
                "char*" when inteiro != 0 => Valor.DeTexto(Marshal.PtrToStringUTF8((IntPtr)inteiro) ?? ""),
                "char" or "int8_t" => Valor.DeInt((sbyte)inteiro),
                "uchar" or "uint8_t" or "byte" => Valor.DeInt((byte)inteiro),
                "short" or "int16_t" => Valor.DeInt((short)inteiro),
                "ushort" or "uint16_t" => Valor.DeInt((ushort)inteiro),
                "uint" or "uint32_t" or "ulong" or "uint32" or "esp_err_t_u" => Valor.DeInt((uint)inteiro),
                "longlong" or "int64_t" or "size_t" or "ssize_t" or "intptr_t" => Valor.DeInt((long)inteiro),
                "ulonglong" or "uint64_t" or "uintptr_t" => Valor.DeInt(inteiro),
                _ when EhPonteiro(retorno) => Valor.DeInt((long)inteiro),
                _ => Valor.DeInt((int)inteiro),
            };
        }
        finally
        {
            foreach (var p in soltar) nativo.Soltar(p);
        }
    }
}
