#ifndef CS_SDMMC_CMD_H
#define CS_SDMMC_CMD_H

#include <stdio.h>
#include "driver/sdmmc_types.h"

esp_err_t sdmmc_card_init(const sdmmc_host_t *host, sdmmc_card_t *card);
void sdmmc_card_print_info(FILE *saida, const sdmmc_card_t *card);
esp_err_t sdmmc_get_status(sdmmc_card_t *card);
esp_err_t sdmmc_read_sectors(sdmmc_card_t *card, void *dst, size_t inicio, size_t quantos);
esp_err_t sdmmc_write_sectors(sdmmc_card_t *card, const void *src, size_t inicio, size_t quantos);

#endif
