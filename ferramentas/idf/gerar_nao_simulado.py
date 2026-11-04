"""Gera nativo/arduino/src/arduino-esp32/idf/cs_idf_nao_simulado.c: o que o ESP-IDF
declara e o simulador nao implementa.

O programa de verdade liga do mesmo jeito (em vez de parar em "funcao declarada e
nunca definida"). Cada funcao e fraca, entao a de verdade, se existir em qualquer
parte do core, fica no lugar dela. Sao dois grupos:

- AJUSTES: so regulam o hardware (retencao no sono, pino para o ULP, antena, canal
  de um filtro...). No simulador nao mudam nada: respondem ESP_OK, em silencio.
- o resto: o que a simulacao nao tem (ULP, ISP, camera, JPEG, NimBLE, malha WiFi,
  eFuse...). Respondem ESP_ERR_NOT_SUPPORTED e avisam uma vez na serial.

Uso: python gerar_nao_simulado.py <pasta com os .o do core de uma placa>
"""
import glob
import os
import re
import struct
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(AQUI, '..', '..'))
NATIVO = os.path.join(REPO, 'nativo')
SAIDA = os.path.join(NATIVO, 'arduino', 'src', 'arduino-esp32', 'idf', 'cs_idf_nao_simulado.c')

PREFIXOS = ('esp_', 'gpio_', 'uart_', 'i2c_', 'spi_', 'i2s_', 'ledc_', 'adc', 'dac_', 'touch_', 'rmt_', 'mcpwm_', 'pcnt_', 'twai_', 'timer_', 'gptimer_', 'sdm_', 'sigmadelta_',
            'temperature_', 'temp_sensor_', 'rtc_', 'nvs_', 'sdmmc_', 'sdspi_', 'sdio_', 'ulp_', 'lp_', 'wifi_', 'protocomm_', 'heap_', 'multi_heap_', 'himem', 'ets_', 'dedic_',
            'parlio_', 'ppa_', 'isp_', 'jpeg_', 'ana_cmpr_', 'usb_', 'wl_', 'sntp_', 'bootloader_', 'memspi_', 'spi_flash_', 'uhci_', 'smartconfig_', 'sd_pwr_')
# Quem tem codigo em outro lugar (as pilhas sob demanda): uma funcao fraca aqui nao
# deixaria o ligador buscar a de verdade na biblioteca.
DE_FORA = ('esp_http_client', 'esp_http_server', 'httpd_', 'esp_tls', 'esp_transport', 'esp_mqtt', 'esp_https', 'esp_crt_bundle', 'esp_openthread', 'esp_zb_', 'esp_rmaker', 'esp_netif_new',
           'esp_netif_attach', 'esp_vfs_eventfd', 'mqtt', 'esp_console_new_repl')
PASTAS_FORA = ('soc/', 'hal/', 'private', 'esp_private', 'priv', 'lwip/', 'esp_rmaker', 'network_provisioning', 'esp_tee', 'riscv/', 'xtensa/', 'freertos/', 'unity')
ARQUIVOS_FORA = ('arch/', 'net/', 'netdb', 'libunwind', 'sockets_ext', 'miniz', 'unity_', 'zb')

AJUSTES = re.compile(r'^(gpio_(sleep_|deep_sleep_|hold_|force_|wakeup_|iomux_|isr_|set_drive_capability|set_intr|intr_|install_isr|uninstall_isr)|esp_sleep_(pd_|enable_|disable_|config_|cpu_)|esp_deep_sleep_(disable_|register_|deregister_)'
                     r'|esp_register_mac|esp_unregister_mac|esp_wifi_(set_|config_|connectionless|disable_pmf|force_|80211_tx|register_)|esp_coex|esp_phy_|ledc_(timer_|bind_|set_pin|isr_|cb_|fill_|set_multi)'
                     r'|adc_(set_|vref_|digi_controller)|adc1_ulp|rtc_gpio_|touch_pad_(set_|clear|filter|intr|isr|sw_|fsm_)|esp_intr_|esp_pm_light|esp_bt_sleep|esp_bt_set_lpclk|esp_ble_tx_power_set|esp_bredr_tx_power_set'
                     r'|esp_ble_gap_(set_|config_local|clean_|clear_|add_|remove_|security|prefer_|update_whitelist)|esp_bt_gap_(set_|config_|ssp_|pin_)|esp_ble_(confirm_|passkey_|set_|sc_|remove_)|esp_cpu_|esp_rom_(gpio_|route_|install_|set_cpu|software_reset_cpu|output_)'
                     r'|esp_netif_(tx_rx|set_route|bridge_)|esp_task_wdt_isr|esp_timer_(early|deinit)|esp_system_(abort|console)|esp_ipc_isr|ets_(isr_|intr_|set_|update_|install_)|esp_flash_(set_|chip_)|heap_caps_(monitor|malloc_extmem|register_failed))')

