// BASE ZERO -- tiro em primeira pessoa no estilo do DOOM (1993), feito com
// raycasting numa ESP32-S3 e num display ILI9341 de 320x240.
//
// Como sai o 3D: para cada coluna da tela sai um raio do jogador. O raio
// anda pelo mapa de quadrado em quadrado (o algoritmo DDA) ate bater numa
// parede, e a distancia do batido diz a altura da parede naquela coluna.
// Sao 160 raios por quadro, um a cada duas colunas -- e so as colunas que
// mudaram vao para o display, que e o que deixa o SPI dar conta.
//
// As fases estao no cartao microSD: /E1M1.TXT e /E1M2.TXT. Abra a peca do
// cartao (Arquivos...), mude as paredes e rode de novo: a fase muda. Sem
// cartao, o jogo usa a fase que esta no proprio codigo.
//
// Controles: as setas andam e viram, A atira, B abre as portas e aperta o
// painel verde da saida, START pausa.
//
// A chave liga e desliga o console de verdade. Desligada, a ESP32 apaga a
// luz da tela e entra no sono profundo (o chip gasta microamperes); ligada,
// ela acorda e volta para a fase -- a fase e os pontos ficam na memoria
// RTC, que o sono profundo nao apaga. O recorde fica no cartao.
//
// Na bancada: o display e o cartao dividem o SPI (SCK 12, MOSI 11, MISO
// 13), cada um com o seu CS. O LED do display vai num pino para o jogo
// poder apagar a luz de fundo -- no modulo vermelho ele passa por um
// transistor, e o GPIO so manda o sinal. Ligado direto no 3V3, a tela
// ficaria acesa com a placa dormindo.

#include <SPI.h>
#include <SD.h>
#include <Adafruit_GFX.h>
#include <Adafruit_ILI9341.h>
#include "driver/rtc_io.h"

// ---------------- pinos ----------------
const int TFT_CS = 10;
const int TFT_DC = 9;
const int TFT_RST = 8;
const int TFT_LUZ = 7;        // o LED do display: a luz de fundo
const int SD_CS = 14;
const int BT_CIMA = 4;
const int BT_BAIXO = 5;
const int BT_ESQ = 6;
const int BT_DIR = 15;
const int BT_START = 16;      // pausa
const int BT_A = 17;          // atirar
const int BT_B = 18;          // abrir porta, apertar a saida
const int BUZZER = 21;
const int CHAVE = 1;          // chave liga/desliga: ligada = LOW

Adafruit_ILI9341 tft(TFT_CS, TFT_DC, TFT_RST);

// ---------------- tela ----------------
const int LARG = 320;
const int VISTA = 192;        // a vista 3D; embaixo fica o painel
const int COL = 2;            // cada raio pinta duas colunas
const int RAIOS = LARG / COL;
const int HORIZONTE = VISTA / 2;

const uint16_t PRETO = 0x0000;
const uint16_t BRANCO = 0xFFFF;
const uint16_t VERMELHO = 0xF800;
const uint16_t AMARELO = 0xFFE0;
const uint16_t PAINEL = 0x31A6;
const uint16_t PAINEL_CLARO = 0x6B4D;
const uint16_t LETREIRO = 0xBDF7;

// Teto e chao em faixas: mais escuros perto do horizonte, que e longe.
uint16_t tetoLonge, tetoPerto, chaoLonge, chaoMedio, chaoPerto;

// Cada tipo de parede tem 8 tons, do perto (claro) ao longe (escuro).
const int TIPOS = 5;          // # concreto, B tijolo, M metal, D porta, X saida
uint16_t tons[TIPOS][8];

// ---------------- o mapa ----------------
const int TAM = 16;
char mapa[TAM][TAM + 1];
char nomeDaFase[32];

// A fase que vem no codigo, para jogar sem cartao.
const char* FASE_INTERNA[TAM] = {
  "################",
  "#P..#......R...#",
  "#...#..........#",
  "#...D...BBBB...#",
  "#...#...B..B.A.#",
  "##D###..B..B...#",
  "#.......BDBB...#",
  "#..R...........#",
  "#.......MMMMMM.#",
  "#..V....M....M.#",
  "#.......M.R..M.#",
  "######..M....D.#",
  "#....#..MMMMMM.#",
  "#.A..D......R..#",
  "#....#.......V.X",
  "################"
};

// ---------------- robos, kits e municao ----------------
struct Coisa {
  float x, y;
  char tipo;                  // 'R' robo, 'V' kit de vida, 'A' municao
  int vida;
  bool ativa;                 // pega ou destruida: nao e mais do jogo
  unsigned long ultimoAtaque;
};
const int MAX_COISAS = 24;
Coisa coisas[MAX_COISAS];
int nCoisas = 0;

