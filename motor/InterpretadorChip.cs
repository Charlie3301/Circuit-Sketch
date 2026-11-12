namespace Simulador;

public partial class Interpretador
{
    /// <summary>
    /// As funcoes de pino POR NOME, que so um chip proprio usa.
    ///
    /// Num chip feito pela pessoa nao existe "GPIO 13": existe o pino
    /// que ela chamou de CLK no .chip.json. O nome vira indice pelo
    /// perfil sintetizado da peca, e dai para baixo e a mesma Maquina
    /// que roda uma placa de verdade -- mesmo pinMode, mesmo
    /// digitalWrite, mesma interrupcao.
    /// </summary>
    bool ChamarPinoPorNome(string nome, Chamada c, Ambiente amb, out Valor resultado)
    {
        resultado = Valor.Vazio;

        switch (nome)
        {
            case "modoDoPino":
            case "pinModeNamed":
                maq.PinMode(PinoDoChip(c, amb, 0), (int)Numero(c, amb, 1));
                return true;

            case "escreverPino":
            case "digitalWriteNamed":
                maq.DigitalWrite(PinoDoChip(c, amb, 0), (int)Numero(c, amb, 1));
                return true;

            case "lerPino":
            case "digitalReadNamed":
                resultado = Valor.DeInt(maq.DigitalRead(PinoDoChip(c, amb, 0)));
                return true;

            case "lerAnalogico":
            case "analogReadNamed":
                resultado = Valor.DeInt(maq.AnalogRead(PinoDoChip(c, amb, 0)));
                return true;

            case "escreverAnalogico":
            case "analogWriteNamed":
                maq.AnalogWrite(PinoDoChip(c, amb, 0), (int)Numero(c, amb, 1));
                return true;

            case "aoMudarPino":
            {
                if (c.Args.Count < 3 || SemEndereco(c.Args[2]) is not Variavel alvo)
                    throw new ErroDeExecucao(
                        Idioma.T("aoMudarPino() espera o nome de uma funcao no 3o argumento, como em aoMudarPino(\"CLK\", RISING, contar)."), c.Linha);

                if (!funcoes.ContainsKey(alvo.Nome))
                    throw new ErroDeExecucao(
                        Idioma.T("aoMudarPino() aponta para '{0}()', que nao existe neste chip.", alvo.Nome), c.Linha);

                maq.Vigiar(PinoDoChip(c, amb, 0), alvo.Nome, (int)Numero(c, amb, 1));
                return true;
            }
        }

        return false;
    }

    /// <summary>O indice do pino pelo nome, ou erro dizendo quais existem.</summary>
    int PinoDoChip(Chamada c, Ambiente amb, int i)
    {
        string nome = i < c.Args.Count ? Avaliar(c.Args[i], amb).Mostrar() : "";
        int indice = maq.Perfil?.Numero(nome) ?? -1;

        if (indice < 0)
            throw new ErroDeExecucao(
                Idioma.T("este chip nao tem o pino \"{0}\". Os pinos sao os que estao no .chip.json.", nome), c.Linha);

        return indice;
    }

    double Numero(Chamada c, Ambiente amb, int i)
        => i < c.Args.Count ? Avaliar(c.Args[i], amb).Num : 0;

    /// <summary>O chipInit() do chip, se ele tiver um.</summary>
    public void IniciarChip() => ChamarSeExistir("chipInit");

    /// <summary>
    /// Um quadro do chip: as interrupcoes de borda e o chipLoop().
    ///
    /// Separado do Loop() da placa de proposito: um chip nao tem setup()
    /// nem loop(), e chamar os dois nomes faria o sketch de uma placa
    /// rodar por engano dentro de um chip.
    /// </summary>
    public void PassoDoChip()
    {
        AtenderInterrupcoes();
        ChamarSeExistir("chipLoop");
    }

    /// <summary>O chip declarou alguma das duas funcoes?</summary>
    public bool EhChip() => funcoes.ContainsKey("chipInit") || funcoes.ContainsKey("chipLoop");
}
