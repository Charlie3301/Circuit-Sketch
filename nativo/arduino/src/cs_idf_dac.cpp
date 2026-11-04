/* cs_idf_dac.cpp -- o DAC antigo do ESP-IDF (driver/dac.h), pelo dacWrite do core. */
#include "Arduino.h"
#if defined(ESP32) && SOC_DAC_SUPPORTED
#include "driver/dac.h"

static int pinoDoDac(dac_channel_t canal)
{
#if CONFIG_IDF_TARGET_ESP32
    return canal == DAC_CHAN_0 ? 25 : 26;
#else
    return canal == DAC_CHAN_0 ? 17 : 18;
#endif
}
extern "C" esp_err_t dac_pad_get_io_num(dac_channel_t canal, gpio_num_t *gpio)
{
    if (!gpio) return ESP_ERR_INVALID_ARG;
    *gpio = (gpio_num_t)pinoDoDac(canal);
    return ESP_OK;
}
extern "C" esp_err_t dac_output_voltage(dac_channel_t canal, uint8_t valor)
{
    dacWrite((uint8_t)pinoDoDac(canal), valor);
    return ESP_OK;
}
extern "C" esp_err_t dac_output_enable(dac_channel_t canal)
{
    (void)canal;
    return ESP_OK;
}
extern "C" esp_err_t dac_output_disable(dac_channel_t canal)
{
    dacDisable((uint8_t)pinoDoDac(canal));
    return ESP_OK;
}
#endif
