/* HX711.h -- a celula de carga com o HX711 (a biblioteca do bogde). */
#ifndef HX711_h
#define HX711_h

#include "cs_objeto.h"

class HX711 : public CsObjeto {
public:
    HX711() { cs_criar("HX711"); }
    void begin(byte dout, byte pd_sck, byte gain = 128) { cs_obj_n(cs_nome, "begin", dout, pd_sck, gain); }
    bool is_ready() { return cs_obj_i(cs_nome, "is_ready") != 0; }
    void wait_ready(unsigned long delay_ms = 0) { (void)delay_ms; cs_obj_n(cs_nome, "wait_ready"); }
    bool wait_ready_retry(int retries = 3, unsigned long delay_ms = 0) { (void)retries; (void)delay_ms; return is_ready(); }
    bool wait_ready_timeout(unsigned long timeout = 1000, unsigned long delay_ms = 0) { (void)timeout; (void)delay_ms; return is_ready(); }
    void set_gain(byte gain = 128) { cs_obj_n(cs_nome, "set_gain", gain); }
    long read() { return (long)cs_obj_i(cs_nome, "read"); }
    long read_average(byte times = 10) { return (long)cs_obj_i(cs_nome, "read_average", times); }
    double get_value(byte times = 1) { return cs_obj_r(cs_nome, "get_value", times); }
    float get_units(byte times = 1) { return (float)cs_obj_r(cs_nome, "get_units", times); }
    void tare(byte times = 10) { cs_obj_n(cs_nome, "tare", times); }
    void set_scale(float scale = 1.f) { cs_obj_n(cs_nome, "set_scale", scale); }
    float get_scale() { return (float)cs_obj_r(cs_nome, "get_scale"); }
    void set_offset(long offset = 0) { cs_obj_n(cs_nome, "set_offset", offset); }
    long get_offset() { return (long)cs_obj_i(cs_nome, "get_offset"); }
    void power_down() { cs_obj_n(cs_nome, "power_down"); }
    void power_up() { cs_obj_n(cs_nome, "power_up"); }
};

#endif
