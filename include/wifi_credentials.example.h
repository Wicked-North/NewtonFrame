#pragma once

// Copy this file to wifi_credentials.h and edit the values for your network.
// wifi_credentials.h is ignored by Git. Leave WIFI_SSID empty to use only
// the fallback access point.
#define WIFI_SSID ""
#define WIFI_PASSWORD ""

// Credentials for the ESP32 file editor. Change these before exposing the
// device on an untrusted network.
#define WEB_USERNAME "admin"
#define WEB_PASSWORD "change-me"

// Direct fallback access point used when station Wi-Fi is not configured or
// cannot be reached within 20 seconds. WPA2 passwords require 8+ characters.
#define FALLBACK_AP_SSID "SWD-Photo"
#define FALLBACK_AP_PASSWORD "epaper648"
