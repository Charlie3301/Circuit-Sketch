namespace Simulador;

/// <summary>
/// A rede do C++ compilado que o interpretador nao tem: o UDP (WiFiUDP,
/// NetworkUdp, AsyncUDP, o NTPClient por cima deles).
/// </summary>
public partial class Interpretador
{
    /// <summary>
    /// "__toque"(pino): quanto o dedo encosta no pino de toque (0 a 1, a
    /// leitura "pino:toque" da bancada); o HAL (cs_hal.cpp) faz dela o
    /// numero que o sensor do chip daria.
    /// "__fita"(pino, bytes): os LEDs enderecaveis que o RMT mandou (3 bytes
    /// por LED, na ordem G R B), como a fita do Adafruit_NeoPixel.
    /// "__usb"(tipo, protocolo, id, bytes em hexadecimal): o que o USB da
    /// placa (o TinyUSB do core) mandou ao computador -- "conectou" (com as
    /// interfaces no lugar dos bytes), "hid", "midi", "vendor" (motor/Usb.cs).
    /// </summary>
    Valor HalDoNativo(string nome, List<Valor> args)
    {
        switch (nome)
        {
            case "__toque":
                return Valor.DeReal(Math.Clamp(maq.Leitura($"{(int)args[0].Num}:toque"), 0, 1));
            case "__fita":
            {
                int pino = (int)args[0].Num;
                if (args.Count < 2 || args[1].Referencia is not Valor[] bytes || bytes.Length < 3) return Valor.Vazio;
                int leds = Math.Min(bytes.Length / 3, 256);
                if (!maq.Fitas.TryGetValue(pino, out var fita) || fita.Quantidade < leds) maq.Fitas[pino] = fita = new Fita(leds);
                int B(int i) => (int)((long)bytes[i].Num & 0xFF);
                for (int i = 0; i < leds; i++) fita.Definir(i, Fita.Montar(B(i * 3 + 1), B(i * 3), B(i * 3 + 2)));
                fita.Mostrar();
                return Valor.Vazio;
            }
            // A NVS da placa (src/cs_idf_nvs.cpp): o nvs.h do IDF e o Preferences do core.
            case "__nvs_gravar":
            {
                var quadro = args[3].Referencia as Valor[] ?? [];
                var dados = new byte[quadro.Length];
                for (int i = 0; i < dados.Length; i++) dados[i] = (byte)((long)quadro[i].Num & 0xFF);
                lock (maq.Nvs) maq.Nvs[args[0].Texto + "\0" + args[1].Texto] = ((int)args[2].Num, dados);
                return Valor.Vazio;
            }
            case "__nvs_tipo":
                lock (maq.Nvs) return Valor.DeInt(maq.Nvs.TryGetValue(args[0].Texto + "\0" + args[1].Texto, out var nvsTipo) ? nvsTipo.Tipo : -1);
            case "__nvs_tamanho":
                lock (maq.Nvs) return Valor.DeInt(maq.Nvs.TryGetValue(args[0].Texto + "\0" + args[1].Texto, out var nvsTam) ? nvsTam.Dados.Length : -1);
            case "__nvs_ler":
            {
                if (args[2].Referencia is not Valor[] destino) return Valor.DeInt(-1);
                lock (maq.Nvs)
                {
                    if (!maq.Nvs.TryGetValue(args[0].Texto + "\0" + args[1].Texto, out var nvs)) return Valor.DeInt(-1);
                    int n = Math.Min(nvs.Dados.Length, destino.Length);
                    for (int i = 0; i < n; i++) destino[i] = Valor.DeC(nvs.Dados[i], TipoC.UChar);
                    return Valor.DeInt(n);
                }
            }
            case "__nvs_apagar":
                lock (maq.Nvs) return Valor.DeInt(maq.Nvs.Remove(args[0].Texto + "\0" + args[1].Texto) ? 0 : -1);
            case "__nvs_limpar":
                lock (maq.Nvs) maq.Nvs.Clear();
                return Valor.Vazio;
            case "__nvs_chaves":
            {
                string prefixo = args[0].Texto + "\0";
                lock (maq.Nvs) return Valor.DeTexto(string.Join("\n", maq.Nvs.Keys.Where(k => k.StartsWith(prefixo, StringComparison.Ordinal)).Select(k => k[prefixo.Length..])));
            }
            case "__nvs_contar":
                lock (maq.Nvs) return Valor.DeInt(maq.Nvs.Keys.Count(k => !k.EndsWith("\0\u0001", StringComparison.Ordinal)));
            case "__nvs_espacos":
                lock (maq.Nvs) return Valor.DeInt(maq.Nvs.Keys.Select(k => k.Split('\0')[0]).Distinct().Count());
            // O radio 802.15.4 do OpenThread (motor/Rede154.cs).
            case "__154_ligar":
                maq.Rede?.Ligar154(maq, args[0].Num != 0);
                return Valor.Vazio;
            case "__154_enviar":
            {
                if (args.Count < 2 || args[1].Referencia is not Valor[] quadro) return Valor.Vazio;
                var dados = new byte[quadro.Length];
                for (int i = 0; i < dados.Length; i++) dados[i] = (byte)((long)quadro[i].Num & 0xFF);
                maq.Rede?.Enviar154(maq, (int)args[0].Num, dados);
                return Valor.Vazio;
            }
            case "__154_receber":
            {
                if (args.Count < 1 || args[0].Referencia is not Valor[] destino) return Valor.DeInt(-1);
                var pacote = maq.Rede?.Receber154(maq);
                if (pacote is null) return Valor.DeInt(-1);
                int n = Math.Min(pacote.Length, destino.Length);
                for (int i = 0; i < n; i++) destino[i] = Valor.DeC(pacote[i], TipoC.UChar);
                return Valor.DeInt(n);
            }
            // O RainMaker (src/cs_rainmaker.cpp): a nuvem e o aplicativo sao a bancada (motor/RainMaker.cs).
            // O erro que escapou do programa em Python: o traceback ja foi para o monitor.
            case "__erro_python":
                throw new ErroDeExecucao($"{args[0].Mostrar()}: {args[1].Mostrar()}", (int)args[3].Num);
            case "__abort":
                // Na ESP32 o abort() e um panico: a placa reinicia. Nas outras, ela para ali.
                if (EhDaFamiliaEsp32 || EhEsp8266) throw Reiniciar(ReinicioDaPlaca.PeloPanico, 0, 0);
                throw new ErroDeExecucao(Idioma.T("o programa chamou abort(): a placa parou aqui."), 0);
            case "__matter_comissionada": return Valor.DeBool(maq.Matter.Comissionada);
            case "__matter_proxima": return Valor.DeTexto(maq.Matter.ProximaEscrita());
            case "__matter_relatar":
                maq.Matter.Relatar((int)args[0].Num, (long)args[1].Num, (long)args[2].Num, args[3].Texto ?? "");
                return Valor.Vazio;
            case "__matter_descomissionar": maq.Matter.Descomissionar(); return Valor.Vazio;
            case "__matter_evento":
                maq.Matter.Evento((int)args[0].Num, args[1].Texto ?? "", (int)args[2].Num, (int)args[3].Num);
                return Valor.Vazio;
            case "__rmaker_rede": return Valor.DeBool(maq.Rede?.NaRede(maq) ?? false);
            case "__rmaker_conectou": maq.RainMaker.Conectou(); return Valor.Vazio;
            case "__rmaker_relatar":
                maq.RainMaker.Relatar(args[0].Texto ?? "", args[1].Texto ?? "", args[2].Texto ?? "");
                return Valor.Vazio;
            case "__rmaker_proxima": return Valor.DeTexto(maq.RainMaker.ProximaEscrita());
            case "__rmaker_aviso": maq.RainMaker.Avisar(args[0].Texto ?? ""); return Valor.Vazio;
            case "__usb":
            {
                string tipo = args[0].Texto, dados = args.Count > 3 ? args[3].Texto ?? "" : "";
                byte[] Bytes() => Convert.FromHexString(dados);
                switch (tipo)
                {
                    case "conectou": maq.Usb.Conectar(dados); break;
                    case "hid": maq.Usb.RelatorioHid((int)args[1].Num, (int)args[2].Num, Bytes()); break;
                    case "midi": maq.Usb.Midi(Bytes()); break;
                    case "vendor": maq.Usb.Vendor(Bytes()); break;
                }
                return Valor.Vazio;
            }
        }
        return Valor.Vazio;
    }

