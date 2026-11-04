/* cs_eth.cpp -- o Ethernet da ESP32 no simulador: o ETH.h de verdade (o
 * RMII da ESP32 e os modulos SPI, W5500 e companhia), com o cabo ligado na
 * rede da bancada (Rede.LigarCabo). A placa fica na rede pelo cabo: o IP, o
 * TCP, o UDP, o HTTP e o DNS funcionam com o WiFi desligado, e os eventos
 * chegam na ordem da placa -- ETH_START, ETH_CONNECTED e ETH_GOT_IP. */
#include "Arduino.h"
#if defined(ESP32)
#include "sdkconfig.h"
#if CONFIG_ETH_ENABLED
#include "ETH.h"
#include "cs_objeto.h"

ETHClass ETH;

/* O que a interface do cabo responde (NetworkInterface, em cs_wifi.cpp). */
static bool cs_eth_comecou = false, cs_eth_ligado = false;
bool cs_eth_iniciada() { return cs_eth_comecou; }
bool cs_eth_conectada() { return cs_eth_ligado; }
IPAddress cs_eth_ip()
{
    IPAddress ip;
    if (cs_eth_ligado) ip.fromString(cs_obj_t(nullptr, "__eth_ip"));
    return ip;
}
IPAddress cs_eth_roteador()
{
    IPAddress ip;
    if (cs_eth_ligado) ip.fromString(cs_obj_t(nullptr, "__eth_roteador"));
    return ip;
}
String cs_eth_mac() { return cs_obj_t(nullptr, "__eth_mac"); }
bool cs_eth_config(IPAddress ip)
{
    cs_obj_n(nullptr, "__eth_config", ip.toString());
    return true;
}

static void cs_eth_postar(arduino_event_id_t id, const arduino_event_info_t *info = nullptr)
{
    arduino_event_t e = {};
    e.event_id = id;
    if (info) e.event_info = *info;
    Network.postEvent(&e);
}

/* O PHY negocia o enlace um instante depois do begin(), e o DHCP responde. */
static void cs_eth_tarefa_do_enlace(void *)
{
    vTaskDelay(pdMS_TO_TICKS(50));
    if (cs_eth_comecou) {
        cs_obj_n(nullptr, "__eth", 1);
        cs_eth_ligado = true;
        cs_eth_postar(ARDUINO_EVENT_ETH_CONNECTED);
        arduino_event_info_t g = {};
        g.got_ip.ip_info.ip.addr = (uint32_t)cs_eth_ip();
        g.got_ip.ip_info.netmask.addr = (uint32_t)IPAddress(255, 255, 255, 0);
        g.got_ip.ip_info.gw.addr = (uint32_t)cs_eth_roteador();
        g.got_ip.ip_changed = true;
        cs_eth_postar(ARDUINO_EVENT_ETH_GOT_IP, &g);
    }
    vTaskDelete(NULL);
}

ETHClass::ETHClass(uint8_t eth_index)
    : _eth_handle(NULL), _eth_index(eth_index), _phy_type(ETH_PHY_MAX), _glue_handle(NULL), _mac(NULL), _phy(NULL), _eth_started(false),
      _link_speed(100), _full_duplex(true), _auto_negotiation(true),
#if ETH_SPI_SUPPORTS_CUSTOM
      _spi(NULL),
#endif
      _spi_freq_mhz(ETH_PHY_SPI_FREQ_MHZ), _pin_cs(-1), _pin_irq(-1), _pin_rst(-1), _pin_sck(-1), _pin_miso(-1), _pin_mosi(-1),
#if CONFIG_ETH_USE_ESP32_EMAC
      _pin_mdc(-1), _pin_mdio(-1), _pin_power(-1), _pin_rmii_clock(-1),
#endif
      _task_stack_size(4096), _poll_period_ms(0), _eth_connected_event_handle(0)
{
    _interface_id = (Network_Interface_ID)(ESP_NETIF_ID_ETH0 + eth_index);
#if ETH_SPI_SUPPORTS_CUSTOM
    _cs_str[0] = 0;
#endif
}

ETHClass::~ETHClass() {}

/* Todas as formas do begin() chegam aqui: o que muda na placa e o barramento ate o PHY. */
static bool cs_eth_comecar(ETHClass *eth, esp_eth_handle_t *handle)
{
    if (cs_eth_comecou) return true;
    Network.begin();
    cs_eth_comecou = true;
    *handle = (esp_eth_handle_t)eth;
    cs_eth_postar(ARDUINO_EVENT_ETH_START);
    return xTaskCreate(cs_eth_tarefa_do_enlace, "eth_enlace", 4096, NULL, 2, NULL) == pdPASS;
}

