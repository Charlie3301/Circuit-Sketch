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
        }
        return Valor.Vazio;
    }

    /// <summary>
    /// "__dns"(nome) -> o IP em texto (vazio: nao achou); "__wifi_estacoes"()
    /// -> quantas placas estao no AP desta; "__mdns"(nome) -> anuncia
    /// "nome.local" (vazio: para); "__mdns_servico"(servico, protocolo,
    /// porta); "__mdns_buscar"(servico, protocolo) -> "nome|ip|porta" por linha.
    /// </summary>
    Valor NomesDoNativo(string nome, List<Valor> args)
    {
        var rede = maq.Rede;
        switch (nome)
        {
            case "__dns": return Valor.DeTexto(rede?.ResolverNome(maq, args.Count > 0 ? args[0].Texto : "") ?? "");
            case "__wifi_estacoes": return Valor.DeInt(rede?.EstacoesNoAp(maq) ?? 0);
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
