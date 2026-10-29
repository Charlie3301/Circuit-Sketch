/* Ticker.h -- o Ticker da ESP32: chamar uma funcao de tempos em tempos (ou uma
 * vez), pelo esp_timer -- a funcao roda na tarefa do esp_timer, fora do
 * loop(), como na placa. A mesma API do core oficial. */
#ifndef TICKER_H
#define TICKER_H

extern "C" {
#include "esp_timer.h"
}
#include <functional>

class Ticker {
public:
    Ticker() : _timer(nullptr) {}
    ~Ticker() { detach(); }

    typedef void (*callback_with_arg_t)(void *);
    typedef std::function<void(void)> callback_function_t;

    void attach(float seconds, callback_function_t callback) { _com(1000000ULL * seconds, true, std::move(callback)); }
    void attach_ms(uint64_t milliseconds, callback_function_t callback) { _com(1000ULL * milliseconds, true, std::move(callback)); }
    void attach_us(uint64_t micros, callback_function_t callback) { _com(micros, true, std::move(callback)); }
    template <typename TArg> void attach(float seconds, void (*callback)(TArg), TArg arg)
    {
        _com(1000000ULL * seconds, true, [callback, arg]() { callback(arg); });
    }
    template <typename TArg> void attach_ms(uint64_t milliseconds, void (*callback)(TArg), TArg arg)
    {
        _com(1000ULL * milliseconds, true, [callback, arg]() { callback(arg); });
    }
    template <typename TArg> void attach_us(uint64_t micros, void (*callback)(TArg), TArg arg)
    {
        _com(micros, true, [callback, arg]() { callback(arg); });
    }

    void once(float seconds, callback_function_t callback) { _com(1000000ULL * seconds, false, std::move(callback)); }
    void once_ms(uint64_t milliseconds, callback_function_t callback) { _com(1000ULL * milliseconds, false, std::move(callback)); }
    void once_us(uint64_t micros, callback_function_t callback) { _com(micros, false, std::move(callback)); }
    template <typename TArg> void once(float seconds, void (*callback)(TArg), TArg arg)
    {
        _com(1000000ULL * seconds, false, [callback, arg]() { callback(arg); });
    }
    template <typename TArg> void once_ms(uint64_t milliseconds, void (*callback)(TArg), TArg arg)
    {
        _com(1000ULL * milliseconds, false, [callback, arg]() { callback(arg); });
    }
    template <typename TArg> void once_us(uint64_t micros, void (*callback)(TArg), TArg arg)
    {
        _com(micros, false, [callback, arg]() { callback(arg); });
    }

    void restart(float seconds) { restart_us(1000000ULL * seconds); }
    void restart_ms(uint64_t milliseconds) { restart_us(1000ULL * milliseconds); }
    void restart_us(uint64_t micros)
    {
        if (_timer) esp_timer_restart(_timer, micros);
    }

    void detach()
    {
        if (_timer) {
            esp_timer_stop(_timer);
            esp_timer_delete(_timer);
            _timer = nullptr;
            _callback_function = nullptr;
        }
    }
    bool active() const { return _timer && esp_timer_is_active(_timer); }

protected:
    static void _static_callback(void *arg)
    {
        Ticker *t = (Ticker *)arg;
        if (t && t->_callback_function) t->_callback_function();
    }
    callback_function_t _callback_function = nullptr;
    esp_timer_handle_t _timer;

private:
    void _com(uint64_t micros, bool repeat, callback_function_t callback)
    {
        detach();
        _callback_function = std::move(callback);
        _attach_us(micros, repeat, _static_callback, this);
    }
    void _attach_us(uint64_t micros, bool repeat, callback_with_arg_t callback, void *arg)
    {
        esp_timer_create_args_t args = {};
        args.callback = callback;
        args.arg = arg;
        args.dispatch_method = ESP_TIMER_TASK;
        args.name = "Ticker";
        if (esp_timer_create(&args, &_timer) != ESP_OK) return;
        if (repeat) esp_timer_start_periodic(_timer, micros);
        else esp_timer_start_once(_timer, micros);
    }
};

#endif