#if CONFIG_ETH_USE_ESP32_EMAC
bool ETHClass::begin(eth_phy_type_t type, int32_t phy_addr, int mdc, int mdio, int power, eth_clock_mode_t clk_mode)
{
    (void)phy_addr;
    (void)clk_mode;
    _phy_type = type;
    _pin_mdc = mdc;
    _pin_mdio = mdio;
    _pin_power = power;
    _eth_started = cs_eth_comecar(this, &_eth_handle);
    return _eth_started;
}
#endif

#if ETH_SPI_SUPPORTS_CUSTOM
bool ETHClass::begin(eth_phy_type_t type, int32_t phy_addr, int cs, int irq, int rst, SPIClass &spi, uint8_t spi_freq_mhz)
{
    (void)phy_addr;
    _phy_type = type;
    _pin_cs = cs;
    _pin_irq = irq;
    _pin_rst = rst;
    _spi = &spi;
    _spi_freq_mhz = spi_freq_mhz;
    _eth_started = cs_eth_comecar(this, &_eth_handle);
    return _eth_started;
}
#endif

bool ETHClass::begin(eth_phy_type_t type, int32_t phy_addr, int cs, int irq, int rst, spi_host_device_t spi_host, int sck, int miso, int mosi,
                     uint8_t spi_freq_mhz)
{
    (void)phy_addr;
    (void)spi_host;
    _phy_type = type;
    _pin_cs = cs;
    _pin_irq = irq;
    _pin_rst = rst;
    _pin_sck = sck;
    _pin_miso = miso;
    _pin_mosi = mosi;
    _spi_freq_mhz = spi_freq_mhz;
    _eth_started = cs_eth_comecar(this, &_eth_handle);
    return _eth_started;
}

void ETHClass::end()
{
    if (!_eth_started) return;
    if (cs_eth_ligado) {
        cs_eth_ligado = false;
        cs_obj_n(nullptr, "__eth", 0);
        cs_eth_postar(ARDUINO_EVENT_ETH_DISCONNECTED);
    }
    cs_eth_comecou = false;
    _eth_started = false;
    _eth_handle = NULL;
    cs_eth_postar(ARDUINO_EVENT_ETH_STOP);
}

void ETHClass::setTaskStackSize(size_t size) { _task_stack_size = size; }
void ETHClass::setPollPeriod(uint32_t poll_period_ms) { _poll_period_ms = poll_period_ms; }
bool ETHClass::fullDuplex() const { return _full_duplex; }
bool ETHClass::setFullDuplex(bool on)
{
    _full_duplex = on;
    return true;
}
uint16_t ETHClass::linkSpeed() const { return _link_speed; }
bool ETHClass::setLinkSpeed(uint16_t speed)
{
    if (speed != 10 && speed != 100) return false;
    _link_speed = speed;
    return true;
}
bool ETHClass::autoNegotiation() const { return _auto_negotiation; }
bool ETHClass::setAutoNegotiation(bool on)
{
    _auto_negotiation = on;
    return true;
}
uint32_t ETHClass::phyAddr() const { return 0; }
esp_eth_handle_t ETHClass::handle() const { return _eth_handle; }

#if ESP_IDF_VERSION >= ESP_IDF_VERSION_VAL(5, 5, 0)
/* Os filtros do MAC: o switch da bancada ja entrega so o que e da placa. */
bool ETHClass::addMacFilter(uint8_t *mac_addr)
{
    (void)mac_addr;
    return _eth_started;
}
bool ETHClass::removeMacFilter(uint8_t *mac_addr)
{
    (void)mac_addr;
    return _eth_started;
}
bool ETHClass::addMulticastFilter(IPAddress address)
{
    (void)address;
    return _eth_started;
}
bool ETHClass::removeMulticastFilter(IPAddress address)
{
    (void)address;
    return _eth_started;
}
bool ETHClass::receiveAllMulticast(bool on)
{
    (void)on;
    return _eth_started;
}
#endif

size_t ETHClass::printDriverInfo(Print &out) const
{
    size_t n = 0;
    if (linkUp()) {
        n += out.printf(",%s,%uM", fullDuplex() ? "FULL_DUPLEX" : "HALF_DUPLEX", linkSpeed());
        if (autoNegotiation()) n += out.print(",AUTO");
    }
    return n;
}

void ETHClass::_onEthEvent(int32_t event_id, void *event_data)
{
    (void)event_id;
    (void)event_data;
}

#endif
#endif
