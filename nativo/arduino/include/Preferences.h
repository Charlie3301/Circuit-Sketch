/* Preferences.h -- o NVS da ESP32 (chave e valor que sobrevivem ao reset). */
#ifndef _PREFERENCES_H_
#define _PREFERENCES_H_

#include "cs_objeto.h"

typedef enum { PT_I8, PT_U8, PT_I16, PT_U16, PT_I32, PT_U32, PT_I64, PT_U64, PT_STR, PT_BLOB, PT_INVALID } PreferenceType;

class Preferences : public CsObjeto {
public:
    Preferences() { cs_criar("Preferences"); }
    bool begin(const char *name, bool readOnly = false, const char *partition_label = NULL)
    {
        (void)partition_label;
        return cs_obj_i(cs_nome, "begin", name, readOnly) != 0;
    }
    void end() { cs_obj_n(cs_nome, "end"); }
    bool clear() { return cs_obj_i(cs_nome, "clear") != 0; }
    bool remove(const char *key) { return cs_obj_i(cs_nome, "remove", key) != 0; }
    bool isKey(const char *key) { return cs_obj_i(cs_nome, "isKey", key) != 0; }
    size_t freeEntries() { return (size_t)cs_obj_i(cs_nome, "freeEntries"); }
    PreferenceType getType(const char *key) { return (PreferenceType)cs_obj_i(cs_nome, "getType", key); }

#define CS_PREF_INT(tipo, put, get)                                                                                 \
    size_t put(const char *key, tipo value) { return (size_t)cs_obj_i(cs_nome, #put, key, value); }               \
    tipo get(const char *key, tipo defaultValue = 0) { return (tipo)cs_obj_i(cs_nome, #get, key, defaultValue); }
    CS_PREF_INT(int8_t, putChar, getChar)
    CS_PREF_INT(uint8_t, putUChar, getUChar)
    CS_PREF_INT(int16_t, putShort, getShort)
    CS_PREF_INT(uint16_t, putUShort, getUShort)
    CS_PREF_INT(int32_t, putInt, getInt)
    CS_PREF_INT(uint32_t, putUInt, getUInt)
    CS_PREF_INT(int32_t, putLong, getLong)
    CS_PREF_INT(uint32_t, putULong, getULong)
    CS_PREF_INT(int64_t, putLong64, getLong64)
    CS_PREF_INT(uint64_t, putULong64, getULong64)
#undef CS_PREF_INT
    size_t putFloat(const char *key, float value) { return (size_t)cs_obj_i(cs_nome, "putFloat", key, value); }
    float getFloat(const char *key, float defaultValue = NAN) { return (float)cs_obj_r(cs_nome, "getFloat", key, defaultValue); }
    size_t putDouble(const char *key, double value) { return (size_t)cs_obj_i(cs_nome, "putDouble", key, value); }
    double getDouble(const char *key, double defaultValue = NAN) { return cs_obj_r(cs_nome, "getDouble", key, defaultValue); }
    size_t putBool(const char *key, bool value) { return (size_t)cs_obj_i(cs_nome, "putBool", key, value); }
    bool getBool(const char *key, bool defaultValue = false) { return cs_obj_i(cs_nome, "getBool", key, defaultValue) != 0; }
    size_t putString(const char *key, const char *value) { return (size_t)cs_obj_i(cs_nome, "putString", key, value); }
    size_t putString(const char *key, const String &value) { return (size_t)cs_obj_i(cs_nome, "putString", key, value); }
    String getString(const char *key, const String defaultValue = String())
    {
        return cs_obj_t(cs_nome, "getString", key, defaultValue);
    }
    size_t getString(const char *key, char *value, size_t maxLen)
    {
        String s = getString(key);
        snprintf(value, maxLen, "%s", s.c_str());
        return s.length();
    }
    size_t putBytes(const char *key, const void *value, size_t len)
    {
        return (size_t)cs_obj_i(cs_nome, "putBytes", key, cs_vetor(value, (int)len), (int)len);
    }
    size_t getBytes(const char *key, void *buf, size_t maxLen)
    {
        return (size_t)cs_obj_i(cs_nome, "getBytes", key, cs_vetor_mutavel(buf, (int)maxLen), (int)maxLen);
    }
    size_t getBytesLength(const char *key) { return (size_t)cs_obj_i(cs_nome, "getBytesLength", key); }
};

#endif
