using System.IO;

namespace Simulador.App;

/// <summary>
/// Os projetos de uma pasta (CS_PASTA), cada um aberto e rodado na janela
/// como a pessoa rodaria: a linguagem, se compila nativo (e, se nao, por
/// que), o erro do compilador e o que o console e o monitor mostraram.
/// Nao confere nada -- e o relatorio para achar o que falta.
/// </summary>
public static partial class Autoteste
{
    static void Diagnostico_Pasta()
    {
        string pasta = Environment.GetEnvironmentVariable("CS_PASTA");
        if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta))
        {
            saida.AppendLine("diagnostico: CS_PASTA nao aponta para uma pasta");
            return;
        }
        int ms = int.TryParse(Environment.GetEnvironmentVariable("CS_MS"), out int lido) ? lido : 4000;
        foreach (string arquivo in Directory.GetFiles(pasta, "*.sktch").Order())
        {
            saida.AppendLine($"=== {Path.GetFileName(arquivo)}");
            try
            {
                var (circuito, codigo) = Sktch.Abrir(arquivo);
                foreach (var placa in circuito.Pecas.Where(p => PerfilPlaca.EhPlaca(p.Tipo)))
                {
                    var projeto = new Projeto
                    {
                        Sketch = placa.Sketch ?? codigo ?? "",
                        Arquivos = circuito.Arquivos,
                        Placa = placa.Tipo,
                        Bibliotecas = Acervo.ParaCompilar(),
                        DoSimulador = Acervo.EhDoSimulador,
                    };
                    var linguagem = Compilador.Detectar(projeto.Sketch);
                    saida.AppendLine($"  placa {placa.Id} ({placa.Tipo}), {linguagem}, abas: {string.Join(", ", circuito.Arquivos?.Keys.ToArray() ?? [])}");
                    string porque = CompiladorNativo.PorQueNao(projeto);
                    saida.AppendLine($"  nativo: {porque ?? "sim"}");
                    if (porque is null && linguagem is Linguagem.Arduino or Linguagem.C)
                    {
                        try { CompiladorNativo.Compilar(projeto); saida.AppendLine("  clang: compilou"); }
                        catch (Exception e) { saida.AppendLine($"  clang: {e.Message.Split('\n')[0]}"); }
                    }
                }

                var janela = new MainWindow();
                try
                {
                    janela.TesteAbrir(circuito, codigo);
                    janela.TesteRodar();
                    EsperarPor(() => false, ms);

                    // O servidor da placa, pelo navegador do computador: a pagina e o WebSocket.
                    foreach (var servidor in janela.TesteRedeSimulada?.Servidores.ToArray() ?? [])
                    {
                        if (servidor.UrlLocal is null) continue;
                        try
                        {
                            using var navegador = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                            var r = navegador.GetAsync(servidor.UrlLocal).Result;
                            string pagina = r.Content.ReadAsStringAsync().Result;
                            saida.AppendLine($"  pagina {servidor.UrlLocal}: {(int)r.StatusCode}, {pagina.Length} caracteres");
                        }
                        catch (Exception e) { saida.AppendLine($"  pagina {servidor.UrlLocal}: {e.GetBaseException().Message}"); }
                        try
                        {
                            using var ws = new System.Net.WebSockets.ClientWebSocket();
                            var ate = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                            ws.ConnectAsync(new Uri(servidor.UrlLocal.Replace("http://", "ws://") + "ws"), ate.Token).Wait();
                            var buf = new byte[512];
                            var recebido = ws.ReceiveAsync(buf, ate.Token).Result;
                            saida.AppendLine($"  websocket: conectou; primeira mensagem: {System.Text.Encoding.UTF8.GetString(buf, 0, recebido.Count)}");
                            // CS_WS_ENVIAR: o que a pagina manda (o joystick), a cada 50 ms por 1 s.
                            if (Environment.GetEnvironmentVariable("CS_WS_ENVIAR") is { Length: > 0 } envio)
                            {
                                // "k;c,0,60,2,0": cada parte uma vez, e a ultima repetida (o joystick).
                                var partes = envio.Split(';');
                                for (int k = 0; k < partes.Length + 30; k++)
                                {
                                    var bytes = System.Text.Encoding.UTF8.GetBytes(partes[Math.Min(k, partes.Length - 1)]);
                                    ws.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, ate.Token).Wait();
                                    EsperarPor(() => false, k < partes.Length ? 300 : 50);
                                }
                                var maq = servidor.Dona;
                                var pwm = Enumerable.Range(0, maq.Pwm.Length).Where(p => maq.Pwm[p] > 0).Select(p => $"{p}={maq.Pwm[p]}").ToList();
                                var tons = Enumerable.Range(0, maq.Frequencia.Length).Where(p => maq.Frequencia[p] > 0).Select(p => $"{p}={maq.Frequencia[p]}Hz").ToList();
                                var servos = Enumerable.Range(0, maq.AnguloServo.Length).Where(p => maq.AnguloServo[p] >= 0).Select(p => $"{p}={maq.AnguloServo[p]}").ToList();
                                // A ultima mensagem que a placa mandou depois dos comandos.
                                string ultima = "";
                                var ateFim = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
                                try
                                {
                                    while (true)
                                    {
                                        var r2 = ws.ReceiveAsync(buf, ateFim.Token).Result;
                                        ultima = System.Text.Encoding.UTF8.GetString(buf, 0, r2.Count);
                                    }
                                }
                                catch { }
                                saida.AppendLine($"  a placa mandou por ultimo: {ultima}");
                                saida.AppendLine($"  depois de \"{envio}\": pwm {string.Join(" ", pwm)} | tons {string.Join(" ", tons)} | servos {string.Join(" ", servos)}");
                            }
                        }
                        catch (Exception e) { saida.AppendLine($"  websocket: {e.GetBaseException().Message}"); }
                    }
                    saida.AppendLine("  console: " + string.Join(" | ", janela.TesteConsole.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(40)));
                    saida.AppendLine("  serial: " + string.Join(" | ", janela.TesteSerial.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(10)));
                }
                finally
                {
                    janela.TesteParar();
                    janela.Close();
                }
            }
            catch (Exception e)
            {
                saida.AppendLine($"  estourou: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
