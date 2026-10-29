/* soc/soc_caps.h -- o que o chip tem (o soc_caps.h de verdade do ESP-IDF, em
 * chip/<chip>/soc/), escolhido pelo chip da placa simulada. */
#ifndef CS_SOC_CAPS_H
#define CS_SOC_CAPS_H

#include "sdkconfig.h"

#if defined(CONFIG_IDF_TARGET_ESP32S3)
#include "../chip/esp32s3/soc/soc_caps.h"
#elif defined(CONFIG_IDF_TARGET_ESP32S2)
#include "../chip/esp32s2/soc/soc_caps.h"
#elif defined(CONFIG_IDF_TARGET_ESP32C3)
#include "../chip/esp32c3/soc/soc_caps.h"
#elif defined(CONFIG_IDF_TARGET_ESP32C6)
#include "../chip/esp32c6/soc/soc_caps.h"
#elif defined(CONFIG_IDF_TARGET_ESP32H2)
#include "../chip/esp32h2/soc/soc_caps.h"
#else
#include "../chip/esp32/soc/soc_caps.h"
#endif

#endif