// Os desenhos: 12x16 quadradinhos, uma letra por cor, '.' transparente.
const char* ROBO[16] = {
  "....kkkk....",
  "...kmmmmk...",
  "..kmmrrmmk..",
  "..kmmrrmmk..",
  "...kmmmmk...",
  "....kkkk....",
  ".kkbbbbbbkk.",
  "kbbbbllbbbbk",
  "kb.bbllbb.bk",
  "kb.bbbbbb.bk",
  "kk.bbbbbb.kk",
  "...bbkkbb...",
  "...bb..bb...",
  "...kk..kk...",
  "..kkk..kkk..",
  "............"
};
const char* SUCATA[16] = {
  "............", "............", "............", "............",
  "............", "............", "............", "............",
  "............", "............", "............", "....k..k....",
  "...kbrbk.k..", "..kbbmbbbk..", ".kkbbkbbrbk.", "kkkkkkkkkkkk"
};
const char* KIT[16] = {
  "............", "............", "............", "............",
  "............", "............", "............", "............",
  ".kkkkkkkkkk.", ".kwwwrrwwwk.", ".kwwwrrwwwk.", ".kwrrrrrrwk.",
  ".kwrrrrrrwk.", ".kwwwrrwwwk.", ".kwwwrrwwwk.", ".kkkkkkkkkk."
};
const char* CAIXA[16] = {
  "............", "............", "............", "............",
  "............", "............", "............", "............",
  "............", "..y.y.y.y...", "..y.y.y.y...", ".kkkkkkkkkk.",
  ".kooooooook.", ".koOOOOOOok.", ".kooooooook.", ".kkkkkkkkkk."
};

// A arma, vista de tras: 16x16 quadradinhos de 4x3 pixels.
const char* ARMA[16] = {
  "......kkkk......",
  "......kmmk......",
  ".....kmmmmk.....",
  ".....kmllmk.....",
  ".....kmllmk.....",
  "....kmmllmmk....",
  "....kmmmmmmk....",
  "...kkmmmmmmkk...",
  "..kpppmmmmpppk..",
  ".kppppmmmmppppk.",
  ".kppppkkkkppppk.",
  "kpppppqqqqpppppk",
  "kppppqqqqqqppppk",
  "kpppppppppppppqk",
  "kqppppppppppppqk",
  "kqqqppppppppqqqk"
};
const int ARMA_X = 128;       // 16 quadradinhos de 4 pixels: de 128 a 191
const int ARMA_Y = 144;       // 16 de 3: de 144 a 191
int16_t armaTopo[RAIOS];      // onde a arma comeca, em cada raio

// ---------------- o jogador ----------------
float px, py;                 // posicao, em quadrados do mapa
float dirX, dirY;             // para onde olha
float planoX, planoY;         // o plano da camera: o campo de visao
int vida = 100;
int municao = 30;

RTC_DATA_ATTR int fase = 1;   // sobrevivem ao sono profundo
RTC_DATA_ATTR int pontos = 0;
int recorde = 0;              // este fica no cartao, em /RECORDE.TXT
bool temCartao = false;
bool faseDoCartao = false;
bool telaIniciada = false;

// ---------------- estado ----------------
enum Estado { TITULO, JOGANDO, PAUSA, MORTO, VITORIA };
Estado estado = TITULO;

float zbuf[RAIOS];            // a distancia da parede em cada raio
int16_t topoAntes[RAIOS];
int16_t baseAntes[RAIOS];
uint16_t corAntes[RAIOS];
bool sujo[RAIOS];             // coluna que precisa ir de novo para a tela

bool aAntes = false, bAntes = false, startAntes = false;
unsigned long quadroAntes = 0;
unsigned long ultimoTiro = 0;
unsigned long clarao = 0;     // ate quando o clarao do tiro aparece
unsigned long ferido = 0;     // ate quando a borda vermelha aparece
bool armaNaTela = false;
int vidaMostrada = -1, municaoMostrada = -1, pontosMostrados = -1, rostoMostrado = -1;

// =====================================================================
//  Cores
// =====================================================================

uint16_t corDaLetra(char c) {
  switch (c) {
    case 'k': return 0x18E3;              // quase preto: o contorno
    case 'm': return 0x6B4D;              // metal
    case 'l': return 0xAD55;              // brilho do metal
    case 'r': return 0xF800;              // o olho do robo, a cruz do kit
    case 'b': return 0x4A9F;              // o corpo do robo
    case 'w': return 0xFFFF;
    case 'y': return 0xFFE0;
    case 'o': return 0x5AC0;              // a caixa de municao
    case 'O': return 0x8C40;
    case 'p': return tft.color565(214, 150, 110);  // a mao
    case 'q': return tft.color565(160, 100, 70);
  }
  return PRETO;
}

