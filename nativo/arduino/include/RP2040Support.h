/* RP2040Support.h -- o objeto rp2040 do core arduino-pico (o f_cpu, os ciclos, a
 * heap, o watchdog, o reboot e o FIFO entre os nucleos), sobre o simulador: os
 * numeros sao os da placa da bancada e o reboot reinicia a placa. */
#pragma once

#include <stdint.h>
#include <stddef.h>
#include <deque>
#include <mutex>

extern "C" volatile bool __otherCoreIdled;

/* O FIFO do SIO entre o nucleo 0 e o 1: o que um empurra, o outro tira. */
class _MFIFO {
public:
    void begin(int cores) { (void)cores; }
    void registerCore() {}
    void push(uint32_t val)
    {
        std::lock_guard<std::mutex> t(trava);
        fila.push_back(val);
    }
    bool push_nb(uint32_t val)
    {
        push(val);
        return true;
    }
    uint32_t pop()
    {
        uint32_t v;
        while (!pop_nb(&v)) delay(1);
        return v;
    }
    bool pop_nb(uint32_t *val)
    {
        std::lock_guard<std::mutex> t(trava);
        if (fila.empty()) return false;
        *val = fila.front();
        fila.pop_front();
        return true;
    }
    int available()
    {
        std::lock_guard<std::mutex> t(trava);
        return (int)fila.size();
    }
    void clear()
    {
        std::lock_guard<std::mutex> t(trava);
        fila.clear();
    }
    void idleOtherCore() {}
    void resumeOtherCore() {}

private:
    std::deque<uint32_t> fila;
    std::mutex trava;
};

class RP2040 {
public:
    RP2040() {}
    void begin(int cpuid) { (void)cpuid; }

    static int usToPIOCycles(int us) { return us * (f_cpu() / 1000000); }
    static int f_cpu();
    static int cpuid() { return 0; }

    inline uint32_t getCycleCount() { return (uint32_t)getCycleCount64(); }
    inline uint64_t getCycleCount64() { return (uint64_t)micros() * (uint64_t)(f_cpu() / 1000000); }

    int getFreeHeap();
    int getUsedHeap();
    int getTotalHeap();
    inline int getFreePSRAMHeap() { return 0; }
    inline int getUsedPSRAMHeap() { return 0; }
    inline int getTotalPSRAMHeap() { return 0; }
    inline size_t getPSRAMSize() { return 0; }
    inline uint32_t getStackPointer()
    {
        int marca;
        return (uint32_t)(uintptr_t)&marca;
    }
    inline int getFreeStack() { return 4096; }

    void idleOtherCore() { __otherCoreIdled = true; }
    void resumeOtherCore() { __otherCoreIdled = false; }
    void restartCore1() {}

    void reboot();
    inline void restart() { reboot(); }
    /* O modo do BOOTSEL (o disco RPI-RP2): no simulador a placa so reinicia. */
    inline void rebootToBootloader() { reboot(); }
    static void enableDoubleResetBootloader() {}

    void wdt_begin(uint32_t delay_ms);
    void wdt_reset();

    bool getBootselButton();

    void *memcpyDMA(void *dest, const void *src, size_t n) { return memcpy(dest, src, n); }
    uint32_t hwrand32() { return ((uint32_t)random(0x10000) << 16) | (uint32_t)random(0x10000); }
    bool isPicoW();

    _MFIFO fifo;
};

extern RP2040 rp2040;
