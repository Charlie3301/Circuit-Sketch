/* network_wlan.c -- network.WLAN, com a API do port da ESP32, por cima do WiFi da placa. */
#include "py/mpconfig.h"
#if MICROPY_PY_NETWORK
#include <stdio.h>
#include <string.h>

#include "py/mphal.h"
#include "py/mperrno.h"
#include "py/runtime.h"
#include "shared/netutils/netutils.h"
#include "port/cs_py_ponte.h"

typedef struct _wlan_if_obj_t {
    mp_obj_base_t base;
    int interface;
} wlan_if_obj_t;

const mp_obj_type_t cs_wlan_type;
static const wlan_if_obj_t wlanSta = {{&cs_wlan_type}, 0};
static const wlan_if_obj_t wlanAp = {{&cs_wlan_type}, 1};

/* O nome e a senha do AP: o WiFi da placa so recebe tudo junto, no softAP. */
static char ssidDoAp[33] = "ESP32";
static char senhaDoAp[65] = "";
static int canal = 1;
static uint16_t statusDaConexao = 1000;

static mp_obj_t wlan_make_new(const mp_obj_type_t *type, size_t n_args, size_t n_kw, const mp_obj_t *args)
{
    mp_arg_check_num(n_args, n_kw, 0, 1, false);
    int i = n_args > 0 ? mp_obj_get_int(args[0]) : 0;
    if (i == 0) return MP_OBJ_FROM_PTR(&wlanSta);
    if (i == 1) return MP_OBJ_FROM_PTR(&wlanAp);
    mp_raise_ValueError(MP_ERROR_TEXT("invalid WLAN interface identifier"));
}

static mp_obj_t wlan_active(size_t n_args, const mp_obj_t *args)
{
    wlan_if_obj_t *self = MP_OBJ_TO_PTR(args[0]);
    if (n_args > 1) {
        bool ligar = mp_obj_is_true(args[1]);
        cs_py_wifi_ativar(self->interface, ligar);
        if (ligar && self->interface == 1) cs_py_wifi_criar_ap(ssidDoAp, senhaDoAp);
        return mp_const_none;
    }
    return mp_obj_new_bool(cs_py_wifi_ativo(self->interface));
}
static MP_DEFINE_CONST_FUN_OBJ_VAR_BETWEEN(wlan_active_obj, 1, 2, wlan_active);

static void exigirSta(wlan_if_obj_t *self)
{
    if (self->interface != 0) mp_raise_msg(&mp_type_OSError, MP_ERROR_TEXT("STA required"));
    if (!cs_py_wifi_ativo(0)) mp_raise_msg(&mp_type_OSError, MP_ERROR_TEXT("Wifi Not Started"));
}

