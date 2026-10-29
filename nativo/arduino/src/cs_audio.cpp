/* cs_audio.cpp -- a ESP32-audioI2S pelo simulador (ver Audio.h). */
#include "Arduino.h"
#if defined(ESP32)
#include "Audio.h"
#include "cs_interno.h"
#define MINIMP3_IMPLEMENTATION
#define MINIMP3_NO_SIMD
#include "terceiros/minimp3.h"

static void info(const char *t)
{
    if (audio_info) audio_info(t);
}

Audio::Audio(bool, uint8_t, uint8_t) { decodificador = calloc(1, sizeof(mp3dec_t)); }

Audio::~Audio()
{
    acabar();
    free(decodificador);
}

bool Audio::setPinout(uint8_t BCLK, uint8_t LRC, uint8_t DOUT, int8_t, int8_t)
{
    bclk = BCLK;
    lrc = LRC;
    dout = DOUT;
    return true;
}

void Audio::setVolume(uint8_t vol, uint8_t) { volume = vol > passos ? passos : vol; }

/* O arquivo inteiro na memoria (o cartao em blocos): o MP3 de alguns MB cabe. */
bool Audio::connecttoFS(fs::FS &fs, const char *path, int32_t inicio)
{
    acabar();
    File f = fs.open(path, "r");
    if (!f) {
        info((String("file not found: ") + path).c_str());
        return false;
    }
    size_t n = f.size();
    uint8_t *b = (uint8_t *)malloc(n ? n : 1);
    size_t lido = b ? (size_t)f.read(b, n) : 0;
    f.close();
    if (!lido) {
        free(b);
        return false;
    }
    if (!comecar(b, lido, path, false)) return false;
    if (inicio > 0) setFilePos((uint32_t)inicio);
    return true;
}

/* A internet: o arquivo inteiro baixado pelo simulador (o stream sem fim das
 * radios nao cabe nesse jeito). */
static uint8_t *baixar(const String &url, size_t *n)
{
    *n = 0;
    int id = (int)ch_i(nullptr, "__baixar", cs_t(url.c_str()));
    if (id < 0) return nullptr;
    size_t total = (size_t)ch_i(nullptr, "__baixado_tamanho", cs_i(id));
    uint8_t *b = (uint8_t *)malloc(total ? total : 1);
    size_t feito = 0;
    while (b && feito < total) {
        size_t pedaco = total - feito > 32768 ? 32768 : total - feito;
        cs_val a[] = {cs_i(id), cs_i((int64_t)feito), cs_de(cs_vetor_mutavel(b + feito, (int)pedaco))};
        int64_t veio = cs_int(nullptr, "__baixado_ler", 3, a);
        if (veio <= 0) break;
        feito += (size_t)veio;
    }
    ch(nullptr, "__baixado_soltar", cs_i(id));
    *n = feito;
    return b;
}

bool Audio::connecttohost(const char *host, const char *, const char *)
{
    acabar();
    String url = host;
    if (!url.startsWith("http")) url = "http://" + url;
    size_t n;
    uint8_t *b = baixar(url, &n);
    if (!b || !n) {
        free(b);
        info((String("connect to ") + host + " failed").c_str());
        return false;
    }
    if (audio_lasthost) audio_lasthost(host);
    return comecar(b, n, host, false);
}

static String urlDoTexto(const char *t)
{
    String r;
    for (const unsigned char *p = (const unsigned char *)t; *p; p++) {
        if (isalnum(*p) || *p == '-' || *p == '_' || *p == '.') r += (char)*p;
        else if (*p == ' ') r += '+';
        else {
            char h[4];
            snprintf(h, sizeof(h), "%%%02X", *p);
            r += h;
        }
    }
    return r;
}

/* A fala: o MP3 que o Google Tradutor devolve, como a biblioteca faz. */
bool Audio::connecttospeech(const char *texto, const char *lingua)
{
    acabar();
    String url = String("http://translate.google.com/translate_tts?ie=UTF-8&tl=") + lingua + "&client=tw-ob&q=" + urlDoTexto(texto);
    size_t n;
    uint8_t *b = baixar(url, &n);
    if (!b || !n) {
        free(b);
        info("connecttospeech: a fala nao veio (a rede do simulador precisa da internet)");
        return false;
    }
    return comecar(b, n, texto, true);
}

