using System.Globalization;
using System.Text;

namespace Simulador.App;

/// <summary>
/// Escreve o circuito como um diagram.json do Wokwi -- o mesmo formato, na
/// mesma ordem e com a mesma formatacao que o editor deles grava, mais o
/// "name" do projeto, o "author" e o "editor".
///
/// E o que aparece na aba "diagram.json" da IDE (e o que ela le de volta,
/// pelo <see cref="ImportarWokwi"/>), e o que o menu Exportar salva. As
/// tabelas de tipos, pinos e cores ficam em <see cref="FormatoWokwi"/>.
///
/// Tres coisas do formato deles que guiam a escrita:
///
///   1. A peca guarda o que nao e posicao em "attrs", sempre texto ("value":
///      "220"). O que o Wokwi conhece vai com o nome dele ("color", "value",
///      "pins"); o que so existe aqui (a ficha do motor, a bateria, a posicao
///      do potenciometro) vai com nome nosso, que o Wokwi ignora.
///   2. O fio e [de, para, cor, caminho], e o caminho anda em "h" e "v" a
///      partir do primeiro pino -- as dobras daqui viram esses passos.
///   3. O sketch e os arquivos do cartao nao moram no diagrama: la tambem nao.
/// </summary>
public static class ExportarWokwi
{
    /// <param name="nome">O "name" (o nome do projeto). Nulo: o do circuito.</param>
    /// <param name="autor">O "author". Nulo: o do circuito.</param>
    public static string Gerar(Circuito circuito, string nome = null, string autor = null)
    {
        var texto = new StringBuilder();
        texto.Append("{\n");
        texto.Append("  \"version\": 1,\n");
        texto.Append($"  \"name\": {FormatoWokwi.Aspas(nome ?? circuito.Nome ?? "sketch")},\n");
        texto.Append($"  \"author\": {FormatoWokwi.Aspas(autor ?? circuito.Autor ?? "")},\n");
        texto.Append($"  \"editor\": {FormatoWokwi.Aspas(FormatoWokwi.Editor)},\n");

        var partes = circuito.Pecas.Where(p => Catalogo.Obter(p.Tipo) is not null).Select(Parte).ToList();
        Lista(texto, "parts", partes);

        var ligacoes = new List<string>();
        foreach (var fio in circuito.Fios)
            if (Ligacao(circuito, fio) is { } linha) ligacoes.Add(linha);
        Lista(texto, "connections", ligacoes);

        // O resto do topo que veio num diagrama deles ("serialMonitor"...),
        // e as dependencias por ultimo, como o Wokwi grava.
        string dependencias = "{}";
        foreach (var (campo, cru) in circuito.ExtrasDoDiagrama ?? [])
        {
            string valor;
            try { using var doc = System.Text.Json.JsonDocument.Parse(cru); valor = FormatoWokwi.Json(doc.RootElement, 2, campo.Length + 4); }
            catch (System.Text.Json.JsonException) { continue; }
            if (campo == "dependencies") { dependencias = valor; continue; }
            texto.Append($"  {FormatoWokwi.Aspas(campo)}: {valor},\n");
        }
        texto.Append($"  \"dependencies\": {dependencias}\n");
        texto.Append("}\n");
        return texto.ToString();
    }

    static void Lista(StringBuilder texto, string nome, List<string> itens)
    {
        if (itens.Count == 0)
        {
            texto.Append($"  \"{nome}\": [],\n");
            return;
        }
        texto.Append($"  \"{nome}\": [\n");
        for (int i = 0; i < itens.Count; i++)
            texto.Append(itens[i]).Append(i < itens.Count - 1 ? ",\n" : "\n");
        texto.Append("  ],\n");
    }

    // =====================================================================
    //  as pecas
    // =====================================================================