void prepararCores() {
  // R, G, B de cada parede: concreto, tijolo, metal, porta, saida.
  const int base[TIPOS][3] = {
    {150, 150, 150}, {176, 72, 48}, {84, 116, 160}, {150, 104, 40}, {40, 180, 70}
  };
  for (int t = 0; t < TIPOS; t++)
    for (int n = 0; n < 8; n++) {
      float luz = 1.0 - n * 0.11;
      tons[t][n] = tft.color565(base[t][0] * luz, base[t][1] * luz, base[t][2] * luz);
    }

  tetoPerto = tft.color565(64, 64, 72);
  tetoLonge = tft.color565(36, 36, 44);
  chaoLonge = tft.color565(48, 40, 32);
  chaoMedio = tft.color565(72, 60, 44);
  chaoPerto = tft.color565(96, 80, 58);
}

// =====================================================================
//  O mapa
// =====================================================================

bool ehParede(char c) {
  return c == '#' || c == 'B' || c == 'M' || c == 'D' || c == 'X';
}

int tipoDaParede(char c) {
  switch (c) {
    case 'B': return 1;
    case 'M': return 2;
    case 'D': return 3;
    case 'X': return 4;
  }
  return 0;
}

// "/E1M1.TXT": a primeira linha e o nome da fase, as 16 seguintes o mapa.
bool lerFaseDoCartao(int numero) {
  if (!temCartao) return false;
  char caminho[16];
  snprintf(caminho, sizeof(caminho), "/E1M%d.TXT", numero);
  File arquivo = SD.open(caminho);
  if (!arquivo) return false;

  String titulo = arquivo.readStringUntil('\n');
  titulo.trim();
  titulo.toCharArray(nomeDaFase, sizeof(nomeDaFase));

  int y = 0;
  while (arquivo.available() && y < TAM) {
    String linha = arquivo.readStringUntil('\n');
    linha.trim();
    if (linha.length() == 0) continue;
    for (int x = 0; x < TAM; x++)
      mapa[y][x] = x < (int)linha.length() ? linha.charAt(x) : '#';
    mapa[y][TAM] = '\0';
    y++;
  }
  arquivo.close();

  for (; y < TAM; y++) {
    for (int x = 0; x < TAM; x++) mapa[y][x] = '#';
    mapa[y][TAM] = '\0';
  }
  return true;
}

void usarFaseInterna() {
  for (int y = 0; y < TAM; y++) {
    for (int x = 0; x < TAM; x++) mapa[y][x] = FASE_INTERNA[y][x];
    mapa[y][TAM] = '\0';
  }
  strcpy(nomeDaFase, "E1M1: HANGAR");
}

// Tira do mapa o jogador, os robos e os itens, e fecha a borda: um mapa
// editado com um buraco na beira faria o raio sair da memoria do mapa.
void prepararFase() {
  nCoisas = 0;
  bool achouJogador = false;

  for (int y = 0; y < TAM; y++)
    for (int x = 0; x < TAM; x++) {
      char c = mapa[y][x];
      bool naBorda = x == 0 || y == 0 || x == TAM - 1 || y == TAM - 1;

      if (c == 'P' && !naBorda) {
        px = x + 0.5;
        py = y + 0.5;
        achouJogador = true;
        mapa[y][x] = '.';
      } else if ((c == 'R' || c == 'V' || c == 'A') && !naBorda && nCoisas < MAX_COISAS) {
        coisas[nCoisas] = {x + 0.5f, y + 0.5f, c, c == 'R' ? 2 : 1, true, 0};
        nCoisas++;
        mapa[y][x] = '.';
      } else if (!ehParede(c) || (naBorda && c == 'D')) {
        mapa[y][x] = naBorda ? '#' : '.';   // porta na beira levaria para fora
      }
    }

  if (!achouJogador) {
    px = 1.5;
    py = 1.5;
  }
  dirX = 1;
  dirY = 0;
  planoX = 0;
  planoY = 0.66;              // 0,66 da um campo de visao de 66 graus
  vida = 100;
  municao = 30;
}

// =====================================================================
//  O desenho 3D
// =====================================================================

// Um pedaco de coluna, cortado ate onde a arma comeca.
void faixa(int x, int y0, int y1, int limite, uint16_t cor) {
  if (y1 > limite) y1 = limite;
  if (y1 > y0) tft.fillRect(x, y0, COL, y1 - y0, cor);
}

void teto(int x, int y0, int y1, int limite) {
  int meio = HORIZONTE / 2;
  if (y0 < meio) faixa(x, y0, y1 < meio ? y1 : meio, limite, tetoPerto);
  if (y1 > meio) faixa(x, y0 > meio ? y0 : meio, y1, limite, tetoLonge);
}

void chao(int x, int y0, int y1, int limite) {
  const int corte1 = HORIZONTE + 24, corte2 = HORIZONTE + 56;
  if (y0 < corte1) faixa(x, y0, y1 < corte1 ? y1 : corte1, limite, chaoLonge);
  if (y1 > corte1 && y0 < corte2)
    faixa(x, y0 > corte1 ? y0 : corte1, y1 < corte2 ? y1 : corte2, limite, chaoMedio);
  if (y1 > corte2) faixa(x, y0 > corte2 ? y0 : corte2, y1, limite, chaoPerto);
}

