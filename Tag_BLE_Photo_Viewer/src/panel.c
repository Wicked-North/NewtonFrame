#include "panel.h"

#include "nrf.h"
#include "nrf_delay.h"
#include "nrf_gpio.h"

#define EPD_BS 2u
#define EPD_BUSY 3u
#define EPD_RST 4u
#define EPD_DC 5u
#define EPD_CS 6u
#define EPD_POWER 7u
#define EPD_CLK 19u
#define EPD_MOSI 20u
#define EPD_HLT 23u
#define EPD_MISO 24u

#define CMD_PANEL_SETTING 0x00u
#define CMD_POWER_SETTING 0x01u
#define CMD_POWER_OFF 0x02u
#define CMD_POWER_ON 0x04u
#define CMD_BOOSTER_SOFT_START 0x06u
#define CMD_DEEP_SLEEP 0x07u
#define CMD_DATA_START 0x10u
#define CMD_DATA_STOP 0x11u
#define CMD_DISPLAY_REFRESH 0x12u
#define CMD_TEMPERATURE_SELECT 0x41u
#define CMD_VCOM_INTERVAL 0x50u
#define CMD_TCON_SETTING 0x60u
#define CMD_RESOLUTION_SETTING 0x61u
#define CMD_POWER_SAVING 0xE3u
#define CMD_FORCE_TEMPERATURE 0xE5u

static bool m_panel_active;
static bool m_stream_open;

static void spi_write(uint8_t value) {
    NRF_SPI0->EVENTS_READY = 0;
    NRF_SPI0->TXD = value;
    while (NRF_SPI0->EVENTS_READY == 0) {}
    (void)NRF_SPI0->RXD;
}

static void command(uint8_t value) {
    nrf_gpio_pin_clear(EPD_DC);
    nrf_gpio_pin_clear(EPD_CS);
    spi_write(value);
    nrf_gpio_pin_set(EPD_CS);
}

static void write_command(uint8_t command_value, uint8_t const *values, uint8_t count) {
    command(command_value);
    nrf_gpio_pin_set(EPD_DC);
    nrf_gpio_pin_clear(EPD_CS);
    for (uint8_t i = 0; i < count; ++i) spi_write(values[i]);
    nrf_gpio_pin_set(EPD_CS);
}

static bool wait_busy_high(uint32_t timeout_ms) {
    while (nrf_gpio_pin_read(EPD_BUSY) == 0) {
        if (timeout_ms-- == 0) return false;
        nrf_delay_ms(1);
    }
    return true;
}

static bool reset_panel(void) {
    for (uint8_t pulse = 10; pulse <= 90; pulse += 20) {
        nrf_gpio_pin_set(EPD_RST);
        nrf_delay_ms(pulse);
        nrf_gpio_pin_clear(EPD_RST);
        nrf_delay_ms(pulse);
        nrf_gpio_pin_set(EPD_RST);
        nrf_delay_ms(pulse);
        if (wait_busy_high(50u + pulse)) {
            nrf_delay_ms(pulse);
            return true;
        }
    }
    return false;
}

static void configure_pins(void) {
    nrf_gpio_cfg_output(EPD_POWER);
    nrf_gpio_pin_set(EPD_POWER);
    nrf_gpio_cfg_output(EPD_RST);
    nrf_gpio_cfg_output(EPD_BS);
    nrf_gpio_cfg_output(EPD_CS);
    nrf_gpio_cfg_output(EPD_DC);
    nrf_gpio_cfg_input(EPD_BUSY, NRF_GPIO_PIN_NOPULL);
    nrf_gpio_cfg_output(EPD_CLK);
    nrf_gpio_cfg_output(EPD_MOSI);
    nrf_gpio_cfg_output(EPD_HLT);
    nrf_gpio_cfg_input(EPD_MISO, NRF_GPIO_PIN_NOPULL);
    nrf_gpio_pin_set(EPD_HLT);
    nrf_gpio_pin_clear(EPD_BS);
    nrf_gpio_pin_set(EPD_CS);

    NRF_SPI0->PSELSCK = EPD_CLK;
    NRF_SPI0->PSELMOSI = EPD_MOSI;
    NRF_SPI0->PSELMISO = EPD_MISO;
    NRF_SPI0->CONFIG =
        (SPI_CONFIG_CPOL_ActiveHigh << SPI_CONFIG_CPOL_Pos) |
        (SPI_CONFIG_CPHA_Leading << SPI_CONFIG_CPHA_Pos);
    NRF_SPI0->FREQUENCY = SPI_FREQUENCY_FREQUENCY_M8;
    NRF_SPI0->ENABLE = SPI_ENABLE_ENABLE_Enabled << SPI_ENABLE_ENABLE_Pos;
    m_panel_active = true;
}

