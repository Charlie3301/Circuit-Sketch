/* ESP_I2S.h -- o I2S do core 3.x da ESP32 (I2SClass): o som vai para o
 * amplificador I2S do circuito (o MAX98357A e o alto-falante), amostra por
 * amostra, no ritmo do DMA da placa (quem escreve espera o som tocar). */
#ifndef CS_ESP_I2S_H
#define CS_ESP_I2S_H

#include "cs_objeto.h"
#include "cs_i2s.h"
#include "driver/i2s_types.h"

typedef enum { I2S_MODE_STD, I2S_MODE_TDM, I2S_MODE_PDM_TX, I2S_MODE_PDM_RX } i2s_mode_t;
typedef enum { I2S_DATA_BIT_WIDTH_8BIT = 8, I2S_DATA_BIT_WIDTH_16BIT = 16, I2S_DATA_BIT_WIDTH_24BIT = 24, I2S_DATA_BIT_WIDTH_32BIT = 32 } i2s_data_bit_width_t;
typedef enum { I2S_SLOT_MODE_MONO = 1, I2S_SLOT_MODE_STEREO = 2 } i2s_slot_mode_t;
typedef enum { I2S_STD_SLOT_LEFT = 1, I2S_STD_SLOT_RIGHT = 2, I2S_STD_SLOT_BOTH = 3 } i2s_std_slot_mask_t;
typedef enum { I2S_RX_TRANSFORM_NONE, I2S_RX_TRANSFORM_32_TO_16, I2S_RX_TRANSFORM_16_STEREO_TO_MONO } i2s_rx_transform_t;

class I2SClass : public Stream {
    int8_t bclk = -1, ws = -1, dout = -1, din = -1;
    bool aberto = false;
    i2s_port_t porta = I2S_NUM_0;
    uint32_t taxa = 0;
    int bits = 16, canais = 2;
    int erro = 0;

public:
    I2SClass() {}
    ~I2SClass() { end(); }

    void setPins(int8_t bclkPin, int8_t wsPin, int8_t doutPin, int8_t dinPin = -1, int8_t mclkPin = -1)
    {
        (void)mclkPin;
        bclk = bclkPin;
        ws = wsPin;
        dout = doutPin;
        din = dinPin;
    }
    void setPinsPdmTx(int8_t clk, int8_t dout0, int8_t dout1 = -1)
    {
        (void)dout1;
        bclk = clk;
        ws = clk;
        dout = dout0;
    }
    void setPinsPdmRx(int8_t clk, int8_t din0, int8_t, int8_t, int8_t) { bclk = clk; din = din0; }
    void setInverted(bool, bool, bool = false) {}
    void setInvertedPdm(bool) {}
    bool setPort(i2s_port_t p)
    {
        if (aberto) return false;
        porta = p;
        return true;
    }
    i2s_port_t getPort() { return porta; }
    /* O handle do driver: aqui nao ha canal do IDF por tras (o som vai direto ao amplificador). */
    i2s_chan_handle_t txChan() { return nullptr; }
    i2s_chan_handle_t rxChan() { return nullptr; }
    uint32_t txSampleRate() { return taxa; }
    i2s_data_bit_width_t txDataWidth() { return (i2s_data_bit_width_t)bits; }
    i2s_slot_mode_t txSlotMode() { return (i2s_slot_mode_t)canais; }
    uint32_t rxSampleRate() { return taxa; }
    i2s_data_bit_width_t rxDataWidth() { return (i2s_data_bit_width_t)bits; }
    i2s_slot_mode_t rxSlotMode() { return (i2s_slot_mode_t)canais; }
    void setTimeout(uint32_t) {}

    bool begin(i2s_mode_t mode, uint32_t rate, i2s_data_bit_width_t bits_cfg, i2s_slot_mode_t ch, int8_t slot_mask = -1)
    {
        (void)mode;
        (void)slot_mask;
        end();
        taxa = rate;
        bits = (int)bits_cfg;
        canais = (int)ch;
        aberto = cs_i2s_abrir(rate, bits, canais, bclk, ws, dout);
        erro = aberto ? 0 : -1;
        return aberto;
    }
    bool configureTX(uint32_t rate, i2s_data_bit_width_t bits_cfg, i2s_slot_mode_t ch, int8_t slot_mask = -1)
    {
        return begin(I2S_MODE_STD, rate, bits_cfg, ch, slot_mask);
    }
    bool configureRX(uint32_t, i2s_data_bit_width_t, i2s_slot_mode_t, i2s_rx_transform_t = I2S_RX_TRANSFORM_NONE) { return true; }
    bool end()
    {
        if (aberto) cs_i2s_fechar();
        aberto = false;
        return true;
    }

    size_t write(uint8_t d) override { return write(&d, 1); }
    size_t write(const uint8_t *buffer, size_t size) override { return aberto ? cs_i2s_escrever(buffer, size) : 0; }
    using Print::write;
    int available() override { return 0; }
    int read() override { return -1; }
    int peek() override { return -1; }
    void flush() override {}
    size_t readBytes(char *, size_t) { return 0; }

    int lastError() { return erro; }

    /* O WAV inteiro na memoria: o cabecalho diz a taxa, os bits e os canais. */
    void playWAV(uint8_t *data, size_t len)
    {
        if (len < 44 || memcmp(data, "RIFF", 4) != 0) return;
        uint16_t ch = data[22] | data[23] << 8;
        uint32_t rate = data[24] | data[25] << 8 | (uint32_t)data[26] << 16 | (uint32_t)data[27] << 24;
        uint16_t b = data[34] | data[35] << 8;
        size_t p = 12;
        while (p + 8 <= len && memcmp(data + p, "data", 4) != 0) p += 8 + (data[p + 4] | data[p + 5] << 8 | (uint32_t)data[p + 6] << 16);
        if (p + 8 > len) return;
        size_t n = data[p + 4] | data[p + 5] << 8 | (uint32_t)data[p + 6] << 16 | (uint32_t)data[p + 7] << 24;
        if (p + 8 + n > len) n = len - p - 8;
        uint32_t antesTaxa = taxa;
        int antesBits = bits, antesCanais = canais;
        begin(I2S_MODE_STD, rate, (i2s_data_bit_width_t)b, (i2s_slot_mode_t)ch);
        write(data + p + 8, n);
        if (antesTaxa) begin(I2S_MODE_STD, antesTaxa, (i2s_data_bit_width_t)antesBits, (i2s_slot_mode_t)antesCanais);
    }

    /* O MP3 inteiro na memoria, quadro a quadro; toca na configuracao que o arquivo diz. */
    bool playMP3(const uint8_t *src, size_t src_len)
    {
        uint32_t antesTaxa = taxa;
        int antesBits = bits, antesCanais = canais;
        bool tocou = cs_i2s_tocar_mp3(src, src_len, bclk, ws, dout);
        if (tocou && antesTaxa) begin(I2S_MODE_STD, antesTaxa, (i2s_data_bit_width_t)antesBits, (i2s_slot_mode_t)antesCanais);
        return tocou;
    }

    uint8_t *recordWAV(size_t, size_t *out_size)
    {
        if (out_size) *out_size = 0;
        return nullptr;
    }
};

#endif