void desenharVista() {
  for (int r = 0; r < RAIOS; r++) {
    float camera = 2.0 * r / RAIOS - 1;         // -1 na esquerda, +1 na direita
    float raioX = dirX + planoX * camera;
    float raioY = dirY + planoY * camera;

    int mx = (int)px, my = (int)py;
    float passoX = raioX == 0 ? 1e30 : fabsf(1 / raioX);
    float passoY = raioY == 0 ? 1e30 : fabsf(1 / raioY);
    int sx, sy;
    float ladoX, ladoY;
    if (raioX < 0) { sx = -1; ladoX = (px - mx) * passoX; }
    else { sx = 1; ladoX = (mx + 1.0 - px) * passoX; }
    if (raioY < 0) { sy = -1; ladoY = (py - my) * passoY; }
    else { sy = 1; ladoY = (my + 1.0 - py) * passoY; }

    // O DDA: um quadrado por vez, sempre pelo lado mais perto.
    int lado = 0;
    char bateu = '#';
    for (int n = 0; n < 40; n++) {
      if (ladoX < ladoY) { ladoX += passoX; mx += sx; lado = 0; }
      else { ladoY += passoY; my += sy; lado = 1; }
      bateu = mapa[my][mx];
      if (ehParede(bateu)) break;
    }

    float dist = lado == 0 ? ladoX - passoX : ladoY - passoY;
    if (dist < 0.05) dist = 0.05;
    zbuf[r] = dist;

    int altura = (int)(VISTA / dist);
    int topo = HORIZONTE - altura / 2;
    int base = topo + altura;
    if (topo < 0) topo = 0;
    if (base > VISTA) base = VISTA;

    // Onde o raio bateu dentro do quadrado: a beira de cada bloco sai mais
    // escura, e e ela que desenha as juntas da parede.
    float ponto = lado == 0 ? py + dist * raioY : px + dist * raioX;
    ponto -= floorf(ponto);
    int tom = (int)(dist * 1.1) + lado;
    if (ponto < 0.06 || ponto > 0.94) tom += 2;
    if (tom > 7) tom = 7;
    uint16_t cor = tons[tipoDaParede(bateu)][tom];

    if (!sujo[r] && topo == topoAntes[r] && base == baseAntes[r] && cor == corAntes[r]) continue;
    topoAntes[r] = topo;
    baseAntes[r] = base;
    corAntes[r] = cor;
    sujo[r] = false;

    int x = r * COL;
    int limite = armaTopo[r];
    teto(x, 0, topo, limite);
    faixa(x, topo, base, limite, cor);
    chao(x, base, VISTA, limite);
  }
}

// Um desenho de 12x16 em pe no chao, em (x, y) do mapa, com o z-buffer:
// o que esta atras de uma parede nao aparece.
void desenharCoisa(float cx, float cy, const char** arte) {
  float sx = cx - px, sy = cy - py;
  float inverso = 1.0 / (planoX * dirY - dirX * planoY);
  float lateral = inverso * (dirY * sx - dirX * sy);
  float fundo = inverso * (-planoY * sx + planoX * sy);   // a profundidade
  if (fundo < 0.2) return;

  int centro = (int)((LARG / 2) * (1 + lateral / fundo));
  int altura = (int)(VISTA / fundo * 0.9);
  int largura = altura * 12 / 16;
  if (largura < 2) return;    // longe demais: e a largura que divide ali embaixo
  int chaoAqui = HORIZONTE + (int)(VISTA / fundo) / 2;
  int topo = chaoAqui - altura;
  int esquerda = centro - largura / 2;

  for (int x = esquerda < 0 ? 0 : esquerda - esquerda % COL; x < esquerda + largura && x < LARG; x += COL) {
    int r = x / COL;
    if (r < 0 || r >= RAIOS || fundo >= zbuf[r]) continue;
    int coluna = (x - esquerda) * 12 / largura;
    if (coluna < 0 || coluna > 11) continue;
    int limite = armaTopo[r];

    // As linhas da mesma cor vao juntas, num retangulo so.
    int linha = 0;
    while (linha < 16) {
      char c = arte[linha][coluna];
      int fim = linha + 1;
      while (fim < 16 && arte[fim][coluna] == c) fim++;
      if (c != '.') {
        int y0 = topo + linha * altura / 16;
        int y1 = topo + fim * altura / 16;
        if (y0 < 0) y0 = 0;
        faixa(x, y0, y1, limite, corDaLetra(c));
      }
      linha = fim;
    }
    sujo[r] = true;           // no proximo quadro a coluna volta a ser parede
  }
}

