# BLE Photo Transfer Protocol

Status: hardware-verified implementation for the ESP32-free nRF52811 photo viewer. Advertising, GATT connection, full image transfer, CRC validation, panel refresh, intentional disconnect, post-transfer System OFF, and physical-button wake have been verified on the tag.

## Responsibility split

### Phone

The phone performs all expensive image processing:

1. Decode JPEG/PNG/HEIC through the browser or native image APIs.
2. Crop/contain, rotate, and mirror to 648 × 480.
3. Quantize to the selected black/white/yellow/red palette.
4. Optionally apply Floyd–Steinberg error-diffusion dithering.
5. Pack four 2-bit pixels into each byte, most-significant pixel first.
6. Calculate CRC-32 and send exactly 77,760 packed bytes over BLE.

Color codes remain:

| Code | Color |
|---:|---|
| `00` | Black |
| `01` | White |
| `10` | Yellow |
| `11` | Red |

The nRF52811 does not dither. Its 24 KB RAM is deliberately reserved for BLE and transfer buffers; a full RGB/error-diffusion workspace would be much larger.

## Radio implementation notes

Nordic specifies Bluetooth LE modes at 2 Mbps, 1 Mbps, 500 kbps, and 125 kbps for the nRF52811, with approximately 4.6 mA peak current in both 0 dBm TX and RX. The firmware lets S112 negotiate the PHY automatically; it does not access `NRF_RADIO` directly because the SoftDevice owns the radio peripheral, packet DMA, timing, and radio interrupts while enabled. The conservative first build uses ATT MTU 23 and 16 image bytes per protocol packet, so it remains valid even if a phone does not negotiate a larger MTU or 2 Mbps PHY.

### Tag

The nRF52811:

1. Wakes and advertises as `EPHOTO-xxxx`.
2. Accepts a transfer header containing format, length, and CRC-32.
3. Initializes the UC8159 but does not refresh it.
4. Receives ordered chunks and streams their pixel bytes into UC8159 display RAM.
5. Maintains byte count and incremental CRC-32.
6. Refreshes only when the byte count is 77,760 and CRC matches.
7. Powers down the panel and enters System OFF.

The e-paper glass retains the completed picture without power, so no internal or external framebuffer is required. If BLE disconnects, a chunk is missing, or CRC fails, the tag does not issue the display-refresh command; the previously visible picture remains.

## BLE service

Custom base UUID: `7b1e0000-6e8a-4f4b-a2b7-2c648480e001`

| Characteristic | UUID | Properties | Purpose |
|---|---|---|---|
| Control | `7b1e0001-6e8a-4f4b-a2b7-2c648480e001` | Write | Start, commit, abort |
| Data | `7b1e0002-6e8a-4f4b-a2b7-2c648480e001` | Write Without Response, Write | Ordered image chunks |
| Status | `7b1e0003-6e8a-4f4b-a2b7-2c648480e001` | Read, Notify | State, accepted offset, errors |

All multibyte integers are little-endian.

## Control messages

### START (`0x01`, 16 bytes)

| Offset | Size | Field | Required value |
|---:|---:|---|---|
| 0 | 1 | Opcode | `0x01` |
| 1 | 1 | Protocol version | `0x01` |
| 2 | 2 | Width | `648` |
| 4 | 2 | Height | `480` |
| 6 | 1 | Pixel format | `0x01` (BWRY 2-bpp) |
| 7 | 1 | Flags | Reserved, zero |
| 8 | 4 | Payload length | `77760` |
| 12 | 4 | Expected CRC-32 | IEEE CRC-32 of packed frame |

The tag responds through Status with `READY` and offset zero after panel initialization succeeds.

### COMMIT (`0x02`, 1 byte)

The tag accepts COMMIT only after exactly 77,760 bytes. It compares the running CRC with the START value, sends `VERIFYING`, refreshes on success, then reports `COMPLETE` before disconnecting/sleeping.

### ABORT (`0x03`, 1 byte)

Stops the transfer, powers down the panel without refreshing, and returns to advertising.

## Data messages

Each Data write contains:

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | Absolute byte offset |
| 4 | variable | Packed image bytes |

The offset must equal the tag's next expected offset. Duplicate or skipped offsets are rejected. Payload size is selected from the negotiated ATT MTU:

- ATT MTU 23 fallback: 16 image bytes per write.
- ATT MTU 247: up to 240 image bytes per write.

The sender uses Write Without Response for image data, pauses every 256 image bytes for a Status acknowledgement, and resumes from the accepted offset. This keeps the radio pipeline moving without sacrificing explicit flow control on phones/browsers whose transmit queues differ.

## Status message

Fixed 12-byte notification:

| Offset | Size | Field |
|---:|---:|---|
| 0 | 1 | Protocol version (`1`) |
| 1 | 1 | State |
| 2 | 2 | Error code |
| 4 | 4 | Next expected byte offset |
| 8 | 4 | Running/final CRC-32 |

States:

| Value | State |
|---:|---|
| `0` | Idle/advertising |
| `1` | Preparing panel |
| `2` | Ready/receiving |
| `3` | Verifying |
| `4` | Refreshing |
| `5` | Complete |
| `255` | Error |

Initial error codes include invalid header, wrong offset, too much data, CRC mismatch, panel timeout, and transfer timeout.

## iPhone transport

Mobile Safari does not expose Web Bluetooth. The practical choices are:

1. Open the HTTPS-hosted converter in a Web Bluetooth-capable iOS browser such as Bluefy, if its current version supports the required GATT operations.
2. Build a small native iOS app using CoreBluetooth and reuse the same conversion/protocol rules.
3. Continue using the ESP32 HTTP/SWD uploader as a recovery and compatibility path.

The existing JavaScript converter can be reused. Only its transport changes from HTTP POST to BLE GATT writes.

## Power and wake behavior

The BLE firmware configures both P0.28 and P0.29 as active-low sense inputs before System OFF, until continuity testing identifies the populated switch. On power-up or button wake it advertises for 60 seconds, then returns to System OFF. A connected transfer keeps the system awake. After a successful refresh, the tag reports COMPLETE, disconnects, and returns to System OFF. The display is powered only after START and is shut down on completion, error, disconnect, or timeout.

## Safe migration

The SDK-matched S112 7.2.0 image has populated data through `0x00018FEB`; the BLE application starts at `0x00019000`. Installing it replaces the current bootloader and Photo Viewer application, so migration must be performed and verified over SWD as one combined SoftDevice-plus-application image. UICR must be backed up and preserved. The current ESP32 SWD setup remains the recovery path.
