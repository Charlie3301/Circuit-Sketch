// ============================================================
//  Porta virtual do CircuitSketch (pluggable discovery).
//
//  Isto era um porta.ps1 chamado por um porta.bat. Funcionava na
//  maior parte das vezes -- e quando nao funcionava, o log da IDE
//  dizia:
//
//      Waiting discovery process termination: exit status 128
//      Discovery circuitsketch failed to run: calling HELLO: timeout
//
//  O processo saia sozinho e SO ENTAO o HELLO estourava. Medindo, o
//  PowerShell respondia o HELLO em ~1,7 s, dentro do prazo -- entao
//  a causa nao era lentidao, era a propria pilha cmd.exe ->
//  powershell morrendo em certas partidas. Sem porta, a IDE nao
//  deixa gravar e o Monitor Serial nem tenta conectar.
//
//  Um executavel nativo tira a pilha inteira do caminho: sobe em
//  milissegundos e nao depende de politica de execucao, de perfil do
//  PowerShell nem de runtime instalado na maquina do laboratorio.
//
//  O protocolo e texto na entrada e JSON na saida, uma mensagem por
//  linha. A IDE mantem o processo vivo enquanto estiver aberta:
//  https://arduino.github.io/arduino-cli/latest/pluggable-discovery-specification/
//
//  A porta anunciada nao existe em lugar nenhum -- e exatamente o
//  ponto: da a IDE algo para selecionar, e o Upload passa a chegar
//  no simulador.
// ============================================================

using System.Text;

// JSON escrito a mao, e nao com JsonSerializer: sao seis mensagens de
// forma fixa, e assim o executavel nao carrega o serializador (que em
// AOT ainda pediria um contexto gerado).
const string Porta =
    "{\"address\":\"circuitsketch\",\"label\":\"CircuitSketch (simulador)\","
    + "\"protocol\":\"circuitsketch\",\"protocolLabel\":\"Simulador\","
    + "\"hardwareId\":\"\",\"properties\":{\"vid\":\"0x0000\",\"pid\":\"0x0000\"}}";

var saida = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
{
    AutoFlush = true
};
var entrada = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));

void Responder(string json)
{
    // \n e nao WriteLine: o protocolo e uma mensagem por linha, e o
    // \r\n do Windows ja apareceu como lixo em cliente que corta em \n.
    saida.Write(json);
    saida.Write('\n');
}

while (true)
{
    string linha = entrada.ReadLine();
    if (linha is null) break;

    var partes = linha.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (partes.Length == 0) continue;
    string comando = partes[0].ToUpperInvariant();

    if (comando == "HELLO")
    {
        Responder("{\"eventType\":\"hello\",\"protocolVersion\":1,\"message\":\"OK\"}");
    }
    else if (comando == "START")
    {
        Responder("{\"eventType\":\"start\",\"message\":\"OK\"}");
    }
    else if (comando == "LIST")
    {
        Responder("{\"eventType\":\"list\",\"ports\":[" + Porta + "]}");
    }
    else if (comando == "START_SYNC")
    {
        Responder("{\"eventType\":\"start_sync\",\"message\":\"OK\"}");
        // A porta esta sempre la: anuncia uma vez e fica quieto.
        Responder("{\"eventType\":\"add\",\"port\":" + Porta + "}");
    }
    else if (comando == "STOP")
    {
        Responder("{\"eventType\":\"stop\",\"message\":\"OK\"}");
    }
    else if (comando == "QUIT")
    {
        Responder("{\"eventType\":\"quit\",\"message\":\"OK\"}");
        break;
    }
    else
    {
        Responder("{\"eventType\":\"command_error\",\"error\":true,"
                  + "\"message\":\"comando desconhecido: " + Limpar(comando) + "\"}");
    }
}

return 0;

// Comando estranho nao pode quebrar o JSON da resposta: aspas, barra e
// caractere de controle saem fora em vez de virar sintaxe.
static string Limpar(string texto)
{
    var limpo = new StringBuilder(texto.Length);
    foreach (char c in texto)
        if (c != '"' && c != '\\' && !char.IsControl(c)) limpo.Append(c);
    return limpo.ToString();
}