void desenharCoisas() {
  // Do mais longe para o mais perto: o de perto pinta por cima.
  int ordem[MAX_COISAS];
  float dist[MAX_COISAS];
  int n = 0;
  for (int i = 0; i < nCoisas; i++) {
    if (!coisas[i].ativa && coisas[i].tipo != 'R') continue;
    float ex = coisas[i].x - px, ey = coisas[i].y - py;
    dist[n] = ex * ex + ey * ey;
    ordem[n] = i;
    n++;
  }
  for (int i = 1; i < n; i++)
    for (int j = i; j > 0 && dist[j] > dist[j - 1]; j--) {
      float d = dist[j]; dist[j] = dist[j - 1]; dist[j - 1] = d;
      int o = ordem[j]; ordem[j] = ordem[j - 1]; ordem[j - 1] = o;
    }

  for (int i = 0; i < n; i++) {
    Coisa &c = coisas[ordem[i]];
    const char** arte = c.tipo == 'V' ? KIT : c.tipo == 'A' ? CAIXA : c.vida > 0 ? ROBO : SUCATA;
    desenharCoisa(c.x, c.y, arte);
  }
}

// =====================================================================
//  A arma e o painel
// =====================================================================

void prepararArma() {
  for (int r = 0; r < RAIOS; r++) {
    armaTopo[r] = VISTA;
    int x = r * COL;
    if (x < ARMA_X || x >= ARMA_X + 64) continue;
    int coluna = (x - ARMA_X) / 4;
    for (int linha = 0; linha < 16; linha++)
      if (ARMA[linha][coluna] != '.') {
        armaTopo[r] = ARMA_Y + linha * 3;
        break;
      }
  }
}

void desenharArma() {
  for (int coluna = 0; coluna < 16; coluna++) {
    int linha = 0;
    while (linha < 16) {
      char c = ARMA[linha][coluna];
      int fim = linha + 1;
      while (fim < 16 && ARMA[fim][coluna] == c) fim++;
      if (c != '.')
        tft.fillRect(ARMA_X + coluna * 4, ARMA_Y + linha * 3, 4, (fim - linha) * 3, corDaLetra(c));
      linha = fim;
    }
  }
  armaNaTela = true;
}

void desenharClarao() {
  tft.fillCircle(160, 134, 9, AMARELO);
  tft.fillCircle(160, 134, 4, BRANCO);
  for (int r = 72; r < 88; r++) sujo[r] = true;
}

void desenharRosto() {
  int humor = vida > 60 ? 0 : vida > 30 ? 1 : vida > 0 ? 2 : 3;
  if (humor == rostoMostrado) return;
  rostoMostrado = humor;

  tft.fillRect(138, 194, 44, 44, PAINEL);
  tft.fillCircle(160, 216, 18, tft.color565(226, 176, 120));
  if (humor == 3) {
    tft.drawLine(151, 208, 157, 214, PRETO); tft.drawLine(157, 208, 151, 214, PRETO);
    tft.drawLine(163, 208, 169, 214, PRETO); tft.drawLine(169, 208, 163, 214, PRETO);
    return;
  }
  tft.fillRect(152, 209, 5, 5, PRETO);
  tft.fillRect(164, 209, 5, 5, PRETO);
  if (humor == 0) {
    tft.drawLine(151, 222, 156, 227, PRETO);
    tft.drawFastHLine(156, 227, 9, PRETO);
    tft.drawLine(164, 227, 169, 222, PRETO);
  } else if (humor == 1) {
    tft.drawFastHLine(153, 225, 15, PRETO);
  } else {
    tft.drawLine(151, 228, 156, 223, PRETO);
    tft.drawFastHLine(156, 223, 9, PRETO);
    tft.drawLine(164, 223, 169, 228, PRETO);
    tft.fillRect(146, 205, 3, 6, VERMELHO);
  }
}

void desenharPainel() {
  tft.fillRect(0, VISTA, LARG, 240 - VISTA, PAINEL);
  tft.drawFastHLine(0, VISTA, LARG, PAINEL_CLARO);
  tft.setTextSize(1);
  tft.setTextColor(LETREIRO, PAINEL);
  tft.setCursor(10, 195);
  tft.print("MUNICAO");
  tft.setCursor(226, 195);
  tft.print("VIDA");
  // O nome cabe em 21 letras, antes do rosto: o resto fica de fora.
  char curto[22];
  strncpy(curto, nomeDaFase, 21);
  curto[21] = '\0';
  tft.setCursor(10, 231);
  tft.print(curto);
  vidaMostrada = municaoMostrada = pontosMostrados = rostoMostrado = -1;
}

void atualizarPainel() {
  tft.setTextSize(3);
  tft.setTextColor(VERMELHO, PAINEL);
  if (municao != municaoMostrada) {
    municaoMostrada = municao;
    tft.setCursor(10, 205);
    tft.printf("%3d", municao);
  }
  if (vida != vidaMostrada) {
    vidaMostrada = vida;
    tft.setCursor(226, 205);
    tft.printf("%3d%%", vida < 0 ? 0 : vida);
  }
  if (pontos != pontosMostrados) {
    pontosMostrados = pontos;
    tft.setTextSize(1);
    tft.setTextColor(AMARELO, PAINEL);
    tft.setCursor(226, 231);
    tft.printf("PTS %05d", pontos);
  }
  desenharRosto();
}

