using System.Text;

namespace Simulador.App;

/// <summary>
/// Testes sem janela, rodados com --teste.
///
/// Existem porque "arrastar o LED para a protoboard e ver se prende" nao
/// da para conferir por captura de tela, e eu ja entreguei duas vezes
/// coisa que parecia certa no codigo e nao funcionava na pratica.
/// </summary>
public static partial class Autoteste
{
    static int passou, falhou;
    static readonly StringBuilder saida = new();

    /// <summary>
    /// Roda um bloco e transforma excecao em FALHA.
    ///
    /// Sem isto, um teste que estoura sobe ate o handler global, que abre
    /// um MessageBox -- e num teste sem janela ninguem clica, entao a
    /// suite fica pendurada para sempre em vez de acusar o erro. Custou
    /// dois timeouts ate eu perceber.
    /// </summary>
    /// <summary>
    /// Só os blocos cujo nome contém isto (--so). Para quem está mexendo
    /// numa coisa só e não quer esperar a bateria inteira a cada volta.
    /// </summary>
    public static string Filtro { get; set; }

    static void Bloco(string nome, Action corpo)
    {
        if (!string.IsNullOrEmpty(Filtro)
            && !nome.Contains(Filtro, StringComparison.OrdinalIgnoreCase)) return;

        try { corpo(); }
        catch (Exception ex)
        {
            falhou++;
            saida.AppendLine($"  FALHA {nome} estourou  {ex.GetType().Name}: {ex.Message}");
            // Onde estourou: as primeiras linhas da pilha.
            foreach (string linha in (ex.StackTrace ?? "").Split('\n').Take(4)) saida.AppendLine("        " + linha.Trim());
        }
    }

    public static string Rodar()
    {
        passou = falhou = 0;
        saida.Clear();

        // CS_CPP_NATIVO=1: a suite inteira com o C++ compilado de verdade (o
        // caminho da janela), para comparar com o interpretado.
        Compilador.CppNativo = Environment.GetEnvironmentVariable("CS_CPP_NATIVO") == "1";

        // A suite nao pode depender do WiFi da maquina que a roda: a
        // mesma execucao tem que dar o mesmo resultado no notebook do
        // laboratorio e na maquina de casa. Quem testa a leitura do
        // radio de verdade e Radio_DoComputador, com saida gravada.
        RadioDoComputador.Desligado = true;
        RadioDoComputador.Esquecer();

        Bloco("Catalogo1_TemAsPecas", Catalogo1_TemAsPecas);
        Bloco("Encaixe_LedPertoDoFuro", Encaixe_LedPertoDoFuro);
        Bloco("Encaixe_LedLongeNaoMexe", Encaixe_LedLongeNaoMexe);
        Bloco("Netlist_PernaNoFuroConduz", Netlist_PernaNoFuroConduz);
        Bloco("Netlist_ColunaLigaLedAoGpio", Netlist_ColunaLigaLedAoGpio);
        Bloco("Netlist_TrilhoAtravessaAPlacaInteira", Netlist_TrilhoAtravessaAPlacaInteira);
        Bloco("Netlist_ResistorConduz", Netlist_ResistorConduz);
        Bloco("Rotacao_MovePinos", Rotacao_MovePinos);
        Bloco("Camadas_PrioridadeDoClique", Camadas_PrioridadeDoClique);
        Bloco("Laco_SelecionaVarias", Laco_SelecionaVarias);
        Bloco("Protoboard_ContagemDePontos", Protoboard_ContagemDePontos);
        Bloco("Pinos_LigadoOuNao", Pinos_LigadoOuNao);
        Bloco("Fios_Otimizar", Fios_Otimizar);
        Bloco("Desempenho_RedeNoArraste", Desempenho_RedeNoArraste);
        Bloco("Placas_OutrosKits", Placas_OutrosKits);
        Bloco("Potenciometro_Continuo", Potenciometro_Continuo);
        Bloco("Auditoria_Componentes", Auditoria_Componentes);
        Bloco("Sensores_DaTabela", Sensores_DaTabela);
        Bloco("Lote2_ServoEDisplays", Lote2_ServoEDisplays);
        Bloco("Lote3_Bibliotecas", Lote3_Bibliotecas);
        Bloco("Clique_NosBotoes", Clique_NosBotoes);
        Bloco("Botoes_LigacaoReal", Botoes_LigacaoReal);
        Bloco("Botoes_ApertoDePontaAPonta", Botoes_ApertoDePontaAPonta);
        Bloco("Svgs_EnquadradosNaCaixa", Svgs_EnquadradosNaCaixa);
        Bloco("Svgs_SemFonteRelativa", Svgs_SemFonteRelativa);
        Bloco("Protoboard_CanalDeTresDecimos", Protoboard_CanalDeTresDecimos);
        Bloco("Pecas_NaGradeDeUmDecimo", Pecas_NaGradeDeUmDecimo);
        Bloco("Protoboard170_MiniSemTrilho", Protoboard170_MiniSemTrilho);
        Bloco("Buzzer_OndaDeVerdade", Buzzer_OndaDeVerdade);
        Bloco("Fios_LacoEEstilos", Fios_LacoEEstilos);
        Bloco("Desempenho_QuadroACuadro", Desempenho_QuadroACuadro);
        Bloco("Controles_ChegamNaSimulacao", Controles_ChegamNaSimulacao);
        Bloco("Copiar_LevaOsFios", Copiar_LevaOsFios);
        Bloco("Formato_Sktch", Formato_Sktch);
        Bloco("Protoboard_UmTerminalPorFuro", Protoboard_UmTerminalPorFuro);
        Bloco("Demos_QueVemNoPrograma", Demos_QueVemNoPrograma);
        Bloco("Fios_TodosValidos", Fios_TodosValidos);
        Bloco("Recusas_Honestas", Recusas_Honestas);
        Bloco("Fios_Moldar", Fios_Moldar);
        Bloco("Texto_MetodosDeString", Texto_MetodosDeString);
        Bloco("Wokwi_DiagramaDeVerdade", Wokwi_DiagramaDeVerdade);
        Bloco("Placa_CodigoDeCadaUma", Placa_CodigoDeCadaUma);
        Bloco("Queima_LigacaoErrada", Queima_LigacaoErrada);
        Bloco("Monitor_ParaAIde", Monitor_ParaAIde);
        Bloco("Motor_Enum", Motor_Enum);
        Bloco("Fios_Sobrepostos", Fios_Sobrepostos);
        Bloco("Motor_Interrupcoes", Motor_Interrupcoes);
        Bloco("Wokwi_IdaEVolta", Wokwi_IdaEVolta);
        Bloco("Editor_Realce", Editor_Realce);
        Bloco("Diagrama_Editavel", Diagrama_Editavel);
        Bloco("Bibliotecas_QueFaltam", Bibliotecas_QueFaltam);
        Bloco("Radio_DoComputador", Radio_DoComputador);
        Bloco("Clique_GiraComAPeca", Clique_GiraComAPeca);
        Bloco("Breadboard_FuroDebaixoDaPlaca", Breadboard_FuroDebaixoDaPlaca);
        Bloco("Componentes_EmArvore", Componentes_EmArvore);
        Bloco("Paleta_PorFamilia", Paleta_PorFamilia);
        Bloco("Paineis_EmJanelaPropria", Paineis_EmJanelaPropria);
        Bloco("Abas_SoltasUmaAUma", Abas_SoltasUmaAUma);
        Bloco("Console_DoSimulador", Console_DoSimulador);
        Bloco("Ligacoes_EmCores", Ligacoes_EmCores);
        Bloco("Limites_DaPlaca", Limites_DaPlaca);
        Bloco("Bluetooth_PelaPortaDoPc", Bluetooth_PelaPortaDoPc);
        Bloco("Ligacao_ComandadaPelaLista", Ligacao_ComandadaPelaLista);
        Bloco("Placas_DaAlpha18", Placas_DaAlpha18);
        Bloco("PicoSdk_EmC", PicoSdk_EmC);
        Bloco("Linguagens_DaAlpha18", Linguagens_DaAlpha18);
        Bloco("Bateria_Completa", Bateria_Completa);
        Bloco("Bateria_PecasDaAlpha23", Bateria_PecasDaAlpha23);
        Bloco("Motores_Modulares", Motores_Modulares);
        Bloco("Diagrama_FormatoWokwi", Diagrama_FormatoWokwi);
        Bloco("Bateria_Alimentacao", Bateria_Alimentacao);
        Bloco("Reguladores_DeTensao", Reguladores_DeTensao);
        Bloco("Diodos_NoCircuito", Diodos_NoCircuito);
        Bloco("Passivos_NoTempo", Passivos_NoTempo);
        Bloco("Semicondutores_NoCircuito", Semicondutores_NoCircuito);
        Bloco("Semicondutores_Familias", Semicondutores_Familias);
        Bloco("Passivos_Modelos", Passivos_Modelos);
        Bloco("Lcd_Pontos", Lcd_Pontos);
        Bloco("Tft_Desenho", Tft_Desenho);
        Bloco("Mostradores_NoEditor", Mostradores_NoEditor);
        Bloco("Entradas_Redesenhadas", Entradas_Redesenhadas);
        Bloco("Logica_Chips", Logica_Chips);
        Bloco("Chips_595", Chips_595);
        Bloco("Pecas_IrEBluetooth", Pecas_IrEBluetooth);
        Bloco("Monitor_Baud", Monitor_Baud);
        Bloco("Sensor_Ds18b20", Sensor_Ds18b20);
        Bloco("Plugins_Wokwi", Plugins_Wokwi);
        Bloco("Multimetro_Medidas", Multimetro_Medidas);
        Bloco("Camadas_CliqueDeNovo", Camadas_CliqueDeNovo);
        Bloco("Paleta_SemTravar", Paleta_SemTravar);
        Bloco("Visao_CinzaTransparente", Visao_CinzaTransparente);
        Bloco("Depurador_AoVivo", Depurador_AoVivo);
        Bloco("Barra_NomeDoCircuito", Barra_NomeDoCircuito);
        Bloco("Queima_NivelDeTensao", Queima_NivelDeTensao);
        Bloco("Avisos_SelosNoCircuito", Avisos_SelosNoCircuito);
        Bloco("Sensores_Redesenhados", Sensores_Redesenhados);
        Bloco("Esp8266_Redesenhadas", Esp8266_Redesenhadas);
        Bloco("Pcb_Base", Pcb_Base);
        Bloco("ModulosDeRele_NoCircuito", ModulosDeRele_NoCircuito);
        Bloco("Audio_Estereo", Audio_Estereo);
        Bloco("Placas_PinoAPino", Placas_PinoAPino);
        Bloco("Pecas_UmaAUma", Pecas_UmaAUma);
        Bloco("SemFio_UmAUm", SemFio_UmAUm);
        Bloco("Comportamento_EntradasESensores", Comportamento_EntradasESensores);
        Bloco("Comportamento_SaidasEMotores", Comportamento_SaidasEMotores);
        Bloco("Comportamento_LogicaEModulos", Comportamento_LogicaEModulos);
        Bloco("Comportamento_PassivosESemicondutores", Comportamento_PassivosESemicondutores);
        Bloco("Idiomas_Base", Idiomas_Base);
        Bloco("Projetos_Testados", Projetos_Testados);
        Bloco("Programa_DesfazerERefazer", Programa_DesfazerERefazer);
        Bloco("Plugin_TodasAsPlacas", Plugin_TodasAsPlacas);
        Bloco("Programa_TrocaDeProjetoZera", Programa_TrocaDeProjetoZera);
        Bloco("Demos_BluetoothEEspNow", Demos_BluetoothEEspNow);
        Bloco("Demos_TodasRodam", Demos_TodasRodam);
        Bloco("Configuracoes_ValemDeVerdade", Configuracoes_ValemDeVerdade);
        Bloco("Motor_PreProcessador", Motor_PreProcessador);
        Bloco("Motor_ClassesCpp", Motor_ClassesCpp);
        Bloco("Motor_ContaDoC", Motor_ContaDoC);
        Bloco("Motor_CpuSimulada", Motor_CpuSimulada);
        Bloco("Motor_MemoriaDaPlaca", Motor_MemoriaDaPlaca);
        Bloco("Motor_Spi", Motor_Spi);
        Bloco("Motor_StdString", Motor_StdString);
        Bloco("Oled_ComoNaBiblioteca", Oled_ComoNaBiblioteca);
        Bloco("Teclado_ComoNaBiblioteca", Teclado_ComoNaBiblioteca);
        Bloco("Passo_ComoAStepper", Passo_ComoAStepper);
        Bloco("Serial_EntrePlacas", Serial_EntrePlacas);
        Bloco("Avr_Registradores", Avr_Registradores);
        Bloco("I2c_DoisBarramentos", I2c_DoisBarramentos);
        Bloco("Motor_VetorDeDimensoes", Motor_VetorDeDimensoes);
        Bloco("Memoria_EepromERtc", Memoria_EepromERtc);
        Bloco("MicroPython_Perifericos", MicroPython_Perifericos);
        Bloco("MicroPython_Linguagem", MicroPython_Linguagem);
        Bloco("MicroPython_TimerRtcSpi", MicroPython_TimerRtcSpi);
        Bloco("Bibliotecas_DeFora", Bibliotecas_DeFora);
        Bloco("Bibliotecas_Online", Bibliotecas_Online);
        Bloco("Bibliotecas_Janela", Bibliotecas_Janela);
        Bloco("Rede_MqttNaAula", Rede_MqttNaAula);
        Bloco("Rede_BluetoothNaAula", Rede_BluetoothNaAula);
        Bloco("Ble_ComoNoCore", Ble_ComoNoCore);
        Bloco("Ble_PlacaComPlaca", Ble_PlacaComPlaca);
        Bloco("Ble_ArduinoBle", Ble_ArduinoBle);
        Bloco("Placa_DeVerdade", Placa_DeVerdade);
        Bloco("Motor_TamanhosEVetores", Motor_TamanhosEVetores);
        Bloco("Motor_SonoProfundo", Motor_SonoProfundo);
        Bloco("Tela_TftEmPixels", Tela_TftEmPixels);
        Bloco("Motor_CppDoJogo", Motor_CppDoJogo);
        Bloco("Demo_BaseZero", Demo_BaseZero);
        Bloco("Nativo_CNaPlaca", Nativo_CNaPlaca);
        Bloco("Cpp_Nativo", Cpp_Nativo);
        Bloco("Cpp_NativoVarredura", Cpp_NativoVarredura);
        Bloco("Cpp_Sdks", Cpp_Sdks);
        Bloco("CSharp_Roslyn", CSharp_Roslyn);
        Bloco("Cpp_Perifericos", Cpp_Perifericos);
        Bloco("Cpp_Depurador", Cpp_Depurador);
        Bloco("Cpp_ArduinoEsp32", Cpp_ArduinoEsp32);
        Bloco("Cpp_Pilhas", Cpp_Pilhas);
        Bloco("Cpp_Json", Cpp_Json);
        Bloco("Cpp_Api", Cpp_Api);
        Bloco("MicroPython_Real", MicroPython_Real);
        Bloco("Cpp_Bibliotecas", Cpp_Bibliotecas);
        Bloco("Cpp_Idf", Cpp_Idf);
        Bloco("Chips_Spi", Chips_Spi);
        Bloco("Cpp_Registradores", Cpp_Registradores);
        Bloco("Diagnostico_Pasta", Diagnostico_Pasta);
        Bloco("Demo_Doom", Demo_Doom);
        Bloco("MotorDc_Fisica", MotorDc_Fisica);
        Bloco("MotorDc_NoCircuito", MotorDc_NoCircuito);
        Bloco("MotorDc_Pecas", MotorDc_Pecas);
        Bloco("MotorDc_NaJanela", MotorDc_NaJanela);
        Bloco("Energia_SemPlaca", Energia_SemPlaca);
        Bloco("Drivers_PonteH", Drivers_PonteH);
        Bloco("Passo_Drivers", Passo_Drivers);
        Bloco("Passo_AccelStepper", Passo_AccelStepper);
        Bloco("Servos_Fisicos", Servos_Fisicos);
        Bloco("Servos_Bracos", Servos_Bracos);
        Bloco("Baterias_Presets", Baterias_Presets);
        Bloco("Placas_LedsDaPlaca", Placas_LedsDaPlaca);
        Bloco("Placas_TodosOsPinos", Placas_TodosOsPinos);
        Bloco("Placas_Esp32", Placas_Esp32);
        Bloco("Placas_Pico", Placas_Pico);
        Bloco("Placas_Stm32", Placas_Stm32);
        Bloco("Rede_Roteador", Rede_Roteador);
        Bloco("Anotacao_TextoEJuncao", Anotacao_TextoEJuncao);
        Bloco("Modulos_Desenho", Modulos_Desenho);
        Bloco("AtuadoresAlimentacao_Desenho", AtuadoresAlimentacao_Desenho);
        Bloco("Leds_Familia", Leds_Familia);
        Bloco("Passivos_Desenho", Passivos_Desenho);
        Bloco("Outros_Desenho", Outros_Desenho);
        Bloco("Chave_FimDeCurso", Chave_FimDeCurso);
        Bloco("Esc_Brushless", Esc_Brushless);
        Bloco("Receptor_Radio", Receptor_Radio);
        Bloco("Acessorios_RodasHelices", Acessorios_RodasHelices);
        Bloco("PlayerDap_Pecas", PlayerDap_Pecas);
        Bloco("PlayerDap_Formatos", PlayerDap_Formatos);
        Bloco("PlayerDap_Ape", PlayerDap_Ape);
        Bloco("PlayerDap_Aac", PlayerDap_Aac);
        Bloco("PlayerDap_Wma", PlayerDap_Wma);
        Bloco("Demo_PlayerDap", Demo_PlayerDap);
        Bloco("Temas_TodosLegiveis", Temas_TodosLegiveis);
        Bloco("Tempo_ComoNoChip", Tempo_ComoNoChip);
        Bloco("Motor_TiposDeBibliotecaEmParametro", Motor_TiposDeBibliotecaEmParametro);
        Bloco("Projeto_VariosArquivos", Projeto_VariosArquivos);
        Bloco("Sketches_DaPastaQuebrada", Sketches_DaPastaQuebrada);
        Bloco("Python_Linguagem", Python_Linguagem);
        Bloco("Python_Pecas", Python_Pecas);
        Bloco("Python_SketchesDoProfessor", Python_SketchesDoProfessor);
        Bloco("Python_Deteccao", Python_Deteccao);
        Bloco("Python_RecusaHonesta", Python_RecusaHonesta);
        Bloco("Rele_Comuta", Rele_Comuta);
        Bloco("Infravermelho_Par", Infravermelho_Par);
        Bloco("Passo_Biaxial", Passo_Biaxial);
        Bloco("Disco_Telefone", Disco_Telefone);
        Bloco("Cartao_Sd", Cartao_Sd);
        Bloco("Tela_Tft", Tela_Tft);
        Bloco("Bancada_DuasPlacas", Bancada_DuasPlacas);
        Bloco("Incomodos_Resolvidos", Incomodos_Resolvidos);
        Bloco("Duas_QuemManda", Duas_QuemManda);
        Bloco("Status_ErroNaoSome", Status_ErroNaoSome);
        Bloco("Motor_InterrupcaoNaExpressao", Motor_InterrupcaoNaExpressao);
        Bloco("Motor_BordaDentroDaChamada", Motor_BordaDentroDaChamada);
        Bloco("Depurador_PassoAPasso", Depurador_PassoAPasso);
        Bloco("Depurador_NaJanela", Depurador_NaJanela);
        Bloco("Botoes_DeTodasAsPlacas", Botoes_DeTodasAsPlacas);
        Bloco("Motor_CharEConversoes", Motor_CharEConversoes);
        Bloco("Serial_Entrada", Serial_Entrada);
        Bloco("Rede_WifiEHttp", Rede_WifiEHttp);
        Bloco("Rede_ServidorCru", Rede_ServidorCru);
        Bloco("Rede_Mqtt", Rede_Mqtt);
        Bloco("Rede_Bluetooth", Rede_Bluetooth);
        Bloco("Painel_RedeEBluetooth", Painel_RedeEBluetooth);
        Bloco("Editor_SelecaoEGrade", Editor_SelecaoEGrade);
        Bloco("Pecas_ArteNoLugar", Pecas_ArteNoLugar);
        Bloco("Pecas_TodosOsPinosPegam", Pecas_TodosOsPinosPegam);
        Bloco("Motor_I2c", Motor_I2c);
        Bloco("Protoboard_MetadesSeparadas", Protoboard_MetadesSeparadas);
        Bloco("Pecas_Animadas", Pecas_Animadas);
        Bloco("Vinculos_TecladoEControle", Vinculos_TecladoEControle);
        Bloco("Pecas_DeEsquema", Pecas_DeEsquema);
        Bloco("Programa_Acabamento", Programa_Acabamento);
        Bloco("Visao_DeCircuito", Visao_DeCircuito);
        Bloco("Pecas_Logica", Pecas_Logica);
        Bloco("Analisador_Logico", Analisador_Logico);
        Bloco("Cartao_ComExplorador", Cartao_ComExplorador);
        Bloco("Motor_Estruturas", Motor_Estruturas);
        Bloco("Rede_PontoDeAcesso", Rede_PontoDeAcesso);
        Bloco("Rede_Nrf24", Rede_Nrf24);
        Bloco("Rede_EspNow", Rede_EspNow);
        Bloco("Arquivos_DoProjeto", Arquivos_DoProjeto);
        Bloco("Pecas_CliqueFisico", Pecas_CliqueFisico);
        Bloco("Pecas_TecladoEDisco", Pecas_TecladoEDisco);
        Bloco("Pecas_ChaveEJoystick", Pecas_ChaveEJoystick);
        Bloco("Programa_Configuracoes", Programa_Configuracoes);
        Bloco("Programa_Bibliotecas", Programa_Bibliotecas);
        Bloco("Chip_Proprio", Chip_Proprio);
        Bloco("Programa_JanelaCabeNaTela", Programa_JanelaCabeNaTela);
        Bloco("Pecas_TintaNaCaixa", Pecas_TintaNaCaixa);

        saida.AppendLine();
        saida.AppendLine($"{passou} passaram, {falhou} falharam");
        return saida.ToString();
    }

    /// <summary>
    /// O caminho inteiro do botao de modulo: espetado na protoboard,
    /// ligado por coluna ate o GPIO, apertado e lido pelo sketch.
    ///
    /// O teste de clique so provava que o ponteiro ACERTA o botao. Isso
    /// nao diz nada sobre o aperto CHEGAR na Maquina, que e a queixa.
    /// </summary>
    static void Botoes_LigacaoReal()
    {
        saida.AppendLine("botao de modulo: ligacao real");

        // Monta: protoboard, ESP32 fora dela, botao espetado numa coluna.
        var c = new Circuito();
        var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard, X = 0, Y = 0 };
        c.Pecas.Add(board);
        var defBoard = Catalogo.Obter(board.Tipo);

        var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 700, Y = 0 };
        c.Pecas.Add(placa);

        var defBt = Catalogo.Obter("pushbutton");
        // O pushbutton grande do Wokwi tem 0,2" entre as fileiras de
        // perna e nao atravessa um canal de 0,3": as quatro pernas ficam
        // na metade de cima. Quem atravessa o canal e o de 6 mm girado,
        // que tem 0,3" no outro eixo -- e o que o teste do canal cobre.
        var alvo = defBoard.Pinos.First(x => x.Nome == "C10");
        var pos = Geometria.PosicaoPino(board, defBoard, alvo);
        var pino1 = defBt.Pinos.First(x => x.Nome == "1.l");
        var bt = new PecaNoCircuito
        { Tipo = "pushbutton", X = pos.X - pino1.X, Y = pos.Y - pino1.Y };
        c.Pecas.Add(bt);

        // Em que furo cada perna caiu?
        var rede0 = new Netlist(c);
        foreach (string nome in new[] { "1.l", "2.l", "1.r", "2.r" })
        {
            var furos = rede0.Rede(Netlist.No(bt.Id, nome))
                             .Where(n => n.StartsWith(board.Id + ":"))
                             .Select(n => n[(n.IndexOf(':') + 1)..])
                             .OrderBy(x => x).ToList();
            saida.AppendLine($"        perna {nome} -> furos [{string.Join(",", furos)}]");
        }

        Conferir("botao: as 4 pernas caem em furos",
            new[] { "1.l", "2.l", "1.r", "2.r" }
                .All(n => rede0.Rede(Netlist.No(bt.Id, n))
                              .Any(x => x.StartsWith(board.Id + ":"))),
            "alguma perna ficou solta");

        // Liga GPIO 14 na coluna da perna 1.l e GND na coluna da 2.l.
        c.Fios.Add(new Fio
        {
            DePeca = placa.Id, DePino = "D14",
            ParaPeca = board.Id, ParaPino = "A10", Cor = "#00ff00"
        });
        c.Fios.Add(new Fio
        {
            DePeca = placa.Id, DePino = "GND.1",
            ParaPeca = board.Id, ParaPino = "A12", Cor = "#000000"
        });

        var ed = new EditorCircuito();
        ed.Carregar(c);
        var rede = new Netlist(c);
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maq.PinMode(14, 2);                       // INPUT_PULLUP

        int achado = -1;
        foreach (string n in new[] { "1.l", "1.r", "2.l", "2.r" })
        {
            int g = rede.GpioDe(c, bt.Id, n);
            saida.AppendLine($"        GpioDe({n}) = {g}");
            if (g >= 0 && achado < 0) achado = g;
        }
        Conferir("botao na protoboard: a coluna chega no GPIO 14",
            achado == 14, $"achou {achado}");

        ed.AplicarEntradas(maq, rede);
        Conferir("botao na protoboard: solto le HIGH",
            maq.DigitalRead(14) == 1, $"leu {maq.DigitalRead(14)}");

        ed.BotoesPressionados.Add(bt.Id);
        ed.AplicarEntradas(maq, rede);
        Conferir("botao na protoboard: apertado le LOW",
            maq.DigitalRead(14) == 0, $"leu {maq.DigitalRead(14)}");

        // ---- o tremido do mouse durante o aperto ----
        //
        // Segurar o botao entra no mesmo caminho de arrastar a peca. Se
        // um pixel de tremido tirar a perna do furo, o aperto morre no
        // meio -- e e exatamente isso que o usuario sente.
        double x0 = bt.X, y0 = bt.Y;
        bt.X += 1; bt.Y += 1;
        var redeTremida = new Netlist(c);
        int gTremido = redeTremida.GpioDe(c, bt.Id, "1.l");
        saida.AppendLine($"        depois de 1px de tremido: GpioDe(1.l) = {gTremido}");
        Conferir("botao: 1px de tremido nao desliga a perna", gTremido == 14,
            $"caiu para {gTremido}");

        bt.X = x0 + 4; bt.Y = y0 + 4;
        var rede4 = new Netlist(c);
        saida.AppendLine($"        depois de 4px: GpioDe(1.l) = {rede4.GpioDe(c, bt.Id, "1.l")}");
        bt.X = x0; bt.Y = y0;
    }

    /// <summary>
    /// O aperto inteiro pelo caminho de verdade: Apertar -> Mover ->
    /// Soltar, os mesmos metodos que os handlers de mouse chamam.
    ///
    /// E o teste que faltava. Conferir so o Resolver provava que o
    /// ponteiro ACERTA o botao, e ele acertava: o aperto morria depois,
    /// porque segurar entrava no caminho de arraste.
    /// </summary>
    static void Botoes_ApertoDePontaAPonta()
    {
        saida.AppendLine("aperto de ponta a ponta");

        static (Circuito c, PecaNoCircuito bt, EditorCircuito ed, Maquina maq) Montar()
        {
            var c = new Circuito();
            var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard, X = 0, Y = 0 };
            c.Pecas.Add(board);
            var defBoard = Catalogo.Obter(board.Tipo);
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 700, Y = 0 };
            c.Pecas.Add(placa);

            var defBt = Catalogo.Obter("pushbutton");
            var pos = Geometria.PosicaoPino(
                board, defBoard, defBoard.Pinos.First(x => x.Nome == "C10"));
            var pino1 = defBt.Pinos.First(x => x.Nome == "1.l");
            var bt = new PecaNoCircuito
            { Tipo = "pushbutton", X = pos.X - pino1.X, Y = pos.Y - pino1.Y };
            c.Pecas.Add(bt);

            c.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = "D14",
                ParaPeca = board.Id, ParaPino = "A10", Cor = "#00ff00"
            });

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.PinMode(14, 2);                   // INPUT_PULLUP
            return (c, bt, ed, maq);
        }

        static System.Windows.Point Capa(PecaNoCircuito bt)
        {
            var capa = EditorCircuito.CapasDeBotao[bt.Tipo];
            return new System.Windows.Point(bt.X + capa.X, bt.Y + capa.Y);
        }

        // ---- aperta, segura com a mao tremida, solta ----
        {
            var (c, bt, ed, maq) = Montar();
            double x0 = bt.X, y0 = bt.Y;

            bool consumiu = ed.Apertar(Capa(bt));
            Conferir("aperto: o clique na capa e consumido", consumiu);
            Conferir("aperto: o botao entra na lista de apertados",
                ed.BotoesPressionados.Contains(bt.Id));

            ed.AplicarEntradas(maq, new Netlist(c));
            Conferir("aperto: o sketch le LOW", maq.DigitalRead(14) == 0,
                $"leu {maq.DigitalRead(14)}");

            // Mao tremida: 6 px, bem acima dos 4,5 px do furo.
            ed.Mover(new System.Windows.Point(Capa(bt).X + 6, Capa(bt).Y + 6), true);
            saida.AppendLine($"        depois de 6 px segurando: peca em "
                             + $"({bt.X - x0:0.##};{bt.Y - y0:0.##}) do lugar");
            Conferir("aperto: segurar nao arrasta a peca",
                Math.Abs(bt.X - x0) < 0.01 && Math.Abs(bt.Y - y0) < 0.01,
                $"andou ({bt.X - x0:0.##};{bt.Y - y0:0.##})");

            ed.AplicarEntradas(maq, new Netlist(c));
            Conferir("aperto: continua LOW com a mao tremida",
                maq.DigitalRead(14) == 0, $"leu {maq.DigitalRead(14)}");

            ed.Soltar();
            Conferir("aperto: soltar limpa a lista", ed.BotoesPressionados.Count == 0);
            ed.AplicarEntradas(maq, new Netlist(c));
            Conferir("aperto: depois de soltar volta a HIGH",
                maq.DigitalRead(14) == 1, $"leu {maq.DigitalRead(14)}");
        }

        // ---- o corpo continua arrastando ----
        {
            var (c, bt, ed, maq) = Montar();
            var def = Catalogo.Obter(bt.Tipo);
            double x0 = bt.X, y0 = bt.Y;

            // Canto do corpo, longe da capa e dos pinos.
            var corpo = new System.Windows.Point(bt.X + def.Largura * 0.5, bt.Y + 3);
            Conferir("arraste: o canto do corpo nao aperta",
                ed.Resolver(corpo).Camada == EditorCircuito.Camada.CorpoPeca,
                $"veio {ed.Resolver(corpo).Camada}");

            ed.Apertar(corpo);
            Conferir("arraste: pegar o corpo nao aperta o botao",
                ed.BotoesPressionados.Count == 0);

            // Abaixo do limiar a peca fica parada.
            ed.Mover(new System.Windows.Point(corpo.X + 2, corpo.Y + 2), true);
            Conferir("arraste: 2 px nao movem a peca",
                Math.Abs(bt.X - x0) < 0.01 && Math.Abs(bt.Y - y0) < 0.01,
                $"andou ({bt.X - x0:0.##};{bt.Y - y0:0.##})");

            // Acima dele, arrasta normalmente.
            ed.Mover(new System.Windows.Point(corpo.X + 40, corpo.Y + 40), true);
            saida.AppendLine($"        arraste de 40 px: peca andou "
                             + $"({bt.X - x0:0.##};{bt.Y - y0:0.##})");
            Conferir("arraste: 40 px movem a peca",
                Math.Abs(bt.X - x0) > 10 || Math.Abs(bt.Y - y0) > 10,
                $"andou so ({bt.X - x0:0.##};{bt.Y - y0:0.##})");
            ed.Soltar();
        }

        // ---- apertar um joystick nao vira um pushbutton ----
        {
            var c = new Circuito();
            var j = new PecaNoCircuito { Tipo = "analog-joystick", X = 100, Y = 100 };
            c.Pecas.Add(j);
            var ed = new EditorCircuito();
            ed.Carregar(c);

            var capa = EditorCircuito.CapasDeBotao["analog-joystick"];
            ed.Apertar(new System.Windows.Point(j.X + capa.X, j.Y + capa.Y));
            Conferir("joystick: o knob aperta", ed.BotoesPressionados.Contains(j.Id));
            Conferir("joystick: nao existe variante apertada para trocar o desenho",
                !Pecas.Existe("analog-joystick-pressionado"));
            ed.Soltar();
        }
    }

    /// <summary>
    /// O desenho de cada peca precisa ocupar a viewport inteira, nao so
    /// o retangulo do que foi pintado.
    ///
    /// O LED tem viewBox "-10 -5 35,456 39,618": folga reservada para o
    /// halo. O conteudo dele mede 22,6x34,1 numa caixa de 40x50, e o
    /// Stretch.Uniform reencaixava esse conteudo na caixa -- esticava o
    /// desenho em 1,46x e recentralizava. As pernas iam parar longe de
    /// onde PosicaoPino diz que os pinos estao, e o LED nao encostava
    /// nos furos da protoboard.
    ///
    /// Vale para o catalogo inteiro porque o defeito e do carregador, e
    /// qualquer peca com folga no viewBox cai nele.
    /// </summary>
    static void Svgs_EnquadradosNaCaixa()
    {
        saida.AppendLine("svg enquadrado na caixa");

        int conferidas = 0, tortas = 0;
        foreach (var def in Catalogo.Todas)
        {
            // Protoboard e resistor sao desenhados a mao, nao vem de SVG.
            if (Catalogo.EhProtoboard(def.Id) || def.Id == "resistor") continue;

            string nome = def.Id == "led" ? "led-vermelho-off" : def.Id;
            if (!Pecas.Existe(nome)) continue;
            if (Pecas.Obter(nome) is not System.Windows.Media.DrawingImage img) continue;

            conferidas++;
            var b = img.Drawing.Bounds;

            // A origem tem que ser o canto da viewport. Se for outra, a
            // folga do viewBox se perdeu.
            bool naOrigem = Math.Abs(b.X) < 0.6 && Math.Abs(b.Y) < 0.6;

            // E a proporcao tem que bater com a caixa do catalogo, senao
            // o Uniform ainda letterboxa e desloca os pinos. A sobra
            // declarada (o USB do Uno, igual dos dois lados) nao conta.
            var sobra = Pecas.SobraDe(nome);
            double propDesenho = (b.Width - 2 * sobra.X) / (b.Height - 2 * sobra.Y);
            double propCaixa = def.Largura / (double)def.Altura;
            bool proporcao = Math.Abs(propDesenho - propCaixa) < 0.03;

            if (naOrigem && proporcao) continue;
            tortas++;
            saida.AppendLine($"        {def.Id,-22} bounds {b.X:0.##};{b.Y:0.##} "
                             + $"{b.Width:0.##}x{b.Height:0.##}  caixa "
                             + $"{def.Largura}x{def.Altura}");
        }

        saida.AppendLine($"        {conferidas} pecas com SVG conferidas");
        Conferir("svg: o desenho comeca no canto da caixa e mantem a proporcao",
            tortas == 0, $"{tortas} fora de esquadro");
        Conferir("svg: conferiu o catalogo inteiro", conferidas >= 40,
            $"so {conferidas}");
    }

    /// <summary>
    /// Nenhum SVG pode ter font-size relativo.
    ///
    /// "200%" so faz sentido com alguem de quem herdar, e o <svg> sai do
    /// shadow root sem esse alguem: o renderizador cai no padrao de 16px.
    /// No arduino-nano eram duas setas de TX/RX que viravam glifos de 32
    /// unidades numa peca de 17,8 de altura. Empurravam o limite do
    /// desenho 202 px para baixo e a placa inteira saia da viewport --
    /// na tela aparecia so um par de setas brancas gigantes.
    ///
    /// O extrator agora resolve isso na hora de gerar; esta guarda existe
    /// para o caso de voltar.
    /// </summary>
    static void Svgs_SemFonteRelativa()
    {
        saida.AppendLine("svg sem fonte relativa");

        var achados = new List<string>();
        int vistos = 0;
        foreach (var (nome, texto) in Pecas.TodosOsSvgs())
        {
            vistos++;
            var m = System.Text.RegularExpressions.Regex.Matches(
                texto, @"font-size\s*[:=]\s*""?\s*-?[\d.]+\s*(%|em|rem)");
            if (m.Count > 0) achados.Add($"{nome} ({m.Count}x {m[0].Value.Trim()})");
        }

        saida.AppendLine($"        {vistos} svgs conferidos");
        Conferir("svg: nenhum font-size relativo", achados.Count == 0,
            string.Join(", ", achados));
        Conferir("svg: leu os arquivos embutidos", vistos >= 40, $"so {vistos}");
    }

    /// <summary>
    /// O canal central mede 0,3" de E a F, nao 0,2".
    ///
    /// E a medida que faz um encapsulamento DIP montar a cavalo nele, e
    /// da area central 11 passos de A a J. Com 10, a ESP32 -- que tem
    /// exatamente 1" entre as fileiras -- cobria as dez linhas e nao
    /// sobrava nenhuma para ligar fio; na bancada sobra uma.
    /// </summary>
    static void Protoboard_CanalDeTresDecimos()
    {
        saida.AppendLine("canal da protoboard");

        var def = Catalogo.Obter(Catalogo.IdProtoboard830);
        var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard830, X = 0, Y = 0 };

        double Y(string furo) =>
            Geometria.PosicaoPino(board, def, def.Pinos.First(x => x.Nome == furo)).Y;

        double canal = (Y("F10") - Y("E10")) / Catalogo.Passo;
        double central = (Y("J10") - Y("A10")) / Catalogo.Passo;
        saida.AppendLine($"        E->F = {canal:0.##} passos ({canal * 2.54:0.#} mm), "
                         + $"A->J = {central:0.##} passos");
        Conferir("canal: 0,3 polegada de E a F", Math.Abs(canal - 3) < 0.01,
            $"{canal:0.##} passos");
        Conferir("area central: 11 passos de A a J", Math.Abs(central - 11) < 0.01,
            $"{central:0.##} passos");

        // A ESP32 tem 1" entre as fileiras: tem que sobrar uma linha.
        var esp = Catalogo.Obter("esp32-devkit-v1");
        double fileiras = (esp.Pinos.Max(p => p.X) - esp.Pinos.Min(p => p.X)) / Catalogo.Passo;
        saida.AppendLine($"        ESP32: {fileiras:0.##} passos entre as fileiras "
                         + $"({fileiras * 2.54:0.#} mm)");
        Conferir("ESP32: cabe deixando uma linha livre", fileiras < central - 0.5,
            $"{fileiras:0.##} passos numa area de {central:0.##}");

        // E um encapsulamento de 0,3" tem que montar a cavalo no canal.
        var rele = Catalogo.Obter("ks2e-m-dc5");
        var c = new Circuito();
        c.Pecas.Add(board);
        var cima = rele.Pinos.OrderBy(p => p.Y).First();
        var alvo = def.Pinos.First(x => x.Nome == "E20");
        var pos = Geometria.PosicaoPino(board, def, alvo);
        var peca = new PecaNoCircuito
        { Tipo = "ks2e-m-dc5", X = pos.X - cima.X, Y = pos.Y - cima.Y };
        c.Pecas.Add(peca);

        var rede = new Netlist(c);
        var furos = rede.Rede(Netlist.No(peca.Id, rele.Pinos.OrderByDescending(p => p.Y).First().Nome))
                        .Where(n => n.StartsWith(board.Id + ":"))
                        .Select(n => n[(n.IndexOf(':') + 1)..]).ToList();
        saida.AppendLine($"        rele 0,3\": perna de baixo -> [{string.Join(",", furos)}]");
        Conferir("canal: encapsulamento de 0,3\" monta a cavalo",
            furos.Any(f => "FGHIJ".Contains(f[0])),
            furos.Count == 0 ? "perna solta" : string.Join(",", furos));
    }

    /// <summary>
    /// Todo pino tem que cair numa grade de 0,1", dentro do raio do furo.
    ///
    /// Passo errado nao aparece numa peca de tres pinos: aparece na ponta
    /// de uma fileira de quinze, onde o erro ja acumulou. E o que decide
    /// se a placa espeta na protoboard ou fica pendurada por um pino so.
    ///
    /// O ajuste e por fileira e so move a origem -- cada pino vai para a
    /// casa mais proxima. Assim um vao proposital, como o de 0,16" entre
    /// os pinos 7 e 8 do Uno, e medido como o desvio que ele realmente e,
    /// em vez de desalinhar a fileira inteira.
    /// </summary>
    static void Pecas_NaGradeDeUmDecimo()
    {
        saida.AppendLine("pinos na grade de 0,1 polegada");

        double P = Catalogo.Passo;
        var piores = new List<(string peca, double desvio)>();

        // Os modulos ESP8266 de ilha castelada (2,0 mm; 1,5 mm no ESP-13), de
        // ilha por baixo e o de 1,27 mm (ESP-11) nao sao de 0,1": na bancada
        // vao soldados numa placa adaptadora. O passo deles e o de verdade,
        // conferido em Esp8266_Redesenhadas.
        string[] passoMetrico = ["esp-03", "esp-04", "esp-06", "esp-07", "esp-08", "esp-09", "esp-10", "esp-11",
                                 "esp-12", "esp-12e", "esp-12f", "esp-13", "esp-14"];
        foreach (var def in Catalogo.Todas)
        {
            if (Catalogo.EhProtoboard(def.Id) || def.Pinos.Count < 3 || passoMetrico.Contains(def.Id)) continue;

            double pior = 0;
            foreach (bool porX in new[] { true, false })
            {
                var fileiras = def.Pinos
                    .GroupBy(p => Math.Round(porX ? p.X : p.Y, 1))
                    .Where(g => g.Count() >= 3);

                foreach (var fileira in fileiras)
                {
                    var v = fileira.Select(p => porX ? p.Y : p.X).OrderBy(x => x).ToList();
                    double baseP = v[0];
                    var casas = v.Select(x => Math.Round((x - baseP) / P)).ToList();
                    double desloc = v.Zip(casas, (x, c) => x - baseP - c * P).Average();
                    double d = v.Zip(casas, (x, c) => Math.Abs(x - baseP - desloc - c * P)).Max();
                    if (d > pior) pior = d;
                }
            }
            if (pior > 0.01) piores.Add((def.Id, pior));
        }

        foreach (var (peca, desvio) in piores.OrderByDescending(x => x.desvio).Take(4))
            saida.AppendLine($"        maior desvio: {peca,-22} {desvio:0.00} px "
                             + $"({desvio * 25.4 / 96:0.00} mm)");

        var fora = piores.Where(x => x.desvio >= Netlist.RaioDoFuro).ToList();
        saida.AppendLine($"        {piores.Count} pecas medidas, "
                         + $"limite {Netlist.RaioDoFuro} px "
                         + $"({Netlist.RaioDoFuro * 25.4 / 96:0.00} mm)");
        Conferir("pinos: toda peca espeta na protoboard", fora.Count == 0,
            string.Join(", ", fora.Select(x => $"{x.peca} {x.desvio:0.0}px")));

        // As fileiras das placas dev tem que estar a um numero inteiro de
        // passos: e o que decide se a placa monta a cavalo no canal.
        foreach (var (id, esperado) in new[]
                 { ("esp32-devkit-v1", 10.0), ("arduino-nano", 6.0),
                   ("nano-rp2040-connect", 6.0) })
        {
            var def = Catalogo.Obter(id);
            if (def is null) continue;
            var xs = def.Pinos.Select(p => p.X).ToList();
            var ys = def.Pinos.Select(p => p.Y).ToList();
            bool emX = xs.Distinct().Count() < ys.Distinct().Count();
            var v = emX ? xs : ys;
            double sep = (v.Max() - v.Min()) / P;
            saida.AppendLine($"        {id,-22} {sep:0.00} passos entre as fileiras "
                             + $"({sep * 2.54:0.0} mm)");
            Conferir($"{id}: fileiras a {esperado:0} passos",
                Math.Abs(sep - esperado) < 0.15, $"{sep:0.00} passos");
        }

        // Uno e Mega levam o mesmo shield: os dois headers longos tem que
        // ficar a mesma distancia um do outro nas duas placas.
        static double VaoDosHeaders(string id)
        {
            var def = Catalogo.Obter(id);
            var linhas = def.Pinos.GroupBy(p => Math.Round(p.Y, 1))
                                  .Where(g => g.Count() >= 4)
                                  .Select(g => g.Key).OrderBy(y => y).ToList();
            return linhas.Count < 2 ? 0 : linhas[^1] - linhas[0];
        }

        double vaoUno = VaoDosHeaders("arduino-uno");
        double vaoMega = VaoDosHeaders("arduino-mega");
        saida.AppendLine($"        headers: Uno {vaoUno / P:0.00} passos, "
                         + $"Mega {vaoMega / P:0.00} passos");
        Conferir("Uno e Mega: mesmo vao entre headers (shield)",
            Math.Abs(vaoUno - vaoMega) < 1.0, $"{vaoUno:0.0} vs {vaoMega:0.0} px");
    }

    /// <summary>
    /// A mini de 170: 17 colunas x 10 linhas, sem trilho de alimentacao.
    /// E a colorida pequena de bancada -- 45,7 x 38,1 mm.
    /// </summary>
    static void Protoboard170_MiniSemTrilho()
    {
        saida.AppendLine("protoboard de 170 pontos");

        var def = Catalogo.Obter(Catalogo.IdProtoboard170);
        Conferir("mini: existe no catalogo", def is not null);
        if (def is null) return;

        saida.AppendLine($"        {def.Pinos.Count} furos, "
                         + $"{def.Largura * 25.4 / 96:0.0} x {def.Altura * 25.4 / 96:0.0} mm");
        Conferir("mini: 170 furos", def.Pinos.Count == 170, $"tem {def.Pinos.Count}");
        // Trilho e "T+n"/"T-n"/"B+n"/"B-n"; olhar so a primeira letra
        // pegaria a linha B da area central, que e furo comum.
        Conferir("mini: nenhum furo de trilho",
            !def.Pinos.Any(p => p.Nome.Length > 1 && p.Nome[0] is 'T' or 'B'
                                && p.Nome[1] is '+' or '-'),
            "achou furo de trilho");
        Conferir("mini: e reconhecida como protoboard",
            Catalogo.EhProtoboard(Catalogo.IdProtoboard170));
        Conferir("mini: cabe na mao (menor que a de 400)",
            def.Largura < Catalogo.Obter(Catalogo.IdProtoboard).Largura
            && def.Altura < Catalogo.Obter(Catalogo.IdProtoboard).Altura);

        // O canal continua com 0,3": e a mesma area central das grandes.
        double canal = (def.Pinos.First(p => p.Nome == "F5").Y
                        - def.Pinos.First(p => p.Nome == "E5").Y) / Catalogo.Passo;
        Conferir("mini: canal de 0,3 polegada", Math.Abs(canal - 3) < 0.01,
            $"{canal:0.##} passos");

        // Coluna conduz; e sem trilho nada liga uma ponta na outra.
        var c = new Circuito();
        var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard170, X = 0, Y = 0 };
        c.Pecas.Add(board);
        var rede = new Netlist(c);
        Conferir("mini: A3 e E3 sao o mesmo no",
            rede.MesmoNo(Netlist.No(board.Id, "A3"), Netlist.No(board.Id, "E3")));
        Conferir("mini: A3 e F3 ficam separados pelo canal",
            !rede.MesmoNo(Netlist.No(board.Id, "A3"), Netlist.No(board.Id, "F3")));
        Conferir("mini: A3 e A4 sao colunas diferentes",
            !rede.MesmoNo(Netlist.No(board.Id, "A3"), Netlist.No(board.Id, "A4")));

        // A ESP32 nao cabe nela: e o que se espera de uma mini.
        var esp = Catalogo.Obter("esp32-devkit-v1");
        double fileiras = (esp.Pinos.Max(p => p.X) - esp.Pinos.Min(p => p.X)) / Catalogo.Passo;
        double central = (def.Pinos.First(p => p.Nome == "J5").Y
                          - def.Pinos.First(p => p.Nome == "A5").Y) / Catalogo.Passo;
        saida.AppendLine($"        area central {central:0.##} passos, "
                         + $"ESP32 pede {fileiras:0.##}");
        Conferir("mini: area central de 11 passos", Math.Abs(central - 11) < 0.01,
            $"{central:0.##}");
    }

    /// <summary>
    /// A onda do buzzer, medida em vez de escutada.
    ///
    /// A versao antiga tinha o periodo em divisao inteira de amostras:
    /// a 5 kHz sobravam 8 amostras por ciclo e saia 5512 Hz, 10% agudo.
    /// E trocar de tom reiniciava o tocador, o que num sketch que varre a
    /// frequencia -- som de motor, sirene -- virava estalo em cima de
    /// estalo. Os dois defeitos aparecem numa conta simples.
    /// </summary>
    static void Buzzer_OndaDeVerdade()
    {
        saida.AppendLine("onda do buzzer");

        int SR = GeradorDeOnda.Taxa;

        // Frequencia medida entre o PRIMEIRO e o ULTIMO cruzamento de
        // zero subindo. Contar cruzamentos numa janela fixa daria um
        // numero inteiro de ciclos e quantizaria a medida -- a 100 Hz
        // isso sozinho ja acusava 0,7% de erro que nao existia.
        static double Medir(short[] v, int taxa)
        {
            int primeiro = -1, ultimo = -1, subidas = 0;
            for (int i = 1; i < v.Length; i++)
            {
                if (v[i - 1] > 0 || v[i] <= 0) continue;
                if (primeiro < 0) primeiro = i;
                ultimo = i;
                subidas++;
            }
            return subidas < 2 ? 0 : (subidas - 1) * (double)taxa / (ultimo - primeiro);
        }

        double pior = 0;
        foreach (int hz in new[] { 100, 220, 440, 1000, 1500, 3000, 5000 })
        {
            var g = new GeradorDeOnda { Hz = hz };
            var buf = new short[SR];              // 1 segundo
            g.Preencher(buf);

            // A rampa de entrada mexe so nos primeiros milissegundos.
            double medida = Medir(buf[1000..], SR);
            double erro = Math.Abs(medida / hz - 1);
            if (erro > pior) pior = erro;
            saida.AppendLine($"        {hz,5} Hz pedido -> {medida,7:0.0} Hz medido "
                             + $"({erro:+0.00%;-0.00%;0.00%})");
        }
        Conferir("buzzer: afinacao dentro de 0,5%", pior < 0.005, $"errou {pior:0.0%}");

        // ---- continuidade ao trocar de tom ----
        {
            var g = new GeradorDeOnda { Hz = 200 };
            var a = new short[GeradorDeOnda.Taxa / 50];
            g.Preencher(a);                        // ja passou da rampa
            g.Preencher(a);

            static int MaiorSalto(short[] v)
            {
                int m = 0;
                for (int i = 1; i < v.Length; i++)
                    m = Math.Max(m, Math.Abs(v[i] - v[i - 1]));
                return m;
            }

            int dentro = MaiorSalto(a);
            short ultima = a[^1];

            g.Hz = 400;                            // o sketch mudou o tom
            var b = new short[GeradorDeOnda.Taxa / 50];
            g.Preencher(b);
            int naEmenda = Math.Abs(b[0] - ultima);

            saida.AppendLine($"        maior salto dentro do bloco {dentro}, "
                             + $"na emenda 200->400 Hz {naEmenda}");
            Conferir("buzzer: trocar de tom nao estala",
                naEmenda <= dentro * 2 + 1, $"emenda {naEmenda} contra {dentro}");
        }

        // ---- rampa: comeca e termina sem estalo ----
        {
            var g = new GeradorDeOnda { Hz = 440 };
            var buf = new short[GeradorDeOnda.Taxa / 100];
            g.Preencher(buf);
            saida.AppendLine($"        primeira amostra {buf[0]}, "
                             + $"pico do bloco {buf.Max(x => Math.Abs((int)x))}");
            Conferir("buzzer: entra em rampa, sem estalo no comeco",
                Math.Abs((int)buf[0]) < 400, $"comecou em {buf[0]}");

            g.Hz = 0;                              // noTone
            var fim = new short[GeradorDeOnda.Taxa / 100];
            g.Preencher(fim);
            Conferir("buzzer: sai em rampa e chega ao silencio",
                fim[^1] == 0 && Math.Abs((int)fim[0]) > 0, $"terminou em {fim[^1]}");
        }

        // ---- timbre: quadrada, nao senoide ----
        {
            // Uma senoide tem valor medio absoluto de 2/pi do pico (0,64).
            // Uma quadrada chega perto de 1. O buzzer tem que soar como
            // piezo, nao como flauta.
            var g = new GeradorDeOnda { Hz = 440 };
            var buf = new short[GeradorDeOnda.Taxa / 10];
            g.Preencher(buf);
            var corte = buf[2000..];
            double pico = corte.Max(x => Math.Abs((int)x));
            double medio = corte.Average(x => Math.Abs((int)x));
            saida.AppendLine($"        medio/pico = {medio / pico:0.00} "
                             + "(senoide 0,64 · quadrada 1,00)");
            Conferir("buzzer: timbre de piezo, nao de senoide",
                medio / pico > 0.72, $"{medio / pico:0.00}");
        }
    }

    /// <summary>
    /// Fios entram no laco de selecao, e cada um guarda o seu tracado.
    ///
    /// Antes o laco so pegava pecas: para apagar meia duzia de fios era
    /// um clique de cada vez. E o tracado era uma chave global -- ou tudo
    /// solto, ou tudo em L, sem misturar.
    /// </summary>
    static void Fios_LacoEEstilos()
    {
        saida.AppendLine("laco e tracado dos fios");

        // ---- os tres tracados sao mesmo diferentes ----
        {
            // Pontas com Y diferente: com o mesmo Y o degrau degenera
            // numa reta e o teste nao distinguiria nada.
            var a = new System.Windows.Point(0, 0);
            var b = new System.Windows.Point(100, 60);

            var solto = EditorCircuito.NaCurva(Fio.EstiloSolto, a, b, 0.5);
            var reto = EditorCircuito.NaCurva(Fio.EstiloReto, a, b, 0.5);
            // No meio exato o degrau passa pelo mesmo ponto do reto; a
            // diferenca esta nas pernas, entao olha a um quarto.
            var degrau = EditorCircuito.NaCurva(Fio.EstiloDegrau, a, b, 0.25);
            var retoEm25 = EditorCircuito.NaCurva(Fio.EstiloReto, a, b, 0.25);

            saida.AppendLine($"        no meio: solto y={solto.Y:0.0}, "
                             + $"reto ({reto.X:0.0};{reto.Y:0.0}); a 1/4 do caminho "
                             + $"reto x={retoEm25.X:0.0} e degrau x={degrau.X:0.0}");

            Conferir("fio reto: passa pelo meio exato",
                Math.Abs(reto.X - 50) < 0.01 && Math.Abs(reto.Y - 30) < 0.01,
                $"({reto.X:0.0};{reto.Y:0.0})");
            Conferir("fio solto: faz barriga para baixo", solto.Y > reto.Y + 15,
                $"solto y={solto.Y:0.0}, reto y={reto.Y:0.0}");
            Conferir("fio degrau: desce antes de atravessar",
                Math.Abs(degrau.X - retoEm25.X) > 5,
                $"degrau x={degrau.X:0.0}, reto x={retoEm25.X:0.0}");

            // Ponta a ponta os tres coincidem: so o meio do caminho muda.
            foreach (string estilo in new[]
                     { Fio.EstiloSolto, Fio.EstiloReto, Fio.EstiloDegrau })
            {
                var p0 = EditorCircuito.NaCurva(estilo, a, b, 0);
                var p1 = EditorCircuito.NaCurva(estilo, a, b, 1);
                Conferir($"fio {estilo}: comeca e termina nos pinos",
                    Math.Abs(p0.X - a.X) < 0.5 && Math.Abs(p1.X - b.X) < 0.5,
                    $"{p0} .. {p1}");
            }
        }

        // ---- o laco pega fios ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            c.Pecas.Add(placa);

            // Tres LEDs longe da placa: os fios atravessam o vazio.
            for (int i = 0; i < 3; i++)
            {
                var led = new PecaNoCircuito { Tipo = "led", X = 700, Y = 40 + i * 90 };
                c.Pecas.Add(led);
                c.Fios.Add(new Fio
                {
                    DePeca = placa.Id, DePino = new[] { "D14", "D15", "D5" }[i],
                    ParaPeca = led.Id, ParaPino = "A", Cor = "#00A651"
                });
            }

            var ed = new EditorCircuito();
            ed.Carregar(c);
            Conferir("laco: comeca sem fio selecionado", ed.FiosSelecionados.Count == 0);

            // Retangulo no vazio entre a placa e os LEDs, onde so ha fio.
            ed.Apertar(new System.Windows.Point(330, 0));
            ed.Mover(new System.Windows.Point(430, 320), true);
            ed.Soltar();

            saida.AppendLine($"        laco no vazio pegou {ed.FiosSelecionados.Count} fios "
                             + $"e {ed.QuantidadeSelecionada} pecas");
            Conferir("laco: pega os fios que passam dentro dele",
                ed.FiosSelecionados.Count == 3, $"pegou {ed.FiosSelecionados.Count}");
            Conferir("laco: nao pega peca nenhuma nessa faixa",
                ed.QuantidadeSelecionada == 0, $"pegou {ed.QuantidadeSelecionada}");

            // Cor e tracado valem para todos de uma vez.
            ed.DefinirEstiloDosFios(Fio.EstiloReto);
            Conferir("laco: o tracado vale para todos os selecionados",
                c.Fios.All(f => f.Estilo == Fio.EstiloReto),
                string.Join(",", c.Fios.Select(f => f.Estilo)));

            ed.DefinirCorDoFio("#D6202A");
            Conferir("laco: a cor vale para todos os selecionados",
                c.Fios.All(f => f.Cor == "#D6202A"));

            // Del apaga os tres de uma vez.
            ed.RemoverSelecionada();
            Conferir("laco: Del apaga todos os fios do laco",
                c.Fios.Count == 0, $"sobraram {c.Fios.Count}");
            Conferir("laco: as pecas ficam onde estavam", c.Pecas.Count == 4,
                $"sobraram {c.Pecas.Count}");
        }

        // ---- laco vazio de verdade nao seleciona nada ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 });
            var ed = new EditorCircuito();
            ed.Carregar(c);

            ed.Apertar(new System.Windows.Point(600, 600));
            ed.Mover(new System.Windows.Point(700, 700), true);
            ed.Soltar();
            Conferir("laco: area sem nada nao seleciona",
                ed.FiosSelecionados.Count == 0 && ed.QuantidadeSelecionada == 0);
        }

        // ---- fio novo nasce com o tracado padrao ----
        {
            var c = new Circuito();
            var ed = new EditorCircuito();
            ed.Carregar(c);
            ed.EstiloPadrao = Fio.EstiloReto;
            Conferir("padrao: o editor guarda o tracado dos proximos fios",
                ed.EstiloPadrao == Fio.EstiloReto);
        }
    }

    /// <summary>
    /// Custo de um quadro da simulacao num circuito cheio.
    ///
    /// AplicarEntradas e RefletirEstado rodam 25 vezes por segundo, e os
    /// dois consultam a rede pino a pino. Num circuito grande isso vira o
    /// gargalo -- e o alvo do projeto sao os PCs fracos do laboratorio,
    /// entao o numero importa mais aqui do que em qualquer outro lugar.
    /// </summary>
    static void Desempenho_QuadroACuadro()
    {
        saida.AppendLine("desempenho: um quadro da simulacao");

        var c = new Circuito();
        var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard830, X = 0, Y = 400 };
        c.Pecas.Add(board);
        var defBoard = Catalogo.Obter(board.Tipo);
        var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        c.Pecas.Add(placa);

        // Um circuito de aula cheio: LEDs, resistores, sensores e telas.
        string[] tipos =
        [
            "led", "led", "led", "resistor", "resistor", "resistor",
            "pushbutton", "potentiometer", "buzzer", "hc-sr04", "dht22",
            "servo", "lcd1602", "photoresistor-sensor", "rgb-led",
        ];
        string[] gpios = ["D14", "D13", "D12", "D27", "D26", "D25", "D33",
                          "D32", "D5", "D18", "D19", "D21", "D22", "D23", "D4"];

        for (int i = 0; i < tipos.Length; i++)
        {
            var def = Catalogo.Obter(tipos[i]);
            var peca = new PecaNoCircuito
            { Tipo = tipos[i], X = 40 + i * 60, Y = 430 };
            c.Pecas.Add(peca);
            c.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = gpios[i],
                ParaPeca = peca.Id, ParaPino = def.Pinos[0].Nome, Cor = "#00A651"
            });
        }

        var ed = new EditorCircuito();
        ed.Carregar(c);
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        var rede = new Netlist(c);

        int nos = defBoard.Pinos.Count + c.Pecas.Sum(x => Catalogo.Obter(x.Tipo)?.Pinos.Count ?? 0);
        saida.AppendLine($"        {c.Pecas.Count} pecas, {c.Fios.Count} fios, "
                         + $"~{nos} nos na rede");

        // Aquece: a primeira passada paga JIT e caches preguicosos.
        for (int k = 0; k < 5; k++) { ed.AplicarEntradas(maq, rede); ed.RefletirEstado(maq, rede); }

        var relogio = System.Diagnostics.Stopwatch.StartNew();
        const int quadros = 50;
        for (int k = 0; k < quadros; k++)
        {
            ed.AplicarEntradas(maq, rede);
            ed.RefletirEstado(maq, rede);
        }
        relogio.Stop();

        double porQuadro = relogio.Elapsed.TotalMilliseconds / quadros;
        saida.AppendLine($"        {porQuadro:0.00} ms por quadro "
                         + $"({porQuadro * 25:0.0}% de um nucleo a 25 fps)");

        // O relogio de tela roda a cada 40 ms. Antes de indexar o
        // netlist isto custava 3,7 ms; depois, 0,01. O limite de 1 ms da
        // folga de sobra para uma maquina lenta e ainda acusa se a
        // varredura por chamada voltar.
        Conferir("desempenho: o quadro custa menos de 1 ms",
            porQuadro < 1.0, $"{porQuadro:0.00} ms");
    }

    /// <summary>
    /// Todo estado que a simulacao le tem que ter como ser mudado.
    ///
    /// Ja aconteceu duas vezes de o componente estar simulado e o
    /// controle nao existir: a chave DIP lia os bits sem ter as chaves na
    /// tela, e o eixo vertical do joystick tinha DefinirGiro2 no editor
    /// sem um unico chamador. Nos dois casos o pino ficava parado para
    /// sempre e nada acusava.
    /// </summary>
    static void Controles_ChegamNaSimulacao()
    {
        saida.AppendLine("controle -> simulacao");

        // ---- joystick: os DOIS eixos ----
        {
            var (c, _, j, ed, maq, rede) = Bancada("analog-joystick", "HORZ", 34);
            c.Fios.Add(new Fio
            {
                DePeca = c.Pecas[0].Id, DePino = "D35",
                ParaPeca = j.Id, ParaPino = "VERT", Cor = "#000000"
            });
            rede = new Netlist(c);

            ed.DefinirGiro(j, 0.25);
            ed.DefinirGiro2(j, 0.75);
            ed.AplicarEntradas(maq, rede);

            int x = maq.AnalogRead(34), y = maq.AnalogRead(35);
            saida.AppendLine($"        joystick X={x}, Y={y} (de {maq.Perfil.MaxAdc})");
            Conferir("joystick: o eixo X chega no ADC",
                Math.Abs(x - maq.Perfil.MaxAdc * 0.25) <= 2, $"leu {x}");
            Conferir("joystick: o eixo Y chega no ADC",
                Math.Abs(y - maq.Perfil.MaxAdc * 0.75) <= 2, $"leu {y}");
            Conferir("joystick: os dois eixos sao independentes", x != y);
        }

        // ---- chave DIP: cada uma das oito ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            c.Pecas.Add(placa);
            var dip = new PecaNoCircuito { Tipo = "dip-switch-8", X = 400, Y = 0 };
            c.Pecas.Add(dip);
            c.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = "D14",
                ParaPeca = dip.Id, ParaPino = "1a", Cor = "#000000"
            });

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            var rede = new Netlist(c);

            ed.AplicarEntradas(maq, rede);
            int solta = maq.DigitalRead(14);
            ed.AlternarChaveDip(dip, 0, true);
            ed.AplicarEntradas(maq, rede);
            int ligada = maq.DigitalRead(14);

            saida.AppendLine($"        chave DIP 1: solta le {solta}, ligada le {ligada}");
            Conferir("chave DIP: acionar muda o nivel do pino", solta != ligada,
                $"os dois leram {solta}");
        }

        // ---- encoder: quadratura de verdade ----
        {
            var (c, _, ky, ed, maq, rede) = Bancada("ky-040", "CLK");
            c.Fios.Add(new Fio
            {
                DePeca = c.Pecas[0].Id, DePino = "D27",
                ParaPeca = ky.Id, ParaPino = "DT", Cor = "#000000"
            });
            rede = new Netlist(c);

            // Um passo do encoder e 1/40 de curso.
            var seq = new List<(int clk, int dt)>();
            for (int passo = 0; passo <= 5; passo++)
            {
                ed.DefinirGiro(ky, passo / 40.0);
                ed.AplicarEntradas(maq, rede);
                seq.Add((maq.DigitalRead(14), maq.DigitalRead(27)));
            }
            saida.AppendLine("        CLK/DT por passo: "
                             + string.Join(" ", seq.Select(x => $"{x.clk}{x.dt}")));

            Conferir("encoder: girar mexe nos sinais",
                seq.Distinct().Count() >= 3, "os sinais mal mudaram");

            // A marca de uma quadratura de verdade: nunca os dois de uma
            // vez. E na borda de um que o sketch le o outro.
            int juntos = 0;
            for (int i = 1; i < seq.Count; i++)
                if (seq[i].clk != seq[i - 1].clk && seq[i].dt != seq[i - 1].dt) juntos++;
            Conferir("encoder: um sinal por passo (quadratura valida)",
                juntos == 0, $"{juntos} passos mudaram os dois");
        }
    }

    /// <summary>
    /// Copiar e colar tem que levar as ligacoes e o estado das pecas.
    ///
    /// Levava so as pecas: duplicar um trecho pronto obrigava a religar
    /// tudo a mao. E a copia listava os campos a mao, entao Giro, Giro2 e
    /// Bits ficavam para tras -- colar um potenciometro perdia a posicao
    /// e colar uma chave DIP perdia as oito chaves.
    /// </summary>
    static void Copiar_LevaOsFios()
    {
        saida.AppendLine("copiar e colar");

        var c = new Circuito();
        var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0, Cor = "azul" };
        var res = new PecaNoCircuito { Tipo = "resistor", X = 400, Y = 120, Ohms = 470 };
        var pot = new PecaNoCircuito
        { Tipo = "potentiometer", X = 400, Y = 240, Giro = 0.75 };
        var dip = new PecaNoCircuito { Tipo = "dip-switch-8", X = 400, Y = 360, Bits = 37 };
        c.Pecas.AddRange([placa, led, res, pot, dip]);

        // Um fio interno (LED-resistor) e um que sai para a placa.
        c.Fios.Add(new Fio
        {
            DePeca = led.Id, DePino = "C", ParaPeca = res.Id, ParaPino = "1",
            Cor = "#F2C300", Estilo = Fio.EstiloReto
        });
        c.Fios.Add(new Fio
        {
            DePeca = placa.Id, DePino = "D14", ParaPeca = led.Id, ParaPino = "A",
            Cor = "#00A651"
        });

        var ed = new EditorCircuito();
        ed.Carregar(c);

        ed.SelecionarPorIndice(1);                    // LED
        ed.SelecionarPorIndice(2, true);              // resistor
        ed.SelecionarPorIndice(3, true);              // potenciometro
        ed.SelecionarPorIndice(4, true);              // chave DIP
        ed.Copiar();
        ed.Colar();

        saida.AppendLine($"        depois de colar: {c.Pecas.Count} pecas, {c.Fios.Count} fios");
        Conferir("colar: as quatro pecas vieram", c.Pecas.Count == 9,
            $"ficaram {c.Pecas.Count}");
        Conferir("colar: o fio entre as copiadas veio junto", c.Fios.Count == 3,
            $"ficaram {c.Fios.Count}");

        var novoFio = c.Fios[^1];
        var novoLed = c.Pecas[5];
        var novoRes = c.Pecas[6];
        Conferir("colar: o fio novo liga as pecas NOVAS, nao as velhas",
            novoFio.DePeca == novoLed.Id && novoFio.ParaPeca == novoRes.Id,
            $"{novoFio.DePeca} -> {novoFio.ParaPeca}");
        Conferir("colar: o fio novo mantem cor e traçado",
            novoFio.Cor == "#F2C300" && novoFio.Estilo == Fio.EstiloReto,
            $"{novoFio.Cor} / {novoFio.Estilo}");
        Conferir("colar: o fio que saia da selecao NAO foi duplicado",
            c.Fios.Count(f => f.DePeca == placa.Id) == 1);

        // O estado de cada peca tem que vir junto.
        Conferir("colar: o LED mantem a cor", novoLed.Cor == "azul", novoLed.Cor);
        Conferir("colar: o resistor mantem os ohms", Math.Abs(novoRes.Ohms - 470) < 0.01,
            $"{novoRes.Ohms}");
        Conferir("colar: o potenciometro mantem a posicao",
            Math.Abs(c.Pecas[7].Giro - 0.75) < 1e-9, $"{c.Pecas[7].Giro}");
        Conferir("colar: a chave DIP mantem as oito chaves",
            c.Pecas[8].Bits == 37, $"{c.Pecas[8].Bits}");

        // A peca colada e outra: mexer nela nao pode mexer na original.
        Conferir("colar: a copia tem Id proprio", novoLed.Id != led.Id);

        // Colar de novo desloca em vez de empilhar, e leva o fio de novo.
        ed.Colar();
        Conferir("colar duas vezes: leva o fio nas duas", c.Fios.Count == 4,
            $"ficaram {c.Fios.Count}");

        // ---- colar no ponteiro ----
        {
            var c2 = new Circuito();
            var a = new PecaNoCircuito { Tipo = "led", X = 100, Y = 100 };
            var b = new PecaNoCircuito { Tipo = "led", X = 180, Y = 100 };
            c2.Pecas.AddRange([a, b]);
            var ed2 = new EditorCircuito();
            ed2.Carregar(c2);
            ed2.SelecionarPorIndice(0);
            ed2.SelecionarPorIndice(1, true);
            ed2.Copiar();

            var alvo = new System.Windows.Point(700, 400);
            ed2.Colar(alvo);

            var novas = c2.Pecas.Skip(2).ToList();
            double cx = (novas.Min(p => p.X) + novas.Max(p => p.X)) / 2;
            double cy = (novas.Min(p => p.Y) + novas.Max(p => p.Y)) / 2;
            saida.AppendLine($"        colado com centro em ({cx:0};{cy:0}), "
                             + $"pedido ({alvo.X:0};{alvo.Y:0})");

            // Encaixa na grade, entao pode ficar a meio passo do pedido.
            Conferir("colar: o grupo vai para onde o ponteiro esta",
                Math.Abs(cx - alvo.X) <= Catalogo.Passo
                && Math.Abs(cy - alvo.Y) <= Catalogo.Passo,
                $"({cx:0};{cy:0}) contra ({alvo.X:0};{alvo.Y:0})");

            Conferir("colar: as pecas guardam a distancia entre elas",
                Math.Abs((novas[1].X - novas[0].X) - (b.X - a.X)) < 0.01,
                "o grupo deformou");

            // Sem ponteiro, o velho deslocamento: nunca empilhado em cima.
            ed2.Copiar();
            ed2.Colar(null);
            var ultima = c2.Pecas[^1];
            Conferir("colar sem ponteiro: desloca em vez de empilhar",
                Math.Abs(ultima.X - novas[1].X) > 1,
                "colou em cima do original");
        }
    }

    /// <summary>
    /// O .sktch guarda circuito e codigo juntos e volta igual.
    ///
    /// Antes eram duas metades soltas, um .json e um .ino, sem nada
    /// dizendo que uma era da outra.
    /// </summary>
    static void Formato_Sktch()
    {
        saida.AppendLine("formato .sktch");

        var c = new Circuito();
        var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 10, Y = 20 };
        var led = new PecaNoCircuito
        { Tipo = "led", X = 300, Y = 40, Cor = "verde", Rotacao = 90 };
        var pot = new PecaNoCircuito { Tipo = "potentiometer", X = 500, Y = 40, Giro = 0.33 };
        c.Pecas.AddRange([placa, led, pot]);
        c.Fios.Add(new Fio
        {
            DePeca = placa.Id, DePino = "D14", ParaPeca = led.Id, ParaPino = "A",
            Cor = "#D6202A", Estilo = Fio.EstiloDegrau
        });

        string codigo = "void setup() {\n  pinMode(14, OUTPUT);\n}\n\n"
                        + "void loop() {\n  digitalWrite(14, HIGH);\n}";

        string caminho = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"teste-{Guid.NewGuid():N}.sktch");
        try
        {
            Sktch.Salvar(caminho, c, codigo);
            string bruto = System.IO.File.ReadAllText(caminho);
            saida.AppendLine($"        arquivo com {bruto.Length} bytes, "
                             + $"{bruto.Split('\n').Length} linhas");

            Conferir("sktch: o arquivo se identifica", bruto.Contains("\"formato\""));
            Conferir("sktch: tem versao", bruto.Contains("\"versao\""));

            // Codigo linha a linha: da para ler e da para fazer diff.
            Conferir("sktch: o codigo vai em linhas, nao escapado",
                bruto.Contains("\"void setup() {\"") && !bruto.Contains("\\n  pinMode"),
                "codigo saiu como uma string so");

            var (voltou, codigoVolta) = Sktch.Abrir(caminho);

            Conferir("sktch: o codigo volta identico", codigoVolta?.Replace("\r\n", "\n") == codigo,
                "codigo diferente");
            Conferir("sktch: as pecas voltam", voltou.Pecas.Count == 3,
                $"voltaram {voltou.Pecas.Count}");
            Conferir("sktch: os fios voltam", voltou.Fios.Count == 1);

            var ledVolta = voltou.Pecas[1];
            Conferir("sktch: o estado da peca volta",
                ledVolta.Cor == "verde" && Math.Abs(ledVolta.Rotacao - 90) < 0.01
                && Math.Abs(voltou.Pecas[2].Giro - 0.33) < 1e-9,
                $"{ledVolta.Cor} {ledVolta.Rotacao} {voltou.Pecas[2].Giro}");
            Conferir("sktch: o traçado do fio volta",
                voltou.Fios[0].Estilo == Fio.EstiloDegrau && voltou.Fios[0].Cor == "#D6202A");
            Conferir("sktch: os Ids batem com os fios",
                voltou.Achar(voltou.Fios[0].ParaPeca) is not null);

            // O .json de circuito antigo continua abrindo.
            string velho = System.IO.Path.ChangeExtension(caminho, ".json");
            System.IO.File.WriteAllText(velho, c.ParaJson());
            var (soCircuito, semCodigo) = Sktch.Abrir(velho);
            Conferir("sktch: abre tambem o .json antigo",
                soCircuito.Pecas.Count == 3 && semCodigo is null,
                $"{soCircuito.Pecas.Count} pecas, codigo {(semCodigo is null ? "nulo" : "presente")}");
            System.IO.File.Delete(velho);

            // Arquivo de outro programa nao entra fingindo ser .sktch.
            string intruso = System.IO.Path.ChangeExtension(caminho, ".outro");
            System.IO.File.WriteAllText(intruso, "{\"formato\": \"sktch\", \"versao\": 99}");
            bool recusou = false;
            try { Sktch.Abrir(intruso); }
            catch (System.IO.InvalidDataException) { recusou = true; }
            Conferir("sktch: recusa versao mais nova que a do programa", recusou);
            System.IO.File.Delete(intruso);

            // A assinatura muda quando o codigo muda, nao so o circuito.
            string a1 = Sktch.Assinatura(c, codigo);
            string a2 = Sktch.Assinatura(c, codigo + " ");
            Conferir("sktch: mexer so no codigo conta como alteracao", a1 != a2);
        }
        finally
        {
            if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
        }
    }

    /// <summary>
    /// Dois objetos nao cabem no mesmo furo -- na bancada tambem nao.
    ///
    /// O encaixe pegava o furo mais PERTO, ocupado ou nao, entao o
    /// resistor ia parar em cima do LED sem nada avisar. Agora ele pega o
    /// mais perto que esteja LIVRE; se nao houver nenhum ao alcance, nao
    /// encaixa e a peca fica solta onde foi largada.
    /// </summary>
    static void Protoboard_UmTerminalPorFuro()
    {
        saida.AppendLine("um terminal por furo");

        var defBoard = Catalogo.Obter(Catalogo.IdProtoboard830);
        var defRes = Catalogo.Obter("resistor");

        // Em que furo cai cada pino de uma peca, se e que cai.
        static List<string> FurosDe(Circuito c, PecaNoCircuito peca)
        {
            var achados = new List<string>();
            var def = Catalogo.Obter(peca.Tipo);
            foreach (var placa in c.Pecas.Where(x => Catalogo.EhProtoboard(x.Tipo)))
            {
                var dp = Catalogo.Obter(placa.Tipo);
                foreach (var pino in def.Pinos)
                {
                    var q = Geometria.PosicaoPino(peca, def, pino);
                    foreach (var furo in dp.Pinos)
                    {
                        var pf = Geometria.PosicaoPino(placa, dp, furo);
                        double dx = pf.X - q.X, dy = pf.Y - q.Y;
                        if (dx * dx + dy * dy <= Netlist.RaioDoFuro * Netlist.RaioDoFuro)
                            achados.Add(furo.Nome);
                    }
                }
            }
            return achados;
        }

        var c = new Circuito();
        var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard830, X = 0, Y = 0 };
        c.Pecas.Add(board);

        // Primeiro resistor: pino 1 no furo B10.
        var alvo = defBoard.Pinos.First(x => x.Nome == "B10");
        var pos = Geometria.PosicaoPino(board, defBoard, alvo);
        var pino1 = defRes.Pinos.First(x => x.Nome == "1");
        var r1 = new PecaNoCircuito
        { Tipo = "resistor", X = pos.X - pino1.X, Y = pos.Y - pino1.Y };
        c.Pecas.Add(r1);

        var furos1 = FurosDe(c, r1);
        saida.AppendLine($"        resistor 1 ocupa [{string.Join(",", furos1)}]");
        Conferir("furo: o primeiro resistor encaixa", furos1.Count == 2,
            $"caiu em {furos1.Count} furos");

        // Segundo resistor largado EM CIMA do primeiro.
        var r2 = new PecaNoCircuito { Tipo = "resistor", X = r1.X + 1, Y = r1.Y + 1 };
        c.Pecas.Add(r2);
        var ajuste = Geometria.AjusteParaFuro(r2, defRes, c, Catalogo.Passo * 0.95);
        if (ajuste is not null) { r2.X += ajuste.Value.X; r2.Y += ajuste.Value.Y; }

        var furos2 = FurosDe(c, r2);
        saida.AppendLine($"        resistor 2 largado em cima -> "
                         + $"[{(furos2.Count == 0 ? "nenhum furo, ficou solto" : string.Join(",", furos2))}]");

        Conferir("furo: o segundo nao rouba furo do primeiro",
            !furos2.Intersect(furos1).Any(),
            $"dividiu {string.Join(",", furos2.Intersect(furos1))}");

        // A regra vale para o circuito inteiro, nao so para esse par.
        {
            var todos = new List<string>();
            foreach (var peca in c.Pecas.Where(x => !Catalogo.EhProtoboard(x.Tipo)))
                todos.AddRange(FurosDe(c, peca));
            var repetidos = todos.GroupBy(x => x).Where(g => g.Count() > 1)
                                 .Select(g => g.Key).ToList();
            Conferir("furo: nenhum furo com dois terminais",
                repetidos.Count == 0, string.Join(",", repetidos));
        }

        // Ponta de fio tambem ocupa: e um terminal espetado como outro.
        {
            var c2 = new Circuito();
            var b2 = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard830, X = 0, Y = 0 };
            c2.Pecas.Add(b2);
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 900, Y = 0 };
            c2.Pecas.Add(placa);
            c2.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = "D14",
                ParaPeca = b2.Id, ParaPino = "B10", Cor = "#00A651"
            });

            var alvo2 = defBoard.Pinos.First(x => x.Nome == "B10");
            var pos2 = Geometria.PosicaoPino(b2, defBoard, alvo2);
            var r3 = new PecaNoCircuito
            { Tipo = "resistor", X = pos2.X - pino1.X + 1, Y = pos2.Y - pino1.Y + 1 };
            c2.Pecas.Add(r3);
            var aj = Geometria.AjusteParaFuro(r3, defRes, c2, Catalogo.Passo * 0.95);
            if (aj is not null) { r3.X += aj.Value.X; r3.Y += aj.Value.Y; }

            var furos3 = FurosDe(c2, r3);
            saida.AppendLine($"        fio ja em B10; resistor foi para "
                             + $"[{(furos3.Count == 0 ? "solto" : string.Join(",", furos3))}]");
            Conferir("furo: ponta de fio tambem ocupa o furo",
                !furos3.Contains("B10"), "o resistor entrou no furo do fio");
        }

        // O caminho do arraste roda a cada movimento do mouse.
        {
            var solto = new PecaNoCircuito
            { Tipo = "resistor", X = 300, Y = 60 };
            c.Pecas.Add(solto);
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            const int vezes = 200;
            for (int k = 0; k < vezes; k++)
            {
                solto.X = 300 + k % 7;
                Geometria.AjusteParaFuro(solto, defRes, c, Catalogo.Passo * 0.95);
            }
            relogio.Stop();
            double porVez = relogio.Elapsed.TotalMilliseconds / vezes;
            saida.AppendLine($"        encaixe: {porVez:0.000} ms por movimento do mouse");
            Conferir("furo: o encaixe nao pesa no arraste", porVez < 2.0,
                $"{porVez:0.00} ms");
        }
    }

    /// <summary>
    /// As demos que vem embutidas, uma por placa.
    ///
    /// Elas sao a primeira coisa que o aluno abre, entao um fio ligado
    /// num pino que nao existe ou uma peca pendurada fora do furo seria
    /// pior do que nao ter demo nenhuma. Aqui cada uma e aberta,
    /// conferida peca a peca e ligada de verdade no netlist.
    /// </summary>
    static void Demos_QueVemNoPrograma()
    {
        saida.AppendLine("demos que vem no programa");

        // Uma por placa era a regra ate a 1.5; agora a ESP32 tem tres (a
        // basica, o servidor web e o MQTT). O que precisa continuar valendo
        // e que nenhuma placa fique sem demo.
        Conferir("demos: pelo menos uma para cada placa",
            Demos.Todas.Select(d => d.Placa).Distinct().Count() == PerfilPlaca.Conhecidos.Count,
            $"{Demos.Todas.Select(d => d.Placa).Distinct().Count()} placas com demo, "
            + $"de {PerfilPlaca.Conhecidos.Count}");

        // ---- o menu "Exemplos prontos", em grupos ----
        {
            var janela = new MainWindow();
            var menu = janela.TesteMenuDeDemos();
            var grupos = menu.Items.OfType<System.Windows.Controls.MenuItem>().ToList();

            // As folhas do menu: familia > (placa >) demo.
            static IEnumerable<System.Windows.Controls.MenuItem> Folhas(System.Windows.Controls.MenuItem m)
                => m.Items.Count == 0 ? [m]
                   : m.Items.OfType<System.Windows.Controls.MenuItem>().SelectMany(Folhas);

            var recursos = grupos.SelectMany(Folhas).Select(i => (string)i.Tag).ToList();

            saida.AppendLine("        exemplos prontos: "
                             + string.Join(", ", grupos.Select(g => g.Header)));

            Conferir("demos: o menu vem em grupos, e nenhum passa da tela",
                grupos.Count >= 4 && grupos.All(g => g.Items.Count is > 0 and <= 26),
                string.Join(", ", grupos.Select(g => $"{g.Header}")));
            Conferir("demos: os grupos nao perdem nem repetem demo",
                recursos.Count == Demos.Todas.Length
                && recursos.Distinct().Count() == recursos.Count,
                $"{recursos.Count} de {Demos.Todas.Length}");
            Conferir("demos: toda demo achou a familia da placa dela",
                Demos.Todas.All(d => Demos.GrupoDe(d) != "Outras"),
                string.Join(", ", Demos.Todas.Where(d => Demos.GrupoDe(d) == "Outras")
                                             .Select(d => d.Recurso)));

            // O menu do botao direito mostra o atalho como se le.
            var direito = janela.TesteMenuDoCircuito();
            var gestos = direito?.Items.OfType<System.Windows.Controls.MenuItem>()
                                 .Select(i => i.InputGestureText)
                                 .Where(t => !string.IsNullOrEmpty(t)).ToList() ?? [];
            if (direito is not null) direito.IsOpen = false;

            saida.AppendLine("        atalhos no botao direito: " + string.Join(", ", gestos));
            Conferir("menu do circuito: atalho escrito como se le (Ctrl+C), e nao como se guarda",
                gestos.Count > 0 && gestos.Contains("Ctrl+C") && !gestos.Any(t => t.Contains(':')),
                string.Join(", ", gestos));

            var linguagens = grupos.FirstOrDefault(
                g => ((string)g.Header).StartsWith(Demos.GrupoDasLinguagens));
            Conferir("demos: as de linguagem ficam juntas, no fim",
                linguagens is not null && grupos[^1] == linguagens && linguagens.Items.Count == 7,
                linguagens is null ? "sem grupo" : $"{linguagens.Items.Count} no grupo");

            // A placa com varias demos ganha submenu; a com uma so, nao.
            var esp32 = grupos.First(g => ((string)g.Header).StartsWith("ESP32"));
            var daDevKit = esp32.Items.OfType<System.Windows.Controls.MenuItem>()
                                .FirstOrDefault(i => ((string)i.Header).StartsWith("ESP32  ("));
            var pico = grupos.First(g => ((string)g.Header).StartsWith("Raspberry Pi"))
                             .Items.OfType<System.Windows.Controls.MenuItem>().ToList();
            saida.AppendLine("        raspberry pi: " + string.Join(" | ", pico.Select(i => i.Header)));
            Conferir("demos: a placa com varias demos tem o seu submenu",
                daDevKit is { Items.Count: 5 } && pico.Count == 2 && pico.All(i => i.Items.Count == 3),
                $"ESP32: {daDevKit?.Items.Count}, Raspberry Pi: {pico.Count}");
            Conferir("demos: a placa com uma demo so e item direto, com o nome da placa",
                esp32.Items.OfType<System.Windows.Controls.MenuItem>()
                     .Any(i => (string)i.Header == "ESP32-S2 DevKit — Relógio pela internet (NTP) no OLED"
                               && i.Items.Count == 0));

            // Nenhum projeto se repete: era o pedido -- "a maioria e pisca-pisca".
            var nomes = Demos.Todas.Select(d => d.Nome).ToList();
            Conferir("demos: nenhum nome de demo se repete",
                nomes.Distinct().Count() == nomes.Count,
                string.Join(", ", nomes.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key)));
            Conferir("demos: nenhuma demo e um pisca-pisca",
                !Demos.Todas.Any(d => d.Recurso.EndsWith("-pisca") || d.Nome.Contains("Pisca")),
                string.Join(", ", Demos.Todas.Where(d => d.Recurso.EndsWith("-pisca")).Select(d => d.Recurso)));
        }

        foreach (var demo in Demos.Todas)
        {
            string texto = Demos.Texto(demo.Recurso);
            if (texto is null)
            {
                Conferir($"{demo.Recurso}: veio embutida no programa", false, "recurso ausente");
                continue;
            }

            string temporario = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"demo-{Guid.NewGuid():N}.sktch");
            try
            {
                System.IO.File.WriteAllText(temporario, texto);
                var (c, codigo) = Sktch.Abrir(temporario);

                // ---- tem placa, e a que o nome promete ----
                var placa = c.Pecas.FirstOrDefault(x => PerfilPlaca.EhPlaca(x.Tipo));
                bool temCodigo = !string.IsNullOrWhiteSpace(codigo);

                saida.AppendLine($"        {demo.Recurso,-24} {c.Pecas.Count} pecas, "
                                 + $"{c.Fios.Count} fios, placa {placa?.Tipo ?? "NENHUMA"}");

                Conferir($"{demo.Recurso}: tem uma placa", placa is not null);
                Conferir($"{demo.Recurso}: tem codigo", temCodigo);
                if (placa is null) continue;

                // ---- todo fio aponta para peca e pino que existem ----
                int quebrados = 0;
                foreach (var f in c.Fios)
                {
                    foreach (var (idPeca, nomePino) in new[]
                             { (f.DePeca, f.DePino), (f.ParaPeca, f.ParaPino) })
                    {
                        var alvo = c.Achar(idPeca);
                        var def = alvo is null ? null : Catalogo.Obter(alvo.Tipo);
                        if (def is null || def.Pinos.All(x => x.Nome != nomePino)) quebrados++;
                    }
                }
                Conferir($"{demo.Recurso}: os fios apontam para pinos que existem",
                    quebrados == 0, $"{quebrados} pontas soltas");

                // ---- peca de protoboard tem que estar ESPETADA ----
                var rede = new Netlist(c);
                var board = c.Pecas.FirstOrDefault(x => Catalogo.EhProtoboard(x.Tipo));
                if (board is not null)
                {
                    // O que importa nao e estar no furo: e estar LIGADA.
                    // Potenciometro e servo ficam de fora de proposito,
                    // ligados por fio -- exigir furo deles acusaria erro
                    // onde nao ha.
                    int soltas = 0;
                    foreach (var peca in c.Pecas)
                    {
                        if (Catalogo.EhProtoboard(peca.Tipo) || PerfilPlaca.EhPlaca(peca.Tipo))
                            continue;
                        var def = Catalogo.Obter(peca.Tipo);
                        if (def is null) continue;

                        bool ligada = def.Pinos.Any(pino =>
                            rede.EstaLigado(peca.Id, pino.Nome));
                        if (!ligada) soltas++;
                    }
                    Conferir($"{demo.Recurso}: nenhuma peca ficou solta",
                        soltas == 0, $"{soltas} sem ligacao nenhuma");
                }

                // ---- o codigo chega nos pinos: pelo menos um GPIO ligado ----
                //
                // So quando ha componente: o ESP-10 so tem a serial para
                // fora, e a demo dele e a placa sozinha na rede.
                var perfil = PerfilPlaca.De(placa.Tipo);
                var componentes = c.Pecas
                    .Where(x => !Catalogo.EhProtoboard(x.Tipo) && !PerfilPlaca.EhPlaca(x.Tipo)).ToList();
                int ligados = componentes
                    .Sum(x => Catalogo.Obter(x.Tipo)?.Pinos
                                .Count(pino => rede.GpioDe(c, x.Id, pino.Nome) >= 0) ?? 0);
                Conferir($"{demo.Recurso}: os componentes chegam em algum GPIO",
                    componentes.Count == 0 || ligados > 0, "nenhuma ligacao ate a placa");
            }
            finally
            {
                if (System.IO.File.Exists(temporario)) System.IO.File.Delete(temporario);
            }
        }
    }

    /// <summary>
    /// Nenhum circuito que vem no programa pode ter fio para pino que
    /// nao existe.
    ///
    /// Fio assim some do desenho sem avisar: PosicaoPino devolve null e
    /// o desenho pula. O circuito padrao ficou semanas com o LED verde
    /// sem ligacao porque o fio apontava para "D16", e a ESP32 DevKit V1
    /// nao tem esse pino -- o GPIO 16 se chama RX2 no header.
    /// </summary>
    static void Fios_TodosValidos()
    {
        saida.AppendLine("fios apontam para pinos que existem");

        // ---- o circuito que abre com o programa ----
        {
            var c = MainWindow.CircuitoDeAbertura();
            var quebrados = c.FiosQuebrados().Distinct().ToList();
            saida.AppendLine($"        circuito padrao: {c.Pecas.Count} pecas, "
                             + $"{c.Fios.Count} fios, {quebrados.Count} quebrados");
            Conferir("circuito padrao: nenhum fio quebrado",
                quebrados.Count == 0, string.Join(", ", quebrados));

            // Cada LED tem que ter um fio saindo da placa ate ele.
            var placa = c.Pecas.First(x => PerfilPlaca.EhPlaca(x.Tipo));
            var rede = new Netlist(c);
            int ligados = c.Pecas.Count(x => x.Tipo == "led"
                && Catalogo.Obter("led").Pinos.Any(pino =>
                     rede.GpioDe(c, x.Id, pino.Nome) >= 0));
            saida.AppendLine($"        LEDs ligados a um GPIO: {ligados} de 3");
            Conferir("circuito padrao: os tres LEDs chegam num GPIO",
                ligados == 3, $"so {ligados}");
        }

        // ---- e as demos ----
        foreach (var demo in Demos.Todas)
        {
            string texto = Demos.Texto(demo.Recurso);
            if (texto is null) continue;
            string tmp = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"fio-{Guid.NewGuid():N}.sktch");
            try
            {
                System.IO.File.WriteAllText(tmp, texto);
                var (c, _) = Sktch.Abrir(tmp);
                var quebrados = c.FiosQuebrados().Distinct().ToList();
                Conferir($"{demo.Recurso}: nenhum fio quebrado",
                    quebrados.Count == 0, string.Join(", ", quebrados));
            }
            finally
            {
                if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
            }
        }
    }

    /// <summary>
    /// O que o motor nao simula tem que RECUSAR, nao aceitar calado.
    ///
    /// attachInterrupt era aceito e nao fazia nada: o sketch rodava, a
    /// interrupcao nunca disparava e o aluno procurava o erro no proprio
    /// codigo. Recusar com uma frase que diz o que fazer custa menos que
    /// uma hora de aula perdida.
    /// </summary>
    static void Recusas_Honestas()
    {
        saida.AppendLine("recusa em vez de aceitar calado");

        static string Rodar(string corpo)
        {
            try
            {
                var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
                var prog = new Parser(Lexer.Tokenizar(
                    "void nada() {}" + Environment.NewLine
                    + "void setup() { " + corpo + " }" + Environment.NewLine
                    + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        // A REGRA e que nada passa calado fingindo que funcionou. O que
        // e recusado muda a cada versao: attachInterrupt e shiftOut eram
        // recusados e hoje sao simulados; na 1.5 foi a vez da rede, e na
        // Alpha 20 a do Serial2 (a UART entre placas). O que sobra sao as
        // bibliotecas que o simulador ainda nao tem.
        foreach (string chamada in new[]
                 { "ArduinoOTA.begin();", "deserializeJson(doc, texto);" })
        {
            string erro = Rodar(chamada);
            saida.AppendLine($"        {chamada,-34} {(erro is null ? "ACEITOU" : "recusou")}");
            Conferir($"recusa: {chamada.Split('(')[0]}", erro is not null,
                "passou calado");
        }

        // Recusa por PLACA: rede numa placa que nao tem radio nenhum.
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Uno };
            maq.Reiniciar();
            string erro = null;
            try
            {
                var prog = new Parser(Lexer.Tokenizar(
                    "void setup() { WiFi.begin(\"x\", \"y\"); }" + Environment.NewLine
                    + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
            }
            catch (Exception e) { erro = e.Message; }

            saida.AppendLine($"        WiFi no Uno: {erro}");
            Conferir("recusa: WiFi numa placa sem WiFi", erro is not null, "passou calado");
            Conferir("recusa: o erro diz qual placa tem WiFi",
                erro is not null && erro.Contains("ESP32"), erro);
        }

        // As inofensivas continuam passando: o sketch roda igual sem elas.
        foreach (string chamada in new[]
                 { "noInterrupts();", "interrupts();", "yield();",
                   "int p = digitalPinToInterrupt(4);" })
        {
            string erro = Rodar(chamada);
            Conferir($"aceita: {chamada.Split('(')[0].Split(' ')[^1]}", erro is null, erro);
        }

        // E as que agora funcionam nao podem voltar a recusar.
        foreach (string chamada in new[]
                 { "attachInterrupt(0, nada, 1);", "detachInterrupt(0);",
                   "shiftOut(1, 2, 0, 5);", "int b = shiftIn(1, 2, 0);" })
        {
            string erro = Rodar(chamada);
            Conferir($"simula: {chamada.Split('(')[0].Split(' ')[^1]}",
                erro is null, erro);
        }
    }

    /// <summary>
    /// Moldar o fio pelas alcas, pelo mesmo caminho que o mouse usa.
    ///
    /// O jeito do Wokwi: bolinha cheia em cada canto, bolinha vazada no
    /// meio de cada trecho. Arrastar a vazada cria um canto; arrastar a
    /// cheia move; clique duplo desfaz.
    /// </summary>
    static void Fios_Moldar()
    {
        saida.AppendLine("moldar o fio");

        static (Circuito c, EditorCircuito ed, Fio f) Bancada()
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var led = new PecaNoCircuito { Tipo = "led", X = 600, Y = 300 };
            c.Pecas.AddRange([placa, led]);
            var f = new Fio
            {
                DePeca = placa.Id, DePino = "D14",
                ParaPeca = led.Id, ParaPino = "A",
                Cor = "#00A651", Estilo = Fio.EstiloReto
            };
            c.Fios.Add(f);

            var ed = new EditorCircuito();
            ed.Carregar(c);
            ed.SelecionarFiosPorIndice([0]);
            return (c, ed, f);
        }

        // ---- alcas: uma vazada por trecho, uma cheia por canto ----
        {
            var (c, ed, f) = Bancada();
            var alcas = ed.AlcasParaTeste(0).ToList();
            saida.AppendLine($"        fio reto sem canto: {alcas.Count} alcas "
                             + $"({alcas.Count(x => x.nova)} vazadas)");
            Conferir("alcas: fio sem canto tem so a do meio",
                alcas.Count == 1 && alcas[0].nova, $"{alcas.Count} alcas");
        }

        // ---- arrastar a vazada cria um canto ----
        {
            var (c, ed, f) = Bancada();
            var meio = ed.AlcasParaTeste(0).First(x => x.nova).pos;

            ed.Apertar(meio);
            Conferir("criar: pegar a alca vazada ja cria o canto",
                f.Dobras.Count == 1, $"{f.Dobras.Count} cantos");

            var destino = new System.Windows.Point(meio.X + 120, meio.Y - 160);
            ed.Mover(destino, true);
            ed.Soltar();

            saida.AppendLine($"        canto foi para ({f.Dobras[0].X:0};{f.Dobras[0].Y:0}), "
                             + $"pedido ({destino.X:0};{destino.Y:0})");
            Conferir("criar: o canto segue o mouse",
                f.Dobras.Count == 1
                && Math.Abs(f.Dobras[0].X - destino.X) <= Catalogo.Passo
                && Math.Abs(f.Dobras[0].Y - destino.Y) <= Catalogo.Passo,
                $"({f.Dobras[0].X:0};{f.Dobras[0].Y:0})");

            // Com um canto, o traçado passa por ele.
            var pa = Geometria.PosicaoPino(c.Pecas[0], Catalogo.Obter(c.Pecas[0].Tipo),
                Catalogo.Obter(c.Pecas[0].Tipo).Pinos.First(x => x.Nome == "D14"));
            var pb = Geometria.PosicaoPino(c.Pecas[1], Catalogo.Obter("led"),
                Catalogo.Obter("led").Pinos.First(x => x.Nome == "A"));
            var noMeio = EditorCircuito.NaCurva(f, pa, pb, 0.5);
            Conferir("criar: o traçado passa pelo canto",
                Math.Abs(noMeio.X - f.Dobras[0].X) < 1
                && Math.Abs(noMeio.Y - f.Dobras[0].Y) < 1,
                $"meio em ({noMeio.X:0};{noMeio.Y:0})");

            // Agora sao dois trechos: duas vazadas e uma cheia.
            var alcas = ed.AlcasParaTeste(0).ToList();
            saida.AppendLine($"        com um canto: {alcas.Count} alcas "
                             + $"({alcas.Count(x => !x.nova)} cheias)");
            Conferir("alcas: um canto vira uma cheia e duas vazadas",
                alcas.Count(x => !x.nova) == 1 && alcas.Count(x => x.nova) == 2,
                $"{alcas.Count} alcas");
        }

        // ---- arrastar a cheia move o canto ----
        {
            var (c, ed, f) = Bancada();
            f.Dobras.Add(new PontoFio { X = 300, Y = 100 });
            ed.Carregar(c);
            ed.SelecionarFiosPorIndice([0]);

            var cheia = ed.AlcasParaTeste(0).First(x => !x.nova);
            ed.Apertar(cheia.pos);
            ed.Mover(new System.Windows.Point(420, 220), true);
            ed.Soltar();

            Conferir("mover: continua sendo um canto so", f.Dobras.Count == 1,
                $"{f.Dobras.Count}");
            Conferir("mover: o canto foi para onde o mouse parou",
                Math.Abs(f.Dobras[0].X - 420) <= Catalogo.Passo
                && Math.Abs(f.Dobras[0].Y - 220) <= Catalogo.Passo,
                $"({f.Dobras[0].X:0};{f.Dobras[0].Y:0})");
        }

        // ---- canto que nao dobra nada some sozinho ----
        {
            var (c, ed, f) = Bancada();
            var pa = Geometria.PosicaoPino(c.Pecas[0], Catalogo.Obter(c.Pecas[0].Tipo),
                Catalogo.Obter(c.Pecas[0].Tipo).Pinos.First(x => x.Nome == "D14"));
            var pb = Geometria.PosicaoPino(c.Pecas[1], Catalogo.Obter("led"),
                Catalogo.Obter("led").Pinos.First(x => x.Nome == "A"));

            f.Dobras.Add(new PontoFio { X = 900, Y = 900 });
            ed.Carregar(c);
            ed.SelecionarFiosPorIndice([0]);

            // Arrasta o canto para cima da reta entre os dois pinos.
            var cheia = ed.AlcasParaTeste(0).First(x => !x.nova);
            ed.Apertar(cheia.pos);
            ed.Mover(new System.Windows.Point((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2), true);
            ed.Soltar();

            saida.AppendLine($"        canto posto sobre a reta: sobraram "
                             + $"{f.Dobras.Count} cantos");
            Conferir("limpeza: canto que nao dobra nada some ao soltar",
                f.Dobras.Count == 0, $"{f.Dobras.Count} sobrou");
        }

        // ---- clique duplo desfaz o canto ----
        {
            var (c, ed, f) = Bancada();
            f.Dobras.Add(new PontoFio { X = 300, Y = 90 });
            ed.Carregar(c);
            ed.SelecionarFiosPorIndice([0]);

            var cheia = ed.AlcasParaTeste(0).First(x => !x.nova);
            bool tirou = ed.RemoverDobraEm(cheia.pos);
            Conferir("clique duplo: desfaz o canto", tirou && f.Dobras.Count == 0,
                $"tirou={tirou}, sobraram {f.Dobras.Count}");
        }

        // ---- endireitar limpa tudo ----
        {
            var (c, ed, f) = Bancada();
            f.Dobras.Add(new PontoFio { X = 200, Y = 90 });
            f.Dobras.Add(new PontoFio { X = 400, Y = 200 });
            ed.Carregar(c);
            ed.SelecionarFiosPorIndice([0]);
            ed.EndireitarFios();
            Conferir("endireitar: tira todos os cantos", f.Dobras.Count == 0,
                $"{f.Dobras.Count} sobraram");
        }

        // ---- os cantos sobrevivem ao .sktch ----
        {
            var (c, ed, f) = Bancada();
            f.Dobras.Add(new PontoFio { X = 250, Y = 80 });
            f.Dobras.Add(new PontoFio { X = 450, Y = 260 });

            string tmp = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dobra-{Guid.NewGuid():N}.sktch");
            try
            {
                Sktch.Salvar(tmp, c, "void setup(){} void loop(){}");
                var (voltou, _) = Sktch.Abrir(tmp);
                var fv = voltou.Fios[0];
                saida.AppendLine($"        depois de salvar e abrir: {fv.Dobras.Count} cantos");
                Conferir("sktch: os cantos voltam iguais",
                    fv.Dobras.Count == 2
                    && Math.Abs(fv.Dobras[1].X - 450) < 0.01
                    && Math.Abs(fv.Dobras[1].Y - 260) < 0.01,
                    $"{fv.Dobras.Count} cantos");
            }
            finally
            {
                if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
            }
        }
    }

    /// <summary>
    /// Metodos de String.
    ///
    /// O sketch de sondagem parava em msg.length(): a variavel guarda
    /// texto, nao um objeto de biblioteca, e o motor so sabia chamar
    /// metodo de objeto. Exercicio que le do Serial e corta a resposta
    /// depende disso, entao vale conferir cada um pelo resultado.
    /// </summary>
    static void Texto_MetodosDeString()
    {
        saida.AppendLine("metodos de String");

        // Roda o corpo e devolve o que foi para o monitor serial.
        static (string saida, string erro) Rodar(string corpo)
        {
            try
            {
                var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
                var prog = new Parser(Lexer.Tokenizar(
                    "void setup() { " + corpo + " }" + Environment.NewLine
                    + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
                // A linha vem com o carimbo de tempo do monitor; aqui
                // interessa so o que o sketch imprimiu.
                return (string.Join("|", maq.Serial.Select(
                    l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l)), null);
            }
            catch (Exception e) { return (null, e.Message); }
        }

        (string corpo, string esperado)[] casos =
        [
            ("String m = \"Arduino\"; Serial.println(m.length());", "7"),
            ("String m = \"Arduino\"; Serial.println(m.substring(0, 3));", "Ard"),
            ("String m = \"Arduino\"; Serial.println(m.substring(3));", "uino"),
            ("String m = \"Arduino\"; Serial.println(m.indexOf(\"dui\"));", "2"),
            ("String m = \"Arduino\"; Serial.println(m.indexOf(\"zz\"));", "-1"),
            ("String m = \"Arduino\"; Serial.println(m.charAt(0));", "A"),
            ("String m = \"  42  \"; Serial.println(m.trim());", "42"),
            ("String m = \"42\"; Serial.println(m.toInt() + 1);", "43"),
            ("String m = \"abc\"; Serial.println(m.toInt());", "0"),
            ("String m = \"3.5cm\"; Serial.println(m.toFloat() * 2);", "7.00"),
            ("String m = \"led\"; Serial.println(m.toUpperCase());", "LED"),
            ("String m = \"LED\"; Serial.println(m.toLowerCase());", "led"),
            ("String m = \"led\"; Serial.println(m.equals(\"led\"));", "1"),
            ("String m = \"led:on\"; Serial.println(m.startsWith(\"led\"));", "1"),
            ("String m = \"led:on\"; Serial.println(m.endsWith(\"on\"));", "1"),
            ("String m = \"a-b\"; Serial.println(m.replace(\"-\", \"+\"));", "a+b"),
            ("String m = \"\"; Serial.println(m.isEmpty());", "1"),

            // Fora do intervalo nao estoura, igual ao Arduino de verdade.
            ("String m = \"ab\"; Serial.println(m.charAt(9));", ""),
            ("String m = \"ab\"; Serial.println(m.substring(9));", ""),

            // O que aparece no exercicio de ler comando do monitor.
            ("String m = \"\"; m += \"cmd:\"; m += 7; "
             + "Serial.println(m.substring(m.indexOf(\":\") + 1).toInt());", "7"),
        ];

        foreach (var (corpo, esperado) in casos)
        {
            var (linha, erro) = Rodar(corpo);
            string curto = corpo.Length > 46 ? corpo[..46] + "..." : corpo;
            saida.AppendLine($"        {curto,-52} -> "
                             + (erro is null ? $"\"{linha}\"" : "ERRO: " + erro));
            Conferir($"String: {curto}", erro is null && linha == esperado,
                erro ?? $"deu \"{linha}\", esperava \"{esperado}\"");
        }

        // Metodo que nao existe continua recusando em vez de dar 0 calado.
        var (_, recusa) = Rodar("String m = \"a\"; Serial.println(m.voar());");
        Conferir("String: metodo desconhecido ainda recusa", recusa is not null,
            "passou calado");
    }

    /// <summary>
    /// Importar um diagram.json como o Wokwi salva hoje.
    ///
    /// O que estava quebrado: a placa vinha como "board-esp32-devkit-c-v4"
    /// e o importador so conhecia "wokwi-*", entao ela era pulada -- e
    /// com ela iam TODOS os fios, porque cada um tem uma ponta na placa.
    /// O que restava na tela eram LEDs soltos. O trecho abaixo e recorte
    /// de um arquivo de verdade, com os pinos por numero e o monitor
    /// serial no meio das ligacoes.
    /// </summary>
    static void Wokwi_DiagramaDeVerdade()
    {
        saida.AppendLine("importar diagram.json do Wokwi");

        const string json = """
        {
          "version": 1, "author": "turma", "editor": "wokwi",
          "parts": [
            { "type": "board-esp32-devkit-c-v4", "id": "esp",
              "top": -9.6, "left": -167.96, "attrs": {} },
            { "type": "wokwi-buzzer", "id": "bz1",
              "top": -16.8, "left": -238.2, "attrs": { "volume": "0.1" } },
            { "type": "wokwi-led", "id": "led1",
              "top": -214.8, "left": -293.8, "attrs": { "color": "red" } },
            { "type": "wokwi-led", "id": "led6",
              "top": -149.8, "left": -293.8, "attrs": { "color": "blue" } },
            { "type": "wokwi-resistor", "id": "r1", "top": -139.2,
              "left": -298.15, "rotate": 90, "attrs": { "value": "220" } },
            { "type": "wokwi-resistor", "id": "r6", "top": -139.2,
              "left": -154.15, "rotate": 90, "attrs": { "value": "220" } },
            { "type": "wokwi-pushbutton-6mm", "id": "btn1", "top": 74.6,
              "left": -240, "attrs": { "color": "green" } }
          ],
          "connections": [
            [ "esp:TX", "$serialMonitor:RX", "", [] ],
            [ "esp:RX", "$serialMonitor:TX", "", [] ],
            [ "led1:A", "r1:1", "green", ["v0"] ],
            [ "led6:A", "r6:1", "green", ["v0"] ],
            [ "r1:2", "esp:4", "green", ["v75.6", "h336", "v163.2"] ],
            [ "r6:2", "esp:16", "green", ["v46.8", "h153.6", "v182.4"] ],
            [ "bz1:1", "esp:GND.2", "green", ["h-28.8", "v-76.8", "h192"] ],
            [ "bz1:2", "esp:27", "green", ["v0"] ],
            [ "btn1:1.r", "esp:32", "green", ["v0"] ],
            [ "led1:C", "esp:GND.2", "green", ["v0"] ]
          ]
        }
        """;

        var r = ImportarWokwi.Ler(json);
        var c = r.Circuito;

        saida.AppendLine($"        {c.Pecas.Count} peças, {c.Fios.Count} fios, "
                         + $"{r.Ignorados.Count} tipo(s) fora, "
                         + $"{r.FiosPerdidos.Count} fio(s) perdido(s)");

        Conferir("wokwi: a placa board-* entra como ESP32",
            c.Pecas.Any(x => x.Tipo == "esp32-devkit-v1"),
            "tipos: " + string.Join(", ", c.Pecas.Select(x => x.Tipo)));

        Conferir("wokwi: nenhum tipo ficou de fora", r.Ignorados.Count == 0,
            string.Join(", ", r.Ignorados));

        // Duas das doze ligacoes sao do monitor serial da tela deles.
        Conferir("wokwi: os 8 fios de verdade entraram", c.Fios.Count == 8,
            $"{c.Fios.Count} fios");

        Conferir("wokwi: o monitor serial nao vira fio perdido",
            r.FiosPerdidos.Count == 0, string.Join("; ", r.FiosPerdidos));

        // A prova real: fio que aponta para pino inexistente some da tela.
        var quebrados = c.FiosQuebrados().ToList();
        Conferir("wokwi: nenhum fio aponta para pino que nao existe",
            quebrados.Count == 0, string.Join("; ", quebrados));

        // GPIO 4 tem serigrafia D4; o 16 e o RX2 -- e o mesmo pino.
        var placa = c.Pecas.First(x => x.Tipo == "esp32-devkit-v1");
        var pinosDaPlaca = c.Fios
            .Where(f => f.DePeca == placa.Id || f.ParaPeca == placa.Id)
            .Select(f => f.DePeca == placa.Id ? f.DePino : f.ParaPino)
            .ToList();
        saida.AppendLine("        pinos usados na placa: "
                         + string.Join(", ", pinosDaPlaca));
        Conferir("wokwi: o pino 4 virou D4", pinosDaPlaca.Contains("D4"),
            string.Join(", ", pinosDaPlaca));
        Conferir("wokwi: o pino 16 virou RX2", pinosDaPlaca.Contains("RX2"),
            string.Join(", ", pinosDaPlaca));
        Conferir("wokwi: GND.2 continua GND.2", pinosDaPlaca.Contains("GND.2"),
            string.Join(", ", pinosDaPlaca));

        // O caminho do fio vira dobra; "v0" nao vira dobra nenhuma.
        var comDobra = c.Fios.First(f => f.Dobras.Count > 0);
        saida.AppendLine($"        dobras por fio: "
                         + string.Join(", ", c.Fios.Select(f => f.Dobras.Count)));
        Conferir("wokwi: o caminho de 3 passos vira 3 dobras",
            c.Fios.Count(f => f.Dobras.Count == 3) == 3
            && comDobra.Estilo == Fio.EstiloReto,
            $"estilo {comDobra.Estilo}");

        // Cinco das oito ligacoes vem com ["v0"], que nao anda nada.
        Conferir("wokwi: passo de tamanho zero nao vira dobra",
            c.Fios.Count(f => f.Dobras.Count == 0) == 5,
            string.Join(", ", c.Fios.Select(f => f.Dobras.Count)));

        // As dobras sao absolutas: a primeira sai do pino de origem.
        var pecaOrigem = c.Pecas.First(x => x.Id == comDobra.DePeca);
        var defOrigem = Catalogo.Obter(pecaOrigem.Tipo);
        var pos = Geometria.PosicaoPino(pecaOrigem, defOrigem,
            defOrigem.Pinos.First(x => x.Nome == comDobra.DePino));
        Conferir("wokwi: a primeira dobra sai do proprio pino",
            Math.Abs(comDobra.Dobras[0].X - pos.X) < 0.01
            || Math.Abs(comDobra.Dobras[0].Y - pos.Y) < 0.01,
            $"pino ({pos.X:0.0};{pos.Y:0.0}), dobra "
            + $"({comDobra.Dobras[0].X:0.0};{comDobra.Dobras[0].Y:0.0})");

        // ---- LCD com conector de I2C ----
        // E o diagrama do professor: o display do painel de carro tem
        // attrs pins="i2c" e quatro fios. Sem ler esse atributo o LCD
        // entrava com o conector de 16 vias e os quatro fios do
        // barramento se perdiam, um por um.
        {
            const string comI2c = """
            {
              "version": 1, "author": "turma", "editor": "wokwi",
              "parts": [
                { "type": "wokwi-esp32-devkit-v1", "id": "esp",
                  "top": 0, "left": 0, "attrs": {} },
                { "type": "wokwi-lcd1602", "id": "lcd_painel",
                  "top": -200, "left": 100, "attrs": { "pins": "i2c" } },
                { "type": "wokwi-lcd2004", "id": "lcd_grande",
                  "top": -400, "left": 100, "attrs": { "pins": "i2c" } },
                { "type": "wokwi-lcd1602", "id": "lcd_paralelo",
                  "top": 200, "left": 100, "attrs": {} }
              ],
              "connections": [
                [ "lcd_painel:SDA", "esp:D21", "green", [] ],
                [ "lcd_painel:SCL", "esp:D22", "yellow", [] ],
                [ "lcd_painel:VCC", "esp:5V", "red", [] ],
                [ "lcd_painel:GND", "esp:GND.1", "black", [] ],
                [ "lcd_paralelo:RS", "esp:D13", "green", [] ]
              ]
            }
            """;

            var comModulo = ImportarWokwi.Ler(comI2c);
            var ci = comModulo.Circuito;
            var tipos = ci.Pecas.Select(x => x.Tipo).ToList();

            saida.AppendLine($"        com I2C: {string.Join(", ", tipos)} · "
                             + $"{ci.Fios.Count} fios, {comModulo.FiosPerdidos.Count} perdidos");

            Conferir("wokwi: pins=\"i2c\" vira o LCD de modulo",
                tipos.Contains("lcd1602-i2c") && tipos.Contains("lcd2004-i2c"),
                string.Join(", ", tipos));
            Conferir("wokwi: sem o atributo, o LCD continua de 16 vias",
                tipos.Count(t => t == "lcd1602") == 1, string.Join(", ", tipos));
            Conferir("wokwi: os quatro fios do barramento entram",
                ci.Fios.Count == 5 && comModulo.FiosPerdidos.Count == 0,
                $"{ci.Fios.Count} fios, perdidos: "
                + string.Join("; ", comModulo.FiosPerdidos));
            Conferir("wokwi: nenhum fio de I2C aponta para pino inexistente",
                !ci.FiosQuebrados().Any(), string.Join("; ", ci.FiosQuebrados()));

            // E a volta: o LCD com modulo sai com o nome daqui, e volta o mesmo.
            string devolta = ExportarWokwi.Gerar(ci);
            Conferir("diagrama: o LCD com modulo sai como lcd2004-i2c (o nome daqui) e volta com modulo",
                devolta.Contains("\"lcd2004-i2c\"") && !devolta.Contains("wokwi-")
                && ImportarWokwi.Ler(devolta).Circuito.Pecas.Count(x => x.Tipo is "lcd1602-i2c" or "lcd2004-i2c") == 2,
                devolta[..Math.Min(400, devolta.Length)]);
        }

        // Cor e valor do resistor continuam vindo dos attrs.
        Conferir("wokwi: o LED azul chega azul",
            c.Pecas.Any(x => x.Tipo == "led" && x.Cor == "azul"),
            string.Join(", ", c.Pecas.Where(x => x.Tipo == "led").Select(x => x.Cor)));
        Conferir("wokwi: o resistor chega com 220 ohms",
            c.Pecas.Where(x => x.Tipo == "resistor").All(x => Math.Abs(x.Ohms - 220) < 0.01),
            string.Join(", ", c.Pecas.Where(x => x.Tipo == "resistor").Select(x => x.Ohms)));
    }

    /// <summary>
    /// Um sketch por placa.
    ///
    /// Antes as duas placas dividiam o mesmo campo de texto: nao dava
    /// para ter um programa para cada, e nem para saber de quem era o
    /// codigo na tela. Agora o codigo mora na peca, o campo mostra o da
    /// placa selecionada e o nome dela fica em cima.
    ///
    /// O teste monta a janela de verdade -- e nela que a troca acontece.
    /// </summary>
    static void Placa_CodigoDeCadaUma()
    {
        saida.AppendLine("um sketch por placa");

        static Circuito DuasPlacas()
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 });
            c.Pecas.Add(new PecaNoCircuito { Tipo = "led", X = 400, Y = 40 });
            c.Pecas.Add(new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 400 });
            return c;
        }

        var janela = new MainWindow();
        var c = DuasPlacas();
        janela.TesteAbrir(c, "// codigo do arquivo antigo");

        var placas = c.Placas().ToList();
        saida.AppendLine($"        placas: {string.Join(", ", placas.Select(x => x.Tipo))}");

        // ---- o codigo solto do arquivo antigo e da primeira placa ----
        Conferir("placa: codigo de arquivo antigo vai para a primeira placa",
            placas[0].Sketch == "// codigo do arquivo antigo",
            placas[0].Sketch);
        Conferir("placa: o cabeçalho diz de quem e o codigo",
            janela.TesteCabecalho == "ESP32 DevKit V1", janela.TesteCabecalho);

        // ---- cada placa guarda o seu ----
        janela.TesteDigitar("// sketch da ESP32");
        janela.TesteSelecionar(2);                     // o Arduino Uno

        saida.AppendLine($"        depois de clicar no Uno: cabeçalho "
                         + $"\"{janela.TesteCabecalho}\", campo \"{janela.TesteCodigo}\"");

        Conferir("placa: clicar na outra placa troca o cabeçalho",
            janela.TesteCabecalho == "Arduino Uno", janela.TesteCabecalho);
        Conferir("placa: o campo nao mostra mais o codigo da ESP32",
            !janela.TesteCodigo.Contains("sketch da ESP32"), janela.TesteCodigo);
        Conferir("placa: a ESP32 continua com o codigo dela",
            placas[0].Sketch == "// sketch da ESP32", placas[0].Sketch);

        janela.TesteDigitar("// sketch do Uno");
        Conferir("placa: o Uno guarda o dele", placas[1].Sketch == "// sketch do Uno",
            placas[1].Sketch);

        // ---- voltar para a ESP32 traz o codigo dela de volta ----
        janela.TesteSelecionar(0);
        Conferir("placa: voltar para a ESP32 traz o codigo dela de volta",
            janela.TesteCodigo == "// sketch da ESP32", janela.TesteCodigo);

        // ---- clicar num LED nao mexe no campo ----
        janela.TesteSelecionar(1);
        Conferir("placa: clicar num LED nao troca o codigo",
            janela.TesteCodigo == "// sketch da ESP32"
            && janela.TesteCabecalho == "ESP32 DevKit V1",
            $"{janela.TesteCabecalho} / {janela.TesteCodigo}");

        // ---- com duas placas o rodapé diz qual e qual ----
        saida.AppendLine($"        rodapé: \"{janela.TesteRodape}\"");
        Conferir("placa: o rodapé conta as placas",
            janela.TesteRodape.Contains("1 de 2"), janela.TesteRodape);

        // ---- os dois sketches sobrevivem ao .sktch ----
        string tmp = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"placas-{Guid.NewGuid():N}.sktch");
        try
        {
            Sktch.Salvar(tmp, c, janela.TesteCodigoPrincipal());
            var (voltou, codigo) = Sktch.Abrir(tmp);
            var deVolta = voltou.Placas().ToList();

            saida.AppendLine($"        depois de salvar e abrir: "
                             + string.Join(" | ", deVolta.Select(x => x.Sketch)));

            Conferir("placa: os dois sketches voltam do .sktch",
                deVolta.Count == 2
                && deVolta[0].Sketch == "// sketch da ESP32"
                && deVolta[1].Sketch == "// sketch do Uno",
                string.Join(" | ", deVolta.Select(x => x.Sketch)));

            // O campo "codigo" solto continua existindo para quem le o
            // arquivo de fora, e e o da primeira placa.
            Conferir("placa: o campo codigo do arquivo e o da primeira placa",
                codigo == "// sketch da ESP32", codigo);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }

        // ---- copiar e colar leva o codigo junto ----
        var copia = placas[0].Copia();
        copia.Sketch = "// mexido na copia";
        Conferir("placa: a copia tem codigo proprio",
            placas[0].Sketch == "// sketch da ESP32"
            && copia.Sketch == "// mexido na copia",
            $"{placas[0].Sketch} / {copia.Sketch}");

        // ---- circuito sem placa nenhuma nao quebra ----
        var soLed = new Circuito();
        soLed.Pecas.Add(new PecaNoCircuito { Tipo = "led", X = 0, Y = 0 });
        janela.TesteAbrir(soLed, "// sem placa");
        saida.AppendLine($"        sem placa: cabeçalho \"{janela.TesteCabecalho}\"");
        Conferir("placa: sem placa o cabeçalho diz isso",
            janela.TesteCabecalho.Contains("sem placa"), janela.TesteCabecalho);
        Conferir("placa: sem placa o codigo continua no campo",
            janela.TesteCodigo == "// sem placa", janela.TesteCodigo);

        // De proposito sem Close(): AoFechar abre um MessageBox de
        // "alteracoes nao salvas", e caixa modal trava o autoteste.
    }

    /// <summary>
    /// Queimar componente ligado errado.
    ///
    /// O simulador era bom demais: LED direto no GPIO acendia bonito, e
    /// esse mesmo circuito apaga o LED de vez na bancada. Agora estoura.
    ///
    /// A metade que importa mais deste teste e a de baixo: nenhuma das
    /// demos que vem no programa pode queimar. Um alarme falso aqui
    /// custaria mais caro que o recurso inteiro.
    /// </summary>
    static void Queima_LigacaoErrada()
    {
        saida.AppendLine("queimar quem esta ligado errado");

        // Placa energizada com o pino 14 em OUTPUT e HIGH.
        static Maquina Ligada(params int[] emAlta)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            foreach (int p in emAlta) { maq.Modo[p] = 1; maq.Pwm[p] = 255; maq.Digital[p] = 1; }
            return maq;
        }

        static (Circuito c, PecaNoCircuito placa, PecaNoCircuito led) Bancada()
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 60 };
            c.Pecas.AddRange([placa, led]);
            return (c, placa, led);
        }

        static void Ligar(Circuito c, PecaNoCircuito a, string pa,
                          PecaNoCircuito b, string pb)
            => c.Fios.Add(new Fio
            {
                DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb
            });

        // ---- LED direto num GPIO de 3,3 V: avisa, mas nao queima ----
        // A correcao que o professor pediu: "algumas portas nao tem
        // carga o suficiente para queimar o componente". A porta da
        // ESP32 entrega uns 40 mA e o LED aguenta; o erro continua
        // sendo erro, mas vira aviso em vez de fumaca.
        {
            var (c, placa, led) = Bancada();
            Ligar(c, placa, "D14", led, "A");
            Ligar(c, led, "C", placa, "GND.2");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada(14));
            var doLed = achados.FirstOrDefault(x => x.Peca == led.Id);
            saida.AppendLine($"        LED no GPIO de 3,3 V: {doLed?.Motivo}");

            Conferir("queima: LED sem resistor e apontado",
                doLed is not null, $"{achados.Count} achados");
            Conferir("queima: mas a porta de 3,3 V nao queima o LED",
                doLed is { Queimou: false }, doLed?.Motivo);
            Conferir("queima: o aviso diz o que fazer",
                doLed?.Motivo.Contains("220") == true, doLed?.Motivo);
        }

        // ---- o mesmo LED num GPIO de 5 V: ai queima ----
        {
            var c = new Circuito();
            var uno = new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 0 };
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 60 };
            c.Pecas.AddRange([uno, led]);
            Ligar(c, uno, "13", led, "A");
            Ligar(c, led, "C", uno, "GND.1");

            var maq = new Maquina { Perfil = PerfilPlaca.Uno };
            maq.Reiniciar();
            maq.Modo[13] = 1; maq.Digital[13] = 1; maq.Pwm[13] = 255;

            var achados = Queima.Conferir(c, new Netlist(c), maq);
            var doLed = achados.FirstOrDefault(x => x.Peca == led.Id);
            saida.AppendLine($"        LED no GPIO de 5 V: {doLed?.Motivo}");

            Conferir("queima: no pino de 5 V o LED queima mesmo",
                doLed is { Queimou: true }, doLed?.Motivo ?? "nenhum achado");
        }

        // ---- o mesmo LED com resistor: nao queima ----
        {
            var (c, placa, led) = Bancada();
            var r = new PecaNoCircuito { Tipo = "resistor", X = 250, Y = 60, Ohms = 220 };
            c.Pecas.Add(r);
            Ligar(c, placa, "D14", r, "1");
            Ligar(c, r, "2", led, "A");
            Ligar(c, led, "C", placa, "GND.2");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada(14));
            Conferir("queima: com resistor nao estoura", achados.Count == 0,
                string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- resistor do lado do catodo tambem serve ----
        {
            var (c, placa, led) = Bancada();
            var r = new PecaNoCircuito { Tipo = "resistor", X = 250, Y = 60, Ohms = 220 };
            c.Pecas.Add(r);
            Ligar(c, placa, "D14", led, "A");
            Ligar(c, led, "C", r, "1");
            Ligar(c, r, "2", placa, "GND.2");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada(14));
            Conferir("queima: resistor no catodo tambem segura", achados.Count == 0,
                string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- pino em LOW: nao passa corrente, nao queima ----
        {
            var (c, placa, led) = Bancada();
            Ligar(c, placa, "D14", led, "A");
            Ligar(c, led, "C", placa, "GND.2");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada());
            Conferir("queima: pino em LOW nao queima nada", achados.Count == 0,
                string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- LED sem resistor no 3V3: queima do mesmo jeito ----
        {
            var (c, placa, led) = Bancada();
            Ligar(c, placa, "3V3", led, "A");
            Ligar(c, led, "C", placa, "GND.2");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada());
            Conferir("queima: LED direto no 3V3 estoura",
                achados.Any(x => x.Peca == led.Id),
                string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- LED sem terra do outro lado: nao fecha, nao queima ----
        {
            var (c, placa, led) = Bancada();
            Ligar(c, placa, "D14", led, "A");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada(14));
            Conferir("queima: LED com o catodo no ar nao queima", achados.Count == 0,
                string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- 3V3 no GND: curto, a placa que vai ----
        {
            var (c, placa, _) = Bancada();
            Ligar(c, placa, "3V3", placa, "GND.2");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada());
            saida.AppendLine($"        3V3 no GND: "
                             + string.Join("; ", achados.Select(x => x.Motivo)));
            Conferir("queima: 3V3 direto no GND queima a placa",
                achados.Any(x => x.Peca == placa.Id),
                string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- saida em HIGH direto no terra ----
        {
            var (c, placa, _) = Bancada();
            Ligar(c, placa, "D14", placa, "GND.2");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada(14));
            Conferir("queima: saida em HIGH no GND queima a placa",
                achados.Any(x => x.Peca == placa.Id),
                string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- resistor do GPIO para o terra e legitimo ----
        {
            var (c, placa, _) = Bancada();
            var r = new PecaNoCircuito { Tipo = "resistor", X = 250, Y = 60, Ohms = 10000 };
            c.Pecas.Add(r);
            Ligar(c, placa, "D14", r, "1");
            Ligar(c, r, "2", placa, "GND.2");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada(14));
            Conferir("queima: resistor do pino para o GND nao e curto",
                achados.Count == 0, string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- placa desligada nao queima nada ----
        {
            var (c, placa, led) = Bancada();
            Ligar(c, placa, "D14", led, "A");
            Ligar(c, led, "C", placa, "GND.2");

            var parada = new Maquina { Perfil = PerfilPlaca.Esp32 };
            parada.Reiniciar();
            parada.Modo[14] = 1; parada.Pwm[14] = 255;
            parada.Desligar();

            var achados = Queima.Conferir(c, new Netlist(c), parada);
            Conferir("queima: circuito parado nao queima", achados.Count == 0,
                string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- saida em LOW ligada na alimentacao ----
        {
            var (c, placa, _) = Bancada();
            Ligar(c, placa, "D14", placa, "3V3");

            var maq = Ligada();
            maq.Modo[14] = 1;                 // OUTPUT, em LOW
            var achados = Queima.Conferir(c, new Netlist(c), maq);
            saida.AppendLine($"        saida em LOW no 3V3: "
                             + string.Join("; ", achados.Select(x => x.Motivo)));
            Conferir("queima: saida em LOW na alimentacao queima a placa",
                achados.Any(x => x.Peca == placa.Id && x.Queimou),
                string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- conflito digital: duas saidas brigando no mesmo no ----
        // O item que o professor deixou pela metade ("sinal digital
        // precisa ser revisado para evitar conflitos"): duas portas no
        // mesmo fio, uma empurrando para 3,3 V e a outra puxando para
        // zero. Na tela nao aparece nada; na bancada as duas se torram.
        {
            var (c, placa, _) = Bancada();
            Ligar(c, placa, "D14", placa, "D27");

            var maq = Ligada(14);
            maq.Modo[27] = 1; maq.Digital[27] = 0; maq.Pwm[27] = 0;

            var achados = Queima.Conferir(c, new Netlist(c), maq);
            var conflito = achados.FirstOrDefault(x => x.Peca == placa.Id);
            saida.AppendLine($"        conflito: {conflito?.Motivo}");

            Conferir("queima: duas saidas brigando queimam a placa",
                conflito is { Queimou: true }, conflito?.Motivo ?? "nenhum achado");
            Conferir("queima: o motivo diz os dois pinos",
                conflito?.Motivo.Contains("14") == true
                && conflito?.Motivo.Contains("27") == true, conflito?.Motivo);
        }

        // ---- duas saidas no MESMO nivel nao brigam ----
        {
            var (c, placa, _) = Bancada();
            Ligar(c, placa, "D14", placa, "D27");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada(14, 27));
            Conferir("queima: duas saidas no mesmo nivel nao e conflito",
                achados.Count == 0, string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- peca alimentada ao contrario ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var sensor = new PecaNoCircuito { Tipo = "dht22", X = 400, Y = 60 };
            c.Pecas.AddRange([placa, sensor]);
            Ligar(c, placa, "3V3", sensor, "GND");
            Ligar(c, placa, "GND.2", sensor, "VCC");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada());
            var doSensor = achados.FirstOrDefault(x => x.Peca == sensor.Id);
            saida.AppendLine($"        invertido: {doSensor?.Motivo}");

            Conferir("queima: peca com VCC e GND trocados queima",
                doSensor is { Queimou: true }, doSensor?.Motivo ?? "nenhum achado");
        }

        // ---- o mesmo sensor ligado certo nao queima ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var sensor = new PecaNoCircuito { Tipo = "dht22", X = 400, Y = 60 };
            c.Pecas.AddRange([placa, sensor]);
            Ligar(c, placa, "3V3", sensor, "VCC");
            Ligar(c, placa, "GND.2", sensor, "GND");
            Ligar(c, placa, "D14", sensor, "SDA");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada(14));
            Conferir("queima: sensor ligado certo nao queima", achados.Count == 0,
                string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- atuador pendurado no pino de dados ----
        // "Nao e so LED que queima": o servo puxa meio ampere e o pino
        // entrega 40 mA. Quem vai e a porta.
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var servo = new PecaNoCircuito { Tipo = "servo", X = 400, Y = 60 };
            c.Pecas.AddRange([placa, servo]);
            Ligar(c, placa, "D14", servo, "V+");
            Ligar(c, placa, "GND.2", servo, "GND");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada(14));
            var daPlaca = achados.FirstOrDefault(x => x.Peca == placa.Id);
            saida.AppendLine($"        servo no pino: {daPlaca?.Motivo}");

            Conferir("queima: servo alimentado pelo GPIO queima a porta",
                daPlaca is { Queimou: true }, daPlaca?.Motivo ?? "nenhum achado");
            Conferir("queima: o motivo compara as correntes",
                daPlaca?.Motivo.Contains("mA") == true, daPlaca?.Motivo);
        }

        // ---- o mesmo servo alimentado pelo trilho: certo ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var servo = new PecaNoCircuito { Tipo = "servo", X = 400, Y = 60 };
            c.Pecas.AddRange([placa, servo]);
            Ligar(c, placa, "VIN", servo, "V+");
            Ligar(c, placa, "GND.2", servo, "GND");
            Ligar(c, placa, "D14", servo, "PWM");

            var achados = Queima.Conferir(c, new Netlist(c), Ligada(14));
            Conferir("queima: servo no VIN nao queima nada", achados.Count == 0,
                string.Join("; ", achados.Select(x => x.Motivo)));
        }

        // ---- a parte que importa: nenhuma demo pode queimar ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            // O pior caso: TODAS as saidas em HIGH ao mesmo tempo.
            for (int i = 0; i < Maquina.NumPinos; i++)
            { maq.Modo[i] = 1; maq.Pwm[i] = 255; maq.Digital[i] = 1; }

            var circuitos = new List<(string nome, Circuito c)>
            {
                ("circuito de abertura", MainWindow.CircuitoDeAbertura())
            };
            foreach (var demo in Demos.Todas)
            {
                string texto = Demos.Texto(demo.Recurso);
                if (texto is null) continue;

                string tmp = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), $"queima-{Guid.NewGuid():N}.sktch");
                try
                {
                    System.IO.File.WriteAllText(tmp, texto);
                    var (c, _) = Sktch.Abrir(tmp);
                    circuitos.Add((demo.Nome, c));
                }
                finally
                {
                    if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
                }
            }

            foreach (var (nome, c) in circuitos)
            {
                var achados = Queima.Conferir(c, new Netlist(c), maq);
                saida.AppendLine($"        {nome,-34} {achados.Count} estouro(s)"
                                 + (achados.Count == 0 ? ""
                                    : " -- " + string.Join("; ",
                                        achados.Select(x => x.Motivo))));
                Conferir($"queima: a demo \"{nome}\" nao queima",
                    achados.Count == 0,
                    string.Join("; ", achados.Select(x => x.Motivo)));
            }
        }
    }

    /// <summary>
    /// O monitor serial que a Arduino IDE le.
    ///
    /// O monitor da IDE abria vazio: a placa e fantasma e nao ha porta
    /// de verdade do outro lado. Agora o CircuitSketch espelha cada
    /// linha num arquivo e o pluggable monitor da plataforma despeja
    /// esse arquivo no monitor da IDE.
    ///
    /// Aqui se prova a metade de ca -- que o arquivo existe, tem o que
    /// o sketch imprimiu e zera a cada execucao. A outra metade (os
    /// dois processos de PowerShell e o socket) e provada de fora, por
    /// ferramentas/provar-monitor, que faz o papel da IDE.
    /// </summary>
    static void Monitor_ParaAIde()
    {
        saida.AppendLine("monitor serial para a Arduino IDE");

        // Guarda o que estava la: quem roda o autoteste pode estar com o
        // programa aberto, e apagar o monitor dele seria falta de educacao.
        string caminho = MonitorIde.Arquivo;
        string antes = System.IO.File.Exists(caminho)
            ? System.IO.File.ReadAllText(caminho) : null;

        try
        {
            MonitorIde.Comecar();
            Conferir("monitor: rodar cria o arquivo zerado",
                System.IO.File.Exists(caminho)
                && System.IO.File.ReadAllText(caminho).Length == 0,
                caminho);

            MonitorIde.Escrever(["estado 0", "estado 1"]);
            MonitorIde.Escrever(["estado 2"]);

            var linhas = System.IO.File.ReadAllLines(caminho);
            saida.AppendLine($"        {caminho}");
            saida.AppendLine($"        {linhas.Length} linha(s): "
                             + string.Join(" | ", linhas));

            Conferir("monitor: as linhas chegam na ordem",
                linhas.Length == 3 && linhas[0] == "estado 0"
                && linhas[2] == "estado 2",
                string.Join(" | ", linhas));

            // Rodar de novo zera: o cano ve o arquivo encolher e trata
            // como reinicio da placa, igual a IDE faz.
            MonitorIde.Comecar();
            Conferir("monitor: rodar de novo zera o arquivo",
                System.IO.File.ReadAllText(caminho).Length == 0,
                $"{System.IO.File.ReadAllText(caminho).Length} bytes");

            // O cano abre o arquivo com o simulador escrevendo nele. Se
            // o CircuitSketch travar o arquivo, o monitor da IDE fica
            // preto e ninguem descobre por que.
            using (var aberto = System.IO.File.Open(caminho,
                       System.IO.FileMode.Open, System.IO.FileAccess.Read,
                       System.IO.FileShare.ReadWrite))
            {
                MonitorIde.Escrever(["com o cano lendo junto"]);
            }
            Conferir("monitor: da para escrever com o cano lendo junto",
                System.IO.File.ReadAllText(caminho).Contains("com o cano lendo"),
                System.IO.File.ReadAllText(caminho));
        }
        finally
        {
            try
            {
                if (antes is null) System.IO.File.Delete(caminho);
                else System.IO.File.WriteAllText(caminho, antes);
            }
            catch { }
        }
    }

    /// <summary>
    /// enum.
    ///
    /// Achado no sketch de verdade da turma: o programa parava logo na
    /// linha 23, em "enum Modo { ALTERNADO, WIGWAG, ... };", com
    /// "esperava ';' mas encontrei '{'". Enum e a primeira coisa que
    /// aparece quando o exercicio tem mais de um modo de funcionamento,
    /// e o sketch inteiro morria nele.
    ///
    /// Nao basta aceitar a declaracao: o NOME do enum precisa virar um
    /// tipo do sketch, senao "Modo m", "(Modo)x" e o parametro
    /// "nomeModo(Modo m)" continuam sem passar.
    /// </summary>
    static void Motor_Enum()
    {
        saida.AppendLine("enum");

        static (string linha, string erro) Imprimir(string antes, string dentro)
        {
            try
            {
                var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
                var prog = new Parser(Lexer.Tokenizar(
                    antes + Environment.NewLine
                    + "void setup() { " + dentro + " }" + Environment.NewLine
                    + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
                return (string.Join("|", maq.Serial.Select(
                    l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l)), null);
            }
            catch (Exception e) { return (null, e.Message); }
        }

        (string nome, string antes, string dentro, string esperado)[] casos =
        [
            ("conta de zero",
             "enum Modo { ALTERNADO, WIGWAG, CORRIDA };",
             "Serial.println(ALTERNADO); Serial.println(CORRIDA);", "0|2"),

            ("valor escrito continua a contagem",
             "enum E { X = 5, Y, Z = 10, W };",
             "Serial.println(Y); Serial.println(W);", "6|11"),

            ("o nome do enum vira tipo",
             "enum Modo { A, B, C };" + Environment.NewLine + "Modo m = B;",
             "Serial.println(m);", "1"),

            ("conversao para o tipo do enum",
             "enum Modo { A, B, C, TOTAL };" + Environment.NewLine + "Modo m = C;",
             "m = (Modo)((m + 1) % TOTAL); Serial.println(m);", "0"),

            ("enum como tipo de parametro",
             "enum Modo { A, B, C };" + Environment.NewLine
             + "String nome(Modo m) { if (m == B) return \"bee\"; return \"outro\"; }",
             "Serial.println(nome(B));", "bee"),

            ("enum dentro da funcao",
             "",
             "enum Cor { VERDE, AZUL }; Serial.println(AZUL);", "1"),

            ("enum class",
             "enum class Estado { PARADO, ANDANDO };",
             "Serial.println(ANDANDO);", "1"),

            ("enum com tipo declarado",
             "enum Bits : uint8_t { UM = 1, DOIS };",
             "Serial.println(DOIS);", "2"),

            ("enum e variavel na mesma linha",
             "enum Modo { A, B, C } atual = C;",
             "Serial.println(atual);", "2"),

            ("switch sobre o enum",
             "enum Modo { A, B, C };" + Environment.NewLine + "Modo m = B;",
             "switch (m) { case A: Serial.println(\"a\"); break; "
             + "case B: Serial.println(\"b\"); break; default: Serial.println(\"?\"); }",
             "b"),
        ];

        foreach (var (nome, antes, dentro, esperado) in casos)
        {
            var (linha, erro) = Imprimir(antes, dentro);
            saida.AppendLine($"        {nome,-34} -> "
                             + (erro is null ? $"\"{linha}\"" : "ERRO: " + erro));
            Conferir($"enum: {nome}", erro is null && linha == esperado,
                erro ?? $"deu \"{linha}\", esperava \"{esperado}\"");
        }

        // Membro que vem depois de um valor que so da para saber rodando:
        // recusa dizendo o que fazer, em vez de inventar um numero.
        var (_, recusa) = Imprimir(
            "int f() { return 3; }" + Environment.NewLine + "enum E { A = f(), B };",
            "Serial.println(B);");
        saida.AppendLine($"        recusa esperada: {recusa}");
        Conferir("enum: valor que so da para saber rodando e recusado com explicacao",
            recusa is not null && recusa.Contains("Escreva o valor"), recusa);

        // O tipo de um sketch nao pode vazar para o proximo: o Parser e
        // por sketch, e "Modo" so existe em quem declarou.
        var (_, vazou) = Imprimir("Modo m = 0;", "Serial.println(m);");
        Conferir("enum: o tipo nao vaza para outro sketch", vazou is not null,
            "aceitou 'Modo' sem enum nenhum");

        // O sketch da turma inteiro, do jeito que ele e.
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            string fonte = """
                enum Modo { ALTERNADO, WIGWAG, CORRIDA, FLASH_TUDO, DESLIGADO, TOTAL_MODOS };
                Modo modoAtual = ALTERNADO;
                const int PINOS_VERMELHO[6] = { 4, 5, 13, 16, 17, 18 };

                unsigned long intervaloDoModo(Modo m) {
                  switch (m) {
                    case ALTERNADO:  return 150;
                    case WIGWAG:     return 90;
                    default:         return 200;
                  }
                }

                String nomeModo(Modo m) {
                  switch (m) {
                    case ALTERNADO: return "ALTERNADO";
                    case WIGWAG:    return "WIGWAG";
                    default:        return "OUTRO";
                  }
                }

                void setup() {
                  Serial.begin(115200);
                  for (int i = 0; i < 6; i++) pinMode(PINOS_VERMELHO[i], OUTPUT);
                  Serial.println(nomeModo(modoAtual));
                  Serial.println(intervaloDoModo(modoAtual));
                  modoAtual = (Modo)((modoAtual + 1) % TOTAL_MODOS);
                  Serial.println(nomeModo(modoAtual));
                }

                void loop() {}
                """;

            string erro = null;
            try
            {
                var prog = new Parser(Lexer.Tokenizar(fonte)).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
            }
            catch (Exception e) { erro = e.Message; }

            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            saida.AppendLine($"        sketch da turma: {string.Join(" | ", linhas)}");

            Conferir("enum: o recorte do sketch da turma roda inteiro",
                erro is null && linhas.Count == 3
                && linhas[0] == "ALTERNADO" && linhas[1] == "150"
                && linhas[2] == "WIGWAG",
                erro ?? string.Join(" | ", linhas));
        }
    }

    /// <summary>
    /// Fio que cobre outro sai de cima dele.
    ///
    /// Dois fios entre os mesmos dois pinos desenhavam exatamente o mesmo
    /// traço: viravam um so na tela, e apagar "o de cima" apagava o que a
    /// pessoa nao estava vendo. O sombreado ajudou a perceber, mas eles
    /// continuavam empilhados.
    ///
    /// O teste que importa nao e o desenho: e o CLIQUE. Se os dois se
    /// afastam na tela mas o clique continua achando sempre o mesmo,
    /// nada foi resolvido.
    /// </summary>
    static void Fios_Sobrepostos()
    {
        saida.AppendLine("separar fio que cobre outro");

        static (Circuito c, EditorCircuito ed) Bancada(string estilo, int quantos)
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var led = new PecaNoCircuito { Tipo = "led", X = 500, Y = 300 };
            c.Pecas.AddRange([placa, led]);

            for (int i = 0; i < quantos; i++)
                c.Fios.Add(new Fio
                {
                    DePeca = placa.Id, DePino = "D14",
                    ParaPeca = led.Id, ParaPino = "A",
                    Cor = "#00A651", Estilo = estilo
                });

            var ed = new EditorCircuito();
            ed.Carregar(c);
            return (c, ed);
        }

        static (System.Windows.Point a, System.Windows.Point b) Pontas(Circuito c)
        {
            var defP = Catalogo.Obter(c.Pecas[0].Tipo);
            var defL = Catalogo.Obter("led");
            return (Geometria.PosicaoPino(c.Pecas[0], defP,
                        defP.Pinos.First(x => x.Nome == "D14")),
                    Geometria.PosicaoPino(c.Pecas[1], defL,
                        defL.Pinos.First(x => x.Nome == "A")));
        }

        foreach (string estilo in new[] { Fio.EstiloReto, Fio.EstiloDegrau })
        {
            var (c, ed) = Bancada(estilo, 2);
            var (a, b) = Pontas(c);

            var meio0 = ed.PontoDoFioParaTeste(0, 0.5);
            var meio1 = ed.PontoDoFioParaTeste(1, 0.5);
            double afastamento = Math.Sqrt(Math.Pow(meio0.X - meio1.X, 2)
                                           + Math.Pow(meio0.Y - meio1.Y, 2));

            saida.AppendLine($"        {estilo,-7}: meios em ({meio0.X:0.0};{meio0.Y:0.0}) "
                             + $"e ({meio1.X:0.0};{meio1.Y:0.0}) -- {afastamento:0.0} px");

            // O traco do fio tem 3 px: menos que isso e eles se encostam
            // e continuam parecendo um so.
            Conferir($"sobreposto: dois fios {estilo} nao ficam no mesmo lugar",
                afastamento >= 4.5, $"{afastamento:0.0} px de distancia");

            // As pontas continuam nos pinos: separar nao pode tirar o fio
            // do pino, senao troca um problema por outro.
            var ini0 = ed.PontoDoFioParaTeste(0, 0);
            var fim0 = ed.PontoDoFioParaTeste(0, 1);
            Conferir($"sobreposto: as pontas do fio {estilo} continuam nos pinos",
                Math.Abs(ini0.X - a.X) < 0.01 && Math.Abs(ini0.Y - a.Y) < 0.01
                && Math.Abs(fim0.X - b.X) < 0.01 && Math.Abs(fim0.Y - b.Y) < 0.01,
                $"({ini0.X:0.0};{ini0.Y:0.0}) -> ({fim0.X:0.0};{fim0.Y:0.0})");

            // O que importa de verdade: cada um pode ser clicado.
            int achado0 = ed.FioPertoParaTeste(meio0, 3);
            int achado1 = ed.FioPertoParaTeste(meio1, 3);
            saida.AppendLine($"        {estilo,-7}: clique no meio de cada um achou "
                             + $"{achado0} e {achado1}");
            Conferir($"sobreposto: da para clicar em cada fio {estilo} separadamente",
                achado0 != achado1 && achado0 >= 0 && achado1 >= 0,
                $"achou {achado0} e {achado1}");
        }

        // Tres fios: espalham em torno do lugar original, nenhum fica onde
        // estava sozinho e nenhum sai muito mais que os outros.
        {
            var (c, ed) = Bancada(Fio.EstiloReto, 3);
            var meios = new[] { ed.PontoDoFioParaTeste(0, 0.5),
                                ed.PontoDoFioParaTeste(1, 0.5),
                                ed.PontoDoFioParaTeste(2, 0.5) };
            saida.AppendLine("        tres fios: "
                + string.Join(", ", meios.Select(q => $"({q.X:0.0};{q.Y:0.0})")));

            var distintos = meios.Select(q => ($"{q.X:0.0}", $"{q.Y:0.0}")).Distinct().Count();
            Conferir("sobreposto: tres fios viram tres traços", distintos == 3,
                $"{distintos} posicoes diferentes");
        }

        // Um fio sozinho nao se mexe: quem nao cobre ninguem fica no lugar.
        {
            var (c, ed) = Bancada(Fio.EstiloReto, 1);
            var (a, b) = Pontas(c);
            var meio = ed.PontoDoFioParaTeste(0, 0.5);
            Conferir("sobreposto: fio sozinho nao sai do lugar",
                Math.Abs(meio.X - (a.X + b.X) / 2) < 0.01
                && Math.Abs(meio.Y - (a.Y + b.Y) / 2) < 0.01,
                $"({meio.X:0.0};{meio.Y:0.0})");
        }

        // "Solto" fica de fora de proposito: a curva dele ja depende da
        // distancia, entao dois soltos ja caem por caminhos diferentes.
        {
            var (c, ed) = Bancada(Fio.EstiloSolto, 2);
            var meio0 = ed.PontoDoFioParaTeste(0, 0.5);
            var meio1 = ed.PontoDoFioParaTeste(1, 0.5);
            Conferir("sobreposto: fio solto nao e empurrado",
                Math.Abs(meio0.X - meio1.X) < 0.01 && Math.Abs(meio0.Y - meio1.Y) < 0.01,
                $"({meio0.X:0.0};{meio0.Y:0.0}) e ({meio1.X:0.0};{meio1.Y:0.0})");
        }

        // Fio moldado a mao manda em si mesmo: empurrar seria desmanchar
        // o trabalho de quem colocou os cantos.
        {
            var (c, ed) = Bancada(Fio.EstiloReto, 2);
            c.Fios[0].Dobras.Add(new PontoFio { X = 300, Y = 120 });
            ed.Carregar(c);

            var meio0 = ed.PontoDoFioParaTeste(0, 0.5);
            Conferir("sobreposto: fio com dobra passa pelo canto que a pessoa pos",
                Math.Abs(meio0.X - 300) < 0.01 && Math.Abs(meio0.Y - 120) < 0.01,
                $"({meio0.X:0.0};{meio0.Y:0.0})");
        }
    }

    /// <summary>
    /// attachInterrupt, shiftOut e shiftIn.
    ///
    /// Antes eram recusados com uma mensagem mandando ler o pino dentro
    /// do loop(). Honesto -- muito melhor que aceitar calado e nunca
    /// disparar --, mas nao ajudava quem PRECISA de interrupcao, que e o
    /// exercicio de contar pulso de botao.
    ///
    /// A interrupcao aqui nao corta a execucao no meio: ela e atendida
    /// entre voltas do loop e depois de cada delay(). O efeito que o
    /// aluno ve e o mesmo -- o sketch reage ao botao sem te-lo lido --,
    /// e o estado nunca fica pela metade.
    /// </summary>
    static void Motor_Interrupcoes()
    {
        saida.AppendLine("interrupcao, shiftOut e shiftIn");

        static Interpretador Preparar(Maquina maq, string codigo)
        {
            var prog = new Parser(Lexer.Tokenizar(codigo)).AnalisarPrograma();
            var interp = new Interpretador(prog, maq);
            interp.Preparar();
            interp.Setup();
            return interp;
        }

        // ---- o exercicio classico: contar cliques ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();

            var interp = Preparar(maq, """
                volatile int cliques = 0;
                void contar() { cliques++; }
                void setup() {
                  Serial.begin(115200);
                  pinMode(4, INPUT_PULLUP);
                  attachInterrupt(digitalPinToInterrupt(4), contar, FALLING);
                }
                void loop() { Serial.println(cliques); }
                """);

            // Solto (pull-up = HIGH): nenhuma volta conta.
            interp.Loop();
            interp.Loop();

            // Aperta: o nivel cai, e a borda de descida vence.
            maq.Pressionado[4] = true;
            interp.Loop();
            // Solta: borda de subida, que nao interessa a este sketch.
            maq.Pressionado[4] = false;
            interp.Loop();
            maq.Pressionado[4] = true;
            interp.Loop();

            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            saida.AppendLine("        cliques por volta: " + string.Join(", ", linhas));

            Conferir("irq: a interrupcao dispara ao apertar",
                linhas.Count == 5 && linhas[0] == "0" && linhas[1] == "0"
                && linhas[2] == "1" && linhas[3] == "1" && linhas[4] == "2",
                string.Join(", ", linhas));
        }

        // ---- CHANGE conta os dois lados ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var interp = Preparar(maq, """
                int n = 0;
                void mexeu() { n++; }
                void setup() { pinMode(4, INPUT_PULLUP); attachInterrupt(4, mexeu, CHANGE); }
                void loop() { Serial.println(n); }
                """);

            maq.Pressionado[4] = true;  interp.Loop();
            maq.Pressionado[4] = false; interp.Loop();

            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            Conferir("irq: CHANGE conta apertar e soltar",
                linhas.Count == 2 && linhas[0] == "1" && linhas[1] == "2",
                string.Join(", ", linhas));
        }

        // ---- detachInterrupt para de contar ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var interp = Preparar(maq, """
                int n = 0;
                void contar() { n++; }
                void setup() { pinMode(4, INPUT_PULLUP); attachInterrupt(4, contar, FALLING); }
                void loop() { Serial.println(n); }
                """);

            maq.Pressionado[4] = true;  interp.Loop();
            maq.Pressionado[4] = false; interp.Loop();
            maq.PararDeVigiar(4);
            maq.Pressionado[4] = true;  interp.Loop();

            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            Conferir("irq: detachInterrupt para de contar",
                linhas.Count == 3 && linhas[2] == "1", string.Join(", ", linhas));
        }

        // ---- noInterrupts segura, e o que passou nao volta ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var interp = Preparar(maq, """
                int n = 0;
                void contar() { n++; }
                void setup() { pinMode(4, INPUT_PULLUP); attachInterrupt(4, contar, FALLING); }
                void loop() { Serial.println(n); }
                """);

            maq.InterrupcoesLigadas = false;
            maq.Pressionado[4] = true;  interp.Loop();
            maq.Pressionado[4] = false; interp.Loop();
            maq.InterrupcoesLigadas = true;
            interp.Loop();

            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            saida.AppendLine("        com noInterrupts: " + string.Join(", ", linhas));
            Conferir("irq: noInterrupts segura e o que passou nao volta",
                linhas.All(l => l == "0"), string.Join(", ", linhas));
        }

        // ---- ligar a vigia nao dispara sozinho ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.Pressionado[4] = true;          // ja apertado ANTES do setup
            var interp = Preparar(maq, """
                int n = 0;
                void contar() { n++; }
                void setup() { pinMode(4, INPUT_PULLUP); attachInterrupt(4, contar, CHANGE); }
                void loop() { Serial.println(n); }
                """);
            interp.Loop();

            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            Conferir("irq: ligar a vigia com o pino ja mexido nao dispara",
                linhas.Count == 1 && linhas[0] == "0", string.Join(", ", linhas));
        }

        // ---- apontar para funcao que nao existe e recusado ----
        {
            string erro = null;
            try
            {
                var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
                maq.Reiniciar();
                Preparar(maq, """
                    void setup() { attachInterrupt(4, naoExiste, FALLING); }
                    void loop() {}
                    """);
            }
            catch (Exception e) { erro = e.Message; }
            saida.AppendLine($"        funcao inexistente: {erro}");
            Conferir("irq: apontar para funcao que nao existe e recusado",
                erro is not null && erro.Contains("naoExiste"), erro);
        }

        // ---- shiftOut mexe os pinos de verdade ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            Preparar(maq, """
                void setup() {
                  pinMode(13, OUTPUT); pinMode(14, OUTPUT);
                  shiftOut(13, 14, MSBFIRST, 5);
                }
                void loop() {}
                """);

            int pulsos;
            lock (maq.Trava)
                pulsos = maq.Eventos.Count(e => e.Pino == 14 && e.Valor == 1);

            saida.AppendLine($"        shiftOut: {pulsos} pulsos de relogio");
            Conferir("irq: shiftOut da 8 pulsos de relogio", pulsos == 8, $"{pulsos}");
        }

        // ---- shiftIn le o que estiver no pino ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.NivelExterno[12] = 1;           // dado preso em HIGH
            Preparar(maq, """
                void setup() {
                  pinMode(12, INPUT); pinMode(14, OUTPUT);
                  Serial.println(shiftIn(12, 14, MSBFIRST));
                }
                void loop() {}
                """);

            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            saida.AppendLine($"        shiftIn com o dado em HIGH: {string.Join(",", linhas)}");
            Conferir("irq: shiftIn com o dado em HIGH le 255",
                linhas.Count == 1 && linhas[0] == "255", string.Join(",", linhas));
        }
    }

    /// <summary>
    /// Exportar diagram.json, e a prova de que ele presta: a ida e volta.
    ///
    /// Conferir o texto gerado campo a campo provaria pouco -- daria para
    /// escrever um arquivo bonito que o Wokwi nao abre. O que prova e
    /// exportar, importar de volta pelo nosso proprio importador e achar
    /// o mesmo circuito: as mesmas peças, os mesmos fios entre os mesmos
    /// pinos, e as dobras no mesmo lugar.
    ///
    /// Como o importador foi escrito contra um arquivo de verdade do
    /// Wokwi (Marco 23), passar por ele vale como passar pelo formato.
    /// </summary>
    static void Wokwi_IdaEVolta()
    {
        saida.AppendLine("exportar diagram.json");

        var c = new Circuito();
        var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 60, Y = 40 };
        var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 40, Cor = "azul" };
        var r = new PecaNoCircuito { Tipo = "resistor", X = 260, Y = 40,
                                     Ohms = 330, Rotacao = 90 };
        var botao = new PecaNoCircuito { Tipo = "pushbutton-6mm", X = 400, Y = 300 };
        c.Pecas.AddRange([placa, led, r, botao]);

        c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D4", ParaPeca = r.Id,
                             ParaPino = "1", Cor = "#D6202A", Estilo = Fio.EstiloReto });
        c.Fios.Add(new Fio { DePeca = r.Id, DePino = "2", ParaPeca = led.Id,
                             ParaPino = "A", Cor = "#00A651" });
        c.Fios.Add(new Fio { DePeca = led.Id, DePino = "C", ParaPeca = placa.Id,
                             ParaPino = "GND.2", Cor = "#000000" });

        // Um fio moldado a mao: as dobras tem que sobreviver a viagem.
        var comDobra = new Fio
        {
            DePeca = botao.Id, DePino = "1.r", ParaPeca = placa.Id, ParaPino = "RX2",
            Cor = "#1668C1", Estilo = Fio.EstiloReto
        };
        comDobra.Dobras.Add(new PontoFio { X = 300, Y = 320 });
        comDobra.Dobras.Add(new PontoFio { X = 300, Y = 180 });
        c.Fios.Add(comDobra);

        string json = ExportarWokwi.Gerar(c);
        saida.AppendLine($"        gerado: {json.Length} caracteres");

        // A placa sai com o nome daqui (sem o "wokwi-", que e do plugin), e o
        // pino com a serigrafia.
        string esp = placa.Id;
        Conferir("exportar: a placa sai como esp32-devkit-v1, o nome daqui",
            json.Contains("\"esp32-devkit-v1\"") && !json.Contains("wokwi-"), "tipo da placa");
        Conferir("exportar: os pinos da DevKit V1 saem com a serigrafia (D4, RX2)",
            json.Contains($"\"{esp}:D4\"") && json.Contains($"\"{esp}:RX2\""), json[..Math.Min(400, json.Length)]);

        Conferir("exportar: GND continua GND", json.Contains($"{esp}:GND.2"), "GND");

        Conferir("exportar: a cor do fio vira nome, nao hexadecimal",
            json.Contains("\"red\"") && !json.Contains("#D6202A"), "cor");

        // ---- a volta ----
        var r2 = ImportarWokwi.Ler(json);
        var volta = r2.Circuito;

        saida.AppendLine($"        de volta: {volta.Pecas.Count} peças, "
                         + $"{volta.Fios.Count} fios, {r2.Ignorados.Count} fora, "
                         + $"{r2.FiosPerdidos.Count} perdidos");

        Conferir("ida e volta: as peças voltam todas",
            volta.Pecas.Count == c.Pecas.Count && r2.Ignorados.Count == 0,
            string.Join(", ", r2.Ignorados));

        Conferir("ida e volta: os fios voltam todos",
            volta.Fios.Count == c.Fios.Count && r2.FiosPerdidos.Count == 0,
            string.Join("; ", r2.FiosPerdidos));

        Conferir("ida e volta: nenhum fio aponta para pino que nao existe",
            !volta.FiosQuebrados().Any(),
            string.Join("; ", volta.FiosQuebrados()));

        // Os pinos da placa tem que ser os MESMOS, com a serigrafia de volta.
        var placaVolta = volta.Pecas.First(x => PerfilPlaca.EhPlaca(x.Tipo));
        var pinos = volta.Fios
            .Where(f => f.DePeca == placaVolta.Id || f.ParaPeca == placaVolta.Id)
            .Select(f => f.DePeca == placaVolta.Id ? f.DePino : f.ParaPino)
            .OrderBy(x => x).ToList();
        saida.AppendLine("        pinos da placa na volta: " + string.Join(", ", pinos));
        Conferir("ida e volta: D4 e RX2 voltam com o nome daqui",
            pinos.Contains("D4") && pinos.Contains("RX2") && pinos.Contains("GND.2"),
            string.Join(", ", pinos));

        // Posicao e atributos.
        var ledVolta = volta.Pecas.First(x => x.Tipo == "led");
        Conferir("ida e volta: o LED azul volta azul e no mesmo lugar",
            ledVolta.Cor == "azul" && Math.Abs(ledVolta.X - 400) < 0.01
            && Math.Abs(ledVolta.Y - 40) < 0.01,
            $"{ledVolta.Cor} em ({ledVolta.X};{ledVolta.Y})");

        var rVolta = volta.Pecas.First(x => x.Tipo == "resistor");
        Conferir("ida e volta: o resistor volta com 330 ohms e girado",
            Math.Abs(rVolta.Ohms - 330) < 0.01 && Math.Abs(rVolta.Rotacao - 90) < 0.01,
            $"{rVolta.Ohms} ohms, {rVolta.Rotacao} graus");

        // As dobras: o desenho do fio tem que chegar igual do outro lado.
        var dobrado = volta.Fios.FirstOrDefault(f => f.Dobras.Count > 0);
        saida.AppendLine("        dobras na volta: "
            + (dobrado is null ? "nenhuma"
               : string.Join(" ", dobrado.Dobras.Select(d => $"({d.X:0};{d.Y:0})"))));
        // Nao da para exigir o MESMO numero de cantos: o formato deles so
        // anda em h e v, entao um trecho na diagonal vira um L e nasce um
        // canto no meio. O que tem que valer e que o fio continua passando
        // pelos cantos que a pessoa colocou, na ordem.
        static bool PassaPor(Fio f, double x, double y)
            => f.Dobras.Any(d => Math.Abs(d.X - x) < 0.5 && Math.Abs(d.Y - y) < 0.5);

        Conferir("ida e volta: o fio continua passando pelos cantos que a pessoa pos",
            dobrado is not null && PassaPor(dobrado, 300, 320) && PassaPor(dobrado, 300, 180),
            dobrado is null ? "nenhum fio com dobra"
                : string.Join(" ", dobrado.Dobras.Select(d => $"({d.X:0.0};{d.Y:0.0})")));

        // E os cantos que nascem sao so os do "L": cada um alinhado com o
        // vizinho em algum eixo. Canto solto em lugar nenhum seria erro.
        Conferir("ida e volta: os cantos a mais sao so os do L",
            dobrado is not null && dobrado.Dobras.Count <= c.Fios[3].Dobras.Count * 2
            && dobrado.Dobras.Zip(dobrado.Dobras.Skip(1)).All(
                par => Math.Abs(par.First.X - par.Second.X) < 0.5
                       || Math.Abs(par.First.Y - par.Second.Y) < 0.5),
            dobrado is null ? "-"
                : string.Join(" ", dobrado.Dobras.Select(d => $"({d.X:0.0};{d.Y:0.0})")));

        // O circuito de abertura tambem tem que sobreviver a viagem.
        {
            var abertura = MainWindow.CircuitoDeAbertura();
            var ida = ImportarWokwi.Ler(ExportarWokwi.Gerar(abertura));
            saida.AppendLine($"        circuito de abertura: {abertura.Pecas.Count} peças "
                             + $"-> {ida.Circuito.Pecas.Count}, {abertura.Fios.Count} fios "
                             + $"-> {ida.Circuito.Fios.Count}");
            Conferir("ida e volta: o circuito de abertura sobrevive inteiro",
                ida.Circuito.Pecas.Count == abertura.Pecas.Count
                && ida.Circuito.Fios.Count == abertura.Fios.Count
                && !ida.Circuito.FiosQuebrados().Any(),
                $"{ida.Ignorados.Count} tipos fora, {ida.FiosPerdidos.Count} fios perdidos");
        }
    }

    /// <summary>
    /// As cores do editor de codigo.
    ///
    /// Olhar a tela nao serve para conferir isto: numa foto em fundo
    /// escuro, "sem cor" e "cor errada" parecem a mesma coisa. Aqui o
    /// realce e aplicado num trecho e a cor de cada palavra e lida.
    ///
    /// O que se cobra e o que o aluno procura na tela: pinMode e
    /// digitalWrite marcados como funcao, HIGH e OUTPUT como constante,
    /// e -- o mais importante -- comentario nao ganhando cor de codigo.
    /// </summary>
    static void Editor_Realce()
    {
        saida.AppendLine("cores do editor");

        var doc = new ICSharpCode.AvalonEdit.Document.TextDocument();
        var realce = new ICSharpCode.AvalonEdit.Highlighting.DocumentHighlighter(
            doc, Realce.Arduino);

        // Qual cor o realce deu a esta palavra nesta linha?
        string CorDe(string linha, string palavra)
        {
            doc.Text = linha;
            var pintada = realce.HighlightLine(1);
            int onde = linha.IndexOf(palavra, StringComparison.Ordinal);
            if (onde < 0) return "(palavra nao esta na linha)";

            foreach (var trecho in pintada.Sections)
                if (onde >= trecho.Offset && onde < trecho.Offset + trecho.Length)
                    return trecho.Color?.Name ?? "(sem nome)";

            return "(sem cor)";
        }

        (string linha, string palavra, string esperado)[] casos =
        [
            ("pinMode(13, OUTPUT);", "pinMode", "Funcao"),
            ("digitalWrite(13, HIGH);", "digitalWrite", "Funcao"),
            ("void setup() {", "setup", "Funcao"),
            ("void loop() {", "loop", "Funcao"),
            ("if (millis() > 1000) {", "millis", "Funcao"),

            ("pinMode(13, OUTPUT);", "OUTPUT", "Constante"),
            ("digitalWrite(13, HIGH);", "HIGH", "Constante"),
            ("pinMode(LED_BUILTIN, OUTPUT);", "LED_BUILTIN", "Constante"),

            ("void setup() {", "void", "Tipo"),
            ("const int p = 4;", "const", "Tipo"),
            ("volatile int n = 0;", "volatile", "Tipo"),
            ("enum Modo { A, B };", "enum", "Tipo"),

            ("if (x > 3) return;", "if", "Controle"),
            ("switch (m) {", "switch", "Controle"),

            ("int atraso = 1000;", "1000", "Numero"),
            ("int x = 0xFF;", "0xFF", "Numero"),

            ("// pinMode aqui e so comentario", "pinMode", "Comentario"),
            ("Serial.println(\"pinMode\");", "pinMode", "Texto"),
        ];

        foreach (var (linha, palavra, esperado) in casos)
        {
            string cor = CorDe(linha, palavra);
            saida.AppendLine($"        {palavra,-14} em {linha,-34} -> {cor}");
            Conferir($"realce: {palavra} e {esperado}", cor == esperado,
                $"deu {cor}");
        }

        // O nome de uma variavel do aluno NAO pode ganhar cor: pintar
        // "ultimaTroca" de amarelo prometeria que o motor a conhece.
        Conferir("realce: nome inventado pelo aluno fica sem cor",
            CorDe("int ultimaTroca = 0;", "ultimaTroca") == "(sem cor)",
            CorDe("int ultimaTroca = 0;", "ultimaTroca"));
    }

    /// <summary>
    /// O rele KS2E: bobina energizada muda o contato de lugar.
    ///
    /// Era um dos sete que desenhavam e nao simulavam. E o mais pedido
    /// dos sete, porque e o exercicio de "a placa liga uma carga que ela
    /// nao aguenta" -- e sem simulacao o aluno nao via nada acontecer.
    ///
    /// O netlist e estatico, entao P nao passa a conduzir em NO de
    /// verdade. O que acontece e o que importa: o NIVEL atravessa. Em
    /// repouso vale NC; com a bobina ligada, NO.
    /// </summary>
    static void Rele_Comuta()
    {
        saida.AppendLine("rele KS2E");

        // Placa -> bobina; do outro lado, NO no GND e P num pino de
        // entrada. Com o rele ligado, esse pino tem que cair para LOW.
        static (Circuito c, EditorCircuito ed, Maquina maq, Netlist rede) Bancada(
            string pinoDoContato)
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var rele = new PecaNoCircuito { Tipo = "ks2e-m-dc5", X = 500, Y = 200 };
            c.Pecas.AddRange([placa, rele]);

            void Ligar(string pa, string pb) => c.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = pa, ParaPeca = rele.Id, ParaPino = pb
            });

            Ligar("D14", "COIL1");            // a placa aciona a bobina
            Ligar("GND.2", "COIL2");
            Ligar("D12", "P1");               // entrada que vai ler o contato
            c.Fios.Add(new Fio
            {
                DePeca = rele.Id, DePino = pinoDoContato,
                ParaPeca = placa.Id, ParaPino = "GND.1"
            });

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.Modo[12] = 2;                 // INPUT_PULLUP, como no sketch
            maq.Modo[14] = 1;

            return (c, ed, maq, new Netlist(c));
        }

        // ---- NO no GND: em repouso solto, com a bobina ligada cai ----
        {
            var (c, ed, maq, rede) = Bancada("NO1");

            maq.DigitalWrite(14, 0);
            ed.AplicarEntradas(maq, rede);
            int solto = maq.DigitalRead(12);

            maq.DigitalWrite(14, 1);
            ed.AplicarEntradas(maq, rede);
            int acionado = maq.DigitalRead(12);

            saida.AppendLine($"        NO no GND: bobina desligada le {solto}, "
                             + $"ligada le {acionado}");

            Conferir("rele: em repouso o contato NO nao conduz",
                solto == 1, $"leu {solto}");
            Conferir("rele: com a bobina ligada o NO fecha e leva o GND",
                acionado == 0, $"leu {acionado}");
        }

        // ---- NC no GND: o contrario, que e o que "normalmente fechado" quer dizer ----
        {
            var (c, ed, maq, rede) = Bancada("NC1");

            maq.DigitalWrite(14, 0);
            ed.AplicarEntradas(maq, rede);
            int solto = maq.DigitalRead(12);

            maq.DigitalWrite(14, 1);
            ed.AplicarEntradas(maq, rede);
            int acionado = maq.DigitalRead(12);

            saida.AppendLine($"        NC no GND: bobina desligada le {solto}, "
                             + $"ligada le {acionado}");

            Conferir("rele: em repouso o contato NC ja conduz",
                solto == 0, $"leu {solto}");
            Conferir("rele: com a bobina ligada o NC abre",
                acionado == 1, $"leu {acionado}");
        }

        // ---- desligar a bobina tem que voltar atras ----
        {
            var (c, ed, maq, rede) = Bancada("NO1");

            maq.DigitalWrite(14, 1);
            ed.AplicarEntradas(maq, rede);
            maq.DigitalWrite(14, 0);
            ed.AplicarEntradas(maq, rede);

            Conferir("rele: desligar a bobina solta o contato",
                maq.DigitalRead(12) == 1, $"leu {maq.DigitalRead(12)}");
        }

        // ---- o segundo contato e independente do primeiro ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var rele = new PecaNoCircuito { Tipo = "ks2e-m-dc5", X = 500, Y = 200 };
            c.Pecas.AddRange([placa, rele]);

            void Ligar(string pa, string pb) => c.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = pa, ParaPeca = rele.Id, ParaPino = pb
            });

            Ligar("D14", "COIL1");
            Ligar("GND.2", "COIL2");
            Ligar("D12", "P2");                       // a SEGUNDA via
            c.Fios.Add(new Fio { DePeca = rele.Id, DePino = "NO2",
                                 ParaPeca = placa.Id, ParaPino = "GND.1" });

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.Modo[12] = 2;
            maq.Modo[14] = 1;
            var rede = new Netlist(c);

            maq.DigitalWrite(14, 1);
            ed.AplicarEntradas(maq, rede);

            Conferir("rele: a segunda via funciona igual a primeira",
                maq.DigitalRead(12) == 0, $"leu {maq.DigitalRead(12)}");
        }

        // ---- rele sem nada no contato nao inventa nivel ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var rele = new PecaNoCircuito { Tipo = "ks2e-m-dc5", X = 500, Y = 200 };
            c.Pecas.AddRange([placa, rele]);
            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D14",
                                 ParaPeca = rele.Id, ParaPino = "COIL1" });
            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D12",
                                 ParaPeca = rele.Id, ParaPino = "P1" });

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.Modo[12] = 2;
            maq.Modo[14] = 1;
            var rede = new Netlist(c);

            maq.DigitalWrite(14, 1);
            ed.AplicarEntradas(maq, rede);

            Conferir("rele: contato no ar nao inventa nivel",
                maq.DigitalRead(12) == 1, $"leu {maq.DigitalRead(12)}");
        }
    }

    /// <summary>
    /// Controle remoto e receptor infravermelho.
    ///
    /// Os dois eram do grupo "desenha mas nao simula". O receptor tinha
    /// meio caminho feito -- decode() ja olhava uma leitura --, mas o
    /// jeito como o sketch e escrito em aula nao passava:
    ///
    ///   decode_results results;
    ///   if (irrecv.decode(&results)) { Serial.println(results.value); }
    ///
    /// Faltavam tres coisas no motor, e nenhuma e do infravermelho: o
    /// parser nao aceitava "&", o interpretador nao lia campo com ponto
    /// ("results.value") e nao existia a classe decode_results.
    /// </summary>
    static void Infravermelho_Par()
    {
        saida.AppendLine("controle e receptor infravermelho");

        const string sketch = """
            IRrecv irrecv(15);
            decode_results results;

            void setup() {
              Serial.begin(115200);
              irrecv.enableIRIn();
            }

            void loop() {
              if (irrecv.decode(&results)) {
                Serial.println(results.value);
                irrecv.resume();
              }
            }
            """;

        static Interpretador Preparar(Maquina maq)
        {
            var prog = new Parser(Lexer.Tokenizar(sketch)).AnalisarPrograma();
            var interp = new Interpretador(prog, maq);
            interp.Preparar();
            interp.Setup();
            return interp;
        }

        static List<string> Linhas(Maquina maq) => [.. maq.Serial.Select(
            l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l)];

        // ---- o sketch de aula roda e le o codigo ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var interp = Preparar(maq);

            interp.Loop();                       // nada apertado ainda
            int semAperto = maq.Serial.Count;

            maq.Leituras["ir:codigo"] = 162;     // botao do controle
            interp.Loop();

            var linhas = Linhas(maq);
            saida.AppendLine($"        sem aperto: {semAperto} linhas; "
                             + $"depois: {string.Join(", ", linhas)}");

            Conferir("ir: sem aperto nao chega codigo", semAperto == 0, $"{semAperto}");
            Conferir("ir: o codigo do controle chega em results.value",
                linhas.Count == 1 && linhas[0] == "162", string.Join(", ", linhas));
        }

        // ---- resume() consome: um aperto nao vira dez ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var interp = Preparar(maq);

            maq.Leituras["ir:codigo"] = 70;
            interp.Loop();
            interp.Loop();
            interp.Loop();

            var linhas = Linhas(maq);
            saida.AppendLine($"        tres voltas depois de um aperto: {linhas.Count} linha(s)");
            Conferir("ir: resume() consome o codigo", linhas.Count == 1,
                string.Join(", ", linhas));
        }

        // ---- campo que nao existe e recusado dizendo quais existem ----
        {
            string erro = null;
            try
            {
                var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
                maq.Reiniciar();
                var prog = new Parser(Lexer.Tokenizar("""
                    IRrecv irrecv(15);
                    decode_results results;
                    void setup() { irrecv.decode(&results); Serial.println(results.cor); }
                    void loop() {}
                    """)).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                maq.Leituras["ir:codigo"] = 5;
                interp.Setup();
            }
            catch (Exception e) { erro = e.Message; }

            saida.AppendLine($"        campo inventado: {erro}");
            Conferir("ir: campo que nao existe diz quais existem",
                erro is not null && erro.Contains("value"), erro);
        }

        // ---- sem receptor no circuito, o controle nao chega a lugar nenhum ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 });
            var controle = new PecaNoCircuito { Tipo = "ir-remote", X = 400, Y = 0,
                                                Giro = 0.5 };
            c.Pecas.Add(controle);

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            ed.AplicarEntradas(maq, new Netlist(c));

            double semReceptor = maq.Leitura("ir:codigo");

            c.Pecas.Add(new PecaNoCircuito { Tipo = "ir-receiver", X = 400, Y = 300 });
            ed.Carregar(c);
            ed.AplicarEntradas(maq, new Netlist(c));
            double comReceptor = maq.Leitura("ir:codigo");

            saida.AppendLine($"        so o controle: {semReceptor}; "
                             + $"com receptor: {comReceptor}");

            Conferir("ir: sem receptor no circuito o codigo nao chega",
                semReceptor == 0, $"{semReceptor}");
            Conferir("ir: com o receptor na tela o codigo chega",
                comReceptor > 0, $"{comReceptor}");
        }
    }

    /// <summary>
    /// Motor de passo biaxial.
    ///
    /// A conta sai dos PINOS, e nao da biblioteca Stepper -- o exercicio
    /// de motor de passo costuma ser escrever a sequencia a mao com
    /// digitalWrite, e quem faz isso nunca cria um objeto Stepper.
    ///
    /// O que precisa estar certo e o SENTIDO: a sequencia ao contrario
    /// tem que girar para tras. Sem isso, o aluno nao tem como descobrir
    /// que inverteu dois fios.
    /// </summary>
    static void Passo_Biaxial()
    {
        saida.AppendLine("motor de passo biaxial");

        static (PecaNoCircuito m, EditorCircuito ed, Maquina maq, Netlist rede,
                Dictionary<string, int> gpios) Bancada()
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var motor = new PecaNoCircuito { Tipo = "biaxial-stepper", X = 600, Y = 0 };
            c.Pecas.AddRange([placa, motor]);

            // Quatro bobinas do eixo 1 em quatro pinos da placa.
            var pinos = new[] { "D14", "D12", "D13", "D27" };
            var bobinas = new[] { "A1+", "B1+", "A1-", "B1-" };
            var gpios = new Dictionary<string, int>();

            for (int i = 0; i < 4; i++)
            {
                c.Fios.Add(new Fio
                {
                    DePeca = placa.Id, DePino = pinos[i],
                    ParaPeca = motor.Id, ParaPino = bobinas[i]
                });
                gpios[bobinas[i]] = PerfilPlaca.Esp32.Numero(pinos[i]);
            }

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            foreach (int g in gpios.Values) maq.Modo[g] = 1;

            return (motor, ed, maq, new Netlist(c), gpios);
        }

        // Aciona uma bobina de cada vez, na ordem dada.
        static void Sequencia(EditorCircuito ed, Maquina maq, Netlist rede,
                              Dictionary<string, int> gpios, string[] ordem, int voltas)
        {
            for (int v = 0; v < voltas; v++)
                foreach (string bobina in ordem)
                {
                    foreach (var (nome, g) in gpios) maq.DigitalWrite(g, nome == bobina ? 1 : 0);
                    ed.AplicarEntradas(maq, rede);
                }
        }

        string[] paraFrente = ["A1+", "B1+", "A1-", "B1-"];
        string[] paraTras = ["B1-", "A1-", "B1+", "A1+"];

        // ---- para frente ----
        {
            var (motor, ed, maq, rede, gpios) = Bancada();
            Sequencia(ed, maq, rede, gpios, paraFrente, 2);
            double angulo = ed.AnguloDoEixoParaTeste(motor.Id, 0);
            saida.AppendLine($"        8 passos para frente: {angulo:0.0} graus");
            Conferir("biaxial: a sequencia para frente gira para frente",
                angulo > 0, $"{angulo:0.0} graus");
        }

        // ---- para tras: o mesmo tanto, do outro lado ----
        {
            var (motor, ed, maq, rede, gpios) = Bancada();
            Sequencia(ed, maq, rede, gpios, paraTras, 2);
            double angulo = ed.AnguloDoEixoParaTeste(motor.Id, 0);
            saida.AppendLine($"        8 passos ao contrario: {angulo:0.0} graus");
            Conferir("biaxial: a sequencia invertida gira para tras",
                angulo < 0, $"{angulo:0.0} graus");
        }

        // ---- ida e volta volta ao lugar ----
        {
            var (motor, ed, maq, rede, gpios) = Bancada();
            Sequencia(ed, maq, rede, gpios, paraFrente, 3);
            Sequencia(ed, maq, rede, gpios, paraTras, 3);
            double angulo = ed.AnguloDoEixoParaTeste(motor.Id, 0);
            saida.AppendLine($"        ida e volta: {angulo:0.0} graus");
            Conferir("biaxial: ida e volta volta ao mesmo lugar",
                Math.Abs(angulo) < EixoDePasso.GrausPorPassoNema * 1.5, $"{angulo:0.0}");
        }

        // ---- bobina nenhuma ligada nao gira ----
        {
            var (motor, ed, maq, rede, gpios) = Bancada();
            foreach (int g in gpios.Values) maq.DigitalWrite(g, 0);
            for (int i = 0; i < 8; i++) ed.AplicarEntradas(maq, rede);
            Conferir("biaxial: motor sem bobina ligada nao gira",
                ed.AnguloDoEixoParaTeste(motor.Id, 0) == 0,
                $"{ed.AnguloDoEixoParaTeste(motor.Id, 0):0.0}");
        }

        // ---- o segundo eixo nao anda junto com o primeiro ----
        {
            var (motor, ed, maq, rede, gpios) = Bancada();
            Sequencia(ed, maq, rede, gpios, paraFrente, 2);
            Conferir("biaxial: mexer um eixo nao mexe o outro",
                ed.AnguloDoEixoParaTeste(motor.Id, 1) == 0,
                $"{ed.AnguloDoEixoParaTeste(motor.Id, 1):0.0}");
        }
    }

    /// <summary>
    /// Disco de telefone.
    ///
    /// Nao e um controle de posicao como o potenciometro: e um EVENTO com
    /// duracao. Discar o 3 manda tres pulsos, um depois do outro, e o
    /// sketch conta as bordas -- por isso um deslizante nao serviria.
    ///
    /// O "0" manda DEZ pulsos, que e uma das coisas que o exercicio
    /// existe para mostrar.
    /// </summary>
    static void Disco_Telefone()
    {
        saida.AppendLine("disco de telefone");

        static (PecaNoCircuito d, EditorCircuito ed, Maquina maq, Netlist rede)
            Bancada()
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var disco = new PecaNoCircuito { Tipo = "rotary-dialer", X = 600, Y = 0 };
            c.Pecas.AddRange([placa, disco]);

            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D14",
                                 ParaPeca = disco.Id, ParaPino = "PULSE" });
            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D12",
                                 ParaPeca = disco.Id, ParaPino = "DIAL" });

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            return (disco, ed, maq, new Netlist(c));
        }

        // Conta as descidas do PULSE, que e o que o sketch faz.
        // Conta ate a discagem TERMINAR, e nao por um numero fixo de
        // quadros: o disco agora vai ate o batente antes de voltar
        // pulsando, e quantos quadros isso leva e detalhe da animacao.
        static int Contar(EditorCircuito ed, Maquina maq, Netlist rede, int quadros)
        {
            int pulsos = 0, antes = 1;
            for (int i = 0; i < quadros; i++)
            {
                ed.AplicarEntradas(maq, rede);
                int agora = maq.DigitalRead(14);
                if (antes == 1 && agora == 0) pulsos++;
                antes = agora;
            }
            return pulsos;
        }

        foreach (int numero in new[] { 1, 3, 7, 0 })
        {
            var (disco, ed, maq, rede) = Bancada();
            ed.Discar(disco.Id, numero);
            int pulsos = Contar(ed, maq, rede, 120);

            int esperado = numero == 0 ? 10 : numero;
            saida.AppendLine($"        discou {numero}: {pulsos} pulso(s)");
            Conferir($"disco: o {numero} manda {esperado} pulsos",
                pulsos == esperado, $"{pulsos}");
        }

        // ---- parado nao pulsa ----
        {
            var (disco, ed, maq, rede) = Bancada();
            Conferir("disco: parado nao manda pulso",
                Contar(ed, maq, rede, 20) == 0, "pulsou sozinho");
        }

        // ---- DIAL avisa que esta discando ----
        {
            var (disco, ed, maq, rede) = Bancada();
            ed.AplicarEntradas(maq, rede);
            int parado = maq.DigitalRead(12);

            ed.Discar(disco.Id, 5);
            ed.AplicarEntradas(maq, rede);
            int discando = maq.DigitalRead(12);

            saida.AppendLine($"        DIAL parado={parado}, discando={discando}");
            Conferir("disco: DIAL avisa enquanto disca",
                parado == 1 && discando == 0, $"{parado} e {discando}");
        }

        // ---- termina e volta ao repouso ----
        {
            var (disco, ed, maq, rede) = Bancada();
            ed.Discar(disco.Id, 2);
            for (int i = 0; i < 120; i++) ed.AplicarEntradas(maq, rede);

            Conferir("disco: termina de discar e volta ao repouso",
                !ed.Discando(disco.Id) && maq.DigitalRead(14) == 1
                && maq.DigitalRead(12) == 1,
                $"PULSE={maq.DigitalRead(14)} DIAL={maq.DigitalRead(12)}");
        }
    }

    /// <summary>
    /// Cartao microSD.
    ///
    /// O exercicio que pede cartao SD e sempre o mesmo -- o datalogger:
    /// gravar uma linha por leitura e depois abrir o arquivo para
    /// conferir. Sem sistema de arquivos, ele nao tinha como acontecer.
    ///
    /// Na ESP32 o caminho comeca com "/" e o FILE_WRITE comeca o arquivo
    /// do zero; no Uno o FILE_WRITE escreve no fim. O reset da placa nao
    /// apaga o cartao -- ele e outro chip.
    /// </summary>
    static void Cartao_Sd()
    {
        saida.AppendLine("cartao microSD");

        static (Maquina maq, string erro) Rodar(string corpo, PerfilPlaca perfil = null)
        {
            var maq = new Maquina { Perfil = perfil ?? PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.Cartao.Presente = true;
            try
            {
                var prog = new Parser(Lexer.Tokenizar(
                    "void setup() {" + corpo + "}" + Environment.NewLine
                    + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
                return (maq, null);
            }
            catch (Exception e) { return (maq, e.Message); }
        }

        // ---- o datalogger inteiro ----
        {
            var (maq, erro) = Rodar("""
                Serial.begin(115200);
                SD.begin(5);
                File dados = SD.open("/registro.txt", FILE_WRITE);
                dados.println("25.5");
                dados.println("26.1");
                dados.close();

                File leitura = SD.open("/registro.txt");
                while (leitura.available() > 0) {
                  Serial.println(leitura.readStringUntil('\n'));
                }
                leitura.close();
                """);

            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            saida.AppendLine($"        datalogger: {erro ?? string.Join(" | ", linhas)}");

            Conferir("sd: o datalogger grava e le de volta",
                erro is null && linhas.Count == 2
                && linhas[0] == "25.5" && linhas[1] == "26.1",
                erro ?? string.Join(" | ", linhas));
        }

        // ---- abrir para leitura o que nao existe devolve nada ----
        {
            var (maq, erro) = Rodar("""
                Serial.begin(115200);
                SD.begin(5);
                if (SD.exists("/nada.txt")) Serial.println("existe");
                else Serial.println("nao existe");
                """);
            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            Conferir("sd: exists() diz a verdade sobre arquivo que nao ha",
                erro is null && linhas.Count == 1 && linhas[0] == "nao existe",
                erro ?? string.Join("|", linhas));
        }

        // ---- o FILE_WRITE de cada plataforma ----
        //
        // No Uno ele escreve no fim. Na ESP32 ele e o "w" do C: comeca o
        // arquivo do zero, e quem quer continuar usa FILE_APPEND -- o
        // registrador que so guarda a ultima linha e o tropeco classico.
        {
            const string duasVezes = """
                SD.begin(5);
                File a = SD.open("/log.txt", FILE_WRITE);
                a.println("um");
                a.close();
                File b = SD.open("/log.txt", FILE_WRITE);
                b.println("dois");
                b.close();
                """;
            var (noUno, erroUno) = Rodar(duasVezes, PerfilPlaca.De("arduino-uno"));
            var (naEsp, erroEsp) = Rodar(duasVezes);
            var (comAppend, erroAppend) = Rodar(duasVezes.Replace("File b = SD.open(\"/log.txt\", FILE_WRITE)",
                                                                  "File b = SD.open(\"/log.txt\", FILE_APPEND)"));
            string uno = noUno.Cartao.Conteudo("log.txt"), esp = naEsp.Cartao.Conteudo("log.txt"),
                   anexado = comAppend.Cartao.Conteudo("log.txt");
            saida.AppendLine($"        dois FILE_WRITE: Uno {uno?.Replace("\n", "/")} ESP32 {esp?.Replace("\n", "/")}"
                             + $" / FILE_APPEND na ESP32: {anexado?.Replace("\n", "/")}");
            Conferir("sd: no Uno o FILE_WRITE escreve no fim do arquivo",
                erroUno is null && uno == "um\ndois\n", erroUno ?? uno);
            Conferir("sd: na ESP32 o FILE_WRITE comeca o arquivo do zero (e o \"w\" do C)",
                erroEsp is null && esp == "dois\n", erroEsp ?? esp);
            Conferir("sd: na ESP32 o FILE_APPEND escreve no fim",
                erroAppend is null && anexado == "um\ndois\n", erroAppend ?? anexado);
        }

        // ---- na ESP32 o caminho comeca com "/" ----
        {
            var (maq, erro) = Rodar("""
                Serial.begin(115200);
                SD.begin(5);
                File f = SD.open("dados.txt", FILE_WRITE);
                if (!f) Serial.println("nao abriu");
                """);
            var linhas = LinhasDoSerial(maq);
            Conferir("sd: na ESP32, SD.open sem a barra na frente nao abre, e o aviso diz o porque",
                erro is null && linhas.Contains("nao abriu") && maq.Avisos.Any(a => a.Contains("\"/dados.txt\"")),
                erro ?? string.Join(" | ", linhas) + " / " + string.Join(" / ", maq.Avisos));

            var (noUno, erroUno) = Rodar("""
                Serial.begin(115200);
                SD.begin(5);
                File f = SD.open("dados.txt", FILE_WRITE);
                if (f) Serial.println("abriu");
                """, PerfilPlaca.De("arduino-uno"));
            Conferir("sd: no Uno a barra e opcional",
                erroUno is null && LinhasDoSerial(noUno).Contains("abriu"), erroUno ?? string.Join(" | ", LinhasDoSerial(noUno)));
        }

        // ---- remove apaga ----
        {
            var (maq, erro) = Rodar("""
                SD.begin(5);
                File a = SD.open("/x.txt", FILE_WRITE);
                a.println("oi");
                a.close();
                SD.remove("/x.txt");
                """);
            Conferir("sd: remove() apaga o arquivo",
                erro is null && !maq.Cartao.Existe("x.txt"), erro);
        }

        // ---- o aluno e avisado de onde os arquivos estao ----
        {
            var (maq, _) = Rodar("SD.begin(5);");
            saida.AppendLine($"        avisos: {string.Join(" / ", maq.Avisos)}");
            Conferir("sd: avisa onde os arquivos ficam",
                maq.Avisos.Any(a => a.Contains("fica na peca do cartao")),
                string.Join(" / ", maq.Avisos));
        }

        // ---- o reset da placa nao apaga o cartao, que e outro chip ----
        {
            var (maq, _) = Rodar("""
                SD.begin(5);
                File a = SD.open("/y.txt", FILE_WRITE);
                a.println("oi");
                a.close();
                """);
            bool antes = maq.Cartao.Existe("y.txt");
            maq.Reiniciar();
            bool depois = maq.Cartao.Existe("y.txt");

            saida.AppendLine($"        antes de reiniciar: {antes}, depois: {depois}, montado: {maq.Cartao.Montado}");
            Conferir("sd: reiniciar a placa nao apaga o cartao (ele e outro chip)", antes && depois,
                $"{antes} -> {depois}");
            Conferir("sd: depois do reset o cartao precisa do SD.begin() de novo", !maq.Cartao.Montado,
                "continuou montado");
        }
    }

    /// <summary>
    /// O display TFT ILI9341.
    ///
    /// O ultimo dos sete. Era o pior dos sete, alias: as chamadas eram
    /// aceitas em SILENCIO e devolviam "true", entao o sketch parecia
    /// funcionar e a peca continuava um retangulo preto -- nada indicava
    /// que faltava alguma coisa.
    ///
    /// Conferir pixel e o unico jeito honesto de testar desenho: se
    /// drawLine so incrementasse um contador, o teste passaria com a
    /// linha saindo no lugar errado.
    /// </summary>
    static void Tela_Tft()
    {
        saida.AppendLine("display TFT ILI9341");

        const int Preto = 0x0000, Vermelho = 0xF800, Verde = 0x07E0;
        const int Azul = 0x001F, Branco = 0xFFFF;

        static (Maquina maq, string erro) Rodar(string corpo)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            try
            {
                var prog = new Parser(Lexer.Tokenizar(
                    "Adafruit_ILI9341 tft(15, 2);" + Environment.NewLine
                    + "void setup() {" + corpo + "}" + Environment.NewLine
                    + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
                return (maq, null);
            }
            catch (Exception e) { return (maq, e.Message); }
        }

        // ---- a tela existe e tem o tamanho do ILI9341 ----
        {
            var (maq, erro) = Rodar("tft.begin();");
            saida.AppendLine($"        tela: {maq.Tela?.Largura}x{maq.Tela?.Altura}");
            Conferir("tft: begin() cria uma tela de 240x320",
                erro is null && maq.Tela is { Largura: 240, Altura: 320 }, erro);
        }

        // ---- fillScreen pinta tudo ----
        {
            var (maq, erro) = Rodar($"tft.begin(); tft.fillScreen({Azul});");
            int esperado = TelaGrafica.DeRgb565(Azul);
            Conferir("tft: fillScreen pinta o canto e o meio",
                erro is null && maq.Tela.CorEm(0, 0) == esperado
                && maq.Tela.CorEm(120, 160) == esperado
                && maq.Tela.CorEm(239, 319) == esperado, erro);
        }

        // ---- a linha cai onde foi pedida, e nao ao lado ----
        {
            var (maq, erro) = Rodar($"""
                tft.begin();
                tft.fillScreen({Preto});
                tft.drawLine(10, 20, 100, 20, {Vermelho});
                """);
            int vermelho = TelaGrafica.DeRgb565(Vermelho);
            int preto = TelaGrafica.DeRgb565(Preto);

            saida.AppendLine($"        linha horizontal em y=20: "
                             + $"(10;20)={maq.Tela.CorEm(10, 20) == vermelho}, "
                             + $"(55;20)={maq.Tela.CorEm(55, 20) == vermelho}, "
                             + $"(55;21)={maq.Tela.CorEm(55, 21) == preto}");

            Conferir("tft: a linha cai exatamente na altura pedida",
                erro is null
                && maq.Tela.CorEm(10, 20) == vermelho
                && maq.Tela.CorEm(55, 20) == vermelho
                && maq.Tela.CorEm(100, 20) == vermelho
                && maq.Tela.CorEm(55, 21) == preto
                && maq.Tela.CorEm(9, 20) == preto, erro);
        }

        // ---- linha na diagonal nao deixa buraco ----
        {
            var (maq, erro) = Rodar($"""
                tft.begin();
                tft.fillScreen({Preto});
                tft.drawLine(0, 0, 50, 50, {Branco});
                """);
            int branco = TelaGrafica.DeRgb565(Branco);
            bool cheia = Enumerable.Range(0, 51).All(i => maq.Tela.CorEm(i, i) == branco);
            Conferir("tft: a diagonal nao tem buraco", erro is null && cheia, erro);
        }

        // ---- retangulo vazado tem borda e nao tem miolo ----
        {
            var (maq, erro) = Rodar($"""
                tft.begin();
                tft.fillScreen({Preto});
                tft.drawRect(20, 30, 40, 50, {Verde});
                """);
            int verde = TelaGrafica.DeRgb565(Verde);
            int preto = TelaGrafica.DeRgb565(Preto);

            Conferir("tft: drawRect desenha so a borda",
                erro is null
                && maq.Tela.CorEm(20, 30) == verde
                && maq.Tela.CorEm(59, 79) == verde
                && maq.Tela.CorEm(40, 30) == verde
                && maq.Tela.CorEm(40, 55) == preto, erro);
        }

        // ---- fillRect enche ----
        {
            var (maq, erro) = Rodar($"""
                tft.begin();
                tft.fillScreen({Preto});
                tft.fillRect(20, 30, 40, 50, {Verde});
                """);
            int verde = TelaGrafica.DeRgb565(Verde);
            Conferir("tft: fillRect enche o miolo",
                erro is null && maq.Tela.CorEm(40, 55) == verde, erro);
        }

        // ---- circulo: a borda pega, o centro nao ----
        {
            var (maq, erro) = Rodar($"""
                tft.begin();
                tft.fillScreen({Preto});
                tft.drawCircle(120, 160, 30, {Branco});
                """);
            int branco = TelaGrafica.DeRgb565(Branco);
            int preto = TelaGrafica.DeRgb565(Preto);
            Conferir("tft: drawCircle desenha a borda e nao o miolo",
                erro is null
                && maq.Tela.CorEm(150, 160) == branco
                && maq.Tela.CorEm(120, 130) == branco
                && maq.Tela.CorEm(120, 160) == preto, erro);
        }

        // ---- desenhar fora da tela nao estoura ----
        {
            var (maq, erro) = Rodar($"""
                tft.begin();
                tft.drawPixel(-5, -5, {Branco});
                tft.drawPixel(9999, 9999, {Branco});
                tft.fillRect(200, 300, 500, 500, {Vermelho});
                tft.drawLine(-50, -50, 300, 400, {Verde});
                """);
            saida.AppendLine($"        fora da tela: {erro ?? "sem erro"}");
            Conferir("tft: desenhar fora da tela nao estoura", erro is null, erro);
        }

        // ---- texto: guardado com posicao, cor e tamanho ----
        {
            var (maq, erro) = Rodar($"""
                tft.begin();
                tft.fillScreen({Preto});
                tft.setCursor(10, 40);
                tft.setTextColor({Branco});
                tft.setTextSize(2);
                tft.println("Temp: 25C");
                tft.print("ok");
                """);

            var t = maq.Tela;
            saida.AppendLine($"        textos: "
                + string.Join(" | ", t.Textos.Select(x => $"({x.X};{x.Y}) '{x.Texto}' t{x.Tamanho}")));

            Conferir("tft: o texto guarda posicao, cor e tamanho",
                erro is null && t.Textos.Count == 2
                && t.Textos[0].Texto == "Temp: 25C"
                && t.Textos[0].X == 10 && t.Textos[0].Y == 40
                && t.Textos[0].Tamanho == 2
                && t.Textos[0].Cor == TelaGrafica.DeRgb565(Branco),
                erro ?? $"{t.Textos.Count} textos");

            // println desce a linha; print continua na mesma.
            Conferir("tft: println desce a linha e print continua nela",
                erro is null && t.Textos.Count == 2
                && t.Textos[1].Y == 40 + 8 * 2 && t.Textos[1].X == 0,
                erro ?? $"segundo em ({t.Textos[1].X};{t.Textos[1].Y})");
        }

        // ---- fillScreen apaga o texto junto ----
        {
            var (maq, erro) = Rodar($"""
                tft.begin();
                tft.setCursor(0, 0);
                tft.println("some");
                tft.fillScreen({Azul});
                """);
            Conferir("tft: fillScreen apaga o texto tambem",
                erro is null && maq.Tela.Textos.Count == 0,
                erro ?? $"{maq.Tela.Textos.Count} sobraram");
        }

        // ---- reiniciar a placa apaga a tela ----
        {
            var (maq, erro) = Rodar($"tft.begin(); tft.fillScreen({Vermelho});");
            int antes = maq.Tela.CorEm(0, 0);
            maq.Reiniciar();
            int depois = maq.Tela.CorEm(0, 0);

            Conferir("tft: reiniciar a placa apaga a tela",
                erro is null && antes == TelaGrafica.DeRgb565(Vermelho)
                && depois == TelaGrafica.DeRgb565(0), erro);
        }

        // ---- a tela deitada ----
        //
        // setRotation era aceito e guardado, e nao virava nada. Agora a
        // tela deita: width() e height() trocam de lugar, e o ponto que
        // o sketch pede cai no lugar certo do painel.
        {
            var (maq, erro) = Rodar($"""
                Serial.begin(115200);
                tft.begin();
                tft.setRotation(1);
                Serial.println(tft.width());
                Serial.println(tft.height());
                tft.fillScreen({Preto});
                tft.fillRect(0, 0, 40, 20, {Vermelho});
                """);

            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            saida.AppendLine($"        deitada: width/height = {string.Join("x", linhas)}");

            Conferir("tft: deitada, width e height trocam de lugar",
                erro is null && linhas.Count == 2
                && linhas[0] == "320" && linhas[1] == "240",
                erro ?? string.Join("x", linhas));

            int vermelho = TelaGrafica.DeRgb565(Vermelho);
            int preto = TelaGrafica.DeRgb565(Preto);

            // Do ponto de vista do sketch, o retangulo esta no canto.
            Conferir("tft: deitada, o desenho cai onde o sketch pediu",
                erro is null
                && maq.Tela.CorEm(0, 0) == vermelho
                && maq.Tela.CorEm(39, 19) == vermelho
                && maq.Tela.CorEm(41, 19) == preto, erro);

            // E no PAINEL ele saiu girado: o canto (0,0) do sketch foi
            // parar no canto de cima a direita dos pixels de verdade.
            var (px, py) = maq.Tela.NoPainel(0, 0);
            saida.AppendLine($"        (0;0) do sketch caiu em ({px};{py}) do painel");
            Conferir("tft: deitada, o painel continua em pe",
                erro is null && maq.Tela.LarguraFisica == 240
                && maq.Tela.AlturaFisica == 320 && px == 239 && py == 0,
                $"({px};{py})");
        }

        // ---- desenhar fora da tela deitada ----
        {
            var (maq, erro) = Rodar($"""
                tft.begin();
                tft.setRotation(1);
                tft.fillScreen({Preto});
                tft.drawPixel(300, 10, {Branco});
                """);
            // 300 e valido deitada (largura 320) e seria cortado em pe.
            Conferir("tft: deitada, x=300 e valido",
                erro is null && maq.Tela.CorEm(300, 10) == TelaGrafica.DeRgb565(Branco),
                erro);
        }

        // ---- de volta em pe ----
        {
            var (maq, erro) = Rodar($"""
                Serial.begin(115200);
                tft.begin();
                tft.setRotation(1);
                tft.setRotation(0);
                Serial.println(tft.width());
                """);
            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            Conferir("tft: voltar para 0 poe a tela em pe de novo",
                erro is null && linhas.Count == 1 && linhas[0] == "240",
                erro ?? string.Join(",", linhas));
        }

        // ---- o texto guarda para que lado a tela estava ----
        {
            var (maq, erro) = Rodar($"""
                tft.begin();
                tft.setRotation(3);
                tft.setCursor(10, 10);
                tft.println("deitado");
                """);
            Conferir("tft: o texto guarda a rotacao para o editor virar",
                erro is null && maq.Tela.Textos.Count == 1
                && maq.Tela.Textos[0].Rotacao == 3,
                erro ?? $"{maq.Tela.Textos.Count} textos");
        }

        // ---- a tela chega no EDITOR, e nao so no buffer ----
        //
        // O buffer certo e a tela preta sao coisas diferentes: ja
        // aconteceu de tudo passar aqui e a peca continuar preta na
        // janela.
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var display = new PecaNoCircuito { Tipo = "ili9341", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, display]);

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var prog = new Parser(Lexer.Tokenizar("""
                Adafruit_ILI9341 tft(15, 2);
                void setup() {
                  tft.begin();
                  tft.fillScreen(0x001F);
                  tft.setCursor(5, 5);
                  tft.println("oi");
                }
                void loop() {}
                """)).AnalisarPrograma();
            var interp = new Interpretador(prog, maq);
            interp.Preparar();
            interp.Setup();

            var rede = new Netlist(c);
            ed.RefletirEstado(maq, rede);

            var estado = ed.TftParaTeste(display.Id);
            saida.AppendLine($"        no editor: criada={estado.criada} "
                             + $"imagem={estado.comImagem} {estado.largura}x{estado.altura} "
                             + $"pontos brancos={estado.brancos}");

            Conferir("tft: a peca ganha uma tela no editor", estado.criada, "nao criou");
            Conferir("tft: o desenho chega na tela do editor",
                estado.comImagem && estado.largura == 240 && estado.altura == 320,
                $"{estado.largura}x{estado.altura}");
            // "oi" na fonte 5x7: o texto e pixel, e chega no vidro com o
            // resto do desenho.
            Conferir("tft: o texto chega na tela do editor, em pixels", estado.brancos > 10,
                $"{estado.brancos} pontos brancos");
        }

        // ---- width/height sao os que o sketch usa para centralizar ----
        {
            var (maq, erro) = Rodar("""
                Serial.begin(115200);
                tft.begin();
                Serial.println(tft.width());
                Serial.println(tft.height());
                """);
            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            Conferir("tft: width() e height() respondem",
                erro is null && linhas.Count == 2
                && linhas[0] == "240" && linhas[1] == "320",
                erro ?? string.Join(",", linhas));
        }
    }

    /// <summary>
    /// Duas placas, dois programas, ao mesmo tempo.
    ///
    /// Ate aqui o simulador tinha UMA maquina. Com duas placas na tela,
    /// o "D14" de qualquer uma acionava a mesma saida e um so programa
    /// rodava -- o aviso existia, mas era so um aviso.
    ///
    /// O teste que importa e o do MEIO: o mesmo numero de GPIO nas duas
    /// placas, com valores diferentes. E o caso que a maquina unica
    /// errava, e o que nao aparece em nenhum circuito de uma placa so.
    /// </summary>
    static void Bancada_DuasPlacas()
    {
        saida.AppendLine("duas placas ao mesmo tempo");

        // Duas ESP32, um LED em cada, as duas no mesmo GPIO 14.
        var c = new Circuito();
        var placaA = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        var ledA = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0 };
        var placaB = new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 500 };
        var ledB = new PecaNoCircuito { Tipo = "led", X = 400, Y = 500 };
        c.Pecas.AddRange([placaA, ledA, placaB, ledB]);

        c.Fios.Add(new Fio { DePeca = placaA.Id, DePino = "D14",
                             ParaPeca = ledA.Id, ParaPino = "A" });
        c.Fios.Add(new Fio { DePeca = ledA.Id, DePino = "C",
                             ParaPeca = placaA.Id, ParaPino = "GND.2" });
        c.Fios.Add(new Fio { DePeca = placaB.Id, DePino = "13",
                             ParaPeca = ledB.Id, ParaPino = "A" });
        c.Fios.Add(new Fio { DePeca = ledB.Id, DePino = "C",
                             ParaPeca = placaB.Id, ParaPino = "GND.1" });

        var rede = new Netlist(c);

        // ---- o netlist diz de QUAL placa e cada pino ----
        var deA = rede.PinoDe(c, ledA.Id, "A");
        var deB = rede.PinoDe(c, ledB.Id, "A");
        saida.AppendLine($"        LED A -> placa {(deA.Placa == placaA.Id ? "A" : "?")} "
                         + $"gpio {deA.Gpio}; LED B -> placa "
                         + $"{(deB.Placa == placaB.Id ? "B" : "?")} gpio {deB.Gpio}");

        Conferir("bancada: o netlist diz de qual placa e cada pino",
            deA.Placa == placaA.Id && deB.Placa == placaB.Id,
            $"{deA.Placa} e {deB.Placa}");

        // ---- uma maquina por placa, com o perfil de cada uma ----
        var bancada = new Bancada();
        bancada.Sincronizar(c);

        saida.AppendLine($"        maquinas: {bancada.Quantas}; "
                         + $"perfis: {bancada.De(placaA.Id).Perfil.Nome} e "
                         + $"{bancada.De(placaB.Id).Perfil.Nome}");

        Conferir("bancada: uma maquina por placa", bancada.Quantas == 2,
            $"{bancada.Quantas}");
        Conferir("bancada: cada maquina com o perfil da sua placa",
            bancada.De(placaA.Id).Perfil.Nome.Contains("ESP32")
            && bancada.De(placaB.Id).Perfil.Nome.Contains("Uno"),
            $"{bancada.De(placaA.Id).Perfil.Nome} / {bancada.De(placaB.Id).Perfil.Nome}");

        // ---- o mesmo GPIO nas duas, com valores diferentes ----
        //
        // O 14 da ESP32 aceso e o 14 do Uno apagado. Com uma maquina so,
        // um dos dois LEDs mostrava o estado do outro.
        {
            bancada.Reiniciar();
            var mA = bancada.De(placaA.Id);
            var mB = bancada.De(placaB.Id);

            mA.Modo[14] = 1; mA.DigitalWrite(14, 1);
            mB.Modo[14] = 1; mB.DigitalWrite(14, 0);
            mB.Modo[13] = 1; mB.DigitalWrite(13, 1);

            var ed = new EditorCircuito();
            ed.Carregar(c);
            ed.RefletirEstado(bancada, rede);

            saida.AppendLine($"        GPIO 14: ESP32={mA.Pwm[14]}, Uno={mB.Pwm[14]}");

            Conferir("bancada: o GPIO 14 de uma nao e o da outra",
                mA.Pwm[14] == 255 && mB.Pwm[14] == 0,
                $"{mA.Pwm[14]} e {mB.Pwm[14]}");

            // E o desenho tem que seguir a placa certa: o LED da B esta
            // no 13 dela, que esta aceso.
            Conferir("bancada: cada LED obedece a placa em que esta ligado",
                ed.BrilhoParaTeste(ledA.Id) > 0 && ed.BrilhoParaTeste(ledB.Id) > 0,
                $"A={ed.BrilhoParaTeste(ledA.Id)}, B={ed.BrilhoParaTeste(ledB.Id)}");

            // Apagar SO a A tem que apagar so o LED da A.
            mA.DigitalWrite(14, 0);
            ed.RefletirEstado(bancada, rede);
            saida.AppendLine($"        depois de apagar a A: "
                             + $"A={ed.BrilhoParaTeste(ledA.Id)}, "
                             + $"B={ed.BrilhoParaTeste(ledB.Id)}");

            Conferir("bancada: apagar uma placa nao apaga o LED da outra",
                ed.BrilhoParaTeste(ledA.Id) == 0 && ed.BrilhoParaTeste(ledB.Id) > 0,
                $"A={ed.BrilhoParaTeste(ledA.Id)}, B={ed.BrilhoParaTeste(ledB.Id)}");
        }

        // ---- cada placa tem o seu monitor serial ----
        {
            bancada.Reiniciar();
            var mA = bancada.De(placaA.Id);
            var mB = bancada.De(placaB.Id);
            mA.EscreverSerial("sou a ESP32");
            mB.EscreverSerial("sou o Uno");

            Conferir("bancada: o monitor de uma nao mistura com o da outra",
                mA.Serial.Count == 1 && mB.Serial.Count == 1
                && mA.Serial[0].Contains("ESP32") && mB.Serial[0].Contains("Uno"),
                $"{mA.Serial.Count} e {mB.Serial.Count}");
        }

        // ---- tirar uma placa do circuito descarta a maquina dela ----
        {
            c.Pecas.Remove(placaB);
            bancada.Sincronizar(c);
            Conferir("bancada: tirar a placa descarta a maquina dela",
                bancada.Quantas == 1, $"{bancada.Quantas}");
            c.Pecas.Add(placaB);
            bancada.Sincronizar(c);
        }

        // ---- peca solta nao acende: nao ha placa para mandar nela ----
        {
            var solto = new Circuito();
            var led = new PecaNoCircuito { Tipo = "led", X = 0, Y = 0 };
            solto.Pecas.Add(led);

            var b2 = new Bancada();
            b2.Sincronizar(solto);

            var ed = new EditorCircuito();
            ed.Carregar(solto);
            ed.RefletirEstado(b2, new Netlist(solto));

            Conferir("bancada: LED sem placa nenhuma fica apagado",
                ed.BrilhoParaTeste(led.Id) == 0, $"{ed.BrilhoParaTeste(led.Id)}");
        }
    }

    /// <summary>
    /// Os incomodos que sobraram na lista, cada um com a sua prova.
    ///
    /// Nao sao recursos novos: sao limites que estavam escritos no
    /// README como "incomoda, nao impede". Cada um vira teste aqui para
    /// nao voltar calado.
    /// </summary>
    static void Incomodos_Resolvidos()
    {
        saida.AppendLine("incomodos da lista");

        // ---- interrupcao ATENDIDA ENTRE COMANDOS ----
        //
        // Antes so entre voltas do loop(). Um sketch que fica preso num
        // while dentro do loop nunca atendia nada -- e esperar num while
        // e exatamente o que se faz enquanto se espera um botao.
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();

            var prog = new Parser(Lexer.Tokenizar("""
                volatile int cliques = 0;
                void contar() { cliques++; }
                void setup() {
                  Serial.begin(115200);
                  pinMode(4, INPUT_PULLUP);
                  attachInterrupt(4, contar, FALLING);
                }
                void loop() {
                  int voltas = 0;
                  while (cliques == 0 && voltas < 500) { voltas++; }
                  Serial.println(cliques);
                }
                """)).AnalisarPrograma();

            var interp = new Interpretador(prog, maq);
            interp.Preparar();
            interp.Setup();

            // O botao e apertado ANTES da volta: durante o while, a
            // interrupcao tem que ser atendida e soltar o laco.
            maq.Pressionado[4] = true;
            interp.Loop();

            var linhas = maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l).ToList();
            saida.AppendLine($"        preso num while: {string.Join(",", linhas)}");

            Conferir("irq: atende no meio de um while, sem voltar ao loop",
                linhas.Count == 1 && linhas[0] == "1", string.Join(",", linhas));
        }

        // ---- sketch sem interrupcao nao paga por isso ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var prog = new Parser(Lexer.Tokenizar("""
                void setup() { Serial.begin(115200); }
                void loop() { int n = 0; while (n < 2000) { n++; } Serial.println(n); }
                """)).AnalisarPrograma();
            var interp = new Interpretador(prog, maq);
            interp.Preparar();
            interp.Setup();

            var relogio = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) interp.Loop();
            relogio.Stop();

            saida.AppendLine($"        20 voltas de 2000 passos: {relogio.ElapsedMilliseconds} ms");
            Conferir("irq: sketch sem interrupcao nao fica mais lento",
                relogio.ElapsedMilliseconds < 700, $"{relogio.ElapsedMilliseconds} ms");
        }

        // ---- peca entre duas placas e denunciada ----
        {
            var c = new Circuito();
            var a = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var b = new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 600 };
            var botao = new PecaNoCircuito { Tipo = "pushbutton", X = 500, Y = 300 };
            var led = new PecaNoCircuito { Tipo = "led", X = 900, Y = 0 };
            c.Pecas.AddRange([a, b, botao, led]);

            // O botao fica entre as duas placas -- uma perna em cada.
            c.Fios.Add(new Fio { DePeca = a.Id, DePino = "D14",
                                 ParaPeca = botao.Id, ParaPino = "1.l" });
            c.Fios.Add(new Fio { DePeca = b.Id, DePino = "7",
                                 ParaPeca = botao.Id, ParaPino = "2.l" });

            // O LED fica so na primeira: nao pode ser acusado de nada.
            c.Fios.Add(new Fio { DePeca = a.Id, DePino = "D12",
                                 ParaPeca = led.Id, ParaPino = "A" });
            c.Fios.Add(new Fio { DePeca = led.Id, DePino = "C",
                                 ParaPeca = a.Id, ParaPino = "GND.2" });

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var achados = ed.PecasEntreDuasPlacas(new Netlist(c)).ToList();

            saida.AppendLine($"        entre duas placas: {achados.Count} peça(s)");

            Conferir("duas placas: a peca entre as duas e denunciada",
                achados.Count == 1 && achados[0].Peca == botao.Id
                && achados[0].Placas.Length == 2,
                $"{achados.Count} achados");
        }

        // ---- GND compartilhado NAO conta como estar em duas placas ----
        //
        // Ligar o terra das duas placas e o certo a fazer num circuito
        // com duas, e acusar isso viraria um aviso que ninguem le.
        {
            var c = new Circuito();
            var a = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var b = new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 600 };
            var led = new PecaNoCircuito { Tipo = "led", X = 500, Y = 0 };
            c.Pecas.AddRange([a, b, led]);

            c.Fios.Add(new Fio { DePeca = a.Id, DePino = "GND.2",
                                 ParaPeca = b.Id, ParaPino = "GND.1" });
            c.Fios.Add(new Fio { DePeca = a.Id, DePino = "D14",
                                 ParaPeca = led.Id, ParaPino = "A" });
            c.Fios.Add(new Fio { DePeca = led.Id, DePino = "C",
                                 ParaPeca = b.Id, ParaPino = "GND.2" });

            var ed = new EditorCircuito();
            ed.Carregar(c);
            var achados = ed.PecasEntreDuasPlacas(new Netlist(c)).ToList();

            saida.AppendLine($"        com o GND comum: {achados.Count} peça(s) acusada(s)");
            Conferir("duas placas: terra compartilhado nao vira acusacao",
                achados.Count == 0,
                string.Join(", ", achados.Select(x => x.Peca)));
        }

        // ---- o monitor junta as duas placas, com o nome de cada uma ----
        {
            var janela = new MainWindow();
            var c = new Circuito();
            var esp = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var uno = new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 600 };
            c.Pecas.AddRange([esp, uno]);
            janela.TesteAbrir(c, "void setup() {} void loop() {}");

            janela.TesteFalarNoSerial(esp.Id, "sou a ESP32");
            janela.TesteFalarNoSerial(uno.Id, "sou o Uno");
            janela.TesteAtualizarSerial();

            string painel = janela.TesteSerial;
            saida.AppendLine("        painel: " + painel.Replace(Environment.NewLine, " | "));

            Conferir("monitor: mostra as duas placas de uma vez",
                painel.Contains("sou a ESP32") && painel.Contains("sou o Uno"),
                painel);

            Conferir("monitor: cada linha diz de que placa veio",
                painel.Contains("[ESP32]") && painel.Contains("[Uno]"), painel);
        }

        // ---- com uma placa so, nada de prefixo ----
        {
            var janela = new MainWindow();
            var c = new Circuito();
            var esp = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            c.Pecas.Add(esp);
            janela.TesteAbrir(c, "void setup() {} void loop() {}");

            janela.TesteFalarNoSerial(esp.Id, "sozinha");
            janela.TesteAtualizarSerial();

            string painel = janela.TesteSerial;
            saida.AppendLine($"        com uma placa: \"{painel}\"");
            Conferir("monitor: com uma placa so, sem prefixo nenhum",
                painel.Contains("sozinha") && !painel.Contains("["), painel);
        }
    }

    /// <summary>
    /// Escolher qual placa manda na peca ligada em duas.
    ///
    /// Ela obedecia a primeira em silencio; depois passou a avisar; agora
    /// a pessoa escolhe e o aviso some. O que o teste cobra e o EFEITO --
    /// o LED passa a seguir a outra placa --, e nao so que o campo mudou.
    /// </summary>
    static void Duas_QuemManda()
    {
        saida.AppendLine("qual placa manda");

        // Um LED entre duas placas: anodo no GPIO 14 da ESP32, catodo no
        // 7 do Uno. As duas maquinas com o GPIO 14 em estados opostos,
        // para a escolha aparecer no brilho.
        static (Circuito c, PecaNoCircuito esp, PecaNoCircuito uno,
                PecaNoCircuito led, EditorCircuito ed, Bancada b) Bancada2()
        {
            var c = new Circuito();
            var esp = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var uno = new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 600 };
            var led = new PecaNoCircuito { Tipo = "led", X = 500, Y = 300 };
            c.Pecas.AddRange([esp, uno, led]);

            c.Fios.Add(new Fio { DePeca = esp.Id, DePino = "D14",
                                 ParaPeca = led.Id, ParaPino = "A" });
            c.Fios.Add(new Fio { DePeca = uno.Id, DePino = "7",
                                 ParaPeca = led.Id, ParaPino = "C" });

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var b = new Bancada();
            b.Sincronizar(c);
            b.Reiniciar();
            b.De(esp.Id).Modo[14] = 1; b.De(esp.Id).DigitalWrite(14, 1);
            b.De(uno.Id).Modo[14] = 1; b.De(uno.Id).DigitalWrite(14, 0);
            return (c, esp, uno, led, ed, b);
        }

        // ---- sem escolha: obedece a primeira e aparece no aviso ----
        {
            var (c, esp, uno, led, ed, b) = Bancada2();
            var rede = new Netlist(c);
            ed.RefletirEstado(b, rede);

            var placas = ed.PlacasDaPeca(rede, led);
            saida.AppendLine($"        o LED alcanca {placas.Count} placas; "
                             + $"sem escolha brilho={ed.BrilhoParaTeste(led.Id):0.00}");

            Conferir("quem manda: sem escolha, obedece a primeira",
                placas.Count == 2 && placas[0] == esp.Id && ed.BrilhoParaTeste(led.Id) > 0,
                $"{placas.Count} placas, brilho {ed.BrilhoParaTeste(led.Id)}");
            Conferir("quem manda: sem escolha, a peca e denunciada",
                ed.PecasEntreDuasPlacas(rede).Any(x => x.Peca == led.Id), "nao denunciou");
        }

        // ---- escolher o Uno: o LED passa a seguir o Uno ----
        {
            var (c, esp, uno, led, ed, b) = Bancada2();
            var rede = new Netlist(c);

            ed.SelecionarPorIndice(2);
            ed.DefinirPlacaQueManda(uno.Id);
            ed.RefletirEstado(b, rede);

            saida.AppendLine($"        escolhido o Uno: brilho={ed.BrilhoParaTeste(led.Id):0.00}");

            Conferir("quem manda: escolher a outra placa muda o LED de verdade",
                led.PlacaQueManda == uno.Id && ed.BrilhoParaTeste(led.Id) == 0,
                $"escolha {led.PlacaQueManda}, brilho {ed.BrilhoParaTeste(led.Id)}");
            Conferir("quem manda: depois de escolher, o aviso some",
                !ed.PecasEntreDuasPlacas(rede).Any(), "continua denunciada");
        }

        // ---- escolha velha (placa que a peca nao alcanca) e ignorada ----
        {
            var (c, esp, uno, led, ed, b) = Bancada2();
            led.PlacaQueManda = "placa-que-nao-existe";
            var rede = new Netlist(c);
            ed.RefletirEstado(b, rede);

            Conferir("quem manda: escolha que aponta para o nada volta ao automatico",
                ed.BrilhoParaTeste(led.Id) > 0
                && ed.PecasEntreDuasPlacas(rede).Any(x => x.Peca == led.Id),
                $"brilho {ed.BrilhoParaTeste(led.Id)}");
        }

        // ---- a escolha vai para o .sktch ----
        {
            var (c, esp, uno, led, ed, b) = Bancada2();
            led.PlacaQueManda = uno.Id;

            string tmp = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"quem-{Guid.NewGuid():N}.sktch");
            try
            {
                Sktch.Salvar(tmp, c, "void setup(){} void loop(){}");
                var (volta, _) = Sktch.Abrir(tmp);
                var ledVolta = volta.Pecas.First(x => x.Tipo == "led");
                Conferir("quem manda: a escolha sobrevive ao .sktch",
                    ledVolta.PlacaQueManda == uno.Id, ledVolta.PlacaQueManda);
            }
            finally
            {
                if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
            }
        }

        // ---- copiar e colar as tres: o LED colado obedece o Uno COLADO ----
        {
            var (c, esp, uno, led, ed, b) = Bancada2();
            led.PlacaQueManda = uno.Id;

            ed.SelecionarPorIndice(0);
            ed.SelecionarPorIndice(1, true);
            ed.SelecionarPorIndice(2, true);
            ed.Copiar();
            ed.Colar(null);

            var novas = c.Pecas.Skip(3).ToList();
            var ledNovo = novas.FirstOrDefault(x => x.Tipo == "led");
            var unoNovo = novas.FirstOrDefault(x => x.Tipo == "arduino-uno");

            saida.AppendLine($"        colado: LED novo manda={(ledNovo?.PlacaQueManda == unoNovo?.Id ? "Uno novo" : ledNovo?.PlacaQueManda == uno.Id ? "Uno ORIGINAL" : "?")}");

            Conferir("quem manda: colar as tres faz o LED novo obedecer o Uno novo",
                ledNovo is not null && unoNovo is not null
                && ledNovo.PlacaQueManda == unoNovo.Id,
                $"{ledNovo?.PlacaQueManda}");
        }
    }

    /// <summary>
    /// O erro tem que FICAR na barra de status.
    ///
    /// Achado pela captura de tela, nao por teste: ao abrir um circuito
    /// com um LED entre duas placas, a barra mostrava "placa: ESP32 ·
    /// ADC 0-4095". O erro era escrito e, uma linha depois, apagado pelo
    /// aviso do perfil da placa. O de fio em pino inexistente sofria do
    /// mesmo mal havia muito tempo, e nenhum teste via -- porque todos
    /// conferiam que o erro era DETECTADO, e nenhum que ele era MOSTRADO.
    /// </summary>
    static void Status_ErroNaoSome()
    {
        saida.AppendLine("o erro fica na barra");

        // ---- peca entre duas placas, ao abrir ----
        {
            var janela = new MainWindow();
            var c = new Circuito();
            var esp = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var uno = new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 600 };
            var led = new PecaNoCircuito { Tipo = "led", X = 500, Y = 300 };
            c.Pecas.AddRange([esp, uno, led]);
            c.Fios.Add(new Fio { DePeca = esp.Id, DePino = "D14",
                                 ParaPeca = led.Id, ParaPino = "A" });
            c.Fios.Add(new Fio { DePeca = uno.Id, DePino = "7",
                                 ParaPeca = led.Id, ParaPino = "C" });

            janela.TesteAbrir(c, "void setup() {} void loop() {}");
            string barra = janela.TesteStatus;
            saida.AppendLine($"        barra: {barra}");

            Conferir("status: o aviso de peca entre duas placas nao e apagado",
                barra.Contains("duas placas"), barra);
            Conferir("status: o aviso diz como resolver",
                barra.Contains("manda"), barra);
        }

        // ---- fio em pino inexistente, ao abrir ----
        {
            var janela = new MainWindow();
            var c = new Circuito();
            var esp = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var led = new PecaNoCircuito { Tipo = "led", X = 500, Y = 0 };
            c.Pecas.AddRange([esp, led]);
            // A ESP32 DevKit V1 nao tem D99.
            c.Fios.Add(new Fio { DePeca = esp.Id, DePino = "D99",
                                 ParaPeca = led.Id, ParaPino = "A" });

            janela.TesteAbrir(c, "void setup() {} void loop() {}");
            string barra = janela.TesteStatus;
            saida.AppendLine($"        barra: {barra}");

            Conferir("status: o erro de fio em pino inexistente nao e apagado",
                barra.Contains("pino inexistente"), barra);
        }

        // ---- sem erro, a informacao da placa continua aparecendo ----
        {
            var janela = new MainWindow();
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 0 });

            janela.TesteAbrir(c, "void setup() {} void loop() {}");
            string barra = janela.TesteStatus;
            saida.AppendLine($"        barra sem erro: {barra}");

            Conferir("status: sem erro, o perfil da placa aparece",
                barra.Contains("Uno") && barra.Contains("ADC"), barra);
        }
    }

    /// <summary>
    /// Interrupcao no meio da expressao.
    ///
    /// A conta "cont * 10 + digitalWrite(4, HIGH) + cont" le "cont" duas
    /// vezes, com a borda de subida acontecendo entre as duas leituras.
    /// No hardware a rotina roda assim que a borda chega, e a segunda
    /// leitura ja ve o valor novo -- e o motivo de o Arduino pedir
    /// "volatile". Atendendo so entre comandos, as duas leituras davam 0.
    ///
    /// Os outros dois casos sao o preco de cortar mais fundo: o contador
    /// de "loop preso" nao pode ser zerado pela rotina, e o sketch com
    /// interrupcao registrada nao pode ficar lento.
    /// </summary>
    static void Motor_InterrupcaoNaExpressao()
    {
        saida.AppendLine("interrupcao no meio da expressao");

        static List<string> Linhas(Maquina m)
            => [.. m.Serial.Select(l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l)];

        // ---- a borda no meio da conta ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            string erro = null;
            try
            {
                var prog = new Parser(Lexer.Tokenizar("""
                    volatile int cont = 0;
                    void contar() { cont++; }
                    void setup() {
                      Serial.begin(115200);
                      pinMode(4, OUTPUT);
                      digitalWrite(4, LOW);
                      attachInterrupt(4, contar, RISING);
                      int r = cont * 10 + digitalWrite(4, HIGH) + cont;
                      Serial.println(r);
                    }
                    void loop() {}
                    """)).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
            }
            catch (Exception e) { erro = e.Message; }

            var linhas = Linhas(maq);
            saida.AppendLine($"        cont*10 + borda + cont = {erro ?? string.Join(",", linhas)}");

            // 0*10 + 0 + 1: a primeira leitura e de antes da borda, a
            // segunda ja ve a rotina ter rodado.
            Conferir("irq: a borda no meio da conta muda a segunda leitura",
                erro is null && linhas.Count == 1 && linhas[0] == "1",
                erro ?? string.Join(",", linhas));
        }

        // ---- laco preso com a interrupcao pulsando ainda estoura ----
        //
        // A rotina zerava o contador de passos. Com ela cortando cada
        // expressao, um while(true) que pulsa o pino vigiado nunca
        // chegaria ao limite. Roda em outra tarefa com prazo: se o
        // defeito voltar, o teste falha em vez de travar a suite.
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var prog = new Parser(Lexer.Tokenizar("""
                volatile int cont = 0;
                void contar() { cont++; }
                void setup() { pinMode(4, OUTPUT); attachInterrupt(4, contar, RISING); }
                void loop() {
                  while (true) { digitalWrite(4, HIGH); digitalWrite(4, LOW); }
                }
                """)).AnalisarPrograma();
            var interp = new Interpretador(prog, maq) { LimitePassos = 20_000 };
            interp.Preparar();
            interp.Setup();

            string erro = null;
            var tarefa = System.Threading.Tasks.Task.Run(() =>
            {
                try { interp.Loop(); }
                catch (Exception e) { erro = e.Message; }
            });

            bool terminou = tarefa.Wait(TimeSpan.FromSeconds(10));
            if (!terminou)
            {
                maq.Parar = true;
                tarefa.Wait(TimeSpan.FromSeconds(5));
            }

            saida.AppendLine($"        laco pulsando: {(terminou ? erro ?? "terminou sem erro" : "TRAVOU")}");
            Conferir("irq: laco preso com interrupcao pulsando ainda estoura o limite",
                terminou && erro is not null && erro.Contains("loop preso"),
                terminou ? erro ?? "terminou sem erro" : "travou");
        }

        // ---- com interrupcao registrada, continua rapido ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var prog = new Parser(Lexer.Tokenizar("""
                volatile int cont = 0;
                void contar() { cont++; }
                void setup() { pinMode(4, INPUT_PULLUP); attachInterrupt(4, contar, FALLING); }
                void loop() { int n = 0; while (n < 2000) { n = n + 1; } }
                """)).AnalisarPrograma();
            var interp = new Interpretador(prog, maq);
            interp.Preparar();
            interp.Setup();

            var relogio = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) interp.Loop();
            relogio.Stop();

            saida.AppendLine($"        20 voltas com interrupcao registrada: {relogio.ElapsedMilliseconds} ms");
            Conferir("irq: com interrupcao registrada o sketch continua rapido",
                relogio.ElapsedMilliseconds < 1000, $"{relogio.ElapsedMilliseconds} ms");
        }
    }

    /// <summary>
    /// Borda que sobe e desce dentro de uma chamada so.
    ///
    /// A vigia comparava o nivel do pino entre um ponto e outro do
    /// programa. Um shiftOut no pino vigiado da oito pulsos e termina no
    /// nivel em que comecou -- comparando niveis, nenhum era visto. Agora
    /// a borda e anotada na escrita.
    /// </summary>
    static void Motor_BordaDentroDaChamada()
    {
        saida.AppendLine("borda dentro de uma chamada");

        static (List<string> linhas, string erro) Rodar(string corpo)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            try
            {
                var prog = new Parser(Lexer.Tokenizar(
                    "volatile int cont = 0;" + Environment.NewLine
                    + "void contar() { cont++; }" + Environment.NewLine
                    + "void setup() {" + corpo + "}" + Environment.NewLine
                    + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
            }
            catch (Exception e) { return (null, e.Message); }
            return ([.. maq.Serial.Select(
                l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l)], null);
        }

        // ---- os oito pulsos do shiftOut ----
        {
            var (linhas, erro) = Rodar("""
                Serial.begin(115200);
                pinMode(13, OUTPUT);
                pinMode(4, OUTPUT);
                attachInterrupt(4, contar, RISING);
                shiftOut(13, 4, MSBFIRST, 0);
                Serial.println(cont);
                """);
            saida.AppendLine($"        shiftOut no pino vigiado: {erro ?? string.Join(",", linhas)}");
            Conferir("borda: os oito pulsos de um shiftOut disparam oito vezes",
                erro is null && linhas.Count == 1 && linhas[0] == "8",
                erro ?? string.Join(",", linhas));
        }

        // ---- CHANGE conta subida e descida: dezesseis ----
        {
            var (linhas, erro) = Rodar("""
                Serial.begin(115200);
                pinMode(13, OUTPUT);
                pinMode(4, OUTPUT);
                attachInterrupt(4, contar, CHANGE);
                shiftOut(13, 4, MSBFIRST, 0);
                Serial.println(cont);
                """);
            Conferir("borda: CHANGE conta subida e descida dos oito pulsos",
                erro is null && linhas.Count == 1 && linhas[0] == "16",
                erro ?? string.Join(",", linhas));
        }

        // ---- escrita normal nao conta em dobro ----
        //
        // A escrita anota a borda e a comparacao de niveis nao pode ver a
        // mesma borda de novo depois.
        {
            var (linhas, erro) = Rodar("""
                Serial.begin(115200);
                pinMode(4, OUTPUT);
                digitalWrite(4, LOW);
                attachInterrupt(4, contar, RISING);
                digitalWrite(4, HIGH);
                Serial.println(cont);
                Serial.println(cont);
                """);
            saida.AppendLine($"        uma subida, lida duas vezes: {erro ?? string.Join(",", linhas)}");
            Conferir("borda: uma escrita dispara uma vez so",
                erro is null && linhas.Count == 2 && linhas[0] == "1" && linhas[1] == "1",
                erro ?? string.Join(",", linhas));
        }

        // ---- com interrupcoes desligadas, os pulsos nao voltam ----
        {
            var (linhas, erro) = Rodar("""
                Serial.begin(115200);
                pinMode(13, OUTPUT);
                pinMode(4, OUTPUT);
                attachInterrupt(4, contar, RISING);
                noInterrupts();
                shiftOut(13, 4, MSBFIRST, 0);
                interrupts();
                Serial.println(cont);
                """);
            Conferir("borda: pulsos com noInterrupts nao voltam depois",
                erro is null && linhas.Count == 1 && linhas[0] == "0",
                erro ?? string.Join(",", linhas));
        }

        // ---- pino que nao e vigiado nao entra na fila ----
        {
            var (linhas, erro) = Rodar("""
                Serial.begin(115200);
                pinMode(13, OUTPUT);
                pinMode(4, OUTPUT);
                attachInterrupt(4, contar, RISING);
                shiftOut(4, 13, MSBFIRST, 0);
                Serial.println(cont);
                """);
            // Dado 0 no pino 4 o tempo todo: o relogio (13) pulsa, o
            // pino vigiado nao sobe nenhuma vez.
            Conferir("borda: pulso em outro pino nao dispara nada",
                erro is null && linhas.Count == 1 && linhas[0] == "0",
                erro ?? string.Join(",", linhas));
        }
    }

    /// <summary>
    /// O depurador, no motor.
    ///
    /// Cada caso roda o sketch numa tarefa a parte, como a janela faz, e
    /// fala com ele pelo depurador. Todo prazo e curto e todo caso termina
    /// com Parar se algo sair errado: um depurador com defeito trava a
    /// thread do sketch, e isso nao pode travar a suite.
    /// </summary>
    static void Depurador_PassoAPasso()
    {
        saida.AppendLine("depurador");

        static (Maquina maq, Interpretador interp, Depurador dep,
                System.Collections.Concurrent.BlockingCollection<Depurador.Retrato> paradas)
            Montar(string codigo, bool tempoReal = false)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.TempoReal = tempoReal;
            maq.IniciarRelogio();

            var prog = new Parser(Lexer.Tokenizar(codigo)).AnalisarPrograma();
            var interp = new Interpretador(prog, maq);
            interp.Preparar();

            var dep = new Depurador(maq);
            var paradas = new System.Collections.Concurrent.BlockingCollection<Depurador.Retrato>();
            dep.Pausou += r => paradas.Add(r);
            interp.Depurador = dep;
            return (maq, interp, dep, paradas);
        }

        static System.Threading.Tasks.Task Rodar(Action acao, Action<string> anotarErro)
            => System.Threading.Tasks.Task.Run(() =>
            {
                try { acao(); }
                catch (Exception e) { anotarErro(e.GetType().Name + ": " + e.Message); }
            });

        // Se sobrar thread pausada por causa de um defeito, solta.
        static void Encerrar(Maquina maq, System.Threading.Tasks.Task tarefa)
        {
            if (tarefa.Wait(3000)) return;
            maq.Parar = true;
            tarefa.Wait(3000);
        }

        static List<string> Linhas(Maquina m)
            => [.. m.Serial.Select(l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l)];

        static string Valor(Depurador.Retrato r, string nome)
            => r?.Variaveis.FirstOrDefault(v => v.Nome == nome)?.Valor;

        const string Conta = """
            int total = 0;
            void setup() {
              int x = 5;
              total = x * 2;
              Serial.println(total);
            }
            void loop() {}
            """;

        // ---- para na linha marcada e mostra as variaveis ----
        {
            var (maq, interp, dep, paradas) = Montar(Conta);
            dep.DefinirPausas([4]);
            string erro = null;
            var tarefa = Rodar(interp.Setup, e => erro = e);

            bool parou = paradas.TryTake(out var r, 5000);
            saida.AppendLine($"        parou na linha {r?.Linha}: "
                + (r is null ? "-" : string.Join(", ", r.Variaveis.Select(
                    v => $"{v.Nome}={v.Valor}{(v.Global ? " (global)" : "")}"))));

            Conferir("depurador: para na linha marcada", parou && r.Linha == 4,
                parou ? $"linha {r.Linha}" : "nao parou");
            Conferir("depurador: mostra a variavel local com o valor dela",
                Valor(r, "x") == "5", Valor(r, "x") ?? "sem x");
            Conferir("depurador: mostra a global com o valor de antes da linha",
                Valor(r, "total") == "0", Valor(r, "total") ?? "sem total");
            Conferir("depurador: separa o que e global do que e local",
                parou && !r.Variaveis.First(v => v.Nome == "x").Global
                && r.Variaveis.First(v => v.Nome == "total").Global, "misturou");

            dep.Continuar();
            bool terminou = tarefa.Wait(5000);
            Conferir("depurador: continuar roda ate o fim",
                terminou && erro is null && Linhas(maq).LastOrDefault() == "10",
                terminou ? erro ?? string.Join(",", Linhas(maq)) : "nao terminou");
            Encerrar(maq, tarefa);
        }

        // ---- passo a passo ----
        {
            var (maq, interp, dep, paradas) = Montar(Conta);
            dep.DefinirPausas([3]);
            string erro = null;
            var tarefa = Rodar(interp.Setup, e => erro = e);

            paradas.TryTake(out var r1, 5000);
            bool xAntes = r1?.Variaveis.Any(v => v.Nome == "x") ?? false;
            dep.Passo();
            paradas.TryTake(out var r2, 5000);
            dep.Passo();
            paradas.TryTake(out var r3, 5000);
            dep.Continuar();
            tarefa.Wait(5000);

            saida.AppendLine($"        passo a passo: {r1?.Linha} -> {r2?.Linha} -> {r3?.Linha}");

            Conferir("depurador: o passo vai para a linha seguinte",
                r1?.Linha == 3 && r2?.Linha == 4 && r3?.Linha == 5,
                $"{r1?.Linha} {r2?.Linha} {r3?.Linha}");
            Conferir("depurador: a variavel so aparece depois que a linha dela roda",
                !xAntes && Valor(r2, "x") == "5", $"antes={xAntes}, depois={Valor(r2, "x")}");
            Conferir("depurador: o passo ve o valor que a linha anterior calculou",
                Valor(r3, "total") == "10", Valor(r3, "total"));
            Encerrar(maq, tarefa);
        }

        // ---- linha com comando aninhado nao para duas vezes ----
        {
            var (maq, interp, dep, paradas) = Montar("""
                int n = 0;
                void setup() {
                  if (n == 0) n = 1;
                  Serial.println(n);
                }
                void loop() {}
                """);
            dep.DefinirPausas([3]);
            string erro = null;
            var tarefa = Rodar(interp.Setup, e => erro = e);

            bool primeira = paradas.TryTake(out _, 5000);
            dep.Continuar();
            bool segunda = paradas.TryTake(out _, 700);
            if (segunda) dep.Continuar();
            tarefa.Wait(5000);

            Conferir("depurador: 'if (x) y = 1;' para uma vez so",
                primeira && !segunda && Linhas(maq).LastOrDefault() == "1",
                $"primeira={primeira}, segunda={segunda}");
            Encerrar(maq, tarefa);
        }

        // ---- a mesma linha para de novo a cada volta do loop ----
        {
            var (maq, interp, dep, paradas) = Montar("""
                int c = 0;
                void setup() {}
                void loop() {
                  c = c + 1;
                }
                """);
            dep.DefinirPausas([4]);
            string erro = null;
            var tarefa = Rodar(() => { interp.Loop(); interp.Loop(); interp.Loop(); },
                               e => erro = e);

            var valores = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                if (!paradas.TryTake(out var r, 5000)) break;
                valores.Add(Valor(r, "c"));
                dep.Continuar();
            }
            tarefa.Wait(5000);

            saida.AppendLine($"        tres voltas, c em cada parada: {string.Join(",", valores)}");
            Conferir("depurador: a linha do loop para a cada volta",
                valores.Count == 3 && valores[0] == "0" && valores[1] == "1" && valores[2] == "2",
                string.Join(",", valores));
            Encerrar(maq, tarefa);
        }

        // ---- pausar no meio de um delay, com o relogio congelado ----
        {
            var (maq, interp, dep, paradas) = Montar("""
                void setup() {
                  delay(1500);
                  Serial.println("fim");
                }
                void loop() {}
                """, tempoReal: true);
            string erro = null;
            var tarefa = Rodar(interp.Setup, e => erro = e);

            Thread.Sleep(150);
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            dep.Pausar();
            bool parou = paradas.TryTake(out var r, 1000);
            long demorou = relogio.ElapsedMilliseconds;

            long ms1 = maq.Ms;
            Thread.Sleep(400);
            long ms2 = maq.Ms;

            dep.Continuar();
            bool acabouNaHora = tarefa.Wait(400);
            bool acabou = acabouNaHora || tarefa.Wait(5000);

            saida.AppendLine($"        pausa pedida no delay: parou em {demorou} ms na linha {r?.Linha}; "
                             + $"relogio andou {ms2 - ms1} ms em 400 ms de pausa");

            Conferir("depurador: pausa no meio de um delay e atendida na hora",
                parou && demorou < 800 && r.Linha == 2,
                parou ? $"{demorou} ms, linha {r.Linha}" : "nao parou");
            Conferir("depurador: o relogio fica parado durante a pausa",
                parou && ms2 - ms1 < 60, $"andou {ms2 - ms1} ms");
            Conferir("depurador: ao continuar, o delay cumpre o que faltava",
                !acabouNaHora && acabou && Linhas(maq).LastOrDefault() == "fim",
                $"acabou logo={acabouNaHora}, acabou={acabou}");
            Encerrar(maq, tarefa);
        }

        // ---- parar a simulacao com o sketch pausado solta a thread ----
        {
            var (maq, interp, dep, paradas) = Montar(Conta);
            dep.DefinirPausas([4]);
            string erro = null;
            var tarefa = Rodar(interp.Setup, e => erro = e);

            paradas.TryTake(out _, 5000);
            maq.Parar = true;
            bool soltou = tarefa.Wait(2000);

            Conferir("depurador: parar com o sketch pausado solta a thread",
                soltou && erro is not null && erro.Contains("Interrompida"),
                soltou ? erro ?? "sem erro" : "ficou presa");
            Encerrar(maq, tarefa);
        }

        // ---- sem ponto marcado, o depurador nao pesa ----
        {
            var (maq, interp, dep, paradas) = Montar("""
                void setup() {}
                void loop() { int n = 0; while (n < 2000) { n = n + 1; } }
                """);
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) interp.Loop();
            relogio.Stop();

            saida.AppendLine($"        20 voltas com depurador e sem ponto: {relogio.ElapsedMilliseconds} ms");
            Conferir("depurador: ligado e sem ponto marcado, o sketch continua rapido",
                relogio.ElapsedMilliseconds < 1000, $"{relogio.ElapsedMilliseconds} ms");
        }
    }

    /// <summary>
    /// O depurador na janela: marcar, parar, painel, passo, continuar,
    /// pausar no meio e parar tudo com o sketch parado.
    ///
    /// Os avisos da thread do sketch chegam por BeginInvoke, entao o
    /// teste precisa deixar a fila da janela andar enquanto espera.
    /// </summary>
    static void Depurador_NaJanela()
    {
        saida.AppendLine("depurador na janela");

        static bool Esperar(Func<bool> condicao, int ms = 4000)
        {
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            while (relogio.ElapsedMilliseconds < ms)
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                if (condicao()) return true;
                Thread.Sleep(10);
            }
            return false;
        }

        var janela = new MainWindow();
        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 });
        janela.TesteAbrir(c, """
            int total = 0;
            void setup() {
              int x = 5;
              total = x * 2;
            }
            void loop() {
              total = total + 1;
              delay(20);
            }
            """);

        // ---- marcar e desmarcar, sem rodar ----
        janela.TesteMarcarPausa(7);
        janela.TesteMarcarPausa(7);
        janela.TesteMarcarPausa(4);
        string antes = janela.TesteDepurador();
        saida.AppendLine($"        antes de rodar: {antes}");
        Conferir("depurador na janela: clicar de novo desmarca",
            antes.Contains("pausas=[4]"), antes);
        Conferir("depurador na janela: sem rodar, pausar e passo ficam desligados",
            antes.Contains("pausar=False") && antes.Contains("passo=False"), antes);

        try
        {
            janela.TesteRodar();
            bool parou = Esperar(() => janela.TesteDepurador().Contains("pausada=4 "));
            string parado = janela.TesteDepurador();
            saida.AppendLine($"        parado: {parado}");

            Conferir("depurador na janela: para no ponto marcado", parou, parado);
            Conferir("depurador na janela: o painel de variaveis aparece",
                parado.Contains("painel=Visible"), parado);
            Conferir("depurador na janela: o painel mostra a local e a global",
                parado.Contains("x = 5") && parado.Contains("total = 0 (global)"), parado);
            Conferir("depurador na janela: o titulo diz a linha",
                parado.Contains("LINHA 4"), parado);
            Conferir("depurador na janela: parado, o botao vira continuar e o passo liga",
                parado.Contains("pausar=True:▶") && parado.Contains("passo=True"), parado);

            janela.TestePasso();
            bool andou = Esperar(() => janela.TesteDepurador().Contains("pausada=7 "));
            string noLoop = janela.TesteDepurador();
            saida.AppendLine($"        um passo: {noLoop}");
            Conferir("depurador na janela: o passo sai do setup e para na primeira linha do loop",
                andou && noLoop.Contains("total = 10"), noLoop);

            janela.TestePausarOuContinuar();
            bool soltou = Esperar(() => janela.TesteDepurador().Contains("pausada=-1 "));
            string rodando = janela.TesteDepurador();
            Conferir("depurador na janela: continuar esconde o painel e volta a rodar",
                soltou && rodando.Contains("painel=Collapsed")
                && rodando.Contains("pausar=True:❚❚") && rodando.Contains("passo=False"), rodando);

            janela.TestePausarOuContinuar();
            bool pausou = Esperar(() => System.Text.RegularExpressions.Regex.IsMatch(
                janela.TesteDepurador(), "pausada=[78] "));
            string noMeio = janela.TesteDepurador();
            saida.AppendLine($"        pausar no meio: {noMeio}");
            Conferir("depurador na janela: o botao pausar para onde o sketch estiver",
                pausou && noMeio.Contains("painel=Visible"), noMeio);

            janela.TesteParar();
            bool terminou = Esperar(() => !janela.TesteThreadViva, 3000);
            string depois = janela.TesteDepurador();
            saida.AppendLine($"        depois de parar: {depois}");
            Conferir("depurador na janela: parar com o sketch pausado solta tudo",
                terminou && depois.Contains("pausada=-1 ") && depois.Contains("painel=Collapsed")
                && depois.Contains("pausar=False") && depois.Contains("passo=False"), depois);
        }
        finally { janela.TesteParar(); }
    }

    // =====================================================================
    //  1.5: botoes das placas, char, entrada do serial e rede
    // =====================================================================

    /// <summary>Espera uma condicao deixando a fila da janela andar.</summary>
    static bool EsperarPor(Func<bool> condicao, int ms = 4000)
    {
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        while (relogio.ElapsedMilliseconds < ms)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            if (condicao()) return true;
            Thread.Sleep(10);
        }
        return condicao();
    }

    /// <summary>As linhas do monitor de uma placa, sem o carimbo de tempo.</summary>
    static List<string> LinhasDoSerial(Maquina m)
    {
        lock (m.Trava)
            return [.. m.Serial.Select(l => l.Contains("] ") ? l[(l.IndexOf("] ") + 2)..] : l)];
    }

    /// <summary>Uma placa pronta para rodar, ligada numa rede de teste.</summary>
    static (Maquina Maq, Interpretador Interp) MontarPlaca(
        Rede rede, string nome, string codigo, PerfilPlaca perfil = null)
    {
        var maq = new Maquina { Perfil = perfil ?? PerfilPlaca.Esp32 };
        maq.Reiniciar();
        maq.Nome = nome;
        maq.Rede = rede;

        var prog = new Parser(Lexer.Tokenizar(codigo)).AnalisarPrograma();
        var interp = new Interpretador(prog, maq);
        interp.Preparar();
        return (maq, interp);
    }

    /// <summary>
    /// O RESET de cada placa.
    ///
    /// Toda placa de desenvolvimento tem o botao soldado, e ate a 1.4 so o
    /// da ESP32 respondia: nas outras o desenho tinha o botao e o clique
    /// nao fazia nada.
    /// </summary>
    static void Botoes_DeTodasAsPlacas()
    {
        saida.AppendLine("botoes das placas");

        foreach (var (tipo, esperados) in new (string, string[])[]
                 {
                     ("esp32-devkit-v1", ["EN", "BOOT"]),
                     ("arduino-uno", ["RESET"]),
                     ("arduino-mega", ["RESET"]),
                     ("arduino-nano", ["RESET"]),
                     ("nano-rp2040-connect", ["RESET"]),
                     ("franzininho", ["RESET"]),
                 })
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = tipo, X = 120, Y = 80 };
            c.Pecas.Add(placa);
            var def = Catalogo.Obter(tipo);
            var ed = new EditorCircuito();
            ed.Carregar(c);

            var botoes = EditorCircuito.BotoesPorPlaca[tipo];
            var achados = new List<string>();
            foreach (var (bx, by, _, _) in botoes)
            {
                var acerto = ed.Resolver(Geometria.PosicaoLocal(placa, def, bx, by));
                if (acerto.Camada == EditorCircuito.Camada.BotaoPlaca) achados.Add(acerto.Botao);
            }

            saida.AppendLine($"        {tipo,-22} {string.Join(", ", achados)}");
            Conferir($"{tipo}: o clique acha {string.Join(" e ", esperados)}",
                achados.SequenceEqual(esperados), string.Join(",", achados));

            // Botao fora do desenho seria clique no vazio.
            Conferir($"{tipo}: o botao fica dentro do desenho",
                botoes.All(b => b.X >= 0 && b.X <= def.Largura && b.Y >= 0 && b.Y <= def.Altura),
                "fora da caixa da peca");
        }

        // ---- segurar o RESET para a placa; soltar faz ela comecar de novo ----
        {
            var janela = new MainWindow();
            var c = new Circuito();
            var uno = new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 0 };
            c.Pecas.Add(uno);

            janela.TesteAbrir(c, """
                int voltas = 0;
                void setup() {
                  Serial.begin(9600);
                  Serial.println("ligou");
                }
                void loop() {
                  voltas++;
                  Serial.println(voltas);
                  delay(30);
                }
                """);

            var maquina = janela.TesteMaquinaDa(uno.Id);
            var def = Catalogo.Obter(uno.Tipo);
            var (bx, by, _, _) = EditorCircuito.BotoesPorPlaca[uno.Tipo][0];
            var ponto = Geometria.PosicaoLocal(uno, def, bx, by);

            try
            {
                janela.TesteRodar();
                bool andou = EsperarPor(() => LinhasDoSerial(maquina).Count >= 3);
                Conferir("reset: a placa estava rodando antes", andou,
                    string.Join(",", LinhasDoSerial(maquina)));

                janela.TesteEditorCircuito.Apertar(ponto);
                bool parou = EsperarPor(() => !janela.TestePlacaRodando(uno.Id), 3000);
                int quandoParou = LinhasDoSerial(maquina).Count;
                Thread.Sleep(200);

                saida.AppendLine($"        segurando o RESET: parou={parou} linhas={quandoParou}");
                Conferir("reset: segurar o botao para a placa", parou, "continuou rodando");
                Conferir("reset: parada, a placa nao imprime mais nada",
                    LinhasDoSerial(maquina).Count == quandoParou, "imprimiu com o reset segurado");
                Conferir("reset: a placa fica sem energia enquanto o botao esta embaixo",
                    !maquina.Energizada, "continuou energizada");

                janela.TesteEditorCircuito.Soltar();
                bool voltou = EsperarPor(() => LinhasDoSerial(maquina).Contains("ligou")
                                               && LinhasDoSerial(maquina).Count >= 3, 4000);
                var depois = LinhasDoSerial(maquina);
                saida.AppendLine($"        depois de soltar: {string.Join(",", depois.Take(4))}");

                Conferir("reset: soltar faz a placa comecar do setup()", voltou, string.Join(",", depois));
                Conferir("reset: o contador do sketch volta do zero",
                    depois.Count > 1 && depois[0] == "ligou" && depois[1] == "1", string.Join(",", depois));
                Conferir("reset: a placa volta a rodar sozinha",
                    janela.TestePlacaRodando(uno.Id), "a thread nao voltou");
            }
            finally { janela.TesteParar(); }
        }
    }

    /// <summary>
    /// char de verdade, conversoes e as funcoes de texto do C.
    ///
    /// Sem isto, o exemplo de MQTT da aula ("msg += (char)payload[i]")
    /// montava "104101108108111" em vez de "hello", e o snprintf nem
    /// achava o vetor de destino.
    /// </summary>
    static void Motor_CharEConversoes()
    {
        saida.AppendLine("char e conversoes");

        static string Rodar(string corpo)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            try
            {
                var prog = new Parser(Lexer.Tokenizar(
                    "void setup() {" + Environment.NewLine + corpo + Environment.NewLine + "}"
                    + Environment.NewLine + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
            }
            catch (Exception e) { return "ERRO: " + e.Message; }
            return string.Join("|", LinhasDoSerial(maq));
        }

        (string nome, string corpo, string esperado)[] casos =
        [
            ("char imprime a letra",
             "char c = 'A'; Serial.println(c);", "A"),

            ("String + (char) monta texto",
             "String s = \"\"; for (int i = 0; i < 3; i++) { s += (char)(65 + i); } Serial.println(s);",
             "ABC"),

            ("char guardado depois continua char",
             "char c = 'a'; c = 'b'; Serial.println(c);", "b"),

            ("comparar char com literal",
             "char c = '1'; if (c == '1') { Serial.println(\"igual\"); }", "igual"),

            ("snprintf escreve no vetor",
             "char msg[50]; snprintf(msg, 50, \"n=%d %.2f %s\", 7, 3.14159, \"ok\"); Serial.println(msg);",
             "n=7 3.14 ok"),

            ("vetor de char nasce do texto",
             "char nome[] = \"ESP32\"; Serial.println(nome); Serial.println(strlen(nome));",
             "ESP32|5"),

            ("strcmp compara",
             "char t[] = \"abc\"; Serial.println(strcmp(t, \"abc\")); Serial.println(strcmp(t, \"abd\"));",
             "0|-1"),

            ("String(valor, HEX)",
             "Serial.println(String(255, HEX));", "FF"),

            ("printf com largura",
             "Serial.printf(\"%s=%03d\\n\", \"x\", 7);", "x=007"),

            ("write manda o byte, nao o numero",
             "Serial.write(72); Serial.write(105); Serial.println();", "Hi"),

            ("indice numa String",
             "String s = \"casa\"; Serial.println(s[0]);", "c"),

            ("(int) de char volta ao numero",
             "char c = 'A'; Serial.println((int)c);", "65"),
        ];

        foreach (var (nome, corpo, esperado) in casos)
        {
            string veio = Rodar(corpo);
            saida.AppendLine($"        {nome,-38} {veio}");
            Conferir("char: " + nome, veio == esperado, $"veio \"{veio}\", esperava \"{esperado}\"");
        }
    }

    /// <summary>
    /// O monitor serial agora manda texto PARA a placa. Ate a 1.4
    /// Serial.read() devolvia -1 para sempre, e o exemplo de ponte
    /// Serial-Bluetooth nao tinha como funcionar.
    /// </summary>
    static void Serial_Entrada()
    {
        saida.AppendLine("entrada do monitor serial");

        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maq.Reiniciar();
        var prog = new Parser(Lexer.Tokenizar("""
            void setup() { Serial.begin(115200); }
            void loop() {
              if (Serial.available()) {
                int n = Serial.parseInt();
                String resto = Serial.readStringUntil('\n');
                Serial.print("n=");
                Serial.println(n);
                Serial.print("resto=");
                Serial.println(resto);
              }
            }
            """)).AnalisarPrograma();
        var interp = new Interpretador(prog, maq);
        interp.Preparar();
        interp.Setup();

        interp.Loop();
        Conferir("serial: sem nada digitado, available() e zero",
            LinhasDoSerial(maq).Count == 0, string.Join("|", LinhasDoSerial(maq)));

        maq.EntradaSerial.Adicionar("42 ligar\n");
        interp.Loop();

        var linhas = LinhasDoSerial(maq);
        saida.AppendLine($"        digitei \"42 ligar\": {string.Join(" | ", linhas)}");
        Conferir("serial: parseInt le o numero digitado",
            linhas.Contains("n=42"), string.Join("|", linhas));
        Conferir("serial: readStringUntil le o resto da linha",
            linhas.Contains("resto= ligar"), string.Join("|", linhas));
    }

    /// <summary>
    /// WiFi, servidor web e HTTPClient: uma placa serve, a outra le, e o
    /// navegador do computador tambem alcanca.
    /// </summary>
    static void Rede_WifiEHttp()
    {
        saida.AppendLine("WiFi, servidor e HTTP");

        var rede = new Rede { UsarInternet = false };
        var (servidorMaq, servidorInterp) = MontarPlaca(rede, "ESP32-servidor", """
            #include <WiFi.h>
            #include <WebServer.h>
            WebServer server(80);
            int estadoDoLed = 0;

            void raiz() { server.send(200, "text/html", "<h1>oi da placa</h1>"); }

            void led() {
              estadoDoLed = server.arg("estado").toInt();
              digitalWrite(2, estadoDoLed);
              server.send(200, "text/plain", "led=" + String(estadoDoLed));
            }

            void setup() {
              Serial.begin(115200);
              pinMode(2, OUTPUT);
              WiFi.begin("CircuitSketch-WiFi", "12345678");
              while (WiFi.status() != WL_CONNECTED) { delay(200); }
              Serial.println(WiFi.localIP());
              server.on("/", raiz);
              server.on("/led", led);
              server.onNotFound([]() { server.send(404, "text/plain", "essa rota nao existe"); });
              server.begin();
            }

            void loop() { server.handleClient(); delay(5); }
            """);

        servidorInterp.Setup();
        var doServidor = LinhasDoSerial(servidorMaq);
        saida.AppendLine($"        servidor: {string.Join("|", doServidor)}");
        Conferir("wifi: a placa conecta e mostra o IP",
            doServidor.Contains("10.0.0.2"), string.Join("|", doServidor));

        var parar = new CancellationTokenSource();
        var giroDoServidor = Task.Run(() =>
        {
            while (!parar.IsCancellationRequested) servidorInterp.Loop();
        });

        try
        {
            // ---- outra placa da bancada lendo pela rede ----
            var (clienteMaq, clienteInterp) = MontarPlaca(rede, "ESP32-cliente", """
                #include <WiFi.h>
                #include <HTTPClient.h>
                void setup() {
                  Serial.begin(115200);
                  WiFi.begin("CircuitSketch-WiFi", "12345678");
                  while (WiFi.status() != WL_CONNECTED) { delay(200); }

                  HTTPClient http;
                  http.begin("http://10.0.0.2/");
                  Serial.println(http.GET());
                  Serial.println(http.getString());
                  http.end();

                  http.begin("http://10.0.0.2/led?estado=1");
                  Serial.println(http.GET());
                  Serial.println(http.getString());
                  http.end();

                  http.begin("http://10.0.0.2/nao-existe");
                  Serial.println(http.GET());
                  http.end();
                }
                void loop() {}
                """);

            var tarefaCliente = Task.Run(clienteInterp.Setup);
            bool terminou = tarefaCliente.Wait(15000);
            var doCliente = LinhasDoSerial(clienteMaq);
            saida.AppendLine($"        cliente: {string.Join(" | ", doCliente)}");

            Conferir("http: a placa cliente recebeu a pagina da outra",
                terminou && doCliente.Contains("200") && doCliente.Contains("<h1>oi da placa</h1>"),
                string.Join("|", doCliente));
            Conferir("http: o argumento da URL chega no server.arg()",
                doCliente.Contains("led=1"), string.Join("|", doCliente));
            Conferir("http: o pedido mexeu no pino da outra placa",
                servidorMaq.Digital[2] == 1, "o LED nao acendeu");
            Conferir("http: rota que nao existe cai no onNotFound",
                doCliente.Contains("404"), string.Join("|", doCliente));

            // ---- o navegador do computador, pela ponte em localhost ----
            var servidor = rede.Servidores.FirstOrDefault();
            string url = servidor?.UrlLocal;
            saida.AppendLine($"        ponte: {url}");
            Conferir("http: o servidor ganhou um endereco no computador", url is not null,
                "sem ponte em localhost");

            if (url is not null)
            {
                using var navegador = new System.Net.Http.HttpClient
                { Timeout = TimeSpan.FromSeconds(10) };
                string pagina = "";
                int codigo = 0;
                try
                {
                    var resposta = navegador.GetAsync(url).Result;
                    codigo = (int)resposta.StatusCode;
                    pagina = resposta.Content.ReadAsStringAsync().Result;
                }
                catch (Exception e) { pagina = "ERRO: " + e.Message; }

                saida.AppendLine($"        navegador: {codigo} {pagina}");
                Conferir("http: o navegador do computador abre a pagina da placa",
                    codigo == 200 && pagina.Contains("oi da placa"), $"{codigo} {pagina}");
            }
        }
        finally
        {
            parar.Cancel();
            giroDoServidor.Wait(3000);
            rede.Encerrar();
        }

        // ---- sem WiFi, o pedido falha como na placa de verdade ----
        {
            var redeSemWifi = new Rede { UsarInternet = false };
            var (maq, interp) = MontarPlaca(redeSemWifi, "ESP32-sozinha", """
                #include <HTTPClient.h>
                void setup() {
                  Serial.begin(115200);
                  HTTPClient http;
                  http.begin("http://exemplo.com/");
                  Serial.println(http.GET());
                  http.end();
                }
                void loop() {}
                """);
            interp.Setup();
            var linhas = LinhasDoSerial(maq);
            saida.AppendLine($"        sem WiFi: {string.Join("|", linhas)}");
            Conferir("http: sem WiFi devolve -1, como a placa",
                linhas.Contains("-1"), string.Join("|", linhas));

            long visto = 0;
            var eventos = redeSemWifi.Novos(ref visto);
            Conferir("http: o painel Rede explica por que falhou",
                eventos.Any(e => e.Texto.Contains("WL_CONNECTED")),
                string.Join(" / ", eventos.Select(e => e.Texto)));
            redeSemWifi.Encerrar();
        }
    }

    /// <summary>
    /// O servidor "cru" (WiFiServer), que e como os tutoriais mais comuns
    /// de ESP32 fazem: o sketch le os bytes do pedido e escreve o HTTP na
    /// mao.
    /// </summary>
    static void Rede_ServidorCru()
    {
        saida.AppendLine("servidor cru (WiFiServer)");

        var rede = new Rede { UsarInternet = false };
        var (maq, interp) = MontarPlaca(rede, "ESP32-cru", """
            #include <WiFi.h>
            WiFiServer server(80);
            String header;

            void setup() {
              Serial.begin(115200);
              pinMode(26, OUTPUT);
              WiFi.begin("CircuitSketch-WiFi", "12345678");
              while (WiFi.status() != WL_CONNECTED) { delay(200); }
              server.begin();
            }

            void loop() {
              WiFiClient client = server.available();
              if (client) {
                String linhaAtual = "";
                while (client.connected()) {
                  if (client.available()) {
                    char c = client.read();
                    header += c;
                    if (c == '\n') {
                      if (linhaAtual.length() == 0) {
                        if (header.indexOf("GET /26/on") >= 0) { digitalWrite(26, HIGH); }
                        client.println("HTTP/1.1 200 OK");
                        client.println("Content-type:text/html");
                        client.println("Connection: close");
                        client.println();
                        client.print("<h1>ESP32 na mao</h1>");
                        client.println();
                        break;
                      } else {
                        linhaAtual = "";
                      }
                    } else if (c != '\r') {
                      linhaAtual += c;
                    }
                  }
                }
                header = "";
                client.stop();
              }
            }
            """);

        interp.Setup();
        var parar = new CancellationTokenSource();
        var giro = Task.Run(() => { while (!parar.IsCancellationRequested) interp.Loop(); });

        try
        {
            string url = rede.Servidores.FirstOrDefault()?.UrlLocal;
            Conferir("cru: o servidor abriu em localhost", url is not null, "sem ponte");
            if (url is null) return;

            using var navegador = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var resposta = navegador.GetAsync(url + "26/on").Result;
            string pagina = resposta.Content.ReadAsStringAsync().Result;

            saida.AppendLine($"        navegador: {(int)resposta.StatusCode} {pagina.Trim()}");
            Conferir("cru: o sketch montou o HTTP na mao e o navegador entendeu",
                (int)resposta.StatusCode == 200 && pagina.Contains("ESP32 na mao"),
                $"{(int)resposta.StatusCode} {pagina}");

            bool acendeu = EsperarPor(() => maq.Digital[26] == 1, 2000);
            Conferir("cru: o caminho da URL acendeu o LED", acendeu, "o pino 26 continuou apagado");
        }
        finally
        {
            parar.Cancel();
            giro.Wait(3000);
            rede.Encerrar();
        }
    }

    /// <summary>
    /// MQTT: duas placas conversando pelo broker interno, e a mesma placa
    /// falando MQTT de verdade por TCP com o broker que o CircuitSketch
    /// abre em localhost.
    /// </summary>
    static void Rede_Mqtt()
    {
        saida.AppendLine("MQTT");

        const string CodigoDoOuvinte = """
            #include <WiFi.h>
            #include <PubSubClient.h>
            WiFiClient espClient;
            PubSubClient client(espClient);

            void callback(char* topic, byte* payload, unsigned int length) {
              String msg;
              for (int i = 0; i < length; i++) { msg += (char)payload[i]; }
              Serial.println(String(topic) + "=" + msg);
            }

            void setup() {
              Serial.begin(115200);
              WiFi.begin("CircuitSketch-WiFi", "12345678");
              while (WiFi.status() != WL_CONNECTED) { delay(200); }
              client.setServer("broker.hivemq.com", 1883);
              client.setCallback(callback);
              if (client.connect("ouvinte")) {
                client.subscribe("aula/#");
                Serial.println("assinei");
              }
              Serial.println(client.state());
            }

            void loop() { client.loop(); }
            """;

        // ---- broker interno: duas placas da bancada ----
        {
            var rede = new Rede { UsarInternet = false };
            var (ouvinteMaq, ouvinteInterp) = MontarPlaca(rede, "ESP32-ouvinte", CodigoDoOuvinte);
            ouvinteInterp.Setup();

            var (faladorMaq, faladorInterp) = MontarPlaca(rede, "ESP32-falador", """
                #include <WiFi.h>
                #include <PubSubClient.h>
                WiFiClient espClient;
                PubSubClient client(espClient);
                char msg[50];

                void setup() {
                  Serial.begin(115200);
                  WiFi.begin("CircuitSketch-WiFi", "12345678");
                  while (WiFi.status() != WL_CONNECTED) { delay(200); }
                  client.setServer("broker.hivemq.com", 1883);
                  client.connect("falador");
                  snprintf(msg, 50, "temp=%.1f", 23.5);
                  client.publish("aula/sala1", msg);
                }
                void loop() {}
                """);
            faladorInterp.Setup();
            ouvinteInterp.Loop();

            var doOuvinte = LinhasDoSerial(ouvinteMaq);
            saida.AppendLine($"        ouvinte: {string.Join(" | ", doOuvinte)}");

            Conferir("mqtt: a placa conectou (state 0)",
                doOuvinte.Contains("assinei") && doOuvinte.Contains("0"), string.Join("|", doOuvinte));
            Conferir("mqtt: a mensagem da outra placa chegou no callback",
                doOuvinte.Contains("aula/sala1=temp=23.5"), string.Join("|", doOuvinte));

            long visto = 0;
            var eventos = rede.Novos(ref visto);
            Conferir("mqtt: o painel Rede mostra a publicacao",
                eventos.Any(e => e.Tipo == "mqtt" && e.Texto.Contains("aula/sala1")),
                string.Join(" / ", eventos.Select(e => e.Texto)));

            // O painel publica como um app de celular faria.
            rede.PublicarDoPainel("aula/sala1", "do painel");
            ouvinteInterp.Loop();
            Conferir("mqtt: o que o painel publica chega na placa",
                LinhasDoSerial(ouvinteMaq).Contains("aula/sala1=do painel"),
                string.Join("|", LinhasDoSerial(ouvinteMaq)));

            rede.Encerrar();
        }

        // ---- MQTT de verdade, por TCP, no broker interno de localhost ----
        {
            var rede = new Rede { UsarInternet = true };
            bool abriu = rede.AbrirBrokerLocal(0);
            int porta = rede.PortaDoBroker;
            saida.AppendLine($"        broker em localhost: porta {porta}");
            Conferir("mqtt: o broker interno abre em localhost", abriu && porta > 0, "nao abriu");

            var (maq, interp) = MontarPlaca(rede, "ESP32-tcp", """
                #include <WiFi.h>
                #include <PubSubClient.h>
                WiFiClient espClient;
                PubSubClient client(espClient);

                void callback(char* topic, byte* payload, unsigned int length) {
                  String msg;
                  for (int i = 0; i < length; i++) { msg += (char)payload[i]; }
                  Serial.println(String(topic) + "=" + msg);
                }

                void setup() {
                  Serial.begin(115200);
                  WiFi.begin("CircuitSketch-WiFi", "12345678");
                  while (WiFi.status() != WL_CONNECTED) { delay(200); }
                  client.setServer("127.0.0.1", PORTA);
                  client.setCallback(callback);
                  Serial.println(client.connect("placa-tcp"));
                  client.subscribe("bancada/luz");
                  client.publish("bancada/luz", "on");
                }

                void loop() { client.loop(); }
                """.Replace("PORTA", porta.ToString()));

            interp.Setup();
            bool chegou = EsperarPor(() =>
            {
                interp.Loop();
                return LinhasDoSerial(maq).Any(l => l.StartsWith("bancada/luz="));
            }, 5000);

            var linhas = LinhasDoSerial(maq);
            saida.AppendLine($"        por TCP: {string.Join(" | ", linhas)}");
            Conferir("mqtt: conectou no broker por TCP de verdade",
                linhas.Contains("1"), string.Join("|", linhas));
            Conferir("mqtt: a mensagem deu a volta pelo protocolo e voltou",
                chegou && linhas.Contains("bancada/luz=on"), string.Join("|", linhas));

            rede.Encerrar();
        }
    }

    /// <summary>
    /// Bluetooth classico: o celular do painel conversando com a placa, e
    /// duas placas conversando entre si.
    /// </summary>
    static void Rede_Bluetooth()
    {
        saida.AppendLine("Bluetooth");

        var rede = new Rede { UsarInternet = false };
        var (maq, interp) = MontarPlaca(rede, "ESP32-bt", """
            #include "BluetoothSerial.h"
            BluetoothSerial SerialBT;

            void setup() {
              Serial.begin(115200);
              pinMode(2, OUTPUT);
              SerialBT.begin("ESP32-Aula");
            }

            void loop() {
              if (SerialBT.available()) {
                String comando = SerialBT.readStringUntil('\n');
                comando.trim();
                if (comando == "ligar") { digitalWrite(2, HIGH); }
                if (comando == "desligar") { digitalWrite(2, LOW); }
                SerialBT.println("ok: " + comando);
                Serial.println(comando);
              }
            }
            """);
        interp.Setup();

        Conferir("bt: a placa aparece na busca do celular",
            rede.DispositivosVisiveis().Contains("ESP32-Aula"),
            string.Join(",", rede.DispositivosVisiveis()));

        Conferir("bt: o celular conecta", rede.ConectarCelular("ESP32-Aula"), "nao conectou");
        rede.EnviarDoCelular("ligar\n");
        interp.Loop();

        var linhas = LinhasDoSerial(maq);
        saida.AppendLine($"        placa recebeu: {string.Join("|", linhas)}");
        Conferir("bt: o comando do celular chega na placa",
            linhas.Contains("ligar"), string.Join("|", linhas));
        Conferir("bt: o comando acendeu o LED", maq.Digital[2] == 1, "o pino 2 continuou apagado");

        long visto = 0;
        var eventos = rede.Novos(ref visto);
        Conferir("bt: a resposta da placa aparece no painel",
            eventos.Any(e => e.Tipo == "bt" && e.Texto.Contains("ok: ligar")),
            string.Join(" / ", eventos.Select(e => e.Texto)));

        // ---- placa com placa ----
        var (mestreMaq, mestreInterp) = MontarPlaca(rede, "ESP32-mestre", """
            #include "BluetoothSerial.h"
            BluetoothSerial SerialBT;
            void setup() {
              Serial.begin(115200);
              SerialBT.begin("mestre", true);
              Serial.println(SerialBT.connect("ESP32-Aula"));
              SerialBT.println("desligar");
            }
            void loop() {}
            """);
        mestreInterp.Setup();
        interp.Loop();

        saida.AppendLine($"        mestre: {string.Join("|", LinhasDoSerial(mestreMaq))}");
        Conferir("bt: uma placa conecta na outra",
            LinhasDoSerial(mestreMaq).Contains("1"), string.Join("|", LinhasDoSerial(mestreMaq)));
        Conferir("bt: o que uma placa manda chega na outra",
            LinhasDoSerial(maq).Contains("desligar") && maq.Digital[2] == 0,
            string.Join("|", LinhasDoSerial(maq)));

        // ---- placa sem Bluetooth classico ----
        {
            var (unoMaq, unoInterp) = MontarPlaca(rede, "Uno", """
                #include "BluetoothSerial.h"
                BluetoothSerial SerialBT;
                void setup() { SerialBT.begin("uno"); }
                void loop() {}
                """, PerfilPlaca.Uno);

            string erro = null;
            try { unoInterp.Setup(); }
            catch (Exception e) { erro = e.Message; }

            saida.AppendLine($"        no Uno: {erro}");
            Conferir("bt: placa sem Bluetooth recusa com explicacao",
                erro is not null && erro.Contains("ESP32"), erro ?? "passou calado");
        }

        rede.Encerrar();
    }

    /// <summary>
    /// O painel de baixo da janela: as tres abas, o texto que se manda
    /// para o sketch pelo monitor, o registro da rede com o link do
    /// servidor e o celular do Bluetooth.
    /// </summary>
    static void Painel_RedeEBluetooth()
    {
        saida.AppendLine("painel de rede e bluetooth");

        var janela = new MainWindow();
        var c = new Circuito();
        var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        c.Pecas.Add(placa);

        janela.TesteAbrir(c, """
            #include <WiFi.h>
            #include <WebServer.h>
            #include "BluetoothSerial.h"

            WebServer server(80);
            BluetoothSerial SerialBT;

            void setup() {
              Serial.begin(115200);
              pinMode(2, OUTPUT);
              WiFi.begin("CircuitSketch-WiFi", "12345678");
              while (WiFi.status() != WL_CONNECTED) { delay(100); }
              server.on("/", []() { server.send(200, "text/plain", "ok"); });
              server.begin();
              SerialBT.begin("ESP32-Aula");
              Serial.println("pronto");
            }

            void loop() {
              server.handleClient();
              if (Serial.available()) {
                String t = Serial.readStringUntil('\n');
                Serial.println("eco " + t);
              }
              if (SerialBT.available()) {
                String comando = SerialBT.readStringUntil('\n');
                comando.trim();
                if (comando == "ligar") { digitalWrite(2, HIGH); }
                SerialBT.println("ok " + comando);
              }
              delay(5);
            }
            """);

        try
        {
            janela.TesteRodar();
            bool ligou = EsperarPor(() => janela.TesteSerial.Contains("pronto"), 6000);
            Conferir("painel: o sketch de rede subiu", ligou, janela.TesteStatus);

            // ---- aba Rede ----
            bool anotou = EsperarPor(() => janela.TesteTextoDaRede.Contains("conectou em"), 6000);
            saida.AppendLine($"        rede: {janela.TesteTextoDaRede.Split('\n').LastOrDefault()}");
            Conferir("painel: a aba Rede mostra o WiFi conectando", anotou, janela.TesteTextoDaRede);
            Conferir("painel: o servidor virou um link para o navegador",
                EsperarPor(() => janela.TesteLinksDeRede > 0, 3000), "nenhum link");

            janela.TesteMostrarAba("rede");
            Conferir("painel: a aba escolhida troca", janela.TesteAbaVisivel == "rede", janela.TesteAbaVisivel);

            // ---- monitor serial mandando texto para o sketch ----
            janela.TesteDigitarNoSerial("bom dia");
            bool ecoou = EsperarPor(() => janela.TesteSerial.Contains("eco bom dia"), 4000);
            Conferir("painel: o que se digita no monitor chega no sketch", ecoou, janela.TesteSerial);

            // ---- celular do Bluetooth ----
            bool apareceu = EsperarPor(
                () => janela.TesteRedeSimulada.DispositivosVisiveis().Contains("ESP32-Aula"), 4000);
            Conferir("painel: a placa aparece na lista do Bluetooth", apareceu,
                string.Join(",", janela.TesteRedeSimulada.DispositivosVisiveis()));

            janela.TesteConectarBluetooth("ESP32-Aula");
            janela.TesteEnviarPeloBluetooth("ligar");

            var maquina = janela.TesteMaquinaDa(placa.Id);
            bool acendeu = EsperarPor(() => maquina.Digital[2] == 1, 4000);
            bool respondeu = EsperarPor(() => janela.TesteTextoDoBluetooth.Contains("ok ligar"), 4000);

            saida.AppendLine($"        bluetooth: {janela.TesteTextoDoBluetooth.Split('\n').LastOrDefault()}");
            Conferir("painel: o comando do celular acende o LED da placa", acendeu, "o pino 2 nao acendeu");
            Conferir("painel: a resposta da placa aparece na aba Bluetooth", respondeu,
                janela.TesteTextoDoBluetooth);
        }
        finally { janela.TesteParar(); }

        Conferir("painel: parar fecha os servidores da rede",
            janela.TesteRedeSimulada.Servidores.Count == 0,
            janela.TesteRedeSimulada.Servidores.Count + " abertos");
    }

    /// <summary>
    /// Tres queixas do professor sobre o editor: o fio que nao desmarcava,
    /// a grade que acabava ao arrastar e a falta de um "circuito novo".
    /// </summary>
    static void Editor_SelecaoEGrade()
    {
        saida.AppendLine("selecao, grade e circuito novo");

        // ---- clicar no vazio desmarca o fio, e o TRACO acompanha ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 100 };
            c.Pecas.AddRange([placa, led]);
            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D13", ParaPeca = led.Id, ParaPino = "A" });

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var meio = ed.PontoDoFioParaTeste(0, 0.5);
            ed.Apertar(meio);
            bool selecionou = ed.FiosSelecionados.Count == 1;
            bool tracoAceso = ed.FioDestacadoParaTeste(0);

            // Bem longe de tudo: e o clique no vazio.
            ed.Apertar(new System.Windows.Point(-600, -600));
            ed.Soltar();

            bool limpou = ed.FiosSelecionados.Count == 0;
            bool tracoApagado = !ed.FioDestacadoParaTeste(0);

            saida.AppendLine($"        selecionou={selecionou} aceso={tracoAceso} "
                             + $"limpou={limpou} apagado={tracoApagado}");

            Conferir("fio: clicar nele seleciona", selecionou, $"{ed.FiosSelecionados.Count} fios");
            Conferir("fio: selecionado, o traco fica destacado", tracoAceso, "traco igual ao normal");
            Conferir("fio: clicar no vazio desmarca", limpou, "continuou na lista");
            Conferir("fio: desmarcado, o traco volta ao normal", tracoApagado,
                "o traco continuou com cara de selecionado");
        }

        // ---- a grade acompanha o pan e o zoom ----
        {
            var ed = new EditorCircuito { MostrarGrade = true };
            ed.Carregar(new Circuito());
            ed.AplicarGrade();

            var antes = ed.Background as System.Windows.Media.DrawingBrush;
            double x0 = antes?.Viewport.X ?? -1;
            double lado0 = antes?.Viewport.Width ?? -1;

            ed.Deslocar(7, 3);
            var depois = ed.Background as System.Windows.Media.DrawingBrush;
            double x1 = depois?.Viewport.X ?? -1;

            ed.MudarZoom(2);
            var comZoom = ed.Background as System.Windows.Media.DrawingBrush;
            double lado2 = comZoom?.Viewport.Width ?? -1;

            saida.AppendLine($"        grade: lado {lado0:0.0} -> {lado2:0.0} px, "
                             + $"origem {x0:0.0} -> {x1:0.0}");

            Conferir("grade: o fundo do editor e a grade", antes is not null, "fundo liso");
            Conferir("grade: o ladrilho tem o passo da protoboard",
                Math.Abs(lado0 - Catalogo.Passo) < 0.01, $"{lado0:0.00} px");
            Conferir("grade: arrastar move a grade junto", Math.Abs(x1 - x0) > 0.5,
                $"{x0:0.0} -> {x1:0.0}");
            Conferir("grade: o zoom estica a grade junto",
                Math.Abs(lado2 - Catalogo.Passo * 2) < 0.01, $"{lado2:0.00} px");
        }

        // ---- circuito novo ----
        {
            var janela = new MainWindow();
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 0 });
            janela.TesteAbrir(c, "void setup() {} void loop() {}");

            janela.TesteNovoCircuito();
            saida.AppendLine($"        depois do novo: {janela.TesteCircuito.Pecas.Count} pecas, "
                             + $"titulo \"{janela.Title}\"");

            Conferir("novo: o circuito fica vazio", janela.TesteCircuito.Pecas.Count == 0,
                $"{janela.TesteCircuito.Pecas.Count} pecas");
            Conferir("novo: o codigo volta ao modelo",
                janela.TesteCodigo.Contains("void setup()") && janela.TesteCodigo.Contains("pinMode"),
                janela.TesteCodigo.Split('\n').FirstOrDefault());
            Conferir("novo: o titulo volta ao normal", janela.Title == "CircuitSketch", janela.Title);
        }

        // ---- o canto do fio cola na linha do pino ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var led = new PecaNoCircuito { Tipo = "led", X = 300, Y = 200 };
            c.Pecas.AddRange([placa, led]);
            c.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = "D13", ParaPeca = led.Id, ParaPino = "A",
                Dobras = [new PontoFio { X = 150, Y = 150 }],
            });

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var pino = Geometria.PosicaoPino(placa, Catalogo.Obter(placa.Tipo),
                Catalogo.Obter(placa.Tipo).Pinos.First(x => x.Nome == "D13"));

            // Arrastado para perto da linha do pino: cola nela.
            var perto = ed.EncaixarDobraParaTeste(0, 0, new System.Windows.Point(pino.X + 2.5, 260));
            // Longe: vale a grade de 0,1".
            var longe = ed.EncaixarDobraParaTeste(0, 0, new System.Windows.Point(pino.X + 40.3, 260));

            saida.AppendLine($"        pino em x={pino.X:0.0}; perto -> {perto.X:0.0}; "
                             + $"longe -> {longe.X:0.0}");

            Conferir("fio: o canto cola na linha do pino",
                Math.Abs(perto.X - pino.X) < 0.001, $"{perto.X:0.00} x {pino.X:0.00}");
            // ---- nome da peca ----
            {
                var comNome = new Circuito();
                var led2 = new PecaNoCircuito { Tipo = "led", X = 100, Y = 100, Nome = "farol" };
                comNome.Pecas.Add(led2);

                var editor2 = new EditorCircuito();
                editor2.Carregar(comNome);
                string desenhado = editor2.RotuloParaTeste(led2.Id);

                // Girar a peca nao pode virar o nome de cabeca para baixo.
                editor2.DefinirNome(led2, "farol de re");
                string trocado = editor2.RotuloParaTeste(led2.Id);

                string temporario = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), $"nome-{Guid.NewGuid():N}.sktch");
                Sktch.Salvar(temporario, comNome, "void setup() {}");
                var (volta, _) = Sktch.Abrir(temporario);
                System.IO.File.Delete(temporario);

                saida.AppendLine($"        nome: desenhado \"{desenhado}\", trocado \"{trocado}\", "
                                 + $"salvo \"{volta.Pecas[0].Nome}\"");

                Conferir("nome: aparece embaixo da peca", desenhado == "farol", desenhado ?? "nenhum");
                Conferir("nome: trocar o nome troca o que esta na tela",
                    trocado == "farol de re", trocado ?? "nenhum");
                Conferir("nome: vai junto no .sktch", volta.Pecas[0].Nome == "farol de re",
                    volta.Pecas[0].Nome ?? "nenhum");
                Conferir("nome: some quando fica vazio",
                    editor2.DefinirNomeVazioParaTeste(led2), "continuou na tela");
            }

            Conferir("fio: longe do pino, o canto vai para a grade das pecas",
                Math.Abs(longe.X / (Catalogo.Passo / 2) - Math.Round(longe.X / (Catalogo.Passo / 2))) < 0.001,
                $"{longe.X:0.00}");
        }
    }

    /// <summary>
    /// A arte do SVG cai onde os pinos dizem que ela esta.
    ///
    /// A queixa do professor: "SVG do LED RGB fora do centro, o que faz
    /// com que pinos estejam deslocados. O mesmo acontece com os botoes
    /// de 6mm e comum." A causa nao era o desenho: era a conta do
    /// viewBox. O renderizador acertava a escala e a centralizacao e
    /// somava um deslocamento de (-minX, -minY) por cima -- so aparecia
    /// nas pecas cujo viewBox nao comeca em (0 0), que sao justamente o
    /// LED, o LED RGB, os dois botoes e as tres placas Arduino.
    ///
    /// A sonda e um retangulo que cobre exatamente o viewBox: onde ele
    /// cai E a conta do renderizador, medida em vez de suposta.
    /// </summary>
    static void Pecas_ArteNoLugar()
    {
        saida.AppendLine("a arte cai onde os pinos dizem");

        static string Txt(double v)
            => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        static System.Windows.Rect Sondar(string larg, string alt,
            double mx, double my, double vl, double va)
        {
            string svg = $"<svg width=\"{larg}\" height=\"{alt}\" "
                + $"viewBox=\"{Txt(mx)} {Txt(my)} {Txt(vl)} {Txt(va)}\" "
                + "xmlns=\"http://www.w3.org/2000/svg\">"
                + $"<rect x=\"{Txt(mx)}\" y=\"{Txt(my)}\" width=\"{Txt(vl)}\" "
                + $"height=\"{Txt(va)}\" fill=\"#000\"/></svg>";
            return Pecas.TintaDeTexto(svg);
        }

        // O que o navegador faz com preserveAspectRatio padrao
        // ("xMidYMid meet"): escala uniforme e sobra dividida em dois.
        static System.Windows.Rect Esperado(double larg, double alt, double vl, double va)
        {
            double escala = Math.Min(larg / vl, alt / va);
            return new System.Windows.Rect((larg - vl * escala) / 2, (alt - va * escala) / 2,
                                           vl * escala, va * escala);
        }

        const double Mm = 96 / 25.4;

        (string nome, string larg, string alt, double lg, double at,
         double mx, double my, double vl, double va)[] casos =
        [
            ("LED RGB",   "42.129",      "72.582", 42.129,          72.582,
             -17,  -10, 37.3425,   57.5115),
            ("LED",       "40",          "50",     40,              50,
             -10,   -5, 35.456,    39.618),
            ("botao",     "17.802mm",    "12mm",   17.802 * Mm,     12 * Mm,
              -3,    0, 18,        12),
            ("botao 6mm", "7.4129977mm", "6mm",    7.4129977 * Mm,  6 * Mm,
              -3,    0, 7.4954476, 6),
            ("Arduino Uno", "72.58mm",   "53.34mm", 72.58 * Mm,     53.34 * Mm,
              -4,    0, 72.58,     53.34),

            // Sem deslocamento: e a maioria das pecas, e nao pode mudar.
            ("LCD 16x2",  "80mm",        "36mm",   80 * Mm,         36 * Mm,
               0,    0, 80,        36),
        ];

        foreach (var (nome, larg, alt, lg, at, mx, my, vl, va) in casos)
        {
            var deu = Sondar(larg, alt, mx, my, vl, va);
            var devia = Esperado(lg, at, vl, va);
            double erro = Math.Max(Math.Abs(deu.X - devia.X), Math.Abs(deu.Y - devia.Y));

            saida.AppendLine($"    {nome,-12} caiu em ({deu.X:0.00}, {deu.Y:0.00})"
                             + $"  devia ({devia.X:0.00}, {devia.Y:0.00})");

            Conferir($"arte: {nome} no lugar", erro < 0.5,
                $"errou {erro:0.0} px -- viewBox comeca em ({mx}, {my})");
            Conferir($"arte: {nome} do tamanho certo",
                Math.Abs(deu.Width - devia.Width) < 0.5
                && Math.Abs(deu.Height - devia.Height) < 0.5,
                $"{deu.Width:0.0}x{deu.Height:0.0} em vez de {devia.Width:0.0}x{devia.Height:0.0}");
        }

        // A sobra vai METADE de cada lado, e nao toda de um so.
        var larga = Sondar("100", "50", 0, 0, 50, 50);
        var alta = Sondar("50", "100", 0, 0, 50, 50);
        Conferir("arte: sobra horizontal dividida em dois",
            Math.Abs(larga.X - 25) < 0.5, $"x = {larga.X:0.0}");
        Conferir("arte: sobra vertical dividida em dois",
            Math.Abs(alta.Y - 25) < 0.5, $"y = {alta.Y:0.0}");

        // O outro deslocamento: <defs> com coordenada negativa.
        //
        // Um circulo de raio 20 guardado no defs empurrava TODO o
        // desenho em 20 px assim que alguem o usasse com <use>, mesmo
        // com o circulo caindo num ponto positivo da tela. Era o caso do
        // controle remoto -- os botoes dele sao um <circle r="16,3"> no
        // defs -- e o desenho saia 17 px fora da caixa, com a coluna da
        // direita cortada e as teclas fora do lugar.
        {
            const string cabeca =
                "<svg xmlns=\"http://www.w3.org/2000/svg\" "
                + "xmlns:xlink=\"http://www.w3.org/1999/xlink\" "
                + "width=\"100\" height=\"100\" viewBox=\"0 0 100 100\">";
            const string alvo =
                "<rect x=\"10\" y=\"10\" width=\"30\" height=\"30\" fill=\"#000\"/>";

            var comDefs = Pecas.TintaDeTexto(
                cabeca + "<defs><g id=\"c\"><circle r=\"20\" fill=\"#000\"/></g></defs>"
                + alvo + "<use xlink:href=\"#c\" x=\"50\" y=\"50\"/></svg>");

            var semDefs = Pecas.TintaDeTexto(
                cabeca + alvo + "<circle cx=\"50\" cy=\"50\" r=\"20\" fill=\"#000\"/></svg>");

            saida.AppendLine($"    defs: com use ({comDefs.X:0.0}, {comDefs.Y:0.0}), "
                             + $"sem use ({semDefs.X:0.0}, {semDefs.Y:0.0})");

            Conferir("arte: o defs nao empurra o desenho",
                Math.Abs(comDefs.X - semDefs.X) < 0.5 && Math.Abs(comDefs.Y - semDefs.Y) < 0.5,
                $"({comDefs.X:0.0}, {comDefs.Y:0.0}) contra ({semDefs.X:0.0}, {semDefs.Y:0.0})");
            Conferir("arte: e o retangulo fica onde foi desenhado",
                Math.Abs(comDefs.X - 10) < 0.5, $"x = {comDefs.X:0.0}");
        }

        // O controle remoto e o caso real desse deslocamento: a arte tem
        // que caber na caixa da peca, senao o clique cai no botao errado.
        {
            var tinta = Pecas.TintaDe("ir-remote");
            var def = Catalogo.Obter("ir-remote");
            saida.AppendLine($"    controle remoto: tinta ({tinta.X:0.0}, {tinta.Y:0.0}) "
                             + $"{tinta.Width:0.0}x{tinta.Height:0.0} "
                             + $"numa caixa de {def.Largura:0}x{def.Altura:0}");

            Conferir("arte: o controle remoto cabe na propria caixa",
                tinta.X > -1.5 && tinta.Y > -1.5
                && tinta.Right < def.Largura + 1.5 && tinta.Bottom < def.Altura + 1.5,
                $"({tinta.X:0.0}, {tinta.Y:0.0}) {tinta.Width:0.0}x{tinta.Height:0.0}");
        }

        // Cada peca, para o olho: onde a tinta caiu e onde estao os
        // pinos. Nao vira regra -- varias pecas do Wokwi desenham fora
        // do viewBox de proposito e o navegador corta -- mas um pino
        // longe de qualquer tinta e sinal de desalinhamento.
        foreach (var def in Catalogo.Todas.OrderBy(d => d.Id))
        {
            if (!Pecas.Existe(def.Id) || def.Pinos.Count == 0) continue;
            var tinta = Pecas.TintaDe(def.Id);
            if (tinta.IsEmpty) continue;

            var folgada = System.Windows.Rect.Inflate(tinta, 4, 4);
            var fora = def.Pinos.Where(pino =>
                !folgada.Contains(new System.Windows.Point(pino.X, pino.Y))).ToList();

            saida.AppendLine($"    {def.Id,-24} tinta ({tinta.X:0.0},{tinta.Y:0.0}) "
                             + $"{tinta.Width:0.0}x{tinta.Height:0.0}"
                             + (fora.Count == 0 ? "  pinos na arte"
                                : $"  FORA: {string.Join(",", fora.Take(4).Select(f => f.Nome))}"));
        }
    }

    /// <summary>
    /// Todo pino de toda peca aceita fio -- a "conexao amarela" que o
    /// professor procurou no potenciometro e no LCD e nao achou.
    ///
    /// O anel amarelo so aparece onde o clique REALMENTE liga fio, entao
    /// conferir o anel e conferir a ligacao: e o mesmo Resolver().
    /// </summary>
    static void Pecas_TodosOsPinosPegam()
    {
        saida.AppendLine("todo pino pega fio");

        int semAnel = 0, conferidos = 0;
        foreach (var def in Catalogo.Todas.OrderBy(d => d.Id))
        {
            if (Catalogo.EhProtoboard(def.Id) || def.Pinos.Count == 0) continue;

            var c = new Circuito();
            var peca = new PecaNoCircuito { Tipo = def.Id, X = 300, Y = 300 };
            c.Pecas.Add(peca);

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var perdidos = new List<string>();
            foreach (var pino in def.Pinos)
            {
                conferidos++;
                var onde = Geometria.PosicaoPino(peca, def, pino);
                var acerto = ed.Resolver(onde);
                if (acerto.Camada != EditorCircuito.Camada.PinoPeca || acerto.Pino != pino.Nome)
                    perdidos.Add($"{pino.Nome}({acerto.Camada})");
            }

            if (perdidos.Count > 0)
            {
                semAnel++;
                saida.AppendLine($"    {def.Id,-24} {perdidos.Count}/{def.Pinos.Count} sem anel: "
                                 + string.Join(" ", perdidos.Take(6)));
            }

            Conferir($"pino: {def.Id} liga fio em todos", perdidos.Count == 0,
                string.Join(" ", perdidos.Take(6)));
        }

        saida.AppendLine($"    {conferidos} pinos conferidos, {semAnel} pecas com problema");
    }

    /// <summary>
    /// O barramento I2C: o Wire do sketch e as pecas de duas vias.
    ///
    /// O sketch do professor (painel de carro) parava na linha 89, em
    /// "Wire.begin(21, 22)". Este bloco cobre o caminho inteiro: o
    /// codigo, o endereco que responde, o endereco que nao responde, o
    /// LCD de modulo e o scanner de I2C -- o exercicio que todo mundo
    /// roda primeiro quando o display fica em branco.
    /// </summary>
    static void Motor_I2c()
    {
        saida.AppendLine("barramento I2C");

        static (Maquina maq, string erro) Rodar(string corpo, Action<Maquina> antes = null)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            antes?.Invoke(maq);
            try
            {
                var prog = new Parser(Lexer.Tokenizar(
                    "void nada() {}" + Environment.NewLine
                    + "void setup() {" + Environment.NewLine + corpo + Environment.NewLine + "}"
                    + Environment.NewLine + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
            }
            catch (Exception e) { return (maq, e.Message); }
            return (maq, null);
        }

        // ---- a linha que derrubava o sketch do professor ----
        {
            var (maq, erro) = Rodar("Wire.begin(21, 22); Serial.println(\"passou\");");
            saida.AppendLine($"    Wire.begin(21, 22): {erro ?? "passou"}");
            Conferir("i2c: Wire.begin(sda, scl) roda", erro is null, erro);
            Conferir("i2c: os pinos ficam gravados",
                maq.I2c.Sda == 21 && maq.I2c.Scl == 22, $"{maq.I2c.Sda}/{maq.I2c.Scl}");
        }

        // ---- sem argumento, os pinos sao os da placa ----
        {
            var (esp, erro) = Rodar("Wire.begin();");
            Conferir("i2c: sem argumento usa os pinos da placa",
                erro is null && esp.I2c.Sda == 21 && esp.I2c.Scl == 22,
                erro ?? $"{esp.I2c.Sda}/{esp.I2c.Scl}");

            var uno = new Maquina { Perfil = PerfilPlaca.Uno };
            uno.Reiniciar();
            var prog = new Parser(Lexer.Tokenizar(
                "void setup() { Wire.begin(); }" + Environment.NewLine
                + "void loop() {}")).AnalisarPrograma();
            var interp = new Interpretador(prog, uno);
            interp.Preparar();
            interp.Setup();
            Conferir("i2c: no Uno sao o A4 e o A5",
                uno.I2c.Sda == 18 && uno.I2c.Scl == 19, $"{uno.I2c.Sda}/{uno.I2c.Scl}");
        }

        // ---- quem responde e quem nao responde ----
        {
            var (maq, erro) = Rodar(
                "Wire.begin();"
                + "Wire.beginTransmission(0x27); Wire.write(0x00);"
                + "Serial.println(Wire.endTransmission());"
                + "Wire.beginTransmission(0x50);"
                + "Serial.println(Wire.endTransmission());",
                m => m.I2c.Ligar(0x27, "LCD"));

            var linhas = LinhasDoSerial(maq);
            saida.AppendLine($"    endTransmission: {string.Join(" / ", linhas)}");

            Conferir("i2c: endereco que responde devolve 0",
                erro is null && linhas.FirstOrDefault() == "0", erro ?? string.Join("|", linhas));
            Conferir("i2c: endereco vazio devolve 2 (sem resposta)",
                linhas.Count > 1 && linhas[1] == "2", string.Join("|", linhas));
            Conferir("i2c: o aparelho recebeu o byte",
                maq.I2c.Achar(0x27)?.Ultimos.Count == 1,
                $"{maq.I2c.Achar(0x27)?.Ultimos.Count} bytes");
        }

        // ---- requestFrom: o que volta ----
        {
            var (maq, erro) = Rodar(
                "Wire.begin();"
                + "int n = Wire.requestFrom(0x68, 3);"
                + "Serial.println(n);"
                + "while (Wire.available()) { Serial.println(Wire.read()); }",
                m => m.I2c.Ligar(0x68, "MPU6050"));

            var linhas = LinhasDoSerial(maq);
            Conferir("i2c: requestFrom devolve quantos vieram",
                erro is null && linhas.FirstOrDefault() == "3", erro ?? string.Join("|", linhas));
            Conferir("i2c: read esvazia a fila", linhas.Count == 4, string.Join("|", linhas));
        }

        // ---- o LCD de modulo: init() acende e o tamanho vem do construtor ----
        {
            var (maq, erro) = Rodar(
                "LiquidCrystal_I2C lcd(0x27, 20, 4);"
                + "Wire.begin(); lcd.init(); lcd.backlight();"
                + "lcd.setCursor(0, 2); lcd.print(\"terceira linha\");");

            saida.AppendLine($"    LCD 20x4: {maq.Lcd?.Colunas}x{maq.Lcd?.Linhas} "
                             + $"ligado={maq.Lcd?.Ligado}");

            Conferir("i2c: lcd.init() acende o display",
                erro is null && maq.Lcd is { Ligado: true }, erro ?? "apagado");
            Conferir("i2c: o construtor manda no tamanho",
                maq.Lcd is { Colunas: 20, Linhas: 4 }, $"{maq.Lcd?.Colunas}x{maq.Lcd?.Linhas}");
            Conferir("i2c: escreve na terceira linha",
                maq.Lcd?.LinhaTexto(2).Trim() == "terceira linha", maq.Lcd?.LinhaTexto(2));
            Conferir("i2c: declarar o LCD pendura o 0x27 no barramento",
                maq.I2c.Existe(0x27), "nao registrou");
        }

        // ---- recusa dita: a placa como escrava ----
        {
            var (_, erro) = Rodar("Wire.begin(8); Wire.onReceive(nada);");
            Conferir("i2c: recusa o modo escravo em vez de fingir",
                erro is not null && erro.Contains("ESCRAVA"), erro ?? "passou calado");
        }

        // ---- o scanner de I2C, com o circuito de verdade ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var lcd = new PecaNoCircuito { Tipo = "lcd1602-i2c", X = 400, Y = 0 };
            var solto = new PecaNoCircuito { Tipo = "mpu6050", X = 400, Y = 300 };
            c.Pecas.AddRange([placa, lcd, solto]);
            c.Fios.Add(new Fio { DePeca = lcd.Id, DePino = "SDA",
                                 ParaPeca = placa.Id, ParaPino = "D21" });
            c.Fios.Add(new Fio { DePeca = lcd.Id, DePino = "SCL",
                                 ParaPeca = placa.Id, ParaPino = "D22" });

            var janela = new MainWindow();
            janela.TesteAbrir(c,
                "#include <Wire.h>\n"
                + "void setup() {\n"
                + "  Serial.begin(115200);\n"
                + "  Wire.begin();\n"
                + "  for (byte e = 1; e < 127; e++) {\n"
                + "    Wire.beginTransmission(e);\n"
                + "    if (Wire.endTransmission() == 0) { Serial.println(e); }\n"
                + "  }\n"
                + "  Serial.println(\"fim\");\n"
                + "}\n"
                + "void loop() {}");

            janela.TesteRodar();
            EsperarPor(() => LinhasDoSerial(janela.TesteMaquinaDa(placa.Id)).Contains("fim"), 4000);
            var achados = LinhasDoSerial(janela.TesteMaquinaDa(placa.Id));
            janela.TesteParar();

            saida.AppendLine($"    scanner achou: {string.Join(", ", achados)}");

            Conferir("i2c: o scanner acha o LCD ligado (0x27 = 39)",
                achados.Contains("39"), string.Join("|", achados));
            Conferir("i2c: nao acha o MPU6050 que ninguem ligou (0x68 = 104)",
                !achados.Contains("104"), string.Join("|", achados));
        }

        // ---- o painel do professor, do jeito que ele escreveu ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var lcd = new PecaNoCircuito { Tipo = "lcd1602-i2c", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, lcd]);

            var janela = new MainWindow();
            janela.TesteAbrir(c,
                "#include <Wire.h>\n"
                + "#include <LiquidCrystal_I2C.h>\n"
                + "LiquidCrystal_I2C lcd(0x27, 16, 2);\n"
                + "void setup() {\n"
                + "  Wire.begin(21, 22);\n"
                + "  lcd.init();\n"
                + "  lcd.backlight();\n"
                + "  lcd.clear();\n"
                + "  lcd.setCursor(0, 0);\n"
                + "  lcd.print(\"Engine Off\");\n"
                + "}\n"
                + "void loop() {}");

            janela.TesteRodar();
            EsperarPor(() => janela.TesteMaquinaDa(placa.Id).Lcd is { Ligado: true }, 4000);
            string escrito = janela.TesteMaquinaDa(placa.Id).Lcd?.LinhaTexto(0).Trim();
            janela.TesteParar();

            saida.AppendLine($"    painel do professor: \"{escrito}\"");
            Conferir("i2c: o painel do professor escreve no LCD", escrito == "Engine Off", escrito);
        }
    }

    /// <summary>
    /// As duas metades da protoboard sao ilhas separadas.
    ///
    /// Queixa do professor: "os 5 pontos de um lado (A-E) nao deveriam
    /// conectar com os pinos do outro lado (F-J)". A separacao ja estava
    /// nas ligacoes internas do catalogo -- o que faltava era prova, do
    /// jeito que a pessoa ve: um LED que acende de um lado do canal e
    /// fica apagado do outro, no mesmo furo de coluna.
    /// </summary>
    static void Protoboard_MetadesSeparadas()
    {
        saida.AppendLine("as duas metades da protoboard");

        var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard, X = 0, Y = 0 };
        var c = new Circuito();
        c.Pecas.Add(board);
        var rede = new Netlist(c);

        bool Juntos(string a, string b)
            => rede.MesmoNo(Netlist.No(board.Id, a), Netlist.No(board.Id, b));

        Conferir("metades: A5 e E5 sao a mesma ilha", Juntos("A5", "E5"), "separados");
        Conferir("metades: F5 e J5 sao a mesma ilha", Juntos("F5", "J5"), "separados");
        Conferir("metades: E5 NAO alcanca F5", !Juntos("E5", "F5"), "o canal nao separa");
        Conferir("metades: A5 NAO alcanca A6", !Juntos("A5", "A6"),
            "colunas vizinhas ligadas");

        var trilho = Catalogo.ColunasDoTrilho(Catalogo.IdProtoboard).ToList();
        Conferir("trilho: o positivo de cima vai de ponta a ponta",
            Juntos($"T+{trilho[0]}", $"T+{trilho[^1]}"), "trilho partido");
        Conferir("trilho: o + nao encosta no -",
            !Juntos($"T+{trilho[0]}", $"T-{trilho[0]}"), "curto no trilho");
        Conferir("trilho: o de cima nao encosta no de baixo",
            !Juntos($"T+{trilho[0]}", $"B+{trilho[0]}"), "trilhos ligados");

        // ---- a prova que se ve: o mesmo LED, dos dois lados do canal ----
        static double AcendeEm(string furo)
        {
            var circuito = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 700, Y = 400 };
            var bb = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard, X = 0, Y = 0 };
            circuito.Pecas.AddRange([bb, placa]);

            var defBb = Catalogo.Obter(bb.Tipo);
            var defLed = Catalogo.Obter("led");

            // O anodo do LED no furo pedido; o catodo, no furo de baixo
            // da MESMA coluna -- que na metade de cima e o vizinho de
            // linha, e na de baixo tambem.
            var anodo = defLed.Pinos.First(x => x.Nome == "A");
            var alvo = Geometria.PosicaoPino(bb, defBb,
                defBb.Pinos.First(x => x.Nome == furo));

            var led = new PecaNoCircuito
            { Tipo = "led", X = alvo.X - anodo.X, Y = alvo.Y - anodo.Y };
            circuito.Pecas.Add(led);

            // O fio da placa entra sempre em A10: metade de CIMA.
            circuito.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = "D13",
                ParaPeca = bb.Id, ParaPino = "A10"
            });

            // E o catodo volta ao GND direto, fora da protoboard: sem esse
            // caminho o LED nao acende nem na bancada, e o que se mede aqui
            // e so o lado do anodo.
            circuito.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = "GND.1",
                ParaPeca = led.Id, ParaPino = "C"
            });

            var janela = new MainWindow();
            janela.TesteAbrir(circuito,
                "void setup() { pinMode(13, OUTPUT); digitalWrite(13, HIGH); }\n"
                + "void loop() {}");
            janela.TesteRodar();
            EsperarPor(() => janela.TesteEditorCircuito.BrilhoParaTeste(led.Id) > 0.05, 2500);
            double brilho = janela.TesteEditorCircuito.BrilhoParaTeste(led.Id);
            janela.TesteParar();
            return brilho;
        }

        double dentro = AcendeEm("C10");     // mesma ilha do fio (A-E)
        double fora = AcendeEm("H10");       // outra ilha (F-J), mesma coluna

        saida.AppendLine($"        LED em C10 (mesma ilha): brilho {dentro:0.00}");
        saida.AppendLine($"        LED em H10 (do outro lado do canal): brilho {fora:0.00}");

        Conferir("metades: o LED na ilha do fio acende", dentro > 0.05, $"brilho {dentro:0.00}");
        Conferir("metades: o LED do outro lado do canal fica apagado",
            fora <= 0.05, $"brilho {fora:0.00} -- o canal nao esta separando");
    }

    /// <summary>
    /// As pecas que se mexem se mexem mesmo.
    ///
    /// Lista do professor, uma por uma: "Chave deslizante nao e animada,
    /// chave DIP 8 tambem nao. Chave de telefone tambem nao. Encoder
    /// rotativo tambem nao, joystick tambem nao, potenciometro
    /// deslizante tambem nao, Sensor de inclinacao nao. O motor de 4
    /// passos deveria ter alguma animacao(?)".
    ///
    /// Aqui se mede o que esta DESENHADO -- o angulo e o deslocamento da
    /// parte movel na tela -- e nao o estado interno. Peca que nao reage
    /// deixa a pessoa sem saber se o clique pegou.
    /// </summary>
    static void Pecas_Animadas()
    {
        saida.AppendLine("as pecas se mexem");

        static (EditorCircuito ed, PecaNoCircuito peca) Montar(string tipo)
        {
            var c = new Circuito();
            var peca = new PecaNoCircuito { Tipo = tipo, X = 200, Y = 200, Giro2 = 0.5 };
            c.Pecas.Add(peca);

            var ed = new EditorCircuito();
            ed.Carregar(c);
            return (ed, peca);
        }

        // ---- potenciometro: 270 graus de curso ----
        {
            var (ed, peca) = Montar("potentiometer");
            ed.DefinirGiro(peca, 0);
            double esquerda = ed.MovimentoParaTeste(peca.Id).Angulo;
            ed.DefinirGiro(peca, 1);
            double direita = ed.MovimentoParaTeste(peca.Id).Angulo;

            saida.AppendLine($"        potenciometro: {esquerda:0} -> {direita:0} graus");
            Conferir("anima: o potenciometro tem 270 graus de curso",
                Math.Abs(esquerda + 135) < 0.5 && Math.Abs(direita - 135) < 0.5,
                $"{esquerda:0} a {direita:0}");
        }

        // ---- chave deslizante ----
        {
            var (ed, peca) = Montar("slide-switch");
            ed.DefinirGiro(peca, 0);
            double desligada = ed.MovimentoParaTeste(peca.Id).X;
            ed.DefinirGiro(peca, 1);
            double ligada = ed.MovimentoParaTeste(peca.Id).X;

            saida.AppendLine($"        chave deslizante: x {desligada:0.0} -> {ligada:0.0}");
            Conferir("anima: a chave deslizante anda de lado",
                Math.Abs(desligada) < 0.01 && ligada > 3, $"{desligada:0.0} -> {ligada:0.0}");
        }

        // ---- encoder rotativo ----
        {
            var (ed, peca) = Montar("ky-040");
            ed.DefinirGiro(peca, 0.25);
            double angulo = ed.MovimentoParaTeste(peca.Id).Angulo;
            Conferir("anima: o encoder gira com o curso", Math.Abs(angulo - 90) < 0.5,
                $"{angulo:0} graus");
        }

        // ---- potenciometro deslizante ----
        {
            var (ed, peca) = Montar("slide-potentiometer");
            ed.DefinirGiro(peca, 0);
            double comeco = ed.MovimentoParaTeste(peca.Id).X;
            ed.DefinirGiro(peca, 1);
            double fim = ed.MovimentoParaTeste(peca.Id).X;

            saida.AppendLine($"        deslizante: x {comeco:0.0} -> {fim:0.0}");
            Conferir("anima: o cursor do deslizante percorre a regua",
                fim - comeco > 100, $"andou {fim - comeco:0.0} px");
        }

        // ---- joystick: dois eixos ----
        {
            var (ed, peca) = Montar("analog-joystick");
            ed.DefinirGiro(peca, 0.5);
            ed.DefinirGiro2(peca, 0.5);
            var centro = ed.MovimentoParaTeste(peca.Id);

            ed.DefinirGiro(peca, 1);
            ed.DefinirGiro2(peca, 1);
            var canto = ed.MovimentoParaTeste(peca.Id);

            saida.AppendLine($"        manche: centro ({centro.X:0.0};{centro.Y:0.0}) "
                             + $"-> canto ({canto.X:0.0};{canto.Y:0.0})");

            Conferir("anima: o manche parado fica no meio",
                Math.Abs(centro.X) < 0.01 && Math.Abs(centro.Y) < 0.01,
                $"({centro.X:0.0};{centro.Y:0.0})");
            Conferir("anima: empurrado para a direita, o manche vai para a direita",
                canto.X > 3, $"x = {canto.X:0.0}");
            Conferir("anima: com o Y no maximo, o manche sobe",
                canto.Y < -3, $"y = {canto.Y:0.0}");
        }

        // ---- chave DIP de 8 ----
        {
            var (ed, peca) = Montar("dip-switch-8");
            ed.AlternarChaveDip(peca, 2, true);
            ed.RefletirEstado(Simulador.App.Bancada.DeUmaSo(new Maquina()), new Netlist(ed.Atual));

            double ligada = ed.ChaveDipParaTeste(peca.Id, 2);
            double desligada = ed.ChaveDipParaTeste(peca.Id, 3);

            saida.AppendLine($"        DIP: chave 3 em y={ligada:0.0}, chave 4 em y={desligada:0.0}");
            Conferir("anima: a chave DIP ligada sobe para o lado do ON",
                !double.IsNaN(ligada) && !double.IsNaN(desligada) && ligada < desligada - 3,
                $"{ligada:0.0} contra {desligada:0.0}");
        }

        // ---- sensor de inclinacao ----
        {
            var (ed, peca) = Montar("tilt-switch");
            var bancadaSolta = Simulador.App.Bancada.DeUmaSo(new Maquina());

            ed.DefinirGiro(peca, 0);
            ed.RefletirEstado(bancadaSolta, new Netlist(ed.Atual));
            double parado = ed.InclinacaoParaTeste(peca.Id);

            ed.DefinirGiro(peca, 1);
            ed.RefletirEstado(bancadaSolta, new Netlist(ed.Atual));
            double inclinado = ed.InclinacaoParaTeste(peca.Id);

            saida.AppendLine($"        inclinacao: {parado:0} -> {inclinado:0} graus");
            Conferir("anima: o sensor de inclinacao vira de verdade",
                Math.Abs(parado) < 0.5 && Math.Abs(inclinado) > 10,
                $"{parado:0} -> {inclinado:0}");
        }

        // ---- motor de passo de 4 fios ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var motor = new PecaNoCircuito { Tipo = "stepper-motor", X = 500, Y = 0 };
            c.Pecas.AddRange([placa, motor]);

            string[] bobinas = ["A+", "B+", "A-", "B-"];
            int[] gpios = [14, 27, 26, 25];
            for (int k = 0; k < 4; k++)
                c.Fios.Add(new Fio
                {
                    DePeca = placa.Id, DePino = $"D{gpios[k]}",
                    ParaPeca = motor.Id, ParaPino = bobinas[k]
                });

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var bancadaUnica = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);

            // Meia dezena de passos da sequencia, na ordem.
            for (int volta = 0; volta < 6; volta++)
            {
                int fase = volta % 4;
                for (int k = 0; k < 4; k++)
                {
                    maq.Modo[gpios[k]] = 1;
                    maq.Pwm[gpios[k]] = k == fase ? 255 : 0;
                    maq.Digital[gpios[k]] = k == fase ? 1 : 0;
                }
                ed.AplicarEntradas(bancadaUnica, rede);
                ed.RefletirEstado(bancadaUnica, rede);
            }

            double angulo = ed.MovimentoParaTeste(motor.Id).Angulo;
            saida.AppendLine($"        motor de passo: eixo em {angulo:0.0} graus");
            Conferir("anima: o eixo do motor de passo gira com as bobinas",
                Math.Abs(angulo) > 5, $"{angulo:0.0} graus depois de 5 passos");
        }

        // ---- as teclas do controle remoto ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var receptor = new PecaNoCircuito { Tipo = "ir-receiver", X = 300, Y = 0 };
            var controle = new PecaNoCircuito { Tipo = "ir-remote", X = 600, Y = 0 };
            c.Pecas.AddRange([placa, receptor, controle]);
            c.Fios.Add(new Fio { DePeca = receptor.Id, DePino = "DAT",
                                 ParaPeca = placa.Id, ParaPino = "D15" });

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var bancadaUnica = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);

            bool pegou = ed.ApertarTeclaDoControle(controle.Id, "5");
            ed.AplicarEntradas(bancadaUnica, rede);
            double codigo = maq.Leitura("ir:codigo");

            ed.Soltar();
            maq.Leituras["ir:codigo"] = 0;           // o resume() do sketch: o receptor guarda ate ele
            ed.AplicarEntradas(bancadaUnica, rede);
            double depois = maq.Leitura("ir:codigo");

            saida.AppendLine($"        controle: tecla 5 -> 0x{(long)codigo:X}, "
                             + $"solta -> {depois:0}");

            Conferir("anima: a tecla do controle responde ao clique", pegou, "clique no vazio");
            Conferir("anima: a tecla 5 manda o codigo dela",
                (long)codigo == 0xFF38C7, $"0x{(long)codigo:X}");
            Conferir("anima: solta a tecla, o codigo para de chegar",
                depois != 0xFF38C7, $"{depois:0}");
        }

        // ---- a rampa do analogico ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0 };
            var r = new PecaNoCircuito { Tipo = "resistor", X = 250, Y = 0, Ohms = 220 };
            c.Pecas.AddRange([placa, led, r]);
            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D14",
                                 ParaPeca = r.Id, ParaPino = "1" });
            c.Fios.Add(new Fio { DePeca = r.Id, DePino = "2",
                                 ParaPeca = led.Id, ParaPino = "A" });
            c.Fios.Add(new Fio { DePeca = led.Id, DePino = "C",
                                 ParaPeca = placa.Id, ParaPino = "GND.2" });

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.Modo[14] = 1;
            var bancadaUnica = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);

            double Brilho(int pwm)
            {
                maq.Pwm[14] = pwm;
                maq.Digital[14] = pwm > 0 ? 1 : 0;
                ed.RefletirEstado(bancadaUnica, rede);
                return ed.BrilhoParaTeste(led.Id);
            }

            double zero = Brilho(0), baixo = Brilho(20), meio = Brilho(128), cheio = Brilho(255);
            saida.AppendLine($"        rampa: 0={zero:0.00} 20={baixo:0.00} "
                             + $"128={meio:0.00} 255={cheio:0.00}");

            Conferir("anima: apagado e apagado", zero == 0, $"{zero:0.00}");
            Conferir("anima: o primeiro degrau do PWM e fraco mesmo",
                baixo > 0 && baixo < 0.35, $"{baixo:0.00}");
            Conferir("anima: o brilho sobe sem degrau",
                baixo < meio && meio < cheio, $"{baixo:0.00} < {meio:0.00} < {cheio:0.00}");
            Conferir("anima: no maximo o LED fica bem aceso", cheio > 0.85, $"{cheio:0.00}");
        }
    }

    /// <summary>
    /// Jogar com o circuito: teclado e controle de video game no lugar
    /// do mouse.
    ///
    /// Pedido de qualidade de vida: "controlar joystick com WASD ou
    /// mesmo um proprio joystick de Xbox, botoes com J, K, L, I ou X, B,
    /// A e Y do Xbox, os triggers, botoes, Enter". Com dois botoes num
    /// circuito, testar no mouse e impossivel -- so da para apertar um
    /// de cada vez.
    ///
    /// O controle de verdade nao existe na maquina do teste, entao o
    /// XInput e trocado por um estado de mentira. O que importa provar e
    /// o caminho de dentro: forca do vinculo -> estado da peca -> pino
    /// da placa.
    /// </summary>
    static void Vinculos_TecladoEControle()
    {
        saida.AppendLine("teclas e controle no circuito");

        // ---- a tabela de acoes de cada peca ----
        {
            var doBotao = Vinculos.Acoes("pushbutton");
            var doManche = Vinculos.Acoes("analog-joystick");
            var doLed = Vinculos.Acoes("led");

            Conferir("vinculo: o botao tem a acao de apertar",
                doBotao.Count == 1 && doBotao[0].Acao == Vinculos.Apertar,
                string.Join(", ", doBotao.Select(x => x.Acao)));
            Conferir("vinculo: o manche tem quatro direcoes e o aperto",
                doManche.Count == 5, string.Join(", ", doManche.Select(x => x.Acao)));
            Conferir("vinculo: peca sem entrada nao aceita tecla",
                doLed.Count == 0 && !Vinculos.Aceita("led"), "aceitou");
        }

        // ---- nome do que esta ligado ----
        {
            string tecla = Vinculos.DeTecla(System.Windows.Input.Key.J);
            saida.AppendLine($"        J vira \"{tecla}\" e aparece como \"{Vinculos.Rotulo(tecla)}\"");

            Conferir("vinculo: a tecla vira texto curto", tecla == "t:J", tecla);
            Conferir("vinculo: e volta com nome legivel",
                Vinculos.Rotulo(tecla) == "J", Vinculos.Rotulo(tecla));
            Conferir("vinculo: o botao do controle tambem tem nome",
                Vinculos.Rotulo("g:A") == "Botão A", Vinculos.Rotulo("g:A"));
            Conferir("vinculo: sem nada ligado, aparece um traco",
                Vinculos.Rotulo(null) == "—", Vinculos.Rotulo(null));

            // O F5 e do programa: parar a simulacao tem que continuar
            // funcionando mesmo no meio de uma partida.
            Conferir("vinculo: o F5 nao vira atalho de peca",
                Vinculos.Reservada(System.Windows.Input.Key.F5), "aceitou o F5");
            Conferir("vinculo: o J vira", !Vinculos.Reservada(System.Windows.Input.Key.J),
                "recusou o J");
        }

        // ---- forca: gatilho e manche sao analogicos ----
        {
            var solto = EstadoDoControle.Desligado;
            var comA = new EstadoDoControle(true, EstadoDoControle.A, 0, 0, 0, 0, 0, 0);
            var meioGatilho = new EstadoDoControle(true, 0, 0.5, 0, 0, 0, 0, 0);
            var mancheAEsquerda = new EstadoDoControle(true, 0, 0, 0, -0.8, 0, 0, 0);

            Conferir("vinculo: sem controle, nada aciona",
                Vinculos.Forca("g:A", solto) == 0, "acionou");
            Conferir("vinculo: o botao A aciona", Vinculos.Forca("g:A", comA) == 1, "nao acionou");
            Conferir("vinculo: o gatilho e analogico",
                Math.Abs(Vinculos.Forca("g:LT", meioGatilho) - 0.5) < 0.01,
                $"{Vinculos.Forca("g:LT", meioGatilho):0.00}");
            Conferir("vinculo: o manche para a esquerda so aciona a esquerda",
                Vinculos.Forca("g:LSEsquerda", mancheAEsquerda) > 0.7
                && Vinculos.Forca("g:LSDireita", mancheAEsquerda) == 0,
                $"esq={Vinculos.Forca("g:LSEsquerda", mancheAEsquerda):0.00} "
                + $"dir={Vinculos.Forca("g:LSDireita", mancheAEsquerda):0.00}");
        }

        // ---- o circuito obedecendo ao controle ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var manche = new PecaNoCircuito
            {
                Tipo = "analog-joystick", X = 400, Y = 0, Giro = 0.5, Giro2 = 0.5,
                Teclas = new Dictionary<string, string>
                {
                    [Vinculos.Esquerda] = "g:LSEsquerda",
                    [Vinculos.Direita] = "g:LSDireita",
                    [Vinculos.Cima] = "g:LSCima",
                    [Vinculos.Baixo] = "g:LSBaixo",
                    [Vinculos.Apertar] = "g:A",
                },
            };
            var botao = new PecaNoCircuito
            {
                Tipo = "pushbutton", X = 400, Y = 300,
                Teclas = new Dictionary<string, string> { [Vinculos.Apertar] = "g:X" },
            };
            c.Pecas.AddRange([placa, manche, botao]);

            c.Fios.Add(new Fio { DePeca = manche.Id, DePino = "HORZ",
                                 ParaPeca = placa.Id, ParaPino = "D34" });
            c.Fios.Add(new Fio { DePeca = manche.Id, DePino = "SEL",
                                 ParaPeca = placa.Id, ParaPino = "D32" });
            c.Fios.Add(new Fio { DePeca = botao.Id, DePino = "1.l",
                                 ParaPeca = placa.Id, ParaPino = "D33" });

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            var bancadaUnica = Simulador.App.Bancada.DeUmaSo(maq);
            var rede = new Netlist(c);

            // Parado, o controle nao mexe em nada: as teclas ainda sao
            // do programa.
            Controle.Simulado = new EstadoDoControle(
                true, EstadoDoControle.X, 0, 0, 1, 0, 0, 0);
            ed.EmSimulacao = false;
            ed.AplicarEntradas(bancadaUnica, rede);
            double paradoX = manche.Giro;

            // Rodando, obedece.
            ed.EmSimulacao = true;
            ed.AplicarEntradas(bancadaUnica, rede);
            double comManche = manche.Giro;
            int adc = maq.Analogico[34];
            bool botaoApertado = maq.Pressionado[33];

            // Soltou tudo: o manche volta ao meio sozinho.
            Controle.Simulado = EstadoDoControle.Desligado;
            ed.AplicarEntradas(bancadaUnica, rede);
            double voltou = manche.Giro;
            bool aindaApertado = maq.Pressionado[33];

            Controle.Simulado = null;

            saida.AppendLine($"        manche: parado {paradoX:0.00} -> com o controle "
                             + $"{comManche:0.00} (ADC {adc}) -> solto {voltou:0.00}");

            Conferir("vinculo: parado, o controle nao mexe no circuito",
                Math.Abs(paradoX - 0.5) < 1e-6, $"{paradoX:0.00}");
            Conferir("vinculo: rodando, o manche do controle empurra o do circuito",
                comManche > 0.9, $"{comManche:0.00}");
            Conferir("vinculo: e isso chega no ADC da placa", adc > 3500, $"{adc}");
            Conferir("vinculo: o botao X aperta o botao do circuito",
                botaoApertado, "nao apertou");
            Conferir("vinculo: soltando, o manche volta ao meio",
                Math.Abs(voltou - 0.5) < 1e-6, $"{voltou:0.00}");
            Conferir("vinculo: soltando, o botao solta", !aindaApertado, "continuou apertado");
        }

        // ---- os atalhos de uma tecla calam durante a partida ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var botao = new PecaNoCircuito
            {
                Tipo = "pushbutton", X = 400, Y = 0,
                Teclas = new Dictionary<string, string> { [Vinculos.Apertar] = "t:J" },
            };
            c.Pecas.AddRange([placa, botao]);

            var janela = new MainWindow();
            janela.TesteAbrir(c, "void setup() {} void loop() {}");

            bool antes = janela.TesteAtalhoDeUmaTeclaVale();
            janela.TesteRodar();
            bool durante = janela.TesteAtalhoDeUmaTeclaVale();
            janela.TesteParar();
            bool depois = janela.TesteAtalhoDeUmaTeclaVale();

            saida.AppendLine($"        atalhos de uma tecla: antes={antes} "
                             + $"rodando={durante} depois={depois}");

            Conferir("vinculo: parado, os atalhos do programa valem", antes, "calados a toa");
            Conferir("vinculo: rodando com teclas vinculadas, os atalhos calam",
                !durante, "o A ainda abriria a paleta no meio da partida");
            Conferir("vinculo: parou, os atalhos voltam", depois, "ficaram calados");
        }

        // ---- as teclas viajam no arquivo e na copia ----
        {
            var c = new Circuito();
            var botao = new PecaNoCircuito
            {
                Tipo = "pushbutton", X = 10, Y = 10,
                Teclas = new Dictionary<string, string> { [Vinculos.Apertar] = "t:K" },
            };
            c.Pecas.Add(botao);

            var copia = botao.Copia();
            copia.Teclas[Vinculos.Apertar] = "t:L";

            string temporario = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"teclas-{Guid.NewGuid():N}.sktch");
            Sktch.Salvar(temporario, c, "void setup() {}");
            var (volta, _) = Sktch.Abrir(temporario);
            System.IO.File.Delete(temporario);

            Conferir("vinculo: a copia leva as teclas",
                copia.Teclas.GetValueOrDefault(Vinculos.Apertar) == "t:L"
                && botao.Teclas[Vinculos.Apertar] == "t:K",
                $"copia={copia.Teclas.GetValueOrDefault(Vinculos.Apertar)}, "
                + $"original={botao.Teclas[Vinculos.Apertar]}");

            Conferir("vinculo: as teclas vao junto no .sktch",
                volta.Pecas[0].Teclas?.GetValueOrDefault(Vinculos.Apertar) == "t:K",
                volta.Pecas[0].Teclas?.GetValueOrDefault(Vinculos.Apertar) ?? "nenhuma");
        }
    }

    /// <summary>
    /// As pecas de esquema: juncao, simbolo de VCC, simbolo de GND e o
    /// display de 240x240.
    ///
    /// As tres primeiras nao vem dos wokwi-elements -- sao desenho
    /// proprio -- e as duas do meio so valem alguma coisa se o resto do
    /// programa as tratar como trilho de verdade.
    /// </summary>
    static void Pecas_DeEsquema()
    {
        saida.AppendLine("juncao, VCC, GND e o display de 240x240");

        // ---- a juncao junta os quatro lados ----
        {
            var c = new Circuito();
            var no = new PecaNoCircuito { Tipo = "junction", X = 100, Y = 100 };
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 400, Y = 0 };
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 300 };
            c.Pecas.AddRange([no, placa, led]);

            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D14",
                                 ParaPeca = no.Id, ParaPino = "1" });
            c.Fios.Add(new Fio { DePeca = no.Id, DePino = "3",
                                 ParaPeca = led.Id, ParaPino = "A" });

            var rede = new Netlist(c);
            Conferir("juncao: os quatro pinos sao um no so",
                rede.MesmoNo(Netlist.No(no.Id, "1"), Netlist.No(no.Id, "4")), "separados");
            Conferir("juncao: o sinal atravessa ela",
                rede.GpioDe(c, led.Id, "A") == 14, $"{rede.GpioDe(c, led.Id, "A")}");
        }

        // ---- o simbolo de VCC acende um LED ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var vcc = new PecaNoCircuito { Tipo = "vcc-symbol", X = 400, Y = 0 };
            var gnd = new PecaNoCircuito { Tipo = "gnd-symbol", X = 400, Y = 300 };
            var r = new PecaNoCircuito { Tipo = "resistor", X = 450, Y = 120, Ohms = 220 };
            var led = new PecaNoCircuito { Tipo = "led", X = 500, Y = 180 };
            c.Pecas.AddRange([placa, vcc, gnd, r, led]);

            c.Fios.Add(new Fio { DePeca = vcc.Id, DePino = "VCC",
                                 ParaPeca = r.Id, ParaPino = "1" });
            c.Fios.Add(new Fio { DePeca = r.Id, DePino = "2",
                                 ParaPeca = led.Id, ParaPino = "A" });
            c.Fios.Add(new Fio { DePeca = led.Id, DePino = "C",
                                 ParaPeca = gnd.Id, ParaPino = "GND" });

            var janela = new MainWindow();
            janela.TesteAbrir(c, "void setup() {} void loop() {}");

            double apagado = janela.TesteEditorCircuito.BrilhoParaTeste(led.Id);
            janela.TesteRodar();
            EsperarPor(() => janela.TesteEditorCircuito.BrilhoParaTeste(led.Id) > 0.05, 2500);
            double aceso = janela.TesteEditorCircuito.BrilhoParaTeste(led.Id);
            janela.TesteParar();

            saida.AppendLine($"        LED no simbolo de VCC: parado {apagado:0.00}, "
                             + $"rodando {aceso:0.00}");

            Conferir("simbolo: com o circuito parado o LED fica apagado",
                apagado <= 0.05, $"{apagado:0.00}");
            Conferir("simbolo: ligado no VCC e no GND, o LED acende",
                aceso > 0.5, $"{aceso:0.00}");
        }

        // ---- e sem resistor, queima ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var vcc = new PecaNoCircuito { Tipo = "vcc-symbol", X = 400, Y = 0 };
            var gnd = new PecaNoCircuito { Tipo = "gnd-symbol", X = 400, Y = 300 };
            var led = new PecaNoCircuito { Tipo = "led", X = 500, Y = 180 };
            c.Pecas.AddRange([placa, vcc, gnd, led]);

            c.Fios.Add(new Fio { DePeca = vcc.Id, DePino = "VCC",
                                 ParaPeca = led.Id, ParaPino = "A" });
            c.Fios.Add(new Fio { DePeca = led.Id, DePino = "C",
                                 ParaPeca = gnd.Id, ParaPino = "GND" });

            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();

            var achados = Queima.Conferir(c, new Netlist(c), maq);
            var doLed = achados.FirstOrDefault(x => x.Peca == led.Id);
            saida.AppendLine($"        sem resistor no simbolo: {doLed?.Motivo}");

            Conferir("simbolo: o trilho do simbolo queima LED sem resistor",
                doLed is { Queimou: true }, doLed?.Motivo ?? "nenhum achado");
        }

        // ---- o display de 240x240 ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var tela = new PecaNoCircuito { Tipo = "st7789", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, tela]);

            // Display solto nao mostra nada, e esta certo: quem manda na
            // tela e a placa em que ela esta ligada.
            foreach (var (pino, alvo) in new[] { ("SDA", "D23"), ("SCL", "D18"),
                                                 ("CS", "D5"), ("DC", "D2") })
                c.Fios.Add(new Fio { DePeca = tela.Id, DePino = pino,
                                     ParaPeca = placa.Id, ParaPino = alvo });

            var janela = new MainWindow();
            janela.TesteAbrir(c,
                "#include <Adafruit_ST7789.h>\n"
                + "Adafruit_ST7789 tft = Adafruit_ST7789(5, 2, 4);\n"
                + "void setup() {\n"
                + "  tft.init(240, 240);\n"
                + "  tft.fillScreen(0x001F);\n"
                + "  tft.setCursor(10, 20);\n"
                + "  tft.print(\"oi\");\n"
                + "}\n"
                + "void loop() {}");

            janela.TesteRodar();
            // O texto e pixel e chega no quadro em que o print ja rodou --
            // que pode ser o segundo, e nao o primeiro.
            EsperarPor(() => janela.TesteEditorCircuito.TftParaTeste(tela.Id).brancos > 10, 3000);
            var desenhada = janela.TesteEditorCircuito.TftParaTeste(tela.Id);
            janela.TesteParar();

            saida.AppendLine($"        ST7789: criada={desenhada.criada} "
                             + $"{desenhada.largura}x{desenhada.altura}, "
                             + $"{desenhada.brancos} ponto(s) brancos");

            Conferir("240x240: o display existe na tela", desenhada.criada, "nao criou");
            Conferir("240x240: o buffer e quadrado, de 240 por 240",
                desenhada.largura == 240 && desenhada.altura == 240,
                $"{desenhada.largura}x{desenhada.altura}");
            Conferir("240x240: o texto do sketch chega no vidro",
                desenhada.brancos > 10, $"{desenhada.brancos} pontos brancos");
        }

        // ---- o construtor escrito por extenso ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            string erro = null;
            try
            {
                var prog = new Parser(Lexer.Tokenizar(
                    "Adafruit_ILI9341 tft = Adafruit_ILI9341(15, 2);" + Environment.NewLine
                    + "void setup() { tft.begin(); tft.fillScreen(0x1234); }" + Environment.NewLine
                    + "void loop() {}")).AnalisarPrograma();
                var interp = new Interpretador(prog, maq);
                interp.Preparar();
                interp.Setup();
            }
            catch (Exception e) { erro = e.Message; }

            Conferir("240x240: \"Classe x = Classe(...)\" e construtor, nao funcao",
                erro is null, erro);
        }
    }

    /// <summary>
    /// O que ficou pronto do acabamento: descricao do projeto,
    /// preferencias que sobrevivem a fechar o programa, a foto do
    /// circuito e a tela de abertura.
    ///
    /// O teste mexe nas preferencias DE VERDADE (e o mesmo arquivo em
    /// %APPDATA%), entao guarda o que estava la e devolve no fim: quem
    /// roda o autoteste nao pode perder a propria configuracao.
    /// </summary>
    static void Programa_Acabamento()
    {
        saida.AppendLine("descricao, preferencias, foto e abertura");

        // ---- a descricao vai junto no arquivo ----
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 });

            string temporario = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"desc-{Guid.NewGuid():N}.sktch");

            const string texto = "Semaforo de duas vias.\nO botao do pedestre corta o ciclo.";
            Sktch.Salvar(temporario, c, "void setup() {}", texto);

            var (_, _, volta) = Sktch.AbrirCompleto(temporario);
            string comoEstaNoArquivo = System.IO.File.ReadAllText(temporario);
            System.IO.File.Delete(temporario);

            Conferir("descricao: volta inteira do .sktch",
                volta?.Replace("\r\n", "\n") == texto, volta);
            Conferir("descricao: fica uma linha por item, legivel no bloco de notas",
                comoEstaNoArquivo.Contains("\"descricao\"")
                && comoEstaNoArquivo.Contains("O botao do pedestre"),
                "nao achei no arquivo");

            // Projeto sem descricao nao ganha campo a toa.
            string semDescricao = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"desc2-{Guid.NewGuid():N}.sktch");
            Sktch.Salvar(semDescricao, c, "void setup() {}");
            string cru = System.IO.File.ReadAllText(semDescricao);
            var (_, _, nada) = Sktch.AbrirCompleto(semDescricao);
            System.IO.File.Delete(semDescricao);

            Conferir("descricao: projeto sem descricao nao ganha campo vazio",
                !cru.Contains("\"descricao\"") && nada is null, "ganhou");
        }

        // ---- a descricao chega na janela e suja o projeto ----
        {
            var janela = new MainWindow();
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Tipo = "arduino-uno", X = 0, Y = 0 });
            janela.TesteAbrir(c, "void setup() {} void loop() {}");

            bool limpoAntes = janela.TestePodeFecharSemPerguntar();
            janela.TesteDescricao = "isto aqui e novo";
            bool limpoDepois = janela.TestePodeFecharSemPerguntar();

            Conferir("descricao: projeto recem-aberto nao pergunta nada", limpoAntes,
                "ja achava que estava sujo");
            Conferir("descricao: escrever na aba suja o projeto", !limpoDepois,
                "fechar jogaria a descricao fora sem avisar");
        }

        // ---- preferencias ----
        {
            string grade = Preferencias.Texto(Preferencias.Grade);
            string estilo = Preferencias.Texto(Preferencias.EstiloDoFio);
            try
            {
                Preferencias.Definir(Preferencias.Grade, false);
                Preferencias.Definir(Preferencias.EstiloDoFio, "degrau");
                Preferencias.Recarregar();

                Conferir("preferencia: o que foi desligado continua desligado",
                    !Preferencias.Ligado(Preferencias.Grade, true), "voltou ligada");
                Conferir("preferencia: o texto volta igual",
                    Preferencias.Texto(Preferencias.EstiloDoFio) == "degrau",
                    Preferencias.Texto(Preferencias.EstiloDoFio));
                Conferir("preferencia: chave que nao existe cai no padrao",
                    Preferencias.Ligado("nao-existe-isto", true)
                    && Preferencias.Texto("nao-existe-isto", "padrao") == "padrao",
                    "nao caiu");

                // E a janela principal obedece ao que esta gravado.
                var janela = new MainWindow();
                janela.TesteAplicarPreferencias();

                Conferir("preferencia: a janela abre com a grade desligada",
                    !janela.TesteEditorCircuito.MostrarGrade, "abriu com a grade ligada");
                Conferir("preferencia: e com o tracado de fio escolhido",
                    janela.TesteEditorCircuito.EstiloPadrao == "degrau",
                    janela.TesteEditorCircuito.EstiloPadrao);
            }
            finally
            {
                // Devolve o que era da pessoa.
                Preferencias.Definir(Preferencias.Grade, grade);
                Preferencias.Definir(Preferencias.EstiloDoFio, estilo);
                Preferencias.Recarregar();
            }
        }

        // ---- a foto do circuito ----
        {
            string a = Miniaturas.CaminhoDe(@"C:\aula\projeto.sktch");
            string b = Miniaturas.CaminhoDe(@"C:\outra\projeto.sktch");

            Conferir("foto: dois projetos com o mesmo nome nao dividem a foto",
                a != b, "mesma foto");
            Conferir("foto: o mesmo caminho da sempre a mesma foto",
                a == Miniaturas.CaminhoDe(@"C:\AULA\projeto.sktch"), "mudou");

            var editor = new EditorCircuito();
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 20, Y = 20 });
            editor.Carregar(c);

            // Sem janela na tela o WPF nao mede nada sozinho.
            editor.Measure(new System.Windows.Size(420, 300));
            editor.Arrange(new System.Windows.Rect(0, 0, 420, 300));
            editor.UpdateLayout();

            string projeto = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"foto-{Guid.NewGuid():N}.sktch");

            Miniaturas.Guardar(projeto, editor);
            var foto = Miniaturas.Ler(projeto);

            saida.AppendLine($"        foto: {foto?.PixelWidth}x{foto?.PixelHeight}");
            Conferir("foto: o circuito salvo vira imagem", foto is not null, "nao gravou");
            Conferir("foto: no tamanho do cartao da tela de abertura",
                foto is not null && foto.PixelWidth == Miniaturas.Largura
                && foto.PixelHeight == Miniaturas.Altura,
                $"{foto?.PixelWidth}x{foto?.PixelHeight}");

            try { System.IO.File.Delete(Miniaturas.CaminhoDe(projeto)); } catch { }
        }

        // ---- a tela de abertura ----
        {
            var janela = new MainWindow();
            var abertura = new JanelaAbertura(janela);
            abertura.Measure(new System.Windows.Size(780, 480));

            Conferir("abertura: abre sem escolher nada",
                abertura.PlacaEscolhida is null && abertura.ProjetoEscolhido is null,
                "ja veio escolhida");

            // O teste fechado da Alpha 24: o autor na faixa, o aviso de versao
            // em desenvolvimento e o Roadmap no lugar do "O que mudou" e do
            // "Apoiar o projeto".
            var textos = new List<string>();
            void Varrer(object o)
            {
                if (o is System.Windows.Controls.TextBlock bloco)
                    textos.Add(bloco.Inlines.Count > 0
                        ? string.Concat(bloco.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)) : bloco.Text);
                if (o is System.Windows.Controls.Button { Content: string rotulo }) textos.Add(rotulo);
                if (o is System.Windows.DependencyObject d)
                    foreach (object filho in System.Windows.LogicalTreeHelper.GetChildren(d)) Varrer(filho);
            }
            Varrer(abertura);
            Conferir("abertura: o autor no canto da faixa", textos.Contains("por Emannuel Serafim Oliveira"),
                string.Join(" | ", textos.Take(8)));
            Conferir("abertura: o aviso de que esta em desenvolvimento e nao e a versao final",
                textos.Any(t => t.Contains("Versão de teste") && t.Contains("não é a versão final")), string.Join(" | ", textos.Take(8)));
            Conferir("abertura: o botao Roadmap, sem o \"O que mudou\" e o \"Apoiar o projeto\"",
                textos.Contains("Roadmap") && !textos.Contains("O que mudou") && !textos.Contains("Apoiar o projeto"),
                string.Join(" | ", textos.Where(t => t.Length < 30)));
            Conferir("abertura: o roadmap tem a Alpha 24, a 25 e a 26/Beta 1",
                JanelaAbertura.TextoDoRoadmap.Contains("ALPHA 24") && JanelaAbertura.TextoDoRoadmap.Contains("ALPHA 25")
                && JanelaAbertura.TextoDoRoadmap.Contains("ALPHA 26  /  BETA 1"), "faltou uma");
            Conferir("abertura: o changelog veio embutido no programa",
                JanelaAbertura.TextoEmbutido("changelog.txt")?.Contains("CircuitSketch") == true,
                "nao achei o changelog");

            // O "Mais placas" agrupado pela familia, sem perder nenhuma.
            var resto = PerfilPlaca.Conhecidos.Skip(5).ToList();
            var escolhida = "";
            var menu = JanelaAbertura.MenuDeMaisPlacas(resto, id => escolhida = id);
            var grupos = menu.Items.OfType<System.Windows.Controls.MenuItem>().ToList();
            var ids = grupos.SelectMany(g => g.Items.OfType<System.Windows.Controls.MenuItem>())
                            .Select(i => (string)i.Tag).ToList();

            saida.AppendLine($"        mais placas: {resto.Count} em {grupos.Count} familia(s): "
                             + string.Join(", ", grupos.Select(g => g.Header)));

            Conferir("abertura: o 'Mais placas' vem agrupado pela familia",
                grupos.Count >= 4 && grupos.All(g => g.Items.Count > 0),
                string.Join(", ", grupos.Select(g => g.Header)));
            Conferir("abertura: o agrupamento nao perde nem repete placa",
                ids.Count == resto.Count && ids.Distinct().Count() == ids.Count
                && resto.All(p => ids.Contains(p.Id)),
                $"{ids.Count} de {resto.Count}");

            List<string> IdsDoGrupo(string prefixo) => grupos
                .FirstOrDefault(g => ((string)g.Header).StartsWith(prefixo + " "))?
                .Items.OfType<System.Windows.Controls.MenuItem>().Select(i => (string)i.Tag).ToList() ?? [];

            var esp32 = IdsDoGrupo("ESP32");
            var esp8266 = IdsDoGrupo("ESP8266");

            Conferir("abertura: as ESP32 ficam no grupo delas, sem ESP8266 no meio",
                esp32.Contains("esp32-c3-devkit") && esp32.All(id => id.StartsWith("esp32")),
                string.Join(", ", esp32));
            Conferir("abertura: as ESP8266 ficam no grupo delas, sem ESP32 no meio",
                esp8266.Contains("esp-01") && esp8266.All(id => !id.StartsWith("esp32")),
                string.Join(", ", esp8266));

            // Escolher uma placa monta um circuito com ela.
            janela.ComecarComPlaca("arduino-mega");
            var placas = janela.TesteCircuito.Placas().ToList();

            saida.AppendLine($"        abertura: circuito novo com "
                             + string.Join(", ", placas.Select(x => x.Tipo)));

            Conferir("abertura: escolher a placa ja poe a placa no circuito",
                placas.Count == 1 && placas[0].Tipo == "arduino-mega",
                string.Join(", ", placas.Select(x => x.Tipo)));
            Conferir("abertura: e a placa fica guardada para a proxima vez",
                Preferencias.Texto(Preferencias.UltimaPlaca) == "arduino-mega",
                Preferencias.Texto(Preferencias.UltimaPlaca));
        }
    }

    static void Visao_DeCircuito()
    {
        saida.AppendLine("visao de circuito: por onde a corrente passa");

        static (Circuito c, PecaNoCircuito placa, Maquina maq) Bancada(params int[] emAlta)
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            c.Pecas.Add(placa);

            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            foreach (int p in emAlta)
            {
                maq.Modo[p] = 1;
                maq.Digital[p] = 1;
                maq.Pwm[p] = 255;
            }
            return (c, placa, maq);
        }

        static void Ligar(Circuito c, PecaNoCircuito a, string pa, PecaNoCircuito b, string pb)
            => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb });

        {
            var (c, placa, maq) = Bancada(13);
            var r = new PecaNoCircuito { Tipo = "resistor", X = 200, Y = 0, Ohms = 220 };
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0 };
            c.Pecas.AddRange([r, led]);

            Ligar(c, placa, "D13", r, "1");
            Ligar(c, r, "2", led, "A");
            Ligar(c, led, "C", placa, "GND.2");

            var energia = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(maq));

            double corrente = energia.CorrenteDaPeca(led.Id) * 1000;
            saida.AppendLine($"        LED com 220 ohms: {corrente:0.0} mA, "
                             + $"nivel {energia.NivelDaPeca(led.Id)}");


            Conferir("energia: LED com resistor conduz alguns mA",
                corrente > 2 && corrente < 12, $"{corrente:0.0} mA");
            Conferir("energia: e o nivel e aceitavel",
                energia.NivelDaPeca(led.Id) != NivelDeEnergia.Demais,
                $"{energia.NivelDaPeca(led.Id)}");
        }

        {
            var (c, placa, maq) = Bancada(12);
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0 };
            c.Pecas.Add(led);

            Ligar(c, placa, "D12", led, "A");
            Ligar(c, led, "C", placa, "GND.2");

            var energia = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(maq));

            double corrente = energia.CorrenteDaPeca(led.Id) * 1000;

            saida.AppendLine($"        LED direto no pino: {corrente:0.0} mA, "
                             + $"fio {energia.NivelDoPino(led.Id, "A")}, "
                             + $"peca {energia.NivelDaPeca(led.Id)}");

            Conferir("energia: LED sem resistor puxa muito mais",
                corrente > 15, $"{corrente:0.0} mA");
            Conferir("energia: e o fio dele aparece com corrente",
                energia.NivelDoPino(led.Id, "A") is NivelDeEnergia.Aceitavel
                    or NivelDeEnergia.Demais,
                $"{energia.NivelDoPino(led.Id, "A")}");
        }

        {
            var (c, placa, maq) = Bancada();
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0 };
            var r = new PecaNoCircuito { Tipo = "resistor", X = 200, Y = 0, Ohms = 220 };
            c.Pecas.AddRange([r, led]);

            Ligar(c, placa, "D13", r, "1");
            Ligar(c, r, "2", led, "A");
            Ligar(c, led, "C", placa, "GND.2");

            var energia = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(maq));

            Conferir("energia: pino em LOW nao acende nem conduz",
                energia.CorrenteDaPeca(led.Id) < Energia.SemCorrente,
                $"{energia.CorrenteDaPeca(led.Id) * 1000:0.00} mA");
        }

        {
            var (c, placa, maq) = Bancada();
            var servo = new PecaNoCircuito { Tipo = "servo", X = 400, Y = 0 };
            c.Pecas.Add(servo);

            Ligar(c, placa, "VIN", servo, "V+");
            Ligar(c, placa, "GND.2", servo, "GND");

            var energia = Energia.Calcular(c, Simulador.App.Bancada.DeUmaSo(maq));

            double corrente = energia.CorrenteDaPeca(servo.Id) * 1000;
            saida.AppendLine($"        servo no trilho de 5 V: {corrente:0} mA");

            Conferir("energia: o servo puxa centenas de mA do trilho",
                corrente > 200, $"{corrente:0} mA");
        }

        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0 };
            var r = new PecaNoCircuito { Tipo = "resistor", X = 200, Y = 0, Ohms = 220 };
            c.Pecas.AddRange([placa, r, led]);

            Ligar(c, placa, "D13", r, "1");
            Ligar(c, r, "2", led, "A");
            Ligar(c, led, "C", placa, "GND.2");

            var janela = new MainWindow();
            janela.TesteAbrir(c, "void setup() { pinMode(13, OUTPUT); digitalWrite(13, HIGH); }\n"
                                 + "void loop() {}");
            janela.TesteRodar();
            EsperarPor(() => janela.TesteEditorCircuito.BrilhoParaTeste(led.Id) > 0.05, 2500);

            janela.MostrarModo("circuito");
            EsperarPor(() => janela.TesteEditorCircuito.EnergiaAtual is not null, 2000);

            var energia = janela.TesteEditorCircuito.EnergiaAtual;
            string resumo = janela.TesteEditorCircuito.ResumoDaEnergia();
            bool cinza = janela.TesteEditorCircuito.EstaEmCinzaParaTeste(led.Id);

            janela.MostrarModo("simulacao");
            bool voltouColorido = !janela.TesteEditorCircuito.EstaEmCinzaParaTeste(led.Id);
            janela.TesteParar();

            saida.AppendLine($"        na janela: {resumo}");

            Conferir("visao: o modo calcula a energia do circuito",
                energia is not null && energia.CorrenteDaPeca(led.Id) > 0,
                "nao calculou");
            Conferir("visao: as pecas ficam em preto e branco", cinza, "continuaram coloridas");
            Conferir("visao: voltando para a simulacao, a cor volta", voltouColorido,
                "ficaram cinzas");
            Conferir("visao: o resumo explica as cores",
                resumo.Contains("amarelo") && resumo.Contains("vermelho"), resumo);
        }
    }

    static void Pecas_Logica()
    {
        saida.AppendLine("portas logicas, flip-flops e clock");

        static (Circuito c, PecaNoCircuito vcc, PecaNoCircuito gnd) Trilhos()
        {
            var c = new Circuito();
            var vcc = new PecaNoCircuito { Tipo = "vcc-symbol", X = 0, Y = 0 };
            var gnd = new PecaNoCircuito { Tipo = "gnd-symbol", X = 0, Y = 200 };
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 600, Y = 0 };
            c.Pecas.AddRange([vcc, gnd, placa]);
            return (c, vcc, gnd);
        }

        static void Ligar(Circuito c, PecaNoCircuito a, string pa, PecaNoCircuito b, string pb)
            => c.Fios.Add(new Fio { DePeca = a.Id, DePino = pa, ParaPeca = b.Id, ParaPino = pb });

        static Logica Rodar(Circuito c, Dictionary<string, int> memoria = null, long ms = 0)
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            return Logica.Avaliar(c, new Netlist(c), Simulador.App.Bancada.DeUmaSo(maq),
                                  memoria ?? [], ms);
        }

        (string Tipo, int A, int B, int Esperado)[] tabela =
        [
            ("and-gate", 0, 0, 0), ("and-gate", 1, 0, 0), ("and-gate", 1, 1, 1),
            ("or-gate", 0, 0, 0), ("or-gate", 1, 0, 1), ("or-gate", 1, 1, 1),
            ("xor-gate", 0, 0, 0), ("xor-gate", 1, 0, 1), ("xor-gate", 1, 1, 0),
            ("nand-gate", 1, 1, 0), ("nand-gate", 0, 1, 1),
            ("nor-gate", 0, 0, 1), ("nor-gate", 1, 0, 0),
            ("xnor-gate", 1, 1, 1), ("xnor-gate", 1, 0, 0),
        ];

        int erradas = 0;
        foreach (var (tipo, a, b, esperado) in tabela)
        {
            var (c, vcc, gnd) = Trilhos();
            var porta = new PecaNoCircuito { Tipo = tipo, X = 300, Y = 60 };
            c.Pecas.Add(porta);

            Ligar(c, a == 1 ? vcc : gnd, a == 1 ? "VCC" : "GND", porta, "A");
            Ligar(c, b == 1 ? vcc : gnd, b == 1 ? "VCC" : "GND", porta, "B");

            int q = Rodar(c).NivelDoPino(porta.Id, "Q");
            if (q != esperado)
            {
                erradas++;
                saida.AppendLine($"        {tipo}({a},{b}) deu {q}, devia dar {esperado}");
            }
        }

        Conferir("logica: a tabela-verdade das sete portas bate",
            erradas == 0, $"{erradas} linhas erradas");

        {
            var (c, vcc, gnd) = Trilhos();
            var naoA = new PecaNoCircuito { Tipo = "not-gate", X = 300, Y = 60 };
            c.Pecas.Add(naoA);
            Ligar(c, vcc, "VCC", naoA, "A");

            int q = Rodar(c).NivelDoPino(naoA.Id, "Q");
            Conferir("logica: a NOT inverte", q == 0, $"{q}");
        }

        {
            var (c, vcc, gnd) = Trilhos();
            var mux = new PecaNoCircuito { Tipo = "mux-2", X = 300, Y = 60 };
            c.Pecas.Add(mux);

            Ligar(c, gnd, "GND", mux, "A");
            Ligar(c, vcc, "VCC", mux, "B");
            Ligar(c, vcc, "VCC", mux, "SEL");

            int comSel = Rodar(c).NivelDoPino(mux.Id, "Q");

            c.Fios.RemoveAll(f => f.ParaPino == "SEL");
            Ligar(c, gnd, "GND", mux, "SEL");
            int semSel = Rodar(c).NivelDoPino(mux.Id, "Q");

            saida.AppendLine($"        MUX: SEL=1 -> {comSel}, SEL=0 -> {semSel}");
            Conferir("logica: o MUX escolhe a entrada certa",
                comSel == 1 && semSel == 0, $"{comSel}/{semSel}");
        }

        {
            var (c, vcc, gnd) = Trilhos();
            var ff = new PecaNoCircuito { Tipo = "flip-flop-d", X = 300, Y = 60 };
            var clock = new PecaNoCircuito { Tipo = "clock-generator", X = 120, Y = 300, Giro = 1 };
            c.Pecas.AddRange([ff, clock]);

            Ligar(c, vcc, "VCC", ff, "D");
            Ligar(c, clock, "CLK", ff, "CLK");

            var memoria = new Dictionary<string, int>();

            Rodar(c, memoria, 0);
            c.Fios.RemoveAll(f => f.DePeca == ff.Id || f.ParaPeca == ff.Id);
            Ligar(c, gnd, "GND", ff, "D");
            Ligar(c, clock, "CLK", ff, "CLK");

            Rodar(c, memoria, 30);
            int antes = Rodar(c, memoria, 55).NivelDoPino(ff.Id, "Q");

            c.Fios.RemoveAll(f => f.ParaPino == "D");
            Ligar(c, vcc, "VCC", ff, "D");

            Rodar(c, memoria, 80);
            var comBorda = Rodar(c, memoria, 105);
            int depois = comBorda.NivelDoPino(ff.Id, "Q");
            int negado = comBorda.NivelDoPino(ff.Id, "NQ");

            saida.AppendLine($"        flip-flop D: com D em 0 ficou {antes}, "
                             + $"com D em 1 na borda seguinte ficou {depois}");

            Conferir("logica: o flip-flop D guarda o D na subida do clock",
                antes == 0 && depois == 1, $"{antes} -> {depois}");
            Conferir("logica: e o NQ e o contrario do Q", negado == 0, $"{negado}");
        }

        {
            var (c, vcc, gnd) = Trilhos();
            var clock = new PecaNoCircuito { Tipo = "clock-generator", X = 120, Y = 300, Giro = 1 };
            c.Pecas.Add(clock);

            var memoria = new Dictionary<string, int>();
            int baixo = 0, alto = 0;
            for (long ms = 0; ms < 200; ms += 5)
            {
                int nivel = Rodar(c, memoria, ms).NivelDoPino(clock.Id, "CLK");
                if (nivel == 1) alto++; else baixo++;
            }

            saida.AppendLine($"        clock a 20 Hz em 200 ms: {alto} altos, {baixo} baixos");
            Conferir("logica: o gerador de clock oscila", alto > 5 && baixo > 5,
                $"{alto}/{baixo}");
        }

        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var naoA = new PecaNoCircuito { Tipo = "not-gate", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, naoA]);

            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D13",
                                 ParaPeca = naoA.Id, ParaPino = "A" });
            c.Fios.Add(new Fio { DePeca = naoA.Id, DePino = "Q",
                                 ParaPeca = placa.Id, ParaPino = "D14" });

            {
                var ed = new EditorCircuito();
                ed.Carregar(c);

                var maqDireta = new Maquina { Perfil = PerfilPlaca.Esp32 };
                maqDireta.Reiniciar();
                maqDireta.Modo[13] = 1;

                var bancadaDireta = Simulador.App.Bancada.DeUmaSo(maqDireta);
                var redeDireta = new Netlist(c);

                ed.AplicarEntradas(bancadaDireta, redeDireta);
                int comZero = maqDireta.NivelExterno[14];

                maqDireta.Digital[13] = 1;
                maqDireta.Pwm[13] = 255;
                ed.AplicarEntradas(bancadaDireta, redeDireta);
                int comUm = maqDireta.NivelExterno[14];

                saida.AppendLine($"        NOT direto: D13=0 -> D14={comZero}, D13=1 -> D14={comUm}");
                Conferir("logica: a saida da porta chega no pino da placa",
                    comZero == 1 && comUm == 0, $"{comZero}/{comUm}");
            }

            var janela = new MainWindow();
            janela.TesteAbrir(c,
                "void setup() { pinMode(13, OUTPUT); pinMode(14, INPUT); }\n"
                + "void loop() {\n"
                + "  digitalWrite(13, HIGH);\n"
                + "  delay(150);\n"
                + "  Serial.println(digitalRead(14));\n"
                + "  digitalWrite(13, LOW);\n"
                + "  delay(150);\n"
                + "  Serial.println(digitalRead(14));\n"
                + "}");

            janela.TesteRodar();
            EsperarPor(() => LinhasDoSerial(janela.TesteMaquinaDa(placa.Id)).Count >= 6, 6000);
            var linhas = LinhasDoSerial(janela.TesteMaquinaDa(placa.Id));
            janela.TesteParar();

            bool inverteu = false;
            for (int k = 0; k + 1 < linhas.Count; k += 2)
                if (linhas[k] == "0" && linhas[k + 1] == "1") inverteu = true;

            saida.AppendLine($"        sketch lendo a porta NOT: {string.Join(", ", linhas.Take(6))}");

            Conferir("logica: o sketch le a saida da porta pelo pino",
                inverteu, string.Join(",", linhas.Take(8)));
        }
    }

    static void Analisador_Logico()
    {
        saida.AppendLine("analisador logico");

        var c = new Circuito();
        var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        var analisador = new PecaNoCircuito { Tipo = "logic-analyzer", X = 500, Y = 0 };
        c.Pecas.AddRange([placa, analisador]);

        c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D13",
                             ParaPeca = analisador.Id, ParaPino = "D0" });
        c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "GND.2",
                             ParaPeca = analisador.Id, ParaPino = "GND" });

        var janela = new MainWindow();
        janela.TesteAbrir(c,
            "void setup() { pinMode(13, OUTPUT); }\n"
            + "void loop() {\n"
            + "  digitalWrite(13, HIGH);\n"
            + "  delay(120);\n"
            + "  digitalWrite(13, LOW);\n"
            + "  delay(120);\n"
            + "}");

        janela.TesteRodar();

        var ed = janela.TesteEditorCircuito;
        EsperarPor(() => ed.QuadrosDoAnalisadorParaTeste(analisador.Id) > 20
                         && ed.AnalisadorViuBordaParaTeste(analisador.Id, 0), 4000);

        int quadros = ed.QuadrosDoAnalisadorParaTeste(analisador.Id);
        bool borda = ed.AnalisadorViuBordaParaTeste(analisador.Id, 0);
        bool paradoNoCanal7 = !ed.AnalisadorViuBordaParaTeste(analisador.Id, 7);

        // Com um segundo na tela, o pisca de 240 ms mostra varias bordas.
        analisador.JanelaUs = 1_000_000;
        EsperarPor(() => ed.BordasNoAnalisadorParaTeste(analisador.Id, 0) >= 6, 3000);
        int bordasEmUmSegundo = ed.BordasNoAnalisadorParaTeste(analisador.Id, 0);

        janela.TesteParar();

        saida.AppendLine($"        canal D0: {quadros} quadros, borda={borda}; "
                         + $"com 1 s na tela: {bordasEmUmSegundo} bordas");

        Conferir("analisador: desenha o canal quadro a quadro", quadros > 20, $"{quadros} quadros");
        Conferir("analisador: ve o pino subir (o disparo segura a ultima subida)", borda, "nenhuma borda");
        Conferir("analisador: canal sem fio fica parado", paradoNoCanal7, "mexeu sozinho");
        Conferir("analisador: a escala da peca vale -- 1 s na tela mostra o pisca inteiro",
            bordasEmUmSegundo >= 6, $"{bordasEmUmSegundo}");

        // ---- a janela grande: PWM medido e a serial no fio ----
        {
            var cj = new Circuito();
            var uno = new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 0, Y = 0 };
            var la = new PecaNoCircuito { Id = "la", Tipo = "logic-analyzer", X = 500, Y = 0 };
            cj.Pecas.AddRange([uno, la]);
            cj.Fios.Add(new Fio { DePeca = "uno", DePino = "5", ParaPeca = "la", ParaPino = "D0" });
            cj.Fios.Add(new Fio { DePeca = "uno", DePino = "1", ParaPeca = "la", ParaPino = "D7" });
            cj.Fios.Add(new Fio { DePeca = "uno", DePino = "GND.1", ParaPeca = "la", ParaPino = "GND" });

            var jj = new MainWindow();
            jj.TesteAbrir(cj, string.Join(Environment.NewLine,
                "void setup() { Serial.begin(9600); analogWrite(5, 64); }",
                "void loop() { Serial.print(\"Oi\"); delay(20); }"));
            jj.TesteRodar();

            var grande = jj.TesteAbrirAnalisador("la");
            grande.JanelaParaTeste(5000);
            System.Threading.Thread.Sleep(300);
            EsperarPor(() => { grande.DesenharParaTeste(); return grande.BordasParaTeste(0) > 6; }, 3000);
            grande.PausarParaTeste();
            grande.DesenharParaTeste();

            string medida = grande.MedidaParaTeste(0.5, 0);

            // A serial manda "Oi" a cada 20 ms e leva 2 ms: numa tela de
            // 5 ms parada num instante qualquer ela quase nunca esta. Com a
            // captura parada, aumentar a escala anda para tras no tempo.
            grande.JanelaParaTeste(50_000);
            grande.DesenharParaTeste();
            int bordasSerial = grande.BordasParaTeste(7);
            string vcd = grande.VcdParaTeste();
            saida.AppendLine($"        janela: {grande.CanaisLigadosParaTeste} canais ligados; {medida}; "
                             + $"serial com {bordasSerial} bordas; vcd de {vcd.Length} caracteres");

            Conferir("analisador: a janela acha os dois canais ligados", grande.CanaisLigadosParaTeste == 2,
                grande.CanaisLigadosParaTeste.ToString());
            Conferir("analisador: a medida do PWM do pino 5 da o periodo, a frequencia e o ciclo do Uno",
                medida.Contains("1,024 ms") && medida.Contains("976,6 Hz") && medida.Contains("25,1% alto"),
                medida);
            Conferir("analisador: a serial aparece no fio do TX", bordasSerial > 4, bordasSerial.ToString());
            Conferir("analisador: o .vcd sai no formato do PulseView",
                vcd.Contains("$timescale 1 ns $end") && vcd.Contains("D0_GPIO5") && vcd.Contains("D7_GPIO1")
                && vcd.Contains("$enddefinitions $end") && vcd.Split('#').Length > 10,
                vcd.Length > 300 ? vcd[..300] : vcd);

            grande.Close();
            jj.TesteParar();
        }

        // ---- a demo: cada canal com o seu sinal ----
        {
            var jd = new MainWindow();
            jd.TesteAbrirDemo("uno-analisador");
            jd.TesteRodar();

            var jan = jd.TesteAbrirAnalisador("la");
            jan.JanelaParaTeste(1_000_000);

            // A tela de 1 s tem que caber inteira DEPOIS do comeco: parada
            // antes, metade dela e tempo em que o sketch nem existia -- e o
            // servo aparecia "baixo por meio segundo" sem ter parado.
            var maqDaDemo = jd.TesteMaquinaDa("placa");
            EsperarPor(() => maqDaDemo.Ms >= 1600, 6000);
            EsperarPor(() =>
            {
                jan.DesenharParaTeste();
                return Enumerable.Range(0, 8).All(k => jan.BordasParaTeste(k) > 0);
            }, 5000);
            jan.PausarParaTeste();
            jan.DesenharParaTeste();

            var bordas = Enumerable.Range(0, 8).Select(k => jan.BordasParaTeste(k)).ToList();
            string servo = jan.MedidaParaTeste(0.5, 3);
            saida.AppendLine($"        demo: bordas por canal em 1 s: {string.Join(", ", bordas)}; servo: {servo}");

            string[] nomes = ["PWM 25%", "PWM 75%", "tone", "servo", "dado", "relogio", "trava", "serial"];
            for (int k = 0; k < 8; k++)
                Conferir($"analisador: na demo, D{k} ({nomes[k]}) tem sinal", bordas[k] > 0, bordas[k].ToString());

            Conferir("analisador: na demo, o servo pulsa a 50 Hz",
                servo.Contains("período 20 ms") && servo.Contains("50 Hz"), servo);

            jan.Close();
            jd.TesteParar();
        }
    }

    static void Cartao_ComExplorador()
    {
        saida.AppendLine("cartao microSD de verdade");

        static string Lista(Dictionary<string, string> arquivos)
            => arquivos is null ? "nenhum" : string.Join(", ", arquivos.Keys.OrderBy(x => x));

        static (Circuito c, PecaNoCircuito placa, PecaNoCircuito cartao) Montar(bool ligado)
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var cartao = new PecaNoCircuito { Tipo = "microsd-card", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, cartao]);

            if (ligado)
            {
                foreach (var (pino, alvo) in new[] { ("CS", "D5"), ("SCK", "D18"),
                                                     ("DI", "D23"), ("DO", "D19"),
                                                     ("VCC", "3V3"), ("GND", "GND.2") })
                    c.Fios.Add(new Fio { DePeca = cartao.Id, DePino = pino,
                                         ParaPeca = placa.Id, ParaPino = alvo });
            }

            return (c, placa, cartao);
        }

        const string sketch =
            "void setup() {\n"
            + "  Serial.begin(115200);\n"
            + "  if (!SD.begin(5)) { Serial.println(\"sem cartao\"); return; }\n"
            + "  Serial.println(\"cartao ok\");\n"
            + "  File f = SD.open(\"/leituras.csv\", FILE_WRITE);\n"
            + "  f.println(\"12;34\");\n"
            + "  f.close();\n"
            + "  File g = SD.open(\"/config.txt\");\n"
            + "  Serial.println(g.readStringUntil('\\n'));\n"
            + "  g.close();\n"
            + "}\n"
            + "void loop() {}";

        {
            var (c, placa, _) = Montar(ligado: false);

            var janela = new MainWindow();
            janela.TesteAbrir(c, sketch);
            janela.TesteRodar();
            EsperarPor(() => LinhasDoSerial(janela.TesteMaquinaDa(placa.Id)).Count > 0, 4000);
            var linhas = LinhasDoSerial(janela.TesteMaquinaDa(placa.Id));
            janela.TesteParar();

            saida.AppendLine($"        sem a peca no circuito: {string.Join(" | ", linhas)}");
            Conferir("cartao: sem a peca ligada, SD.begin() devolve false",
                linhas.Contains("sem cartao"), string.Join("|", linhas));
        }

        {
            var (c, placa, cartao) = Montar(ligado: true);
            cartao.Arquivos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["config.txt"] = "modo=teste",
            };

            var janela = new MainWindow();
            janela.TesteAbrir(c, sketch);
            janela.TesteRodar();
            EsperarPor(() => LinhasDoSerial(janela.TesteMaquinaDa(placa.Id)).Count >= 3, 5000);
            var linhas = LinhasDoSerial(janela.TesteMaquinaDa(placa.Id));
            janela.TesteParar();

            saida.AppendLine($"        com a peca ligada: {string.Join(" | ", linhas)}");
            saida.AppendLine($"        no cartao depois de parar: {Lista(cartao.Arquivos)}");

            Conferir("cartao: com a peca ligada, SD.begin() devolve true",
                linhas.Contains("cartao ok"), string.Join("|", linhas));
            Conferir("cartao: o sketch le o arquivo que o aluno pos no cartao",
                linhas.Any(l => l.Contains("modo=teste")), string.Join("|", linhas));
            Conferir("cartao: o que o sketch gravou volta para a peca ao parar",
                cartao.Arquivos is not null
                && cartao.Arquivos.TryGetValue("leituras.csv", out var gravado)
                && gravado.Contains("12;34"),
                Lista(cartao.Arquivos));
            Conferir("cartao: o arquivo que ja estava continua la",
                cartao.Arquivos is not null && cartao.Arquivos.ContainsKey("config.txt"),
                Lista(cartao.Arquivos));
        }

        {
            var c = new Circuito();
            var cartao = new PecaNoCircuito
            {
                Tipo = "microsd-card",
                X = 0,
                Y = 0,
                Arquivos = new Dictionary<string, string> { ["dados.txt"] = "uma linha" },
            };
            c.Pecas.Add(cartao);

            string temporario = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"cartao-{Guid.NewGuid():N}.sktch");
            Sktch.Salvar(temporario, c, "void setup() {}");
            var (volta, _) = Sktch.Abrir(temporario);
            System.IO.File.Delete(temporario);

            var copia = cartao.Copia();

            saida.AppendLine($"        depois de salvar e abrir: {Lista(volta.Pecas[0].Arquivos)}");

            Conferir("cartao: os arquivos vao junto no .sktch",
                volta.Pecas[0].Arquivos is not null
                && volta.Pecas[0].Arquivos.GetValueOrDefault("dados.txt") == "uma linha",
                Lista(volta.Pecas[0].Arquivos));
            Conferir("cartao: copiar a peca copia os arquivos",
                copia.Arquivos is not null
                && copia.Arquivos.GetValueOrDefault("dados.txt") == "uma linha",
                Lista(copia.Arquivos));
            Conferir("cartao: a copia nao divide a lista com o original",
                !ReferenceEquals(copia.Arquivos, cartao.Arquivos), "mesma lista");
        }

        {
            // ---- o arquivo em bytes que o sketch grava fica no cartao ----
            // (o codigo C grava pelo AbrirBytes, o mesmo caminho do fopen)
            var (c, placa, cartao) = Montar(ligado: true);
            cartao.Arquivos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["apagar.txt"] = "sai" };
            byte[] dados = [0x00, 0xFF, 0x10, 0x80, 0x0A, 0x0D];

            var janela = new MainWindow();
            janela.TesteAbrir(c, "void setup() { Serial.begin(115200); Serial.println(\"rodando\"); }\nvoid loop() { delay(100); }");
            janela.TesteRodar();
            var maquina = janela.TesteMaquinaDa(placa.Id);
            EsperarPor(() => LinhasDoSerial(maquina).Contains("rodando"), 4000);
            var fluxo = maquina.Cartao.AbrirBytes("save.bin", ler: false, escrever: true, criar: true, truncar: true,
                                                  anexar: false, exclusivo: false, out _);
            fluxo?.Escrever(dados);
            maquina.Cartao.Remover("apagar.txt");
            janela.TesteParar();

            string guardado = cartao.ArquivosGravados?.GetValueOrDefault("save.bin");
            saida.AppendLine($"        depois de parar: gravados {(cartao.ArquivosGravados is null ? "nenhum" : string.Join(", ", cartao.ArquivosGravados.Keys))}, "
                             + $"texto {Lista(cartao.Arquivos)}");
            Conferir("cartao: o arquivo em bytes que o sketch gravou fica na peca ao parar (como num cartao de verdade)",
                guardado is not null && Convert.FromBase64String(guardado).SequenceEqual(dados), guardado ?? "nao ficou");
            Conferir("cartao: o arquivo que o sketch apagou continua apagado depois de parar",
                cartao.Arquivos is null || !cartao.Arquivos.ContainsKey("apagar.txt"), Lista(cartao.Arquivos));

            janela.TesteRodar();
            maquina = janela.TesteMaquinaDa(placa.Id);
            EsperarPor(() => LinhasDoSerial(maquina).Contains("rodando"), 4000);
            byte[] deVolta = maquina.Cartao.Bytes("save.bin");
            janela.TesteParar();
            Conferir("cartao: na rodada seguinte o arquivo em bytes esta no cartao, byte a byte",
                deVolta is not null && deVolta.SequenceEqual(dados), deVolta is null ? "sumiu" : Convert.ToHexString(deVolta));

            string temporario = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cartao-{Guid.NewGuid():N}.sktch");
            Sktch.Salvar(temporario, c, "void setup() {}");
            var (volta, _) = Sktch.Abrir(temporario);
            System.IO.File.Delete(temporario);
            var pecaDeVolta = volta.Pecas.First(p => p.Tipo == "microsd-card");
            Conferir("cartao: o arquivo em bytes vai junto no .sktch",
                pecaDeVolta.ArquivosGravados?.GetValueOrDefault("save.bin") == guardado, "nao foi");
        }

        // ---- a ligacao que o SD.begin() confere, como a placa ----
        (string Serial, string Avisos) RodarComLigacao((string, string)[] ligacao, string sketchDaLigacao)
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var cartao = new PecaNoCircuito { Tipo = "microsd-card", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, cartao]);
            foreach (var (pino, alvo) in ligacao)
                c.Fios.Add(new Fio { DePeca = cartao.Id, DePino = pino, ParaPeca = placa.Id, ParaPino = alvo });

            var janela = new MainWindow();
            janela.TesteAbrir(c, sketchDaLigacao);
            janela.TesteRodar();
            var maquina = janela.TesteMaquinaDa(placa.Id);
            EsperarPor(() => LinhasDoSerial(maquina).Count > 0, 4000);
            string serial = string.Join("|", LinhasDoSerial(maquina));
            string avisos;
            lock (maquina.Trava) avisos = string.Join(" / ", maquina.Avisos);
            janela.TesteParar();
            return (serial, avisos);
        }

        const string beginNoCs4 = "void setup() { Serial.begin(115200); Serial.println(SD.begin(4) ? \"ok\" : \"falhou\"); }\nvoid loop() {}";
        var padrao = new[] { ("CS", "D5"), ("SCK", "D18"), ("DI", "D23"), ("DO", "D19"), ("VCC", "3V3"), ("GND", "GND.2") };
        var (csErrado, avisosCs) = RodarComLigacao(padrao, beginNoCs4);
        saida.AppendLine($"        SD.begin(4) com o CS no GPIO 5: {csErrado} / {avisosCs}");
        Conferir("cartao: SD.begin(4) com o CS ligado no GPIO 5 devolve false, e o aviso diz onde esta o CS",
            csErrado == "falhou" && avisosCs.Contains("o CS do cartao esta no GPIO 5"), $"{csErrado} / {avisosCs}");

        var sckNoGpio14 = new[] { ("CS", "D5"), ("SCK", "D14"), ("DI", "D23"), ("DO", "D19"), ("VCC", "3V3"), ("GND", "GND.2") };
        const string beginNoCs5 = "void setup() { Serial.begin(115200); Serial.println(SD.begin(5) ? \"ok\" : \"falhou\"); }\nvoid loop() {}";
        var (sckErrado, avisosSck) = RodarComLigacao(sckNoGpio14, beginNoCs5);
        Conferir("cartao: com o SCK fora do pino do SPI (GPIO 14, e nao 18), o SD.begin() devolve false e o aviso ensina o SPI.begin()",
            sckErrado == "falhou" && avisosSck.Contains("SCK do cartao esta no GPIO 14") && avisosSck.Contains("SPI.begin(sck, miso, mosi, cs)"),
            $"{sckErrado} / {avisosSck}");

        const string comSpiProprio = "#include <SPI.h>\nvoid setup() { Serial.begin(115200); SPI.begin(14, 19, 23, 5); Serial.println(SD.begin(5) ? \"ok\" : \"falhou\"); }\nvoid loop() {}";
        var (sckTrocado, avisosTrocado) = RodarComLigacao(sckNoGpio14, comSpiProprio);
        Conferir("cartao: o mesmo cartao, com SPI.begin(14, 19, 23, 5) antes, responde",
            sckTrocado == "ok", $"{sckTrocado} / {avisosTrocado}");
    }

    static void Rede_PontoDeAcesso()
    {
        saida.AppendLine("ponto de acesso wifi");

        const string sketch = """
            #include <WiFi.h>
            void setup() {
              Serial.begin(115200);
              WiFi.begin("AulaDeRedes", "12345678");
              for (int i = 0; i < 20 && WiFi.status() != WL_CONNECTED; i++) delay(200);
              Serial.println(WiFi.status());
              if (WiFi.status() == WL_CONNECTED) Serial.println(WiFi.localIP());
            }
            void loop() {}
            """;

        {
            var rede = new Rede { UsarInternet = false };
            rede.DefinirPontosDeAcesso([new PontoDeAcesso("AulaDeRedes", "12345678")]);

            var (maq, interp) = MontarPlaca(rede, "ESP32", sketch);
            interp.Setup();
            var linhas = LinhasDoSerial(maq);

            saida.AppendLine($"        senha certa: {string.Join("|", linhas)}");
            Conferir("ponto de acesso: a placa entra na rede da peca",
                linhas.Contains("3") && linhas.Contains("10.0.0.2"),
                string.Join("|", linhas));
        }

        {
            var rede = new Rede { UsarInternet = false };
            rede.DefinirPontosDeAcesso([new PontoDeAcesso("AulaDeRedes", "outra-senha")]);

            var (maq, interp) = MontarPlaca(rede, "ESP32", sketch);
            interp.Setup();
            var linhas = LinhasDoSerial(maq);

            saida.AppendLine($"        senha errada: {string.Join("|", linhas)} / "
                             + string.Join(" · ", maq.Avisos));
            Conferir("ponto de acesso: senha errada nao conecta",
                linhas.Contains("4"), string.Join("|", linhas));
            Conferir("ponto de acesso: o aviso diz que a senha nao confere",
                maq.Avisos.Any(a => a.Contains("senha nao confere")),
                string.Join(" · ", maq.Avisos));
        }

        {
            var rede = new Rede { UsarInternet = false };
            rede.DefinirPontosDeAcesso([new PontoDeAcesso("OutraRede", "")]);

            var (maq, interp) = MontarPlaca(rede, "ESP32", sketch);
            interp.Setup();
            var linhas = LinhasDoSerial(maq);

            saida.AppendLine($"        rede que nao existe: {string.Join("|", linhas)}");
            Conferir("ponto de acesso: SSID que nao existe no circuito nao conecta",
                linhas.Contains("1"), string.Join("|", linhas));
            Conferir("ponto de acesso: o aviso diz qual nome faltou",
                maq.Avisos.Any(a => a.Contains("AulaDeRedes")),
                string.Join(" · ", maq.Avisos));
        }

        {
            var rede = new Rede { UsarInternet = false };
            var (maq, interp) = MontarPlaca(rede, "ESP32", sketch);
            interp.Setup();
            var linhas = LinhasDoSerial(maq);

            saida.AppendLine($"        sem peca nenhuma: {string.Join("|", linhas)}");
            Conferir("ponto de acesso: sem a peca, qualquer SSID continua conectando",
                linhas.Contains("3"), string.Join("|", linhas));
        }

        {
            var rede = new Rede { UsarInternet = false };
            rede.DefinirPontosDeAcesso([new PontoDeAcesso("AulaDeRedes", "12345678"),
                                        new PontoDeAcesso("Visitantes", "")]);

            var (maq, interp) = MontarPlaca(rede, "ESP32", """
                #include <WiFi.h>
                void setup() {
                  Serial.begin(115200);
                  int n = WiFi.scanNetworks();
                  for (int i = 0; i < n; i++) {
                    Serial.println(WiFi.SSID(i) + ":" + WiFi.encryptionType(i));
                  }
                }
                void loop() {}
                """);
            interp.Setup();
            var linhas = LinhasDoSerial(maq);

            saida.AppendLine($"        varredura: {string.Join("|", linhas)}");
            Conferir("ponto de acesso: a varredura acha a rede da peca",
                linhas.Contains("AulaDeRedes:3"), string.Join("|", linhas));
            Conferir("ponto de acesso: rede sem senha aparece como aberta",
                linhas.Contains("Visitantes:0"), string.Join("|", linhas));
        }
    }

    static void Rede_Nrf24()
    {
        saida.AppendLine("modulo de radio NRF24L01");

        const string escutar = """
            #include <SPI.h>
            #include <RF24.h>
            RF24 radio(4, 5);
            const byte endereco[6] = "00001";
            char recebido[32] = "";
            void setup() {
              Serial.begin(115200);
              if (!radio.begin()) { Serial.println("sem modulo"); return; }
              radio.openReadingPipe(0, endereco);
              radio.setPALevel(RF24_PA_MIN);
              radio.startListening();
            }
            void loop() {
              if (radio.available()) {
                radio.read(&recebido, sizeof(recebido));
                Serial.println(recebido);
              }
              delay(10);
            }
            """;

        const string falar = """
            #include <SPI.h>
            #include <RF24.h>
            RF24 radio(4, 5);
            const byte endereco[6] = "00001";
            void setup() {
              Serial.begin(115200);
              if (!radio.begin()) { Serial.println("sem modulo"); return; }
              radio.openWritingPipe(endereco);
              radio.setPALevel(RF24_PA_MIN);
              radio.stopListening();
            }
            void loop() {
              const char texto[] = "ola do transmissor";
              Serial.println(radio.write(&texto, sizeof(texto)));
              delay(10);
            }
            """;

        {
            var rede = new Rede { UsarInternet = false };

            var (ouvinteMaq, ouvinteInterp) = MontarPlaca(rede, "ESP32-rx", escutar);
            var (falanteMaq, falanteInterp) = MontarPlaca(rede, "ESP32-tx", falar);
            ouvinteMaq.RadioPresente = true;
            falanteMaq.RadioPresente = true;

            ouvinteInterp.Setup();
            falanteInterp.Setup();

            falanteInterp.Loop();
            ouvinteInterp.Loop();

            var doOuvinte = LinhasDoSerial(ouvinteMaq);
            var doFalante = LinhasDoSerial(falanteMaq);

            saida.AppendLine($"        transmissor: {string.Join("|", doFalante)}");
            saida.AppendLine($"        receptor: {string.Join("|", doOuvinte)}");

            Conferir("nrf24: o transmissor sabe que alguem ouviu",
                doFalante.Contains("1"), string.Join("|", doFalante));
            Conferir("nrf24: a mensagem chega inteira na outra placa",
                doOuvinte.Contains("ola do transmissor"), string.Join("|", doOuvinte));
            Conferir("nrf24: o aviso explica o alcance simulado",
                falanteMaq.Avisos.Any(a => a.Contains("bancada inteira")),
                string.Join(" · ", falanteMaq.Avisos));
        }

        {
            var rede = new Rede { UsarInternet = false };
            var (maq, interp) = MontarPlaca(rede, "ESP32-sem-modulo", escutar);
            interp.Setup();

            var linhas = LinhasDoSerial(maq);
            saida.AppendLine($"        sem o modulo: {string.Join("|", linhas)}");

            Conferir("nrf24: sem a peca ligada, radio.begin() devolve false",
                linhas.Contains("sem modulo"), string.Join("|", linhas));
            Conferir("nrf24: o aviso diz quais pinos ligar",
                maq.Avisos.Any(a => a.Contains("CE")), string.Join(" · ", maq.Avisos));
        }

        {
            var rede = new Rede { UsarInternet = false };

            var (ouvinteMaq, ouvinteInterp) = MontarPlaca(rede, "ESP32-rx", escutar.Replace(
                "radio.openReadingPipe(0, endereco);", "radio.setChannel(90);\n  radio.openReadingPipe(0, endereco);"));
            var (falanteMaq, falanteInterp) = MontarPlaca(rede, "ESP32-tx", falar);
            ouvinteMaq.RadioPresente = true;
            falanteMaq.RadioPresente = true;

            ouvinteInterp.Setup();
            falanteInterp.Setup();
            falanteInterp.Loop();
            ouvinteInterp.Loop();

            var doOuvinte = LinhasDoSerial(ouvinteMaq);
            saida.AppendLine($"        em outro canal: {string.Join("|", doOuvinte)}");
            Conferir("nrf24: quem esta em outro canal nao recebe",
                doOuvinte.Count == 0, string.Join("|", doOuvinte));
        }
    }

    static void Rede_EspNow()
    {
        saida.AppendLine("ESP-Now");

        const string receptor = """
            #include <esp_now.h>
            #include <WiFi.h>

            typedef struct mensagem {
              char texto[32];
              int leitura;
            } mensagem;

            mensagem chegou;

            void aoReceber(const uint8_t *mac, const uint8_t *dados, int tamanho) {
              memcpy(&chegou, dados, sizeof(chegou));
              Serial.println(chegou.texto);
              Serial.println(chegou.leitura);
            }

            void setup() {
              Serial.begin(115200);
              WiFi.mode(WIFI_STA);
              if (esp_now_init() != ESP_OK) { Serial.println("falhou"); return; }
              esp_now_register_recv_cb(aoReceber);
            }
            void loop() {}
            """;

        const string emissor = """
            #include <esp_now.h>
            #include <WiFi.h>

            typedef struct mensagem {
              char texto[32];
              int leitura;
            } mensagem;

            mensagem aEnviar;
            uint8_t paraTodos[] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};
            int enviadas = 0;

            void aoEnviar(const uint8_t *mac, int estado) {
              enviadas = enviadas + 1;
              Serial.println(estado);
            }

            void setup() {
              Serial.begin(115200);
              WiFi.mode(WIFI_STA);
              esp_now_init();
              esp_now_register_send_cb(aoEnviar);
              esp_now_add_peer(paraTodos);
            }

            void loop() {
              strcpy(aEnviar.texto, "sala cheia");
              aEnviar.leitura = 42;
              esp_now_send(paraTodos, (uint8_t *) &aEnviar, sizeof(aEnviar));
              delay(10);
            }
            """;

        {
            var rede = new Rede { UsarInternet = false };

            var (rxMaq, rxInterp) = MontarPlaca(rede, "ESP32-rx", receptor);
            var (txMaq, txInterp) = MontarPlaca(rede, "ESP32-tx", emissor);

            rxInterp.Setup();
            txInterp.Setup();

            txInterp.Loop();
            rxInterp.Loop();

            var doRx = LinhasDoSerial(rxMaq);
            var doTx = LinhasDoSerial(txMaq);

            saida.AppendLine($"        emissor: {string.Join("|", doTx)}");
            saida.AppendLine($"        receptor: {string.Join("|", doRx)}");

            Conferir("esp-now: a struct chega com o texto certo",
                doRx.Contains("sala cheia"), string.Join("|", doRx));
            Conferir("esp-now: a struct chega com o numero certo",
                doRx.Contains("42"), string.Join("|", doRx));
            Conferir("esp-now: o callback de envio recebe o estado 0",
                doTx.Contains("0"), string.Join("|", doTx));
        }

        {
            var rede = new Rede { UsarInternet = false };
            var (txMaq, txInterp) = MontarPlaca(rede, "ESP32-sozinha", emissor);

            txInterp.Setup();
            txInterp.Loop();

            var doTx = LinhasDoSerial(txMaq);
            saida.AppendLine($"        sozinha na bancada: {string.Join("|", doTx)}");

            // Difusao nao tem ACK: na placa o callback diz SUCESSO mesmo
            // sem ninguem ouvindo. Este teste dizia o contrario -- e um
            // sketch que confiasse nele erraria na bancada.
            Conferir("esp-now: difusao sem ninguem ouvindo: o callback diz sucesso, como na placa",
                doTx.Contains("0") && !doTx.Contains("1"), string.Join("|", doTx));
        }

        {
            var rede = new Rede { UsarInternet = false };
            var (maq, interp) = MontarPlaca(rede, "UNO", """
                void setup() { esp_now_init(); }
                void loop() {}
                """, PerfilPlaca.Uno);

            string erro = null;
            try { interp.Setup(); }
            catch (ErroDeExecucao e) { erro = e.Message; }

            saida.AppendLine($"        numa placa sem wifi: {erro}");
            Conferir("esp-now: numa placa sem WiFi o erro explica por que",
                erro is not null && erro.Contains("nao tem WiFi"), erro ?? "nao reclamou");
        }
    }

    static void Motor_Estruturas()
    {
        saida.AppendLine("struct no sketch");

        var rede = new Rede { UsarInternet = false };
        var (maq, interp) = MontarPlaca(rede, "ESP32", """
            struct Leitura {
              char nome[16];
              int valor;
              float media;
            };

            typedef struct {
              int a;
              int b;
            } Par;

            Leitura atual;
            Par p;

            void setup() {
              Serial.begin(115200);
              strcpy(atual.nome, "sensor");
              atual.valor = 7;
              atual.media = 2.5;
              p.a = 1;
              p.b = 2;

              Serial.println(atual.nome);
              Serial.println(atual.valor);
              Serial.println(atual.media);
              Serial.println(p.a + p.b);
              Serial.println(sizeof(atual));
            }
            void loop() {}
            """);

        string erro = null;
        try { interp.Setup(); }
        catch (ErroDeExecucao e) { erro = e.Message; }
        catch (ErroDeSintaxe e) { erro = e.Message; }

        var linhas = LinhasDoSerial(maq);
        saida.AppendLine($"        {erro ?? string.Join(" | ", linhas)}");

        Conferir("struct: o sketch com struct roda", erro is null, erro);
        Conferir("struct: campo de texto guarda o que o strcpy escreveu",
            linhas.Contains("sensor"), string.Join("|", linhas));
        Conferir("struct: campo numerico guarda o numero",
            linhas.Contains("7"), string.Join("|", linhas));
        Conferir("struct: typedef sem nome tambem vale",
            linhas.Contains("3"), string.Join("|", linhas));
        Conferir("struct: sizeof da struct devolve algum tamanho",
            linhas.Count > 0 && int.TryParse(linhas[^1], out int tamanho) && tamanho > 0,
            string.Join("|", linhas));
    }

    static void Arquivos_DoProjeto()
    {
        saida.AppendLine("gerenciador de arquivos");

        var c = new Circuito();
        var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        c.Pecas.Add(placa);

        var janela = new MainWindow();
        janela.TesteAbrir(c, "void setup() {}\nvoid loop() {}");

        var abas = janela.TesteAbasDeArquivo;
        saida.AppendLine($"        abas no comeco: {string.Join(" | ", abas)}");

        Conferir("arquivos: comeca com o sketch e o diagrama",
            abas.Count == 2 && abas[0].EndsWith(".ino") && abas[1] == "diagram.json",
            string.Join(" | ", abas));

        janela.TesteAbrirArquivo("diagram.json");
        string json = janela.TesteCodigo;
        saida.AppendLine($"        diagrama tem {json.Length} caracteres, "
                         + $"so leitura = {janela.TesteCodigoSoLeitura}");

        Conferir("arquivos: a aba do diagrama mostra o circuito em json",
            json.Contains("esp32-devkit-v1"), json.Length.ToString());
        // Era so leitura ate a Alpha 17. O professor pediu para poder
        // editar, e e por aqui que se cola um circuito inteiro de outro
        // projeto ou se acerta uma coordenada na unha.
        Conferir("arquivos: o diagrama da para editar",
            !janela.TesteCodigoSoLeitura, "continua so leitura");

        janela.TesteCriarArquivo("anotacoes.md", "# o que falta");
        abas = janela.TesteAbasDeArquivo;
        saida.AppendLine($"        depois de criar: {string.Join(" | ", abas)}");

        Conferir("arquivos: o arquivo novo vira uma aba",
            abas.Contains("anotacoes.md"), string.Join(" | ", abas));
        Conferir("arquivos: o arquivo novo abre no editor",
            janela.TesteCodigo == "# o que falta" && !janela.TesteCodigoSoLeitura,
            janela.TesteCodigo);

        janela.TesteDigitar("# lista nova\n- ligar o rele");
        janela.TesteAbrirArquivo(null);

        Conferir("arquivos: voltar para o sketch mostra o programa da placa",
            janela.TesteCodigo.Contains("void setup()"), janela.TesteCodigo);

        janela.TesteAbrirArquivo("anotacoes.md");
        Conferir("arquivos: o que foi digitado no arquivo ficou guardado",
            janela.TesteCodigo.Contains("ligar o rele"), janela.TesteCodigo);

        string temporario = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"projeto-{Guid.NewGuid():N}.sktch");
        Sktch.Salvar(temporario, janela.TesteEditorCircuito.Atual, janela.TesteCodigo);
        var (volta, _) = Sktch.Abrir(temporario);
        System.IO.File.Delete(temporario);

        saida.AppendLine("        depois de salvar e abrir: "
                         + (volta.Arquivos is null
                            ? "nenhum" : string.Join(", ", volta.Arquivos.Keys)));

        Conferir("arquivos: os arquivos do projeto vao junto no .sktch",
            volta.Arquivos is not null
            && volta.Arquivos.GetValueOrDefault("anotacoes.md", "").Contains("ligar o rele"),
            volta.Arquivos is null ? "nenhum" : string.Join(", ", volta.Arquivos.Keys));

        janela.TesteApagarArquivoAberto();
        abas = janela.TesteAbasDeArquivo;
        saida.AppendLine($"        depois de apagar: {string.Join(" | ", abas)}");

        Conferir("arquivos: apagar tira a aba e volta para o sketch",
            abas.Count == 2 && janela.TesteArquivoAberto is null,
            string.Join(" | ", abas));

        var ed = janela.TesteEditorCircuito;
        var outraPeca = ed.Adicionar("esp32-devkit-v1", new System.Windows.Point(600, 200));
        ed.DefinirNome(outraPeca, "painel");
        ed.SelecionarPorIndice(ed.Atual.Pecas.FindIndex(x => x.Id == outraPeca.Id));

        abas = janela.TesteAbasDeArquivo;
        saida.AppendLine($"        com a segunda placa: {string.Join(" | ", abas)}");
        Conferir("arquivos: a aba do sketch usa o nome da placa escolhida",
            abas.Count > 0 && abas[0] == "painel.ino", string.Join(" | ", abas));
    }

    /// <summary>
    /// A tinta de cada peca cabe na caixa dela, e as pecas que estavam
    /// tortas continuam no lugar.
    ///
    /// Isto existe porque o desenho e os pinos vem de fontes diferentes:
    /// o SVG e o pinInfo dos wokwi-elements. Quando os dois discordam,
    /// a perna do LED nao encosta no pino e a placa fica torta por
    /// baixo dos furos -- e olhar a tela e o unico jeito de perceber.
    /// Aqui a conferencia e por numero.
    /// </summary>
    static void Pecas_TintaNaCaixa()
    {
        saida.AppendLine("a tinta cabe na caixa da peca");

        string[] olhadas = ["led", "rgb-led", "arduino-mega", "arduino-uno",
                            "stepper-motor", "analog-joystick", "slide-switch",
                            "pushbutton", "pushbutton-6mm", "dip-switch-8",
                            "nrf24l01", "modulo-i2c", "membrane-keypad",
                            "rotary-dialer", "ir-remote", "esp32-devkit-v1",
                            "led-ring", "biaxial-stepper"];

        var vazando = new List<string>();

        foreach (string id in olhadas)
        {
            var def = Catalogo.Obter(id);
            if (def is null) continue;

            var tinta = Pecas.TintaDeParaTeste(id);
            if (tinta.IsEmpty) continue;

            // Meio pixel de folga: o traco da borda desenha metade para
            // fora e nao conta como vazamento. A sobra declarada no SVG (o
            // USB-B do Uno sai da placa) pode: ela e desenhada de proposito
            // fora da caixa, igual dos dois lados.
            var sobra = Pecas.SobraDe(id);
            bool cabe = tinta.X >= -1.5 && tinta.Y >= -1.5
                        && tinta.Right <= def.Largura + 2 * sobra.X + 1.5
                        && tinta.Bottom <= def.Altura + 2 * sobra.Y + 1.5;

            saida.AppendLine($"        {id,-18} tinta ({tinta.X:0.0},{tinta.Y:0.0}) "
                             + $"{tinta.Width:0.0}x{tinta.Height:0.0} "
                             + $"caixa {def.Largura}x{def.Altura} "
                             + (def.Pinos.Count == 0 ? "sem pinos"
                                : $"pinos y {def.Pinos.Min(q => q.Y):0.0}"
                                  + $"..{def.Pinos.Max(q => q.Y):0.0}"));

            if (!cabe)
                vazando.Add($"{id} ({tinta.X:0.0},{tinta.Y:0.0} "
                            + $"{tinta.Width:0.0}x{tinta.Height:0.0} "
                            + $"numa caixa {def.Largura}x{def.Altura})");
        }

        saida.AppendLine($"        {olhadas.Length} pecas conferidas, "
                         + $"{vazando.Count} vazando");

        Conferir("arte: nenhuma peca desenha fora da propria caixa",
            vazando.Count == 0, string.Join(" · ", vazando));

        // As que estavam tortas. O canto da tinta e onde a peca comeca
        // de verdade: se voltar a andar, o ajuste de Pecas.cs quebrou.
        //
        // O motor de passo (o NEMA 17 da Alpha 25, desenho proprio) comeca a
        // 1 mm do canto: a tampa de 42,3 mm na caixa de 44,3, sem ajuste.
        (string id, double x, double y)[] arrumadas =
        [
            ("arduino-mega", 0, 0),
            ("stepper-motor", 3.4, 3.4),
        ];

        foreach (var (id, x, y) in arrumadas)
        {
            var tinta = Pecas.TintaDeParaTeste(id);
            saida.AppendLine($"        {id,-16} tinta em ({tinta.X:0.0}, {tinta.Y:0.0})");

            Conferir($"arte: {id} continua ajustada",
                Math.Abs(tinta.X - x) < 2 && Math.Abs(tinta.Y - y) < 2,
                $"({tinta.X:0.0}, {tinta.Y:0.0}) em vez de ({x}, {y})");
        }

        // O LED e o LED RGB: a perna tem que chegar no pino.
        foreach (string id in new[] { "led", "rgb-led" })
        {
            var def = Catalogo.Obter(id);
            var tinta = Pecas.TintaDeParaTeste(id);

            double pinoMaisBaixo = def.Pinos.Max(x => x.Y);

            saida.AppendLine($"        {id,-16} desenho de {tinta.Top:0.0} a {tinta.Bottom:0.0}, "
                             + $"pino mais baixo em {pinoMaisBaixo:0.0}");

            // O fio nao pega necessariamente na PONTA da perna -- no LED
            // RGB as pernas sao abertas e o contato fica no meio delas.
            // O que nao pode e o pino ficar solto no vazio, fora do
            // desenho: ai o fio sai de um ponto onde nao ha perna.
            Conferir($"arte: o pino do {id} cai sobre a perna",
                pinoMaisBaixo > tinta.Top + tinta.Height / 2 && pinoMaisBaixo <= tinta.Bottom + 2,
                $"pino em {pinoMaisBaixo:0.0}, desenho de {tinta.Top:0.0} a {tinta.Bottom:0.0}");

            double centroDosPinos = (def.Pinos.Min(x => x.X) + def.Pinos.Max(x => x.X)) / 2;
            double centroDaTinta = tinta.X + tinta.Width / 2;

            Conferir($"arte: os pinos do {id} ficam sob o desenho",
                Math.Abs(centroDosPinos - centroDaTinta) < 6,
                $"pinos em {centroDosPinos:0.0}, desenho em {centroDaTinta:0.0}");
        }
    }

    static void Pecas_CliqueFisico()
    {
        saida.AppendLine("clique fisico nas pecas");

        {
            var c = new Circuito();
            var dip = new PecaNoCircuito { Tipo = "dip-switch-8", X = 0, Y = 0 };
            c.Pecas.Add(dip);

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var def = Catalogo.Obter("dip-switch-8");
            var alvo = Geometria.PosicaoLocal(dip, def, 4.693 + 2 * 9.6 + 2.9, 21.2 + 6.5);

            bool pegou = ed.Apertar(alvo);
            int depoisDoPrimeiro = dip.Bits;

            ed.Apertar(alvo);
            int depoisDoSegundo = dip.Bits;

            var longe = Geometria.PosicaoLocal(dip, def, 4.693 + 2 * 9.6 + 2.9, -30);
            ed.Apertar(longe);
            int foraDaChave = dip.Bits;

            saida.AppendLine($"        dip: clique={pegou} bits {depoisDoPrimeiro}"
                             + $" -> {depoisDoSegundo}, fora da chave -> {foraDaChave}");

            Conferir("dip: clicar na chave 3 liga so ela",
                pegou && depoisDoPrimeiro == 4, $"bits={depoisDoPrimeiro}");
            Conferir("dip: clicar de novo desliga",
                depoisDoSegundo == 0, $"bits={depoisDoSegundo}");
            Conferir("dip: clicar fora das chaves nao vira nada",
                foraDaChave == 0, $"bits={foraDaChave}");
        }

        {
            var c = new Circuito();
            var botao = new PecaNoCircuito { Tipo = "pushbutton-6mm", X = 200, Y = 0 };
            c.Pecas.Add(botao);

            var ed = new EditorCircuito();
            ed.Carregar(c);

            int antes = ed.MarcasApertadasParaTeste;

            var def = Catalogo.Obter("pushbutton-6mm");
            ed.Apertar(Geometria.PosicaoLocal(botao, def, 14.0, 11.5));
            int durante = ed.MarcasApertadasParaTeste;

            ed.Soltar();
            int depois = ed.MarcasApertadasParaTeste;

            saida.AppendLine($"        botao 6mm: marcas {antes} -> {durante} -> {depois}");

            Conferir("botao: a capa escurece enquanto esta apertado",
                durante > antes, $"{antes} -> {durante}");
            Conferir("botao: solta e a marca some",
                depois == antes, $"{durante} -> {depois}");
        }
    }

    static void Pecas_TecladoEDisco()
    {
        saida.AppendLine("teclado matricial e disco de telefone");

        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 600, Y = 0 };
            var teclado = new PecaNoCircuito { Tipo = "membrane-keypad", X = 0, Y = 0 };
            c.Pecas.AddRange([placa, teclado]);

            // Ligado de verdade: e a placa que o teclado alcanca que
            // recebe a leitura da tecla.
            foreach (var (pino, alvo) in new[] { ("R1", "D13"), ("R2", "D12"),
                                                 ("R3", "D14"), ("R4", "D27"),
                                                 ("C1", "D26"), ("C2", "D25"),
                                                 ("C3", "D33"), ("C4", "D32") })
                c.Fios.Add(new Fio { DePeca = teclado.Id, DePino = pino,
                                     ParaPeca = placa.Id, ParaPino = alvo });

            var janela = new MainWindow();
            janela.TesteAbrir(c, """
                #include <Keypad.h>
                Keypad teclado = Keypad(0, 0, 0, 4, 4);
                void setup() { Serial.begin(115200); }
                void loop() {
                  char t = teclado.getKey();
                  if (t) Serial.println(t);
                  delay(20);
                }
                """);

            var ed = janela.TesteEditorCircuito;
            var def = Catalogo.Obter("membrane-keypad");

            var (x5, y5, _) = EditorCircuito.TeclasMatriciais.First(t => t.Tecla == "5");
            bool pegou = ed.Apertar(Geometria.PosicaoLocal(teclado, def, x5, y5));
            int marcas = ed.MarcasApertadasParaTeste;

            janela.TesteRodar();
            EsperarPor(() => LinhasDoSerial(janela.TesteMaquinaDa(placa.Id)).Count > 0, 3000);
            var comDedo = LinhasDoSerial(janela.TesteMaquinaDa(placa.Id));

            ed.Soltar();
            int marcasDepois = ed.MarcasApertadasParaTeste;

            // Solto, o que importa e que a leitura PARA de crescer.
            EsperarPor(() => false, 400);
            int quantasAteSoltar = LinhasDoSerial(janela.TesteMaquinaDa(placa.Id)).Count;

            EsperarPor(() => false, 400);
            int depoisDeSoltar = LinhasDoSerial(janela.TesteMaquinaDa(placa.Id)).Count;
            janela.TesteParar();

            saida.AppendLine($"        clique na tecla 5: pegou={pegou}, marcas {marcas}"
                             + $", serial: {string.Join(",", comDedo.Take(3))}");

            Conferir("teclado: o clique na tecla e reconhecido", pegou, "nao pegou");
            Conferir("teclado: a tecla apertada acende", marcas > 0, $"{marcas} marcas");
            Conferir("teclado: o sketch le a tecla que foi clicada",
                comDedo.Contains("5"), string.Join(",", comDedo));
            Conferir("teclado: soltar apaga a marca", marcasDepois == 0, $"{marcasDepois}");
            Conferir("teclado: solto, o sketch para de ler a tecla",
                depoisDeSoltar == quantasAteSoltar,
                $"{quantasAteSoltar} -> {depoisDeSoltar}");

            // Segurada, a tecla conta UMA vez -- o getKey() da biblioteca so
            // devolve a tecla no aperto.
            Conferir("teclado: a tecla segurada conta uma vez so",
                comDedo.Count(l => l == "5") == 1, string.Join(",", comDedo));
        }

        {
            var c = new Circuito();
            var disco = new PecaNoCircuito { Tipo = "rotary-dialer", X = 0, Y = 0 };
            c.Pecas.Add(disco);

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var def = Catalogo.Obter("rotary-dialer");
            var (x3, y3, _) = EditorCircuito.FurosDoDisco.First(f => f.Numero == 3);

            bool antes = ed.Discando(disco.Id);
            bool pegou = ed.Apertar(Geometria.PosicaoLocal(disco, def, x3, y3));
            bool depois = ed.Discando(disco.Id);

            var meio = Geometria.PosicaoLocal(disco, def, 133, 133);
            var edLimpo = new EditorCircuito();
            var c2 = new Circuito();
            var disco2 = new PecaNoCircuito { Tipo = "rotary-dialer", X = 0, Y = 0 };
            c2.Pecas.Add(disco2);
            edLimpo.Carregar(c2);
            edLimpo.Apertar(meio);
            bool noMeio = edLimpo.Discando(disco2.Id);

            saida.AppendLine($"        furo 3: pegou={pegou}, discando {antes} -> {depois}"
                             + $"; clique no centro disca? {noMeio}");

            Conferir("disco: clicar no furo comeca a discagem",
                pegou && !antes && depois, $"{antes} -> {depois}");
            Conferir("disco: clicar no meio do disco nao disca", !noMeio, "discou");
        }
    }

    static void Pecas_ChaveEJoystick()
    {
        saida.AppendLine("chave deslizante e joystick");

        {
            var ed = new EditorCircuito();
            ed.Carregar(new Circuito());

            var chave = ed.Adicionar("slide-switch", new System.Windows.Point(200, 200));
            var def = Catalogo.Obter("slide-switch");

            double comecou = chave.Giro;
            var naAlavanca = Geometria.PosicaoLocal(chave, def, 15.6, 4.5);

            bool pegou = ed.Apertar(naAlavanca);
            double depoisDeUm = chave.Giro;

            ed.Apertar(naAlavanca);
            double depoisDeDois = chave.Giro;

            var noCorpo = Geometria.PosicaoLocal(chave, def, 16.0, 28.0);
            ed.Apertar(noCorpo);
            double depoisDoCorpo = chave.Giro;

            saida.AppendLine($"        chave: {comecou} -> {depoisDeUm} -> {depoisDeDois}"
                             + $", clique no corpo -> {depoisDoCorpo}");

            Conferir("chave: clicar na alavanca liga", pegou && depoisDeUm == 1,
                $"{comecou} -> {depoisDeUm}");
            Conferir("chave: clicar de novo desliga", depoisDeDois == 0, $"{depoisDeDois}");
            Conferir("chave: clicar no corpo nao vira a chave",
                depoisDoCorpo == depoisDeDois, $"{depoisDoCorpo}");

            Conferir("chave: a alavanca tem desenho proprio para deslizar",
                Pecas.Existe("slide-switch-movel") && Pecas.Existe("slide-switch-corpo"),
                "faltou metade");

            ed.DefinirGiro(chave, 0);
            double esquerda = ed.DeslocamentoDoMovelParaTeste(chave.Id);
            ed.DefinirGiro(chave, 1);
            double direita = ed.DeslocamentoDoMovelParaTeste(chave.Id);

            saida.AppendLine($"        alavanca desliza de {esquerda:0.0} para {direita:0.0} px");
            Conferir("chave: a alavanca muda de lugar ao virar",
                Math.Abs(direita - esquerda) > 3, $"{esquerda:0.0} -> {direita:0.0}");
        }

        {
            var ed = new EditorCircuito();
            ed.Carregar(new Circuito());

            var joystick = ed.Adicionar("analog-joystick", new System.Windows.Point(200, 200));
            saida.AppendLine($"        joystick novo: giro {joystick.Giro}, giro2 {joystick.Giro2}");

            Conferir("joystick: nasce com o eixo vertical no meio",
                Math.Abs(joystick.Giro - 0.5) < 1e-9, joystick.Giro.ToString());
            Conferir("joystick: nasce com o eixo horizontal no meio",
                Math.Abs(joystick.Giro2 - 0.5) < 1e-9, joystick.Giro2.ToString());

            var led = ed.Adicionar("led", new System.Windows.Point(400, 200));
            Conferir("joystick: o padrao nao vazou para as outras pecas",
                led.Giro == 0, led.Giro.ToString());
        }
    }

    static void Programa_Configuracoes()
    {
        saida.AppendLine("janela de configuracoes");

        var ajustes = new AjustesDoPrograma(
            LerGrade: () => true, DefinirGrade: _ => { },
            LerEstiloDoFio: () => "solto", DefinirEstiloDoFio: _ => { },
            LerInternet: () => false, DefinirInternet: _ => { },
            LerCorDoFio: () => "#00A651", DefinirCorDoFio: _ => { },
            Cores: [("Verde", "#00A651"), ("Vermelho", "#E53935")],
            LerServidores: () => "nenhum servidor",
            LerBluetooth: () => "nenhum dispositivo",
            AbrirPainelDeRede: () => { }, AbrirPainelDeBluetooth: () => { },
            AbrirAjuda: () => { });

        var janela = new JanelaConfiguracoes(null, ajustes);

        var nomes = janela.AssuntosParaTeste;
        saida.AppendLine($"        assuntos: {string.Join(" | ", nomes)}");

        Conferir("configuracoes: a lista tem os assuntos separados",
            nomes.Count >= 8 && nomes.Contains("Circuito") && nomes.Contains("Tela e janela")
            && nomes.Contains("Bluetooth"), string.Join("|", nomes));

        Conferir("configuracoes: abre no primeiro assunto",
            janela.AssuntoAbertoParaTeste == nomes[0], janela.AssuntoAbertoParaTeste);

        int todos = janela.ItensDaListaParaTeste;
        janela.BuscarParaTeste("tema");
        int comTema = janela.ItensDaListaParaTeste;
        string abriu = janela.AssuntoAbertoParaTeste;

        saida.AppendLine($"        busca \"tema\": {todos} -> {comTema} itens, abriu {abriu}");

        Conferir("configuracoes: a busca reduz a lista", comTema < todos, $"{todos} -> {comTema}");
        Conferir("configuracoes: a busca abre o assunto achado",
            abriu == "Aparência", abriu);

        janela.BuscarParaTeste("1366");
        Conferir("configuracoes: a busca acha pelo que a pessoa escreveria",
            janela.AssuntoAbertoParaTeste == "Tela e janela", janela.AssuntoAbertoParaTeste);

        janela.BuscarParaTeste("xyzabc");
        Conferir("configuracoes: busca sem resposta nao quebra",
            janela.ItensDaListaParaTeste == 0, $"{janela.ItensDaListaParaTeste} itens");

        janela.BuscarParaTeste("");
        Conferir("configuracoes: limpar a busca devolve a lista inteira",
            janela.ItensDaListaParaTeste == todos, $"{janela.ItensDaListaParaTeste}/{todos}");

        janela.Close();
    }

    static void Programa_JanelaCabeNaTela()
    {
        saida.AppendLine("a janela cabe na tela");

        // A tela da sala de aula. O tamanho de fabrica era 1400x800:
        // mais largo e mais alto do que cabe nela.
        var sala = new System.Windows.Rect(0, 0, 1366, 728);
        var deFabrica = new System.Windows.Rect(0, 0, 1400, 800);

        var cabe = MainWindow.CaixaQueCabe(sala, deFabrica);
        saida.AppendLine($"        1400x800 em 1366x728 -> {cabe.Width:0}x{cabe.Height:0}");

        Conferir("janela: encolhe para caber na largura da tela",
            cabe.Width <= sala.Width, $"{cabe.Width:0} > {sala.Width:0}");
        Conferir("janela: encolhe para caber na altura da tela",
            cabe.Height <= sala.Height, $"{cabe.Height:0} > {sala.Height:0}");
        Conferir("janela: fica dentro da area util",
            cabe.Left >= sala.Left && cabe.Top >= sala.Top
            && cabe.Right <= sala.Right + 0.01 && cabe.Bottom <= sala.Bottom + 0.01,
            cabe.ToString());

        // Monitor trocado: a janela guardada estava num lugar que nao
        // existe mais e tem que voltar para dentro.
        var perdida = new System.Windows.Rect(2400, 1300, 1200, 700);
        var achada = MainWindow.CaixaQueCabe(sala, perdida);
        saida.AppendLine($"        vinda de (2400,1300) -> ({achada.Left:0},{achada.Top:0})");

        Conferir("janela: a que estava fora da tela volta para dentro",
            achada.Right <= sala.Right + 0.01 && achada.Bottom <= sala.Bottom + 0.01
            && achada.Left >= 0 && achada.Top >= 0, achada.ToString());

        var grande = new System.Windows.Rect(0, 0, 1920, 1032);
        var solta = MainWindow.CaixaQueCabe(grande, deFabrica);
        Conferir("janela: em tela grande o tamanho e respeitado",
            Math.Abs(solta.Width - 1400) < 0.01 && Math.Abs(solta.Height - 800) < 0.01,
            $"{solta.Width:0}x{solta.Height:0}");

        var centrada = MainWindow.CaixaCentralizada(sala, 900, 560);
        saida.AppendLine($"        ajuste manual em 1366x728 -> {centrada.Width:0}"
                         + $"x{centrada.Height:0} em ({centrada.Left:0},{centrada.Top:0})");

        Conferir("janela: o ajuste manual centraliza dentro da tela",
            centrada.Right <= sala.Right + 0.01 && centrada.Bottom <= sala.Bottom + 0.01
            && centrada.Left >= 0 && centrada.Top >= 0
            && centrada.Width >= 900 && centrada.Height >= 560,
            centrada.ToString());

        var minuscula = new System.Windows.Rect(0, 0, 800, 480);
        var apertada = MainWindow.CaixaCentralizada(minuscula, 900, 560);
        Conferir("janela: tela menor que o minimo nao vira janela quebrada",
            apertada.Width >= 800 && apertada.Height >= 480, apertada.ToString());
    }
    static void Programa_Bibliotecas()
    {
        saida.AppendLine("gerenciador de bibliotecas");

        var semNada = Acervo.SemDono("""
            #include <WiFi.h>
            #include <DHT.h>
            void setup() {}
            """);
        saida.AppendLine($"        sketch so com o que roda: {semNada.Count} sem dono");
        Conferir("bibliotecas: o que o simulador executa nao entra como faltando",
            semNada.Count == 0, string.Join(",", semNada));

        var faltando = Acervo.SemDono("""
            #include <WiFi.h>
            #include <FastLED.h>
            #include "MinhaLib.h"
            void setup() {}
            """);
        saida.AppendLine($"        sketch com biblioteca de fora: {string.Join(",", faltando)}");
        Conferir("bibliotecas: aponta o include que ninguem atende",
            faltando.Contains("FastLED.h"), string.Join(",", faltando));
        Conferir("bibliotecas: acha tambem o include entre aspas",
            faltando.Contains("MinhaLib.h"), string.Join(",", faltando));
        Conferir("bibliotecas: nao acusa o que o motor aceita de graca",
            !faltando.Contains("WiFi.h"), string.Join(",", faltando));

        var todas = Acervo.Todas();
        saida.AppendLine($"        acervo: {todas.Count} bibliotecas");
        Conferir("bibliotecas: o acervo lista as do simulador",
            todas.Count >= 20 && todas.Any(b => b.Cabecalho == "RF24.h")
            && todas.Any(b => b.Cabecalho == "Keypad.h"), $"{todas.Count}");
        Conferir("bibliotecas: toda entrada diz para que serve",
            todas.All(b => !string.IsNullOrWhiteSpace(b.Resumo)), "faltou resumo");

        var janela = new JanelaBibliotecas(null, "#include <FastLED.h>\nvoid setup() {}");
        int listadas = janela.ListadasParaTeste;

        janela.BuscarParaTeste("radio");
        int comRadio = janela.ListadasParaTeste;

        janela.BuscarParaTeste("zzzz");
        int semResposta = janela.ListadasParaTeste;

        janela.BuscarParaTeste("");
        int devolta = janela.ListadasParaTeste;
        janela.Close();

        saida.AppendLine($"        janela: {listadas} -> radio {comRadio}"
                         + $" -> sem resposta {semResposta} -> {devolta}");

        Conferir("bibliotecas: a janela lista o acervo", listadas >= 20, $"{listadas}");
        Conferir("bibliotecas: a busca filtra", comRadio > 0 && comRadio < listadas,
            $"{comRadio}");
        Conferir("bibliotecas: busca sem resposta nao quebra", semResposta == 0,
            $"{semResposta}");
        Conferir("bibliotecas: limpar a busca devolve tudo", devolta == listadas,
            $"{devolta}/{listadas}");
    }

    static void Chip_Proprio()
    {
        saida.AppendLine("chip proprio");

        const string json = """
            {
              "nome": "Inversor",
              "autor": "turma",
              "pinos": [
                { "nome": "IN", "lado": "esquerda" },
                { "nome": "OUT", "lado": "direita" }
              ]
            }
            """;

        const string codigo = """
            void chipInit() {
              modoDoPino("IN", INPUT);
              modoDoPino("OUT", OUTPUT);
            }

            void chipLoop() {
              escreverPino("OUT", lerPino("IN") ? 0 : 1);
            }
            """;

        {
            var chip = ChipCustom.Ler(json, codigo, doProjeto: true);

            saida.AppendLine($"        lido: id={chip?.Id}, {chip?.Pinos.Count} pinos,"
                             + $" caixa {chip?.LarguraFinal:0}x{chip?.AlturaFinal:0}");

            Conferir("chip: o .chip.json vira uma peca", chip is not null, "nao leu");
            Conferir("chip: o id sai do nome", chip?.Id == "chip-inversor", chip?.Id);
            Conferir("chip: os pinos vem do json",
                chip is not null && chip.Pinos.Count == 2
                && chip.Pinos[0].Nome == "IN" && chip.Pinos[1].Lado == "direita",
                string.Join(",", chip?.Pinos.Select(x => x.Nome + ":" + x.Lado) ?? []));

            var naCaixa = chip.PinosNaCaixa();
            Conferir("chip: o pino da esquerda encosta na borda esquerda",
                naCaixa[0].X == 0, naCaixa[0].X.ToString());
            Conferir("chip: o pino da direita encosta na borda direita",
                Math.Abs(naCaixa[1].X - chip.LarguraFinal) < 0.01, naCaixa[1].X.ToString());

            string desenho = chip.Svg();
            Conferir("chip: o desenho sai com o nome dos pinos",
                desenho.Contains(">IN<") && desenho.Contains(">OUT<")
                && desenho.Contains("<svg"), desenho.Length.ToString());

            Conferir("chip: nome sem pinos nao vira peca",
                ChipCustom.Ler("{\"nome\":\"Vazio\"}", "", true) is null, "virou peca");
            Conferir("chip: json quebrado nao derruba nada",
                ChipCustom.Ler("{ isso nao e json", "", true) is null, "nao reclamou");
        }

        {
            var c = new Circuito
            {
                Arquivos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["inversor.chip.json"] = json,
                    ["inversor.chip.ino"] = codigo,
                },
            };

            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var chipNoCircuito = new PecaNoCircuito { Tipo = "chip-inversor", X = 400, Y = 0 };
            c.Pecas.AddRange([placa, chipNoCircuito]);

            c.Fios.Add(new Fio { DePeca = chipNoCircuito.Id, DePino = "IN",
                                 ParaPeca = placa.Id, ParaPino = "D13" });
            c.Fios.Add(new Fio { DePeca = chipNoCircuito.Id, DePino = "OUT",
                                 ParaPeca = placa.Id, ParaPino = "D12" });

            var janela = new MainWindow();
            janela.TesteAbrir(c, """
                void setup() {
                  Serial.begin(115200);
                  pinMode(13, OUTPUT);
                  pinMode(12, INPUT);
                }
                void loop() {
                  digitalWrite(13, HIGH);
                  delay(150);
                  Serial.println(digitalRead(12));
                  digitalWrite(13, LOW);
                  delay(150);
                  Serial.println(digitalRead(12));
                }
                """);

            var def = Catalogo.Obter("chip-inversor");
            saida.AppendLine($"        no catalogo: {def?.Nome} ({def?.Categoria})");

            Conferir("chip: entra no catalogo de pecas ao abrir o projeto",
                def is not null && def.Categoria == "Chips", def?.Categoria ?? "nao entrou");

            janela.TesteRodar();
            EsperarPor(() => LinhasDoSerial(janela.TesteMaquinaDa(placa.Id)).Count >= 4, 6000);
            var linhas = LinhasDoSerial(janela.TesteMaquinaDa(placa.Id));
            var erros = janela.TesteEditorCircuito.Chips.Erros.Values.ToList();
            janela.TesteParar();

            saida.AppendLine($"        IN alto/baixo -> OUT: {string.Join(",", linhas.Take(4))}");
            if (erros.Count > 0) saida.AppendLine($"        erros: {string.Join(" | ", erros)}");

            Conferir("chip: roda sem erro", erros.Count == 0, string.Join(" | ", erros));
            Conferir("chip: inverte o que chega no pino",
                linhas.Count >= 2 && linhas.Contains("0") && linhas.Contains("1"),
                string.Join(",", linhas));
        }

        {
            var c = new Circuito
            {
                Arquivos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["quebrado.chip.json"] = json.Replace("Inversor", "Quebrado"),
                    ["quebrado.chip.ino"] = "void chipLoop() { escreverPino(\"NAOEXISTE\", 1); }",
                },
            };
            c.Pecas.Add(new PecaNoCircuito { Tipo = "chip-quebrado", X = 0, Y = 0 });

            var janela = new MainWindow();
            janela.TesteAbrir(c, "void setup() {}\nvoid loop() {}");
            janela.TesteRodar();
            EsperarPor(() => janela.TesteEditorCircuito.Chips.Erros.Count > 0, 3000);
            var erros = janela.TesteEditorCircuito.Chips.Erros.Values.ToList();
            janela.TesteParar();

            saida.AppendLine($"        pino que nao existe: {string.Join(" | ", erros)}");
            Conferir("chip: pino que nao existe vira erro com o nome do chip",
                erros.Any(e => e.Contains("NAOEXISTE")), string.Join(" | ", erros));
        }
    }

    /// <summary>
    /// A bateria esta rodando agora.
    ///
    /// Os testes deixam dezenas de MainWindow vivas -- fechar cada uma
    /// custaria mais codigo do que o que elas verificam. No fim, o
    /// Shutdown fecha todas, e a que tiver circuito alterado abriria o
    /// "salvar antes de sair?": um dialogo modal sem ninguem para
    /// responder trava o processo e a bateria nunca termina.
    /// </summary>
    internal static bool Rodando { get; set; }


    // ---------------- MicroPython ----------------
    /// <summary>
    /// Aspas e apostrofo por codigo.
    ///
    /// Os sketches de teste sao Python DENTRO de texto de C#, e o Python
    /// usa os dois tipos de aspas. Escapando, cada linha viraria um muro
    /// de barras invertidas e ninguem mais leria o sketch.
    /// </summary>
    static readonly string Q = ((char)34).ToString();
    static readonly string A = ((char)39).ToString();

    /// <summary>
    /// Roda um sketch em MicroPython e devolve o que saiu no monitor.
    ///
    /// Mesma Maquina do lado C++ de proposito: e ela que prova que as
    /// duas linguagens chegam no MESMO simulador, e nao em dois.
    /// </summary>
    static (Maquina Maq, string Serial) RodarPython(string codigo, int voltas = 0)
    {
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maq.Reiniciar();

        var prog = Compilador.Compilar(codigo, Linguagem.MicroPython);
        var interp = new Interpretador(prog, maq);
        interp.Preparar();
        interp.Setup();

        for (int i = 0; i < voltas && interp.TemLoop; i++) interp.Loop();

        return (maq, SoOTexto(maq));
    }

    /// <summary>
    /// As linhas do monitor sem o carimbo de tempo.
    ///
    /// O carimbo ("[  120ms] ") serve para ler na tela, mas dentro do
    /// teste ele so atrapalha: a conferencia e sobre o que o sketch
    /// IMPRIMIU, e nao sobre quando.
    /// </summary>
    static string SoOTexto(Maquina maq)
        => string.Join("|", maq.Serial.Select(l =>
        {
            int fecha = l.IndexOf("] ", StringComparison.Ordinal);
            return fecha >= 0 && l.StartsWith('[') ? l[(fecha + 2)..] : l;
        }));

    static void Python_Linguagem()
    {
        saida.AppendLine("micropython: a linguagem");

        // As contas em que o Python difere do C. A divisao e a pegadinha
        // numero um de quem vem do Arduino: 7/2 la e 3, aqui e 3.5.
        var (_, contas) = RodarPython(string.Join("\n",
        [
            "print(7 / 2, 7 // 2, 7 % 2, -1 % 3, 2 ** 10)",
        ]));
        saida.AppendLine($"        contas: {contas}");
        Conferir("python: 7/2 da real", contas.Contains("3.5"), contas);
        Conferir("python: 7//2 da inteiro", contas.Contains(" 3 "), contas);
        Conferir("python: -1%3 acompanha o divisor", contas.Contains(" 2 "), contas);
        Conferir("python: 2**10 continua inteiro",
            contas.Contains("1024") && !contas.Contains("1024.0"), contas);

        // Lista, tupla, dicionario e fatia.
        var (_, colecoes) = RodarPython(string.Join("\n",
        [
            "nums = [3, 1, 2]",
            "nums.append(4)",
            "nums.sort()",
            "print(nums, len(nums), nums[1:3], nums[-1])",
            "cor = (148, 0, 211)",
            "d = {" + Q + "t" + Q + ": 25}",
            "d[" + Q + "u" + Q + "] = 50",
            "print(cor, cor[0], d, d.get(" + Q + "x" + Q + ", " + Q + "sem" + Q + "))",
        ]));
        saida.AppendLine($"        colecoes: {colecoes}");
        Conferir("python: lista ordena e cresce", colecoes.Contains("[1, 2, 3, 4]"), colecoes);
        Conferir("python: fatia", colecoes.Contains("[2, 3]"), colecoes);
        Conferir("python: indice negativo", colecoes.Contains(" 4|") || colecoes.EndsWith(" 4"), colecoes);
        Conferir("python: tupla com parenteses", colecoes.Contains("(148, 0, 211)"), colecoes);
        Conferir("python: dicionario na ordem escrita",
            colecoes.Contains("{" + A + "t" + A + ": 25, " + A + "u" + A + ": 50}"), colecoes);

        // Texto: format, porcento e f-string, os tres jeitos que os
        // sketches usam.
        var (_, textos) = RodarPython(string.Join("\n",
        [
            "n = 7",
            "print(" + Q + "{} e {}" + Q + ".format(" + Q + "a" + Q + ", 2))",
            "print(" + Q + "%s vale %d e %.2f" + Q + " % (" + Q + "x" + Q + ", 42, 3.14159))",
            "print(f" + Q + "{n} ao quadrado e {n*n}, {3.14159:.2f}" + Q + ")",
            "print(" + Q + "  CircuitSketch  " + Q + ".strip().lower())",
            "print(" + Q + "-" + Q + ".join([" + Q + "a" + Q + ", " + Q + "b" + Q + "]))",
        ]));
        saida.AppendLine($"        textos: {textos}");
        Conferir("python: .format()", textos.Contains("a e 2"), textos);
        Conferir("python: %s %d %.2f", textos.Contains("x vale 42 e 3.14"), textos);
        Conferir("python: f-string com conta e casas",
            textos.Contains("7 ao quadrado e 49, 3.14"), textos);
        Conferir("python: strip e lower", textos.Contains("circuitsketch"), textos);
        Conferir("python: join", textos.Contains("a-b"), textos);

        // Fluxo: for-in, enumerate, while, ternario, in, encadeada.
        var (_, fluxo) = RodarPython(string.Join("\n",
        [
            "total = 0",
            "for i in range(1, 5):",
            "    total += i",
            "print(total, sum(range(1, 5)))",
            "for i, v in enumerate([" + Q + "a" + Q + ", " + Q + "b" + Q + "]):",
            "    print(i, v)",
            "n = 7",
            "print(0 <= n < 10, 0 <= 20 < 10)",
            "print(2 in [1, 2], 9 not in [1, 2])",
            "print(" + Q + "par" + Q + " if n % 2 == 0 else " + Q + "impar" + Q + ")",
            "print(None is None)",
            "if n > 5: print(" + Q + "uma linha" + Q + ")",
            "print(" + Q + "depois" + Q + ")",
        ]));
        saida.AppendLine($"        fluxo: {fluxo}");
        Conferir("python: for in range soma", fluxo.Contains("10 10"), fluxo);
        Conferir("python: enumerate desempacota", fluxo.Contains("0 a|1 b"), fluxo);
        Conferir("python: comparacao encadeada", fluxo.Contains("True False"), fluxo);
        Conferir("python: in e not in", fluxo.Contains("True True"), fluxo);
        Conferir("python: ternario", fluxo.Contains("impar"), fluxo);
        Conferir("python: None e True/False por extenso",
            fluxo.Contains("True") && !fluxo.Contains("|1|"), fluxo);
        Conferir("python: bloco de uma linha nao engole a seguinte",
            fluxo.Contains("uma linha|depois"), fluxo);

        // Funcao com valor padrao.
        var (_, funcoes) = RodarPython(string.Join("\n",
        [
            "def saudacao(nome, prefixo=" + Q + "Ola" + Q + "):",
            "    return prefixo + " + Q + ", " + Q + " + nome",
            "print(saudacao(" + Q + "turma" + Q + "), saudacao(" + Q + "turma" + Q + ", " + Q + "Oi" + Q + "))",
        ]));
        saida.AppendLine($"        funcoes: {funcoes}");
        Conferir("python: parametro com valor padrao",
            funcoes.Contains("Ola, turma Oi, turma"), funcoes);

        // "while True:" no fim do modulo VIRA o loop(), e as variaveis
        // do topo continuam vivas entre as voltas.
        var (_, laco) = RodarPython(string.Join("\n",
        [
            "import time",
            "conta = 0",
            "while True:",
            "    conta += 1",
            "    print(" + Q + "volta" + Q + ", conta)",
            "    time.sleep_ms(1)",
        ]), voltas: 3);
        saida.AppendLine($"        laco: {laco}");
        Conferir("python: while True vira o loop()",
            laco.Contains("volta 1|volta 2|volta 3"), laco);
    }

    static void Python_Pecas()
    {
        saida.AppendLine("micropython: as pecas");

        // Pin: o mesmo GPIO que o digitalWrite acende.
        var (pino, serialPino) = RodarPython(string.Join("\n",
        [
            "from machine import Pin",
            "led = Pin(2, Pin.OUT)",
            "led.on()",
            "print(" + Q + "aceso" + Q + ", led.value())",
            "led.toggle()",
            "print(" + Q + "apagado" + Q + ", led.value())",
        ]));
        saida.AppendLine($"        pino: {serialPino}");
        Conferir("python: Pin.on acende o GPIO", serialPino.Contains("aceso 1"), serialPino);
        Conferir("python: Pin.toggle inverte", serialPino.Contains("apagado 0"), serialPino);
        Conferir("python: o pino mexido e o mesmo do C",
            pino.DigitalRead(2) == 0, "GPIO 2 deveria estar em 0");

        // Pin de entrada com pull-up interno le 1 solto, como no C.
        var (entrada, _) = RodarPython(string.Join("\n",
        [
            "from machine import Pin",
            "b = Pin(4, Pin.IN, Pin.PULL_UP)",
        ]));
        Conferir("python: PULL_UP vira INPUT_PULLUP na Maquina",
            entrada.DigitalRead(4) == 1, "GPIO 4 deveria ler 1");

        // NeoPixel: a cor entra pela tupla e vai parar na fita que o
        // desenho le.
        var (fita, _) = RodarPython(string.Join("\n",
        [
            "from machine import Pin",
            "import neopixel",
            "rgb = neopixel.NeoPixel(Pin(12), 16)",
            "rgb[3] = (148, 0, 211)",
            "rgb.write()",
        ]));
        int cor = fita.Fitas.TryGetValue(12, out var f) ? f.Cores[3] : -1;
        saida.AppendLine($"        neopixel: LED 3 = 0x{cor:X6}");
        Conferir("python: NeoPixel guarda a cor da tupla",
            cor == Fita.Montar(148, 0, 211), $"deu 0x{cor:X6}");
        Conferir("python: a fita tem o tamanho pedido",
            f is not null && f.Quantidade == 16);

        // DHT: a leitura sai da mesma tabela que o DHT.h do C usa.
        var (_, clima) = RodarPython(string.Join("\n",
        [
            "from machine import Pin",
            "import dht",
            "s = dht.DHT22(Pin(15))",
            "s.measure()",
            "print(s.temperature(), s.humidity())",
        ]));
        saida.AppendLine($"        dht: {clima}");
        Conferir("python: DHT mede temperatura e umidade",
            clima.Contains("25.0 50.0"), clima);

        // OLED: escreve na mesma tela de caracteres do Adafruit_SSD1306.
        var (tela, _) = RodarPython(string.Join("\n",
        [
            "from machine import Pin, I2C",
            "import ssd1306",
            "i2c = I2C(0, scl=Pin(22), sda=Pin(21))",
            "oled = ssd1306.SSD1306_I2C(128, 64, i2c)",
            "oled.text(" + Q + "CircuitSketch" + Q + ", 0, 0)",
            "oled.show()",
        ]));
        string primeira = tela.Oled is null ? "" : LinhaDoLcd(tela.Oled, 0);
        saida.AppendLine($"        oled: [{primeira}]");
        Conferir("python: oled.text escreve na tela",
            primeira.Contains("CircuitSketch"), primeira);

        // ADC e PWM.
        var (analogico, serialAdc) = RodarPython(string.Join("\n",
        [
            "from machine import Pin, ADC, PWM",
            "a = ADC(Pin(34))",
            "print(" + Q + "adc" + Q + ", a.read())",
            "p = PWM(Pin(5))",
            "p.duty(512)",
        ]));
        saida.AppendLine($"        adc/pwm: {serialAdc}, pwm={analogico.Pwm[5]}");
        Conferir("python: ADC.read devolve a leitura", serialAdc.Contains("adc"), serialAdc);
        Conferir("python: duty de 0..1023 vira 0..255",
            analogico.Pwm[5] == 128, $"deu {analogico.Pwm[5]}");
    }

    /// <summary>Uma linha do display como texto, para conferir o que foi escrito.</summary>
    static string LinhaDoLcd(Lcd lcd, int linha)
    {
        var sb = new StringBuilder();
        for (int c = 0; c < lcd.Colunas; c++) sb.Append(lcd.Tela[linha, c]);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Os tres sketches em MicroPython que o professor mandou como
    /// quebrados.
    ///
    /// Sao os arquivos DELE, letra por letra: se um deles parar de
    /// rodar, o teste avisa antes de a versao sair daqui.
    /// </summary>
    static void Python_SketchesDoProfessor()
    {
        saida.AppendLine("micropython: os sketches do professor");

        // 1) MicroPython OLED Display
        var (telaOled, _) = RodarPython(string.Join("\n",
        [
            "from machine import Pin, I2C",
            "import ssd1306",
            "",
            "i2c = I2C(0, scl=Pin(22), sda=Pin(21))",
            "",
            "oled_width = 128",
            "oled_height = 64",
            "oled = ssd1306.SSD1306_I2C(oled_width, oled_height, i2c)",
            "",
            "oled.text(" + A + "Hello, Wokwi!" + A + ", 10, 10)",
            "oled.show()",
        ]));
        string linhaOled = telaOled.Oled is null ? "" : LinhaDoLcd(telaOled.Oled, 1);
        saida.AppendLine($"        OLED Display: [{linhaOled}]");
        Conferir("professor: MicroPython OLED Display roda",
            linhaOled.Contains("Hello, Wokwi!"), linhaOled);

        // 2) car direction led -- o for com passo negativo e a fita.
        var (seta, _) = RodarPython(string.Join("\n",
        [
            "from machine import Pin",
            "import neopixel",
            "from time import sleep_ms",
            "",
            "pisca = Pin(12, Pin.OUT)",
            "rgb = neopixel.NeoPixel(pisca, 16)",
            "",
            "inicio = 0",
            "fim = 15",
            "",
            "chave = Pin(14, Pin.IN)",
            "seta = Pin(27, Pin.IN)",
            "",
            "if seta.value() == 0:",
            "    if chave.value() == 0:",
            "        for p in range(8, fim,):",
            "            rgb[p] = (148, 0, 211)",
            "            rgb.write()",
            "            sleep_ms(1)",
            "    else:",
            "        for p in range(7, inicio, -1):",
            "            rgb[p] = (148, 0, 211)",
            "            rgb.write()",
            "            sleep_ms(1)",
        ]));
        var fitaSeta = seta.Fitas.GetValueOrDefault(12);
        int acesos = fitaSeta is null ? 0 : fitaSeta.Cores.Count(c => c != 0);
        saida.AppendLine($"        car direction led: {acesos} LEDs acesos");
        Conferir("professor: car direction led acende a fita", acesos == 7, $"acendeu {acesos}");

        // 3) MQTT Weather Logger -- o mais completo: WiFi, DHT, json,
        // format e o while True que vira loop.
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maq.Reiniciar();
        maq.Rede = new Rede();

        var prog = Compilador.Compilar(string.Join("\n",
        [
            "import network",
            "import time",
            "from machine import Pin",
            "import dht",
            "import ujson",
            "from umqtt.simple import MQTTClient",
            "",
            "MQTT_CLIENT_ID = " + Q + "micropython-weather-demo" + Q,
            "MQTT_BROKER    = " + Q + "broker.mqttdashboard.com" + Q,
            "MQTT_USER      = " + Q + Q,
            "MQTT_PASSWORD  = " + Q + Q,
            "MQTT_TOPIC     = " + Q + "wokwi-weather" + Q,
            "",
            "sensor = dht.DHT22(Pin(15))",
            "",
            "print(" + Q + "Connecting to WiFi" + Q + ", end=" + Q + Q + ")",
            "sta_if = network.WLAN(network.STA_IF)",
            "sta_if.active(True)",
            "sta_if.connect(" + A + "Wokwi-GUEST" + A + ", " + A + A + ")",
            "while not sta_if.isconnected():",
            "  print(" + Q + "." + Q + ", end=" + Q + Q + ")",
            "  time.sleep(0.1)",
            "print(" + Q + " Connected!" + Q + ")",
            "",
            "client = MQTTClient(MQTT_CLIENT_ID, MQTT_BROKER, user=MQTT_USER, password=MQTT_PASSWORD)",
            "client.connect()",
            "print(" + Q + "Connected!" + Q + ")",
            "",
            "prev_weather = " + Q + Q,
            "while True:",
            "  sensor.measure()",
            "  message = ujson.dumps({",
            "    " + Q + "temp" + Q + ": sensor.temperature(),",
            "    " + Q + "humidity" + Q + ": sensor.humidity(),",
            "  })",
            "  if message != prev_weather:",
            "    print(" + Q + "Updated!" + Q + ")",
            "    print(" + Q + "Reporting to MQTT topic {}: {}" + Q + ".format(MQTT_TOPIC, message))",
            "    client.publish(MQTT_TOPIC, message)",
            "    prev_weather = message",
            "  else:",
            "    print(" + Q + "No change" + Q + ")",
            "  time.sleep(1)",
        ]), Linguagem.MicroPython);

        var interp = new Interpretador(prog, maq);
        interp.Preparar();
        interp.Setup();
        interp.Loop();
        interp.Loop();

        string monitor = SoOTexto(maq);
        saida.AppendLine($"        MQTT Weather: {monitor}");

        Conferir("professor: o WiFi conecta e a barrinha sai numa linha so",
            monitor.Contains("Connecting to WiFi") && monitor.Contains("Connected!"), monitor);
        Conferir("professor: o ujson.dumps monta o payload",
            monitor.Contains(Q + "temp" + Q + ": 25.0"), monitor);
        Conferir("professor: o payload usa 25.0, e nao o 25.00 do Arduino",
            !monitor.Contains("25.00"), monitor);
        Conferir("professor: a segunda volta ve que nada mudou",
            monitor.Contains("No change"), monitor);
        Conferir("professor: prev_weather sobrevive entre as voltas do loop",
            monitor.Split("Updated!").Length == 2, monitor);
    }

    static void Python_Deteccao()
    {
        saida.AppendLine("micropython: quem decide a linguagem");

        var casos = new (string Codigo, Linguagem Esperada, string Rotulo)[]
        {
            ("void setup() {}\nvoid loop() {}", Linguagem.Arduino, "setup/loop"),
            ("#include <WiFi.h>\nvoid setup() {}", Linguagem.Arduino, "#include"),
            ("int x = 5;\nvoid loop() { x++; }", Linguagem.Arduino, "ponto-e-virgula"),
            ("from machine import Pin\nled = Pin(2, Pin.OUT)", Linguagem.MicroPython, "from import"),
            ("import time\nwhile True:\n    time.sleep(1)", Linguagem.MicroPython, "import"),
            ("def piscar():\n    print(1)\npiscar()", Linguagem.MicroPython, "def"),
            ("", Linguagem.Arduino, "vazio"),
        };

        foreach (var (codigo, esperada, rotulo) in casos)
        {
            var achou = Compilador.Detectar(codigo);
            Conferir($"deteccao: {rotulo} -> {Compilador.Nome(esperada)}",
                achou == esperada, $"achou {Compilador.Nome(achou)}");
        }

        // A deteccao e o realce tem que concordar: se a tela pinta
        // Python, e Python que vai rodar.
        Conferir("deteccao: o realce segue a linguagem",
            Realce.Do("escuro", Linguagem.MicroPython) != Realce.Do("escuro", Linguagem.Arduino));
    }

    static void Python_RecusaHonesta()
    {
        saida.AppendLine("micropython: nada passa calado");

        static string Rodar(string codigo)
        {
            try { RodarPython(codigo); return null; }
            catch (Exception e) { return e.Message; }
        }

        // Modulo que nao existe: o erro tem que DIZER quais existem, em
        // vez de reclamar de nome nao declarado tres linhas depois.
        string semModulo = Rodar("import requests\nprint(1)");
        saida.AppendLine($"        import requests: {semModulo}");
        Conferir("python: modulo desconhecido e recusado", semModulo is not null);
        Conferir("python: o erro lista os modulos que existem",
            semModulo is not null && semModulo.Contains("machine"), semModulo);

        // O que a linguagem ainda nao tem sai com o nome certo.
        foreach (var (codigo, oQue) in new[]
                 {
                     // class, try, with e lambda chegaram na Alpha 24 (MicroPython_Linguagem).
                     ("def contar():\n    yield 1", "yield"),
                     ("async def tarefa():\n    pass", "async"),
                 })
        {
            string erro = Rodar(codigo);
            saida.AppendLine($"        {oQue}: {erro}");
            Conferir($"python: {oQue} e recusado com nome", erro is not null, "passou calado");
        }

        // Metodo que a peca nao tem.
        string semMetodo = Rodar("from machine import Pin\nPin(2, Pin.OUT).voar()");
        saida.AppendLine($"        Pin.voar(): {semMetodo}");
        Conferir("python: metodo inexistente de peca e recusado", semMetodo is not null);

        // Indice fora da lista diz o tamanho.
        string foraDaLista = Rodar("x = [1, 2]\nprint(x[9])");
        Conferir("python: indice fora da lista diz o tamanho",
            foraDaLista is not null && foraDaLista.Contains("2"), foraDaLista);

        // Chave que nao esta no dicionario diz quais estao.
        string semChave = Rodar("d = {" + Q + "a" + Q + ": 1}\nprint(d[" + Q + "b" + Q + "])");
        Conferir("python: chave ausente lista as que existem",
            semChave is not null && semChave.Contains("a"), semChave);

        // Indentacao errada.
        string recuo = Rodar("if True:\n    x = 1\n  y = 2");
        Conferir("python: indentacao torta e recusada com explicacao",
            recuo is not null && recuo.Contains("indenta"), recuo);
    }


    // ---------------- diagram.json editavel ----------------
    /// <summary>
    /// A aba do diagrama muda o circuito.
    ///
    /// Era so leitura, e o professor abriu a lista com isso. O que este
    /// teste garante nao e so que da para digitar: e que um JSON
    /// QUEBRADO nao apaga o circuito de quem estava trabalhando.
    /// </summary>
    static void Diagrama_Editavel()
    {
        saida.AppendLine("diagram.json editavel");

        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 40, Y = 40 });
        c.Pecas.Add(new PecaNoCircuito { Id = "led1", Tipo = "led", X = 300, Y = 200 });
        c.Fios.Add(new Fio
        {
            DePeca = "placa", DePino = "D2",
            ParaPeca = "led1", ParaPino = "A",
        });

        var janela = new MainWindow();
        janela.TesteAbrir(c, "void setup() { pinMode(2, OUTPUT); }" + Environment.NewLine
                             + "void loop() {}");
        janela.TesteAbrirArquivo("diagram.json");

        // ---- 1) mexer numa coordenada move a peca ----
        string json = janela.TesteCodigo;
        Conferir("diagrama: o texto descreve o circuito",
            json.Contains("led1") && json.Contains("300"), json.Length.ToString());

        // A aba e o diagram.json do Wokwi: a posicao e o "left".
        Conferir("diagrama: a aba fala o formato do Wokwi, com nome, autor e editor",
            json.Contains(Q + "version" + Q + ": 1") && json.Contains(Q + "name" + Q)
            && json.Contains(Q + "author" + Q) && json.Contains(Q + "editor" + Q + ": " + Q + "circuitsketch" + Q)
            && json.Contains(Q + "parts" + Q) && json.Contains(Q + "connections" + Q),
            json[..Math.Min(200, json.Length)]);
        janela.TesteDigitar(json.Replace(Q + "left" + Q + ": 300", Q + "left" + Q + ": 555"));
        janela.TesteAplicarDiagrama();

        var led = janela.TesteCircuito.Achar("led1");
        saida.AppendLine($"        depois de editar: led1 em X={led?.X}");
        Conferir("diagrama: editar a coordenada move a peca", led is not null && led.X == 555,
            led?.X.ToString() ?? "sumiu");
        Conferir("diagrama: os fios sobrevivem a edicao",
            janela.TesteCircuito.Fios.Count == 1);
        Conferir("diagrama: o sketch da placa sobrevive a edicao",
            janela.TesteCircuito.Achar("placa")?.Sketch?.Contains("pinMode") == true,
            janela.TesteCircuito.Achar("placa")?.Sketch ?? "sumiu");

        // ---- 2) JSON quebrado NAO apaga o circuito ----
        int antes = janela.TesteCircuito.Pecas.Count;
        janela.TesteDigitar("{ " + Q + "Pecas" + Q + ": [ ");
        janela.TesteAplicarDiagrama();

        saida.AppendLine($"        com json quebrado: {janela.TesteCircuito.Pecas.Count} pecas, "
                         + $"aviso = {janela.TesteUltimoAviso}");
        Conferir("diagrama: json quebrado nao apaga o circuito",
            janela.TesteCircuito.Pecas.Count == antes,
            $"sobraram {janela.TesteCircuito.Pecas.Count}");
        Conferir("diagrama: json quebrado avisa na barra",
            janela.TesteUltimoAviso.Contains("diagram.json"), janela.TesteUltimoAviso);

        // ---- 3) peca que nao existe no catalogo e recusada ----
        janela.TesteDigitar("{ " + Q + "Pecas" + Q + ": [ { " + Q + "Id" + Q + ": "
                            + Q + "x" + Q + ", " + Q + "Tipo" + Q + ": "
                            + Q + "reator-nuclear" + Q + " } ], " + Q + "Fios" + Q + ": [] }");
        janela.TesteAplicarDiagrama();
        saida.AppendLine($"        peca inventada: {janela.TesteUltimoAviso}");
        Conferir("diagrama: peca fora do catalogo e recusada pelo nome",
            janela.TesteUltimoAviso.Contains("reator-nuclear"), janela.TesteUltimoAviso);
        Conferir("diagrama: e o circuito continua inteiro",
            janela.TesteCircuito.Pecas.Count == antes);

        // ---- 4) id repetido e recusado ----
        janela.TesteDigitar("{ " + Q + "Pecas" + Q + ": ["
                            + "{ " + Q + "Id" + Q + ": " + Q + "a" + Q + ", "
                            + Q + "Tipo" + Q + ": " + Q + "led" + Q + " },"
                            + "{ " + Q + "Id" + Q + ": " + Q + "a" + Q + ", "
                            + Q + "Tipo" + Q + ": " + Q + "led" + Q + " }"
                            + "], " + Q + "Fios" + Q + ": [] }");
        janela.TesteAplicarDiagrama();
        saida.AppendLine($"        id repetido: {janela.TesteUltimoAviso}");
        Conferir("diagrama: id repetido e recusado",
            janela.TesteUltimoAviso.Contains("mais de uma"), janela.TesteUltimoAviso);

        // ---- 5) colar um circuito inteiro funciona ----
        var outro = new Circuito();
        outro.Pecas.Add(new PecaNoCircuito { Id = "b1", Tipo = "esp32-devkit-v1", X = 10, Y = 10 });
        outro.Pecas.Add(new PecaNoCircuito { Id = "b2", Tipo = "buzzer", X = 200, Y = 100 });
        outro.Pecas.Add(new PecaNoCircuito { Id = "b3", Tipo = "pushbutton", X = 300, Y = 100 });

        janela.TesteDigitar(outro.ParaJson());
        janela.TesteAplicarDiagrama();
        saida.AppendLine($"        depois de colar: {janela.TesteCircuito.Pecas.Count} pecas");
        Conferir("diagrama: da para colar um circuito inteiro",
            janela.TesteCircuito.Pecas.Count == 3
            && janela.TesteCircuito.Achar("b2")?.Tipo == "buzzer",
            $"tem {janela.TesteCircuito.Pecas.Count}");

        // ---- 6) a aba nao pode fugir debaixo do cursor ----
        Conferir("diagrama: a aba continua no diagrama depois de aplicar",
            janela.TesteArquivoAberto == "diagram.json",
            janela.TesteArquivoAberto ?? "voltou para o sketch");

        // ---- 7) mexer no desenho reescreve a aba ----
        janela.TesteSelecionar(2);
        janela.TesteRemoverSelecionada();

        string depois = janela.TesteCodigo;
        saida.AppendLine($"        apos apagar a peca, a aba tem {depois.Length} caracteres");
        Conferir("diagrama: apagar no desenho reescreve o texto da aba",
            !depois.Contains("pushbutton") && depois.Contains("buzzer"),
            depois.Replace(Environment.NewLine, " "));
    }


    // ---------------- aviso de biblioteca que falta ----------------
    /// <summary>
    /// O que falta para o sketch rodar, com tamanho e candidata.
    ///
    /// O professor pediu o AVISO com a opcao de baixar e o tamanho em
    /// disco. Como o programa e offline, "baixar" aqui e copiar a
    /// biblioteca que a Arduino IDE ja instalou nesta maquina -- e e
    /// isso que este teste garante que continua sendo achado.
    /// </summary>
    static void Bibliotecas_QueFaltam()
    {
        saida.AppendLine("bibliotecas que faltam");

        // ---- 1) o que o simulador JA executa nao pode virar aviso ----
        string comConhecidas = "#include <WiFi.h>" + Environment.NewLine
                               + "#include <PubSubClient.h>" + Environment.NewLine
                               + "#include <DHT.h>" + Environment.NewLine
                               + "void setup() {}" + Environment.NewLine
                               + "void loop() {}";
        var nenhuma = Acervo.OQueFalta(comConhecidas);
        saida.AppendLine($"        so conhecidas: {nenhuma.Count} falta(s)");
        Conferir("bibliotecas: o que o simulador executa nao vira aviso",
            nenhuma.Count == 0, string.Join(", ", nenhuma.Select(f => f.Cabecalho)));

        // ---- 2) o que falta e apontado pelo nome ----
        string comEstranha = "#include <ArduinoJson.h>" + Environment.NewLine
                             + "#include <FastLED.h>" + Environment.NewLine
                             + "void setup() {}" + Environment.NewLine
                             + "void loop() {}";
        var faltam = Acervo.OQueFalta(comEstranha);
        saida.AppendLine("        faltam: "
                         + string.Join(", ", faltam.Select(
                             f => $"{f.Cabecalho} ({f.TamanhoEmTexto}"
                                  + (f.Candidata is null ? ", nao achei" : ", achei") + ")")));

        Conferir("bibliotecas: aponta as duas que faltam", faltam.Count == 2,
            string.Join(", ", faltam.Select(f => f.Cabecalho)));
        Conferir("bibliotecas: pelo nome do cabecalho",
            faltam.Any(f => f.Cabecalho == "ArduinoJson.h")
            && faltam.Any(f => f.Cabecalho == "FastLED.h"));

        // ---- 3) o tamanho sai legivel ----
        saida.AppendLine($"        tamanhos: {Acervo.EmTexto(0)}, {Acervo.EmTexto(900)}, "
                         + $"{Acervo.EmTexto(200 * 1024)}, {Acervo.EmTexto(3 * 1024 * 1024)}");
        Conferir("bibliotecas: tamanho em bytes", Acervo.EmTexto(900) == "900 B");
        Conferir("bibliotecas: tamanho em KB", Acervo.EmTexto(200 * 1024) == "200 KB");
        Conferir("bibliotecas: tamanho em MB", Acervo.EmTexto(3 * 1024 * 1024) == "3,0 MB"
                                               || Acervo.EmTexto(3 * 1024 * 1024) == "3.0 MB",
            Acervo.EmTexto(3 * 1024 * 1024));

        // ---- 4) o tamanho de uma pasta de verdade ----
        string temporaria = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "circuitsketch-teste-lib");
        try
        {
            System.IO.Directory.CreateDirectory(temporaria);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(temporaria, "Falsa.h"), new string('x', 2048));

            long bytes = Acervo.TamanhoDaPasta(temporaria);
            saida.AppendLine($"        pasta de teste: {bytes} bytes = {Acervo.EmTexto(bytes)}");
            Conferir("bibliotecas: o tamanho da pasta e medido de verdade",
                bytes == 2048, bytes.ToString());
        }
        finally
        {
            try { System.IO.Directory.Delete(temporaria, true); }
            catch (System.IO.IOException) { }
        }

        Conferir("bibliotecas: pasta que nao existe da zero, e nao erro",
            Acervo.TamanhoDaPasta(System.IO.Path.Combine(temporaria, "nao-existe")) == 0);

        // ---- 5) MicroPython: import de modulo que nao existe ----
        string pythonEstranho = "import requests" + Environment.NewLine
                                + "from machine import Pin" + Environment.NewLine
                                + "import network";
        var semModulo = Acervo.OQueFalta(pythonEstranho);
        saida.AppendLine("        python: "
                         + string.Join(", ", semModulo.Select(f => f.Cabecalho)));
        Conferir("bibliotecas: o import do Python tambem e conferido",
            semModulo.Count == 1 && semModulo[0].Cabecalho == "requests",
            string.Join(", ", semModulo.Select(f => f.Cabecalho)));
        Conferir("bibliotecas: modulo que existe no simulador nao vira aviso",
            !semModulo.Any(f => f.Cabecalho is "machine" or "network"));

        // ---- 6) a busca no computador nao estoura ----
        saida.AppendLine("        pastas do Arduino procuradas: "
                         + Acervo.PastasDoArduino().Count());
        Conferir("bibliotecas: procura em mais de um lugar",
            Acervo.PastasDoArduino().Count() >= 4);
        Conferir("bibliotecas: procurar um .h inexistente devolve null, sem estourar",
            Acervo.ProcurarPasta("NaoExisteMesmo_zzz.h") is null);
    }


    // ---------------- WiFi e Bluetooth do computador ----------------
    /// <summary>
    /// A leitura do radio da maquina.
    ///
    /// O professor perguntou por que o scan achava "Wokwi-GUEST" em vez
    /// das redes dele. A resposta era que o scan era inventado. Agora
    /// parte do computador -- e o que este teste garante e a LEITURA,
    /// com uma saida de netsh gravada, para a suite nao depender de
    /// haver WiFi na maquina que a roda.
    /// </summary>
    static void Radio_DoComputador()
    {
        saida.AppendLine("radio do computador");

        // Saida do netsh em portugues, do jeito que ele responde.
        string emPortugues = string.Join(Environment.NewLine,
        [
            "Nome da interface: Wi-Fi ",
            "Ha 3 redes visiveis no momento. ",
            "",
            "SSID 1 : LEMOS - NIO FIBRA",
            "    Tipo de rede            : Infraestrutura",
            "    Autenticacao            : WPA2-Personal",
            "    Criptografia            : CCMP",
            "    BSSID 1                 : 44:3b:32:e0:df:41",
            "         Sinal             : 100%  ",
            "         Banda               : 5 GHz",
            "",
            "SSID 2 : Cafe da esquina",
            "    Tipo de rede            : Infraestrutura",
            "    Autenticacao            : Aberta",
            "    Criptografia            : Nenhuma",
            "    BSSID 1                 : 02:eb:d8:68:61:ac",
            "         Sinal             : 50%  ",
            "",
            "SSID 3 : ",
            "    Tipo de rede            : Infraestrutura",
            "    Autenticacao            : WPA2-Personal",
            "         Sinal             : 30%  ",
        ]);

        var lidas = RadioDoComputador.Ler(emPortugues);
        saida.AppendLine("        lidas: "
                         + string.Join(", ", lidas.Select(r => $"{r.Ssid} ({r.Rssi} dBm)")));

        Conferir("radio: le as redes com nome, e pula a escondida",
            lidas.Count == 2, lidas.Count.ToString());
        Conferir("radio: o nome sai inteiro, com espacos",
            lidas.Any(r => r.Ssid == "LEMOS - NIO FIBRA"),
            string.Join(", ", lidas.Select(r => r.Ssid)));
        Conferir("radio: 100% vira -50 dBm",
            lidas.FirstOrDefault(r => r.Ssid.StartsWith("LEMOS")).Rssi == -50,
            lidas.FirstOrDefault(r => r.Ssid.StartsWith("LEMOS")).Rssi.ToString());
        Conferir("radio: 50% vira -75 dBm",
            lidas.FirstOrDefault(r => r.Ssid == "Cafe da esquina").Rssi == -75,
            lidas.FirstOrDefault(r => r.Ssid == "Cafe da esquina").Rssi.ToString());
        Conferir("radio: rede sem senha e marcada como aberta",
            lidas.FirstOrDefault(r => r.Ssid == "Cafe da esquina").Aberta);
        Conferir("radio: rede com senha nao e aberta",
            !lidas.FirstOrDefault(r => r.Ssid.StartsWith("LEMOS")).Aberta);

        // O mesmo netsh em ingles: os ROTULOS mudam, a leitura nao pode.
        string emIngles = string.Join(Environment.NewLine,
        [
            "Interface name : Wi-Fi ",
            "There are 2 networks currently visible. ",
            "",
            "SSID 1 : HomeNet",
            "    Network type            : Infrastructure",
            "    Authentication          : WPA2-Personal",
            "    BSSID 1                 : 44:3b:32:e0:df:41",
            "         Signal            : 80%  ",
            "",
            "SSID 2 : FreeWiFi",
            "    Network type            : Infrastructure",
            "    Authentication          : Open",
            "         Signal            : 20%  ",
        ]);

        var doIngles = RadioDoComputador.Ler(emIngles);
        saida.AppendLine("        em ingles: "
                         + string.Join(", ", doIngles.Select(r => $"{r.Ssid} ({r.Rssi})")));
        Conferir("radio: le tambem o Windows em ingles", doIngles.Count == 2);
        Conferir("radio: e reconhece a aberta em ingles",
            doIngles.FirstOrDefault(r => r.Ssid == "FreeWiFi").Aberta);

        // Saida vazia nao pode virar excecao.
        Conferir("radio: saida vazia devolve lista vazia",
            RadioDoComputador.Ler("").Count == 0);

        // ---- sem radio, o simulador avisa e volta as inventadas ----
        RadioDoComputador.Desligado = true;
        RadioDoComputador.Esquecer();

        var rede = new Rede();
        var visiveis = rede.RedesVisiveis();
        saida.AppendLine("        sem radio: "
                         + string.Join(", ", visiveis.Select(r => r.Ssid))
                         + " | motivo = " + rede.MotivoDaSimulacao);

        Conferir("radio: sem radio, volta a rede da casa e a do Wokwi",
            visiveis.Any(r => r.Ssid == "CircuitSketch-WiFi")
            && visiveis.Any(r => r.Ssid == "Wokwi-GUEST"),
            string.Join(", ", visiveis.Select(r => r.Ssid)));

        Conferir("radio: e diz POR QUE esta simulando",
            rede.MotivoDaSimulacao is not null && rede.MotivoDaSimulacao.Length > 0,
            rede.MotivoDaSimulacao ?? "ficou calado");

        // O ponto de acesso do circuito nao depende do radio: ele e
        // parte do exercicio.
        rede.DefinirPontosDeAcesso([new PontoDeAcesso("AulaDeRedes", "12345678")]);
        Conferir("radio: o ponto de acesso do circuito aparece sempre",
            rede.RedesVisiveis().Any(r => r.Ssid == "AulaDeRedes"),
            string.Join(", ", rede.RedesVisiveis().Select(r => r.Ssid)));
    }


    // ---------------- a area de clique gira com a peca ----------------
    /// <summary>
    /// O corpo da peca responde ao clique no lugar em que ela ESTA.
    ///
    /// Item 8 da lista: "a peca inteira roda, os pontos e pinos, mas a
    /// tela em si continua no mesmo lugar". Ele pediu para conferir peca
    /// por peca -- e o teste faz isso, varrendo o catalogo inteiro, mas a
    /// correcao foi uma so: o teste do corpo passou a desfazer o giro
    /// antes de comparar, em vez de usar um retangulo alinhado aos eixos.
    ///
    /// A conferencia e geometrica e nao depende de janela: para cada
    /// peca, um ponto que esta DENTRO do corpo girado tem que ser aceito,
    /// e um que esta fora tem que ser recusado.
    /// </summary>
    static void Clique_GiraComAPeca()
    {
        saida.AppendLine("a area de clique gira com a peca");

        // ---- 1) a volta ao espaco da peca desfaz a ida ----
        var provas = new (double Giro, bool Espelho)[]
        {
            (0, false), (90, false), (180, false), (270, false),
            (45, false), (30, true), (90, true),
        };

        var pecaDeProva = new PecaNoCircuito { Tipo = "resistor", X = 137, Y = 89 };
        var defDeProva = Catalogo.Obter("resistor");
        double pior = 0;

        foreach (var (giro, espelho) in provas)
        {
            pecaDeProva.Rotacao = giro;
            pecaDeProva.Espelhada = espelho;

            foreach (var (lx, ly) in new[]
                     {
                         (0.0, 0.0), (defDeProva.Largura, 0.0),
                         (defDeProva.Largura, defDeProva.Altura),
                         (defDeProva.Largura / 2, defDeProva.Altura / 2),
                     })
            {
                var naTela = Geometria.PosicaoLocal(pecaDeProva, defDeProva, lx, ly);
                var devolta = Geometria.PontoLocal(pecaDeProva, defDeProva, naTela);
                pior = Math.Max(pior, Math.Abs(devolta.X - lx));
                pior = Math.Max(pior, Math.Abs(devolta.Y - ly));
            }
        }

        saida.AppendLine($"        ida e volta: erro maximo de {pior:0.0000} px");
        Conferir("giro: PontoLocal desfaz exatamente o PosicaoLocal",
            pior < 0.0001, pior.ToString("0.0000"));

        pecaDeProva.Rotacao = 0;
        pecaDeProva.Espelhada = false;

        // ---- 2) peca por peca, em pe e deitada ----
        var editor = new EditorCircuito();
        int conferidas = 0, provadas = 0, erradas = 0;
        var falhas = new List<string>();

        foreach (var def in Catalogo.Todas)
        {
            // O Texto nao gira: ele e um rotulo que fica sempre em pe.
            if (def.Id == "texto") continue;

            foreach (double giro in new double[] { 0, 90, 180, 270 })
            {
                var c = new Circuito();
                var peca = new PecaNoCircuito
                {
                    Id = "alvo", Tipo = def.Id, X = 400, Y = 300, Rotacao = giro,
                };
                c.Pecas.Add(peca);
                editor.Carregar(c);

                // O MEIO da peca cai dentro dela em qualquer angulo: e o
                // ponto que o giro nao mexe.
                var meio = Geometria.PosicaoLocal(peca, def, def.Largura / 2, def.Altura / 2);

                conferidas++;

                if (editor.Resolver(meio).Peca?.Id != "alvo")
                {
                    erradas++;
                    if (falhas.Count < 6) falhas.Add($"{def.Id} a {giro} graus perdeu o meio");
                    continue;
                }

                // Agora o ponto que SO seria aceito se o giro fosse
                // ignorado.
                //
                // De pe, a peca se estende na LARGURA. Deitada a 90 ou
                // 270, essa mesma largura passa a ser vertical -- entao
                // um ponto afastado na horizontal, na distancia da
                // largura, esta fora dela. Era exatamente ai que a caixa
                // velha, alinhada aos eixos, continuava aceitando.
                //
                // A 0 e a 180 o retangulo ocupa o mesmo lugar, e nenhum
                // ponto distingue as duas contas: esses angulos provam
                // so o meio, e esta certo assim.
                if (giro is not (90 or 270)) continue;

                // Quanto a peca ocupa na horizontal DEPOIS de deitada:
                // e a altura dela, mais a folga do alvo minimo (20 px),
                // que e medida no eixo curto.
                double metadeNaTela = def.Altura / 2 + Math.Max(0, (20 - def.Altura) / 2);
                double sonda = def.Largura / 2 - 4;

                // Peca quadrada, ou quase: o ponto cairia dentro nos dois
                // casos e nao provaria nada.
                if (sonda <= metadeNaTela + 1) continue;

                provadas++;
                var soSemGiro = new System.Windows.Point(meio.X + sonda, meio.Y);
                var la = editor.Resolver(soSemGiro);

                // O que nao pode e o CORPO responder ali. Um PINO pode:
                // os pinos giram junto e ficam na borda, entao um ponto
                // logo fora do corpo cai no raio de um deles -- e clicar
                // num pino para puxar fio e o comportamento certo.
                if (la.Peca?.Id != "alvo"
                    || la.Camada != EditorCircuito.Camada.CorpoPeca) continue;

                erradas++;
                if (falhas.Count < 6)
                    falhas.Add($"{def.Id} a {giro} graus aceitou clique fora do corpo");
            }
        }

        saida.AppendLine($"        {conferidas} combinacoes de peca e angulo, "
                         + $"{provadas} com peca comprida deitada, {erradas} erradas");
        if (falhas.Count > 0) saida.AppendLine("        " + string.Join(", ", falhas));

        Conferir("giro: o catalogo inteiro foi conferido", conferidas >= 200,
            conferidas.ToString());
        Conferir("giro: e ha peca comprida bastante para o teste valer",
            provadas >= 20, provadas.ToString());
        Conferir("giro: toda peca responde no meio e recusa onde ela nao esta mais",
            erradas == 0, string.Join(", ", falhas));

        // ---- 3) o caso que ele viu: a peca comprida deitada ----
        // Um resistor tem 11 px de altura e ~60 de largura. Deitado a
        // 90 graus, o ponto 40 px ACIMA do centro passa a estar dentro
        // dele -- e antes da correcao esse clique nao pegava nada.
        {
            var c = new Circuito();
            var resistor = new PecaNoCircuito
            {
                Id = "r1", Tipo = "resistor", X = 500, Y = 400, Rotacao = 90,
            };
            c.Pecas.Add(resistor);
            editor.Carregar(c);

            var defR = Catalogo.Obter("resistor");
            var centro = Geometria.PosicaoLocal(resistor, defR, defR.Largura / 2, defR.Altura / 2);

            // Ao longo do corpo, que depois do giro aponta para baixo.
            var noCorpo = new System.Windows.Point(centro.X, centro.Y + defR.Largura / 2 - 4);

            // Ao lado, onde ele SO estaria se continuasse em pe.
            var aoLado = new System.Windows.Point(centro.X + defR.Largura / 2 - 4, centro.Y);

            var pegou = editor.Resolver(noCorpo);
            var naoPegou = editor.Resolver(aoLado);

            saida.AppendLine($"        resistor deitado: ao longo do corpo = "
                             + $"{pegou.Peca?.Id ?? "nada"}, ao lado = "
                             + $"{naoPegou.Peca?.Id ?? "nada"}");

            Conferir("giro: o resistor deitado pega o clique ao longo do corpo",
                pegou.Peca?.Id == "r1", pegou.Peca?.Id ?? "nada");
            Conferir("giro: e nao pega mais onde ele estaria em pe",
                naoPegou.Peca?.Id != "r1", "continuou pegando");
        }
    }


    /// <summary>
    /// A fileira que sobra debaixo da placa continua alcancavel.
    ///
    /// Item 9 da lista: "Coloquei a ESP na breadboard 800, so uma
    /// fileira ficou acessivel, mas eu nao consigo colocar o fio na
    /// fileira que ficou disponivel". A causa era o tamanho do desenho
    /// da ESP32 -- 107 x 204 px, cobrindo a protoboard de ponta a ponta
    /// na vertical --, e o corpo dela ganhava do furo em 142 dos 830
    /// pontos.
    ///
    /// O teste conta os furos alcancaveis e exige que a placa continue
    /// sendo arrastavel, que e o outro lado da mesma moeda: se o furo
    /// ganhasse em toda parte, ninguem mais moveria a ESP32.
    /// </summary>
    static void Breadboard_FuroDebaixoDaPlaca()
    {
        saida.AppendLine("furo debaixo da placa");

        var c = new Circuito();
        var protoboard = new PecaNoCircuito
        {
            Id = "bb", Tipo = Catalogo.IdProtoboard830, X = 0, Y = 0,
        };
        c.Pecas.Add(protoboard);

        var esp = new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        c.Pecas.Add(esp);

        var defBb = Catalogo.Obter(Catalogo.IdProtoboard830);
        var defEsp = Catalogo.Obter("esp32-devkit-v1");

        // Encaixa como o editor faz ao soltar a peca sobre a placa.
        var ajuste = Geometria.AjusteParaFuro(esp, defEsp, c, 12);
        if (ajuste is not null) { esp.X += ajuste.Value.X; esp.Y += ajuste.Value.Y; }

        var ed = new EditorCircuito();
        ed.Carregar(c);

        int alcancaveis = 0, doPinoDaEsp = 0, perdidos = 0;
        var exemplos = new List<string>();

        foreach (var furo in defBb.Pinos)
        {
            var p = Geometria.PosicaoLocal(protoboard, defBb, furo.X, furo.Y);
            var a = ed.Resolver(p);

            if (a.Camada == EditorCircuito.Camada.Furo) alcancaveis++;

            // Furo ocupado por pino da ESP32 responde como PINO, e esta
            // certo: e nele que o fio tem que se ligar.
            else if (a.Camada is EditorCircuito.Camada.PinoPeca
                     or EditorCircuito.Camada.BotaoPlaca) doPinoDaEsp++;

            else
            {
                perdidos++;
                if (exemplos.Count < 6) exemplos.Add($"{furo.Nome}={a.Camada}");
            }
        }

        saida.AppendLine($"        {defBb.Pinos.Count} furos: {alcancaveis} livres para fio, "
                         + $"{doPinoDaEsp} ocupados por pino da placa, {perdidos} perdidos");
        if (exemplos.Count > 0) saida.AppendLine("        " + string.Join(", ", exemplos));

        Conferir("breadboard: nenhum furo fica sem alcance",
            perdidos == 0, string.Join(", ", exemplos));

        // Antes da correcao eram 688; o resto ia para o corpo da ESP32.
        Conferir("breadboard: a fileira debaixo da placa da para usar",
            alcancaveis >= 800, alcancaveis.ToString());

        // E o outro lado: a placa tem que continuar arrastavel. Ela
        // atravessa o canal do meio da protoboard, que nao tem furo
        // nenhum -- e e por ali que se pega um chip na bancada.
        var meioDaEsp = Geometria.PosicaoLocal(esp, defEsp, defEsp.Largura / 2, defEsp.Altura / 2);
        var noMeio = ed.Resolver(meioDaEsp);
        saida.AppendLine($"        meio da ESP32: {noMeio.Camada}");

        Conferir("breadboard: a placa continua podendo ser arrastada pelo meio",
            noMeio.Camada == EditorCircuito.Camada.CorpoPeca && noMeio.Peca?.Id == "esp",
            noMeio.Camada.ToString());

        // Peca pequena nao muda: o corpo dela continua ganhando do furo.
        var solto = new Circuito();
        var bb2 = new PecaNoCircuito { Id = "bb2", Tipo = Catalogo.IdProtoboard830, X = 0, Y = 0 };
        solto.Pecas.Add(bb2);
        var r = new PecaNoCircuito { Id = "r1", Tipo = "resistor", X = 120, Y = 60 };
        solto.Pecas.Add(r);

        var ed2 = new EditorCircuito();
        ed2.Carregar(solto);

        var defR = Catalogo.Obter("resistor");
        var meioDoResistor = Geometria.PosicaoLocal(r, defR, defR.Largura / 2, defR.Altura / 2);
        var noResistor = ed2.Resolver(meioDoResistor);

        saida.AppendLine($"        meio do resistor sobre a placa: {noResistor.Camada}");
        Conferir("breadboard: o resistor sobre a placa continua pegando o clique",
            noResistor.Camada == EditorCircuito.Camada.CorpoPeca,
            noResistor.Camada.ToString());
    }


    /// <summary>
    /// A lista de componentes, no formato que o professor desenhou.
    ///
    /// Ele escreveu o exemplo na mao, e este teste monta exatamente
    /// aquele circuito: duas protoboards, uma ESP32 espetada numa delas
    /// e um MPU ligado por quatro fios -- mais uma peca deixada de fora,
    /// que a lista tem que denunciar.
    /// </summary>
    static void Componentes_EmArvore()
    {
        saida.AppendLine("componentes em arvore");

        var c = new Circuito();

        var bb = new PecaNoCircuito
        {
            Id = "bb1", Tipo = Catalogo.IdProtoboard170, X = 0, Y = 0,
        };
        c.Pecas.Add(bb);

        // Deitada, como se espeta de verdade: cada pino numa tira da protoboard.
        // Em pe, cinco pinos seguidos cairiam na mesma tira (o D21 e o D22 em
        // curto) -- desde que os pinos ficaram no passo exato de 2,54 mm.
        var esp = new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 0, Y = 0, Rotacao = 90 };
        c.Pecas.Add(esp);

        var defEsp = Catalogo.Obter("esp32-devkit-v1");
        var ajuste = Geometria.AjusteParaFuro(esp, defEsp, c, 12);
        if (ajuste is not null) { esp.X += ajuste.Value.X; esp.Y += ajuste.Value.Y; }

        // O MPU fica FORA da protoboard, ligado so por fio -- e o caso
        // que ele marcou com asterisco no desenho dele.
        var mpu = new PecaNoCircuito { Id = "mpu", Tipo = "mpu6050", X = 900, Y = 500 };
        c.Pecas.Add(mpu);

        foreach (var (daPlaca, daPeca) in new[]
                 { ("GND.1", "GND"), ("3V3", "VCC"), ("D22", "SCL"), ("D21", "SDA") })
            c.Fios.Add(new Fio
            {
                DePeca = "esp", DePino = daPlaca,
                ParaPeca = "mpu", ParaPino = daPeca,
            });

        var rede = new Netlist(c);
        var arvore = ArvoreDeComponentes.Montar(c, rede);
        string texto = ArvoreDeComponentes.EmTexto(arvore);

        saida.AppendLine("        --- a arvore ---");
        foreach (var linha in texto.Replace(Environment.NewLine, "\n").Split('\n'))
            if (linha.Trim().Length > 0) saida.AppendLine("        " + linha);

        Conferir("componentes: a protoboard e a raiz",
            texto.Contains("[Protoboard 170]"), texto);

        Conferir("componentes: a placa aparece dentro dela",
            texto.Contains("ESP32"), texto);

        Conferir("componentes: o que esta ligado na placa fica pendurado nela",
            texto.Contains("> MPU6050") || texto.Contains("> Acelerometro MPU6050"), texto);

        Conferir("componentes: os pinos saem par a par",
            texto.Contains("3V3 > VCC") && texto.Contains("D21 > SDA"), texto);

        // O MPU esta ligado, entao entra pendurado na ESP32 -- e nao na
        // lista dos soltos. O aviso de "fora da protoboard" e para quem
        // nao esta ligado em nada.
        var solta = new PecaNoCircuito { Id = "led1", Tipo = "led", X = 1200, Y = 700 };
        c.Pecas.Add(solta);

        string comSolta = ArvoreDeComponentes.EmTexto(
            ArvoreDeComponentes.Montar(c, new Netlist(c)));

        saida.AppendLine("        --- com uma peca solta ---");
        foreach (var linha in comSolta.Replace(Environment.NewLine, "\n").Split('\n'))
            if (linha.Trim().Length > 0) saida.AppendLine("        " + linha);

        Conferir("componentes: a peca solta e denunciada",
            comSolta.Contains("fora da protoboard e sem ligação"), comSolta);

        // A protoboard lista o que esta espetado nela e diz em que placa
        // cada um esta ligado -- e o indice do que ha em cima dela.
        Conferir("componentes: o que esta espetado na protoboard aparece nela",
            comSolta.Contains("espetadas"), comSolta);

        // O pino mostrado e o que o FIO usa, e nao o primeiro homonimo:
        // D22 e RX0 sao o mesmo GPIO, e quem ligou o fio ligou em D22.
        Conferir("componentes: o pino mostrado e o que o fio usa",
            texto.Contains("D22 > SCL"), texto);

        // Duas protoboards do mesmo tipo viram um grupo, com a contagem.
        c.Pecas.Add(new PecaNoCircuito
        {
            Id = "bb2", Tipo = Catalogo.IdProtoboard170, X = 800, Y = 0,
        });

        string comDuas = ArvoreDeComponentes.EmTexto(
            ArvoreDeComponentes.Montar(c, new Netlist(c)));

        saida.AppendLine($"        com duas: {comDuas.Split('\n')[0].Trim()}");
        Conferir("componentes: duas protoboards iguais viram [Protoboard 170 x2]",
            comDuas.Contains("[Protoboard 170 x2]"), comDuas);

        // Circuito vazio nao pode estourar.
        Conferir("componentes: circuito vazio da lista vazia",
            ArvoreDeComponentes.Montar(new Circuito(), null).Count == 0);
    }

    /// <summary>
    /// As subabas da paleta: uma por familia de microcontrolador.
    ///
    /// Alpha 18: "Uma subaba na aba de novo componente, com cada
    /// microcontrolador e seus filhos. Arduino com Mega, Uno, etc."
    ///
    /// O teste bate na montagem pura, e nao na janela: janela aberta
    /// num autoteste prende o processo. O que se confere e o que de
    /// fato pode quebrar -- se cada placa cai na familia certa, e se
    /// alguma some pelo caminho.
    /// </summary>
    static void Paleta_PorFamilia()
    {
        saida.AppendLine("paleta por familia");

        var abas = JanelaPecas.Subabas();
        saida.AppendLine("        abas: " + string.Join(" | ", abas));

        Conferir("paleta: a primeira aba e a de tudo",
            abas.Count > 0 && abas[0] == JanelaPecas.AbaTudo, string.Join(",", abas));

        Conferir("paleta: a ultima aba e a dos componentes",
            abas.Count > 0 && abas[^1] == JanelaPecas.AbaComponentes, string.Join(",", abas));

        foreach (string familia in new[] { "Arduino", "ESP32", "ESP8266", "Raspberry Pi", "STM32" })
            Conferir($"paleta: existe a aba {familia}", abas.Contains(familia),
                     string.Join(",", abas));

        // ---- cada placa cai na familia certa ----
        foreach (var (id, familia) in new[]
                 {
                     ("arduino-uno", "Arduino"),
                     ("arduino-mega", "Arduino"),
                     ("arduino-nano", "Arduino"),

                     // Chip da Raspberry, marca da Arduino. Quem procura
                     // por ela procura no menu da Arduino.
                     ("nano-rp2040-connect", "Arduino"),

                     ("esp32-devkit-v1", "ESP32"),
                     ("pi-pico", "Raspberry Pi"),
                     ("stm32-bluepill", "STM32"),

                     // O Franzininho e o ATtiny85 dentro dele ficam no
                     // menu da Arduino: nao sao placas da marca, mas e
                     // com a IDE dela que se programa os dois, e foi
                     // assim que o pedido da Alpha 18 os listou.
                     ("franzininho", "Arduino"),
                     ("attiny85", "Arduino"),

                     ("esp8266-nodemcu", "ESP8266"),
                     ("esp-12f", "ESP8266"),
                     ("esp-01", "ESP8266"),
                     ("esp32-c3-devkit", "ESP32"),
                     ("esp32-s3-devkit", "ESP32"),
                     ("pi-pico-w", "Raspberry Pi"),
                     ("st-nucleo-c031c6", "STM32"),
                 })
            Conferir($"paleta: {id} fica em {familia}",
                     Catalogo.Familia(id) == familia,
                     Catalogo.Familia(id) ?? "(nula)");

        Conferir("paleta: peca que nao e placa nao tem familia",
            Catalogo.Familia("led") is null && Catalogo.Familia("resistor") is null);

        // ---- a aba de uma familia so tem as placas dela ----
        var arduino = JanelaPecas.Montar("Arduino", "");
        var nomes = arduino.Where(i => i.Peca is not null).Select(i => i.Peca.Nome).ToList();

        saida.AppendLine("        aba Arduino: " + string.Join(", ", nomes));

        Conferir("paleta: a aba Arduino traz o Uno e o Mega",
            nomes.Any(n => n.Contains("Uno")) && nomes.Any(n => n.Contains("Mega")),
            string.Join(",", nomes));

        Conferir("paleta: a aba Arduino nao traz placa de outra familia",
            arduino.All(i => i.Peca is null || Catalogo.Familia(i.Peca.Id) == "Arduino"));

        Conferir("paleta: a aba de familia abre com o cabecalho dela",
            arduino.Count > 0 && arduino[0].Cabecalho == "ARDUINO",
            arduino.Count > 0 ? arduino[0].Cabecalho ?? "(peca)" : "(vazia)");

        // ---- a aba Tudo separa as placas por familia, sem perder nenhuma ----
        var tudo = JanelaPecas.Montar(JanelaPecas.AbaTudo, "");
        var cabecalhos = tudo.Where(i => i.Cabecalho is not null).Select(i => i.Cabecalho).ToList();

        saida.AppendLine("        cabecalhos: " + string.Join(" / ", cabecalhos));

        Conferir("paleta: a aba Tudo tem a categoria PLACAS",
            cabecalhos.Contains("PLACAS"), string.Join(",", cabecalhos));

        Conferir("paleta: e dentro dela as familias",
            cabecalhos.Contains("ARDUINO") && cabecalhos.Contains("ESP32")
            && cabecalhos.Contains("ESP8266"),
            string.Join(",", cabecalhos));

        Conferir("paleta: a familia fica recuada em relacao a categoria",
            tudo.Any(i => i.Cabecalho == "PLACAS" && i.Nivel == 0)
            && tudo.Any(i => i.Cabecalho == "ARDUINO" && i.Nivel == 1));

        Conferir("paleta: a aba Tudo nao perde nenhuma peca",
            tudo.Count(i => i.Peca is not null) == Catalogo.Todas.Count,
            $"{tudo.Count(i => i.Peca is not null)} de {Catalogo.Todas.Count}");

        // ---- Tudo = Componentes + as familias, sem sobra nem repeticao ----
        int soltas = JanelaPecas.Montar(JanelaPecas.AbaComponentes, "").Count(i => i.Peca is not null);
        int placas = abas.Where(a => a != JanelaPecas.AbaTudo && a != JanelaPecas.AbaComponentes)
                         .Sum(a => JanelaPecas.Montar(a, "").Count(i => i.Peca is not null));

        saida.AppendLine($"        {soltas} componentes + {placas} placas = {Catalogo.Todas.Count}");

        Conferir("paleta: as abas cobrem o catalogo inteiro, uma vez cada",
            soltas + placas == Catalogo.Todas.Count,
            $"{soltas}+{placas} != {Catalogo.Todas.Count}");

        Conferir("paleta: a aba Componentes nao traz placa nenhuma",
            JanelaPecas.Montar(JanelaPecas.AbaComponentes, "")
                       .All(i => i.Peca is null || Catalogo.Familia(i.Peca.Id) is null));

        // ---- a busca vale dentro da aba ----
        var busca = JanelaPecas.Montar("Arduino", "mega");
        Conferir("paleta: a busca filtra dentro da aba",
            busca.Count(i => i.Peca is not null) == 1
            && busca.First(i => i.Peca is not null).Peca.Id == "arduino-mega");

        Conferir("paleta: busca sem resultado na aba devolve lista vazia",
            JanelaPecas.Montar("Arduino", "esp32-devkit").Count == 0);

        // Cabecalho orfao e o erro classico de lista agrupada: a
        // categoria aparece e nao sobra nada embaixo dela.
        var comFiltro = JanelaPecas.Montar(JanelaPecas.AbaTudo, "led");
        bool orfao = false;
        for (int i = 0; i < comFiltro.Count; i++)
            if (comFiltro[i].Cabecalho is not null
                && (i + 1 >= comFiltro.Count || comFiltro[i + 1].Cabecalho is not null
                    && comFiltro[i + 1].Nivel <= comFiltro[i].Nivel))
                orfao = true;

        Conferir("paleta: filtrando, nenhum cabecalho fica sem pecas", !orfao);
    }

    /// <summary>
    /// Soltar um painel numa janela e traze-lo de volta.
    ///
    /// Item 6.1 da lista. O risco aqui nao e abrir a janela -- e o
    /// painel NAO VOLTAR: o WPF so deixa um elemento ter um pai, entao
    /// um retorno malfeito deixa um buraco na janela principal, ou
    /// estoura com "ja tem pai" na segunda vez.
    /// </summary>
    static void Paineis_EmJanelaPropria()
    {
        saida.AppendLine("paineis em janela propria");

        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "p", Tipo = "esp32-devkit-v1", X = 40, Y = 40 });

        var janela = new MainWindow();
        janela.TesteAbrir(c, "void setup() {}" + Environment.NewLine + "void loop() {}");

        foreach (string qual in new[] { "codigo", "baixo", "componentes" })
        {
            bool antes = janela.TestePainelEstaEmCasa(qual);

            janela.TesteSoltarPainel(qual);
            bool solto = janela.TestePainelEstaEmCasa(qual);

            janela.TesteSoltarPainel(qual);
            bool voltou = janela.TestePainelEstaEmCasa(qual);

            saida.AppendLine($"        {qual}: em casa {antes} -> solto {!solto} -> voltou {voltou}");

            Conferir($"janela solta: o painel '{qual}' comeca dentro da janela", antes);
            Conferir($"janela solta: '{qual}' sai da janela principal", !solto);
            Conferir($"janela solta: '{qual}' volta para o lugar", voltou);
        }

        // Soltar e devolver duas vezes seguidas: e onde o "ja tem pai"
        // apareceria.
        janela.TesteSoltarPainel("codigo");
        janela.TesteSoltarPainel("codigo");
        janela.TesteSoltarPainel("codigo");
        janela.TesteSoltarPainel("codigo");

        Conferir("janela solta: soltar e devolver duas vezes nao quebra",
            janela.TestePainelEstaEmCasa("codigo"));

        // E o editor continua sendo o MESMO: o texto sobrevive a viagem.
        janela.TesteDigitar("// sobreviveu");
        janela.TesteSoltarPainel("codigo");
        string noSolto = janela.TesteCodigo;
        janela.TesteSoltarPainel("codigo");

        saida.AppendLine($"        codigo depois da viagem: {janela.TesteCodigo}");
        Conferir("janela solta: o codigo nao e copiado, e o mesmo editor",
            noSolto == "// sobreviveu" && janela.TesteCodigo == "// sobreviveu",
            janela.TesteCodigo);

        // ---- o vao que ficava para tras ----
        //
        // Alpha 18: "ao separar o codigo e o monitor serial, fica um vao
        // que nao da pra controlar no aplicativo". Tirar o painel do
        // Grid nao tira a LINHA dele, entao sobrava o buraco com a
        // altura de antes -- e o MinHeight sozinho ja segurava 120 px.
        const int LinhaDoCodigo = 0;
        const int LinhaDaDivisoria = 3;
        const int LinhaDeBaixo = 4;

        double ladoInteiro = janela.TesteLarguraDoLado;

        janela.TesteSoltarPainel("codigo");
        saida.AppendLine($"        so o codigo fora: linha do codigo = "
                         + $"{janela.TesteAlturaDaLinha(LinhaDoCodigo)}");

        Conferir("vao: a linha do codigo solto nao ocupa espaco",
            janela.TesteAlturaDaLinha(LinhaDoCodigo) == 0,
            janela.TesteAlturaDaLinha(LinhaDoCodigo).ToString());

        Conferir("vao: a divisoria some junto, nao ha mais o que dividir",
            janela.TesteAlturaDaLinha(LinhaDaDivisoria) == 0);

        Conferir("vao: quem ficou herda a coluna",
            janela.TesteAlturaDaLinha(LinhaDeBaixo) > 0);

        janela.TesteSoltarPainel("baixo");
        saida.AppendLine($"        os dois fora: lado = {janela.TesteLarguraDoLado}");

        Conferir("vao: com os dois fora a coluna da direita fecha",
            janela.TesteLarguraDoLado == 0, janela.TesteLarguraDoLado.ToString());

        // O aviso morava na coluna que acabou de fechar. Ele sobe para
        // a barra de cima, senao um erro de compilacao sumiria junto.
        janela.TesteAvisar("faltou ponto e virgula");
        saida.AppendLine($"        aviso na barra: {janela.TesteAvisoNaBarra}");

        Conferir("vao: com a coluna fechada o aviso aparece na barra de cima",
            janela.TesteAvisoNaBarra == "faltou ponto e virgula",
            janela.TesteAvisoNaBarra);

        // ---- e tudo volta ao lugar ----
        janela.TesteSoltarPainel("codigo");
        janela.TesteSoltarPainel("baixo");

        saida.AppendLine($"        de volta: lado = {janela.TesteLarguraDoLado}"
                         + $" (era {ladoInteiro})");

        Conferir("vao: devolvendo os dois, a coluna volta ao tamanho de antes",
            janela.TesteLarguraDoLado == ladoInteiro,
            $"{janela.TesteLarguraDoLado} != {ladoInteiro}");

        Conferir("vao: a divisoria volta",
            janela.TesteAlturaDaLinha(LinhaDaDivisoria) == 6,
            janela.TesteAlturaDaLinha(LinhaDaDivisoria).ToString());

        Conferir("vao: as duas linhas voltam a ter altura",
            janela.TesteAlturaDaLinha(LinhaDoCodigo) > 0
            && janela.TesteAlturaDaLinha(LinhaDeBaixo) > 0);

        Conferir("vao: com a coluna aberta o aviso sai da barra de cima",
            janela.TesteAvisoNaBarra == "", janela.TesteAvisoNaBarra);

        // Soltar o de baixo primeiro e o caminho inverso, e tem que dar
        // no mesmo: a ordem nao pode deixar uma das linhas em zero.
        janela.TesteSoltarPainel("baixo");
        Conferir("vao: soltando o de baixo primeiro, o codigo herda a coluna",
            janela.TesteAlturaDaLinha(LinhaDeBaixo) == 0
            && janela.TesteAlturaDaLinha(LinhaDoCodigo) > 0);

        janela.TesteSoltarPainel("baixo");
        Conferir("vao: e volta inteiro",
            janela.TesteLarguraDoLado == ladoInteiro
            && janela.TesteAlturaDaLinha(LinhaDeBaixo) > 0);
    }


    /// <summary>
    /// Soltar o serial, a rede e o Bluetooth uma aba de cada vez.
    ///
    /// Alpha 18: "adicionar capacidade de separar serial, bluetooth e
    /// rede, assim posso ter, por exemplo, o bluetooth acoplado e o
    /// serial fora".
    ///
    /// O que pode dar errado nao e a janela: e a VISIBILIDADE. As tres
    /// abas sao irmas no mesmo painel e so uma aparece por vez, entao
    /// soltar a que estava escondida abriria uma janela em branco, e
    /// devolver uma aba podia deixar duas acesas ao mesmo tempo.
    /// </summary>
    static readonly string[] TodasAsAbasDeBaixo = ["console", "serial", "rede", "bluetooth"];

    static void Abas_SoltasUmaAUma()
    {
        saida.AppendLine("abas soltas uma a uma");

        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "p", Tipo = "esp32-devkit-v1", X = 40, Y = 40 });

        var janela = new MainWindow();
        janela.TesteAbrir(c, "void setup() {}" + Environment.NewLine + "void loop() {}");
        janela.TesteMostrarAba("serial");

        // ---- o caso que o pedido descreve: bluetooth aqui, serial fora ----
        janela.TesteSoltarAba("serial");

        bool serialAVista = janela.TesteAbaAVista("serial");
        saida.AppendLine($"        serial fora | aba aqui: {janela.TesteAbaVisivel}"
                         + $" | serial a vista: {serialAVista}");

        Conferir("abas: o monitor serial sai sozinho",
            !janela.TestePainelEstaEmCasa("serial"));

        Conferir("abas: e continua a vista, agora na janela dele",
            janela.TesteAbaAVista("serial"));

        Conferir("abas: o painel de baixo continua em casa",
            janela.TestePainelEstaEmCasa("baixo"));

        // A aba escolhida era o serial, que saiu. O painel nao pode
        // ficar vazio: ele passa a mostrar uma das que ficaram.
        Conferir("abas: a tira passa a mostrar uma aba que ficou",
            janela.TesteAbaVisivel != "serial", janela.TesteAbaVisivel);

        Conferir("abas: e so essa aparece aqui dentro",
            janela.TesteAbaAVista(janela.TesteAbaVisivel));

        janela.TesteMostrarAba("bluetooth");
        Conferir("abas: com o serial fora, da para usar o bluetooth aqui",
            janela.TesteAbaAVista("bluetooth") && janela.TestePainelEstaEmCasa("bluetooth"));

        Conferir("abas: e o serial solto nao apaga junto",
            janela.TesteAbaAVista("serial"));

        // ---- de volta ----
        janela.TesteSoltarAba("serial");
        saida.AppendLine($"        serial de volta | aba aqui: {janela.TesteAbaVisivel}");

        Conferir("abas: o serial volta para o painel",
            janela.TestePainelEstaEmCasa("serial"));

        // A aba que volta vira a aba da vez: quem pediu o serial de
        // volta quer olhar para ele. Antes ele voltava escondido atras
        // do bluetooth, e parecia que nao tinha voltado.
        Conferir("abas: a aba que volta passa a ser a que aparece",
            janela.TesteAbaVisivel == "serial"
            && janela.TesteAbaAVista("serial") && !janela.TesteAbaAVista("bluetooth"),
            janela.TesteAbaVisivel);

        // ---- as tres fora: sobra a tira, e a linha encolhe ate ela ----
        const int LinhaDeBaixo = 4;
        const int LinhaDaDivisoria = 3;

        foreach (string aba in TodasAsAbasDeBaixo)
            janela.TesteSoltarAba(aba);

        saida.AppendLine($"        as tres fora: linha de baixo ao conteudo = "
                         + $"{janela.TesteLinhaAoConteudo(LinhaDeBaixo)}"
                         + $" | lado = {janela.TesteLarguraDoLado}");

        // Nao e altura zero: a tira continua ai, e e por ela que as
        // abas voltam. O que a linha nao pode mais e RESERVAR os 232 px
        // do painel que nao esta mais dentro dela.
        Conferir("abas: com as tres fora, a linha de baixo encolhe ate a tira",
            janela.TesteLinhaAoConteudo(LinhaDeBaixo));

        Conferir("abas: e a divisoria some, nao ha mais o que dividir",
            janela.TesteAlturaDaLinha(LinhaDaDivisoria) == 0);

        // A tira e o caminho de volta, entao a coluna NAO fecha aqui.
        Conferir("abas: a coluna fica, porque a tira precisa continuar a mao",
            janela.TesteLarguraDoLado > 0, janela.TesteLarguraDoLado.ToString());

        // Com o codigo fora tambem nao sobra nada: ai sim a coluna fecha.
        janela.TesteSoltarPainel("codigo");
        saida.AppendLine($"        e o codigo fora: lado = {janela.TesteLarguraDoLado}");

        Conferir("abas: com o codigo fora tambem, a coluna fecha",
            janela.TesteLarguraDoLado == 0, janela.TesteLarguraDoLado.ToString());

        // ---- tudo de volta ----
        janela.TesteSoltarPainel("codigo");
        foreach (string aba in TodasAsAbasDeBaixo)
            janela.TesteSoltarAba(aba);

        Conferir("abas: as tres voltam para o painel",
            TodasAsAbasDeBaixo.All(janela.TestePainelEstaEmCasa));

        Conferir("abas: e de volta so uma aparece por vez",
            TodasAsAbasDeBaixo.Count(janela.TesteAbaAVista) == 1,
            janela.TesteAbaVisivel);

        Conferir("abas: a linha de baixo recupera a altura",
            janela.TesteAlturaDaLinha(LinhaDeBaixo) > 0);

        Conferir("abas: e a divisoria volta",
            janela.TesteAlturaDaLinha(LinhaDaDivisoria) == 6);

        // ---- soltar e devolver duas vezes: o "ja tem pai" do WPF ----
        for (int i = 0; i < 4; i++) janela.TesteSoltarAba("rede");

        Conferir("abas: soltar e devolver a mesma aba duas vezes nao quebra",
            janela.TestePainelEstaEmCasa("rede"));

        // ---- arrastar de volta ----
        //
        // Alpha 18: "quando eu arrastar uma janela, posso reacoplar ela
        // sem precisar fechar". O arrasto em si e o laco de movimento do
        // Windows, que nao existe num autoteste. O que da para conferir
        // -- e o que erraria calado -- e a conta que decide se o
        // ponteiro esta OU NAO por cima da janela principal.
        var canto = new System.Windows.Point(100, 50);
        var fim = new System.Windows.Point(900, 650);

        Conferir("arrastar: o ponto no meio da principal reacopla",
            JanelaSolta.Dentro(canto, fim, new System.Windows.Point(500, 300)));

        Conferir("arrastar: em cima da borda ainda conta",
            JanelaSolta.Dentro(canto, fim, canto)
            && JanelaSolta.Dentro(canto, fim, fim));

        Conferir("arrastar: um pixel fora nao reacopla",
            !JanelaSolta.Dentro(canto, fim, new System.Windows.Point(99, 300))
            && !JanelaSolta.Dentro(canto, fim, new System.Windows.Point(500, 651)));

        Conferir("arrastar: soltar longe da principal nao reacopla nada",
            !JanelaSolta.Dentro(canto, fim, new System.Windows.Point(1800, 900)));

        // Monitor a esquerda do principal: as coordenadas ficam
        // negativas e o canto passa a ser maior do que o fim.
        Conferir("arrastar: funciona com a principal num monitor de coordenada negativa",
            JanelaSolta.Dentro(new System.Windows.Point(-1200, 0), new System.Windows.Point(-400, 600),
                               new System.Windows.Point(-800, 300)));
    }

    /// <summary>
    /// O console: o que o SIMULADOR diz, separado do que o sketch imprime.
    ///
    /// Alpha 18: "adicionar CONSOLE do lado esquerdo do monitor serial.
    /// O aplicativo ainda vai abrir com o monitor serial como principal,
    /// mas vai ter o console também."
    ///
    /// O ganho de verdade nao e a aba: e a linha de status parar de
    /// perder o passado. Ela guarda UMA mensagem, e depois de um erro o
    /// que costuma interessar e a mensagem anterior a ele.
    /// </summary>
    static void Console_DoSimulador()
    {
        saida.AppendLine("console do simulador");

        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "p", Tipo = "esp32-devkit-v1", X = 40, Y = 40 });

        var janela = new MainWindow();
        janela.TesteAbrir(c, "void setup() {}" + Environment.NewLine + "void loop() {}");

        // O pedido e explicito: abre no monitor serial, e nao no console.
        Conferir("console: o programa abre no monitor serial",
            janela.TesteAbaVisivel == "serial", janela.TesteAbaVisivel);

        Conferir("console: mas o console esta la, do lado",
            janela.TestePainelEstaEmCasa("console"));

        janela.TesteLimparConsole();

        // ---- a linha de status nao se perde mais ----
        janela.TesteAvisar("primeira mensagem");
        janela.TesteAvisar("segunda mensagem");

        saida.AppendLine("        --- o console ---");
        foreach (string linha in janela.TesteConsole
                                      .Replace(Environment.NewLine, "\n").Split('\n'))
            if (linha.Trim().Length > 0) saida.AppendLine("        " + linha);

        Conferir("console: a linha de status vai parar no console",
            janela.TesteConsole.Contains("segunda mensagem"));

        Conferir("console: e a mensagem ANTERIOR continua la",
            janela.TesteConsole.Contains("primeira mensagem"),
            janela.TesteStatus);

        Conferir("console: a linha de status continua mostrando a ultima",
            janela.TesteStatus == "segunda mensagem", janela.TesteStatus);

        // Mensagem repetida nao vira linha nova: a de status e reescrita
        // muitas vezes com o mesmo texto.
        int antes = janela.TesteLinhasDoConsole;
        janela.TesteAvisar("segunda mensagem");
        janela.TesteAvisar("segunda mensagem");

        Conferir("console: mensagem repetida em sequencia nao repete linha",
            janela.TesteLinhasDoConsole == antes,
            $"{janela.TesteLinhasDoConsole} != {antes}");

        // ---- o nivel ----
        janela.TesteEscreverNoConsole("erro", "estourou o pino");
        janela.TesteEscreverNoConsole("aviso", "LED sem resistor");

        Conferir("console: o erro sai marcado como erro",
            janela.TesteConsole.Contains("ERRO") && janela.TesteConsole.Contains("estourou o pino"),
            janela.TesteConsole);

        Conferir("console: e o aviso como aviso",
            janela.TesteConsole.Contains("AVISO")
            && janela.TesteConsole.Contains("LED sem resistor"));

        // Cada linha tem a hora: sem ela nao da para saber se o erro veio
        // antes ou depois do que o sketch imprimiu.
        Conferir("console: cada linha tem a hora",
            janela.TesteConsole.Split(Environment.NewLine)
                  .All(l => l.Length < 8 || l[2] == ':' && l[5] == ':'),
            janela.TesteConsole.Split(Environment.NewLine)[0]);

        // ---- o que esta na tela acompanha ----
        Conferir("console: o que esta guardado e o que esta pintado batem",
            janela.TesteLinhasPintadas == janela.TesteLinhasDoConsole,
            $"pintadas {janela.TesteLinhasPintadas}, guardadas {janela.TesteLinhasDoConsole}");

        janela.TesteLimparConsole();
        Conferir("console: limpar esvazia os dois",
            janela.TesteLinhasDoConsole == 0 && janela.TesteLinhasPintadas == 0,
            $"{janela.TesteLinhasDoConsole} / {janela.TesteLinhasPintadas}");

        // Depois de limpo, o console volta a encher normalmente -- e o
        // caso em que o contador de linhas ja pintadas desanda.
        janela.TesteAvisar("depois da limpeza");
        Conferir("console: e depois de limpo ele volta a escrever",
            janela.TesteLinhasDoConsole == 1 && janela.TesteLinhasPintadas == 1,
            $"{janela.TesteLinhasDoConsole} / {janela.TesteLinhasPintadas}");

        // ---- rodar deixa rastro ----
        janela.TesteLimparConsole();
        janela.TesteRodar();
        janela.TesteParar();

        saida.AppendLine("        --- depois de rodar ---");
        foreach (string linha in janela.TesteConsole
                                      .Replace(Environment.NewLine, "\n").Split('\n'))
            if (linha.Trim().Length > 0) saida.AppendLine("        " + linha);

        Conferir("console: rodar deixa o que compilou registrado",
            janela.TesteConsole.Contains("compil"), janela.TesteConsole);

        // ---- e da para solta-lo como as outras abas ----
        janela.TesteSoltarAba("console");
        Conferir("console: da para solta-lo numa janela",
            !janela.TestePainelEstaEmCasa("console") && janela.TesteAbaAVista("console"));

        janela.TesteSoltarAba("console");
        Conferir("console: e traze-lo de volta",
            janela.TestePainelEstaEmCasa("console"));
    }

    /// <summary>
    /// A cor de cada linha da lista de componentes.
    ///
    /// Alpha 18: "ao dar play no circuito, deve piscar em verde e voltar
    /// para branco com fade quais fios estao conectados, os que nao
    /// estiverem em amarelo e os com algum erro em vermelho".
    ///
    /// O pisca em si e animacao, e nao da para conferir por teste sem
    /// tela. O que da -- e e o que decide a cor -- e o ESTADO de cada
    /// linha: ligada, sem ligacao ou com erro. Errar isso pintaria de
    /// verde um fio que nao chega em lugar nenhum, que e pior do que
    /// nao pintar nada.
    /// </summary>
    static void Ligacoes_EmCores()
    {
        saida.AppendLine("ligacoes em cores");

        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 0, Y = 0 });

        // Um LED ligado por fio, e outro jogado no canto sem nada.
        c.Pecas.Add(new PecaNoCircuito { Id = "led1", Tipo = "led", X = 400, Y = 100 });
        c.Pecas.Add(new PecaNoCircuito { Id = "led2", Tipo = "led", X = 900, Y = 600 });

        c.Fios.Add(new Fio
        {
            DePeca = "esp", DePino = "D2", ParaPeca = "led1", ParaPino = "A", Cor = "#2FA84F",
        });
        c.Fios.Add(new Fio
        {
            DePeca = "esp", DePino = "GND.1", ParaPeca = "led1", ParaPino = "C", Cor = "#000000",
        });

        var janela = new MainWindow();
        janela.TesteAbrir(c, "void setup() {}" + Environment.NewLine + "void loop() {}");

        saida.AppendLine("        --- a arvore ---");
        foreach (string linha in janela.TesteArvoreDeComponentes()
                                      .Replace(Environment.NewLine, "\n").Split('\n'))
            if (linha.Trim().Length > 0) saida.AppendLine("        " + linha);

        Conferir("cores: o que esta ligado sai como ligado",
            janela.TesteEstadoDaLinha("D2 > A") == "Ligado",
            janela.TesteEstadoDaLinha("D2 > A"));

        // O LED do canto nao chega em placa nenhuma: amarelo.
        Conferir("cores: o que nao chega em placa nenhuma sai sem ligacao",
            janela.TesteEstadoDaPeca("led2") == "SemLigacao",
            janela.TesteEstadoDaPeca("led2"));

        Conferir("cores: e o que chega sai ligado",
            janela.TesteEstadoDaPeca("led1") == "Ligado",
            janela.TesteEstadoDaPeca("led1"));

        int quantas = janela.TesteLigacoesQuePiscam;
        saida.AppendLine($"        linhas que piscam: {quantas}");

        // Duas ligacoes (D2>A e GND>C) mais a linha do proprio LED
        // pendurado na placa. O LED solto NAO entra: ele nao esta ligado.
        Conferir("cores: so o que esta ligado pisca", quantas > 0 && quantas <= 3,
            quantas.ToString());

        // ---- queimou: vermelho ----
        janela.TesteQueimar("led1");

        string depois = janela.TesteEstadoDaLinha("D2 > A");
        saida.AppendLine($"        depois de queimar: {depois}");

        Conferir("cores: a peca queimada pinta a ligacao de vermelho",
            depois == "Erro", depois);

        Conferir("cores: e quem queimou para de piscar",
            janela.TesteLigacoesQuePiscam < quantas,
            $"{janela.TesteLigacoesQuePiscam} vs {quantas}");

        janela.TesteApagarQueimaduras();
        Conferir("cores: apagada a queimadura, a ligacao volta ao normal",
            janela.TesteEstadoDaLinha("D2 > A") == "Ligado",
            janela.TesteEstadoDaLinha("D2 > A"));

        // Sem queimadura nenhuma na conta, a lista nao inventa erro.
        Conferir("cores: sem execucao, nenhuma linha sai como erro",
            !ArvoreDeComponentes.EmTexto(ArvoreDeComponentes.Montar(c, new Netlist(c)))
                                .Contains("💥"));
    }

    /// <summary>
    /// Os limites de cada placa: RAM, flash, nucleos e relogio.
    ///
    /// Alpha 18: "limitadores de placas, para RAM de cada placa,
    /// processador, cores, e etc. para ter uma experiencia mais
    /// acurada".
    ///
    /// O caso que importa e o classico da aula: um vetor que cabe numa
    /// ESP32 e NAO cabe num Uno. Ate a Alpha 17 os dois rodavam igual
    /// aqui, e o aluno so descobria a diferenca na placa de verdade --
    /// que reinicia sozinha, sem mensagem nenhuma.
    /// </summary>
    static void Limites_DaPlaca()
    {
        saida.AppendLine("limites da placa");

        // ---- os numeros das placas ----
        foreach (var placa in PerfilPlaca.Conhecidos)
            saida.AppendLine($"        {placa.Nome}: {placa.Ficha}");

        Conferir("limites: o Uno tem 2 KB de RAM",
            PerfilPlaca.Uno.RamBytes == 2048, PerfilPlaca.Uno.RamBytes.ToString());

        Conferir("limites: a ESP32 tem dois nucleos e 240 MHz",
            PerfilPlaca.Esp32.Nucleos == 2 && PerfilPlaca.Esp32.Megahertz == 240);

        Conferir("limites: os AVR sao de 8 bits e as outras nao",
            PerfilPlaca.Uno.De8Bits && PerfilPlaca.Mega.De8Bits
            && !PerfilPlaca.Esp32.De8Bits && !PerfilPlaca.PiPico.De8Bits);

        Conferir("limites: toda placa conhecida tem RAM e flash declarados",
            PerfilPlaca.Conhecidos.All(p => p.RamBytes > 0 && p.FlashBytes > 0
                                            && p.Processador != "?"),
            string.Join(",", PerfilPlaca.Conhecidos
                                        .Where(p => p.Processador == "?")
                                        .Select(p => p.Nome)));

        // ---- o "int" nao tem o mesmo tamanho nas duas ----
        Conferir("limites: int ocupa 2 bytes no AVR e 4 nas de 32 bits",
            Memoria.Tamanho("int", PerfilPlaca.Uno) == 2
            && Memoria.Tamanho("int", PerfilPlaca.Esp32) == 4);

        Conferir("limites: ponteiro e o endereco, e nao o que esta do outro lado",
            Memoria.Tamanho("char*", PerfilPlaca.Uno) == 2);

        // ---- o mesmo sketch nas duas placas ----
        string grande = "int tabela[1200];" + Environment.NewLine
                        + "void setup() {}" + Environment.NewLine
                        + "void loop() {}";

        var prog = Compilador.Compilar(grande);

        var noUno = Memoria.Estimar(prog, PerfilPlaca.Uno);
        var naEsp = Memoria.Estimar(prog, PerfilPlaca.Esp32);

        saida.AppendLine("        --- int tabela[1200] ---");
        foreach (string linha in noUno.EmTexto().Split(Environment.NewLine))
            saida.AppendLine("        Uno:   " + linha);
        foreach (string linha in naEsp.EmTexto().Split(Environment.NewLine))
            saida.AppendLine("        ESP32: " + linha);

        Conferir("limites: 1.200 int nao cabem na RAM de um Uno", noUno.RamEstourou,
            $"{noUno.Ram} de {PerfilPlaca.Uno.RamBytes}");

        Conferir("limites: e cabem folgados numa ESP32", !naEsp.Estourou,
            $"{naEsp.Ram} de {PerfilPlaca.Esp32.RamBytes}");

        Conferir("limites: o motivo sai escrito, com o numero",
            noUno.PorQueNaoCabe()?.Contains("2.048") == true
            || noUno.PorQueNaoCabe()?.Contains("2048") == true,
            noUno.PorQueNaoCabe());

        // ---- um sketch normal cabe em qualquer uma ----
        var simples = Compilador.Compilar(
            "int contador = 0;" + Environment.NewLine
            + "void setup() { Serial.begin(9600); }" + Environment.NewLine
            + "void loop() { contador++; delay(100); }");

        foreach (var placa in PerfilPlaca.Conhecidos)
        {
            var conta = Memoria.Estimar(simples, placa);
            Conferir($"limites: um sketch simples cabe na {placa.Nome}",
                !conta.Estourou,
                $"{conta.Ram}/{placa.RamBytes} RAM, {conta.Flash}/{placa.FlashBytes} flash");
        }

        // Vetor de tamanho desconhecido nao pode inventar estouro: a
        // conta erra PARA MENOS de proposito, porque recusar um sketch
        // que funciona e pior do que deixar passar um que aperta.
        var incerto = Compilador.Compilar(
            "int n = 10;" + Environment.NewLine
            + "void setup() {}" + Environment.NewLine
            + "void loop() {}");

        Conferir("limites: sem saber o tamanho, a conta nao inventa estouro",
            !Memoria.Estimar(incerto, PerfilPlaca.Franzininho).RamEstourou);

        // ---- e o simulador recusa rodar ----
        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito
        {
            Id = "uno", Tipo = "arduino-uno", X = 40, Y = 40, Sketch = grande,
        });

        var janela = new MainWindow();
        janela.TesteAbrir(c, grande);
        janela.TesteLimparConsole();
        janela.TesteRodar();

        saida.AppendLine("        --- rodando o que nao cabe ---");
        foreach (string linha in janela.TesteConsole
                                      .Replace(Environment.NewLine, "\n").Split('\n'))
            if (linha.Trim().Length > 0) saida.AppendLine("        " + linha);

        Conferir("limites: o simulador recusa rodar o que nao cabe",
            janela.TesteStatus.Contains("não cabe"), janela.TesteStatus);

        Conferir("limites: e o console diz de quanto e o excesso",
            janela.TesteConsole.Contains("RAM"), janela.TesteConsole);

        // A ficha da placa entra no console: e a "experiencia mais
        // acurada" do pedido -- saber em que chip o sketch esta rodando.
        Conferir("limites: a ficha da placa aparece no console",
            janela.TesteConsole.Contains("ATmega328P"), janela.TesteConsole);

        janela.TesteParar();
    }

    /// <summary>
    /// O Bluetooth da placa acessivel de um programa do computador.
    ///
    /// Alpha 18: "assim como servidor wifi, bluetooth funcional com
    /// sktch de teste".
    ///
    /// O servidor WiFi ja era de verdade: abre porta em 127.0.0.1 e o
    /// navegador entra nela. O Bluetooth nao -- so existia entre duas
    /// placas da bancada e o celular desenhado no painel, entao nao
    /// havia como PROVAR que funcionava com um programa de fora.
    ///
    /// Este teste e a prova: abre um socket de verdade na porta que a
    /// placa anunciou, manda bytes e le a resposta.
    /// </summary>
    static void Bluetooth_PelaPortaDoPc()
    {
        saida.AppendLine("bluetooth pela porta do computador");

        var rede = new Rede();
        var placa = new Maquina { Nome = "ESP32" };

        var bt = rede.IniciarBluetooth(placa, "CircuitSketch", mestre: false);

        Conferir("bluetooth: a placa visivel abre uma porta no computador",
            bt.PeloComputador is not null);

        if (bt.PeloComputador is null) return;

        int porta = bt.PeloComputador.Porta;
        saida.AppendLine($"        porta: 127.0.0.1:{porta}");

        Conferir("bluetooth: a porta comeca na 9000",
            porta >= PortaBluetooth.PrimeiraPorta && porta < PortaBluetooth.PrimeiraPorta + 16,
            porta.ToString());

        Conferir("bluetooth: sem ninguem conectado, nao ha conexao",
            !bt.TemConexao);

        using (var tcp = new System.Net.Sockets.TcpClient())
        {
            tcp.Connect(System.Net.IPAddress.Loopback, porta);
            var fluxo = tcp.GetStream();

            // ---- do computador para o sketch ----
            var manda = System.Text.Encoding.UTF8.GetBytes("PING" + (char)10);
            fluxo.Write(manda, 0, manda.Length);
            fluxo.Flush();

            Esperar(() => bt.Entrada.Disponivel >= manda.Length);

            saida.AppendLine($"        chegou no sketch: {bt.Entrada.Disponivel} bytes");

            Conferir("bluetooth: o que o computador manda chega no SerialBT",
                bt.Entrada.Disponivel >= manda.Length,
                bt.Entrada.Disponivel.ToString());

            Conferir("bluetooth: e chega igual, byte a byte",
                LerTudo(bt.Entrada) == "PING" + (char)10);

            // Um programa conectado conta como conexao: para o sketch e
            // indistinguivel de um celular pareado, que e o ponto.
            Esperar(() => bt.TemConexao);
            Conferir("bluetooth: um programa conectado conta como conexao", bt.TemConexao);

            // ---- do sketch para o computador ----
            bt.Enviar(System.Text.Encoding.UTF8.GetBytes("PONG" + (char)10));

            var balde = new byte[32];
            int quantos = fluxo.Read(balde, 0, balde.Length);
            string voltou = System.Text.Encoding.UTF8.GetString(balde, 0, quantos);

            saida.AppendLine($"        voltou do sketch: {voltou.Trim()}");

            Conferir("bluetooth: o que o sketch escreve chega no computador",
                voltou.StartsWith("PONG"), voltou);
        }

        // ---- a placa saindo do ar leva a porta junto ----
        Esperar(() => !bt.TemConexao);
        rede.EsquecerBluetooth(placa);

        Conferir("bluetooth: a placa saindo do ar fecha a porta",
            bt.PeloComputador is null);

        // Fechada a porta, o numero volta a ficar livre -- senao a
        // execucao seguinte cairia na porta de baixo a cada vez.
        var outra = new Maquina { Nome = "ESP32" };
        var bt2 = rede.IniciarBluetooth(outra, "CircuitSketch", mestre: false);

        saida.AppendLine($"        de novo: 127.0.0.1:{bt2.PeloComputador?.Porta}");

        Conferir("bluetooth: e a mesma porta serve de novo",
            bt2.PeloComputador?.Porta == porta,
            $"{bt2.PeloComputador?.Porta} != {porta}");

        // Duas placas visiveis ao mesmo tempo: a segunda pega a porta
        // seguinte, e nao fica sem.
        var terceira = new Maquina { Nome = "outra" };
        var bt3 = rede.IniciarBluetooth(terceira, "Outra", mestre: false);

        saida.AppendLine($"        a segunda placa: 127.0.0.1:{bt3.PeloComputador?.Porta}");

        Conferir("bluetooth: duas placas ao mesmo tempo, duas portas",
            bt3.PeloComputador is not null
            && bt3.PeloComputador.Porta != bt2.PeloComputador.Porta,
            $"{bt3.PeloComputador?.Porta}");

        // Mestre nao abre porta: quem conecta e ele, e nao o contrario.
        var mestre = new Maquina { Nome = "mestre" };
        var btm = rede.IniciarBluetooth(mestre, "Mestre", mestre: true);

        Conferir("bluetooth: a placa mestre nao abre porta",
            btm.PeloComputador is null);

        rede.EsquecerBluetooth(outra);
        rede.EsquecerBluetooth(terceira);
        rede.EsquecerBluetooth(mestre);
    }

    /// <summary>
    /// Toda linha do Intel HEX fecha a soma de verificacao?
    ///
    /// E a unica prova de que o arquivo grava: o avrdude recusa um
    /// registro cuja soma nao bate, e um .hex que nao grava nao serve
    /// para nada.
    /// </summary>
    static bool SomaDoHexFecha(string hex)
    {
        foreach (string bruta in hex.Replace(Environment.NewLine, "\n").Split('\n'))
        {
            string linha = bruta.Trim();
            if (linha.Length < 11 || linha[0] != ':') continue;

            int soma = 0;

            for (int i = 1; i + 1 < linha.Length; i += 2)
                soma += Convert.ToInt32(linha.Substring(i, 2), 16);

            if ((soma & 0xFF) != 0) return false;
        }

        return true;
    }

    /// <summary>Espera a condicao virar verdadeira, ate meio segundo.</summary>
    static void Esperar(Func<bool> pronto)
    {
        for (int i = 0; i < 100 && !pronto(); i++) Thread.Sleep(5);
    }

    /// <summary>Tudo o que esta no fluxo, como texto.</summary>
    static string LerTudo(FluxoDeEntrada fluxo)
    {
        var bytes = new List<byte>();
        for (int b = fluxo.Ler(); b >= 0; b = fluxo.Ler()) bytes.Add((byte)b);

        return System.Text.Encoding.UTF8.GetString([.. bytes]);
    }

    /// <summary>
    /// Mexer no fio pela lista de componentes.
    ///
    /// Alpha 18: "capacidade de controlar onde os fios vao e conexoes de
    /// pinos na aba circuito".
    ///
    /// Ate a Alpha 17 a aba so DESCREVIA: dava para ler "D2 > A" e nao
    /// dava para fazer nada com aquilo. O risco aqui nao e o menu -- e a
    /// lista continuar mostrando o estado ANTIGO depois da mudanca, que
    /// e o jeito mais rapido de a pessoa perder a confianca nela.
    /// </summary>
    static void Ligacao_ComandadaPelaLista()
    {
        saida.AppendLine("ligacao comandada pela lista");

        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 0, Y = 0 });
        c.Pecas.Add(new PecaNoCircuito { Id = "led1", Tipo = "led", X = 400, Y = 100 });

        c.Fios.Add(new Fio
        {
            DePeca = "esp", DePino = "D2", ParaPeca = "led1", ParaPino = "A", Cor = "#00A651",
        });
        c.Fios.Add(new Fio
        {
            DePeca = "esp", DePino = "GND.1", ParaPeca = "led1", ParaPino = "C", Cor = "#000000",
        });

        var janela = new MainWindow();
        janela.TesteAbrir(c, "void setup() {}" + Environment.NewLine + "void loop() {}");

        Conferir("ligacao: a linha da ligacao sabe de qual fio ela e",
            janela.TesteLinhaTemFio("D2 > A"));

        Conferir("ligacao: a linha de uma peca nao e um fio",
            !janela.TesteLinhaTemFio("Arduino") && !janela.TesteLinhaTemFio("[Protoboard"));

        // ---- o menu so oferece os pinos DA PLACA ----
        var pinos = janela.TestePinosOferecidos(0);
        saida.AppendLine($"        pinos oferecidos: {pinos.Count}");

        Conferir("ligacao: o menu oferece os pinos da placa",
            pinos.Contains("D2") && pinos.Contains("D4"), string.Join(",", pinos.Take(8)));

        Conferir("ligacao: e nao oferece os pinos do LED",
            !pinos.Contains("A") && !pinos.Contains("C"));

        // ---- trocar o pino ----
        janela.TesteTrocarPinoDoFio(0, "D4");

        saida.AppendLine("        --- depois de trocar para D4 ---");
        foreach (string linha in janela.TesteArvoreDeComponentes()
                                      .Replace(Environment.NewLine, "\n").Split('\n'))
            if (linha.Trim().Length > 0) saida.AppendLine("        " + linha);

        Conferir("ligacao: o fio passa a sair do pino novo",
            c.Fios[0].DePino == "D4", c.Fios[0].DePino);

        Conferir("ligacao: e a lista ja mostra o pino novo",
            janela.TesteArvoreDeComponentes().Contains("D4 > A")
            && !janela.TesteArvoreDeComponentes().Contains("D2 > A"),
            janela.TesteArvoreDeComponentes());

        // A troca nao pode mexer na outra ponta: o fio continua no LED.
        Conferir("ligacao: a ponta do LED nao se mexe",
            c.Fios[0].ParaPeca == "led1" && c.Fios[0].ParaPino == "A");

        // ---- a cor ----
        janela.TestePintarFio(0, "#D6202A");

        Conferir("ligacao: pintar o fio muda a cor da linha",
            janela.TesteCorDaLinha("D4 > A") == "#D6202A",
            janela.TesteCorDaLinha("D4 > A"));

        // ---- o tracado ----
        c.Fios[0].Dobras.Add(new PontoFio { X = 200, Y = 200 });
        c.Fios[0].Dobras.Add(new PontoFio { X = 300, Y = 120 });

        Conferir("ligacao: os cantos entram no fio", c.Fios[0].Dobras.Count == 2);

        janela.TesteEndireitarFio(0);
        Conferir("ligacao: e o menu tira todos de uma vez",
            c.Fios[0].Dobras.Count == 0, c.Fios[0].Dobras.Count.ToString());

        // ---- apagar ----
        int antes = c.Fios.Count;
        janela.TesteApagarFio(0);

        Conferir("ligacao: apagar tira o fio do circuito",
            c.Fios.Count == antes - 1, $"{c.Fios.Count} de {antes}");

        Conferir("ligacao: e a linha some da lista",
            !janela.TesteArvoreDeComponentes().Contains("D4 > A"),
            janela.TesteArvoreDeComponentes());

        // O GND continua la: apagar um fio nao pode levar o outro junto.
        Conferir("ligacao: o outro fio continua inteiro",
            janela.TesteArvoreDeComponentes().Contains("> C"),
            janela.TesteArvoreDeComponentes());
    }

    /// <summary>
    /// As vinte e seis placas novas da Alpha 18.
    ///
    /// O que decide se uma placa funciona e uma coisa so: o NOME do
    /// pino virar o GPIO certo. E por esse nome que o aluno liga o fio e
    /// e esse numero que o sketch escreve -- errar a traducao deixa o
    /// LED apagado sem nenhuma mensagem de erro, que e o pior defeito
    /// que um simulador pode ter.
    /// </summary>
    static void Placas_DaAlpha18()
    {
        saida.AppendLine("placas da Alpha 18");

        string[] novas =
        [
            "esp32-s2-devkit", "esp32-s3-devkit", "esp32-c3-devkit",
            "esp32-c6-devkit", "esp32-h2-devkit",
            "esp8266-nodemcu",
            "esp-01", "esp-02", "esp-03", "esp-04", "esp-05", "esp-06", "esp-07",
            "esp-08", "esp-09", "esp-10", "esp-11", "esp-12", "esp-12e", "esp-12f",
            "esp-13", "esp-14",
            "attiny85", "st-nucleo-l031k6", "st-nucleo-c031c6", "pi-pico-w",
        ];

        saida.AppendLine($"        {novas.Length} placas novas");

        foreach (string id in novas)
        {
            Conferir($"placa {id}: esta no catalogo com desenho e pinos",
                Catalogo.Obter(id) is { Pinos.Count: > 0 } && Pecas.Talvez(id) is not null);

            Conferir($"placa {id}: tem perfil",
                PerfilPlaca.De(id) is not null);

            Conferir($"placa {id}: entra na categoria Placas",
                Catalogo.Obter(id)?.Categoria == "Placas",
                Catalogo.Obter(id)?.Categoria);
        }

        // ---- o nome do pino vira o GPIO certo ----
        foreach (var (id, pino, gpio) in new[]
                 {
                     // As DevKit novas usam o numero puro na serigrafia.
                     ("esp32-c3-devkit", "4", 4),
                     ("esp32-s3-devkit", "48", 48),

                     // A NodeMCU renumerou tudo: o D4 dela e o GPIO2.
                     // E a pegadinha mais comum da aula.
                     ("esp8266-nodemcu", "D4", 2),
                     ("esp8266-nodemcu", "D0", 16),
                     ("esp8266-nodemcu", "D5", 14),

                     // Nos modulos ESP-xx o nome JA e o GPIO.
                     ("esp-12f", "GPIO4", 4),
                     ("esp-01", "GPIO2", 2),
                     ("esp-05", "TX", 1),

                     ("attiny85", "PB1", 1),
                     ("st-nucleo-l031k6", "D13", 13),
                     ("st-nucleo-l031k6", "A0", 14),
                     ("pi-pico-w", "GP15", 15),
                 })
            Conferir($"pino: {id}/{pino} e o GPIO {gpio}",
                PerfilPlaca.De(id)?.Numero(pino) == gpio,
                PerfilPlaca.De(id)?.Numero(pino).ToString());

        // ---- o que cada uma tem de hardware ----
        Conferir("placas: a H2 nao tem WiFi, e as outras ESP32 tem",
            !PerfilPlaca.Esp32H2.TemWifi && PerfilPlaca.Esp32C3.TemWifi
            && PerfilPlaca.Esp32S3.TemWifi);

        Conferir("placas: so a ESP32 original tem Bluetooth classico",
            PerfilPlaca.Esp32.TemBluetoothClassico
            && !PerfilPlaca.Esp32C3.TemBluetoothClassico
            && !PerfilPlaca.Esp32S3.TemBluetoothClassico);

        Conferir("placas: a S3 tem dois nucleos e a C3 tem um",
            PerfilPlaca.Esp32S3.Nucleos == 2 && PerfilPlaca.Esp32C3.Nucleos == 1);

        Conferir("placas: o ATtiny85 tem meio KB de RAM",
            PerfilPlaca.ATtiny85.RamBytes == 512 && PerfilPlaca.ATtiny85.De8Bits);

        Conferir("placas: a Pico W tem WiFi e a Pico nao",
            PerfilPlaca.PiPicoW.TemWifi && !PerfilPlaca.PiPico.TemWifi);

        // ---- as familias da paleta ----
        var abas = JanelaPecas.Subabas();
        saida.AppendLine("        abas: " + string.Join(" | ", abas));

        foreach (var (id, familia) in new[]
                 {
                     ("esp32-c6-devkit", "ESP32"), ("esp-14", "ESP8266"),
                     ("esp8266-nodemcu", "ESP8266"),
                     ("attiny85", "Arduino"),
                     ("st-nucleo-c031c6", "STM32"),
                     ("pi-pico-w", "Raspberry Pi"),
                 })
            Conferir($"familia: {id} fica em {familia}",
                Catalogo.Familia(id) == familia, Catalogo.Familia(id));

        var abaEsp32 = JanelaPecas.Montar("ESP32", "").Where(i => i.Peca is not null)
                                  .Select(i => i.Peca.Id).ToList();
        var abaEsp8266 = JanelaPecas.Montar("ESP8266", "").Where(i => i.Peca is not null)
                                    .Select(i => i.Peca.Id).ToList();
        saida.AppendLine($"        aba ESP32: {abaEsp32.Count} placas; aba ESP8266: {abaEsp8266.Count}");

        Conferir("familia: a aba ESP32 junta a DevKit V1 e as variantes, e so elas",
            abaEsp32.Count == 6 && abaEsp32.All(id => id.StartsWith("esp32")),
            string.Join(", ", abaEsp32));

        Conferir("familia: a aba ESP8266 junta a NodeMCU e os modulos ESP-01 ao ESP-14",
            abaEsp8266.Count == 17 && abaEsp8266.Contains("esp8266-nodemcu")
            && abaEsp8266.Contains("esp-01") && abaEsp8266.Contains("esp-14")
            && abaEsp8266.All(id => !id.StartsWith("esp32")),
            string.Join(", ", abaEsp8266));

        // Nenhuma placa pode ficar fora das abas.
        int soltas = JanelaPecas.Montar(JanelaPecas.AbaComponentes, "")
                                .Count(i => i.Peca is not null && PerfilPlaca.EhPlaca(i.Peca.Id));

        Conferir("familia: nenhuma placa sobra na aba Componentes", soltas == 0,
            soltas.ToString());
    }

    /// <summary>
    /// O SDK em C da Raspberry Pi Pico.
    ///
    /// Alpha 18 pediu a Pico em tres sabores -- Arduino, MicroPython e
    /// "Pi Pico SDK (Avancado)" -- e pediu suporte a C. E a mesma
    /// coisa: o SDK da Raspberry e C puro.
    ///
    /// O que muda em relacao a um sketch e o PONTO DE ENTRADA: o
    /// programa comeca em main(), com o laco infinito escrito a mao. Se
    /// a conversao para setup/loop errar, o sketch compila e nao roda
    /// nada -- sem erro nenhum na tela.
    /// </summary>
    static void PicoSdk_EmC()
    {
        saida.AppendLine("SDK da Pico, em C");

        string fonte = string.Join(Environment.NewLine,
            "#include \"pico/stdlib.h\"",
            "",
            "#define LED 15",
            "",
            "int main() {",
            "    stdio_init_all();",
            "    gpio_init(LED);",
            "    gpio_set_dir(LED, GPIO_OUT);",
            "",
            "    while (true) {",
            "        gpio_put(LED, 1);",
            "        sleep_ms(500);",
            "        gpio_put(LED, 0);",
            "        sleep_ms(500);",
            "    }",
            "}");

        Conferir("pico sdk: o detector ve C, e nao um sketch",
            Compilador.Detectar(fonte) == Linguagem.C,
            Compilador.Detectar(fonte).ToString());

        Conferir("pico sdk: e a interface chama de C",
            Compilador.Nome(Linguagem.C).StartsWith("C"),
            Compilador.Nome(Linguagem.C));

        // Um sketch normal continua sendo sketch, mesmo com um main()
        // no meio: quem manda e o setup/loop.
        Conferir("pico sdk: um sketch com setup continua sendo sketch",
            Compilador.Detectar("void setup() {} void loop() {} int main() { return 0; }")
                == Linguagem.Arduino);

        var prog = Compilador.Compilar(fonte);

        saida.AppendLine("        funcoes: "
                         + string.Join(", ", prog.Funcoes.Select(f => f.Nome)));

        Conferir("pico sdk: o main vira setup e loop",
            prog.Funcoes.Any(f => f.Nome == "setup") && prog.Funcoes.Any(f => f.Nome == "loop"),
            string.Join(",", prog.Funcoes.Select(f => f.Nome)));

        Conferir("pico sdk: e o main some, para nao rodar duas vezes",
            !prog.Funcoes.Any(f => f.Nome == "main"));

        // O que vem ANTES do laco e o setup; o corpo do laco e o loop.
        var setup = prog.Funcoes.First(f => f.Nome == "setup");
        var loop = prog.Funcoes.First(f => f.Nome == "loop");

        saida.AppendLine($"        setup com {setup.Corpo.Corpo.Count} comandos, "
                         + $"loop com {loop.Corpo.Corpo.Count}");

        Conferir("pico sdk: o preparo fica no setup", setup.Corpo.Corpo.Count == 3,
            setup.Corpo.Corpo.Count.ToString());

        Conferir("pico sdk: e o miolo do laco fica no loop", loop.Corpo.Corpo.Count == 4,
            loop.Corpo.Corpo.Count.ToString());

        // ---- e roda de verdade, mexendo no pino ----
        var maq = new Maquina();
        var interp = new Interpretador(prog, maq);
        interp.Preparar();
        interp.Setup();

        for (int i = 0; i < 4; i++) interp.Loop();

        int mexidas = maq.Eventos.Count(e => e.Pino == 15);
        saida.AppendLine($"        GPIO15 mudou {mexidas} vez(es) em {maq.Ms} ms");

        Conferir("pico sdk: o gpio_put mexe no pino de verdade",
            mexidas >= 4, mexidas.ToString());

        Conferir("pico sdk: e o sleep_ms anda com o relogio",
            maq.Ms >= 3500, maq.Ms.ToString());

        // "for (;;)" e o outro jeito classico de escrever o mesmo laco.
        var comFor = Compilador.Compilar(string.Join(Environment.NewLine,
            "int main() {",
            "    gpio_init(2);",
            "    for (;;) {",
            "        gpio_put(2, 1);",
            "        sleep_ms(10);",
            "    }",
            "}"));

        Conferir("pico sdk: o for(;;) tambem vira loop",
            comFor.Funcoes.First(f => f.Nome == "loop").Corpo.Corpo.Count == 2,
            comFor.Funcoes.First(f => f.Nome == "loop").Corpo.Corpo.Count.ToString());
    }

    /// <summary>
    /// Roda um programa e diz quantas vezes o pino mexeu.
    ///
    /// E o teste que importa para uma linguagem nova: nao basta
    /// compilar, o LED tem que acender. Um analisador que engole o
    /// programa e nao mexe em pino nenhum passaria num teste de
    /// sintaxe e falharia na unica coisa que o simulador serve para
    /// fazer.
    /// </summary>
    static (int Mexidas, long Ms, string Serial) Rodar(string fonte, int pino, int voltas = 4,
                                                        PerfilPlaca placa = null)
    {
        var prog = Compilador.Compilar(fonte);

        // O Assembly e AVR e so roda no ATmega328P; as outras linguagens
        // rodam em qualquer placa, e a ESP32 continua sendo a padrao.
        var maq = new Maquina
        {
            Perfil = placa ?? (prog.Linguagem == Linguagem.Assembly
                ? PerfilPlaca.Uno : PerfilPlaca.Esp32),
        };
        var interp = new Interpretador(prog, maq);

        interp.Preparar();
        interp.Setup();
        for (int i = 0; i < voltas; i++) interp.Loop();

        string serial;
        lock (maq.Trava) serial = string.Join(" | ", maq.Serial);

        return (maq.Eventos.Count(e => e.Pino == pino), maq.Ms, serial);
    }

    /// <summary>
    /// As linguagens novas da Alpha 18.
    ///
    /// Sao nove portas de entrada para o MESMO interpretador. E a
    /// decisao que sustenta tudo isto desde a Alpha 17: um
    /// interpretador por linguagem criaria nove verdades sobre o que
    /// "acender o pino 13" significa, e elas divergiriam na primeira
    /// correcao feita em oito delas.
    ///
    /// Cada bloco aqui escreve o MESMO pisca-pisca no idioma da
    /// linguagem, e confere a mesma coisa: detectou certo, compilou, e
    /// o pino mexeu.
    /// </summary>
    static void Linguagens_DaAlpha18()
    {
        saida.AppendLine("linguagens da Alpha 18");

        saida.AppendLine("        " + string.Join(", ",
            Compilador.Todas.Select(l => Compilador.Nome(l) + Compilador.Extensao(l))));

        Conferir("linguagens: sao dez no menu", Compilador.Todas.Length == 10,
            Compilador.Todas.Length.ToString());

        Conferir("linguagens: cada uma tem nome proprio",
            Compilador.Todas.Select(Compilador.Nome).Distinct().Count() == 10);

        // A extensao NAO e unica de proposito: um corpo em Ada e um em
        // SPARK sao os dois ".adb" na vida real, porque SPARK e Ada com
        // contratos. Inventar uma extensao para separa-los aqui seria
        // ensinar errado.
        Conferir("linguagens: so Ada e SPARK dividem extensao",
            Compilador.Todas.Select(Compilador.Extensao).Distinct().Count() == 9);

        // ================= Ada =================
        string ada = string.Join(Environment.NewLine,
            "with Ada.Text_IO; use Ada.Text_IO;",
            "",
            "procedure Pisca is",
            "   Led : constant Integer := 13;",
            "begin",
            "   Pin_Mode (Led, Output);",
            "   Put_Line (\"Ada acordou\");",
            "",
            "   loop",
            "      Digital_Write (Led, High);",
            "      Delay_Ms (250);",
            "      Digital_Write (Led, Low);",
            "      Delay_Ms (250);",
            "   end loop;",
            "end Pisca;");

        Conferir("ada: o detector reconhece", Compilador.Detectar(ada) == Linguagem.Ada,
            Compilador.Detectar(ada).ToString());

        var r = Rodar(ada, 13);
        saida.AppendLine($"        ada: {r.Mexidas} mexidas em {r.Ms} ms | {r.Serial}");

        Conferir("ada: o LED pisca", r.Mexidas >= 8, r.Mexidas.ToString());
        Conferir("ada: o Put_Line chega no monitor", r.Serial.Contains("Ada acordou"), r.Serial);
        Conferir("ada: o Delay_Ms anda com o relogio", r.Ms >= 2000, r.Ms.ToString());

        // O "for I in 1 .. 3" passa TRES vezes: o limite e inclusivo, e
        // usar "<" aqui comeria a ultima volta de todo programa em Ada.
        var contagem = Rodar(string.Join(Environment.NewLine,
            "procedure Conta is",
            "   Total : Integer := 0;",
            "begin",
            "   for I in 1 .. 3 loop",
            "      Total := Total + 1;",
            "      Digital_Write (2, High);",
            "      Digital_Write (2, Low);",
            "   end loop;",
            "   Put_Line (\"total\");",
            "   loop",
            "      Delay_Ms (10);",
            "   end loop;",
            "end Conta;"), 2, voltas: 1);

        saida.AppendLine($"        ada for: {contagem.Mexidas} mexidas");
        Conferir("ada: o intervalo do for inclui o ultimo",
            contagem.Mexidas == 6, contagem.Mexidas.ToString());

        // ================= SPARK =================
        string spark = string.Join(Environment.NewLine,
            "pragma SPARK_Mode (On);",
            "",
            "procedure Pisca is",
            "   Led : constant Integer := 13;",
            "begin",
            "   Pin_Mode (Led, Output);",
            "   loop",
            "      Digital_Write (Led, High);",
            "      Delay_Ms (100);",
            "      Digital_Write (Led, Low);",
            "      Delay_Ms (100);",
            "   end loop;",
            "end Pisca;");

        Conferir("spark: o detector separa do Ada pelo pragma",
            Compilador.Detectar(spark) == Linguagem.Spark,
            Compilador.Detectar(spark).ToString());

        var rs = Rodar(spark, 13);
        saida.AppendLine($"        spark: {rs.Mexidas} mexidas");
        Conferir("spark: o LED pisca", rs.Mexidas >= 8, rs.Mexidas.ToString());

        // Sub-rotina declarada DENTRO de outra, com contrato, que e o
        // jeito idiomatico de escrever SPARK. Sem tratar isso, o
        // analisador lia o corpo da de dentro como se fosse o da de
        // fora -- o programa compilava e nao fazia nada.
        var aninhada = Rodar(string.Join(Environment.NewLine,
            "pragma SPARK_Mode (On);",
            "",
            "procedure Pisca is",
            "   Led : constant Integer := 13;",
            "",
            "   procedure Acender (Pino : Integer)",
            "     with Pre => Pino >= 0;",
            "",
            "   procedure Acender (Pino : Integer) is",
            "   begin",
            "      Digital_Write (Pino, High);",
            "   end Acender;",
            "",
            "begin",
            "   Pin_Mode (Led, Output);",
            "   loop",
            "      Acender (Led);",
            "      Delay_Ms (50);",
            "      Digital_Write (Led, Low);",
            "      Delay_Ms (50);",
            "   end loop;",
            "end Pisca;"), 13);

        saida.AppendLine($"        spark aninhada: {aninhada.Mexidas} mexidas");

        Conferir("spark: procedimento dentro de procedimento funciona",
            aninhada.Mexidas >= 8, aninhada.Mexidas.ToString());

        Conferir("spark: o contrato nao come a sub-rotina seguinte",
            Compilador.Compilar(string.Join(Environment.NewLine,
                "procedure P is",
                "   procedure A (X : Integer) with Pre => X > 0;",
                "   procedure A (X : Integer) is",
                "   begin",
                "      Digital_Write (X, High);",
                "   end A;",
                "begin",
                "   A (13);",
                "end P;")).Funcoes.Any(f => f.Nome == "A"));

        // ================= Cowgol =================
        string cowgol = string.Join(Environment.NewLine,
            "# pisca em Cowgol",
            "sub main() is",
            "    var led: uint8 := 13;",
            "    pin_mode(led, OUTPUT);",
            "    loop",
            "        digital_write(led, HIGH);",
            "        delay_ms(200);",
            "        digital_write(led, LOW);",
            "        delay_ms(200);",
            "    end loop;",
            "end sub;");

        Conferir("cowgol: o detector reconhece",
            Compilador.Detectar(cowgol) == Linguagem.Cowgol,
            Compilador.Detectar(cowgol).ToString());

        var rc = Rodar(cowgol, 13);
        saida.AppendLine($"        cowgol: {rc.Mexidas} mexidas");
        Conferir("cowgol: o LED pisca", rc.Mexidas >= 8, rc.Mexidas.ToString());

        // ================= Millipascal =================
        string milli = string.Join(Environment.NewLine,
            "proc main",
            "begin",
            "    pin_mode(13, OUTPUT);",
            "    while true begin",
            "        digital_write(13, HIGH);",
            "        delay_ms(100);",
            "        digital_write(13, LOW);",
            "        delay_ms(100);",
            "    end",
            "end proc");

        Conferir("millipascal: o detector reconhece",
            Compilador.Detectar(milli) == Linguagem.Millipascal,
            Compilador.Detectar(milli).ToString());

        var rm = Rodar(milli, 13);
        saida.AppendLine($"        millipascal: {rm.Mexidas} mexidas");
        Conferir("millipascal: o LED pisca", rm.Mexidas >= 8, rm.Mexidas.ToString());

        // ================= Rust =================
        string rust = string.Join(Environment.NewLine,
            "#![no_std]",
            "#![no_main]",
            "",
            "const LED: u8 = 13;",
            "",
            "fn main() {",
            "    pin_mode(LED, OUTPUT);",
            "    println!(\"Rust acordou\");",
            "",
            "    let mut contador = 0;",
            "",
            "    loop {",
            "        digital_write(LED, HIGH);",
            "        delay_ms(150);",
            "        digital_write(LED, LOW);",
            "        delay_ms(150);",
            "        contador += 1;",
            "    }",
            "}");

        Conferir("rust: o detector reconhece", Compilador.Detectar(rust) == Linguagem.Rust,
            Compilador.Detectar(rust).ToString());

        var rr = Rodar(rust, 13);
        saida.AppendLine($"        rust: {rr.Mexidas} mexidas | {rr.Serial}");

        Conferir("rust: o LED pisca", rr.Mexidas >= 8, rr.Mexidas.ToString());
        Conferir("rust: o println! chega no monitor",
            rr.Serial.Contains("Rust acordou"), rr.Serial);

        // "0..3" NAO inclui o 3; "0..=3" inclui. E uma volta de
        // diferenca, e e a que mais pega quem vem de outra linguagem.
        var rustFor = Rodar(string.Join(Environment.NewLine,
            "fn main() {",
            "    for _i in 0..3 {",
            "        digital_write(2, HIGH);",
            "        digital_write(2, LOW);",
            "    }",
            "    loop { delay_ms(10); }",
            "}"), 2, voltas: 1);

        saida.AppendLine($"        rust for: {rustFor.Mexidas} mexidas");
        Conferir("rust: o 0..3 passa tres vezes, sem incluir o 3",
            rustFor.Mexidas == 6, rustFor.Mexidas.ToString());

        // O match vira switch, com o break que o Rust nao escreve.
        var rustMatch = Rodar(string.Join(Environment.NewLine,
            "fn main() {",
            "    let x = 2;",
            "    match x {",
            "        1 => digital_write(3, HIGH),",
            "        2 => digital_write(4, HIGH),",
            "        _ => digital_write(5, HIGH),",
            "    }",
            "    loop { delay_ms(10); }",
            "}"), 4, voltas: 1);

        saida.AppendLine($"        rust match: pino 4 mexeu {rustMatch.Mexidas} vez(es)");
        Conferir("rust: o match escolhe um ramo so",
            rustMatch.Mexidas == 1, rustMatch.Mexidas.ToString());

        // ================= C# =================
        string csharp = string.Join(Environment.NewLine,
            "using System;",
            "",
            "public class Program",
            "{",
            "    public static void Main()",
            "    {",
            "        int led = 13;",
            "        PinMode(led, OUTPUT);",
            "        Console.WriteLine(\"C# acordou\");",
            "",
            "        while (true)",
            "        {",
            "            DigitalWrite(led, HIGH);",
            "            Delay(120);",
            "            DigitalWrite(led, LOW);",
            "            Delay(120);",
            "        }",
            "    }",
            "}");

        Conferir("csharp: o detector reconhece",
            Compilador.Detectar(csharp) == Linguagem.CSharp,
            Compilador.Detectar(csharp).ToString());

        var rcs = Rodar(csharp, 13);
        saida.AppendLine($"        csharp: {rcs.Mexidas} mexidas | {rcs.Serial}");

        Conferir("csharp: o LED pisca", rcs.Mexidas >= 8, rcs.Mexidas.ToString());
        Conferir("csharp: o Console.WriteLine chega no monitor",
            rcs.Serial.Contains("C# acordou"), rcs.Serial);

        // ================= Assembly =================
        //
        // O classico: PORTB bit 5 e o pino 13 do Uno. Se a tabela de
        // portas errar, o LED do pisca-pisca mais copiado do mundo nao
        // acende -- e e exatamente por isso que ela e testada.
        string asm = string.Join(Environment.NewLine,
            ".equ LED = 5",
            "",
            "    ldi r16, (1<<LED)",
            "    out DDRB, r16",
            "",
            "loop:",
            "    sbi PORTB, LED",
            "    rcall espera",
            "    cbi PORTB, LED",
            "    rcall espera",
            "    rjmp loop",
            "",
            "espera:",
            "    ldi r17, 20",
            "volta:",
            "    dec r17",
            "    brne volta",
            "    ret");

        Conferir("assembly: o detector reconhece",
            Compilador.Detectar(asm) == Linguagem.Assembly,
            Compilador.Detectar(asm).ToString());

        var montado = Montador.Juntar(asm);
        saida.AppendLine($"        assembly: {montado.Palavras} palavras "
                         + $"({montado.Bytes} bytes), {montado.Rotulos.Count} rotulos");

        Conferir("assembly: os rotulos foram achados",
            montado.Rotulos.ContainsKey("loop") && montado.Rotulos.ContainsKey("espera")
            && montado.Rotulos.ContainsKey("volta"));

        Conferir("assembly: o .equ virou simbolo",
            montado.Simbolos.ContainsKey("LED"), string.Join(",", montado.Simbolos.Keys));

        var ra = Rodar(asm, 13, voltas: 3);
        saida.AppendLine($"        assembly: pino 13 mexeu {ra.Mexidas} vez(es)");

        Conferir("assembly: o SBI PORTB,5 acende o pino 13 do Uno",
            ra.Mexidas >= 2, ra.Mexidas.ToString());

        // As contas e as flags, que e onde um executor erra calado.
        var contas = Montador.Juntar(string.Join(Environment.NewLine,
            "    ldi r16, 10",
            "    ldi r17, 3",
            "    add r16, r17",
            "    ldi r18, 13",
            "    cp  r16, r18",
            "    brne erro",
            "    ldi r19, 1",
            "    rjmp fim",
            "erro:",
            "    ldi r19, 99",
            "fim:",
            "    nop"));

        var maqConta = new Maquina();
        var executor = new Avr(contas, maqConta);
        executor.Reiniciar();
        executor.Passo(200);

        saida.AppendLine($"        assembly: r16={executor.Registro(16)} "
                         + $"r19={executor.Registro(19)} | {executor.Flags}");

        Conferir("assembly: 10 + 3 da 13", executor.Registro(16) == 13,
            executor.Registro(16).ToString());

        Conferir("assembly: o CP com BRNE desvia certo",
            executor.Registro(19) == 1, executor.Registro(19).ToString());

        // ---- os opcodes sao os de verdade ----
        //
        // E a diferenca entre simular Assembly e MONTAR Assembly. Se o
        // binario aqui nao for o mesmo que o avr-as produz, "funcionou
        // no simulador" nao vale nada na placa.
        foreach (var (fonte, esperado, porque) in new[]
                 {
                     ("nop", 0x0000, "nop e a palavra zero"),
                     ("ldi r16, 0xFF", 0xEF0F, "ldi r16, 0xFF"),
                     ("ldi r17, 1", 0xE011, "ldi r17, 1"),
                     ("out 0x04, r16", 0xB904, "out DDRB, r16"),
                     ("sbi 0x05, 5", 0x9A2D, "sbi PORTB, 5"),
                     ("cbi 0x05, 5", 0x982D, "cbi PORTB, 5"),
                     ("add r16, r17", 0x0F01, "add r16, r17"),
                     ("dec r17", 0x951A, "dec r17"),
                     ("ret", 0x9508, "ret"),
                     ("sei", 0x9478, "sei"),
                     ("cli", 0x94F8, "cli"),
                     ("push r16", 0x930F, "push r16"),
                     ("pop r16", 0x910F, "pop r16"),
                     ("com r16", 0x9500, "com r16"),
                     ("mul r16, r17", 0x9F01, "mul r16, r17"),
                     ("adiw r24, 1", 0x9601, "adiw r24, 1"),
                     ("clr r16", 0x2700, "clr vira eor r16, r16"),
                     ("lsl r16", 0x0F00, "lsl vira add r16, r16"),
                 })
        {
            var so = Montador.Juntar(fonte);
            int saiu = so.Palavras > 0 ? so.Flash[0] : -1;

            Conferir($"opcode: {porque}", saiu == esperado,
                     $"saiu {saiu:X4}, esperava {esperado:X4}");
        }

        // As de duas palavras: errar o tamanho desalinha tudo o que
        // vem depois, e um salto cai no meio de outra instrucao.
        foreach (var (fonte, palavras, porque) in new[]
                 {
                     ("lds r16, 0x0100", 2, "lds ocupa duas palavras"),
                     ("sts 0x0100, r16", 2, "sts ocupa duas palavras"),
                     ("jmp 0x100", 2, "jmp ocupa duas palavras"),
                     ("call 0x100", 2, "call ocupa duas palavras"),
                     ("rjmp 0", 1, "rjmp ocupa uma"),
                 })
            Conferir($"opcode: {porque}",
                     Montador.Juntar(fonte).Palavras == palavras,
                     Montador.Juntar(fonte).Palavras.ToString());

        // O rotulo vira deslocamento RELATIVO, em palavras, a partir da
        // instrucao seguinte -- e nao o endereco absoluto.
        var comSalto = Montador.Juntar(string.Join(Environment.NewLine,
            "inicio:", "    nop", "    nop", "    rjmp inicio"));

        int rjmp = comSalto.Flash[2];
        int deslocamento = (rjmp & 0x0FFF) >= 0x800 ? (rjmp & 0x0FFF) - 0x1000 : rjmp & 0x0FFF;

        saida.AppendLine($"        rjmp para tras: {rjmp:X4}, deslocamento {deslocamento}");

        Conferir("opcode: o rjmp para tras conta certo",
            (rjmp & 0xF000) == 0xC000 && deslocamento == -3, deslocamento.ToString());

        // ---- os ciclos sao os do datasheet ----
        //
        // E ISTO que separa um simulador util de um brinquedo. Um laco
        // de espera calibrado com a contagem errada rega a horta pela
        // metade do tempo -- e o defeito so aparece no campo.
        foreach (var (fonte, ciclos, porque) in new[]
                 {
                     ("nop", 1L, "nop custa 1 ciclo"),
                     ("ldi r16, 5", 1L, "ldi custa 1"),
                     ("push r16", 2L, "push custa 2"),
                     ("adiw r24, 1", 2L, "adiw custa 2"),
                     ("mul r16, r17", 2L, "mul custa 2"),
                     ("sbi 0x05, 5", 2L, "sbi custa 2"),
                     ("lds r16, 0x0100", 2L, "lds custa 2"),
                     ("sts 0x0100, r16", 2L, "sts custa 2"),
                     ("jmp 0x0", 3L, "jmp custa 3"),
                 })
        {
            var so = new Avr(Montador.Juntar(fonte), new Maquina());
            so.Reiniciar();
            so.Passo(1);

            Conferir($"ciclos: {porque}", so.Ciclos == ciclos, so.Ciclos.ToString());
        }

        // Desvio custa 1 quando NAO desvia e 2 quando desvia. E a conta
        // que muda o tempo de todo laco de espera do mundo.
        var naoDesvia = new Avr(Montador.Juntar(string.Join(Environment.NewLine,
            "    ldi r16, 0", "    dec r16", "    breq fim", "fim:", "    nop")), new Maquina());
        naoDesvia.Reiniciar();
        naoDesvia.Passo(3);

        saida.AppendLine($"        breq sem desviar: {naoDesvia.Ciclos} ciclos");

        Conferir("ciclos: o desvio que NAO desvia custa 1",
            naoDesvia.Ciclos == 3, naoDesvia.Ciclos.ToString());

        var desvia = new Avr(Montador.Juntar(string.Join(Environment.NewLine,
            "    ldi r16, 1", "    dec r16", "    breq fim", "    nop", "fim:", "    nop")),
            new Maquina());
        desvia.Reiniciar();
        desvia.Passo(4);

        saida.AppendLine($"        breq desviando: {desvia.Ciclos} ciclos");

        Conferir("ciclos: o desvio que desvia custa 2",
            desvia.Ciclos == 4, desvia.Ciclos.ToString());

        // rcall custa 3 e ret custa 4: sao 7 ciclos por chamada, e nao
        // 2. Quem contar 2 erra o tempo em 350%.
        var chamada = new Avr(Montador.Juntar(string.Join(Environment.NewLine,
            "    rcall sub", "    nop", "sub:", "    ret")), new Maquina());
        chamada.Reiniciar();

        // Ate o RET devolver o controle para depois do RCALL, que e a
        // palavra 1. Medir por numero fixo de ciclos mediria o que
        // roda DEPOIS da volta tambem.
        for (int i = 0; i < 50 && chamada.Pc != 1; i++) chamada.Passo(1);

        saida.AppendLine($"        rcall + ret: {chamada.Ciclos} ciclos");

        Conferir("ciclos: rcall custa 3 e ret custa 4",
            chamada.Ciclos == 7, chamada.Ciclos.ToString());

        // ---- o tempo de verdade ----
        //
        // O laco classico de tres instrucoes: dec, brne. Sao 3 ciclos
        // por volta (1 do dec + 2 do brne que desvia), e a ultima volta
        // custa 2. A 16 MHz, 256 voltas dao 47,9 us.
        var laco = new Avr(Montador.Juntar(string.Join(Environment.NewLine,
            "    ldi r16, 0",
            "volta:",
            "    dec r16",
            "    brne volta",
            "fim:",
            "    rjmp fim")), new Maquina());

        laco.Reiniciar();

        // Ate o laco terminar, e nao um numero fixo de ciclos: depois
        // dele o programa fica girando no "fim", e medir o total
        // mediria o giro, e nao o laco.
        int alvoDoFim = Montador.Juntar(string.Join(Environment.NewLine,
            "    ldi r16, 0",
            "volta:",
            "    dec r16",
            "    brne volta",
            "fim:",
            "    rjmp fim")).Rotulos["fim"];

        for (int i = 0; i < 5000 && laco.Pc != alvoDoFim; i++) laco.Passo(1);

        // 1 (ldi) + 256*3 - 1 (a ultima volta nao desvia) = 768
        saida.AppendLine($"        laco de 256 voltas: {laco.Ciclos} ciclos, "
                         + $"{laco.Microssegundos:0.0} us");

        Conferir("ciclos: o laco de 256 voltas custa 768 ciclos",
            laco.Ciclos >= 768 && laco.Ciclos <= 772, laco.Ciclos.ToString());

        Conferir("tempo: 768 ciclos a 16 MHz dao 48 microssegundos",
            Math.Abs(laco.Microssegundos - 48.0) < 1.0,
            laco.Microssegundos.ToString("0.0"));

        // ---- a pilha e a SRAM sao de verdade ----
        var pilha = new Avr(Montador.Juntar(string.Join(Environment.NewLine,
            "    ldi r16, 0xAB",
            "    push r16",
            "    ldi r16, 0x00",
            "    pop r17",
            "    sts 0x0200, r17",
            "    lds r18, 0x0200")), new Maquina());

        pilha.Reiniciar();
        pilha.Passo(100);

        saida.AppendLine($"        pilha: r17={pilha.Registro(17):X2} "
                         + $"r18={pilha.Registro(18):X2} sram[0x200]={pilha.Memoria(0x200):X2}");

        Conferir("assembly: push e pop passam pela pilha de verdade",
            pilha.Registro(17) == 0xAB, pilha.Registro(17).ToString("X2"));

        Conferir("assembly: sts e lds passam pela SRAM de verdade",
            pilha.Memoria(0x200) == 0xAB && pilha.Registro(18) == 0xAB,
            pilha.Memoria(0x200).ToString("X2"));

        // ---- o .hex e gravavel ----
        string hex = Montador.Juntar(string.Join(Environment.NewLine,
            "    ldi r16, 0xFF", "    out 0x04, r16", "fim:", "    rjmp fim")).Hex();

        saida.AppendLine("        --- o .hex ---");
        foreach (string l in hex.Replace(Environment.NewLine, "\n").Split('\n'))
            if (l.Trim().Length > 0) saida.AppendLine("        " + l);

        Conferir("hex: comeca com dois-pontos e termina com o fim de arquivo",
            hex.TrimStart().StartsWith(":") && hex.Contains(":00000001FF"), hex);

        Conferir("hex: a soma de verificacao fecha", SomaDoHexFecha(hex), hex);

        // ---- as bandeiras se apagam escrevendo 1 ----
        //
        // E o que o datasheet manda, e e contra-intuitivo: quem espera o
        // TOV1 e depois faz "out TIFR1, r16" com r16=1 quer APAGAR a
        // bandeira. Gravar o 1 a deixaria ligada para sempre.
        var bandeiras = new Avr(Montador.Juntar(string.Join(Environment.NewLine,
            "    ldi r16, 0x01",
            "    out TIFR1, r16")), new Maquina { Perfil = PerfilPlaca.Uno });

        bandeiras.Reiniciar();
        bandeiras.TesteLigarBandeira(AvrIsa.TIFR1, 0);
        bandeiras.TesteLigarBandeira(AvrIsa.TIFR1, 1);
        bandeiras.Passo(10);

        saida.AppendLine($"        TIFR1 depois de escrever 1 no bit 0: "
                         + $"{Convert.ToString(bandeiras.TesteES(AvrIsa.TIFR1), 2).PadLeft(8, '0')}");

        Conferir("fidelidade: escrever 1 no TIFR APAGA a bandeira",
            (bandeiras.TesteES(AvrIsa.TIFR1) & 1) == 0);

        Conferir("fidelidade: e deixa as outras como estavam",
            (bandeiras.TesteES(AvrIsa.TIFR1) & 2) != 0);

        // No 328P o SBI mexe SO no bit pedido, inclusive nas bandeiras.
        var soUmBit = new Avr(Montador.Juntar("    sbi TIFR1, 0"),
                              new Maquina { Perfil = PerfilPlaca.Uno });

        soUmBit.Reiniciar();
        soUmBit.TesteLigarBandeira(AvrIsa.TIFR1, 0);
        soUmBit.TesteLigarBandeira(AvrIsa.TIFR1, 1);
        soUmBit.Passo(5);

        Conferir("fidelidade: SBI numa bandeira apaga so aquele bit",
            (soUmBit.TesteES(AvrIsa.TIFR1) & 1) == 0 && (soUmBit.TesteES(AvrIsa.TIFR1) & 2) != 0,
            Convert.ToString(soUmBit.TesteES(AvrIsa.TIFR1), 2));

        // ---- o TEMP dos 16 bits ----
        //
        // Escreve o alto primeiro, depois o baixo -- e le o baixo
        // primeiro, depois o alto. E a ordem do datasheet, e e o que
        // garante que os dois bytes sao da mesma contagem.
        var dezesseis = new Avr(Montador.Juntar(string.Join(Environment.NewLine,
            "    ldi r16, 0x12",
            "    sts OCR1AH, r16",
            "    ldi r16, 0x34",
            "    sts OCR1AL, r16",
            "    lds r20, OCR1AL",
            "    lds r21, OCR1AH")), new Maquina { Perfil = PerfilPlaca.Uno });

        dezesseis.Reiniciar();
        dezesseis.Passo(40);

        int lido = dezesseis.Registro(20) | (dezesseis.Registro(21) << 8);
        saida.AppendLine($"        OCR1A escrito 0x1234, lido 0x{lido:X4}");

        Conferir("fidelidade: o registrador de 16 bits passa pelo TEMP",
            lido == 0x1234, lido.ToString("X4"));

        // ---- a interrupcao do timer de verdade ----
        //
        // Timer1 sem prescaler, estouro liberado, sei -- e a ISR incrementa
        // um contador. E o que um sistema de irrigacao usa para marcar
        // tempo sem travar o programa num laco de espera.
        var comIsr = new Avr(Montador.Juntar(string.Join(Environment.NewLine,
            "    rjmp inicio",
            ".org OVF1addr",
            "    rjmp estourou",
            "inicio:",
            "    ldi r16, high(RAMEND)",
            "    out SPH, r16",
            "    ldi r16, low(RAMEND)",
            "    out SPL, r16",
            "    ldi r16, 1",
            "    sts TIMSK1, r16",
            "    ldi r16, 1",
            "    sts TCCR1B, r16",
            "    clr r25",
            "    sei",
            "espera:",
            "    rjmp espera",
            "estourou:",
            "    inc r25",
            "    reti")), new Maquina { Perfil = PerfilPlaca.Uno });

        comIsr.Reiniciar();

        // Tres estouros do Timer1 sem prescaler: 3 x 65536 ciclos.
        comIsr.Passo(3 * 65536 + 500);

        saida.AppendLine($"        timer1: {comIsr.Registro(25)} estouro(s) em "
                         + $"{comIsr.Ciclos} ciclos ({comIsr.Microssegundos / 1000:0.00} ms)");

        Conferir("fidelidade: a ISR do Timer1 roda a cada estouro",
            comIsr.Registro(25) == 3, comIsr.Registro(25).ToString());

        Conferir("fidelidade: o RETI religa as interrupcoes",
            comIsr.Flags.EndsWith("I"), comIsr.Flags);

        // ---- o atraso do demo e o que o comentario diz ----
        //
        // O demo promete 244,8 ms por espera. Contado ciclo a ciclo sao
        // 3.916.867 -- e se o executor cobrar um ciclo a menos no brne
        // que desvia, a conta cai para 2,6 milhoes e o LED pisca 50% mais
        // rapido que na placa. Este teste e o que impede isso.
        // Passo a passo, para parar EXATAMENTE quando o RET devolve o
        // controle para a palavra 1, e nao uma leva inteira depois.
        var conta = new Avr(Montador.Juntar(string.Join(Environment.NewLine,
            "    rcall espera", "fim:", "    rjmp fim",
            "espera:", "    ldi r20, 20", "nivel3:", "    ldi r18, 255",
            "nivel2:", "    ldi r17, 255", "nivel1:", "    dec r17",
            "    brne nivel1", "    dec r18", "    brne nivel2",
            "    dec r20", "    brne nivel3", "    ret")),
            new Maquina { Perfil = PerfilPlaca.Uno });

        conta.Reiniciar();
        while (conta.Pc != 1 && conta.Ciclos < 5_000_000) conta.Passo(1);

        double msDaEspera = conta.Ciclos * AvrIsa.NanosPorCiclo / 1_000_000.0;
        saida.AppendLine($"        rcall espera: {conta.Ciclos:N0} ciclos = {msDaEspera:0.0} ms");

        Conferir("tempo: a espera do demo custa 3.916.867 ciclos",
            conta.Ciclos == 3_916_867, conta.Ciclos.ToString("N0"));

        Conferir("tempo: que a 16 MHz sao 244,8 ms",
            Math.Abs(msDaEspera - 244.8) < 0.1, msDaEspera.ToString("0.00"));

        // ---- a tabela das bandeiras ----
        //
        // Cada linha e um trecho curto e o SREG que o manual da Atmel diz
        // que ele deixa, calculado a mao bit a bit (I T H S V N Z C). E a
        // rede de seguranca de qualquer mudanca no executor: um desvio
        // condicional le essas bandeiras, e uma so errada manda o programa
        // para o lado errado sem erro nenhum na tela.
        foreach (var (codigo, esperado, porque) in new (string, int, string)[]
                 {
                     ("ldi r16,0x7F|ldi r17,1|add r16,r17", 0x2C,
                      "add 7F+1: estouro com sinal, H, N"),
                     ("ldi r16,0xFF|ldi r17,1|add r16,r17", 0x23,
                      "add FF+1: zera, carry, H"),
                     ("ldi r16,0|ldi r17,1|sub r16,r17", 0x35,
                      "sub 0-1: empresta, fica negativo"),
                     ("ldi r16,0x80|ldi r17,1|sub r16,r17", 0x38,
                      "sub 80-1: estouro com sinal"),
                     ("ldi r16,1|dec r16", 0x02,
                      "dec 1: zera"),
                     ("ldi r16,0x80|dec r16", 0x18,
                      "dec 80: estouro com sinal"),
                     ("sec|ldi r16,5|dec r16", 0x01,
                      "dec NAO mexe no carry"),
                     ("ldi r16,0x81|lsr r16", 0x19,
                      "lsr 81: carry e V = N xor C"),
                     ("sec|ldi r16,0x0F|ldi r17,0xF0|and r16,r17", 0x03,
                      "and: zera, e o carry fica"),
                     ("ldi r16,0x55|com r16", 0x15,
                      "com: carry sempre 1"),
                     ("ldi r16,0x80|neg r16", 0x0D,
                      "neg 80: continua 80, com estouro"),
                     ("ldi r16,0|ldi r17,1|ldi r18,0|ldi r19,1|cp r16,r18|cpc r17,r19", 0x02,
                      "cp+cpc: 16 bits iguais dao Z"),
                     ("ldi r16,0|ldi r17,1|ldi r18,1|ldi r19,0|cp r16,r18|cpc r17,r19", 0x00,
                      "cp+cpc: o Z do byte de baixo nao se perde"),
                     ("ldi r24,0xFF|ldi r25,0xFF|adiw r24,1", 0x03,
                      "adiw FFFF+1: zera, carry"),
                     ("ldi r24,0|ldi r25,0|sbiw r24,1", 0x15,
                      "sbiw 0-1: FFFF, carry, negativo"),
                     ("ldi r16,3|ldi r17,4|mul r16,r17", 0x00,
                      "mul 3x4: sem carry, sem zero"),
                     ("ldi r16,0x7F|inc r16", 0x0C,
                      "inc 7F: estouro com sinal"),
                     ("sec|ldi r16,1|ror r16", 0x15,
                      "ror: o carry entra em cima"),
                     ("ldi r16,0x80|asr r16", 0x0C,
                      "asr: o sinal se repete"),
                 })
        {
            var trecho = new Avr(Montador.Juntar(codigo.Replace("|", Environment.NewLine)),
                                 new Maquina { Perfil = PerfilPlaca.Uno });
            trecho.Reiniciar();
            trecho.Passo(40);

            Conferir($"sreg: {porque}", trecho.TesteSreg == esperado,
                     $"saiu {Convert.ToString(trecho.TesteSreg, 2).PadLeft(8, '0')}, "
                     + $"esperava {Convert.ToString(esperado, 2).PadLeft(8, '0')}");
        }

        var pula = new Avr(Montador.Juntar(string.Join(Environment.NewLine,
            "ldi r16,0x10", "ldi r17,0x10", "cpse r16,r17", "ldi r20,1", "ldi r21,2")),
            new Maquina { Perfil = PerfilPlaca.Uno });
        pula.Reiniciar();
        pula.Passo(40);

        Conferir("sreg: cpse pula a instrucao quando sao iguais",
            pula.Registro(20) == 0 && pula.Registro(21) == 2,
            $"r20={pula.Registro(20)} r21={pula.Registro(21)}");

        // ---- a velocidade do executor ----
        //
        // O chip roda a 16 MHz. Se o executor nao passar disso, o
        // programa em Assembly anda mais DEVAGAR que na placa -- e foi o
        // que a primeira versao fazia: rodava o ATmega328P a pouco mais
        // da metade da velocidade real, e um LED de 250 ms piscava a cada
        // 440. Aqui se mede quantos milhoes de ciclos por segundo este
        // computador consegue executar.
        var veloz = new Avr(Montador.Juntar(string.Join(Environment.NewLine,
            "volta:",
            "    ldi r17, 255",
            "dentro:",
            "    dec r17",
            "    brne dentro",
            "    rjmp volta")), new Maquina { Perfil = PerfilPlaca.Uno });

        veloz.Reiniciar();
        veloz.Passo(200_000);    // aquece o JIT

        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        long antes = veloz.Ciclos;
        veloz.Passo(16_000_000);
        cronometro.Stop();

        double megahertz = (veloz.Ciclos - antes) / cronometro.Elapsed.TotalSeconds / 1_000_000.0;
        saida.AppendLine($"        executor: {megahertz:0} MHz efetivos neste computador "
                         + $"({megahertz / 16.0:0.0}x o ATmega328P)");

        // O piso e baixo de proposito: medir velocidade num teste e medir
        // a maquina, e numa maquina ocupada ele cairia sem nada estar
        // errado. O que ele pega e a regressao grande -- o executor
        // voltando a nao dar conta do chip.
        Conferir("velocidade: o executor passa dos 16 MHz do chip",
            megahertz >= 16, megahertz.ToString("0.0"));

        // ---- o Assembly so roda no chip em que foi validado ----
        //
        // Numa ESP32 o conjunto de instrucoes nem e AVR, e no ATtiny85
        // o PORTB mora noutro endereco. Rodar mesmo assim seria o
        // defeito que um simulador nao pode ter: funcionar na tela e
        // nao funcionar na placa.
        foreach (var (placa, deveRodar) in new[]
                 {
                     (PerfilPlaca.Uno, true),
                     (PerfilPlaca.Nano, true),
                     (PerfilPlaca.Esp32, false),
                     (PerfilPlaca.ATtiny85, false),
                     (PerfilPlaca.Mega, false),
                     (PerfilPlaca.PiPico, false),
                 })
        {
            string erro = null;

            try { Rodar(asm, 13, voltas: 1, placa: placa); }
            catch (ErroDeExecucao e) { erro = e.Message; }

            if (!deveRodar && erro is not null)
                saida.AppendLine($"        {placa.Nome}: {erro}");

            Conferir(deveRodar
                         ? $"assembly: roda na {placa.Nome}"
                         : $"assembly: recusa na {placa.Nome}, dizendo por que",
                deveRodar ? erro is null : erro?.Contains("ATmega328P") == true,
                erro ?? "rodou");
        }

        // ================= todas juntas =================
        //
        // A regra que nao pode quebrar: um sketch de Arduino continua
        // sendo lido como Arduino. Linguagem nova que rouba sketch
        // antigo e pior do que linguagem nova nenhuma.
        Conferir("linguagens: um sketch de Arduino continua sendo Arduino",
            Compilador.Detectar(string.Join(Environment.NewLine,
                "void setup() { pinMode(13, OUTPUT); }",
                "void loop() { digitalWrite(13, HIGH); delay(500); }")) == Linguagem.Arduino);

        Conferir("linguagens: um sketch em MicroPython continua sendo MicroPython",
            Compilador.Detectar(string.Join(Environment.NewLine,
                "from machine import Pin",
                "led = Pin(13, Pin.OUT)",
                "while True:",
                "    led.value(1)")) == Linguagem.MicroPython);
    }

    /// <summary>
    /// A bateria: toda placa, todo componente e toda linguagem.
    ///
    /// Sao mais de cem projetos, gerados por ferramentas/bateria.py.
    /// Cada um abre, compila e roda de verdade -- e o que se confere e
    /// o que um aluno notaria: o circuito montou, o programa compilou,
    /// a simulacao andou e algum pino mexeu.
    ///
    /// A bateria fica na pasta do projeto porque ela precisa rodar em
    /// qualquer maquina, e nao so onde o pendrive esta espetado. A
    /// copia do pendrive e um espelho dela.
    /// </summary>
    static void Bateria_Completa()
    {
        saida.AppendLine("bateria completa");

        string pasta = AcharABateria();

        if (pasta is null)
        {
            saida.AppendLine("        a pasta da bateria nao esta aqui; pulando");
            Conferir("bateria: a pasta existe", false, "nao achei bateria/");
            return;
        }

        saida.AppendLine("        pasta: " + pasta);

        foreach (string grupo in new[] { "placas", "componentes", "linguagens", "energia" })
        {
            string caminho = System.IO.Path.Combine(pasta, grupo);

            if (!System.IO.Directory.Exists(caminho))
            {
                Conferir($"bateria: a pasta {grupo} existe", false, caminho);
                continue;
            }

            var arquivos = System.IO.Directory.GetFiles(caminho, "*.sktch")
                                              .OrderBy(a => a).ToList();

            saida.AppendLine($"        --- {grupo}: {arquivos.Count} projetos ---");

            int bons = 0;
            var ruins = new List<string>();

            foreach (string arquivo in arquivos)
            {
                string nome = System.IO.Path.GetFileNameWithoutExtension(arquivo);
                string queixa = RodarDaBateria(arquivo);

                if (queixa is null) bons++;
                else ruins.Add($"{nome}: {queixa}");
            }

            saida.AppendLine($"        {bons} de {arquivos.Count} passaram");

            foreach (string queixa in ruins.Take(12))
                saida.AppendLine("        !! " + queixa);

            Conferir($"bateria: todos os {grupo} passam",
                     ruins.Count == 0,
                     ruins.Count == 0 ? "" : string.Join(" | ", ruins.Take(6)));
        }
    }

    /// <summary>
    /// Desfazer e refazer, passando pelos recursos novos.
    ///
    /// E onde recurso novo costuma escapar: o menu da aba Componentes
    /// mexe no fio direto, e se ele nao passasse pelo historico o Ctrl+Z
    /// simplesmente nao voltaria -- sem erro nenhum, so com a pessoa
    /// perdendo o que fez.
    /// </summary>
    static void Programa_DesfazerERefazer()
    {
        saida.AppendLine("desfazer e refazer");

        var c = new Circuito();
        c.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 0, Y = 0 });
        c.Pecas.Add(new PecaNoCircuito { Id = "led1", Tipo = "led", X = 400, Y = 100 });
        c.Fios.Add(new Fio
        {
            DePeca = "esp", DePino = "D2", ParaPeca = "led1", ParaPino = "A", Cor = "#00A651",
        });

        var janela = new MainWindow();
        janela.TesteAbrir(c, "void setup() {}" + Environment.NewLine + "void loop() {}");

        int pecasNoComeco = janela.TesteCircuito.Pecas.Count;

        // ---- peca nova ----
        janela.TesteAdicionarPeca("buzzer");
        int comABuzina = janela.TesteCircuito.Pecas.Count;

        janela.TesteDesfazer();
        int depoisDeDesfazer = janela.TesteCircuito.Pecas.Count;

        janela.TesteRefazer();
        int depoisDeRefazer = janela.TesteCircuito.Pecas.Count;

        saida.AppendLine($"        pecas: {pecasNoComeco} -> {comABuzina} -> desfaz {depoisDeDesfazer}"
                         + $" -> refaz {depoisDeRefazer}");

        Conferir("desfazer: tira a peca que acabou de entrar",
            comABuzina == pecasNoComeco + 1 && depoisDeDesfazer == pecasNoComeco);

        Conferir("refazer: devolve a peca", depoisDeRefazer == comABuzina);

        // ---- o menu da aba Componentes passa pelo historico ----
        janela.TesteTrocarPinoDoFio(0, "D4");
        string pinoTrocado = janela.TesteCircuito.Fios[0].DePino;

        janela.TesteDesfazer();
        string pinoDesfeito = janela.TesteCircuito.Fios[0].DePino;

        saida.AppendLine($"        pino pelo menu: D2 -> {pinoTrocado} -> desfaz {pinoDesfeito}");

        Conferir("desfazer: volta a troca de pino feita pelo menu da ligacao",
            pinoTrocado == "D4" && pinoDesfeito == "D2", pinoDesfeito);

        janela.TesteApagarFio(0);
        int fiosDepoisDeApagar = janela.TesteCircuito.Fios.Count;

        janela.TesteDesfazer();
        int fiosDepoisDeDesfazer = janela.TesteCircuito.Fios.Count;

        Conferir("desfazer: devolve o fio apagado pelo menu da ligacao",
            fiosDepoisDeApagar == 0 && fiosDepoisDeDesfazer == 1,
            $"{fiosDepoisDeApagar} -> {fiosDepoisDeDesfazer}");

        janela.TestePintarFio(0, "#D6202A");
        janela.TesteDesfazer();

        Conferir("desfazer: volta a cor trocada pelo menu",
            janela.TesteCircuito.Fios[0].Cor == "#00A651", janela.TesteCircuito.Fios[0].Cor);

        // ---- nada para desfazer nao estoura ----
        for (int i = 0; i < 50; i++) janela.TesteDesfazer();

        // O comeco do historico e o projeto aberto -- nunca o circuito
        // que estava na tela antes de abrir. Foi este o defeito que o
        // teste achou: o Ctrl+Z passava do comeco e trazia de volta as
        // pecas do circuito anterior, com o codigo do novo.
        Conferir("desfazer: para no projeto aberto, sem voltar ao circuito anterior",
            janela.TesteCircuito.Pecas.Count == pecasNoComeco
            && janela.TesteCircuito.Pecas.All(p => p.Id is "esp" or "led1"),
            string.Join(", ", janela.TesteCircuito.Pecas.Select(p => p.Id)));

        for (int i = 0; i < 50; i++) janela.TesteRefazer();

        Conferir("desfazer: apertar sem ter o que desfazer nao estoura",
            janela.TesteCircuito.Pecas.Count >= pecasNoComeco);

        // Abrir outro projeto recomeca o historico: nada do de antes volta.
        var outro = new Circuito();
        outro.Pecas.Add(new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 0, Y = 0 });
        janela.TesteAbrir(outro, "void setup() {}" + Environment.NewLine + "void loop() {}");

        for (int i = 0; i < 10; i++) janela.TesteDesfazer();

        Conferir("desfazer: abrir outro projeto recomeca o historico",
            janela.TesteCircuito.Pecas.Count == 1 && janela.TesteCircuito.Pecas[0].Id == "uno",
            string.Join(", ", janela.TesteCircuito.Pecas.Select(p => p.Id)));

        janela.TesteAbrir(c, "void setup() {}" + Environment.NewLine + "void loop() {}");

        // ---- a lista de componentes acompanha o desfazer ----
        janela.TesteTrocarPinoDoFio(0, "D5");
        janela.TesteDesfazer();

        Conferir("desfazer: a aba Componentes mostra o pino de volta",
            !janela.TesteArvoreDeComponentes().Contains("D5 >"),
            janela.TesteArvoreDeComponentes());
    }

    /// <summary>
    /// Onde a bateria esta: na pasta do projeto, ou ao lado do programa.
    /// </summary>
    /// <summary>
    /// O tempo dos pinos como no chip de verdade: e o que o analisador
    /// logico mostra, e o que um protocolo feito na mao precisa.
    ///
    /// Os numeros saem do datasheet e do core de cada placa: digitalWrite
    /// de 3,6 us no Uno, PWM de 976,56 Hz nos pinos 5 e 6 e de 490 Hz nos
    /// outros, bit de 104 us a 9600 baud, pulso de servo a cada 20 ms.
    /// </summary>
    static void Tempo_ComoNoChip()
    {
        saida.AppendLine("tempo como no chip");

        (Maquina Maq, Interpretador Interp) Uno(string codigo)
        {
            var rede = new Rede { UsarInternet = false };
            return MontarPlaca(rede, "UNO", codigo, PerfilPlaca.Uno);
        }

        // Quantas subidas e quanto tempo alto, numa janela.
        static (int Subidas, double AltoMedio, double Periodo) Medir(List<(double Us, int Nivel)> onda)
        {
            var subidas = new List<double>();
            var altos = new List<double>();
            for (int k = 1; k < onda.Count; k++)
            {
                if (onda[k].Nivel == 1 && onda[k - 1].Nivel == 0) subidas.Add(onda[k].Us);
                if (onda[k].Nivel == 0 && onda[k - 1].Nivel == 1 && subidas.Count > 0)
                    altos.Add(onda[k].Us - subidas[^1]);
            }
            double periodo = subidas.Count > 1 ? (subidas[^1] - subidas[0]) / (subidas.Count - 1) : 0;
            return (subidas.Count, altos.Count > 0 ? altos.Average() : 0, periodo);
        }

        // ---- digitalWrite custa 3,6 us; o shiftOut, perto de cem ----
        {
            var (maq, interp) = Uno("""
                void setup() {
                  pinMode(13, OUTPUT); pinMode(11, OUTPUT); pinMode(12, OUTPUT);
                  digitalWrite(13, HIGH);
                  digitalWrite(13, LOW);
                  shiftOut(11, 12, MSBFIRST, 0xA5);
                }
                void loop() {}
                """);
            interp.Setup();

            var onda13 = maq.Linha.Onda(13, 0, 1000);
            double entre = onda13.Count >= 3 ? onda13[2].Us - onda13[1].Us : -1;
            var relogio = maq.Linha.Onda(12, 0, 1000);
            double shift = relogio.Count > 2 ? relogio[^1].Us - relogio[1].Us : -1;

            saida.AppendLine($"        uno: digitalWrite a digitalWrite {entre:0.00} us; "
                             + $"clock do shiftOut dura {shift:0} us, {relogio.Count - 1} bordas");
            Conferir("tempo: dois digitalWrite no Uno ficam 3,6 us um do outro (58 ciclos)",
                Math.Abs(entre - 3.625) < 0.01, $"{entre}");
            Conferir("tempo: o shiftOut de um byte leva uns 80 us no Uno, e nao zero",
                shift is > 60 and < 120 && relogio.Count - 1 == 16, $"{shift} us, {relogio.Count - 1} bordas");
        }

        // ---- delayMicroseconds e micros() de verdade ----
        {
            var (maq, interp) = Uno("""
                unsigned long a, b;
                void setup() {
                  Serial.begin(9600);
                  a = micros();
                  delayMicroseconds(250);
                  b = micros();
                  Serial.println(b - a);
                }
                void loop() {}
                """);
            interp.Setup();
            var linhas = LinhasDoSerial(maq);
            string medido = linhas.FirstOrDefault() ?? "";
            saida.AppendLine($"        delayMicroseconds(250) medido com micros(): {medido}");
            Conferir("tempo: delayMicroseconds(250) espera 250 us, e micros() ve",
                long.TryParse(medido, out long us) && us >= 250 && us < 260, medido);
        }

        // ---- tone com duracao para sozinho; a onda tem a frequencia ----
        {
            var (maq, interp) = Uno("""
                void setup() { tone(8, 1000, 200); }
                void loop() { delay(50); }
                """);
            interp.Setup();
            bool tocando = maq.Frequencia[8] == 1000;
            for (int i = 0; i < 6; i++) interp.Loop();

            var (subidas, alto, periodo) = Medir(maq.Linha.Onda(8, 0, 10_000));
            saida.AppendLine($"        tone 1 kHz: {subidas} subidas em 10 ms, periodo {periodo:0} us, "
                             + $"alto {alto:0} us; depois de 300 ms: {maq.Frequencia[8]} Hz");
            Conferir("tempo: tone(8, 1000, 200) toca e para sozinho depois de 200 ms",
                tocando && maq.Frequencia[8] == 0, $"agora {maq.Frequencia[8]} Hz");
            Conferir("tempo: a onda do tone e 1 kHz com metade alta",
                Math.Abs(periodo - 1000) < 1 && Math.Abs(alto - 500) < 1, $"{periodo} / {alto}");
            var depois = maq.Linha.Onda(8, 250_000, 260_000);
            Conferir("tempo: e depois dos 200 ms o pino fica parado",
                depois.Count == 1 && depois[0].Nivel == 0, $"{depois.Count} trechos");
        }

        // ---- PWM do Uno: 976,56 Hz no 5, 490 Hz no 9 ----
        {
            var (maq, interp) = Uno("""
                void setup() { analogWrite(5, 64); analogWrite(9, 192); }
                void loop() {}
                """);
            interp.Setup();
            var (_, alto5, periodo5) = Medir(maq.Linha.Onda(5, 0, 20_000));
            var (_, alto9, periodo9) = Medir(maq.Linha.Onda(9, 0, 20_000));
            saida.AppendLine($"        pwm pino 5: periodo {periodo5:0.0} us, alto {alto5:0.0}; "
                             + $"pino 9: periodo {periodo9:0.0} us, alto {alto9:0.0}");
            Conferir("tempo: PWM do pino 5 do Uno a 976,56 Hz (Timer0), 25% alto",
                Math.Abs(periodo5 - 1024) < 1 && Math.Abs(alto5 / periodo5 - 64 / 255.0) < 0.01,
                $"{periodo5} / {alto5}");
            Conferir("tempo: PWM do pino 9 a 490 Hz (Timer1), 75% alto",
                Math.Abs(periodo9 - 2040) < 1 && Math.Abs(alto9 / periodo9 - 192 / 255.0) < 0.01,
                $"{periodo9} / {alto9}");
        }

        // ---- ESP32: resolucao e ledc com frequencia ----
        {
            var rede = new Rede { UsarInternet = false };
            var (maq, interp) = MontarPlaca(rede, "ESP32", """
                void setup() {
                  analogWriteResolution(10);
                  analogWrite(2, 512);
                  ledcAttach(4, 5000, 10);
                  ledcWrite(4, 256);
                }
                void loop() {}
                """);
            interp.Setup();
            var (_, alto, periodo) = Medir(maq.Linha.Onda(4, 0, 2000));
            saida.AppendLine($"        esp32: analogWrite(2, 512) em 10 bits = {maq.Pwm[2]}/255; "
                             + $"ledc 5 kHz periodo {periodo:0.0} us, alto {alto:0.0}");
            Conferir("tempo: analogWriteResolution(10) faz 512 ser metade",
                Math.Abs(maq.Pwm[2] - 128) <= 1, maq.Pwm[2].ToString());
            Conferir("tempo: ledcAttach(pino, 5000, 10) e ledcWrite(256): 5 kHz com um quarto alto",
                Math.Abs(periodo - 200) < 0.5 && Math.Abs(alto - 50) < 1, $"{periodo} / {alto}");
        }

        // ---- a serial sai pelo pino TX, bit a bit ----
        {
            var (maq, interp) = Uno("""
                void setup() { Serial.begin(9600); Serial.print("A"); }
                void loop() {}
                """);
            interp.Setup();

            var onda = maq.Linha.Onda(1, 0, 3000);
            double bit = 1e6 / 9600;
            double inicio = onda.Count > 1 ? onda[1].Us : double.NaN;

            // O nivel no meio de cada bit: inicio, 8 de dado (LSB primeiro), parada.
            int Nivel(double t)
            {
                int v = onda[0].Nivel;
                foreach (var (us, n) in onda) { if (us > t) break; v = n; }
                return v;
            }

            string bits = string.Concat(Enumerable.Range(0, 10).Select(k => Nivel(inicio + (k + 0.5) * bit)));
            saida.AppendLine($"        'A' a 9600 baud no pino 1: {bits} (bit de {bit:0.00} us)");
            Conferir("tempo: 'A' sai no TX como na placa: 0 10000010 1",
                bits == "0100000101", bits);
            Conferir("tempo: antes de mandar, a linha fica em repouso alta",
                onda.Count > 0 && onda[0].Nivel == 1, onda.Count > 0 ? onda[0].Nivel.ToString() : "vazia");
        }

        // ---- servo: um pulso a cada 20 ms, com a largura do angulo ----
        {
            var (maq, interp) = Uno("""
                #include <Servo.h>
                Servo s;
                void setup() { s.attach(10); s.write(90); }
                void loop() {}
                """);
            interp.Setup();
            var (_, alto, periodo) = Medir(maq.Linha.Onda(10, 0, 100_000));
            saida.AppendLine($"        servo em 90 graus: periodo {periodo:0} us, pulso {alto:0} us");
            Conferir("tempo: servo em 90 graus manda 1472 us a cada 20 ms, como a Servo.h",
                Math.Abs(periodo - 20000) < 1 && Math.Abs(alto - 1472) < 1, $"{periodo} / {alto}");
        }
    }

    /// <summary>
    /// Os treze temas: todos completos, todos legiveis, e a troca chega na
    /// janela inteira -- inclusive na de configuracoes, que esta aberta
    /// quando a pessoa escolhe.
    ///
    /// O contraste e medido pela conta da WCAG. Tema bonito com letra
    /// ilegivel nao passa: e o tipo de defeito que ninguem ve no editor de
    /// cores e todo mundo ve numa sala com projetor.
    /// </summary>
    static void Temas_TodosLegiveis()
    {
        saida.AppendLine("temas");

        string antes = Tema.Atual;
        var paletas = Tema.Paletas;
        saida.AppendLine("        " + string.Join(", ", paletas.Select(p => p.Nome)));

        Conferir("temas: os treze pedidos existem",
            paletas.Count == 13
            && new[] { "escuro", "claro", "arduino-ide", "arduino", "stm32", "raspberry-pi", "esp32",
                       "azul", "vermelho", "verde", "amarelo", "laranja", "rosa" }
                .All(id => Tema.De(id) is not null),
            string.Join(", ", paletas.Select(p => p.Id)));

        foreach (var p in paletas)
        {
            var faltam = Tema.Chaves.Where(ch => !p.Cores.ContainsKey(ch)).ToList();
            Conferir($"tema {p.Nome}: tem todas as cores", faltam.Count == 0, string.Join(", ", faltam));
            if (faltam.Count > 0) continue;

            double C(string a, string b) => Tema.Contraste(p.Cores[a], p.Cores[b]);

            var medidas = new (string O, double Valor, double Minimo)[]
            {
                ("texto no painel", C(Tema.Texto, Tema.Painel), 4.5),
                ("texto no fundo", C(Tema.Texto, Tema.Fundo), 4.5),
                ("texto no campo", C(Tema.Texto, Tema.Campo), 4.5),
                ("texto forte no painel", C(Tema.TextoForte, Tema.Painel), 7.0),
                ("texto fraco no painel", C(Tema.TextoFraco, Tema.Painel), 3.5),
                ("letra no destaque", C(Tema.TextoNoDestaque, Tema.Destaque), 4.5),
                ("texto na selecao", C(Tema.Texto, Tema.Selecao), 4.5),
                ("destaque no painel", C(Tema.Destaque, Tema.Painel), 3.0),
                ("aviso no painel", C(Tema.Aviso, Tema.Painel), 4.5),
                ("erro no campo", C(Tema.Erro, Tema.Campo), 4.5),
                ("serial no campo", C(Tema.Terminal, Tema.Campo), 4.5),
                ("rede no campo", C(Tema.TerminalRede, Tema.Campo), 4.5),
                ("bluetooth no campo", C(Tema.TerminalBt, Tema.Campo), 4.5),
            };

            var fracos = medidas.Where(m => m.Valor < m.Minimo)
                                .Select(m => $"{m.O} {m.Valor:0.0} (min {m.Minimo})").ToList();
            Conferir($"tema {p.Nome}: contraste legivel em tudo", fracos.Count == 0,
                string.Join("; ", fracos));
        }

        // ---- aplicar troca os recursos da janela, e grava ----
        Tema.Aplicar("rosa");
        var recurso = System.Windows.Application.Current.Resources[Tema.Destaque]
            as System.Windows.Media.SolidColorBrush;
        Conferir("temas: aplicar troca a cor que as janelas usam",
            recurso?.Color == Tema.De("rosa").Cores[Tema.Destaque], recurso?.Color.ToString());
        Conferir("temas: e fica gravado para a proxima vez",
            Preferencias.Texto(Preferencias.Tema) == "rosa", Preferencias.Texto(Preferencias.Tema));

        Conferir("temas: tema claro usa as cores de codigo claras",
            Realce.Do("stm32") == Realce.ArduinoClaro && Realce.Do("amarelo") == Realce.Arduino);

        // ---- a janela de configuracoes se repinta aberta ----
        var janela = new MainWindow();
        var config = janela.TesteCriarConfiguracoes();
        config.AbrirParaTeste("Aparência");
        var nomes = config.TemasNaTelaParaTeste;
        Conferir("temas: a pagina Aparencia mostra uma amostra de cada tema",
            nomes.Count == 13, string.Join(", ", nomes));

        Tema.Aplicar("stm32");
        EsperarPor(() => (config.Background as System.Windows.Media.SolidColorBrush)?.Color
                         == Tema.De("stm32").Cores[Tema.Fundo], 2000);
        Conferir("temas: escolher com as configuracoes abertas repinta a janela na hora",
            (config.Background as System.Windows.Media.SolidColorBrush)?.Color
                == Tema.De("stm32").Cores[Tema.Fundo]
            && config.AssuntoAbertoParaTeste == "Aparência",
            $"{(config.Background as System.Windows.Media.SolidColorBrush)?.Color} na pagina {config.AssuntoAbertoParaTeste}");
        config.Close();

        Tema.Aplicar(antes);
    }

    /// <summary>
    /// As duas demos da Alpha 19 que precisam de gente do outro lado:
    /// o Bluetooth (o celular manda comandos) e o ESP-NOW (duas placas, e
    /// os botoes de cada uma mexem na outra).
    ///
    /// Roda pela janela de verdade -- abrir pelo menu, rodar, conectar,
    /// apertar --, porque e assim que a turma vai usar. Uma demo que so
    /// passa no motor sozinho e so metade de uma demo.
    /// </summary>
    static void Demos_BluetoothEEspNow()
    {
        saida.AppendLine("demos de bluetooth e esp-now");

        static string TextoDoLcd(Lcd lcd)
            => lcd is null ? ""
               : string.Join(" | ", Enumerable.Range(0, lcd.Linhas).Select(i => lcd.LinhaTexto(i).TrimEnd()));

        // ---- Bluetooth: o celular manda, a placa obedece e responde ----
        {
            var janela = new MainWindow();
            janela.TesteAbrirDemo("esp32-bluetooth");
            janela.TesteRodar();

            try
            {
                var maq = janela.TesteMaquinaDa("placa");
                bool conectou = false;
                EsperarPor(() => (conectou = janela.TesteConectarCelular("ESP32-Sala")), 4000);
                if (!conectou)
                {
                    janela.TesteAtualizarPaineis();
                    saida.AppendLine($"        serial da placa: {string.Join(" / ", LinhasDoSerial(maq))}");
                    saida.AppendLine($"        painel bt: {janela.TesteTextoDoBluetooth.Replace(Environment.NewLine, " / ")}");
                    saida.AppendLine($"        avisos: {string.Join(" / ", maq.Avisos)}");
                }
                Conferir("bluetooth: o celular acha e conecta na ESP32-Sala", conectou);

                string Mandar(string comando, string esperado)
                {
                    janela.TesteEnviarPeloCelular(comando);
                    EsperarPor(() =>
                    {
                        janela.TesteAtualizarPaineis();
                        return janela.TesteTextoDoBluetooth.Contains(esperado);
                    }, 4000);
                    return janela.TesteTextoDoBluetooth;
                }

                string painel = Mandar("vermelho", "LED vermelho");
                Conferir("bluetooth: 'vermelho' responde e acende o vermelho",
                    painel.Contains("LED vermelho") && maq.Pwm[25] == 255 && maq.Pwm[26] == 0,
                    $"R={maq.Pwm[25]} G={maq.Pwm[26]} B={maq.Pwm[27]}");

                painel = Mandar("AZUL", "LED azul");
                Conferir("bluetooth: o comando vale em maiusculas tambem (toLowerCase)",
                    painel.Contains("LED azul") && maq.Pwm[27] == 255 && maq.Pwm[25] == 0,
                    $"R={maq.Pwm[25]} G={maq.Pwm[26]} B={maq.Pwm[27]}");

                painel = Mandar("temp", "umidade:");
                Conferir("bluetooth: 'temp' devolve temperatura e umidade do DHT22",
                    painel.Contains("temperatura:") && painel.Contains("umidade:"), painel);

                painel = Mandar("bip", "bip!");
                Conferir("bluetooth: 'bip' responde", painel.Contains("bip!"), painel);

                painel = Mandar("dancar", "Mande: ajuda");
                Conferir("bluetooth: comando desconhecido explica o que mandar",
                    painel.Contains("nao conheco 'dancar'"), painel);

                string lcd = TextoDoLcd(maq.Lcd);
                saida.AppendLine($"        lcd: {lcd}");
                Conferir("bluetooth: o LCD mostra o ultimo comando",
                    lcd.Contains("Ultimo comando:") && lcd.Contains("dancar"), lcd);
            }
            finally { janela.TesteParar(); }
        }

        // ---- ESP-NOW: duas placas, e os botoes de cada uma mexem na outra ----
        {
            var janela = new MainWindow();
            janela.TesteAbrirDemo("esp32-espnow");
            janela.TesteRodar();

            try
            {
                var sensor = janela.TesteMaquinaDa("sensor");
                var painel = janela.TesteMaquinaDa("painel");

                bool chegou = EsperarPor(() => TextoDoLcd(painel?.Lcd).Contains("Temp:"), 5000);
                string lcd = TextoDoLcd(painel?.Lcd);
                saida.AppendLine($"        lcd do painel: {lcd}");
                Conferir("esp-now: a leitura do sensor chega no LCD do painel", chegou, lcd);
                // A segunda linha vem logo depois da primeira: esperar por
                // ela, e nao julgar no meio da escrita (com o PC ocupado, o
                // teste pegava "Temp:" com a linha de baixo ainda vazia).
                bool comUmidade = EsperarPor(() => TextoDoLcd(painel?.Lcd).Contains("Umid:"), 3000);
                Conferir("esp-now: com a umidade junto", comUmidade, TextoDoLcd(painel?.Lcd));

                // O potenciometro de la vira o angulo do servo daqui.
                int pot = sensor.Analogico[34];
                int esperado = (int)Math.Round(pot * 180.0 / 4095);
                EsperarPor(() => Math.Abs(painel.AnguloServo[18] - esperado) <= 2, 3000);
                saida.AppendLine($"        potenciometro {pot} -> servo {painel.AnguloServo[18]} graus");
                Conferir("esp-now: o servo do painel segue o potenciometro do sensor",
                    Math.Abs(painel.AnguloServo[18] - esperado) <= 2,
                    $"pot={pot} servo={painel.AnguloServo[18]} esperado~{esperado}");

                var ed = janela.TesteEditorCircuito;
                System.Windows.Point Capa(string id)
                {
                    var bt = janela.TesteCircuito.Pecas.First(p => p.Id == id);
                    var capa = EditorCircuito.CapasDeBotao[bt.Tipo];
                    return new System.Windows.Point(bt.X + capa.X, bt.Y + capa.Y);
                }

                // Botao do SENSOR apertado: o LED do PAINEL acende.
                ed.Apertar(Capa("botao1"));
                bool acendeu = EsperarPor(() => painel.DigitalRead(19) == 1, 3000);
                ed.Soltar();
                bool apagou = EsperarPor(() => painel.DigitalRead(19) == 0, 3000);
                Conferir("esp-now: o botao do sensor acende o LED do painel", acendeu);
                Conferir("esp-now: e solto, apaga", apagou);

                // Botao do PAINEL: o LED do SENSOR inverte -- o caminho de volta.
                ed.Apertar(Capa("botao2"));
                bool voltou = EsperarPor(() => sensor.DigitalRead(27) == 1, 3000);
                ed.Soltar();
                Conferir("esp-now: o botao do painel acende o LED do sensor (o caminho de volta)",
                    voltou, $"LED do sensor = {sensor.DigitalRead(27)}");

                var serialDoSensor = LinhasDoSerial(sensor);
                Conferir("esp-now: difusao nunca e dada como perdida no callback",
                    !serialDoSensor.Any(l => l.Contains("nao ouviu")),
                    string.Join(" | ", serialDoSensor.TakeLast(4)));
            }
            finally { janela.TesteParar(); }
        }
    }

    /// <summary>
    /// Cada configuracao nova vale de verdade (Alpha 19): muda a
    /// preferencia, a janela aplica, e o programa faz diferente. Caixa que
    /// nao muda nada e pior do que caixa nenhuma.
    /// </summary>
    static void Configuracoes_ValemDeVerdade()
    {
        saida.AppendLine("configuracoes valem de verdade");
        Preferencias.Restaurar();

        var janela = new MainWindow();
        var codigo = janela.TesteEditorDeCodigo;
        string pasta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cs-cfg-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(pasta);

        void Definir(string chave, string valor)
        {
            Preferencias.Definir(chave, valor);
            janela.TesteAplicarPreferencias();
        }

        string Serial()
        {
            janela.TesteAtualizarPaineis();
            return janela.TesteSerial;
        }

        // Um circuito novo a cada abertura: a placa guarda o codigo dela, e
        // reabrir o MESMO objeto manteria o sketch anterior.
        static Circuito NovaEsp()
        {
            var c = new Circuito();
            c.Pecas.Add(new PecaNoCircuito { Id = "placa", Tipo = "esp32-devkit-v1", X = 0, Y = 0 });
            return c;
        }

        try
        {
            // ---- as paginas existem e montam ----
            var cfg = janela.TesteCriarConfiguracoes();
            var assuntos = cfg.AssuntosParaTeste;
            saida.AppendLine("        paginas: " + string.Join(" | ", assuntos));
            string falhou = null;
            foreach (string assunto in assuntos)
                try { cfg.AbrirParaTeste(assunto); }
                catch (Exception e) { falhou = $"{assunto}: {e.Message}"; }
            Conferir("configuracoes: as paginas novas estao na lista",
                new[] { "Editor de código", "Monitor serial", "Simulação", "Arquivos e projetos" }
                    .All(assuntos.Contains), string.Join("|", assuntos));
            Conferir("configuracoes: toda pagina monta sem erro", falhou is null, falhou);

            // ---- editor de codigo ----
            Definir(Preferencias.TamanhoDoCodigo, "16");
            Definir(Preferencias.NumerosDeLinha, "nao");
            Definir(Preferencias.QuebrarLinhas, "sim");
            Definir(Preferencias.TamanhoDoRecuo, "4");
            Definir(Preferencias.FonteDoCodigo, "Courier New");
            Conferir("editor: o tamanho da letra", codigo.FontSize == 16, codigo.FontSize.ToString());
            Conferir("editor: sem o numero das linhas", !codigo.ShowLineNumbers);
            Conferir("editor: quebra as linhas compridas", codigo.WordWrap);
            Conferir("editor: recuo de 4", codigo.Options.IndentationSize == 4,
                codigo.Options.IndentationSize.ToString());
            Conferir("editor: a fonte escolhida", codigo.FontFamily.Source == "Courier New",
                codigo.FontFamily.Source);

            codigo.Text = "x = f";
            codigo.CaretOffset = codigo.Text.Length;
            codigo.TextArea.PerformTextInput("(");
            bool fechou = codigo.Text == "x = f()" && codigo.CaretOffset == 6;
            codigo.TextArea.PerformTextInput(")");
            bool passou = codigo.Text == "x = f()" && codigo.CaretOffset == 7;
            Conferir("editor: o parentese fecha sozinho, com o cursor no meio", fechou, codigo.Text);
            Conferir("editor: digitar o fechamento passa por cima", passou, codigo.Text);

            Definir(Preferencias.FecharPares, "nao");
            codigo.Text = "x = f";
            codigo.CaretOffset = codigo.Text.Length;
            codigo.TextArea.PerformTextInput("(");
            Conferir("editor: desligado, o parentese nao fecha", codigo.Text == "x = f(", codigo.Text);

            // ---- monitor serial ----
            Definir(Preferencias.FimDeLinha, "crlf");
            janela.TesteAbrir(NovaEsp(), """
                void setup() { Serial.begin(115200); }
                void loop() {
                  if (Serial.available() >= 5) {
                    Serial.print("bytes:");
                    while (Serial.available()) { Serial.print(" "); Serial.print((int)Serial.read()); }
                    Serial.println();
                  }
                  delay(20);
                }
                """);
            janela.TesteRodar();
            EsperarPor(() => janela.TestePlacaRodando("placa"), 3000);
            EsperarPor(() => false, 300);
            janela.TesteDigitarNoSerial("abc");
            bool crlf = EsperarPor(() => Serial().Contains("bytes: 97 98 99 13 10"), 4000);
            Conferir("monitor: manda o fim de linha escolhido (CR+LF)", crlf, Serial());

            Definir(Preferencias.TempoNoSerial, "nao");
            string semTempo = Serial();
            Definir(Preferencias.TempoNoSerial, "sim");
            string comTempo = Serial();
            Conferir("monitor: sem a hora de cada linha",
                !semTempo.Contains("ms] ") && semTempo.Contains("bytes:"), semTempo);
            Conferir("monitor: e com ela de volta", comTempo.Contains("ms] "), comTempo);

            Definir(Preferencias.LimparSerialAoRodar, "nao");
            janela.TesteRodar();
            string guardou = Serial();
            Conferir("monitor: sem limpar ao rodar, a execucao anterior fica",
                guardou.Contains("bytes: 97") && guardou.Contains("rodando de novo"), guardou);
            Definir(Preferencias.LimparSerialAoRodar, "sim");
            janela.TesteRodar();
            Conferir("monitor: limpando ao rodar, comeca vazio", !Serial().Contains("bytes:"), Serial());

            Definir(Preferencias.LinhasNoSerial, "200");
            janela.TesteAbrir(NovaEsp(), """
                void setup() { Serial.begin(115200); for (int i = 0; i < 300; i++) Serial.println(i); }
                void loop() { delay(100); }
                """);
            janela.TesteRodar();
            EsperarPor(() => Serial().Contains("299"), 4000);
            int linhas = Serial().Split('\n').Length;
            Conferir("monitor: guarda so as linhas escolhidas", linhas <= 200 && Serial().Contains("299"),
                $"{linhas} linhas: [{Serial()[..Math.Min(160, Serial().Length)]}] console: "
                + janela.TesteConsole.Replace(Environment.NewLine, " / "));
            janela.TesteParar();

            // ---- simulacao: queima e som ----
            {
                var c = new Circuito();
                var uno = new PecaNoCircuito { Id = "placa", Tipo = "arduino-uno", X = 0, Y = 0 };
                var led = new PecaNoCircuito { Id = "led", Tipo = "led", X = 400, Y = 40 };
                var buzzer = new PecaNoCircuito { Id = "buzzer", Tipo = "buzzer", X = 500, Y = 40 };
                c.Pecas.AddRange([uno, led, buzzer]);
                // LED sem resistor num pino de 5 V: queima.
                foreach (var (dp, dpi, pp, ppi) in new[] { ("placa", "13", "led", "A"), ("led", "C", "placa", "GND.1"),
                                                           ("placa", "8", "buzzer", "1"), ("buzzer", "2", "placa", "GND.2") })
                    c.Fios.Add(new Fio { DePeca = dp, DePino = dpi, ParaPeca = pp, ParaPino = ppi });

                Definir(Preferencias.SimularQueima, "nao");
                Definir(Preferencias.SomDoBuzzer, "nao");
                janela.TesteAbrir(c, """
                    void setup() { pinMode(13, OUTPUT); digitalWrite(13, HIGH); tone(8, 440); }
                    void loop() { delay(50); }
                    """);
                janela.TesteRodar();
                EsperarPor(() => janela.TesteMaquinaDa("placa")?.Frequencia[8] == 440, 3000);
                EsperarPor(() => false, 800);
                bool inteiro = !janela.TesteEditorCircuito.EstaQueimada("led");
                bool mudo = janela.TesteUltimoTom == 0;

                Definir(Preferencias.SimularQueima, "sim");
                Definir(Preferencias.SomDoBuzzer, "sim");
                bool queimou = EsperarPor(() => janela.TesteEditorCircuito.EstaQueimada("led"), 3000);
                bool tocou = EsperarPor(() => janela.TesteUltimoTom == 440, 3000);
                janela.TesteParar();

                Conferir("simulacao: queima desligada, o LED sem resistor sobrevive", inteiro);
                Conferir("simulacao: ligada de novo, ele queima", queimou);
                Conferir("simulacao: som desligado, o buzzer fica mudo", mudo, janela.TesteUltimoTom.ToString());
                Conferir("simulacao: som ligado, o tom volta", tocou, janela.TesteUltimoTom.ToString());

                Definir(Preferencias.VolumeDoBuzzer, "baixo");
                Conferir("simulacao: o volume baixo chega na onda", Math.Abs(GeradorDeOnda.Volume - 0.4) < 1e-9,
                    GeradorDeOnda.Volume.ToString());
            }

            // ---- simulacao: a demo roda sozinha ----
            Definir(Preferencias.RodarAoAbrirDemo, "sim");
            janela.TesteAbrirDemo("esp32-semaforo");
            bool sozinha = EsperarPor(() => janela.TesteThreadViva, 3000);
            janela.TesteParar();
            Definir(Preferencias.RodarAoAbrirDemo, "nao");
            janela.TesteAbrirDemo("esp32-semaforo");
            EsperarPor(() => false, 600);
            Conferir("simulacao: a demo roda sozinha quando pedido", sozinha);
            Conferir("simulacao: e nao roda quando nao", !janela.TesteThreadViva);

            // ---- arquivos: Ctrl+S, .bak e salvamento automatico ----
            string arquivo = System.IO.Path.Combine(pasta, "projeto.sktch");
            Definir(Preferencias.CopiaDeSeguranca, "sim");
            janela.TesteAbrir(NovaEsp(), "void setup() {}\nvoid loop() {}");
            bool primeira = janela.TesteSalvarEm(arquivo);
            janela.TesteDigitar("// mudou\nvoid setup() {}\nvoid loop() {}");
            janela.TesteSalvar();
            string salvo = System.IO.File.ReadAllText(arquivo);
            string copia = System.IO.File.Exists(arquivo + ".bak") ? System.IO.File.ReadAllText(arquivo + ".bak") : "";
            Conferir("arquivos: o Ctrl+S grava no arquivo do projeto, sem perguntar",
                primeira && salvo.Contains("mudou"), salvo.Length.ToString());
            Conferir("arquivos: a versao anterior virou .bak",
                copia.Length > 0 && !copia.Contains("mudou"), copia.Length.ToString());

            Definir(Preferencias.SalvarSozinho, "1");
            janela.TesteDigitar("// de novo\nvoid setup() {}\nvoid loop() {}");
            bool automatico = janela.SalvarSozinhoSePrecisar(agora: true);
            bool semMudanca = janela.SalvarSozinhoSePrecisar(agora: true);
            Conferir("arquivos: o salvamento automatico grava o que mudou",
                automatico && System.IO.File.ReadAllText(arquivo).Contains("de novo"));
            Conferir("arquivos: sem mudanca, nao grava de novo", !semMudanca);

            Definir(Preferencias.QuantosRecentes, "5");
            for (int i = 0; i < 8; i++)
            {
                string p = System.IO.Path.Combine(pasta, $"p{i}.sktch");
                System.IO.File.WriteAllText(p, "{}");
                Recentes.Registrar(p);
            }
            Conferir("arquivos: a lista de recentes guarda o numero escolhido",
                Recentes.Ler().Count == 5, Recentes.Ler().Count.ToString());

            Definir(Preferencias.PlacaDoCircuitoNovo, "arduino-uno");
            janela.TesteNovoProjetoDoMenu();
            var placas = janela.TesteCircuito.Placas().Select(p => p.Tipo).ToList();
            Conferir("arquivos: o circuito novo nasce com a placa escolhida",
                placas.SequenceEqual(["arduino-uno"]), string.Join(",", placas));

            // ---- aparencia e fios ----
            Definir(Preferencias.EscalaDaInterface, "125");
            Conferir("aparencia: a interface cresce", Math.Abs(janela.TesteEscala - 1.25) < 0.001,
                janela.TesteEscala.ToString());
            Definir(Preferencias.EspessuraDoFio, "grossa");
            Conferir("circuito: os fios ficam grossos", janela.TesteEditorCircuito.FatorDosFios > 1.4,
                janela.TesteEditorCircuito.FatorDosFios.ToString());

            // ---- de fabrica ----
            cfg.RestaurarPadroes();
            Conferir("fabrica: tudo volta ao padrao",
                codigo.FontSize == 12 && codigo.ShowLineNumbers && !codigo.WordWrap
                && Math.Abs(janela.TesteEscala - 1) < 0.001 && Math.Abs(GeradorDeOnda.Volume - 1) < 1e-9
                && janela.TesteEditorCircuito.FatorDosFios == 1
                && Preferencias.Texto(Preferencias.TamanhoDoCodigo).Length == 0,
                $"letra {codigo.FontSize}, escala {janela.TesteEscala}, volume {GeradorDeOnda.Volume}");
            cfg.Close();
        }
        finally
        {
            janela.TesteParar();
            Preferencias.Restaurar();
            janela.TesteAplicarPreferencias();
            try { System.IO.Directory.Delete(pasta, true); } catch { }
        }
    }

    /// <summary>
    /// Toda demo roda de verdade, pela janela (Alpha 19).
    ///
    /// Abrir pelo mesmo caminho do menu, rodar e olhar: nenhum erro no
    /// console e alguma coisa acontecendo -- serial, pino mexendo, tela
    /// escrita, fita acesa. E depois, em cada uma, o que ela promete na
    /// descricao: o botao que se aperta, a mensagem que se publica no MQTT,
    /// a conta digitada na serial, o sensor que se muda. Uma demo que so
    /// "nao da erro" e so metade de uma demo.
    /// </summary>
    static void Demos_TodasRodam()
    {
        saida.AppendLine("todas as demos rodam, e fazem o que prometem");

        var janela = new MainWindow();
        var ed = janela.TesteEditorCircuito;

        // So o broker interno: o autoteste nao publica nada num broker
        // publico da internet, e nao depende da internet para passar.
        janela.TesteUsarInternet = false;

        PecaNoCircuito Peca(string id) => janela.TesteCircuito.Pecas.First(p => p.Id == id);
        Maquina M(string id = "placa") => janela.TesteMaquinaDa(id);
        List<string> Linhas(string id = "placa") => LinhasDoSerial(M(id));
        string Serial(string id = "placa") => string.Join(" | ", Linhas(id).TakeLast(6));
        bool TemNaSerial(string trecho, int ms = 5000, string id = "placa")
            => EsperarPor(() => Linhas(id).Any(l => l.Contains(trecho)), ms);

        static string TextoDe(Lcd tela)
            => tela is null ? "" : string.Join(" | ", Enumerable.Range(0, tela.Linhas)
                                                               .Select(i => tela.LinhaTexto(i).TrimEnd()));

        // Alguma placa ja esta vendo um botao apertado? E o que a pessoa
        // espera antes de soltar: sob carga, o quadro que leva o dedo ate a
        // placa pode demorar mais que um aperto curto.
        bool AlgumApertado()
            => janela.TesteCircuito.Pecas.Where(p => PerfilPlaca.EhPlaca(p.Tipo))
                     .Select(p => janela.TesteMaquinaDa(p.Id))
                     .Any(mq => mq is not null && Enumerable.Range(0, Maquina.NumPinos).Any(g => mq.Pressionado[g]));

        void Apertar(string id, int ms = 200)
        {
            var bt = Peca(id);
            var capa = EditorCircuito.CapasDeBotao[bt.Tipo];
            ed.Apertar(new System.Windows.Point(bt.X + capa.X, bt.Y + capa.Y));
            EsperarPor(AlgumApertado, 1500);
            EsperarPor(() => false, ms);
            ed.Soltar();
            EsperarPor(() => false, 80);
        }

        void Girar(string id, double valor) => ed.DefinirGiro(Peca(id), valor);

        void Tecla(string id, string tecla)
        {
            var teclado = Peca(id);
            var (x, y, _) = EditorCircuito.TeclasMatriciais.First(t => t.Tecla == tecla);
            ed.Apertar(Geometria.PosicaoLocal(teclado, Catalogo.Obter(teclado.Tipo), x, y));
            EsperarPor(() => M().Leitura("keypad:tecla") > 0, 1500);
            EsperarPor(() => false, 150);
            ed.Soltar();
            EsperarPor(() => false, 150);
        }

        int ValoresDistintos(Func<int> leitura, int ms)
        {
            var vistos = new HashSet<int>();
            EsperarPor(() => { vistos.Add(leitura()); return false; }, ms);
            return vistos.Count;
        }

        static bool Atividade(Maquina m)
        {
            if (m is null) return false;
            lock (m.Trava) if (m.Serial.Count > 0) return true;
            for (int p = 0; p < Maquina.NumPinos; p++)
                if (m.Linha.TemAlgo(p) || m.Pwm[p] > 0 || m.Frequencia[p] > 0) return true;
            if (TextoDe(m.Lcd).Trim(' ', '|').Length > 0 || TextoDe(m.Oled).Trim(' ', '|').Length > 0)
                return true;
            return m.Fitas.Values.Any(f => f.Envios > 0) || m.Tela is not null;
        }

        var horario = new System.Text.RegularExpressions.Regex(@"\d\d:\d\d:\d\d");

        // O que cada demo promete, conferido do jeito que a pessoa faria.
        (bool Ok, string Detalhe) Promessa(string recurso)
        {
            var m = M();
            switch (recurso)
            {
                case "uno-botao":
                {
                    var bt = Peca("bt");
                    var capa = EditorCircuito.CapasDeBotao[bt.Tipo];
                    ed.Apertar(new System.Windows.Point(bt.X + capa.X, bt.Y + capa.Y));
                    bool acendeu = EsperarPor(() => m.DigitalRead(13) == 1, 2000);
                    ed.Soltar();
                    bool apagou = EsperarPor(() => m.DigitalRead(13) == 0, 2000);
                    return (acendeu && apagou, $"pino 13: {m.DigitalRead(13)}");
                }

                case "esp32s2-relogio-ntp":
                    return (EsperarPor(() => horario.IsMatch(TextoDe(M().Oled)), 6000), TextoDe(m.Oled));

                case "esp32s3-anel-neopixel":
                {
                    bool arco = EsperarPor(() => m.Fitas.Values.Any(f => f.Envios > 5
                                                                        && f.Cores.Distinct().Count() > 8), 4000);
                    Apertar("botao");
                    return (arco && TemNaSerial("efeito: cometa"), Serial());
                }

                case "esp32c3-estufa":
                {
                    bool quente = EsperarPor(() => m.AnguloServo[5] == 90 && m.DigitalRead(6) == 1
                                                   && TextoDe(m.Lcd).Contains("Ventilando"), 6000);
                    Girar("dht", (15 + 40) / 120.0);
                    bool frio = EsperarPor(() => m.AnguloServo[5] == 0 && m.DigitalRead(7) == 1
                                                 && TextoDe(m.Lcd).Contains("Aquecendo"), 6000);
                    return (quente && frio, $"servo {m.AnguloServo[5]}, lcd {TextoDe(m.Lcd)}");
                }

                case "esp32c6-alarme":
                {
                    TemNaSerial("desarmado");
                    Apertar("botao");
                    bool armou = TemNaSerial("ARMADO", 8000);
                    Girar("pir", 1);
                    bool disparou = TemNaSerial("INVASAO", 3000)
                                    && EsperarPor(() => m.Frequencia[3] > 0, 2000);
                    Girar("pir", 0);
                    Apertar("botao");
                    bool desarmou = EsperarPor(() => Linhas().Count(l => l == "desarmado") >= 2, 3000);
                    return (armou && disparou && desarmou, Serial());
                }

                case "esp32h2-estacionamento":
                {
                    bool amarelo = EsperarPor(() => Linhas().Any(l => l == "distancia: 35 cm")
                                                    && m.Pwm[10] == 255 && m.Pwm[11] == 160, 4000);
                    Girar("sonar", 10 / 400.0);
                    bool encostou = EsperarPor(() => m.Frequencia[22] == 2000 && m.Pwm[11] == 0, 4000);
                    return (amarelo && encostou, $"R={m.Pwm[10]} G={m.Pwm[11]} f={m.Frequencia[22]}");
                }

                case "nodemcu-servidor":
                    return (TemNaSerial("servidor no ar", 6000), Serial());

                case "esp01-tomada-mqtt":
                {
                    bool conectou = TemNaSerial("broker... ok", 6000);
                    janela.TestePublicarNoBroker("casa/tomada", "liga", retida: false);
                    bool ligou = EsperarPor(() => m.DigitalRead(2) == 0
                                                  && Linhas().Contains("tomada LIGADA"), 4000);
                    // Um aperto de dedo (400 ms): com o PC ocupado, o loop() de
                    // 20 ms podia nao ver um aperto curto demais.
                    Apertar("botao", 400);
                    bool desligou = EsperarPor(() => m.DigitalRead(2) == 1, 3000);
                    return (conectou && ligou && desligou, Serial());
                }

                case "esp02-campainha":
                    TemNaSerial("Campainha pronta");
                    Apertar("botao");
                    return (TemNaSerial("Visita numero 1"), Serial());

                case "esp03-termo-higrometro":
                    // O seco (umidade de 35 %) fica no GPIO15: o ESP-03 nao tem o GPIO4.
                    return (EsperarPor(() => Linhas().Any(l => l.Contains("umidade: 35 %"))
                                             && m.DigitalRead(15) == 1 && m.DigitalRead(13) == 1, 4000),
                            Serial());

                case "esp04-semaforo-pedestre":
                {
                    // O sketch tem que ter comecado: o verde dos carros e o
                    // estado de partida, e o botao apertado antes do setup se
                    // perderia.
                    bool verde = TemNaSerial("Verde para os carros")
                                 && EsperarPor(() => m.DigitalRead(13) == 1, 2000);
                    Apertar("botao");
                    bool pediu = TemNaSerial("pedido de travessia");
                    bool amarelo = EsperarPor(() => m.DigitalRead(12) == 1, 3000);
                    return (verde && pediu && amarelo, Serial());
                }

                case "esp05-calculadora":
                    TemNaSerial("Calculadora");
                    janela.TesteDigitarNoSerial("12 + 30");
                    bool somou = TemNaSerial("12 + 30 = 42");
                    janela.TesteDigitarNoSerial("2 ^ 10");
                    return (somou && TemNaSerial("2 ^ 10 = 1024"), Serial());

                case "esp06-genius":
                    TemNaSerial("Genius:");
                    Apertar("b_vermelho");
                    return (TemNaSerial("Rodada 1"), Serial());

                case "esp07-scanner-wifi":
                    return (TemNaSerial("dBm", 6000), Serial());

                case "esp08-reflexo":
                {
                    bool verde = EsperarPor(() => m.DigitalRead(14) == 1, 7000);
                    Apertar("botao");
                    return (verde && TemNaSerial("Seu tempo:"), Serial());
                }

                case "esp09-dado":
                    TemNaSerial("Dado eletronico");
                    Apertar("botao");
                    return (TemNaSerial("Saiu "), Serial());

                case "esp10-ponte-serial-web":
                    TemNaSerial("pagina em http://", 6000);
                    janela.TesteDigitarNoSerial("ola mundo");
                    return (TemNaSerial("ok, esta na pagina: ola mundo"), Serial());

                case "esp11-minuteria":
                {
                    // A luz vai no RXD (o GPIO3), que acende com LOW; o sketch
                    // nao abre a Serial, que tomaria o pino.
                    bool apagada = EsperarPor(() => m.Modo[3] == 1 && m.DigitalRead(3) == 1, 3000);
                    Apertar("botao");
                    return (apagada && EsperarPor(() => m.DigitalRead(3) == 0, 2000), $"GPIO3 = {m.DigitalRead(3)}");
                }

                case "esp12-balanca":
                    TemNaSerial(" g");
                    Girar("celula", 250 / 5000.0);
                    return (TemNaSerial("250 g"), Serial());

                case "esp12e-rgb-navegador":
                    return (EsperarPor(() => Linhas().Any(l => l.Contains("R=255 G=106 B=0"))
                                             && m.Pwm[14] == 255 && m.Pwm[12] == 106, 6000), Serial());

                case "esp12f-luminaria-mqtt":
                {
                    bool pronta = TemNaSerial("broker... ok", 6000) && TemNaSerial("efeito=fixo", 3000);
                    janela.TestePublicarNoBroker("casa/luminaria/efeito", "arco-iris", retida: false);
                    bool arco = TemNaSerial("efeito=arco-iris")
                                && EsperarPor(() => m.Fitas.Values.Any(f => f.Cores.Distinct().Count() > 8), 3000);
                    return (pronta && arco, Serial());
                }

                case "esp13-fechadura":
                    TemNaSerial("Fechadura");
                    foreach (string t in new[] { "1", "2", "3", "4", "#" }) Tecla("teclado", t);
                    return (TemNaSerial("SENHA CERTA") && EsperarPor(() => m.DigitalRead(16) == 1, 2000),
                            Serial());

                case "esp14-placar":
                {
                    // Do ESP8266 do ESP-14 so sai o GPIO0: o placar e o anel
                    // NeoPixel nele, e os pontos chegam pelo MQTT.
                    bool pronto = TemNaSerial("broker... ok", 6000) && TemNaSerial("placar: 0 x 0", 3000);
                    janela.TestePublicarNoBroker("placar/ponto", "a", retida: false);
                    bool ponto = TemNaSerial("placar: 1 x 0")
                                 && EsperarPor(() => m.Fitas.Values.Any(f => f.Cores[0] != 0 && f.Cores.Count(c => c != 0) == 1), 3000);
                    return (pronto && ponto, Serial());
                }

                case "franzininho-vela":
                {
                    bool tremula = ValoresDistintos(() => m.Pwm[0], 1500) > 3;
                    Apertar("sopro");
                    bool apagou = EsperarPor(() => m.Pwm[0] == 0, 3000);
                    return (tremula && apagou, $"PWM no PB0: {m.Pwm[0]}");
                }

                case "attiny85-luz-noturna":
                {
                    bool noite = EsperarPor(() => m.Pwm[0] > 150, 3000);
                    int acesa = m.Pwm[0];
                    Girar("ldr", (Math.Log10(10000) + 1) / 6);
                    bool dia = EsperarPor(() => m.Pwm[0] == 0, 3000);
                    return (noite && dia, $"noite {acesa}, dia {m.Pwm[0]}");
                }

                case "pico-piano":
                {
                    EsperarPor(() => m.Modo[4] == 2, 3000);   // as teclas ja tem pull-up
                    var bt = Peca("tecla3");
                    var capa = EditorCircuito.CapasDeBotao[bt.Tipo];
                    ed.Apertar(new System.Windows.Point(bt.X + capa.X, bt.Y + capa.Y));
                    bool tocou = EsperarPor(() => m.Frequencia[15] == 330, 3000) && TemNaSerial("mi");
                    ed.Soltar();
                    bool parou = EsperarPor(() => m.Frequencia[15] == 0, 3000);
                    return (tocou && parou, $"f={m.Frequencia[15]} {Serial()}");
                }

                case "pico-joystick-oled":
                {
                    bool meio = EsperarPor(() => TextoDe(M().Oled).Contains("X "), 3000);
                    Girar("joy", 1.0);
                    bool canto = EsperarPor(() => TextoDe(M().Oled).Contains("X 65535"), 3000);
                    return (meio && canto, TextoDe(m.Oled));
                }

                case "pico-sdk-respirar":
                    return (ValoresDistintos(() => m.Pwm[15], 2000) > 5, $"PWM no GP15: {m.Pwm[15]}");

                case "picow-estacao-web":
                    return (TemNaSerial("23.5 C  58 %", 6000), Serial());

                case "picow-mqtt":
                {
                    bool pronta = TemNaSerial("pronto: publique", 6000);
                    bool publicou = TemNaSerial("pot: 40 %", 4000);
                    janela.TestePublicarNoBroker("pico/led", "on", retida: false);
                    bool acendeu = EsperarPor(() => m.DigitalRead(15) == 1, 3000);
                    return (pronta && publicou && acendeu, Serial());
                }

                case "picow-sdk-voltimetro":
                {
                    // A temperatura sai a cada 8 leituras (2 s de placa): sob a
                    // carga da bateria inteira, 4 s de parede nao bastavam.
                    bool mediu = TemNaSerial(" V (leitura") && TemNaSerial("temperatura do chip: 27", 8000);
                    Girar("pot", 0.9);
                    bool alerta = TemNaSerial("ALERTA") && EsperarPor(() => m.DigitalRead(15) == 1, 2000);
                    return (mediu && alerta, Serial());
                }

                case "nucleo-l031-termometro":
                {
                    bool mediu = TemNaSerial("temperatura: 26.0 C");
                    Girar("ntc", (35 + 24) / 104.0);
                    bool alarme = TemNaSerial("ALARME", 4000) && EsperarPor(() => m.DigitalRead(5) == 1, 2000);
                    return (mediu && alarme, Serial());
                }

                case "nucleo-c031-relogio-rtc":
                {
                    bool relogio = EsperarPor(() => horario.IsMatch(TextoDe(M().Lcd)), 4000);
                    // Uma volta do loop e o delay(200) mais as duas linhas do LCD
                    // pelo I2C: o toque de 200 ms podia cair inteiro no delay.
                    Apertar("botao", 400);
                    bool doze = EsperarPor(() => TextoDe(M().Lcd).Contains(" AM")
                                                 || TextoDe(M().Lcd).Contains(" PM"), 3000);
                    return (relogio && doze, TextoDe(m.Lcd));
                }

                case "ada-semaforo":
                    return (TemNaSerial("Semaforo em Ada") && EsperarPor(() => m.DigitalRead(10) == 1, 2000),
                            Serial());

                case "spark-termostato":
                {
                    bool aquece = TemNaSerial("aquecendo") && EsperarPor(() => m.DigitalRead(8) == 1, 2000);
                    Girar("pot", 0.6);
                    bool parou = EsperarPor(() => m.DigitalRead(8) == 0
                                                  && Linhas().LastOrDefault()?.EndsWith(" C") == true, 4000);
                    return (aquece && parou, Serial());
                }

                case "cowgol-contador":
                    EsperarPor(() => m.Modo[2] == 2, 3000);   // o pin_mode(INPUT_PULLUP) ja rodou
                    Apertar("botao");
                    return (TemNaSerial("contagem: 1") && EsperarPor(() => m.DigitalRead(8) == 1, 2000), Serial());

                case "millipascal-respirar":
                    return (ValoresDistintos(() => m.Pwm[9], 1500) > 5, $"PWM no 9: {m.Pwm[9]}");

                case "rust-sirene":
                    return (TemNaSerial("Sirene ligada") && ValoresDistintos(() => m.Frequencia[8], 1500) > 5,
                            $"f={m.Frequencia[8]}");

                case "csharp-medidor-de-luz":
                    return (TemNaSerial("LEDs acesos: 2"), Serial());

                case "assembly-contador":
                {
                    int Numero() => Enumerable.Range(0, 6).Sum(b => m.DigitalRead(8 + b) << b);
                    return (ValoresDistintos(Numero, 2000) >= 4, $"contador em {Numero()}");
                }
            }
            return (true, "");
        }

        try
        {
            // CS_DEMO="a,b": so as demos com esses pedacos no nome.
            string[] so = Environment.GetEnvironmentVariable("CS_DEMO")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var demo in Demos.Todas)
            {
                if (so is { Length: > 0 } && !so.Any(s => demo.Recurso.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
                janela.TesteParar();
                janela.TesteAbrirDemo(demo.Recurso);
                janela.TesteRodar();

                var placas = janela.TesteCircuito.Pecas.Where(p => PerfilPlaca.EhPlaca(p.Tipo))
                                   .Select(p => p.Id).ToList();

                // O gesto vem primeiro: ha demo que so faz alguma coisa depois
                // que alguem aperta o botao -- como o piano e o contador.
                var (ok, detalhe) = Promessa(demo.Recurso);
                bool mexeu = EsperarPor(() => placas.All(id => Atividade(janela.TesteMaquinaDa(id))), 6000);

                var erros = janela.TesteLinhasDoNivel(NivelDoConsole.Erro);
                var avisos = janela.TesteLinhasDoNivel(NivelDoConsole.Aviso);

                saida.AppendLine($"        {demo.Recurso,-26} {(mexeu ? "rodando" : "PARADA")}"
                                 + (detalhe.Length > 0 ? $" | {detalhe}" : "")
                                 + (avisos.Count > 0 ? $" | avisos: {string.Join(" / ", avisos)}" : ""));

                Conferir($"{demo.Recurso}: roda sem erro no console",
                    erros.Count == 0, string.Join(" / ", erros));
                Conferir($"{demo.Recurso}: faz alguma coisa (serial, pino, tela ou fita)", mexeu);
                Conferir($"{demo.Recurso}: faz o que a descricao promete", ok, detalhe);
            }
        }
        finally { janela.TesteParar(); }
    }

    /// <summary>
    /// Tipo de biblioteca em parametro de funcao: o callback do ESP-NOW,
    /// o cliente do servidor, o evento do WiFi. O parser so aceitava os
    /// tipos que conhecia, e o codigo do tutorial parava em "tipo
    /// esperado" na linha do callback.
    /// </summary>
    static void Motor_TiposDeBibliotecaEmParametro()
    {
        saida.AppendLine("tipos de biblioteca em parametro");

        foreach (var (codigo, porque) in new[]
                 {
                     ("void cb(const uint8_t *mac, esp_now_send_status_t estado) {}", "status do ESP-NOW"),
                     ("void cb(const esp_now_recv_info_t *info, const uint8_t *d, int n) {}", "recv_info do ESP-NOW 3.x"),
                     ("void atender(WiFiClient &cliente) {}", "referencia a cliente"),
                     ("void evento(WiFiEvent_t e, WiFiEventInfo_t info) {}", "evento do WiFi"),
                     ("void f(struct Leitura *l) {}", "struct com a palavra struct"),
                     ("void f(const String &texto, int n) {}", "String por referencia"),
                 })
        {
            string erro = null;
            try { Compilador.Compilar(codigo + Environment.NewLine + "void setup() {}" + Environment.NewLine + "void loop() {}"); }
            catch (Exception e) { erro = e.Message; }
            Conferir($"parametro: {porque}", erro is null, erro);
        }

        // ---- "::" e o std:: ----
        {
            var redeServo = new Rede { UsarInternet = false };
            var (mServo, iServo) = MontarPlaca(redeServo, "ESP32", """
                #include <ESP32Servo.h>
                Servo a;
                Servo b;
                void setup() {
                  Serial.begin(115200);
                  ESP32PWM::allocateTimer(0);
                  a.setPeriodHertz(50);
                  a.attach(18, 500, 2400);
                  b.attach(19);
                  a.writeMicroseconds(1450);
                  b.write(1472);
                  Serial.println(a.read());
                  Serial.println(b.read());
                  Serial.println(std::min(3, 7));
                  std::string nome = "servo";
                  Serial.println(nome);
                }
                void loop() {}
                """);
            string erro = null;
            try { iServo.Setup(); } catch (Exception e) { erro = e.Message; }
            var linhasServo = LinhasDoSerial(mServo);
            saida.AppendLine("        servo e escopo: " + (erro ?? string.Join(" | ", linhasServo)));

            Conferir("escopo: ESP32PWM::allocateTimer e std:: passam", erro is null, erro);
            Conferir("servo: writeMicroseconds usa os limites do attach(18, 500, 2400)",
                linhasServo.ElementAtOrDefault(0) == "90", string.Join(" | ", linhasServo));
            Conferir("servo: write(1472) e pulso, e nao angulo (como na Servo.h)",
                linhasServo.ElementAtOrDefault(1) == "90", string.Join(" | ", linhasServo));
            Conferir("escopo: std::min vira min, std::string vira String",
                linhasServo.ElementAtOrDefault(2) == "3" && linhasServo.ElementAtOrDefault(3) == "servo",
                string.Join(" | ", linhasServo));
        }

        // E o nome NAO vira tipo do sketch: a declaracao continua objeto.
        var rede = new Rede { UsarInternet = false };
        var (maq, interp) = MontarPlaca(rede, "ESP32", """
            #include <esp_now.h>
            #include <WiFi.h>
            void cb(const uint8_t *mac, esp_now_send_status_t estado) { Serial.println(estado); }
            esp_now_peer_info_t par;
            uint8_t todos[] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};
            uint8_t outro[] = {0x24, 0x0A, 0xC4, 0x99, 0x99, 0x99};
            void setup() {
              Serial.begin(115200);
              WiFi.mode(WIFI_STA);
              esp_now_init();
              esp_now_register_send_cb(cb);
              memcpy(par.peer_addr, todos, 6);
              par.channel = 0;
              par.encrypt = false;
              Serial.println(esp_now_add_peer(&par));
              Serial.println(esp_now_add_peer(&par));
              Serial.println(esp_now_send(todos, (uint8_t *) "oi", 3));
              Serial.println(esp_now_send(outro, (uint8_t *) "oi", 3));
            }
            void loop() {}
            """);
        interp.Setup();
        var linhas = LinhasDoSerial(maq);
        saida.AppendLine("        " + string.Join(" | ", linhas));

        Conferir("esp-now: o par entra na primeira vez (ESP_OK)", linhas.ElementAtOrDefault(0) == "0",
            string.Join(" | ", linhas));
        Conferir("esp-now: o mesmo par de novo e ESP_ERR_ESPNOW_EXIST, como na placa",
            linhas.ElementAtOrDefault(1) == (0x306B).ToString(), string.Join(" | ", linhas));
        Conferir("esp-now: difusao sem ninguem ouvindo: callback 0 (sem ACK na difusao) e ESP_OK",
            linhas.ElementAtOrDefault(2) == "0" && linhas.ElementAtOrDefault(3) == "0",
            string.Join(" | ", linhas));
        Conferir("esp-now: para quem nao e par, ESP_ERR_ESPNOW_NOT_FOUND e nada sai",
            linhas.ElementAtOrDefault(4) == (0x3069).ToString(), string.Join(" | ", linhas));
    }

    /// <summary>
    /// Trocar de projeto zera o que era do anterior (Alpha 19).
    ///
    /// O projeto A imprime no serial, entra no WiFi, liga o Bluetooth e
    /// tem uma pausa marcada; o broker guarda uma retida dele. Depois
    /// abre o B pelo caminho de verdade -- o de abrir arquivo --, e nada
    /// disso pode aparecer: nem no monitor, nem nos paineis, nem no
    /// console, nem como placa rodando por baixo.
    /// </summary>
    static void Programa_TrocaDeProjetoZera()
    {
        saida.AppendLine("trocar de projeto zera tudo");

        var a = new Circuito();
        a.Pecas.Add(new PecaNoCircuito { Id = "esp", Tipo = "esp32-devkit-v1", X = 0, Y = 0 });

        var janela = new MainWindow();
        janela.TesteAbrir(a, string.Join(Environment.NewLine,
            "#include <WiFi.h>",
            "#include <BluetoothSerial.h>",
            "BluetoothSerial SerialBT;",
            "void setup() {",
            "  Serial.begin(115200);",
            "  WiFi.begin(\"rede\", \"senha\");",
            "  SerialBT.begin(\"placa-A\");",
            "}",
            "void loop() {",
            "  Serial.println(\"sou o projeto A\");",
            "  delay(100);",
            "}"));

        janela.TesteMarcarPausa(1);
        janela.TesteRodar();
        janela.TestePublicarNoBroker("sala/luz", "do projeto A", retida: true);

        bool imprimiu = EsperarPor(() =>
        {
            janela.TesteAtualizarPaineis();
            return janela.TesteSerial.Contains("projeto A")
                   && janela.TesteTextoDoBluetooth.Contains("placa-A");
        }, 6000);

        saida.AppendLine($"        antes: serial {janela.TesteSerial.Length} car., "
                         + $"rede {janela.TesteTextoDaRede.Length}, bt {janela.TesteTextoDoBluetooth.Length}, "
                         + $"pausas {janela.TesteQuantasPausas}, retidas {janela.TesteRetidasNoBroker}");

        Conferir("troca: o projeto A deixou rastro em tudo antes da troca",
            imprimiu && janela.TesteQuantasPausas == 1 && janela.TesteRetidasNoBroker == 1,
            janela.TesteEstado());

        // ---- abre o B como a pessoa abre ----
        var b = new Circuito();
        b.Pecas.Add(new PecaNoCircuito { Id = "uno", Tipo = "arduino-uno", X = 0, Y = 0 });
        string arquivoB = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                                 $"troca-{Guid.NewGuid():N}.sktch");
        Sktch.Salvar(arquivoB, b, string.Join(Environment.NewLine,
            "void setup() { pinMode(13, OUTPUT); }",
            "void loop() { digitalWrite(13, !digitalRead(13)); delay(200); }"));

        try
        {
            janela.AbrirProjeto(arquivoB);

            string console = janela.TesteConsole;
            saida.AppendLine($"        depois: serial {janela.TesteSerial.Length} car., "
                             + $"rede {janela.TesteTextoDaRede.Length}, bt {janela.TesteTextoDoBluetooth.Length}, "
                             + $"pausas {janela.TesteQuantasPausas}, retidas {janela.TesteRetidasNoBroker}; "
                             + $"console: {console.Trim().Replace(Environment.NewLine, " / ")}");

            Conferir("troca: o monitor serial comeca vazio", janela.TesteSerial.Length == 0,
                janela.TesteSerial);
            Conferir("troca: os paineis Rede e Bluetooth comecam vazios",
                janela.TesteTextoDaRede.Length == 0 && janela.TesteTextoDoBluetooth.Length == 0,
                janela.TesteTextoDaRede + " | " + janela.TesteTextoDoBluetooth);
            // A primeira linha diz o que abriu; depois dela so pode vir o
            // que e do projeto novo (a linha da placa, por exemplo).
            string primeira = console.Split('\n').FirstOrDefault() ?? "";
            Conferir("troca: o console recomeca dizendo o que abriu",
                primeira.Contains("projeto aberto") && !console.Contains("ESP32")
                && !console.Contains("placa-A"), console);
            Conferir("troca: as pausas do codigo anterior somem", janela.TesteQuantasPausas == 0);
            Conferir("troca: a retida do projeto anterior sai do broker",
                janela.TesteRetidasNoBroker == 0, janela.TesteRetidasNoBroker.ToString());
            Conferir("troca: a placa do projeto anterior parou", janela.TesteEstado().Contains("threadViva=False"),
                janela.TesteEstado());

            // O B roda sozinho ao abrir; o que ele mostra tem que ser dele.
            EsperarPor(() => { janela.TesteAtualizarPaineis(); return false; }, 800);
            Conferir("troca: rodando o B, nada do A volta ao monitor",
                !janela.TesteSerial.Contains("projeto A"), janela.TesteSerial);

            // ---- a demo tambem para o que estava rodando ----
            janela.TesteRodar();
            janela.TesteAbrirDemo("esp32-semaforo");
            Conferir("troca: abrir uma demo para a simulacao que estava rodando",
                janela.TesteEstado().Contains("threadViva=False"), janela.TesteEstado());
            Conferir("troca: e o console diz qual exemplo abriu",
                janela.TesteConsole.Contains("exemplo aberto"), janela.TesteConsole);

            // ---- circuito novo ----
            janela.TesteNovoCircuito();
            Conferir("troca: circuito novo tambem recomeca o console",
                janela.TesteLinhasDoConsole <= 2 && janela.TesteConsole.Contains("circuito novo"),
                janela.TesteConsole);
        }
        finally
        {
            janela.TesteParar();
            try { System.IO.File.Delete(arquivoB); } catch { }
        }
    }

    /// <summary>
    /// O plugin da Arduino IDE lista toda placa do simulador, com a mesma
    /// flash e RAM do limitador.
    ///
    /// Na Alpha 18 o boards.txt tinha seis das trinta e quatro placas: a
    /// placa existia no simulador e nao existia na IDE, e nada avisava.
    /// </summary>
    static void Plugin_TodasAsPlacas()
    {
        saida.AppendLine("plugin da arduino ide: as placas");

        string boards = null;
        for (var pasta = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
             pasta is not null; pasta = pasta.Parent)
        {
            string candidato = System.IO.Path.Combine(pasta.FullName, "plataforma", "boards.txt");
            if (System.IO.File.Exists(candidato)) { boards = candidato; break; }
        }

        if (boards is null)
        {
            saida.AppendLine("        (sem a pasta plataforma por perto: pulado)");
            return;
        }

        string texto = System.IO.File.ReadAllText(boards);
        var faltando = PlacasDaIde.Faltando(texto);
        var chaves = System.Text.RegularExpressions.Regex
            .Matches(texto, @"(?m)^(\w+)\.name=")
            .Select(m => m.Groups[1].Value).ToList();

        saida.AppendLine($"        {chaves.Count} placas na IDE, {PerfilPlaca.Conhecidos.Count} no simulador");

        Conferir("plugin: toda placa do simulador aparece na Arduino IDE",
            faltando.Count == 0, string.Join(", ", faltando.Select(p => p.Id)));
        Conferir("plugin: nenhuma chave de placa repetida",
            chaves.Count == chaves.Distinct().Count(),
            string.Join(", ", chaves.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key)));

        // A porcentagem que a IDE mostra e a recusa do limitador tem que
        // sair da MESMA conta -- senao a IDE diz 90% e o simulador recusa.
        var divergentes = new List<string>();
        foreach (var p in PerfilPlaca.Conhecidos)
        {
            string chave = System.Text.RegularExpressions.Regex.Match(texto,
                $@"(?m)^(\w+)\.simulador\.peca={System.Text.RegularExpressions.Regex.Escape(p.Id)}\s*$")
                .Groups[1].Value;
            if (chave.Length == 0) continue;

            string Valor(string prop) => System.Text.RegularExpressions.Regex.Match(texto,
                $@"(?m)^{chave}\.{System.Text.RegularExpressions.Regex.Escape(prop)}=(.*?)\s*$")
                .Groups[1].Value;

            string flash = Valor("upload.maximum_size"), ram = Valor("upload.maximum_data_size");
            if (flash != p.FlashBytes.ToString() || ram != p.RamBytes.ToString())
                divergentes.Add($"{p.Id}: IDE {flash}/{ram}, simulador {p.FlashBytes}/{p.RamBytes}");
        }

        Conferir("plugin: a IDE e o limitador contam a mesma flash e RAM",
            divergentes.Count == 0, string.Join("; ", divergentes));
    }

    internal static string AcharABateria()
    {
        var candidatos = new List<string>();

        string perto = AppDomain.CurrentDomain.BaseDirectory;

        for (var pasta = new System.IO.DirectoryInfo(perto);
             pasta is not null; pasta = pasta.Parent)
        {
            candidatos.Add(System.IO.Path.Combine(pasta.FullName, "bateria"));
            candidatos.Add(System.IO.Path.Combine(pasta.FullName, "sketches-teste", "bateria"));
        }

        return candidatos.FirstOrDefault(System.IO.Directory.Exists);
    }

    /// <summary>
    /// Abre, compila e roda um projeto da bateria. Devolve a queixa, ou null.
    ///
    /// O que se confere e o que um aluno notaria: o circuito montou, o
    /// programa compilou, a simulacao andou e algum pino mexeu. Nao e
    /// pouco: um nome de pino errado, uma biblioteca faltando ou um
    /// laco que nao anda derrubam pelo menos um desses quatro.
    /// </summary>
    static string RodarDaBateria(string arquivo)
    {
        try
        {
            var (circuito, codigo) = Sktch.Abrir(arquivo);

            if (circuito is null || circuito.Pecas.Count == 0) return "circuito vazio";

            // Os fios apontam para pinos que existem?
            foreach (var fio in circuito.Fios)
                foreach (var (peca, pino) in new[]
                         { (fio.DePeca, fio.DePino), (fio.ParaPeca, fio.ParaPino) })
                {
                    var def = Catalogo.Obter(circuito.Achar(peca)?.Tipo);

                    if (def is null) return $"peca '{peca}' nao existe";
                    if (!def.Pinos.Any(x => x.Nome == pino)) return $"pino '{peca}:{pino}' nao existe";
                }

            // Sem placa (a pasta "energia"): quem trabalha e a bateria.
            var placa = circuito.Placas().FirstOrDefault();
            if (placa is null) return RodarSemPlaca(circuito);

            string fonte = placa.Sketch ?? codigo ?? "";
            if (fonte.Trim().Length == 0) return "sem codigo";

            var prog = Compilador.Compilar(fonte);

            var maq = new Maquina { Perfil = PerfilPlaca.De(placa.Tipo) ?? PerfilPlaca.Esp32 };
            var rede = new Netlist(circuito);

            var interp = new Interpretador(prog, maq);
            interp.Preparar();
            interp.Setup();

            for (int i = 0; i < 6; i++) interp.Loop();

            // Um pino mexeu, ou pelo menos o programa escreveu algo.
            int eventos = maq.Eventos.Count;
            int linhas;
            lock (maq.Trava) linhas = maq.Serial.Count;

            if (eventos == 0 && linhas == 0) return "rodou sem mexer em nada";

            _ = rede;
            return null;
        }
        catch (Exception e)
        {
            return e.GetType().Name + ": " + e.Message.Replace(Environment.NewLine, " ");
        }
    }

    /// <summary>
    /// Projeto de varios arquivos: o #include "config.h" traz o config.h.
    ///
    /// E o caso do laptimer da lista do professor. O simulador ja tinha
    /// as abas para criar esses arquivos, mas na hora de rodar compilava
    /// SO o sketch -- e a variavel declarada no outro arquivo "nao
    /// existia", tres linhas depois do include.
    /// </summary>
    static void Projeto_VariosArquivos()
    {
        saida.AppendLine("projeto com varios arquivos");

        var arquivos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["config.h"] = "const int LED_DO_PROJETO = 14;",
            ["app_state.h"] = string.Join(Environment.NewLine,
            [
                "#include " + Q + "config.h" + Q,
                "int voltas = 0;",
            ]),
        };

        string sketch = string.Join(Environment.NewLine,
        [
            "#include " + Q + "config.h" + Q,
            "#include " + Q + "app_state.h" + Q,
            "#include <WiFi.h>",
            "",
            "void setup() {",
            "  Serial.begin(115200);",
            "  pinMode(LED_DO_PROJETO, OUTPUT);",
            "  voltas = 3;",
            "  Serial.println(voltas);",
            "}",
            "void loop() {}",
        ]);

        string montado = Montagem.Montar(sketch, arquivos);
        saida.AppendLine($"        montado: {montado.Length} caracteres "
                         + $"(sketch sozinho: {sketch.Length})");

        Conferir("projeto: o conteudo do config.h entra na compilacao",
            montado.Contains("LED_DO_PROJETO = 14"), montado);

        // Cada arquivo UMA vez: os dois incluem o config.h.
        int quantas = montado.Split("LED_DO_PROJETO = 14").Length - 1;
        saida.AppendLine($"        config.h entrou {quantas} vez(es)");
        Conferir("projeto: arquivo incluido duas vezes entra uma so", quantas == 1,
            quantas.ToString());

        Conferir("projeto: o #include <WiFi.h> continua sendo com o simulador",
            !montado.Contains("biblioteca"), "puxou biblioteca do projeto");

        // E o sketch montado RODA.
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maq.Reiniciar();

        string erro = null;
        try
        {
            var prog = Compilador.Compilar(montado);
            var interp = new Interpretador(prog, maq);
            interp.Preparar();
            interp.Setup();
        }
        catch (Exception e) { erro = e.Message; }

        saida.AppendLine($"        rodou: {erro ?? "sem erro"} | serial: {SoOTexto(maq)}");
        Conferir("projeto: o sketch montado roda", erro is null, erro);
        Conferir("projeto: e a variavel do outro arquivo vale",
            SoOTexto(maq).Contains("3"), SoOTexto(maq));

        // Include circular nao pode travar.
        var circular = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["a.h"] = "#include " + Q + "b.h" + Q,
            ["b.h"] = "#include " + Q + "a.h" + Q,
        };
        string fechado = Montagem.Montar("#include " + Q + "a.h" + Q, circular);
        Conferir("projeto: include circular nao trava", fechado.Length < 400,
            fechado.Length.ToString());

        // Sem arquivos, o sketch sai como entrou.
        Conferir("projeto: sem arquivos do projeto, nada muda",
            Montagem.Montar(sketch, null) == sketch);
    }


    /// <summary>
    /// O que a pasta "NOT WORKING" do professor cobrou.
    ///
    /// Cada conferencia aqui nasceu de um sketch que nao rodava, e a
    /// causa nunca foi a que parecia pelo nome do arquivo. Sao as cinco
    /// que mais doeriam se voltassem.
    /// </summary>
    static void Sketches_DaPastaQuebrada()
    {
        saida.AppendLine("os sketches da pasta quebrada");

        // ---- 1) o texto de um literal nao e simbolo ----
        //
        // "Serial.print(\")\")" nao compilava: o parser comparava o TEXTO
        // do token sem olhar o tipo, e a string ")" contava como o
        // fecha-parenteses da chamada.
        var (_, parenteses) = RodarCpp(
            "Serial.print(" + Q + " (" + Q + "); "
            + "Serial.print(" + Q + ")" + Q + "); "
            + "Serial.println(" + Q + "fim" + Q + ");");

        saida.AppendLine($"        parenteses em texto: {parenteses}");
        Conferir("quebrada: Serial.print de um parentese compila",
            parenteses.Contains(" ()fim"), parenteses);

        // ---- 2) vetor de duas dimensoes ----
        var (_, teclas) = RodarCpp(string.Join(Environment.NewLine,
        [
            "char mapa[2][3] = {{" + Q[0] + "a" + Q[0] + "," + Q[0] + "b" + Q[0]
                + "," + Q[0] + "c" + Q[0] + "},{" + Q[0] + "d" + Q[0] + ","
                + Q[0] + "e" + Q[0] + "," + Q[0] + "f" + Q[0] + "}};",
            "Serial.print(mapa[0][0]); Serial.print(mapa[1][2]);",
            "mapa[1][0] = " + Q[0] + "Z" + Q[0] + "; Serial.println(mapa[1][0]);",
        ]));
        saida.AppendLine($"        vetor 2D: {teclas}");
        Conferir("quebrada: vetor de duas dimensoes le e escreve",
            teclas.Contains("afZ"), teclas);

        // ---- 3) constexpr, struct como tipo, struct dentro de struct ----
        var (_, estruturas) = RodarCpp(string.Join(Environment.NewLine,
        [
            "Serial.print(PERIODO); Serial.print(" + Q + "," + Q + ");",
            "GeoLine linha;",
            "linha.a.lat = 12;",
            "Serial.println(linha.a.lat);",
        ]), antes: string.Join(Environment.NewLine,
        [
            "constexpr uint32_t PERIODO = 40;",
            "struct GeoPoint { double lat = 0; double lon = 0; };",
            "struct GeoLine { GeoPoint a; GeoPoint b; };",
        ]));
        saida.AppendLine($"        struct: {estruturas}");
        Conferir("quebrada: constexpr vira uma constante normal",
            estruturas.Contains("40,"), estruturas);
        Conferir("quebrada: struct dentro de struct",
            estruturas.Contains("12"), estruturas);

        // ---- 4) o codigo do infravermelho no formato da IRremote.hpp ----
        //
        // As duas bibliotecas mostram o MESMO botao com numeros
        // diferentes: a antiga da 0x00FFA25D e a nova, 0x5DA2FF00. O
        // sketch de controle remoto compara com a segunda.
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Reiniciar();
            maq.Leituras["ir:codigo"] = 0xFFA25D;

            var prog = Compilador.Compilar(string.Join(Environment.NewLine,
            [
                "void setup() {",
                "  Serial.begin(115200);",
                "  if (IrReceiver.decode()) {",
                "    Serial.print(" + Q + "raw=" + Q + ");",
                "    Serial.println(IrReceiver.decodedIRData.decodedRawData, HEX);",
                "  }",
                "}",
                "void loop() {}",
            ]));

            var interp = new Interpretador(prog, maq);
            interp.Preparar();
            interp.Setup();

            string ir = SoOTexto(maq);
            saida.AppendLine($"        infravermelho: {ir}");
            Conferir("quebrada: decodedRawData sai no formato da biblioteca nova",
                ir.Contains("5DA2FF00", StringComparison.OrdinalIgnoreCase), ir);
        }

        // ---- 5) em Python, o if NAO cria escopo ----
        //
        // "result" escrito dentro do if tem que existir depois dele. E
        // assim que o sketch da calculadora guarda a conta, e com escopo
        // por bloco a linha seguinte dizia que a variavel nao existia.
        var (_, escopo) = RodarPython(string.Join("\n",
        [
            "sinal = " + Q + "+" + Q,
            "if sinal == " + Q + "+" + Q + ":",
            "    resultado = 12 + 7",
            "else:",
            "    resultado = 0",
            "print(" + Q + "Result =" + Q + ", resultado)",
            "for i in range(3):",
            "    ultimo = i",
            "print(" + Q + "ultimo =" + Q + ", ultimo)",
        ]));
        saida.AppendLine($"        escopo do Python: {escopo}");
        Conferir("quebrada: variavel escrita dentro do if sobrevive a ele",
            escopo.Contains("Result = 19"), escopo);
        Conferir("quebrada: e a do for tambem",
            escopo.Contains("ultimo = 2"), escopo);

        // ---- 6) o diagrama guardado na descricao ----
        string diagrama = "{" + Q + "version" + Q + ": 1, " + Q + "parts" + Q + ": ["
            + "{" + Q + "type" + Q + ": " + Q + "board-esp32-devkit-c-v4" + Q + ", "
            + Q + "id" + Q + ": " + Q + "esp" + Q + "},"
            + "{" + Q + "type" + Q + ": " + Q + "wokwi-led-strip" + Q + ", "
            + Q + "id" + Q + ": " + Q + "fita" + Q + "}], "
            + Q + "connections" + Q + ": [[" + Q + "esp:D12" + Q + ", "
            + Q + "fita:DIN" + Q + ", " + Q + "green" + Q + ", []]]}";

        Conferir("quebrada: o simulador reconhece um diagrama na descricao",
            ImportarWokwi.PareceDiagrama(diagrama), "nao reconheceu");
        Conferir("quebrada: texto comum nao e confundido com diagrama",
            !ImportarWokwi.PareceDiagrama("Este projeto acende um LED."));

        var montado = ImportarWokwi.Ler(diagrama);
        saida.AppendLine($"        do diagrama: {montado.Circuito.Pecas.Count} pecas, "
                         + $"{montado.Ignorados.Count} sem equivalente");
        Conferir("quebrada: a fita de LEDs do Wokwi tem equivalente aqui",
            montado.Circuito.Pecas.Any(x => x.Tipo == "neopixel"),
            string.Join(", ", montado.Circuito.Pecas.Select(x => x.Tipo)));
    }

    /// <summary>Roda um corpo de setup() em C++ e devolve o monitor.</summary>
    static (Maquina Maq, string Serial) RodarCpp(string corpo, string antes = "")
    {
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maq.Reiniciar();

        var prog = Compilador.Compilar(
            antes + Environment.NewLine
            + "void setup() { Serial.begin(115200); " + corpo + " }" + Environment.NewLine
            + "void loop() {}");

        var interp = new Interpretador(prog, maq);
        interp.Preparar();
        interp.Setup();

        return (maq, SoOTexto(maq));
    }

    static void Conferir(string nome, bool ok, string detalhe = "")
    {
        if (ok) { passou++; saida.AppendLine($"  OK    {nome}"); }
        else { falhou++; saida.AppendLine($"  FALHA {nome}  {detalhe}"); }
    }

    // ---------------- catalogo ----------------
    static void Catalogo1_TemAsPecas()
    {
        saida.AppendLine("catalogo");
        Conferir("50+ pecas", Catalogo.Todas.Count >= 50, $"tem {Catalogo.Todas.Count}");
        Conferir("ESP32 com 30 pinos",
            Catalogo.Obter("esp32-devkit-v1")?.Pinos.Count == 30);
        Conferir("protoboard 400 existe", Catalogo.Obter(Catalogo.IdProtoboard) is not null);
        Conferir("protoboard 830 e maior",
            Catalogo.Obter(Catalogo.IdProtoboard830).Pinos.Count
                > Catalogo.Obter(Catalogo.IdProtoboard).Pinos.Count);
    }

    // ---------------- encaixe ----------------
    static (Circuito c, PecaNoCircuito board, PecaNoCircuito led) Cenario(double erroX, double erroY)
    {
        var c = new Circuito();
        var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard, X = 100, Y = 100 };
        c.Pecas.Add(board);

        var defBoard = Catalogo.Obter(board.Tipo);
        var defLed = Catalogo.Obter("led");
        var furo = defBoard.Pinos.First(p => p.Nome == "A5");
        var posFuro = Geometria.PosicaoPino(board, defBoard, furo);
        var pinoA = defLed.Pinos.First(p => p.Nome == "A");

        // Posiciona o LED para que a perna A caia sobre o furo, com erro.
        var led = new PecaNoCircuito { Tipo = "led", X = 0, Y = 0 };
        var relativo = Geometria.PosicaoPino(led, defLed, pinoA);
        led.X = posFuro.X - relativo.X + erroX;
        led.Y = posFuro.Y - relativo.Y + erroY;
        c.Pecas.Add(led);

        return (c, board, led);
    }

    static void Encaixe_LedPertoDoFuro()
    {
        saida.AppendLine("encaixe");
        var (c, board, led) = Cenario(3.0, -2.5);

        var ajuste = Geometria.AjusteParaFuro(
            led, Catalogo.Obter("led"), c, Catalogo.Passo * 0.95);
        Conferir("perna a 4 px do furo encaixa", ajuste is not null);

        if (ajuste is null) return;
        led.X += ajuste.Value.X;
        led.Y += ajuste.Value.Y;

        // O encaixe alinha a perna MAIS PROXIMA, nao necessariamente a "A".
        // O passo do LED (10 px) nao bate com o da placa (9,63), entao as
        // duas pernas nunca caem exatas ao mesmo tempo -- na bancada e a
        // mesma coisa: entorta-se a perna. Basta uma estar no furo.
        var defBoard = Catalogo.Obter(board.Tipo);
        var defLed = Catalogo.Obter("led");

        double menorResto = double.MaxValue;
        foreach (var pino in defLed.Pinos)
        {
            var q = Geometria.PosicaoPino(led, defLed, pino);
            foreach (var furo2 in defBoard.Pinos)
            {
                double d = (Geometria.PosicaoPino(board, defBoard, furo2) - q).Length;
                if (d < menorResto) menorResto = d;
            }
        }
        Conferir("alguma perna fica exatamente no furo",
            menorResto < 0.01, $"a mais proxima sobrou {menorResto:0.###} px");
    }

    static void Encaixe_LedLongeNaoMexe()
    {
        var (c, _, led) = Cenario(200, 200);
        var ajuste = Geometria.AjusteParaFuro(
            led, Catalogo.Obter("led"), c, Catalogo.Passo * 0.95);
        Conferir("longe da placa nao encaixa", ajuste is null);
    }

    // ---------------- netlist ----------------
    static void Netlist_PernaNoFuroConduz()
    {
        saida.AppendLine("netlist");
        var (c, board, led) = Cenario(0, 0);
        var rede = new Netlist(c);

        Conferir("perna encaixada conduz para o furo",
            rede.MesmoNo(Netlist.No(led.Id, "A"), Netlist.No(board.Id, "A5")));
    }

    static void Netlist_ColunaLigaLedAoGpio()
    {
        var (c, board, led) = Cenario(0, 0);

        // ESP32 com um fio do D14 para outro furo da MESMA coluna.
        var esp = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = -300, Y = 0 };
        c.Pecas.Add(esp);
        c.Fios.Add(new Fio
        {
            DePeca = esp.Id, DePino = "D14",
            ParaPeca = board.Id, ParaPino = "E5",
            Cor = "#000000"
        });

        var rede = new Netlist(c);
        int gpio = rede.GpioDe(c, led.Id, "A");
        Conferir("LED espetado na coluna chega no D14 sem fio proprio",
            gpio == 14, $"achou GPIO {gpio}");
    }

    static void Netlist_TrilhoAtravessaAPlacaInteira()
    {
        var c = new Circuito();
        var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard830, X = 0, Y = 0 };
        c.Pecas.Add(board);
        var rede = new Netlist(c);

        // Usa as colunas que o trilho REALMENTE tem: com os grupos de 5,
        // nem toda coluna da placa tem furo de alimentacao.
        var doTrilho = Catalogo.ColunasDoTrilho(Catalogo.IdProtoboard830).ToList();
        Conferir("trilho negativo e um no so de ponta a ponta",
            rede.MesmoNo(Netlist.No(board.Id, $"B-{doTrilho[0]}"),
                         Netlist.No(board.Id, $"B-{doTrilho[^1]}")));
        Conferir("coluna A-E nao vaza para F-J pelo canal",
            !rede.MesmoNo(Netlist.No(board.Id, "A5"), Netlist.No(board.Id, "F5")));
    }

    static void Netlist_ResistorConduz()
    {
        var c = new Circuito();
        var r = new PecaNoCircuito { Tipo = "resistor", X = 0, Y = 0 };
        c.Pecas.Add(r);
        var rede = new Netlist(c);
        Conferir("resistor conduz entre as duas pernas",
            rede.MesmoNo(Netlist.No(r.Id, "1"), Netlist.No(r.Id, "2")));
    }

    // ---------------- camadas de acerto ----------------
    /// <summary>
    /// A reclamacao concreta: clicar num resistor espetado na protoboard
    /// criava um fio no furo debaixo dele. Estes testes travam a ordem.
    /// </summary>
    static void Camadas_PrioridadeDoClique()
    {
        saida.AppendLine("camadas");

        var c = new Circuito();
        var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard, X = 0, Y = 0 };
        c.Pecas.Add(board);

        var defBoard = Catalogo.Obter(board.Tipo);
        var defRes = Catalogo.Obter("resistor");
        var furo = defBoard.Pinos.First(p => p.Nome == "F10");
        var posFuro = Geometria.PosicaoPino(board, defBoard, furo);

        // Resistor espetado com a perna 1 no furo F10.
        var pino1 = defRes.Pinos.First(p => p.Nome == "1");
        var res = new PecaNoCircuito { Tipo = "resistor" };
        res.X = posFuro.X - pino1.X;
        res.Y = posFuro.Y - pino1.Y;
        c.Pecas.Add(res);

        var editor = new EditorCircuito();
        editor.Carregar(c);

        // Meio do corpo do resistor, longe das duas pernas.
        var meio = new System.Windows.Point(res.X + defRes.Largura / 2,
                                            res.Y + defRes.Altura / 2);
        var a1 = editor.Resolver(meio);
        Conferir("corpo do resistor ganha do furo debaixo",
            a1.Camada == EditorCircuito.Camada.CorpoPeca, $"veio {a1.Camada} ({a1.Pino})");

        // Alguns pixels acima do corpo: ainda deve pegar o resistor,
        // porque a caixa dele tem so 11 px de altura.
        var acima = new System.Windows.Point(meio.X, res.Y - 3);
        var a2 = editor.Resolver(acima);
        Conferir("resistor fino continua clicavel um pouco acima",
            a2.Camada == EditorCircuito.Camada.CorpoPeca, $"veio {a2.Camada}");

        // Na propria perna do resistor: ai sim comeca fio.
        var naPerna = Geometria.PosicaoPino(res, defRes, pino1);
        var a3 = editor.Resolver(naPerna);
        Conferir("perna do resistor comeca fio",
            a3.Camada == EditorCircuito.Camada.PinoPeca && a3.Pino == "1",
            $"veio {a3.Camada} ({a3.Pino})");

        // Furo vazio, longe do resistor: continua sendo furo.
        var vazio = Geometria.PosicaoPino(
            board, defBoard, defBoard.Pinos.First(p => p.Nome == "A25"));
        var a4 = editor.Resolver(vazio);
        Conferir("furo livre continua comecando fio",
            a4.Camada == EditorCircuito.Camada.Furo, $"veio {a4.Camada}");

        // Corpo da protoboard onde nao ha furo nem peca.
        var bordaPlaca = new System.Windows.Point(board.X + 2, board.Y + 2);
        var a5 = editor.Resolver(bordaPlaca);
        Conferir("borda da placa seleciona a placa",
            a5.Camada == EditorCircuito.Camada.CorpoPlaca, $"veio {a5.Camada}");
    }

    // ---------------- laco de selecao ----------------
    static void Laco_SelecionaVarias()
    {
        saida.AppendLine("laco");

        var c = new Circuito();
        var a = new PecaNoCircuito { Tipo = "led", X = 0, Y = 0 };
        var b = new PecaNoCircuito { Tipo = "led", X = 60, Y = 0 };
        var longe = new PecaNoCircuito { Tipo = "led", X = 500, Y = 500 };
        c.Pecas.Add(a); c.Pecas.Add(b); c.Pecas.Add(longe);

        var dentro = Geometria.NoRetangulo(c, new System.Windows.Rect(-10, -10, 130, 80));
        Conferir("laco pega as duas de dentro", dentro.Count == 2, $"pegou {dentro.Count}");
        Conferir("laco nao pega a de fora", !dentro.Contains(longe));

        // Encostar de raspao ja conta: o retangulo intersecta a caixa.
        var raspao = Geometria.NoRetangulo(c, new System.Windows.Rect(35, 10, 10, 10));
        Conferir("encostar na caixa ja seleciona", raspao.Count == 1 && raspao[0] == a);

        var vazio = Geometria.NoRetangulo(c, new System.Windows.Rect(200, 200, 20, 20));
        Conferir("area vazia nao seleciona nada", vazio.Count == 0);
    }

    // ---------------- contagem de pontos ----------------
    /// <summary>
    /// Placa de verdade: 63 colunas x 10 linhas = 630 no miolo, mais 4
    /// trilhos de 50 furos (10 grupos de 5) = 830. A de 400 e 30x10 = 300
    /// mais 4 trilhos de 25 = 400.
    /// </summary>
    static void Protoboard_ContagemDePontos()
    {
        saida.AppendLine("protoboard");

        var b830 = Catalogo.Obter(Catalogo.IdProtoboard830);
        var b400 = Catalogo.Obter(Catalogo.IdProtoboard);

        Conferir("830 tem 830 pontos", b830.Pinos.Count == 830, $"tem {b830.Pinos.Count}");
        Conferir("400 tem 400 pontos", b400.Pinos.Count == 400, $"tem {b400.Pinos.Count}");

        // Trilho se reconhece pelo + ou - no nome; a LINHA B tambem
        // comeca com "B" e nao pode ser confundida com o trilho de baixo.
        int miolo830 = b830.Pinos.Count(p => !p.Nome.Contains('+') && !p.Nome.Contains('-'));
        Conferir("miolo da 830 e 63x10", miolo830 == 630, $"tem {miolo830}");

        int trilho = b830.Pinos.Count(p => p.Nome.StartsWith("T+"));
        Conferir("cada trilho da 830 tem 50 furos", trilho == 50, $"tem {trilho}");

        // O vao do desenho nao pode cortar a ligacao eletrica.
        var c = new Circuito();
        var placa = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard830, X = 0, Y = 0 };
        c.Pecas.Add(placa);
        var rede = new Netlist(c);

        var cols = Catalogo.ColunasDoTrilho(Catalogo.IdProtoboard830).ToList();
        Conferir("trilho continua eletricamente inteiro apesar dos vaos",
            rede.MesmoNo(Netlist.No(placa.Id, $"T+{cols[0]}"),
                         Netlist.No(placa.Id, $"T+{cols[^1]}")));
    }

    // ---------------- pinos ligados ----------------
    static void Pinos_LigadoOuNao()
    {
        saida.AppendLine("pinos ligados");

        var c = new Circuito();
        var esp = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0 };
        c.Pecas.Add(esp);
        c.Pecas.Add(led);
        c.Fios.Add(new Fio
        {
            DePeca = esp.Id, DePino = "D14",
            ParaPeca = led.Id, ParaPino = "A", Cor = "#000000"
        });

        var rede = new Netlist(c);
        Conferir("pino com fio conta como ligado", rede.EstaLigado(esp.Id, "D14"));
        Conferir("pino sem nada nao conta", !rede.EstaLigado(esp.Id, "D23"));
        Conferir("a outra ponta do fio tambem conta", rede.EstaLigado(led.Id, "A"));

        // Espetado na protoboard tambem deve acender.
        var c2 = new Circuito();
        var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard, X = 0, Y = 0 };
        c2.Pecas.Add(board);
        var defBoard = Catalogo.Obter(board.Tipo);
        var defLed = Catalogo.Obter("led");
        var furo = defBoard.Pinos.First(p => p.Nome == "A5");
        var posFuro = Geometria.PosicaoPino(board, defBoard, furo);
        var pinoA = defLed.Pinos.First(p => p.Nome == "A");

        var led2 = new PecaNoCircuito { Tipo = "led" };
        led2.X = posFuro.X - pinoA.X;
        led2.Y = posFuro.Y - pinoA.Y;
        c2.Pecas.Add(led2);

        var rede2 = new Netlist(c2);
        Conferir("perna no furo tambem conta como ligada",
            rede2.EstaLigado(led2.Id, "A"));

        // A marca amarela e so de placa de desenvolvimento.
        Conferir("ESP32 e placa de desenvolvimento",
            Catalogo.EhPlacaDeDesenvolvimento("esp32-devkit-v1"));
        Conferir("LED nao recebe marca de pino",
            !Catalogo.EhPlacaDeDesenvolvimento("led"));
        Conferir("resistor nao recebe marca de pino",
            !Catalogo.EhPlacaDeDesenvolvimento("resistor"));
        Conferir("protoboard nao recebe marca de pino",
            !Catalogo.EhPlacaDeDesenvolvimento(Catalogo.IdProtoboard830));
        Conferir("Arduino Uno tambem e placa",
            Catalogo.EhPlacaDeDesenvolvimento("arduino-uno"));
    }

    // ---------------- otimizar fios ----------------
    static void Fios_Otimizar()
    {
        saida.AppendLine("otimizar fios");

        var c = new Circuito();
        var esp = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        var led = new PecaNoCircuito { Tipo = "led", X = 400, Y = 0 };
        c.Pecas.Add(esp); c.Pecas.Add(led);

        var util = new Fio { DePeca = esp.Id, DePino = "D14",
                             ParaPeca = led.Id, ParaPino = "A", Cor = "#000000" };
        var repetido = new Fio { DePeca = led.Id, DePino = "A",
                                 ParaPeca = esp.Id, ParaPino = "D14", Cor = "#FF0000" };
        var praSiMesmo = new Fio { DePeca = esp.Id, DePino = "D2",
                                   ParaPeca = esp.Id, ParaPino = "D2", Cor = "#000000" };
        c.Fios.Add(util); c.Fios.Add(repetido); c.Fios.Add(praSiMesmo);

        var editor = new EditorCircuito();
        editor.Carregar(c);
        int tirados = editor.OtimizarFios();

        Conferir("tirou o repetido e o que liga em si mesmo",
            tirados == 2, $"tirou {tirados}");
        Conferir("o fio util continua",
            editor.Atual.Fios.Count == 1, $"sobraram {editor.Atual.Fios.Count}");
        Conferir("a ligacao continua valendo",
            new Netlist(editor.Atual).GpioDe(editor.Atual, led.Id, "A") == 14);
    }

    // ---------------- desempenho ----------------
    /// <summary>
    /// A rede e refeita a cada passo do arraste. Numa 830 sao ~1300
    /// furos; se isso custar caro, arrastar uma peca trava a maquina do
    /// laboratorio. Este teste mede antes de a gente descobrir na aula.
    /// </summary>
    static void Desempenho_RedeNoArraste()
    {
        saida.AppendLine("desempenho");

        var c = new Circuito();
        var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard830, X = 0, Y = 0 };
        c.Pecas.Add(board);
        c.Pecas.Add(new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = -300, Y = 0 });

        var defBoard = Catalogo.Obter(board.Tipo);
        var defLed = Catalogo.Obter("led");
        var pinoA = defLed.Pinos.First(p => p.Nome == "A");
        for (int i = 0; i < 6; i++)
        {
            var furo = defBoard.Pinos.First(p => p.Nome == $"B{4 + i * 6}");
            var pos = Geometria.PosicaoPino(board, defBoard, furo);
            c.Pecas.Add(new PecaNoCircuito
            { Tipo = "led", X = pos.X - pinoA.X, Y = pos.Y - pinoA.Y });
        }

        var relogio = System.Diagnostics.Stopwatch.StartNew();
        const int voltas = 60;
        for (int i = 0; i < voltas; i++) _ = new Netlist(c);
        relogio.Stop();

        double porVolta = relogio.Elapsed.TotalMilliseconds / voltas;
        saida.AppendLine($"        {porVolta:0.00} ms por reconstrucao da rede");

        // 60 fps da 16,6 ms por quadro. Ficar abaixo de 4 ms deixa folga
        // de sobra para o resto do desenho.
        Conferir("refazer a rede cabe num quadro", porVolta < 4.0,
            $"levou {porVolta:0.00} ms");
    }

    // ---------------- outros kits ----------------
    /// <summary>
    /// Cada placa precisa desenhar, ser reconhecida como placa e
    /// realmente acionar um componente ligado nela.
    /// </summary>
    static void Placas_OutrosKits()
    {
        saida.AppendLine("outras placas");

        foreach (var perfil in PerfilPlaca.Conhecidos)
        {
            var def = Catalogo.Obter(perfil.Id);
            if (def is null) { Conferir($"{perfil.Id}: existe no catalogo", false); continue; }

            Conferir($"{perfil.Nome}: desenha e e reconhecida",
                Pecas.Obter(perfil.Id) is not null
                && Catalogo.EhPlacaDeDesenvolvimento(perfil.Id));

            // Acha um pino de dados de verdade usando o proprio perfil.
            var pinoDados = def.Pinos.FirstOrDefault(x => perfil.Numero(x.Nome) >= 0);
            if (pinoDados is null)
            {
                Conferir($"{perfil.Nome}: tem pino de dados", false);
                continue;
            }

            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = perfil.Id, X = 0, Y = 0 };
            var led = new PecaNoCircuito { Tipo = "led", X = 500, Y = 0 };
            c.Pecas.Add(placa); c.Pecas.Add(led);
            c.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = pinoDados.Nome,
                ParaPeca = led.Id, ParaPino = "A", Cor = "#000000"
            });

            int esperado = perfil.Numero(pinoDados.Nome);
            int achado = new Netlist(c).GpioDe(c, led.Id, "A");

            saida.AppendLine(
                $"        {perfil.Nome,-24} pino {pinoDados.Nome,-5} -> GPIO {achado,-3} "
                + $"ADC 0-{perfil.MaxAdc,-4} LED_BUILTIN={perfil.LedInterno}");

            Conferir($"{perfil.Nome}: aciona componente pelo fio",
                achado == esperado, $"esperava {esperado}, veio {achado}");
        }

        // Numeracao analogica no estilo Arduino: A0 vem depois dos digitais.
        Conferir("Uno: A0 e o pino 14", PerfilPlaca.Uno.Numero("A0") == 14);
        Conferir("Mega: A0 e o pino 54", PerfilPlaca.Mega.Numero("A0") == 54);
        Conferir("Uno: pad repetido A5.2 vale como A5",
            PerfilPlaca.Uno.Numero("A5.2") == PerfilPlaca.Uno.Numero("A5"));
        Conferir("ESP32: VP e o GPIO 36", PerfilPlaca.Esp32.Numero("VP") == 36);
        Conferir("Uno recusa pino 30", PerfilPlaca.Uno.NumPinos == 20);

        // Avisos do ESP32 nao podem vazar para outras placas.
        var maqUno = new Maquina { Perfil = PerfilPlaca.Uno };
        maqUno.PinMode(7, 1);
        Conferir("Uno nao recebe aviso de flash do ESP32", maqUno.Avisos.Count == 0,
            maqUno.Avisos.Count > 0 ? maqUno.Avisos[0] : "");

        var maqEsp = new Maquina { Perfil = PerfilPlaca.Esp32 };
        maqEsp.PinMode(7, 1);
        Conferir("ESP32 continua avisando sobre o GPIO 7", maqEsp.Avisos.Count > 0);

        // O potenciometro escreve na faixa da placa, nao numa fixa.
        foreach (var (perfil, esperado) in new[]
                 { (PerfilPlaca.Esp32, 4095), (PerfilPlaca.Uno, 1023) })
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = perfil.Id, X = 0, Y = 0 };
            var pot = new PecaNoCircuito { Tipo = "potentiometer", X = 500, Y = 0, Giro = 1.0 };
            c.Pecas.Add(placa); c.Pecas.Add(pot);

            var def = Catalogo.Obter(perfil.Id);
            var pinoDados = def.Pinos.First(x => perfil.Numero(x.Nome) >= 0);
            c.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = pinoDados.Nome,
                ParaPeca = pot.Id, ParaPino = "SIG", Cor = "#000000"
            });

            var maq = new Maquina { Perfil = perfil };
            var editor = new EditorCircuito();
            editor.Carregar(c);
            editor.AplicarEntradas(maq, new Netlist(c));

            int gpio = perfil.Numero(pinoDados.Nome);
            Conferir($"{perfil.Nome}: potenciometro no maximo le {esperado}",
                maq.Analogico[gpio] == esperado, $"leu {maq.Analogico[gpio]}");
        }
    }

    // ---------------- potenciometro ----------------
    /// <summary>
    /// O slider so dava 0 ou 1: cada mudanca remontava a tab e destruia
    /// o proprio controle que estava sendo arrastado. Estes testes
    /// exigem os valores do meio.
    /// </summary>
    static void Potenciometro_Continuo()
    {
        saida.AppendLine("potenciometro");

        var c = new Circuito();
        var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        var pot = new PecaNoCircuito { Tipo = "potentiometer", X = 500, Y = 0 };
        c.Pecas.Add(placa); c.Pecas.Add(pot);
        c.Fios.Add(new Fio
        {
            DePeca = placa.Id, DePino = "D34",
            ParaPeca = pot.Id, ParaPino = "SIG", Cor = "#000000"
        });

        var editor = new EditorCircuito();
        editor.Carregar(c);
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        var rede = new Netlist(c);

        int Ler(double valor)
        {
            editor.DefinirGiro(pot, valor);
            // AplicarEntradas leva o valor da peca para a Maquina;
            // RefletirEstado so mexe no desenho.
            editor.AplicarEntradas(maq, rede);
            return maq.Analogico[34];
        }

        Conferir("meio do curso le ~2048", Math.Abs(Ler(0.5) - 2048) <= 1, $"leu {Ler(0.5)}");
        Conferir("um quarto le ~1024", Math.Abs(Ler(0.25) - 1024) <= 1, $"leu {Ler(0.25)}");
        Conferir("tres quartos le ~3071", Math.Abs(Ler(0.75) - 3071) <= 1, $"leu {Ler(0.75)}");
        Conferir("zero le 0", Ler(0) == 0);
        Conferir("cheio le 4095", Ler(1) == 4095);

        // Passos da roda do mouse somam, nao saltam para os extremos.
        editor.DefinirGiro(pot, 0);
        for (int i = 0; i < 4; i++) editor.AjustarPotenciometro(pot, 0.05);
        Conferir("quatro passos da roda dao 0,20",
            Math.Abs(pot.Giro - 0.20) < 1e-6, $"deu {pot.Giro:0.###}");

        // Nao passa dos limites.
        for (int i = 0; i < 50; i++) editor.AjustarPotenciometro(pot, 0.05);
        Conferir("nao passa de 1", Math.Abs(pot.Giro - 1) < 1e-9, $"deu {pot.Giro}");
        for (int i = 0; i < 50; i++) editor.AjustarPotenciometro(pot, -0.05);
        Conferir("nao desce abaixo de 0", Math.Abs(pot.Giro) < 1e-9, $"deu {pot.Giro}");

        // O evento avisa a interface sem remontar a tab.
        int avisos = 0;
        editor.GiroMudou += _ => avisos++;
        editor.DefinirGiro(pot, 0.42);
        editor.DefinirGiro(pot, 0.42);   // repetido nao avisa de novo
        Conferir("avisa uma vez por mudanca real", avisos == 1, $"avisou {avisos}");
    }

    // ---------------- auditoria dos componentes ----------------
    /// <summary>Monta placa + peca ligada num GPIO e devolve o circuito.</summary>
    static (Circuito c, PecaNoCircuito placa, PecaNoCircuito alvo, EditorCircuito ed, Maquina maq, Netlist rede)
        Bancada(string tipo, string pino, int gpio = 14)
    {
        var c = new Circuito();
        var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
        var alvo = new PecaNoCircuito { Tipo = tipo, X = 600, Y = 0 };
        c.Pecas.Add(placa); c.Pecas.Add(alvo);
        c.Fios.Add(new Fio
        {
            DePeca = placa.Id, DePino = $"D{gpio}",
            ParaPeca = alvo.Id, ParaPino = pino, Cor = "#000000"
        });

        var ed = new EditorCircuito();
        ed.Carregar(c);
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        return (c, placa, alvo, ed, maq, new Netlist(c));
    }

    static void Auditoria_Componentes()
    {
        saida.AppendLine("auditoria dos componentes");

        // ---- LED: acende conforme o PWM ----
        {
            var (c, _, led, ed, maq, rede) = Bancada("led", "A");
            maq.DigitalWrite(14, 1);
            ed.RefletirEstado(maq, rede);
            Conferir("LED: acende com digitalWrite HIGH", maq.Pwm[14] == 255);
            maq.DigitalWrite(14, 0);
            Conferir("LED: apaga com LOW", maq.Pwm[14] == 0);
            maq.AnalogWrite(14, 128);
            Conferir("LED: meio brilho com analogWrite", maq.Pwm[14] == 128);
        }

        // ---- Botao: apertar muda o digitalRead ----
        {
            var (c, _, bt, ed, maq, rede) = Bancada("pushbutton", "1.l");
            maq.PinMode(14, 2);                       // INPUT_PULLUP
            ed.AplicarEntradas(maq, rede);
            Conferir("Botao: solto le HIGH com pull-up", maq.DigitalRead(14) == 1);

            ed.BotoesPressionados.Add(bt.Id);
            ed.AplicarEntradas(maq, rede);
            Conferir("Botao: apertado le LOW com pull-up", maq.DigitalRead(14) == 0,
                $"leu {maq.DigitalRead(14)}");

            ed.BotoesPressionados.Clear();
            ed.AplicarEntradas(maq, rede);
            Conferir("Botao: soltar volta a HIGH", maq.DigitalRead(14) == 1);
        }

        // ---- Botao ligado pela OUTRA perna ----
        {
            var (c, _, bt, ed, maq, rede) = Bancada("pushbutton", "2.r");
            maq.PinMode(14, 2);
            ed.BotoesPressionados.Add(bt.Id);
            ed.AplicarEntradas(maq, rede);
            Conferir("Botao: funciona ligado em qualquer perna",
                maq.DigitalRead(14) == 0, $"leu {maq.DigitalRead(14)}");
        }

        // ---- Buzzer: tone / noTone ----
        {
            var (c, _, bz, ed, maq, rede) = Bancada("buzzer", "1");
            maq.Tone(14, 440);
            Conferir("Buzzer: tone define a frequencia", maq.Frequencia[14] == 440);
            maq.NoTone(14);
            Conferir("Buzzer: noTone silencia", maq.Frequencia[14] == 0);
        }

        // ---- Potenciometro ----
        {
            var (c, _, pot, ed, maq, rede) = Bancada("potentiometer", "SIG", 34);
            ed.DefinirGiro(pot, 0.5);
            ed.AplicarEntradas(maq, rede);
            Conferir("Potenciometro: meio curso le ~2048",
                Math.Abs(maq.AnalogRead(34) - 2048) <= 1, $"leu {maq.AnalogRead(34)}");
        }

        // ---- LCD ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            var lcd = maq.CriarLcd();
            lcd.Begin(16, 2);
            lcd.PosicionarCursor(0, 0);
            lcd.Escrever("Ola turma");
            Conferir("LCD: escreve na linha certa",
                lcd.LinhaTexto(0).StartsWith("Ola turma"), $"'{lcd.LinhaTexto(0)}'");
            lcd.PosicionarCursor(14, 1);
            lcd.Escrever("ABCDE");
            Conferir("LCD: nao quebra linha sozinho (como o real)",
                lcd.LinhaTexto(1).EndsWith("AB"), $"'{lcd.LinhaTexto(1)}'");
            lcd.Limpar();
            Conferir("LCD: clear apaga", lcd.LinhaTexto(0).Trim().Length == 0);
        }

        // ---- Resistor ----
        {
            var (c, _, r, ed, maq, rede) = Bancada("resistor", "1");
            Conferir("Resistor: conduz para o GPIO",
                rede.GpioDe(c, r.Id, "2") == 14, $"deu {rede.GpioDe(c, r.Id, "2")}");
        }

        // ---- inventario em tres niveis, sem enfeitar ----
        // Dizer "52 de 52 funcionam" seria mentira: alguns so aceitam as
        // chamadas para o sketch nao quebrar, sem efeito nenhum na tela.
        // Esteve com sete nomes por muitos marcos. Hoje esta vazia -- e
        // se algum dia voltar a ter um nome, que seja com o motivo
        // escrito do lado, como estava.
        var aceitosSemEfeito = Array.Empty<string>();

        var simulados = new HashSet<string>(Comportamento.Cobertos);
        foreach (string extra in new[]
        {
            "led", "pushbutton", "pushbutton-6mm", "potentiometer",
            "buzzer", "lcd1602", "lcd2004", "resistor",
            "servo", "analog-joystick", "dip-switch-8", "ky-040",
            "neopixel", "neopixel-matrix", "led-ring", "stepper-motor",
            "ssd1306", "ds1307",
            // Saidos do "aceita sem efeito" no Marco 28: o rele comuta os
            // contatos e o par infravermelho leva codigo de um lado ao
            // outro. Cobertos por Rele_Comuta e Infravermelho_Par.
            "ks2e-m-dc5", "ir-receiver", "ir-remote",
            // Marco 29: motor biaxial conta passo pelas bobinas, disco
            // manda pulso de discagem e o cartao SD tem arquivos.
            "biaxial-stepper", "rotary-dialer", "microsd-card",
            // O ultimo dos sete: o TFT tem tela de pixels de verdade.
            "ili9341",
            // Alpha 23: os motores DC giram pela conta de energia do circuito
            // (MotorDc_Fisica, MotorDc_NoCircuito).
            "motor-dc", "motor-tt", "motor-brushless",
            "l298n", "l293d", "l9110s", "tb6612fng", "drv8833", "bts7960",
            "a4988", "drv8825", "uln2003", "motor-28byj48", "micro-switch",
            "servo-mg996r", "servo-360", "esc", "receptor-rc", "radio-controle",
            "roda-tt", "helice-130", "helice-1045",
            Catalogo.IdProtoboard, Catalogo.IdProtoboard830,
        }) simulados.Add(extra);
        foreach (var perfil in PerfilPlaca.Conhecidos) simulados.Add(perfil.Id);
        foreach (string p in aceitosSemEfeito) simulados.Remove(p);

        var semNada = Catalogo.Todas.Select(p => p.Id)
            .Where(id => !simulados.Contains(id) && !aceitosSemEfeito.Contains(id))
            .OrderBy(x => x).ToList();

        saida.AppendLine($"        {simulados.Count} simulados de verdade");
        saida.AppendLine($"        {aceitosSemEfeito.Length} aceitos sem efeito: "
                       + string.Join(", ", aceitosSemEfeito));
        if (semNada.Count > 0)
            saida.AppendLine("        sem nada: " + string.Join(", ", semNada));

        Conferir("inventario bate com o catalogo",
            simulados.Count + aceitosSemEfeito.Length + semNada.Count == Catalogo.Todas.Count,
            $"{simulados.Count}+{aceitosSemEfeito.Length}+{semNada.Count} vs {Catalogo.Todas.Count}");
    }

    // ---------------- sensores da tabela ----------------
    static void Sensores_DaTabela()
    {
        saida.AppendLine("sensores da tabela");

        foreach (string tipo in Comportamento.Cobertos.OrderBy(x => x))
        {
            var comp = Comportamento.De(tipo);
            var def = Catalogo.Obter(tipo);
            Conferir($"{tipo}: existe no catalogo", def is not null);
            if (def is null) continue;

            // Todo pino citado na tabela tem que existir mesmo na peca.
            foreach (string nome in comp.Analogicos.Concat(comp.Digitais).Concat(comp.Saidas))
                Conferir($"{tipo}: pino {nome} existe",
                    def.Pinos.Any(p => p.Nome == nome));
        }

        // ---- analogico: a luz do controle vira a leitura do modulo ----
        //
        // Com a mesma fisica do modulo de verdade: mais luz, menos tensao.
        // A prova e a formula do exemplo do Wokwi, que a turma copia:
        // aplicada na leitura, ela tem que devolver a luz do controle.
        {
            var (c, _, ldr, ed, maq, rede) = Bancada("photoresistor-sensor", "AO", 34);
            ed.DefinirGiro(ldr, 0.75);
            ed.AplicarEntradas(maq, rede);
            int claro = maq.AnalogRead(34);
            ed.DefinirGiro(ldr, 0.25);
            ed.AplicarEntradas(maq, rede);
            int escuro = maq.AnalogRead(34);

            static double LuxDaFormula(int leitura)
            {
                double f = leitura / 4095.0;
                double r = 10000 * f / (1 - f);
                return Math.Pow(50 * 1e3 * Math.Pow(10, 0.7) / r, 1 / 0.7);
            }

            double luz = Comportamento.De("photoresistor-sensor").ValorReal(0.75);
            saida.AppendLine($"        LDR: {luz:0} lux le {claro}, {Comportamento.De("photoresistor-sensor").ValorReal(0.25):0.0} "
                             + $"lux le {escuro}; a formula do tutorial da {LuxDaFormula(claro):0} lux");
            Conferir("LDR: mais luz, leitura MENOR (como no modulo de verdade)",
                claro < escuro, $"claro {claro}, escuro {escuro}");
            Conferir("LDR: a formula do tutorial devolve a luz do controle",
                Math.Abs(LuxDaFormula(claro) - luz) / luz < 0.03, $"{LuxDaFormula(claro):0} de {luz:0}");
        }

        // ---- NTC: o controle e a temperatura, e a formula a devolve ----
        {
            var (c, _, ntc, ed, maq, rede) = Bancada("ntc-temperature-sensor", "OUT", 34);
            foreach (double graus in new[] { -10.0, 24, 45, 75 })
            {
                ed.DefinirGiro(ntc, (graus + 24) / 104);
                ed.AplicarEntradas(maq, rede);
                int leitura = maq.AnalogRead(34);
                double celsius = 1 / (Math.Log(1 / (4095.0 / leitura - 1)) / 3950 + 1.0 / 298.15) - 273.15;
                Conferir($"NTC: {graus} graus no controle, a formula do tutorial le {celsius:0.0}",
                    Math.Abs(celsius - graus) < 0.5, $"leitura {leitura}");
            }
        }

        // ---- digital invertido: detectar leva o pino a LOW ----
        {
            var (c, _, ldr, ed, maq, rede) = Bancada("photoresistor-sensor", "DO", 15);
            maq.PinMode(15, 2);

            ed.DefinirGiro(ldr, 0.9);
            ed.AplicarEntradas(maq, rede);
            Conferir("LDR: acima do limiar a saida digital vai a LOW",
                maq.DigitalRead(15) == 0, $"leu {maq.DigitalRead(15)}");

            ed.DefinirGiro(ldr, 0.1);
            ed.AplicarEntradas(maq, rede);
            Conferir("LDR: abaixo do limiar volta a HIGH",
                maq.DigitalRead(15) == 1, $"leu {maq.DigitalRead(15)}");
        }

        // ---- chave simples: PIR ----
        {
            var (c, _, pir, ed, maq, rede) = Bancada("pir-motion-sensor", "OUT", 15);
            maq.PinMode(15, 0);

            ed.DefinirGiro(pir, 1);
            ed.AplicarEntradas(maq, rede);
            Conferir("PIR: com movimento le HIGH", maq.DigitalRead(15) == 1);

            ed.DefinirGiro(pir, 0);
            ed.AplicarEntradas(maq, rede);
            Conferir("PIR: sem movimento le LOW", maq.DigitalRead(15) == 0);
        }

        // ---- saida multipla: LED RGB ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var rgb = new PecaNoCircuito { Tipo = "rgb-led", X = 600, Y = 0 };
            c.Pecas.Add(placa); c.Pecas.Add(rgb);
            foreach (var (pino, gpio) in new[] { ("R", 14), ("G", 13), ("B", 12) })
                c.Fios.Add(new Fio
                {
                    DePeca = placa.Id, DePino = $"D{gpio}",
                    ParaPeca = rgb.Id, ParaPino = pino, Cor = "#000000"
                });

            var rede = new Netlist(c);
            Conferir("LED RGB: os tres canais chegam em GPIOs diferentes",
                rede.GpioDe(c, rgb.Id, "R") == 14
                && rede.GpioDe(c, rgb.Id, "G") == 13
                && rede.GpioDe(c, rgb.Id, "B") == 12);
        }

        // ---- barra de LEDs: 10 segmentos independentes ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var barra = new PecaNoCircuito { Tipo = "led-bar-graph", X = 600, Y = 0 };
            c.Pecas.Add(placa); c.Pecas.Add(barra);
            c.Fios.Add(new Fio
            {
                DePeca = placa.Id, DePino = "D14",
                ParaPeca = barra.Id, ParaPino = "A3", Cor = "#000000"
            });

            var rede = new Netlist(c);
            Conferir("Barra de LEDs: segmento ligado responde",
                rede.GpioDe(c, barra.Id, "A3") == 14);
            Conferir("Barra de LEDs: segmento nao ligado fica solto",
                rede.GpioDe(c, barra.Id, "A7") == -1);
        }
    }

    // ---------------- servo, joystick, DIP, encoder ----------------
    static void Lote2_ServoEDisplays()
    {
        saida.AppendLine("servo e companhia");

        // ---- Servo pelo objeto do sketch ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            var prog = new Parser(Lexer.Tokenizar("""
                #include <Servo.h>
                Servo s;
                void setup() { s.attach(25); s.write(137); }
                void loop() {}
                """)).AnalisarPrograma();
            var interp = new Interpretador(prog, maq);
            interp.Preparar();
            interp.Setup();

            Conferir("Servo: attach + write guardam o angulo",
                maq.AnguloServo[25] == 137, $"ficou {maq.AnguloServo[25]}");
        }

        // ---- writeMicroseconds ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            var prog = new Parser(Lexer.Tokenizar("""
                Servo s;
                void setup() { s.attach(4); s.writeMicroseconds(1500); }
                void loop() {}
                """)).AnalisarPrograma();
            var interp = new Interpretador(prog, maq);
            interp.Preparar(); interp.Setup();
            // Com os limites de fabrica da Servo.h (544 a 2400 us), 1500 us
            // e 92,7 graus -- e e por isso que o write(90) da biblioteca
            // manda 1472 us, e nao 1500. Este teste esperava 90 pela conta
            // antiga (1000 a 2000 us), que nao e a de biblioteca nenhuma.
            Conferir("Servo: 1500 us da 93 graus, pela conta da Servo.h",
                maq.AnguloServo[4] == 93, $"ficou {maq.AnguloServo[4]}");
        }

        // ---- LCD 20x4 ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            var lcd = maq.CriarLcd();
            lcd.Begin(20, 4);
            lcd.PosicionarCursor(0, 3);
            lcd.Escrever("linha quatro");
            Conferir("LCD 20x4: escreve na quarta linha",
                lcd.LinhaTexto(3).StartsWith("linha quatro"), $"'{lcd.LinhaTexto(3)}'");
            Conferir("LCD 20x4: tem 20 colunas", lcd.LinhaTexto(0).Length == 20);
        }

        // ---- Joystick: dois eixos ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var joy = new PecaNoCircuito
            { Tipo = "analog-joystick", X = 600, Y = 0, Giro = 0.25, Giro2 = 0.75 };
            c.Pecas.Add(placa); c.Pecas.Add(joy);
            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D34",
                ParaPeca = joy.Id, ParaPino = "HORZ", Cor = "#000000" });
            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D35",
                ParaPeca = joy.Id, ParaPino = "VERT", Cor = "#000000" });

            var ed = new EditorCircuito(); ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            ed.AplicarEntradas(maq, new Netlist(c));

            Conferir("Joystick: eixo X independente",
                Math.Abs(maq.AnalogRead(34) - 1024) <= 2, $"leu {maq.AnalogRead(34)}");
            Conferir("Joystick: eixo Y independente",
                Math.Abs(maq.AnalogRead(35) - 3071) <= 2, $"leu {maq.AnalogRead(35)}");
        }

        // ---- Chave DIP: cada chave e independente ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var dip = new PecaNoCircuito { Tipo = "dip-switch-8", X = 600, Y = 0 };
            c.Pecas.Add(placa); c.Pecas.Add(dip);
            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D14",
                ParaPeca = dip.Id, ParaPino = "3a", Cor = "#000000" });

            var ed = new EditorCircuito(); ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            var rede = new Netlist(c);

            ed.AlternarChaveDip(dip, 2, true);      // chave 3
            ed.AplicarEntradas(maq, rede);
            Conferir("DIP: chave ligada leva o pino a LOW", maq.DigitalRead(14) == 0);

            ed.AlternarChaveDip(dip, 2, false);
            ed.AplicarEntradas(maq, rede);
            Conferir("DIP: chave desligada volta a HIGH", maq.DigitalRead(14) == 1);

            ed.AlternarChaveDip(dip, 5, true);      // outra chave nao afeta
            ed.AplicarEntradas(maq, rede);
            Conferir("DIP: chaves sao independentes", maq.DigitalRead(14) == 1);
        }
    }

    // ---------------- bibliotecas ----------------
    static Maquina Rodar(string codigo)
    {
        var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
        var prog = new Parser(Lexer.Tokenizar(codigo)).AnalisarPrograma();
        var interp = new Interpretador(prog, maq);
        interp.Preparar();
        interp.Setup();
        return maq;
    }

    static void Lote3_Bibliotecas()
    {
        saida.AppendLine("bibliotecas");

        // ---- HC-SR04 via pulseIn ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var sonar = new PecaNoCircuito { Tipo = "hc-sr04", X = 600, Y = 0, Giro = 0.25 };
            c.Pecas.Add(placa); c.Pecas.Add(sonar);
            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D27",
                ParaPeca = sonar.Id, ParaPino = "ECHO", Cor = "#000000" });

            var ed = new EditorCircuito(); ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            ed.AplicarEntradas(maq, new Netlist(c));

            // 25% de 400 cm = 100 cm; ida e volta a 58 us/cm.
            Conferir("HC-SR04: distancia vira tempo de pulso",
                Math.Abs(maq.Leitura("27:pulso") - 5800) < 1,
                $"deu {maq.Leitura("27:pulso")}");
            Conferir("HC-SR04: a conta volta a distancia certa",
                Math.Abs(maq.Leitura("27:pulso") / 58 - 100) < 0.1);
        }

        // ---- DHT ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 0, Y = 0 };
            var dht = new PecaNoCircuito
            { Tipo = "dht22", X = 600, Y = 0, Giro = 0.5, Giro2 = 0.4 };
            c.Pecas.Add(placa); c.Pecas.Add(dht);
            c.Fios.Add(new Fio { DePeca = placa.Id, DePino = "D4",
                ParaPeca = dht.Id, ParaPino = "SDA", Cor = "#000000" });

            var ed = new EditorCircuito(); ed.Carregar(c);
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            ed.AplicarEntradas(maq, new Netlist(c));

            // Faixa -40..80: meio do curso da 20 graus.
            Conferir("DHT: temperatura vem do controle",
                Math.Abs(maq.Leitura("4:temperatura") - 20) < 0.1,
                $"deu {maq.Leitura("4:temperatura")}");
            Conferir("DHT: umidade vem do segundo eixo",
                Math.Abs(maq.Leitura("4:umidade") - 50) < 0.1,
                $"deu {maq.Leitura("4:umidade")}");
        }

        // ---- NeoPixel ----
        {
            var maq = Rodar("""
                Adafruit_NeoPixel fita(8, 5, NEO_GRB + NEO_KHZ800);
                void setup() {
                  fita.begin();
                  fita.setPixelColor(0, fita.Color(255, 0, 0));
                  fita.setPixelColor(3, fita.Color(0, 128, 64));
                  fita.show();
                }
                void loop() {}
                """);

            var fita = maq.Fitas.GetValueOrDefault(5);
            Conferir("NeoPixel: fita registrada no pino", fita is not null);
            Conferir("NeoPixel: 8 LEDs", fita?.Quantidade == 8);
            Conferir("NeoPixel: LED 0 vermelho", fita?.Cores[0] == 0xFF0000,
                $"deu {fita?.Cores[0]:X6}");
            Conferir("NeoPixel: LED 3 com verde e azul", fita?.Cores[3] == 0x008040,
                $"deu {fita?.Cores[3]:X6}");
            Conferir("NeoPixel: LED nao tocado fica apagado", fita?.Cores[5] == 0);
        }

        // ---- Motor de passo ----
        {
            var maq = Rodar("""
                Stepper motor(200, 14, 27, 26, 25);
                void setup() { motor.setSpeed(60); motor.step(1); }
                void loop() {}
                """);
            // O Stepper.h de 4 fios liga DUAS bobinas por passo (1010, 0110,
            // 0101, 1001): o passo completo, de torque cheio. Depois de
            // step(1) a sequencia esta no 0110.
            int acesos = 0;
            foreach (int p in new[] { 14, 27, 26, 25 }) if (maq.Digital[p] == 1) acesos++;
            Conferir("Stepper: duas bobinas acesas por passo, como no Stepper.h",
                acesos == 2 && maq.Digital[27] == 1 && maq.Digital[26] == 1, $"acesas {acesos}");
        }

        // ---- Celula de carga: o caminho da biblioteca ----
        //
        // Crua, a leitura NAO e grama: e a contagem do conversor. So com o
        // tare() (o zero) e o set_scale() (a calibracao) o get_units() vira
        // grama -- como na bancada, onde o sketch sem calibrar mostra lixo.
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Esp32 };
            maq.Leituras["hx711:peso"] = 0;
            var prog = new Parser(Lexer.Tokenizar("""
                HX711 balanca;
                void setup() {
                  Serial.begin(115200);
                  balanca.begin(4, 5);
                  balanca.set_scale(420);
                  balanca.tare();
                }
                void loop() {
                  Serial.println(balanca.get_units(10), 0);
                  Serial.println(balanca.read());
                }
                """)).AnalisarPrograma();
            var interp = new Interpretador(prog, maq);
            interp.Preparar(); interp.Setup();

            maq.Leituras["hx711:peso"] = 1250;
            interp.Loop();
            var linhas = LinhasDoSerial(maq);
            saida.AppendLine("        hx711: " + string.Join(" | ", linhas));

            Conferir("HX711: calibrado e tarado, o get_units() sai em gramas",
                linhas.ElementAtOrDefault(0) == "1250", string.Join(" | ", linhas));
            Conferir("HX711: o read() cru e contagem, e nao grama",
                long.TryParse(linhas.ElementAtOrDefault(1), out long cru) && cru > 100000,
                linhas.ElementAtOrDefault(1));
        }

        // ---- Relogio: o DateTime do RTClib ----
        {
            var maq = new Maquina { Perfil = PerfilPlaca.Uno };
            var prog = new Parser(Lexer.Tokenizar("""
                #include <RTClib.h>
                RTC_DS1307 rtc;
                void setup() {
                  Serial.begin(9600);
                  rtc.begin();
                  rtc.adjust(DateTime(2026, 9, 24, 23, 59, 58));
                }
                void loop() {
                  DateTime agora = rtc.now();
                  Serial.print(agora.day()); Serial.print('/');
                  Serial.print(agora.month()); Serial.print('/');
                  Serial.print(agora.year()); Serial.print(' ');
                  Serial.print(agora.hour()); Serial.print(':');
                  Serial.print(agora.minute()); Serial.print(':');
                  Serial.println(agora.second());
                  delay(3000);
                }
                """)).AnalisarPrograma();
            var interp = new Interpretador(prog, maq);
            interp.Preparar(); interp.Setup();
            interp.Loop();
            interp.Loop();
            var linhas = LinhasDoSerial(maq);
            saida.AppendLine("        rtc: " + string.Join(" | ", linhas));

            Conferir("RTC: now() devolve o DateTime acertado no adjust()",
                linhas.ElementAtOrDefault(0) == "24/9/2026 23:59:58", string.Join(" | ", linhas));
            Conferir("RTC: o relogio anda sozinho e vira o dia",
                linhas.ElementAtOrDefault(1) == "25/9/2026 0:0:1", string.Join(" | ", linhas));

            var data = new Parser(Lexer.Tokenizar("""
                void setup() {
                  Serial.begin(9600);
                  DateTime compilado(F(__DATE__), F(__TIME__));
                  Serial.println(compilado.year());
                  Serial.println(compilado.dayOfTheWeek());
                }
                void loop() {}
                """)).AnalisarPrograma();
            var maqData = new Maquina { Perfil = PerfilPlaca.Uno };
            var interpData = new Interpretador(data, maqData);
            interpData.Preparar(); interpData.Setup();
            var linhasData = LinhasDoSerial(maqData);
            Conferir("RTC: DateTime(F(__DATE__), F(__TIME__)) le a data da compilacao",
                linhasData.ElementAtOrDefault(0) == DateTime.Now.Year.ToString()
                && linhasData.ElementAtOrDefault(1) == ((int)DateTime.Now.DayOfWeek).ToString(),
                string.Join(" | ", linhasData));
        }

        // ---- OLED como tela de texto ----
        {
            var maq = Rodar("""
                Adafruit_SSD1306 tela(128, 64);
                void setup() {
                  tela.begin(SSD1306_SWITCHCAPVCC, 60);
                  tela.clearDisplay();
                  tela.setCursor(0, 0);
                  tela.print("IoT ok");
                  tela.display();
                }
                void loop() {}
                """);
            Conferir("SSD1306: escreve na tela",
                maq.Oled is not null && maq.Oled.LinhaTexto(0).StartsWith("IoT ok"),
                maq.Oled?.LinhaTexto(0) ?? "sem tela");
        }

        // ---- constantes nao podem faltar ----
        {
            var maq = Rodar("""
                void setup() { int a = DHT22 + NEO_GRB + WHITE + RISING; }
                void loop() {}
                """);
            Conferir("constantes de biblioteca existem", true);
        }
    }

    // ---------------- clique nos botoes ----------------
    /// <summary>
    /// O usuario diz que nao consegue apertar os botoes. Estes testes
    /// perguntam ao Resolver o que acontece em cada ponto -- e ele e a
    /// MESMA funcao que o clique usa.
    /// </summary>
    static void Clique_NosBotoes()
    {
        saida.AppendLine("clique nos botoes");

        // ---- pushbutton solto no canvas ----
        {
            var c = new Circuito();
            var bt = new PecaNoCircuito { Tipo = "pushbutton", X = 200, Y = 200 };
            c.Pecas.Add(bt);
            var def = Catalogo.Obter("pushbutton");
            var ed = new EditorCircuito();
            ed.Carregar(c);

            var centro = new System.Windows.Point(
                bt.X + def.Largura / 2, bt.Y + def.Altura / 2);
            var a = ed.Resolver(centro);
            saida.AppendLine($"        centro do botao -> {a.Camada} {a.Pino}");
            Conferir("botao: o centro aperta",
                a.Camada == EditorCircuito.Camada.CapaBotao, $"veio {a.Camada} ({a.Pino})");

            // Varre a peca inteira: a capa aperta, o resto arrasta, as
            // bordas ligam fio. Os tres precisam sobrar espaco.
            int capa = 0, corpo = 0, pino = 0, outro = 0;
            for (int gx = 0; gx <= 10; gx++)
                for (int gy = 0; gy <= 10; gy++)
                {
                    var p = new System.Windows.Point(
                        bt.X + def.Largura * gx / 10.0,
                        bt.Y + def.Altura * gy / 10.0);
                    switch (ed.Resolver(p).Camada)
                    {
                        case EditorCircuito.Camada.CapaBotao: capa++; break;
                        case EditorCircuito.Camada.CorpoPeca: corpo++; break;
                        case EditorCircuito.Camada.PinoPeca: pino++; break;
                        default: outro++; break;
                    }
                }
            saida.AppendLine($"        area do botao: {capa} capa, {corpo} corpo, "
                             + $"{pino} pino, {outro} outro (de 121)");
            Conferir("botao: a capa e um alvo grande o bastante", capa >= 12,
                $"so {capa} de 121");
            Conferir("botao: ainda sobra corpo para arrastar", corpo >= 20,
                $"so {corpo} de 121");
        }

        // ---- pushbutton espetado na protoboard ----
        {
            var c = new Circuito();
            var board = new PecaNoCircuito { Tipo = Catalogo.IdProtoboard, X = 0, Y = 0 };
            c.Pecas.Add(board);
            var defBoard = Catalogo.Obter(board.Tipo);
            var defBt = Catalogo.Obter("pushbutton");

            var furo = defBoard.Pinos.First(x => x.Nome == "C10");
            var pos = Geometria.PosicaoPino(board, defBoard, furo);
            var pino1 = defBt.Pinos.First(x => x.Nome == "1.l");
            var bt = new PecaNoCircuito
            { Tipo = "pushbutton", X = pos.X - pino1.X, Y = pos.Y - pino1.Y };
            c.Pecas.Add(bt);

            var ed = new EditorCircuito();
            ed.Carregar(c);

            var centro = new System.Windows.Point(
                bt.X + defBt.Largura / 2, bt.Y + defBt.Altura / 2);
            var a = ed.Resolver(centro);
            saida.AppendLine($"        botao na protoboard -> {a.Camada} {a.Pino}");
            Conferir("botao na protoboard: centro ainda aperta",
                a.Camada == EditorCircuito.Camada.CapaBotao, $"veio {a.Camada} ({a.Pino})");
        }

        // ---- toda peca apertavel tem capa, e ela cabe na peca ----
        foreach (string tipo in new[]
                 { "pushbutton", "pushbutton-6mm", "analog-joystick", "ky-040" })
        {
            var c = new Circuito();
            var peca = new PecaNoCircuito { Tipo = tipo, X = 200, Y = 200 };
            c.Pecas.Add(peca);
            var def = Catalogo.Obter(tipo);
            var ed = new EditorCircuito();
            ed.Carregar(c);

            // A capa nao fica no centro de toda peca: o knob do KY-040
            // e da esquerda. O centro declarado veio do SVG; o que o
            // teste guarda e o ROTEAMENTO ate ele e o encaixe na caixa.
            var capa = EditorCircuito.CapasDeBotao[tipo];
            var alvoCapa = new System.Windows.Point(peca.X + capa.X, peca.Y + capa.Y);
            var a = ed.Resolver(alvoCapa);
            saida.AppendLine($"        {tipo}: capa em ({capa.X};{capa.Y}) r{capa.R} "
                             + $"na caixa {def.Largura}x{def.Altura} -> {a.Camada}");
            Conferir($"{tipo}: a capa aperta",
                a.Camada == EditorCircuito.Camada.CapaBotao, $"veio {a.Camada}");
            Conferir($"{tipo}: a capa cabe dentro da peca",
                capa.X - capa.R >= -1 && capa.Y - capa.R >= -1
                && capa.X + capa.R <= def.Largura + 1
                && capa.Y + capa.R <= def.Altura + 1,
                $"capa ({capa.X};{capa.Y}) r{capa.R} vaza de {def.Largura}x{def.Altura}");

            // A capa nao pode engolir os pinos: sem eles nao da para ligar.
            int pinosVivos = def.Pinos.Count(x =>
            {
                var q = Geometria.PosicaoPino(peca, def, x);
                return ed.Resolver(q).Camada == EditorCircuito.Camada.PinoPeca;
            });
            Conferir($"{tipo}: os pinos continuam clicaveis",
                pinosVivos == def.Pinos.Count,
                $"{pinosVivos} de {def.Pinos.Count}");
        }

        // ---- BOOT e EN da placa ----
        {
            var c = new Circuito();
            var placa = new PecaNoCircuito { Tipo = "esp32-devkit-v1", X = 100, Y = 100 };
            c.Pecas.Add(placa);
            var def = Catalogo.Obter(placa.Tipo);
            var ed = new EditorCircuito();
            ed.Carregar(c);

            foreach (var (bx, by, nome) in new[]
                     { (23.25, 183.5, "EN"), (83.15, 183.5, "BOOT") })
            {
                var p = Geometria.PosicaoLocal(placa, def, bx, by);
                var a = ed.Resolver(p);
                saida.AppendLine($"        {nome} -> {a.Camada} {a.Botao ?? a.Pino}");
                Conferir($"placa: {nome} e reconhecido",
                    a.Camada == EditorCircuito.Camada.BotaoPlaca && a.Botao == nome,
                    $"veio {a.Camada} ({a.Botao ?? a.Pino})");
            }
        }
    }

    // ---------------- rotacao ----------------
    static void Rotacao_MovePinos()
    {
        saida.AppendLine("rotacao");
        var def = Catalogo.Obter("led");
        var pino = def.Pinos.First(p => p.Nome == "A");

        var reto = new PecaNoCircuito { Tipo = "led", X = 0, Y = 0 };
        var girado = new PecaNoCircuito { Tipo = "led", X = 0, Y = 0, Rotacao = 90 };

        var a = Geometria.PosicaoPino(reto, def, pino);
        var b = Geometria.PosicaoPino(girado, def, pino);
        Conferir("girar 90 muda a posicao do pino", (a - b).Length > 5);

        var voltou = new PecaNoCircuito { Tipo = "led", X = 0, Y = 0, Rotacao = 360 };
        var d = Geometria.PosicaoPino(voltou, def, pino);
        Conferir("girar 360 volta ao mesmo lugar", (a - d).Length < 0.01);

        var espelhado = new PecaNoCircuito { Tipo = "led", X = 0, Y = 0, Espelhada = true };
        var esp = Geometria.PosicaoPino(espelhado, def, pino);
        Conferir("espelhar move o pino no eixo X", Math.Abs(esp.X - a.X) > 5);
    }
}
