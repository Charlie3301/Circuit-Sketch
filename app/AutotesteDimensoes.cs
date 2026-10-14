namespace Simulador.App;

/// <summary>
/// Vetores de tres ou mais dimensoes, guardados esticados como o C guarda,
/// e os pedacos deles ("cubo[1]") lendo e escrevendo no vetor de verdade.
/// </summary>
public static partial class Autoteste
{
    static void Motor_VetorDeDimensoes()
    {
        saida.AppendLine("vetores de varias dimensoes");

        var (_, serial, erro) = RodarComPinos("""
            int cubo[2][3][4];
            const byte quadros[2][3][4] = {
              {{1, 2, 3, 4}, {5, 6, 7, 8}, {9, 10, 11, 12}},
              {{13, 14, 15, 16}, {17, 18, 19, 20}, {21, 22, 23, 24}}
            };
            const char nomes[2][3][8] = {{"um", "dois", "tres"}, {"quatro", "cinco", "seis"}};
            int grade[3][4] = {{1, 2}, {3, 4, 5, 6}};

            void preencher(int c[2][3][4]) {
              for (int i = 0; i < 2; i++)
                for (int j = 0; j < 3; j++)
                  for (int k = 0; k < 4; k++) c[i][j][k] = i * 100 + j * 10 + k;
            }
            void zerarPlano(int p[3][4]) {
              for (int j = 0; j < 3; j++)
                for (int k = 0; k < 4; k++) p[j][k] = 0;
            }
            int somaGrade(int m[][4], int linhas) {
              int s = 0;
              for (int i = 0; i < linhas; i++)
                for (int j = 0; j < 4; j++) s += m[i][j];
              return s;
            }
            void trocar(int &a, int &b) { int t = a; a = b; b = t; }

            void setup() {
              Serial.begin(115200);
              preencher(cubo);
              Serial.println(cubo[1][2][3]);
              Serial.println(quadros[1][0][2]);
              Serial.println(sizeof(cubo));
              Serial.println(sizeof(cubo[0]));
              Serial.println(sizeof(cubo[0][0]));
              cubo[0][1][2] += 5;
              Serial.println(cubo[0][1][2]);
              int soma = 0;
              for (auto &plano : quadros) for (auto &linha : plano) for (byte v : linha) soma += v;
              Serial.println(soma);
              zerarPlano(cubo[1]);
              Serial.println(cubo[1][2][3] + cubo[1][0][0] + cubo[0][2][3]);
              for (auto &plano : cubo) for (auto &linha : plano) for (auto &v : linha) v = 7;
              Serial.println(cubo[1][1][1] + cubo[0][2][3]);
              Serial.println(nomes[1][2]);
              Serial.println(somaGrade(grade, 3));
              trocar(grade[0][0], grade[1][3]);
              Serial.println(grade[0][0] * 10 + grade[1][3]);
            }
            void loop() {}
            """, "esp32-devkit-v1", 1000);
        string s = string.Join(" | ", serial);
        saida.AppendLine($"        {s} {erro}");
        string[] esperado = ["123", "15", "96", "48", "16", "17", "300", "23", "14", "seis", "21", "61"];
        string[] nomesDosItens =
        [
            "item de um vetor 3D preenchido numa funcao", "inicializacao com chaves por plano e linha",
            "sizeof do vetor 3D", "sizeof de um plano", "sizeof de uma linha do plano", "+= num item 3D",
            "for (auto &x : ...) aninhado percorre plano, linha e item", "pedaco cubo[1] passado a funcao zera o plano de verdade",
            "for com & escreve no vetor", "texto numa linha de um vetor de char 3D",
            "vetor 2D numa funcao com int m[][4] (antes as dimensoes se perdiam)", "int& num item de vetor 2D troca de verdade",
        ];
        for (int i = 0; i < esperado.Length; i++)
            Conferir($"dimensoes: {nomesDosItens[i]}", erro is null && serial.Count > i && serial[i] == esperado[i],
                     serial.Count > i ? $"{serial[i]} (esperado {esperado[i]})" : erro ?? "faltou linha");

        // No Uno o int tem 2 bytes.
        var (_, serialUno, _) = RodarComPinos("int c[2][3][4];\nvoid setup() { Serial.begin(9600); Serial.println(sizeof(c)); Serial.println(sizeof(c[1][2])); }\nvoid loop() {}",
                                              "arduino-uno", 500);
        Conferir("dimensoes: no Uno o int[2][3][4] tem 48 bytes e a linha, 8",
            serialUno.Count >= 2 && serialUno[0] == "48" && serialUno[1] == "8", string.Join(" | ", serialUno));

        // Atribuir a um pedaco nao compila.
        var (_, _, erroPedaco) = RodarComPinos("int c[2][3][4];\nvoid setup() { c[1][2] = 5; }\nvoid loop() {}", "esp32-devkit-v1", 500);
        Conferir("dimensoes: atribuir num pedaco (c[1][2] = 5) da o erro do C++",
            erroPedaco?.Contains("not assignable") == true, erroPedaco ?? "rodou");

        // Itens demais na inicializacao.
        var (_, _, erroItens) = RodarComPinos("int c[2][2][2] = {{{1, 2, 3}}};\nvoid setup() {}\nvoid loop() {}", "esp32-devkit-v1", 500);
        Conferir("dimensoes: linha com itens demais da erro", erroItens?.Contains("excess elements") == true, erroItens ?? "rodou");
    }
}
