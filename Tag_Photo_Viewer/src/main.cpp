#include <Arduino.h>

namespace {
constexpr uint16_t PANEL_WIDTH = 648;
constexpr uint16_t PANEL_HEIGHT = 480;
constexpr uint32_t IMAGE_ADDRESS = 0x0001D000;
constexpr uint32_t IMAGE_SIZE = PANEL_WIDTH * PANEL_HEIGHT / 4;
constexpr uint32_t IMAGE_MARKER_ADDRESS = 0x0002FFFC;
constexpr uint32_t IMAGE_MARKER = 0x31475045;  // "EPG1" in little endian

constexpr uint8_t EPD_BS = 2;
constexpr uint8_t EPD_BUSY = 3;
constexpr uint8_t EPD_RST = 4;
constexpr uint8_t EPD_DC = 5;
constexpr uint8_t EPD_CS = 6;
constexpr uint8_t EPD_POWER = 7;
constexpr uint8_t EPD_CLK = 19;
constexpr uint8_t EPD_MOSI = 20;
constexpr uint8_t EPD_MISO = 24;
constexpr uint8_t EPD_HLT = 23;

constexpr uint8_t CMD_PANEL_SETTING = 0x00;
constexpr uint8_t CMD_POWER_SETTING = 0x01;
constexpr uint8_t CMD_POWER_OFF = 0x02;
constexpr uint8_t CMD_POWER_ON = 0x04;
constexpr uint8_t CMD_BOOSTER_SOFT_START = 0x06;
constexpr uint8_t CMD_DEEP_SLEEP = 0x07;
constexpr uint8_t CMD_DATA_START = 0x10;
constexpr uint8_t CMD_DATA_STOP = 0x11;
constexpr uint8_t CMD_DISPLAY_REFRESH = 0x12;
constexpr uint8_t CMD_TEMPERATURE_SELECT = 0x41;
constexpr uint8_t CMD_VCOM_INTERVAL = 0x50;
constexpr uint8_t CMD_TCON_SETTING = 0x60;
constexpr uint8_t CMD_RESOLUTION_SETTING = 0x61;
constexpr uint8_t CMD_POWER_SAVING = 0xE3;
constexpr uint8_t CMD_FORCE_TEMPERATURE = 0xE5;

const uint8_t viewerId[16] __attribute__((section(".viewer_id"), used)) = {
    'E', 'P', 'H', 'O', 'T', 'O', '6', '4', '8', 'V', '1', 0x00, 0x51, 0xA7, 0x2C, 0xE9
};

void spiWrite(uint8_t value) {
    NRF_SPI0->EVENTS_READY = 0;
    NRF_SPI0->TXD = value;
    while (!NRF_SPI0->EVENTS_READY) {}
    (void)NRF_SPI0->RXD;
}

void command(uint8_t value) {
    digitalWrite(EPD_DC, LOW);
    digitalWrite(EPD_CS, LOW);
    spiWrite(value);
    digitalWrite(EPD_CS, HIGH);
}

void data(uint8_t value) {
    digitalWrite(EPD_DC, HIGH);
    digitalWrite(EPD_CS, LOW);
    spiWrite(value);
    digitalWrite(EPD_CS, HIGH);
}

void writeCommand(uint8_t commandValue, const uint8_t *values, size_t count) {
    command(commandValue);
    for (size_t i = 0; i < count; i++) data(values[i]);
}

bool waitBusyHigh(uint32_t timeoutMs) {
    const uint32_t start = millis();
    while (!digitalRead(EPD_BUSY)) {
        if (millis() - start >= timeoutMs) return false;
    }
    return true;
}

bool resetPanel() {
    for (uint8_t pulse = 10; pulse <= 90; pulse += 20) {
        digitalWrite(EPD_RST, HIGH);
        delay(pulse);
        digitalWrite(EPD_RST, LOW);
        delay(pulse);
        digitalWrite(EPD_RST, HIGH);
        delay(pulse);
        if (waitBusyHigh(50 + pulse)) {
            delay(pulse);
            return true;
        }
    }
    return false;
}

void configurePanelPins() {
    pinMode(EPD_POWER, OUTPUT);
    digitalWrite(EPD_POWER, HIGH);
    pinMode(EPD_RST, OUTPUT);
    pinMode(EPD_BS, OUTPUT);
    pinMode(EPD_CS, OUTPUT);
    pinMode(EPD_DC, OUTPUT);
    pinMode(EPD_BUSY, INPUT);
    pinMode(EPD_CLK, OUTPUT);
    pinMode(EPD_MOSI, OUTPUT);
    pinMode(EPD_HLT, OUTPUT);
    pinMode(EPD_MISO, INPUT);
    digitalWrite(EPD_HLT, HIGH);
    digitalWrite(EPD_BS, LOW);
    digitalWrite(EPD_CS, HIGH);

    NRF_SPI0->PSELSCK = EPD_CLK;
    NRF_SPI0->PSELMOSI = EPD_MOSI;
    NRF_SPI0->PSELMISO = EPD_MISO;
    NRF_SPI0->CONFIG =
        (SPI_CONFIG_CPOL_ActiveHigh << SPI_CONFIG_CPOL_Pos) |
        (SPI_CONFIG_CPHA_Leading << SPI_CONFIG_CPHA_Pos);
    NRF_SPI0->FREQUENCY = SPI_FREQUENCY_FREQUENCY_M8;
    NRF_SPI0->ENABLE = SPI_ENABLE_ENABLE_Enabled << SPI_ENABLE_ENABLE_Pos;
}

bool setupPanel() {
    if (!resetPanel()) return false;
    const uint8_t panel[] = {0xEF, 0x08};
    const uint8_t power[] = {0x07, 0x00};
    const uint8_t booster[] = {0xC7, 0xCC, 0x1B};
    const uint8_t temperature[] = {0x00};
    const uint8_t vcom[] = {0x77};
    const uint8_t tcon[] = {0x22};
    const uint8_t resolution[] = {0x02, 0x88, 0x01, 0xE0};
    const uint8_t saving[] = {0xAA};
    const uint8_t forcedTemperature[] = {0x03};

    writeCommand(CMD_PANEL_SETTING, panel, sizeof(panel));
    writeCommand(CMD_POWER_SETTING, power, sizeof(power));
    writeCommand(CMD_BOOSTER_SOFT_START, booster, sizeof(booster));
    waitBusyHigh(250);
    command(CMD_POWER_ON);
    if (!waitBusyHigh(2000)) return false;
    writeCommand(CMD_TEMPERATURE_SELECT, temperature, sizeof(temperature));
    writeCommand(CMD_VCOM_INTERVAL, vcom, sizeof(vcom));
    writeCommand(CMD_TCON_SETTING, tcon, sizeof(tcon));
    writeCommand(CMD_RESOLUTION_SETTING, resolution, sizeof(resolution));
    writeCommand(CMD_POWER_SAVING, saving, sizeof(saving));
    writeCommand(CMD_FORCE_TEMPERATURE, forcedTemperature, sizeof(forcedTemperature));
    delay(10);
    return true;
}

void displayStoredImage() {
    const uint8_t *image = reinterpret_cast<const uint8_t *>(IMAGE_ADDRESS);
    command(CMD_DATA_START);
    digitalWrite(EPD_DC, HIGH);
    digitalWrite(EPD_CS, LOW);
    for (uint32_t i = 0; i < IMAGE_SIZE; i++) spiWrite(image[i]);
    digitalWrite(EPD_CS, HIGH);
    command(CMD_DATA_STOP);

    const uint8_t refreshMode = 0x00;
    writeCommand(CMD_DISPLAY_REFRESH, &refreshMode, 1);
    waitBusyHigh(50000);

    const uint8_t powerOffMode = 0x00;
    writeCommand(CMD_POWER_OFF, &powerOffMode, 1);
    waitBusyHigh(2000);
    const uint8_t sleepKey = 0xA5;
    writeCommand(CMD_DEEP_SLEEP, &sleepKey, 1);
    delay(100);
}

void powerDownPanel() {
    NRF_SPI0->ENABLE = SPI_ENABLE_ENABLE_Disabled << SPI_ENABLE_ENABLE_Pos;
    digitalWrite(EPD_POWER, LOW);
    digitalWrite(EPD_RST, LOW);
    digitalWrite(EPD_BS, LOW);
    digitalWrite(EPD_CS, LOW);
    digitalWrite(EPD_DC, LOW);
    digitalWrite(EPD_CLK, LOW);
    digitalWrite(EPD_MOSI, LOW);
    digitalWrite(EPD_HLT, LOW);
}
}  // namespace

void setup() {
    if (*reinterpret_cast<const uint32_t *>(IMAGE_MARKER_ADDRESS) == IMAGE_MARKER) {
        configurePanelPins();
        if (setupPanel()) displayStoredImage();
        powerDownPanel();
    }

    NRF_POWER->SYSTEMOFF = 1;
    while (true) __WFE();
}

void loop() {}