DECLARACAO = re.compile(r'^[ \t]*(?:extern\s+)?((?:const\s+)?(?:struct\s+|enum\s+)?[A-Za-z_]\w*(?:\s+const)?[\s\*]+)([a-z_][a-z0-9_]*)\s*\(([^;{}()]*(?:\([^()]*\)[^;{}()]*)*)\)\s*(?:__attribute__\(\(.*?\)\))?\s*;', re.M)
INLINE = re.compile(r'\b(?:static\s+)?inline\b[^;{}]*?\b([a-z_][a-z0-9_]*)\s*\(', re.M)


def simbolos(caminho):
    b = open(caminho, 'rb').read()
    _, _, _, ptr, n = struct.unpack_from('<HHIII', b, 0)
    tab = ptr + n * 18
    i = 0
    while i < n:
        off = ptr + i * 18
        nome = b[off:off + 8]
        _, sec, _, classe, aux = struct.unpack_from('<IhHBB', b, off + 8)
        if classe in (2, 105) and sec > 0:
            if nome[:4] == b'\0\0\0\0':
                o = struct.unpack_from('<I', nome, 4)[0]
                yield b[tab + o:b.index(b'\0', tab + o)].decode('latin1')
            else:
                yield nome.rstrip(b'\0').decode('latin1')
        i += 1 + aux


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    definidas = set()
    for o in glob.glob(os.path.join(sys.argv[1], '*.o')):
        if os.path.basename(o).startswith('arduino-esp32_idf_cs_idf_nao_simulado'):
            continue
        definidas.update(simbolos(o))
    achadas = {}
    embutidas = set()
    pastas = [os.path.join(NATIVO, 'idf', d) for d in os.listdir(os.path.join(NATIVO, 'idf')) if d != 'proximos']
    for base in pastas:
        for d, _, fs in os.walk(base):
            for f in fs:
                if not f.endswith('.h'):
                    continue
                rel = os.path.relpath(os.path.join(d, f), base).replace(os.sep, '/')
                if rel.startswith(PASTAS_FORA) or rel.startswith(ARQUIVOS_FORA) or '/private' in rel or 'esp_private' in rel:
                    continue
                t = re.sub(r'/\*.*?\*/', '', open(os.path.join(d, f), encoding='utf-8', errors='ignore').read(), flags=re.S)
                t = re.sub(r'//[^\n]*', '', t)
                embutidas.update(INLINE.findall(t))
                for m in DECLARACAO.finditer(t):
                    tipo, nome = ' '.join(m.group(1).split()), m.group(2)
                    if nome.startswith(PREFIXOS) and not nome.startswith(DE_FORA):
                        achadas.setdefault(nome, tipo)
    faltam = sorted(n for n in achadas if n not in definidas and n not in embutidas)
    linhas = ['/* Gerado por ferramentas/idf/gerar_nao_simulado.py: nao edite a mao.',
              ' * O que o ESP-IDF declara e o simulador nao implementa (ver la). */',
              '#include <stdio.h>', '#include <stdint.h>', '',
              'unsigned long cs_millis_para_log(void) __attribute__((weak));',
              'static void cs_nao_simulado(const char *nome, char *avisou)', '{', '    if (*avisou) return;', '    *avisou = 1;',
              '    printf("E (%lu) simulador: %s() nao existe no simulador\\n", cs_millis_para_log ? cs_millis_para_log() : 0ul, nome);', '}', '',
              '#define CS_AJUSTE(tipo, nome) __attribute__((weak)) tipo nome() { return 0; }',
              '#define CS_AJUSTE_V(nome) __attribute__((weak)) void nome() {}',
              '#define CS_ERRO(nome) __attribute__((weak)) int nome() { static char a; cs_nao_simulado(#nome, &a); return 0x106; }',
              '#define CS_ZERO(tipo, nome) __attribute__((weak)) tipo nome() { static char a; cs_nao_simulado(#nome, &a); return 0; }',
              '#define CS_NADA(nome) __attribute__((weak)) void nome() { static char a; cs_nao_simulado(#nome, &a); }', '']
    ajustes = sem = 0
    for n in faltam:
        tipo = achadas[n]
        ponteiro = '*' in tipo
        ajuste = AJUSTES.match(n) is not None
        if tipo == 'void':
            linhas.append(f'CS_AJUSTE_V({n})' if ajuste else f'CS_NADA({n})')
        elif ponteiro:
            linhas.append(f'CS_ZERO(void *, {n})')
        elif tipo == 'esp_err_t':
            linhas.append(f'CS_AJUSTE(int, {n})' if ajuste else f'CS_ERRO({n})')
        else:
            linhas.append(f'CS_AJUSTE(long long, {n})' if ajuste else f'CS_ZERO(long long, {n})')
        if ajuste:
            ajustes += 1
        else:
            sem += 1
    with open(SAIDA, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(linhas) + '\n')
    total = len(achadas)
    print(f'{total} declaradas; {total - len(faltam)} implementadas; {ajustes} ajustes sem efeito; {sem} nao simuladas')


if __name__ == '__main__':
    main()