// =====================================================================
//  O jogo
// =====================================================================

bool livre(float x, float y) {
  return !ehParede(mapa[(int)y][(int)x]);
}

// Da para ver o jogador de (x, y)? Anda pela reta ate ele, de pouco em pouco.
bool enxerga(float x, float y) {
  float ex = px - x, ey = py - y;
  float d = sqrtf(ex * ex + ey * ey);
  int passos = (int)(d / 0.2);
  for (int i = 1; i < passos; i++) {
    float t = (float)i / passos;
    if (!livre(x + ex * t, y + ey * t)) return false;
  }
  return true;
}

void somDoTiro() { tone(BUZZER, 160, 70); }

void atirar(unsigned long agora) {
  ultimoTiro = agora;
  if (municao <= 0) {
    tone(BUZZER, 2000, 15);   // o clique da arma vazia
    return;
  }
  municao--;
  clarao = agora + 90;
  somDoTiro();

  // O tiro vai reto pelo meio da tela: acerta o robo mais perto que esteja
  // na frente da parede do meio.
  int alvo = -1;
  float melhor = zbuf[RAIOS / 2];
  float inverso = 1.0 / (planoX * dirY - dirX * planoY);
  for (int i = 0; i < nCoisas; i++) {
    Coisa &c = coisas[i];
    if (c.tipo != 'R' || c.vida <= 0) continue;
    float sx = c.x - px, sy = c.y - py;
    float lateral = inverso * (dirY * sx - dirX * sy);
    float fundo = inverso * (-planoY * sx + planoX * sy);
    if (fundo < 0.2 || fundo >= melhor) continue;
    int centro = (int)((LARG / 2) * (1 + lateral / fundo));
    int meiaLargura = (int)(VISTA / fundo * 0.9) * 12 / 16 / 2;
    if (abs(centro - LARG / 2) > meiaLargura * 2 / 3) continue;
    melhor = fundo;
    alvo = i;
  }
  if (alvo < 0) return;

  Coisa &robo = coisas[alvo];
  robo.vida--;
  if (robo.vida > 0) {
    tone(BUZZER, 90, 120);
  } else {
    tone(BUZZER, 60, 250);
    pontos += 100;
    Serial.println("robo destruido: +100 pontos");
  }
}

void usar() {
  int cx = (int)(px + dirX * 0.9);
  int cy = (int)(py + dirY * 0.9);
  char c = mapa[cy][cx];
  if (c == 'D') {
    mapa[cy][cx] = '.';
    tone(BUZZER, 220, 150);
    Serial.println("porta aberta");
  } else if (c == 'X') {
    concluirFase();
  }
}

void andar(float dx, float dy) {
  // Separado em x e em y: encostado na parede, o jogador escorrega nela.
  float folga = 0.25;
  float nx = px + dx, ny = py + dy;
  if (livre(nx + (dx > 0 ? folga : -folga), py)) px = nx;
  if (livre(px, ny + (dy > 0 ? folga : -folga))) py = ny;
}

void girar(float angulo) {
  float c = cosf(angulo), s = sinf(angulo);
  float d = dirX;
  dirX = dirX * c - dirY * s;
  dirY = d * s + dirY * c;
  float p = planoX;
  planoX = planoX * c - planoY * s;
  planoY = p * s + planoY * c;
}

void atualizarCoisas(unsigned long agora, float dt) {
  for (int i = 0; i < nCoisas; i++) {
    Coisa &c = coisas[i];
    if (!c.ativa) continue;
    float ex = px - c.x, ey = py - c.y;
    float d = sqrtf(ex * ex + ey * ey);

    if (c.tipo == 'V' && d < 0.6 && vida < 100) {
      vida = vida + 25 > 100 ? 100 : vida + 25;
      c.ativa = false;
      pontos += 10;
      tone(BUZZER, 1200, 60);
    } else if (c.tipo == 'A' && d < 0.6 && municao < 99) {
      municao = municao + 12 > 99 ? 99 : municao + 12;
      c.ativa = false;
      pontos += 10;
      tone(BUZZER, 1500, 60);
    } else if (c.tipo == 'R') {
      if (c.vida <= 0) { c.ativa = false; continue; }
      if (d > 9 || !enxerga(c.x, c.y)) continue;

      // Anda na direcao do jogador ate ficar perto.
      if (d > 1.1) {
        float nx = c.x + ex / d * 1.1 * dt, ny = c.y + ey / d * 1.1 * dt;
        if (livre(nx, c.y)) c.x = nx;
        if (livre(c.x, ny)) c.y = ny;
      }

      // E atira de vez em quando: de perto erra menos.
      if (d < 7 && agora - c.ultimoAtaque > 1300) {
        c.ultimoAtaque = agora;
        if (random(100) < (d < 2.5 ? 70 : 35)) {
          vida -= random(5, 13);
          ferido = agora + 150;
          tone(BUZZER, 110, 90);
        }
      }
    }
  }
}

