namespace Simulador.App;

/// <summary>
/// Uma <see cref="Maquina"/> por placa do circuito.
///
/// Até aqui o simulador tinha uma máquina só. Com duas placas na tela,
/// o `D14` de qualquer uma acionava a mesma saída, o `Serial.println`
/// das duas caía no mesmo monitor e um só programa rodava. O aviso
/// existia, mas era só um aviso.
///
/// A bancada resolve isso pelo lugar certo: cada placa é uma máquina, e
/// **quem diz de qual placa é cada pino é o netlist**. Um LED ligado no
/// `D14` da segunda placa consulta a segunda máquina, sem que o desenho
/// precise saber que existe mais de uma.
///
/// Máquina de placa que saiu do circuito é descartada; placa que entrou
/// ganha uma nova. Isso acontece a cada mudança de circuito, então tem
/// que ser barato — e é: só um dicionário por id.
/// </summary>
public sealed class Bancada
{
    readonly Dictionary<string, Maquina> porPlaca = [];

    /// <summary>
    /// Quando ha uma maquina so, ela responde por tudo -- inclusive pelo
    /// que esta solto.
    ///
    /// E o comportamento de antes da bancada existir, e continua valendo
    /// para o circuito de uma placa, que e a esmagadora maioria. Sem
    /// isto, uma peca cujo pino ainda nao foi indexado cairia na maquina
    /// desligada e apagaria sem motivo.
    /// </summary>
    Maquina unica;

    /// <summary>
    /// Máquina de quem não está ligado em placa nenhuma.
    ///
    /// Existe para o desenho não precisar testar null em toda linha: um
    /// LED solto consulta esta, que está sempre desligada, e apaga.
    /// </summary>
    Maquina solta;

    public Maquina Solta => unica ?? (solta ??= Desligada());

    static Maquina Desligada()
    {
        var m = new Maquina();
        m.Desligar();
        return m;
    }

    /// <summary>Placa cujo monitor e cujos controles aparecem na janela.</summary>
    public string IdEmFoco { get; set; }

    /// <summary>
    /// Acerta a bancada com o circuito: cria o que falta, joga fora o
    /// que sobrou. Devolve true se alguma coisa mudou.
    /// </summary>
    public bool Sincronizar(Circuito circuito)
    {
        var placas = circuito.Placas().ToList();
        bool mudou = false;

        foreach (var placa in placas)
        {
            if (porPlaca.ContainsKey(placa.Id)) continue;
            porPlaca[placa.Id] = new Maquina
            {
                Perfil = PerfilPlaca.De(placa.Tipo) ?? PerfilPlaca.Esp32
            };
            mudou = true;
        }

        foreach (string id in porPlaca.Keys.ToList())
        {
            if (placas.Any(p => p.Id == id)) continue;
            porPlaca[id].Parar = true;
            porPlaca.Remove(id);
            mudou = true;
        }

        // O perfil segue o tipo: trocar a placa por outra no mesmo lugar
        // tem que trocar a faixa do ADC e os pinos válidos junto.
        foreach (var placa in placas)
        {
            var perfil = PerfilPlaca.De(placa.Tipo);
            if (perfil is null || ReferenceEquals(porPlaca[placa.Id].Perfil, perfil)) continue;
            porPlaca[placa.Id].Perfil = perfil;
            mudou = true;
        }

        if (IdEmFoco is null || !porPlaca.ContainsKey(IdEmFoco))
            IdEmFoco = placas.FirstOrDefault()?.Id;

        return mudou;
    }

    /// <summary>
    /// Bancada de uma maquina so, que responde por qualquer pino.
    ///
    /// E como o simulador se comportava antes: serve para os testes de
    /// uma placa e para os modos de linha de comando, que nao precisam
    /// saber que existe bancada.
    /// </summary>
    public static Bancada DeUmaSo(Maquina maq) => new() { unica = maq };

    /// <summary>A máquina de uma placa, ou a solta quando não há placa.</summary>
    public Maquina De(string idDaPlaca)
        => unica
           ?? (idDaPlaca is not null && porPlaca.TryGetValue(idDaPlaca, out var m)
               ? m : Solta);

    /// <summary>
    /// A máquina que a janela mostra: monitor serial, avisos e controles.
    /// É a da placa selecionada, a mesma cujo código está no editor.
    /// </summary>
    public Maquina EmFoco => unica ?? De(IdEmFoco);

    public IEnumerable<KeyValuePair<string, Maquina>> Todas
        => unica is not null
           ? [new KeyValuePair<string, Maquina>("", unica)]
           : porPlaca;

    public int Quantas => porPlaca.Count;

    public void Reiniciar()
    {
        foreach (var m in Todas) m.Value.Reiniciar();
    }

    public void Parar()
    {
        foreach (var m in Todas) m.Value.Parar = true;
    }
}
