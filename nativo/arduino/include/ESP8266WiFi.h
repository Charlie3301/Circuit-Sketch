/* ESP8266WiFi.h -- o WiFi do ESP8266: o mesmo WiFi da bancada. */
#ifndef ESP8266WiFi_h
#define ESP8266WiFi_h
#include "WiFi.h"
#include "WiFiClientSecure.h"
typedef WiFiClientSecure BearSSLClient;
namespace BearSSL { typedef WiFiClientSecure WiFiClientSecure; }
#endif
