"""Lista os cabecalhos publicos dos componentes do ESP-IDF que o simulador traz.

Saida: um caminho de #include por linha (o que um programa escreveria:
"driver/gpio.h", "esp_wifi.h"), para o copiar_idf.py.

Uso: python listar_publicos.py <pasta do esp-idf> > lista.txt
"""
import os
import sys

# Os componentes cujo include/ e a API publica que um programa usa. Os que
# sao submodulos do IDF (lwip, mbedtls, esp-mqtt, cJSON, nimble) nao vem no
# pacote do codigo-fonte e o simulador escreve os seus.
COMPONENTES = [
    'driver', 'esp_driver_gpio', 'esp_driver_ledc', 'esp_driver_uart', 'esp_driver_i2c', 'esp_driver_spi',
    'esp_driver_i2s', 'esp_driver_gptimer', 'esp_driver_rmt', 'esp_driver_mcpwm', 'esp_driver_pcnt',
    'esp_driver_dac', 'esp_driver_sdm', 'esp_driver_tsens', 'esp_driver_touch_sens', 'esp_driver_sdmmc',
    'esp_driver_sdspi', 'esp_driver_sdio', 'esp_driver_usb_serial_jtag', 'esp_driver_twai', 'esp_driver_ana_cmpr',
    'esp_driver_cam', 'esp_driver_parlio', 'esp_driver_i3c', 'esp_driver_ppa', 'esp_driver_jpeg', 'esp_driver_isp',
    'esp_adc', 'esp_wifi', 'esp_event', 'esp_netif', 'esp_timer', 'esp_system', 'esp_common', 'esp_hw_support',
    'esp_rom', 'hal', 'soc', 'log', 'heap', 'nvs_flash', 'esp_partition', 'spi_flash', 'app_update', 'bootloader_support',
    'esp_http_server', 'esp_http_client', 'esp_https_ota', 'esp_https_server', 'esp_pm', 'vfs', 'fatfs', 'sdmmc',
    'esp_ringbuf', 'esp_mm', 'esp_psram', 'esp_lcd', 'esp_eth', 'console', 'esp_coex', 'esp_phy', 'efuse',
    'esp_app_format', 'esp_bootloader_format', 'wear_levelling', 'spiffs', 'esp_local_ctrl', 'protocomm',
    'wifi_provisioning', 'esp_hid', 'usb', 'esp_tee', 'esp_security', 'ulp', 'esp_gdbstub', 'esp_vfs_console',
    'pthread', 'cxx', 'newlib', 'esp_netif_stack', 'tcp_transport', 'http_parser', 'esp-tls', 'unity',
]
PULAR_PASTA = ('test', 'test_apps', 'tests', 'host_test', 'linux', 'mock', 'examples', 'private_include', 'port', 'src')


def main():
    idf = sys.argv[1]
    nomes = set()
    for comp in COMPONENTES:
        raiz = os.path.join(idf, 'components', comp)
        if not os.path.isdir(raiz):
            continue
        for dirpath, dirnames, files in os.walk(raiz):
            rel = os.path.relpath(dirpath, raiz).replace(os.sep, '/').split('/')
            if any(p in PULAR_PASTA for p in rel):
                dirnames[:] = []
                continue
            if 'include' not in rel:
                continue
            # O caminho do #include: o que vem depois do ultimo "include".
            base = rel[len(rel) - 1 - rel[::-1].index('include') + 1:]
            # include/<chip>/... (esp_rom, bt): o que vem depois do chip.
            if base and base[0].startswith('esp32'):
                base = base[1:]
            for f in files:
                if f.endswith('.h'):
                    nomes.add('/'.join(base + [f]))
    for n in sorted(nomes):
        print(n)


if __name__ == '__main__':
    main()
