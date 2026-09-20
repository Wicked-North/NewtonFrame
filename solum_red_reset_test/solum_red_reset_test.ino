#include <Arduino.h>

// SoluM test-pad connections
#define EPD_CS    15  // TEST pad
#define EPD_DC    27  // D/L pad
#define EPD_RST   26  // RST / reset pad
#define EPD_MOSI  14  // TXD pad
#define EPD_SCK   13  // RXD pad
#define EPD_BUSY   4  // BUSY, currently monitored only

// This is the payload size that worked in the original test.
// It should eventually be replaced with the exact panel frame size.
#define RED_PAYLOAD_BYTES 15000UL

// Keep false for the first test; this matches the original known-working code.
// Set true only after confirming that the red refresh works normally.
#define HOLD_RESET_AFTER_REFRESH false

void sendByteSoftwareSPI(uint8_t data)
{
  for (uint8_t bit = 0; bit < 8; bit++)
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

void resetDisplay()
{
  Serial.println("Resetting display/reset line...");
  digitalWrite(EPD_RST, HIGH);
  delay(50);
  digitalWrite(EPD_RST, LOW);
  delay(200);
  digitalWrite(EPD_RST, HIGH);
  delay(200);
}

void setup()
{
  Serial.begin(115200);
  delay(1000);

  Serial.println();
  Serial.println("--- SoluM red test with nRF52 reset test ---");
  Serial.println("Keep SoluM USB-C disconnected when powering from ESP32 3.3V.");

  pinMode(EPD_CS, OUTPUT);
  pinMode(EPD_DC, OUTPUT);
  pinMode(EPD_RST, OUTPUT);
  pinMode(EPD_MOSI, OUTPUT);
  pinMode(EPD_SCK, OUTPUT);
  pinMode(EPD_BUSY, INPUT);

  // CS must be inactive during startup.
  digitalWrite(EPD_CS, HIGH);
  digitalWrite(EPD_DC, HIGH);
  digitalWrite(EPD_SCK, LOW);
  digitalWrite(EPD_MOSI, LOW);

  resetDisplay();

  Serial.println("Sending software reset...");
  sendCommand(0x12);
  delay(200);

  Serial.println("Writing red payload...");
  sendCommand(0x24);
  for (uint32_t index = 0; index < RED_PAYLOAD_BYTES; index++)
  {
    sendData(0x00);
  }

  Serial.println("Starting display refresh...");
  sendCommand(0x20);

  // Give the display controller time to begin the refresh.
  delay(100);

  if (HOLD_RESET_AFTER_REFRESH)
  {
    Serial.println("Holding RST LOW to stop the onboard nRF52 redraw test.");
    digitalWrite(EPD_RST, LOW);
  }
  else
  {
    Serial.println("Leaving RST HIGH.");
  }

  Serial.println("Test complete. Watch whether the barcode returns.");
}

void loop()
{
  // Run once only.
}