bool panel_begin_stream(void) {
    static uint8_t const panel[] = {0xEF, 0x08};
    static uint8_t const power[] = {0x07, 0x00};
    static uint8_t const booster[] = {0xC7, 0xCC, 0x1B};
    static uint8_t const temperature[] = {0x00};
    static uint8_t const vcom[] = {0x77};
    static uint8_t const tcon[] = {0x22};
    static uint8_t const resolution[] = {0x02, 0x88, 0x01, 0xE0};
    static uint8_t const saving[] = {0xAA};
    static uint8_t const forced_temperature[] = {0x03};

    panel_abort_stream();
    configure_pins();
    if (!reset_panel()) {
        panel_abort_stream();
        return false;
    }

    write_command(CMD_PANEL_SETTING, panel, sizeof(panel));
    write_command(CMD_POWER_SETTING, power, sizeof(power));
    write_command(CMD_BOOSTER_SOFT_START, booster, sizeof(booster));
    (void)wait_busy_high(250);
    command(CMD_POWER_ON);
    if (!wait_busy_high(2000)) {
        panel_abort_stream();
        return false;
    }
    write_command(CMD_TEMPERATURE_SELECT, temperature, sizeof(temperature));
    write_command(CMD_VCOM_INTERVAL, vcom, sizeof(vcom));
    write_command(CMD_TCON_SETTING, tcon, sizeof(tcon));
    write_command(CMD_RESOLUTION_SETTING, resolution, sizeof(resolution));
    write_command(CMD_POWER_SAVING, saving, sizeof(saving));
    write_command(CMD_FORCE_TEMPERATURE, forced_temperature, sizeof(forced_temperature));
    nrf_delay_ms(10);

    command(CMD_DATA_START);
    m_stream_open = true;
    return true;
}

void panel_write_stream(uint8_t const *data, uint16_t length) {
    if (!m_stream_open) return;
    nrf_gpio_pin_set(EPD_DC);
    nrf_gpio_pin_clear(EPD_CS);
    while (length--) spi_write(*data++);
    nrf_gpio_pin_set(EPD_CS);
}

bool panel_finish_stream(void) {
    if (!m_stream_open) return false;
    m_stream_open = false;
    command(CMD_DATA_STOP);

    uint8_t refresh_mode = 0x00;
    write_command(CMD_DISPLAY_REFRESH, &refresh_mode, 1);
    if (!wait_busy_high(50000)) {
        panel_abort_stream();
        return false;
    }

    uint8_t power_off_mode = 0x00;
    write_command(CMD_POWER_OFF, &power_off_mode, 1);
    if (!wait_busy_high(2000)) {
        panel_abort_stream();
        return false;
    }
    uint8_t sleep_key = 0xA5;
    write_command(CMD_DEEP_SLEEP, &sleep_key, 1);
    nrf_delay_ms(100);
    panel_abort_stream();
    return true;
}

void panel_abort_stream(void) {
    m_stream_open = false;
    NRF_SPI0->ENABLE = SPI_ENABLE_ENABLE_Disabled << SPI_ENABLE_ENABLE_Pos;
    if (m_panel_active) {
        nrf_gpio_pin_clear(EPD_POWER);
        nrf_gpio_pin_clear(EPD_RST);
        nrf_gpio_pin_clear(EPD_BS);
        nrf_gpio_pin_clear(EPD_CS);
        nrf_gpio_pin_clear(EPD_DC);
        nrf_gpio_pin_clear(EPD_CLK);
        nrf_gpio_pin_clear(EPD_MOSI);
        nrf_gpio_pin_clear(EPD_HLT);
    }
    m_panel_active = false;
}