    /// <summary>
    /// "__dns"(nome) -> o IP em texto (vazio: nao achou); "__wifi_estacoes"()
    /// -> quantas placas estao no AP desta; "__mdns"(nome) -> anuncia
    /// "nome.local" (vazio: para); "__mdns_servico"(servico, protocolo,
    /// porta); "__mdns_buscar"(servico, protocolo) -> "nome|ip|porta" por linha.
    /// "__eth"(1 ou 0): liga ou desliga o cabo de rede (o ETH.begin/end);
    /// "__eth_ip", "__eth_roteador", "__eth_mac" (o MAC da placa + 3, como no
    /// esp_read_mac do ETH) e "__eth_config"(ip): o IP fixo do ETH.config().
    /// </summary>
    Valor NomesDoNativo(string nome, List<Valor> args)
    {
        var rede = maq.Rede;
        switch (nome)
        {
            case "__dns": return Valor.DeTexto(rede?.ResolverNome(maq, args.Count > 0 ? args[0].Texto : "", args.Count > 1 ? (int)args[1].Num : 0) ?? "");
            case "__wifi_estacoes": return Valor.DeInt(rede?.EstacoesNoAp(maq) ?? 0);
            case "__eth":
                if (args[0].Num != 0) rede?.LigarCabo(maq);
                else rede?.DesligarCabo(maq);
                return Valor.Vazio;
            case "__eth_ip": return Valor.DeTexto(rede?.Wifi(maq).Ip ?? "0.0.0.0");
            case "__eth_roteador": return Valor.DeTexto(Rede.Gateway);
            case "__eth_mac":
            {
                var partes = (rede?.Wifi(maq).Mac ?? "00:00:00:00:00:00").Split(':');
                partes[5] = ((Convert.ToInt32(partes[5], 16) + 3) & 0xFF).ToString("X2");
                return Valor.DeTexto(string.Join(":", partes));
            }
            case "__eth_config":
                if (rede is not null && args[0].Texto is { Length: > 0 } fixo && fixo != "0.0.0.0") rede.Wifi(maq).Ip = fixo;
                return Valor.DeBool(rede is not null);
            case "__mdns":
                if (rede is null) return Valor.DeBool(false);
                if (args.Count > 0 && !string.IsNullOrWhiteSpace(args[0].Texto)) rede.AnunciarMdns(maq, args[0].Texto);
                else rede.PararMdns(maq);
                return Valor.DeBool(true);
            case "__mdns_servico":
                rede?.AnunciarServico(maq, args[0].Texto, args[1].Texto, (int)args[2].Num);
                return Valor.DeBool(rede is not null);
            case "__mdns_buscar":
            {
                // "nome|ip|porta" por linha.
                var achados = rede?.BuscarServico(maq, args[0].Texto, args[1].Texto) ?? [];
                return Valor.DeTexto(string.Join("\n", achados.Select(a => $"{a.Nome}|{a.Ip}|{a.Porta}")));
            }
        }
        return Valor.Vazio;
    }

