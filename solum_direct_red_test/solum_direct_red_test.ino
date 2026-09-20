#include <Arduino.h>

#define EPD_CS   15  // TEST Pad
#define EPD_DC   27  // D/L Pad
#define EPD_RST  26  // RST Pad
#define EPD_MOSI 14  // TXD Pad
#define EPD_SCK  13  // RXD Pad

void sendByteSoftwareSPI(uint8_t data) {
  for (int i = 0; i < 8; i++) {
    digitalWrite(EPD_SCK, LOW);
    if (data & 0x80) {
      digitalWrite(EPD_MOSI, HIGH);
    } else {
      digitalWrite(EPD_MOSI, LOW);
    }
    digitalWrite(EPD_SCK, HIGH);
    data <<= 1;
  }
  digitalWrite(EPD_SCK, LOW);
}

void sendCommand(uint8_t cmd) {
  digitalWrite(EPD_DC, LOW);
  digitalWrite(EPD_CS, LOW);
  sendByteSoftwareSPI(cmd);
  digitalWrite(EPD_CS, HIGH);
}

void sendData(uint8_t data) {
  digitalWrite(EPD_DC, HIGH);
  digitalWrite(EPD_CS, LOW);
  sendByteSoftwareSPI(data);
  digitalWrite(EPD_CS, HIGH);
}

void setup() {
  Serial.begin(115200);
  delay(1000);
  Serial.println("\n--- SoluM Recovery & Red Fill Test ---");

  pinMode(EPD_CS, OUTPUT);
  pinMode(EPD_DC, OUTPUT);
  pinMode(EPD_RST, OUTPUT);
  pinMode(EPD_MOSI, OUTPUT);
  pinMode(EPD_SCK, OUTPUT);

  // Forced Hardware Reset sequence to wake up controller
  Serial.println("Performing forced hardware reset...");
  digitalWrite(EPD_RST, HIGH);
  delay(50);
  digitalWrite(EPD_RST, LOW);
  delay(200);
  digitalWrite(EPD_RST, HIGH);
  delay(200);

  // Software Reset
  sendCommand(0x12);
  delay(200);

  // Fill RAM with 0x00 (Known Red payload)
  Serial.println("Streaming 0x00 payload...");
  sendCommand(0x24);
  for (uint32_t i = 0; i < 15000; i++) {
    sendData(0x00);
  }

  // Master Activation
  Serial.println("Triggering display refresh...");
  sendCommand(0x20);
  Serial.println("Recovery command sent!");
}

void loop() {
  // Run once
}