// =====================================================================
//  Fases, telas e a chave
// =====================================================================

void comecarFase() {
  faseDoCartao = lerFaseDoCartao(fase);
  if (!faseDoCartao) {
    if (fase > 1) {            // acabaram as fases do cartao
      mostrarVitoria();
      return;
    }
    usarFaseInterna();
  }
  prepararFase();
  Serial.printf("fase %d: %s (%s)\n", fase, nomeDaFase, faseDoCartao ? "do cartao" : "do codigo");

  tft.fillScreen(PRETO);
  tft.setTextSize(2);
  tft.setTextColor(VERMELHO);
  tft.setCursor(20, 100);
  tft.print(nomeDaFase);
  delay(1200);

  tft.fillScreen(PRETO);
  for (int r = 0; r < RAIOS; r++) sujo[r] = true;
  desenharPainel();
  armaNaTela = false;
  estado = JOGANDO;
  quadroAntes = millis();
}

void concluirFase() {
  pontos += 500 + vida;
  Serial.printf("fase %d concluida: %d pontos\n", fase, pontos);
  tone(BUZZER, 660, 120);
  fase++;
  comecarFase();
}

void mostrarTitulo() {
  estado = TITULO;
  tft.fillScreen(PRETO);
  for (int i = 0; i < 12; i++)
    tft.fillRect(0, 168 + i * 6, LARG, 6, tft.color565(255, i * 18, 0));
  tft.setTextColor(VERMELHO);
  tft.setTextSize(5);
  tft.setCursor(25, 40);
  tft.print("BASE ZERO");
  tft.setTextSize(1);
  tft.setTextColor(LETREIRO);
  tft.setCursor(40, 92);
  tft.print("tiro em primeira pessoa com raycasting");
  tft.setCursor(40, 110);
  tft.printf("recorde: %d", recorde);
  tft.setCursor(40, 122);
  tft.print(temCartao ? "fases: cartao microSD" : "sem cartao: fase do codigo");
  tft.setTextSize(2);
  tft.setTextColor(AMARELO);
  tft.setCursor(88, 140);
  tft.print("APERTE START");
}

void mostrarMorte() {
  estado = MORTO;
  salvarRecorde();
  tft.fillRect(0, 0, LARG, VISTA, 0x7800);
  tft.setTextColor(BRANCO);
  tft.setTextSize(3);
  tft.setCursor(52, 70);
  tft.print("VOCE MORREU");
  tft.setTextSize(1);
  tft.setCursor(88, 110);
  tft.print("START recomeca a fase");
  Serial.println("o jogador morreu");
}

void mostrarVitoria() {
  estado = VITORIA;
  salvarRecorde();
  tft.fillScreen(PRETO);
  tft.setTextColor(AMARELO);
  tft.setTextSize(3);
  tft.setCursor(16, 60);
  tft.print("MISSAO CUMPRIDA");
  tft.setTextSize(2);
  tft.setTextColor(BRANCO);
  tft.setCursor(60, 110);
  tft.printf("pontos: %d", pontos);
  tft.setCursor(60, 136);
  tft.printf("recorde: %d", recorde);
  tft.setTextSize(1);
  tft.setCursor(100, 180);
  tft.print("START joga de novo");
  Serial.printf("vitoria com %d pontos\n", pontos);
}

void lerRecorde() {
  if (!temCartao) return;
  File arquivo = SD.open("/RECORDE.TXT");
  if (!arquivo) return;
  recorde = arquivo.readStringUntil('\n').toInt();
  arquivo.close();
}

void salvarRecorde() {
  if (!temCartao || pontos <= recorde) return;
  recorde = pontos;
  // Na ESP32 o FILE_WRITE comeca o arquivo do zero: o recorde velho sai.
  File arquivo = SD.open("/RECORDE.TXT", FILE_WRITE);
  if (!arquivo) return;
  arquivo.println(recorde);
  arquivo.close();
  Serial.printf("recorde novo: %d\n", recorde);
}

// A chave desligou: apaga tudo e dorme ate ela voltar.
void desligar() {
  Serial.println("chave desligada: sono profundo ate ela voltar");
  if (telaIniciada) {
    salvarRecorde();
    tone(BUZZER, 300, 120);
    // O desligar da TV antiga: a imagem vira uma linha e some.
    tft.fillScreen(PRETO);
    tft.fillRect(0, 118, LARG, 4, BRANCO);
    delay(120);
    tft.fillRect(0, 118, LARG, 4, PRETO);
    tft.fillRect(150, 118, 20, 4, BRANCO);
    delay(120);
    tft.fillScreen(PRETO);
    tft.sendCommand(ILI9341_DISPOFF);
    tft.sendCommand(ILI9341_SLPIN);
  }

  // A luz apagada e segura em LOW durante o sono; a chave acorda a placa
  // quando for ligada (LOW), com o pull-up do RTC segurando o nivel.
  digitalWrite(TFT_LUZ, LOW);
  gpio_hold_en((gpio_num_t)TFT_LUZ);
  gpio_deep_sleep_hold_en();
  rtc_gpio_pullup_en((gpio_num_t)CHAVE);
  rtc_gpio_pulldown_dis((gpio_num_t)CHAVE);
  esp_sleep_enable_ext0_wakeup((gpio_num_t)CHAVE, 0);
  Serial.flush();
  esp_deep_sleep_start();
}

