#pragma once

#include <stdbool.h>

void led_indicator_init(void);
void led_indicator_advertising(void);
void led_indicator_connected(void);
void led_indicator_transferring(void);
void led_indicator_refreshing(void);
void led_indicator_complete(void);
bool led_indicator_complete_done(void);
void led_indicator_off(void);