bool Audio::comecar(uint8_t *bytes, size_t n, const String &de, bool fala)
{
    dados = bytes;
    tamanho = n;
    posicao = 0;
    nome = de;
    ehFala = fala;
    quadrosTocados = 0;
    ehWav = n >= 44 && memcmp(bytes, "RIFF", 4) == 0 && memcmp(bytes + 8, "WAVE", 4) == 0;
    if (ehWav) {
        canais = bytes[22];
        taxa = bytes[24] | bytes[25] << 8 | (uint32_t)bytes[26] << 16 | (uint32_t)bytes[27] << 24;
        bitsDoWav = bytes[34];
        size_t p = 12;
        while (p + 8 <= n && memcmp(bytes + p, "data", 4) != 0)
            p += 8 + (bytes[p + 4] | bytes[p + 5] << 8 | (uint32_t)bytes[p + 6] << 16);
        inicioDosDados = posicao = p + 8 <= n ? p + 8 : n;
    } else {
        mp3dec_init((mp3dec_t *)decodificador);
        /* A etiqueta ID3 do comeco: pula. */
        if (n > 10 && memcmp(bytes, "ID3", 3) == 0)
            posicao = 10 + ((bytes[6] & 0x7F) << 21 | (bytes[7] & 0x7F) << 14 | (bytes[8] & 0x7F) << 7 | (bytes[9] & 0x7F));
        inicioDosDados = posicao;
    }
    tocando = true;
    pausado = false;
    info((String(ehWav ? "WAV " : "MP3 ") + de).c_str());
    return true;
}

/* As amostras com o volume (a curva da biblioteca e quadratica) ate o I2S. */
void Audio::tocar(int16_t *pcm, int amostras, int ch, uint32_t hz)
{
    if (hz != taxaAberta || ch != canaisAbertos) {
        cs_i2s_abrir(hz, 16, ch, bclk, lrc, dout);
        taxaAberta = hz;
        canaisAbertos = (uint8_t)ch;
    }
    float g = (float)volume / passos;
    g *= g;
    for (int i = 0; i < amostras * ch; i++) pcm[i] = (int16_t)(pcm[i] * g);
    if (mono && ch == 2)
        for (int i = 0; i < amostras; i++) pcm[2 * i] = pcm[2 * i + 1] = (int16_t)((pcm[2 * i] + pcm[2 * i + 1]) / 2);
    cs_i2s_escrever(pcm, (size_t)amostras * ch * 2);
    quadrosTocados += (uint32_t)amostras;
}

void Audio::loop()
{
    if (!tocando || pausado) return;
    if (ehWav) {
        int porAmostra = bitsDoWav / 8, quadro = porAmostra * canais;
        int quadros = quadro > 0 ? (int)((tamanho - posicao) / (size_t)quadro) : 0;
        if (quadros > 1152) quadros = 1152;
        if (quadros <= 0) {
            acabar();
            return;
        }
        static int16_t pcm[1152 * 2];
        for (int i = 0; i < quadros * canais; i++) {
            const uint8_t *s = dados + posicao + (size_t)i * porAmostra;
            pcm[i] = porAmostra == 1 ? (int16_t)((s[0] - 128) << 8) : (int16_t)(s[porAmostra - 2] | s[porAmostra - 1] << 8);
        }
        posicao += (size_t)quadros * quadro;
        tocar(pcm, quadros, canais, taxa);
        return;
    }
    mp3dec_frame_info_t f = {};
    static int16_t pcm[MINIMP3_MAX_SAMPLES_PER_FRAME];
    int amostras = 0;
    while (posicao < tamanho && amostras == 0) {
        amostras = mp3dec_decode_frame((mp3dec_t *)decodificador, dados + posicao, (int)(tamanho - posicao), pcm, &f);
        if (!f.frame_bytes) {
            posicao = tamanho;
            break;
        }
        posicao += (size_t)f.frame_bytes;
    }
    if (!amostras) {
        acabar();
        return;
    }
    taxa = (uint32_t)f.hz;
    canais = (uint8_t)f.channels;
    taxaDeBits = (uint32_t)f.bitrate_kbps * 1000;
    tocar(pcm, amostras, f.channels, (uint32_t)f.hz);
}

void Audio::acabar()
{
    bool estava = tocando;
    tocando = false;
    free(dados);
    dados = nullptr;
    tamanho = posicao = 0;
    if (!estava) return;
    if (ehFala) {
        if (audio_eof_speech) audio_eof_speech(nome.c_str());
    } else if (nome.startsWith("http")) {
        if (audio_eof_stream) audio_eof_stream(nome.c_str());
    } else if (audio_eof_mp3) {
        audio_eof_mp3(nome.c_str());
    }
}

uint32_t Audio::stopSong()
{
    uint32_t p = (uint32_t)posicao;
    acabar();
    return p;
}

bool Audio::pauseResume()
{
    if (!dados) return false;
    pausado = !pausado;
    return true;
}

uint32_t Audio::getAudioCurrentTime() { return taxa ? quadrosTocados / taxa : 0; }

uint32_t Audio::getAudioFileDuration()
{
    if (ehWav) return taxa && canais ? (uint32_t)((tamanho - inicioDosDados) / ((bitsDoWav / 8) * canais) / taxa) : 0;
    return taxaDeBits ? (uint32_t)((tamanho - inicioDosDados) * 8 / taxaDeBits) : 0;
}

bool Audio::setFilePos(uint32_t pos)
{
    if (!dados || pos >= tamanho) return false;
    posicao = pos < inicioDosDados ? inicioDosDados : pos;
    return true;
}
#endif
