/* Audio.h -- a biblioteca ESP32-audioI2S (schreibfaul1): o MP3 e o WAV do
 * cartao, da internet e a fala (connecttospeech, a voz do Google Tradutor),
 * tocando pelo amplificador I2S do circuito. O MP3 e decodificado pelo
 * minimp3 (dominio publico); o som espera tocar como o DMA da placa.
 *
 * Os avisos da biblioteca chegam pelas funcoes que o sketch define, se
 * definir: audio_info, audio_eof_mp3, audio_eof_speech... */
#ifndef CS_AUDIO_H
#define CS_AUDIO_H

#include "Arduino.h"
#include "SD.h"
#include "cs_i2s.h"

extern void audio_info(const char *info) __attribute__((weak));
extern void audio_id3data(const char *info) __attribute__((weak));
extern void audio_eof_mp3(const char *info) __attribute__((weak));
extern void audio_eof_speech(const char *info) __attribute__((weak));
extern void audio_eof_stream(const char *info) __attribute__((weak));
extern void audio_showstation(const char *info) __attribute__((weak));
extern void audio_showstreamtitle(const char *info) __attribute__((weak));
extern void audio_bitrate(const char *info) __attribute__((weak));
extern void audio_lasthost(const char *info) __attribute__((weak));

class Audio {
public:
    Audio(bool internalDAC = false, uint8_t channelEnabled = 3, uint8_t i2sPort = 0);
    ~Audio();

    bool setPinout(uint8_t BCLK, uint8_t LRC, uint8_t DOUT, int8_t MCLK = -1, int8_t DIN = -1);
    void setVolume(uint8_t vol, uint8_t curve = 0);
    uint8_t getVolume() { return volume; }
    uint8_t maxVolume() { return passos; }
    void setVolumeSteps(uint8_t steps) { passos = steps ? steps : 21; }
    void setTone(int8_t, int8_t, int8_t) {}
    void forceMono(bool m) { mono = m; }
    void setBalance(int8_t) {}

    bool connecttoFS(fs::FS &fs, const char *path, int32_t fileStartPos = -1);
    bool connecttoFS(fs::FS &fs, const String &path) { return connecttoFS(fs, path.c_str()); }
    bool connecttoSD(const char *path) { return connecttoFS(SD, path); }
    bool connecttohost(const char *host, const char *user = "", const char *pwd = "");
    bool connecttospeech(const char *speech, const char *lang);

    void loop();
    bool isRunning() { return tocando; }
    uint32_t stopSong();
    bool pauseResume();
    uint32_t getAudioCurrentTime();
    uint32_t getAudioFileDuration();
    uint32_t getSampleRate() { return taxa; }
    uint8_t getBitsPerSample() { return 16; }
    uint8_t getChannels() { return canais; }
    uint32_t getBitRate() { return taxaDeBits; }
    uint32_t getFileSize() { return (uint32_t)tamanho; }
    uint32_t getFilePos() { return (uint32_t)posicao; }
    bool setFilePos(uint32_t pos);

private:
    int8_t bclk = -1, lrc = -1, dout = -1;
    uint8_t volume = 21, passos = 21;
    bool mono = false, tocando = false, pausado = false, ehFala = false, ehWav = false;
    uint32_t taxa = 0, taxaDeBits = 0, quadrosTocados = 0;
    uint8_t canais = 2;
    uint8_t *dados = nullptr;
    size_t tamanho = 0, posicao = 0, inicioDosDados = 0;
    int bitsDoWav = 16;
    uint32_t taxaAberta = 0;
    uint8_t canaisAbertos = 0;
    void *decodificador = nullptr;
    String nome;

    bool comecar(uint8_t *bytes, size_t n, const String &de, bool fala);
    void tocar(int16_t *pcm, int amostras, int ch, uint32_t hz);
    void acabar();
};

#endif
