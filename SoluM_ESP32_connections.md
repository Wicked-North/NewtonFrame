# SoluM PCB to ESP32 Connections

For operating and safety instructions, see [`PHOTO_FRAME_MANUAL.md`](PHOTO_FRAME_MANUAL.md).

## Current SWD photo-uploader connections

| SoluM pad | ESP32 connection | Function |
|---|---:|---|
| `SWDIO` | `GPIO19` | nRF52811 SWD data |
| `SWDCLK` | `GPIO21` | nRF52811 SWD clock |
| `GND` | `GND` | Common ground |
| `VCC` | `3.3V` | Fixed target power |

This is the normal connection set. The onboard nRF52811 drives the panel; the direct display connections below are not required.

### Alternative switched target power

The ESP32 firmware defines `GPIO22` as `NRF_POWER`. SoluM `VCC` may be connected to `GPIO22` instead of `3.3V` when software-switched target power is specifically required.

Never connect SoluM `VCC` to both `3.3V` and `GPIO22`, and never apply 5 V.

## Optional direct display connections

These are only for the separate direct-display test firmware, not the normal SWD Photo Viewer.

| SoluM pad | ESP32 connection | Function |
|---|---:|---|
| `VCC` | `3.3V` | Power |
| `GND` | `GND` | Ground |
| `TXD` | `GPIO14` | SPI data / MOSI |
| `RXD` | `GPIO13` | SPI clock / CLK |
| `D/L` | `GPIO27` | Data/Command / DC |
| `RST` | `GPIO26` | Hardware reset |
| `TEST` | `GPIO15` | Chip Select / CS |
| `BUSY` | `GPIO4` | Display busy/status |

## Optional glitcher and diagnostic pins

| ESP32 pin | Connection/function |
|---:|---|
| `GPIO2` | Onboard status LED; no external wire |
| `GPIO5` | Optional glitcher MOSFET gate |
| `GPIO22` | Optional switched nRF target power |
| `GPIO34` | Optional oscilloscope input |

## Notes

- Use only `3.3V`; do not connect the SoluM board to ESP32 `5V` or `VIN`.
- Do not power the SoluM board from its USB-C connector at the same time as the ESP32 `3.3V` connection.
- Keep `TEST`/CS HIGH while the ESP32 is booting.
- Keep the optional direct-display wires disconnected for normal SWD Photo Viewer operation.
- Do not run direct-display firmware while the onboard nRF52811 is driving the panel.
- The glitcher wiring and controls are not needed because the current nRF52811 is already unlocked.
