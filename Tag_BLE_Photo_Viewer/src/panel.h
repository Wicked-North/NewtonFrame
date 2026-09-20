#pragma once

#include <stdbool.h>
#include <stdint.h>

bool panel_begin_stream(void);
void panel_write_stream(uint8_t const *data, uint16_t length);
bool panel_finish_stream(void);
void panel_abort_stream(void);