    readonly List<PortaUdp> portasUdpDoNativo = [];
    readonly Dictionary<int, Datagrama> ultimoDatagrama = [];

    /// <summary>
    /// "__udp_abrir"(porta) -> id; "__udp_grupo"(id, ip); "__udp_enviar"(id,
    /// host, porta, bytes) -> bool; "__udp_receber"(id, buffer) -> bytes do
    /// proximo datagrama (-1: nenhum); "__udp_remetente"(id) -> "ip:porta";
    /// "__udp_porta"(id); "__udp_esperando"(id); "__udp_fechar"(id).
    /// </summary>
    Valor UdpDoNativo(string nome, List<Valor> args)
    {
        var rede = maq.Rede;
        PortaUdp Porta(int i) => i >= 0 && i < portasUdpDoNativo.Count ? portasUdpDoNativo[i] : null;
        int id = args.Count > 0 ? (int)args[0].Num : -1;
        switch (nome)
        {
            case "__udp_abrir":
            {
                if (rede is null) return Valor.DeInt(-1);
                var p = rede.AbrirUdp(maq, id);
                if (p is null) return Valor.DeInt(-1);
                portasUdpDoNativo.Add(p);
                return Valor.DeInt(portasUdpDoNativo.Count - 1);
            }
            case "__udp_grupo":
                if (Porta(id) is { } pg && args.Count > 1) rede?.EntrarNoGrupo(pg, args[1].Texto ?? "");
                return Valor.Vazio;
            case "__udp_enviar":
            {
                if (rede is null || Porta(id) is not { } p || args.Count < 4 || args[3].Referencia is not Valor[] bytes)
                    return Valor.DeBool(false);
                var dados = new byte[bytes.Length];
                for (int i = 0; i < dados.Length; i++) dados[i] = (byte)((long)bytes[i].Num & 0xFF);
                return Valor.DeBool(rede.EnviarUdp(p, args[1].Texto ?? "", (int)args[2].Num, dados));
            }
            case "__udp_receber":
            {
                if (Porta(id) is not { } p || args.Count < 2 || args[1].Referencia is not Valor[] destino) return Valor.DeInt(-1);
                var d = p.Proximo();
                if (d is null) return Valor.DeInt(-1);
                ultimoDatagrama[id] = d;
                int n = Math.Min(d.Dados.Length, destino.Length);
                for (int i = 0; i < n; i++) destino[i] = Valor.DeC(d.Dados[i], TipoC.UChar);
                return Valor.DeInt(n);
            }
            case "__udp_remetente":
                return Valor.DeTexto(ultimoDatagrama.TryGetValue(id, out var ultimo) ? $"{ultimo.Ip}:{ultimo.Porta}" : "0.0.0.0:0");
            case "__udp_porta": return Valor.DeInt(Porta(id)?.Porta ?? 0);
            case "__udp_esperando": return Valor.DeInt(Porta(id)?.Esperando ?? 0);
            case "__udp_fechar":
                if (Porta(id) is { } pf)
                {
                    rede?.FecharUdp(pf);
                    portasUdpDoNativo[id] = null;
                }
                return Valor.Vazio;
        }
        return Valor.Vazio;
    }
}