static mp_obj_t wlan_connect(size_t n_args, const mp_obj_t *pos_args, mp_map_t *kw_args)
{
    enum { ARG_ssid, ARG_key, ARG_bssid };
    static const mp_arg_t allowed_args[] = {
        { MP_QSTR_, MP_ARG_OBJ, {.u_rom_obj = MP_ROM_NONE} },
        { MP_QSTR_, MP_ARG_OBJ, {.u_rom_obj = MP_ROM_NONE} },
        { MP_QSTR_bssid, MP_ARG_KW_ONLY | MP_ARG_OBJ, {.u_rom_obj = MP_ROM_NONE} },
    };
    wlan_if_obj_t *self = MP_OBJ_TO_PTR(pos_args[0]);
    mp_arg_val_t args[MP_ARRAY_SIZE(allowed_args)];
    mp_arg_parse_all(n_args - 1, pos_args + 1, kw_args, MP_ARRAY_SIZE(allowed_args), allowed_args, args);
    exigirSta(self);
    const char *ssid = args[ARG_ssid].u_obj != mp_const_none ? mp_obj_str_get_str(args[ARG_ssid].u_obj) : "";
    const char *senha = args[ARG_key].u_obj != mp_const_none ? mp_obj_str_get_str(args[ARG_key].u_obj) : "";
    if (strlen(ssid) > 32) mp_raise_ValueError(MP_ERROR_TEXT("ssid too long"));
    if (strlen(senha) > 64) mp_raise_ValueError(MP_ERROR_TEXT("password too long"));
    cs_py_wifi_conectar(ssid, senha);
    statusDaConexao = 1001;
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_KW(wlan_connect_obj, 1, wlan_connect);

static mp_obj_t wlan_disconnect(mp_obj_t self_in)
{
    exigirSta(MP_OBJ_TO_PTR(self_in));
    cs_py_wifi_desconectar();
    statusDaConexao = 1000;
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_1(wlan_disconnect_obj, wlan_disconnect);

static mp_obj_t wlan_status(size_t n_args, const mp_obj_t *args)
{
    wlan_if_obj_t *self = MP_OBJ_TO_PTR(args[0]);
    if (n_args == 1) {
        if (self->interface != 0) return mp_const_none;
        /* Os do wl_status_t: 3 conectado, 1 sem rede, 4 falhou (a senha, aqui). */
        switch (cs_py_wifi_status()) {
        case 3: return MP_OBJ_NEW_SMALL_INT(1010);
        case 1: return MP_OBJ_NEW_SMALL_INT(201);
        case 4: return MP_OBJ_NEW_SMALL_INT(202);
        default: return MP_OBJ_NEW_SMALL_INT(statusDaConexao);
        }
    }
    switch ((uintptr_t)mp_obj_str_get_qstr(args[1])) {
    case MP_QSTR_rssi:
        if (self->interface != 0) mp_raise_ValueError(MP_ERROR_TEXT("STA required"));
        return MP_OBJ_NEW_SMALL_INT(cs_py_wifi_rssi());
    case MP_QSTR_stations:
        return mp_obj_new_list(0, NULL);
    default:
        mp_raise_ValueError(MP_ERROR_TEXT("unknown status param"));
    }
}
static MP_DEFINE_CONST_FUN_OBJ_VAR_BETWEEN(wlan_status_obj, 1, 2, wlan_status);

static mp_obj_t wlan_isconnected(mp_obj_t self_in)
{
    wlan_if_obj_t *self = MP_OBJ_TO_PTR(self_in);
    if (self->interface == 0) return mp_obj_new_bool(cs_py_wifi_conectado());
    return mp_obj_new_bool(cs_py_wifi_clientes_do_ap() > 0);
}
static MP_DEFINE_CONST_FUN_OBJ_1(wlan_isconnected_obj, wlan_isconnected);

static mp_obj_t wlan_scan(mp_obj_t self_in)
{
    exigirSta(MP_OBJ_TO_PTR(self_in));
    mp_obj_t lista = mp_obj_new_list(0, NULL);
    int n = cs_py_wifi_varrer();
    for (int i = 0; i < n; i++) {
        const char *ssid;
        uint8_t bssid[6];
        int ch, rssi, seguranca;
        cs_py_wifi_rede(i, &ssid, bssid, &ch, &rssi, &seguranca);
        mp_obj_t t[6] = {
            mp_obj_new_bytes((const byte *)ssid, strlen(ssid)), mp_obj_new_bytes(bssid, 6), MP_OBJ_NEW_SMALL_INT(ch),
            MP_OBJ_NEW_SMALL_INT(rssi), MP_OBJ_NEW_SMALL_INT(seguranca), mp_const_false,
        };
        mp_obj_list_append(lista, mp_obj_new_tuple(6, t));
    }
    return lista;
}
static MP_DEFINE_CONST_FUN_OBJ_1(wlan_scan_obj, wlan_scan);

static mp_obj_t wlan_ifconfig(size_t n_args, const mp_obj_t *args)
{
    wlan_if_obj_t *self = MP_OBJ_TO_PTR(args[0]);
    if (n_args == 1) {
        char ip[16], mascara[16], porta[16], dns[16];
        cs_py_wifi_ip(self->interface, ip, mascara, porta, dns);
        mp_obj_t t[4] = {mp_obj_new_str(ip, strlen(ip)), mp_obj_new_str(mascara, strlen(mascara)),
                         mp_obj_new_str(porta, strlen(porta)), mp_obj_new_str(dns, strlen(dns))};
        return mp_obj_new_tuple(4, t);
    }
    if (mp_obj_is_str(args[1])) return mp_const_none;     /* 'dhcp': o simulador ja da o IP */
    mp_obj_t *itens;
    mp_obj_get_array_fixed_n(args[1], 4, &itens);
    cs_py_wifi_definir_ip(self->interface, mp_obj_str_get_str(itens[0]), mp_obj_str_get_str(itens[1]),
                          mp_obj_str_get_str(itens[2]), mp_obj_str_get_str(itens[3]));
    return mp_const_none;
}
static MP_DEFINE_CONST_FUN_OBJ_VAR_BETWEEN(wlan_ifconfig_obj, 1, 2, wlan_ifconfig);

static mp_obj_t wlan_config(size_t n_args, const mp_obj_t *args, mp_map_t *kwargs)
{
    wlan_if_obj_t *self = MP_OBJ_TO_PTR(args[0]);
    if (n_args != 1 && kwargs->used != 0) mp_raise_TypeError(MP_ERROR_TEXT("either pos or kw args are allowed"));
    if (kwargs->used != 0) {
        bool mudouAp = false;
        for (size_t i = 0; i < kwargs->alloc; i++) {
            if (!mp_map_slot_is_filled(kwargs, i)) continue;
            mp_obj_t v = kwargs->table[i].value;
            switch (mp_obj_str_get_qstr(kwargs->table[i].key)) {
            case MP_QSTR_ssid: case MP_QSTR_essid:
                snprintf(ssidDoAp, sizeof(ssidDoAp), "%s", mp_obj_str_get_str(v));
                mudouAp = true;
                break;
            case MP_QSTR_key: case MP_QSTR_password:
                snprintf(senhaDoAp, sizeof(senhaDoAp), "%s", mp_obj_str_get_str(v));
                mudouAp = true;
                break;
            case MP_QSTR_channel:
                canal = mp_obj_get_int(v);
                break;
            case MP_QSTR_hostname: case MP_QSTR_dhcp_hostname:
                cs_py_wifi_definir_nome(mp_obj_str_get_str(v));
                break;
            default:
                break;
            }
        }
        if (mudouAp && self->interface == 1 && cs_py_wifi_ativo(1)) cs_py_wifi_criar_ap(ssidDoAp, senhaDoAp);
        return mp_const_none;
    }
    if (n_args != 2) mp_raise_TypeError(MP_ERROR_TEXT("can query only one param"));
    switch (mp_obj_str_get_qstr(args[1])) {
    case MP_QSTR_mac: {
        uint8_t mac[6];
        cs_py_wifi_mac(self->interface, mac);
        return mp_obj_new_bytes(mac, 6);
    }
    case MP_QSTR_ssid: case MP_QSTR_essid:
        return mp_obj_new_str(ssidDoAp, strlen(ssidDoAp));
    case MP_QSTR_channel:
        return MP_OBJ_NEW_SMALL_INT(canal);
    case MP_QSTR_hostname: case MP_QSTR_dhcp_hostname: {
        const char *n = cs_py_wifi_nome();
        return mp_obj_new_str(n, strlen(n));
    }
    case MP_QSTR_txpower:
        return mp_obj_new_float(20.0f);
    default:
        mp_raise_ValueError(MP_ERROR_TEXT("unknown config param"));
    }
}
static MP_DEFINE_CONST_FUN_OBJ_KW(wlan_config_obj, 1, wlan_config);

static const mp_rom_map_elem_t wlan_if_locals_dict_table[] = {
    { MP_ROM_QSTR(MP_QSTR_active), MP_ROM_PTR(&wlan_active_obj) },
    { MP_ROM_QSTR(MP_QSTR_connect), MP_ROM_PTR(&wlan_connect_obj) },
    { MP_ROM_QSTR(MP_QSTR_disconnect), MP_ROM_PTR(&wlan_disconnect_obj) },
    { MP_ROM_QSTR(MP_QSTR_status), MP_ROM_PTR(&wlan_status_obj) },
    { MP_ROM_QSTR(MP_QSTR_scan), MP_ROM_PTR(&wlan_scan_obj) },
    { MP_ROM_QSTR(MP_QSTR_isconnected), MP_ROM_PTR(&wlan_isconnected_obj) },
    { MP_ROM_QSTR(MP_QSTR_config), MP_ROM_PTR(&wlan_config_obj) },
    { MP_ROM_QSTR(MP_QSTR_ifconfig), MP_ROM_PTR(&wlan_ifconfig_obj) },
    { MP_ROM_QSTR(MP_QSTR_IF_STA), MP_ROM_INT(0) },
    { MP_ROM_QSTR(MP_QSTR_IF_AP), MP_ROM_INT(1) },
    { MP_ROM_QSTR(MP_QSTR_SEC_OPEN), MP_ROM_INT(0) },
    { MP_ROM_QSTR(MP_QSTR_SEC_WEP), MP_ROM_INT(1) },
    { MP_ROM_QSTR(MP_QSTR_SEC_WPA), MP_ROM_INT(2) },
    { MP_ROM_QSTR(MP_QSTR_SEC_WPA2), MP_ROM_INT(3) },
    { MP_ROM_QSTR(MP_QSTR_SEC_WPA_WPA2), MP_ROM_INT(4) },
    { MP_ROM_QSTR(MP_QSTR_PM_NONE), MP_ROM_INT(0) },
    { MP_ROM_QSTR(MP_QSTR_PM_PERFORMANCE), MP_ROM_INT(1) },
    { MP_ROM_QSTR(MP_QSTR_PM_POWERSAVE), MP_ROM_INT(2) },
};
static MP_DEFINE_CONST_DICT(wlan_if_locals_dict, wlan_if_locals_dict_table);

MP_DEFINE_CONST_OBJ_TYPE(
    cs_wlan_type,
    MP_QSTR_WLAN,
    MP_TYPE_FLAG_NONE,
    make_new, wlan_make_new,
    locals_dict, &wlan_if_locals_dict
    );

/* ---- network.ipconfig e network.phy_mode ---- */
static mp_obj_t cs_rede_ipconfig(size_t n_args, const mp_obj_t *args, mp_map_t *kwargs)
{
    (void)args;
    (void)kwargs;
    if (n_args == 1) return mp_const_none;
    return mp_const_none;
}
MP_DEFINE_CONST_FUN_OBJ_KW(cs_rede_ipconfig_obj, 0, cs_rede_ipconfig);

static mp_obj_t cs_rede_modo(size_t n_args, const mp_obj_t *args)
{
    (void)args;
    return n_args == 0 ? MP_OBJ_NEW_SMALL_INT(4) : mp_const_none;
}
MP_DEFINE_CONST_FUN_OBJ_VAR_BETWEEN(cs_rede_modo_obj, 0, 1, cs_rede_modo);

#endif /* MICROPY_PY_NETWORK */
