namespace Simulador;

/// <summary>
/// Os sistemas de arquivos da flash da ESP32: o LittleFS, o SPIFFS e o FFat.
///
/// Os arquivos moram na flash da placa (Maquina.SistemaDaFlash): ficam de
/// uma rodada para outra e sobrevivem ao reinicio, como na placa. Como no
/// core, a particao que nunca foi formatada nao monta -- o begin(false) da
/// placa nova devolve false, e o begin(true) formata. Os arquivos que vieram
/// do computador (a pasta "data" enviada para a flash) contam como
/// formatada.
/// </summary>
public partial class Interpretador
{
    /// <summary>O tamanho da particao "spiffs" do esquema padrao (4 MB com spiffs): 0x160000.</summary>
    const long TamanhoDaParticao = 0x160000;

    Valor ChamarFlash(string nome, string metodo, Chamada c, Ambiente amb)
    {
        var flash = maq.SistemaDaFlash;
        var args = c.Args.Select(a => Avaliar(a, amb)).ToList();
        string Texto(int i) => i < args.Count ? args[i].Mostrar() : "";
        bool Montado() => flash.Montado;

        bool CaminhoRecusado(string caminho)
        {
            if (caminho.StartsWith('/')) return false;
            maq.Avisar(Idioma.T("{0}.{1}(\"{2}\"): o caminho na flash comeca com \"/\" -- escreva \"/{3}\".", nome, metodo, caminho, caminho));
            return true;
        }

        switch (metodo)
        {
            case "begin":
            {
                bool formatarSeFalhar = args.Count > 0 && args[0].Num != 0;
                if (!flash.Formatado && flash.Nomes.Count == 0)
                {
                    if (!formatarSeFalhar)
                    {
                        maq.Avisar(Idioma.T("{0}.begin() devolveu false: a particao da flash nunca foi formatada (a placa nova). Use {1}.begin(true), que formata, ou mande os arquivos da pasta data para a flash.", nome, nome));
                        return Valor.DeBool(false);
                    }
                    flash.Limpar();
                }
                flash.Formatado = true;
                return Valor.DeBool(flash.Montar());
            }
            case "format":
                flash.Limpar();
                flash.Formatado = true;
                return Valor.DeBool(true);
            case "end": flash.Desmontar(); return Valor.Vazio;
            case "totalBytes": return Valor.DeC(Montado() ? TamanhoDaParticao : 0, TipoC.ULongLong);
            case "usedBytes":
            {
                if (!Montado()) return Valor.DeC(0, TipoC.ULongLong);
                long usado = 0;
                foreach (string arquivo in flash.Nomes)
                {
                    flash.Tipo(arquivo, out long tamanho);
                    usado += (tamanho + 4095) / 4096 * 4096;
                }
                return Valor.DeC(usado, TipoC.ULongLong);
            }
            case "freeBytes":
                return Valor.DeC(Montado() ? TamanhoDaParticao - (long)ChamarFlash(nome, "usedBytes", c, amb).Num : 0, TipoC.ULongLong);
            case "exists": return Valor.DeBool(Montado() && !CaminhoRecusado(Texto(0)) && flash.Existe(Texto(0)));
            case "remove": return Valor.DeBool(Montado() && !CaminhoRecusado(Texto(0)) && flash.Remover(Texto(0)));
            case "rename": return Valor.DeBool(Montado() && !CaminhoRecusado(Texto(0)) && flash.Renomear(Texto(0), Texto(1)));
            case "mkdir": return Valor.DeBool(Montado() && !CaminhoRecusado(Texto(0)) && flash.CriarPasta(Texto(0)));
            case "rmdir": return Valor.DeBool(Montado() && !CaminhoRecusado(Texto(0)) && flash.RemoverPasta(Texto(0)));
            case "open":
            {
                if (!Montado() || CaminhoRecusado(Texto(0)))
                {
                    arquivoRecemAberto = null;
                    return Valor.DeBool(false);
                }
                string modo = ModoDoOpen(c, args);
                var arquivo = flash.Abrir(Texto(0), modo != "r", truncar: modo == "w");
                arquivoRecemAberto = arquivo;
                return Valor.DeBool(arquivo is not null);
            }
        }
        throw new ErroDeExecucao(Idioma.T("{0}.{1}() ainda nao e suportado pelo simulador.", nome, metodo), c.Linha);
    }

    /// <summary>O modo do open(): "r", "w" (do zero) ou "a" (no fim), como a ESP32 entende.</summary>
    string ModoDoOpen(Chamada c, List<Valor> args)
    {
        if (c.Args.Count < 2) return "r";
        string escrito = c.Args[1] is Variavel constante ? constante.Nome : args[1].Mostrar();
        return escrito switch
        {
            "FILE_READ" => "r",
            "FILE_APPEND" => "a",
            "FILE_WRITE" => EhDaFamiliaEsp32 ? "w" : "a",
            _ when args[1].EhTexto => escrito.Contains('a') ? "a" : escrito.Contains('w') ? "w" : escrito.Contains('+') ? "a" : "r",
            _ => args[1].Num > 0 ? (EhDaFamiliaEsp32 ? "w" : "a") : "r",
        };
    }
}
