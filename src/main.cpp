/*
   Copyright (c) 2021 Aaron Christophel ATCnetz.de
   SPDX-License-Identifier: GPL-3.0-or-later
*/
#include <Arduino.h>
#include <WiFi.h>
#include <Preferences.h>
#include <mbedtls/base64.h>

#ifdef ENABLE_OTA
#include <ArduinoOTA.h>
#endif

#include "web.h"
#include "glitcher.h"
#include "nrf_swd.h"
#include "swd.h"

namespace
{
bool decode_base64(const String &encoded, String &decoded)
{
  size_t required = 0;
  const unsigned char *input = reinterpret_cast<const unsigned char *>(encoded.c_str());
  mbedtls_base64_decode(nullptr, 0, &required, input, encoded.length());
  if (required == 0 || required > 128)
    return false;
  unsigned char output[129] = {0};
  size_t written = 0;
  if (mbedtls_base64_decode(output, sizeof(output) - 1, &written, input, encoded.length()) != 0)
    return false;
  output[written] = 0;
  decoded = reinterpret_cast<char *>(output);
  return true;
}

void handle_manager_serial()
{
  if (!Serial.available())
    return;
  String command = Serial.readStringUntil('\n');
  command.trim();
  if (command == "ROGGENCORE?")
  {
    const IPAddress address = WiFi.getMode() == WIFI_AP ? WiFi.softAPIP() : WiFi.localIP();
    Serial.printf("ROGGENCORE_BRIDGE;1;http://%s\r\n", address.toString().c_str());
  }
  else if (command.startsWith("ROGGENCORE_WIFI;"))
  {
    const int separator = command.indexOf(';', 16);
    String configured_ssid;
    String configured_password;
    if (separator < 0 ||
        !decode_base64(command.substring(16, separator), configured_ssid) ||
        !decode_base64(command.substring(separator + 1), configured_password) ||
        configured_ssid.length() == 0)
    {
      Serial.println("ROGGENCORE_ERROR;INVALID_WIFI_CONFIGURATION");
      return;
    }
    Preferences preferences;
    if (!preferences.begin("roggencore", false))
    {
      Serial.println("ROGGENCORE_ERROR;NVS_UNAVAILABLE");
      return;
    }
    preferences.putString("wifiSsid", configured_ssid);
    preferences.putString("wifiPass", configured_password);
    const bool saved = preferences.getString("wifiSsid", "") == configured_ssid &&
                       preferences.getString("wifiPass", "") == configured_password;
    preferences.end();
    configured_password = "";
    if (!saved)
    {
      Serial.println("ROGGENCORE_ERROR;NVS_WRITE_FAILED");
      return;
    }
    Serial.println("ROGGENCORE_WIFI_SAVED");
    Serial.flush();
    delay(100);
    ESP.restart();
  }
}
}

void setup()
{
  Serial.begin(115200);
  delay(2000);
  swd_begin();
  glitcher_begin();
  init_web();
  const uint32_t swd_id = nrf_begin();
  Serial.printf("SWD Id: 0x%08x\r\n", swd_id);
  if (swd_id == 0x2ba01477 && is_nrf_connected() == 2)
  {
    write_register(0xE000EDF0, 0xA05F0000, true);
    Serial.println("nRF CPU released after startup probe");
  }

#ifdef ENABLE_OTA
  ArduinoOTA.begin();
#endif
}

void loop()
{
  handle_manager_serial();
#ifdef ENABLE_OTA
  ArduinoOTA.handle();
#endif
  if (get_glitcher())
  {
    do_glitcher();
  }
  else
  {
    do_nrf_swd();
  }
}