    /// <summary>
    /// Uma peca: numa linha so quando cabe, como o editor deles faz; senao um
    /// campo por linha (e os attrs abertos tambem, se ainda nao couberem).
    /// </summary>
    static string Parte(PecaNoCircuito peca)
    {
        var campos = new List<(string Chave, string Valor)>
        {
            ("type", FormatoWokwi.Aspas(FormatoWokwi.TipoParaWokwi(peca.Tipo))),
            ("id", FormatoWokwi.Aspas(peca.Id)),
            ("top", FormatoWokwi.Num(peca.Y)),
            ("left", FormatoWokwi.Num(peca.X)),
        };
        if (peca.Rotacao != 0) campos.Add(("rotate", FormatoWokwi.Num(peca.Rotacao)));

        var atributos = Atributos(peca);
        string attrsEmLinha = atributos.Count == 0 ? "{}"
            : "{ " + string.Join(", ", atributos.Select(a => $"{FormatoWokwi.Aspas(a.Chave)}: {FormatoWokwi.Aspas(a.Valor)}")) + " }";

        string emLinha = "    { " + string.Join(", ", campos.Select(c => $"\"{c.Chave}\": {c.Valor}"))
                         + $", \"attrs\": {attrsEmLinha} }}";
        if (emLinha.Length + 1 <= FormatoWokwi.Largura) return emLinha;

        var aberto = new StringBuilder("    {\n");
        foreach (var (chave, valor) in campos) aberto.Append($"      \"{chave}\": {valor},\n");
        string linhaDosAttrs = $"      \"attrs\": {attrsEmLinha}";
        if (linhaDosAttrs.Length <= FormatoWokwi.Largura) aberto.Append(linhaDosAttrs).Append('\n');
        else
        {
            aberto.Append("      \"attrs\": {\n");
            for (int i = 0; i < atributos.Count; i++)
                aberto.Append($"        {FormatoWokwi.Aspas(atributos[i].Chave)}: {FormatoWokwi.Aspas(atributos[i].Valor)}")
                      .Append(i < atributos.Count - 1 ? ",\n" : "\n");
            aberto.Append("      }\n");
        }
        aberto.Append("    }");
        return aberto.ToString();
    }

