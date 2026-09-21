# RoggenCore nRF52811 Firmware

Experimental ESP32-free receiver for the SoluM/Newton M3 648 × 480 BWRY tag.

## Current status

- Builds against Nordic nRF5 SDK 17.1.0 and S112 7.2.0.
- Version 1.1.1 advertises as `RoggenCore`; Studio also accepts legacy `EPHOTO-648` builds.
- Implements the Control, Data, and Status characteristics from [`../docs/BLE_PHOTO_PROTOCOL.md`](../docs/BLE_PHOTO_PROTOCOL.md).
- Validates image dimensions, format, offsets, length, and CRC-32.
- Streams accepted image bytes directly into UC8159 display RAM.
- Refreshes only after a valid COMMIT and matching CRC.
- Advertises for 60 seconds, then enters System OFF.
- Wakes and advertises again when either active-low button candidate P0.28 or P0.29 is pressed.
- Disconnects and enters System OFF after a successful display refresh.
- Hardware testing has verified S112 startup, advertising, GATT connection, a complete 77,760-byte image transfer, CRC validation, panel refresh, intentional disconnect, post-transfer System OFF, and physical-button wake into a fresh advertising window.
- Hardware-verified, battery-friendly RGB indicators use short pulses: blue every 3 seconds while advertising, two green connection flashes, yellow every second during transfer, green every 2 seconds during refresh, and a 300 ms green completion pulse. All channels are off in System OFF.
- Reports a one-shot supply-voltage measurement in millivolts through optional characteristic `...0004...`; displayed percentage is an estimate because primary lithium discharge is nonlinear.
- Disconnects and enters System OFF after 120 seconds without a control or image-data write. Refresh is exempt so a slow panel cycle is never interrupted.

The panel is bistable: flashing firmware does not erase the picture already visible on it. RoggenCore Studio can generate and upload a RoggenCore welcome frame to replace retained factory artwork.

## Memory layout

| Region | Address |
|---|---|
| S112 MBR and SoftDevice | `0x00000000–0x00018FFF` |
| BLE application | `0x00019000–0x0002FFFF` |
| Application RAM | `0x20001AE0–0x20005FFF` |

The build uses the calibrated internal RC low-frequency clock because the tag does not provide the development kit's external 32.768 kHz crystal.

The image is not stored in internal flash. The display is bistable and retains the last successfully refreshed image without power.

## Build dependencies

- Nordic nRF5 SDK 17.1.0 (`nRF5_SDK_17.1.0_ddde560`)
- Nordic S112 7.2.0 included with that SDK
- ARM GNU toolchain 7.2.1
- CMake and Ninja

Configure CMake with `NRF5_SDK_ROOT` pointing to the extracted SDK and `ARM_GCC_ROOT` pointing to the directory containing `bin/arm-none-eabi-gcc.exe`.

The generated application files are placed in `build/`. Merge `epaper_ble_receiver.hex` with the SDK's `components/softdevice/s112/hex/s112_nrf52_7.2.0_softdevice.hex` before programming a blank/erased target.

## Safety

Installing the combined image replaces the current bootloader and Photo Viewer application. Do not flash only the application over the current layout: its vector table begins at `0x19000` and requires S112 below it. Preserve the current flash/UICR backup and keep ESP32 SWD connected as the recovery path during bring-up.

The first hardware test should verify advertising and characteristic access before sending START. Sending a valid START powers the panel and begins a display-RAM stream; sending a valid complete frame plus COMMIT refreshes the display.