// =====================================================================

void setup() {
  Serial.begin(115200);

  // O pino da luz pode ter ficado segurado pelo sono: ele volta ao sketch.
  gpio_hold_dis((gpio_num_t)TFT_LUZ);
  pinMode(TFT_LUZ, OUTPUT);
  digitalWrite(TFT_LUZ, LOW);
  pinMode(CHAVE, INPUT_PULLUP);
  int botoes[7] = {BT_CIMA, BT_BAIXO, BT_ESQ, BT_DIR, BT_A, BT_B, BT_START};
  for (int i = 0; i < 7; i++) pinMode(botoes[i], INPUT_PULLUP);

  // Ligou pela USB com a chave desligada: volta a dormir sem acender nada.
  if (digitalRead(CHAVE) == HIGH) desligar();

  tft.begin();
  tft.setRotation(1);
  tft.fillScreen(PRETO);
  telaIniciada = true;
  digitalWrite(TFT_LUZ, HIGH);
  prepararCores();
  prepararArma();

  temCartao = SD.begin(SD_CS);
  lerRecorde();
  Serial.printf("BASE ZERO -- cartao: %s, recorde: %d\n", temCartao ? "sim" : "nao", recorde);

  if (esp_sleep_get_wakeup_cause() == ESP_SLEEP_WAKEUP_EXT0) {
    Serial.printf("acordou pela chave: voltando para a fase %d com %d pontos\n", fase, pontos);
    comecarFase();
  } else {
    fase = 1;
    pontos = 0;
    mostrarTitulo();
  }
}

void loop() {
  unsigned long agora = millis();
  if (digitalRead(CHAVE) == HIGH) desligar();

  bool start = digitalRead(BT_START) == LOW;
  bool apertouStart = start && !startAntes;
  startAntes = start;

  if (estado == TITULO || estado == MORTO || estado == VITORIA) {
    if (apertouStart) {
      if (estado == VITORIA) { fase = 1; pontos = 0; }
      comecarFase();
    }
    delay(30);
    return;
  }

  if (estado == PAUSA) {
    if (apertouStart) {
      estado = JOGANDO;
      for (int r = 0; r < RAIOS; r++) sujo[r] = true;
      armaNaTela = false;
      quadroAntes = millis();
    }
    delay(30);
    return;
  }

  if (apertouStart) {
    estado = PAUSA;
    tft.fillRect(100, 80, 120, 32, PRETO);
    tft.drawRect(100, 80, 120, 32, AMARELO);
    tft.setTextSize(2);
    tft.setTextColor(AMARELO);
    tft.setCursor(118, 89);
    tft.print("PAUSADO");
    return;
  }

  float dt = (agora - quadroAntes) / 1000.0;
  quadroAntes = agora;
  if (dt > 0.1) dt = 0.1;

  // ---- os botoes ----
  if (digitalRead(BT_CIMA) == LOW) andar(dirX * 2.6 * dt, dirY * 2.6 * dt);
  if (digitalRead(BT_BAIXO) == LOW) andar(-dirX * 2.0 * dt, -dirY * 2.0 * dt);
  if (digitalRead(BT_ESQ) == LOW) girar(-2.4 * dt);
  if (digitalRead(BT_DIR) == LOW) girar(2.4 * dt);

  bool a = digitalRead(BT_A) == LOW;
  if (a && (!aAntes || agora - ultimoTiro > 350)) atirar(agora);
  aAntes = a;

  bool b = digitalRead(BT_B) == LOW;
  if (b && !bAntes) usar();
  bAntes = b;
  if (estado != JOGANDO) return;        // o B pode ter terminado a fase

  atualizarCoisas(agora, dt);
  if (vida <= 0) {
    vida = 0;
    atualizarPainel();
    mostrarMorte();
    return;
  }

  // ---- o quadro ----
  desenharVista();
  desenharCoisas();
  if (!armaNaTela) desenharArma();
  if (agora < clarao) desenharClarao();
  if (agora < ferido) {
    tft.drawRect(0, 0, LARG, VISTA, VERMELHO);
    tft.drawRect(1, 1, LARG - 2, VISTA - 2, VERMELHO);
    for (int r = 0; r < RAIOS; r++) sujo[r] = true;
  }
  atualizarPainel();

  // No maximo 30 quadros por segundo: o SPI agradece, e o computador fraco
  // do laboratorio tambem.
  unsigned long gasto = millis() - agora;
  if (gasto < 33) delay(33 - gasto);
}
