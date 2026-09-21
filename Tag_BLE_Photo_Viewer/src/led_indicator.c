#include "led_indicator.h"

#include <stdint.h>

#include "nrf.h"
#include "nrf_gpio.h"

#define LED_RED 16u
#define LED_GREEN 17u
#define LED_BLUE 18u
#define RTC_TICKS_50_MS 1638u

typedef enum {
    LED_MODE_OFF,
    LED_MODE_ADVERTISING,
    LED_MODE_CONNECTED,
    LED_MODE_TRANSFERRING,
    LED_MODE_REFRESHING,
    LED_MODE_COMPLETE
} led_mode_t;

static volatile led_mode_t m_mode = LED_MODE_OFF;
static volatile uint16_t m_tick;
static volatile bool m_complete_done;

static void leds_write(bool red, bool green, bool blue) {
    red ? nrf_gpio_pin_clear(LED_RED) : nrf_gpio_pin_set(LED_RED);
    green ? nrf_gpio_pin_clear(LED_GREEN) : nrf_gpio_pin_set(LED_GREEN);
    blue ? nrf_gpio_pin_clear(LED_BLUE) : nrf_gpio_pin_set(LED_BLUE);
}

static void mode_set(led_mode_t mode) {
    NRF_RTC1->TASKS_STOP = 1;
    NRF_RTC1->TASKS_CLEAR = 1;
    NRF_RTC1->EVENTS_COMPARE[0] = 0;
    m_tick = 0;
    m_complete_done = false;
    m_mode = mode;
    leds_write(false, false, false);
    if (mode != LED_MODE_OFF) NRF_RTC1->TASKS_START = 1;
}

void led_indicator_init(void) {
    nrf_gpio_cfg_output(LED_RED);
    nrf_gpio_cfg_output(LED_GREEN);
    nrf_gpio_cfg_output(LED_BLUE);
    leds_write(false, false, false);

    NRF_RTC1->TASKS_STOP = 1;
    NRF_RTC1->TASKS_CLEAR = 1;
    NRF_RTC1->PRESCALER = 0;
    NRF_RTC1->CC[0] = RTC_TICKS_50_MS;
    NRF_RTC1->EVENTS_COMPARE[0] = 0;
    NRF_RTC1->INTENSET = RTC_INTENSET_COMPARE0_Msk;
    NVIC_SetPriority(RTC1_IRQn, 6);
    NVIC_ClearPendingIRQ(RTC1_IRQn);
    NVIC_EnableIRQ(RTC1_IRQn);
}

void led_indicator_advertising(void) {
    mode_set(LED_MODE_ADVERTISING);
}

void led_indicator_connected(void) {
    mode_set(LED_MODE_CONNECTED);
}

void led_indicator_transferring(void) {
    mode_set(LED_MODE_TRANSFERRING);
}

void led_indicator_refreshing(void) {
    mode_set(LED_MODE_REFRESHING);
}

void led_indicator_complete(void) {
    mode_set(LED_MODE_COMPLETE);
}

bool led_indicator_complete_done(void) {
    return m_complete_done;
}

void led_indicator_off(void) {
    mode_set(LED_MODE_OFF);
}

void RTC1_IRQHandler(void) {
    if (NRF_RTC1->EVENTS_COMPARE[0] == 0) return;
    NRF_RTC1->EVENTS_COMPARE[0] = 0;
    NRF_RTC1->TASKS_CLEAR = 1;

    uint16_t tick = m_tick++;
    switch (m_mode) {
        case LED_MODE_ADVERTISING:
            leds_write(false, false, tick % 60u == 0u);
            break;

        case LED_MODE_CONNECTED:
            leds_write(false, tick < 2u || (tick >= 4u && tick < 6u), false);
            if (tick >= 6u) mode_set(LED_MODE_OFF);
            break;

        case LED_MODE_TRANSFERRING:
            leds_write(tick % 20u == 0u, tick % 20u == 0u, false);
            break;

        case LED_MODE_REFRESHING:
            leds_write(false, tick % 40u == 0u, false);
            break;

        case LED_MODE_COMPLETE:
            leds_write(false, tick < 6u, false);
            if (tick >= 6u) {
                NRF_RTC1->TASKS_STOP = 1;
                m_mode = LED_MODE_OFF;
                leds_write(false, false, false);
                m_complete_done = true;
            }
            break;

        default:
            leds_write(false, false, false);
            break;
    }
}
