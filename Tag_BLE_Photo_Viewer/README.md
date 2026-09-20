# nRF52811 BLE Photo Viewer

Experimental ESP32-free receiver for the SoluM/Newton M3 648 × 480 BWRY tag.

## Current status

- Builds against Nordic nRF5 SDK 17.1.0 and S112 7.2.0.
- Advertises as `EPHOTO-648`.
- Implements the Control, Data, and Status characteristics from [`../docs/BLE_PHOTO_PROTOCOL.md`](../docs/BLE_PHOTO_PROTOCOL.md).
- Validates image dimensions, format, offsets, length, and CRC-32.
- Streams accepted image bytes directly into UC8159 display RAM.
- Refreshes only after a valid COMMIT and matching CRC.
- Has not yet been flashed or tested on the physical tag.

## Memory layout

| Region | Address |
|---|---|
| S112 MBR and SoftDevice | `0x00000000–0x00018FFF` |
| BLE application | `0x00019000–0x0002FFFF` |
| Application RAM | `0x20001A40–0x20005FFF` |

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