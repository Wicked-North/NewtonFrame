#include <Arduino.h>

#define EPD_CS    15  // TEST pad
#define EPD_DC    27  // D/L pad
#define EPD_RST   26  // RST pad
#define EPD_MOSI  14  // TXD pad
#define EPD_SCK   13  // RXD pad

#define PAYLOAD_BYTES 15000UL

// Your known-good test uses 0x00 and produces red.
// 0xFF is the first yellow candidate for this panel/protocol.
#define YELLOW_PAYLOAD 0xFF

void sendByteSoftwareSPI(uint8_t data)
{
  for (int bit = 0; bit < 8; bit++)
  {
    digitalWrite(EPD_SCK, LOW);
    digitalWrite(EPD_MOSI, (data & 0x80) ? HIGH : LOW);
    digitalWrite(EPD_SCK, HIGH);
    data <<= 1;
  }
  digitalWrite(EPD_SCK, LOW);
}

void sendCommand(uint8_t command)
{
  digitalWrite(EPD_DC, LOW);
  digitalWrite(EPD_CS, LOW);
  sendByteSoftwareSPI(command);
  digitalWrite(EPD_CS, HIGH);
}

void sendData(uint8_t data)
{
  digitalWrite(EPD_DC, HIGH);
  digitalWrite(EPD_CS, LOW);
  sendByteSoftwareSPI(data);
  digitalWrite(EPD_CS, HIGH);
}

void setup()
{
  Serial.begin(115200);
  delay(1000);
  Serial.println("\n--- SoluM yellow test ---");

  pinMode(EPD_CS, OUTPUT);
  pinMode(EPD_DC, OUTPUT);
  pinMode(EPD_RST, OUTPUT);
  pinMode(EPD_MOSI, OUTPUT);
  pinMode(EPD_SCK, OUTPUT);

  digitalWrite(EPD_CS, HIGH);
  digitalWrite(EPD_DC, HIGH);
  digitalWrite(EPD_SCK, LOW);
  digitalWrite(EPD_MOSI, LOW);

  Serial.println("Performing hardware reset...");
  digitalWrite(EPD_RST, HIGH);
  delay(50);
  digitalWrite(EPD_RST, LOW);
  delay(200);
  digitalWrite(EPD_RST, HIGH);
  delay(200);

  Serial.println("Sending software reset...");
  sendCommand(0x12);
  delay(200);

  Serial.println("Streaming yellow candidate payload 0xFF...");
  sendCommand(0x24);
  for (uint32_t index = 0; index < PAYLOAD_BYTES; index++)
  {
    sendData(YELLOW_PAYLOAD);
  }

  Serial.println("Triggering display refresh...");
  sendCommand(0x20);
  Serial.println("Yellow test command sent.");
}

void loop()
{
  // Run once.
}