    /// <summary>
    /// Os atributos da peca. Primeiro os que o Wokwi conhece; depois os
    /// nossos, so quando fogem do padrao (a peca comum sai com "attrs": {}).
    /// </summary>
    internal static List<(string Chave, string Valor)> Atributos(PecaNoCircuito p)
    {
        var a = new List<(string, string)>();
        void Por(string chave, string valor) => a.Add((chave, valor));
        static string Exato(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);

        switch (p.Tipo)
        {
            case "led": Por("color", FormatoWokwi.CorDoLedParaWokwi(p.Cor)); break;
            case "resistor": Por("value", FormatoWokwi.Num(p.Ohms <= 0 ? 220 : p.Ohms)); break;
            case "lcd1602-i2c" or "lcd2004-i2c": Por("pins", "i2c"); break;
            case "texto": Por("text", p.Nome ?? ""); break;
            case "servo" when FormatoWokwi.BracoParaWokwi(p.Braco) is { } horn: Por("horn", horn); break;
        }

        // Os do Wokwi que vieram num diagrama deles e o simulador nao usa.
        if (p.AtributosDoWokwi is { } guardados)
            foreach (var (chave, valor) in guardados)
                if (!a.Any(x => x.Item1 == chave)) Por(chave, valor);

        if (!string.IsNullOrEmpty(p.Nome) && p.Tipo != "texto") Por("nome", p.Nome);
        if (p.Cor is not null && p.Cor != "vermelho" && p.Tipo != "led") Por("cor", p.Cor);
        if (p.Tipo != "resistor" && Math.Abs(p.Ohms - 220) > 1e-9) Por("ohms", Exato(p.Ohms));
        if (p.Giro != 0) Por("giro", Exato(p.Giro));
        if (p.Giro2 != 0.5) Por("giro2", Exato(p.Giro2));
        if (p.Giro3 != 0.5) Por("giro3", Exato(p.Giro3));
        if (p.Giro4 != 0.5) Por("giro4", Exato(p.Giro4));
        if (p.Bits != 0) Por("bits", p.Bits.ToString(CultureInfo.InvariantCulture));
        var fabrica = Baterias.DeFabrica(p.Tipo);
        if (p.Volts != (fabrica?.Volts ?? Reguladores.VoltsDeFabrica(p.Tipo) ?? 9)) Por("volts", Exato(p.Volts));
        if (p.MiliAmperes != (fabrica?.MiliAmperes ?? 500)) Por("corrente", Exato(p.MiliAmperes));
        if (p.MiliAmperesHora != (fabrica?.MiliAmperesHora ?? 500)) Por("capacidade", Exato(p.MiliAmperesHora));
        if (p.Modelo is not null) Por("modelo", p.Modelo);
        if (p.Modelo == "personalizado" && p.Ficha is { } f)
        {
            Por("ficha-volts", Exato(f.Volts));
            Por("ficha-rpm-kv", Exato(f.RpmOuKv));
            Por("ficha-resistencia", Exato(f.Resistencia));
            Por("ficha-corrente-vazio", Exato(f.CorrenteVazio));
            Por("ficha-corrente-maxima", Exato(f.CorrenteMaxima));
            Por("ficha-inercia", f.Inercia.ToString("R", CultureInfo.InvariantCulture));
            Por("ficha-medida-a", Exato(f.MedidaA));
            Por("ficha-medida-b", Exato(f.MedidaB));
            Por("ficha-polos", f.ParesDePolos.ToString(CultureInfo.InvariantCulture));
        }
        if (p.Amperes != 0) Por("amperes", Exato(p.Amperes));
        if (p.Acabamento is not null) Por("acabamento", p.Acabamento);
        if (p.Braco is not null && !(p.Tipo == "servo" && FormatoWokwi.BracoParaWokwi(p.Braco) is not null)) Por("braco", p.Braco);
        if (p.Ssid is not null) Por("ssid", p.Ssid);
        if (p.Senha is not null) Por("senha", p.Senha);
        if (p.PlacaQueManda is not null) Por("placa-que-manda", p.PlacaQueManda);
        if (p.JanelaUs != 0) Por("janela-us", Exato(p.JanelaUs));
        if (p.Espelhada) Por("espelhada", "true");
        if (p.Usb is { } usb) Por("usb", usb ? "true" : "false");
        if (p.Teclas is { Count: > 0 })
            Por("teclas", string.Join(";", p.Teclas.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}={k.Value}")));
        return a;
    }

    // =====================================================================
    //  os fios
    // =====================================================================

    static string Ligacao(Circuito circuito, Fio fio)
    {
        var pecaDe = circuito.Achar(fio.DePeca);
        var pecaPara = circuito.Achar(fio.ParaPeca);
        if (pecaDe is null || pecaPara is null) return null;
        if (Catalogo.Obter(pecaDe.Tipo) is null || Catalogo.Obter(pecaPara.Tipo) is null) return null;

        string de = $"{pecaDe.Id}:{FormatoWokwi.PinoParaWokwi(pecaDe.Tipo, fio.DePino)}";
        string para = $"{pecaPara.Id}:{FormatoWokwi.PinoParaWokwi(pecaPara.Tipo, fio.ParaPino)}";
        return $"    [ {FormatoWokwi.Aspas(de)}, {FormatoWokwi.Aspas(para)}, "
               + $"{FormatoWokwi.Aspas(FormatoWokwi.CorDoFioParaWokwi(fio.Cor))}, {Rota(circuito, fio)} ]";
    }

    /// <summary>O caminho do fio no formato deles: "[]" sem dobra, "[ "h12", "v-3" ]" com.</summary>
    internal static string Rota(Circuito circuito, Fio fio)
    {
        // O caminho que veio do Wokwi, se o fio continua com os mesmos cantos.
        if (fio.CaminhoDoWokwi is { } original && circuito.Achar(fio.DePeca) is { } a && circuito.Achar(fio.ParaPeca) is { } b)
        {
            var pontos = ImportarWokwi.Pontos(fio, original, a, b);
            if (pontos.Count == fio.Dobras.Count
                && pontos.Zip(fio.Dobras).All(z => Math.Abs(z.First.X - z.Second.X) < 0.01 && Math.Abs(z.First.Y - z.Second.Y) < 0.01))
                return original.Count == 0 ? "[]" : "[ " + string.Join(", ", original.Select(FormatoWokwi.Aspas)) + " ]";
        }

        var passos = Passos(circuito, fio);
        return passos.Count == 0 ? "[]" : "[ " + string.Join(", ", passos.Select(FormatoWokwi.Aspas)) + " ]";
    }

    /// <summary>
    /// As dobras viram passos relativos a partir do primeiro pino, como eles
    /// gravam. O formato so anda em "h" e "v": um canto que pede os dois eixos
    /// sai como dois passos (o fio chega la em L); nao ha como escrever um
    /// trecho na diagonal sem inventar sintaxe que o Wokwi nao le.
    /// </summary>
    static List<string> Passos(Circuito circuito, Fio fio)
    {
        var passos = new List<string>();
        if (fio.Dobras.Count == 0) return passos;

        var peca = circuito.Achar(fio.DePeca);
        var def = peca is null ? null : Catalogo.Obter(peca.Tipo);
        var pino = def?.Pinos.FirstOrDefault(x => x.Nome == fio.DePino);
        if (pino is null) return passos;

        var ponto = Geometria.PosicaoPino(peca, def, pino);
        foreach (var dobra in fio.Dobras)
        {
            double dx = dobra.X - ponto.X, dy = dobra.Y - ponto.Y;
            if (Math.Abs(dx) >= 0.01) passos.Add("h" + FormatoWokwi.Num(dx));
            if (Math.Abs(dy) >= 0.01) passos.Add("v" + FormatoWokwi.Num(dy));
            ponto = new System.Windows.Point(dobra.X, dobra.Y);
        }
        return passos;
    }
}
