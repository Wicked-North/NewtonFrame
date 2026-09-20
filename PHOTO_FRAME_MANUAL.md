# SoluM 6-inch E-Paper Photo Frame Manual

This manual covers the working SoluM/Newton M3 photo-frame setup using:

- A SoluM `EL060H6W4A` 648×480 black/white/yellow/red display
- Its onboard nRF52811 running the signed Photo Viewer firmware
- An ESP32 connected over SWD
- The ESP32-hosted image converter and uploader

## 1. What each board does

### ESP32

The ESP32:

- Connects to Wi-Fi and hosts the upload webpage
- Converts selected photos in the browser
- Programs image data into the nRF52811 over SWD

The ESP32 is required when changing the image with the current setup. It may be disconnected after a successful update.

### Onboard nRF52811

The nRF52811:

- Stores the converted 648×480 image
- Drives and refreshes the e-paper panel
- Enters a low-power state after completing the refresh

The e-paper retains its image without power.

## 2. Safety rules

- Use **3.3 V only**. Never connect the SoluM board to 5 V or VIN.
- Always share ground between the ESP32 and SoluM board.
- Do not move or solder wires while USB or target power is connected.
- Do not power the SoluM board from two sources simultaneously.
- Do not unplug either board while an image is being erased, uploaded, verified, or refreshed.
- Do not use **Erase nRF**, **Erase All**, lock-bit, or glitcher controls for normal photo uploads.
- Never mass-erase the nRF52811. Its UICR calibration and board metadata must be preserved.

## 3. Complete connection and pin reference

![SoluM Newton M3 nRF52811 functional schematic](docs/nrf52811-functional-schematic.svg)

The diagram is a functional reconstruction from verified firmware pin assignments and measured chip configuration. It is not SoluM's unpublished PCB artwork.

### Required wiring for the current photo uploader

These are the only signal connections required between the ESP32 and the assembled SoluM tag for web uploads:

| SoluM/nRF52811 pad | ESP32 pin | Direction | Purpose |
|---|---:|---|---|
| `SWDIO` | `GPIO19` | Bidirectional | SWD data |
| `SWDCLK` | `GPIO21` | ESP32 → tag | SWD clock |
| `GND` | `GND` | — | Common electrical reference |
| `VCC` | `3.3V` | ESP32 → tag | Fixed 3.3 V target power |

The e-paper panel remains connected to and controlled by the onboard nRF52811. Do **not** add the direct-display wiring in the next table for normal photo-uploader use.

### ESP32 pins used by the SWD firmware

| ESP32 pin | Firmware name | Use | Required for photos? |
|---:|---|---|---|
| `GPIO2` | `LED` | ESP32 status LED | No external wire |
| `GPIO5` | `GLITCHER` | Optional APPROTECT glitch MOSFET gate | No |
| `GPIO19` | `swd_data_pin` | nRF52811 `SWDIO` | Yes |
| `GPIO21` | `swd_clock_pin` | nRF52811 `SWDCLK` | Yes |
| `GPIO22` | `NRF_POWER` | Optional switched target-power output | Only if using switched power |
| `GPIO34` | `OSCI_PIN` | Optional analog oscilloscope input | No |
| `3.3V` | — | Fixed target supply | Yes for the documented fixed-power wiring |
| `GND` | — | Common ground | Yes |
| `5V` / `VIN` | — | USB/raw supply | **Never connect to the tag** |

There are two possible target-power arrangements:

1. **Fixed power, used in this manual:** SoluM `VCC` → ESP32 `3.3V`.
2. **Software-switched power:** SoluM `VCC` → ESP32 `GPIO22`, as used by the original glitcher design.

Use exactly **one** arrangement. Never connect SoluM `VCC` to both `3.3V` and `GPIO22`, and never combine either arrangement with another powered tag connection.

### Optional glitcher wiring — not needed for photo uploads

The tag is already unlocked. Leave these disconnected during normal operation.

| Connection | Purpose |
|---|---|
| ESP32 `GPIO5` → N-channel MOSFET gate/PWM input | DEC1 glitch pulse |
| MOSFET drain/output → nRF52811 `DEC1` | Glitch target |
| MOSFET source/ground → common `GND` | Return path |
| ESP32 `GPIO34` → measurement point | Optional oscilloscope sampling |

Do not enable the glitcher against the working tag firmware.

### Optional direct-display test wiring — not used by the photo uploader

This legacy/experimental arrangement lets separate ESP32 test firmware drive exposed display signals directly. It is **not** part of the current SWD Photo Viewer path.

| SoluM display pad | ESP32 pin | Function |
|---|---:|---|
| `VCC` | `3.3V` | Display power |
| `GND` | `GND` | Common ground |
| `TXD` | `GPIO14` | SPI MOSI/data |
| `RXD` | `GPIO13` | SPI clock |
| `D/L` | `GPIO27` | Data/command |
| `RST` | `GPIO26` | Display reset |
| `TEST` | `GPIO15` | Chip select |
| `BUSY` | `GPIO4` | Display busy/status |

Do not run direct-display firmware while the onboard nRF52811 is also driving the panel signals.

### Onboard nRF52811 peripheral map

These are nRF52811 `P0.xx` GPIO numbers inside the SoluM tag—not ESP32 GPIO numbers. They are listed for firmware development and diagnosis; no external wiring is required for ordinary use.

| nRF pin | Connected function |
|---:|---|
| `P0.00` | No function assigned by the current Newton M3 HAL |
| `P0.01` | No function assigned by the current Newton M3 HAL |
| `P0.02` | E-paper bus select (`EPD_BS`) |
| `P0.03` | E-paper busy |
| `P0.04` | E-paper reset |
| `P0.05` | E-paper data/command |
| `P0.06` | E-paper chip select |
| `P0.07` | E-paper power control |
| `P0.08` | External NFC I²C SDA |
| `P0.09` | External NFC I²C SCL |
| `P0.10` | External NFC power control |
| `P0.11` | External NFC IRQ/wake |
| `P0.12` | External SPI flash chip select |
| `P0.13` | External SPI flash MISO |
| `P0.14` | External SPI flash clock |
| `P0.15` | External SPI flash MOSI |
| `P0.16` | Red LED definition |
| `P0.17` | Green LED definition |
| `P0.18` | Blue LED definition |
| `P0.19` | E-paper SPI clock |
| `P0.20` | E-paper SPI MOSI |
| `P0.21` | Debug reset pad (`DBG_RST`) |
| `P0.22` | Debug/download pad (`DBG_DL`) |
| `P0.23` | E-paper hold/external EPD EEPROM CS; also peghook-button definition on other variants |
| `P0.24` | E-paper/external EEPROM MISO (`EPD_VPP`) |
| `P0.25` | Debug UART TX |
| `P0.26` | Debug UART RX |
| `P0.27` | Debug test pad |
| `P0.28` | Button 1 definition; physical QFN package pin 40 |
| `P0.29` | Button 2 definition; physical QFN package pin 41 |
| `P0.30` | No function assigned by the current Newton M3 HAL |
| `P0.31` | Button 3 definition |

This is primarily the firmware's logical GPIO map, not the chip package pin-number diagram. The OEPL Newton M3 HAL defines `BUTTON1` as P0.28 and `BUTTON2` as P0.29, matching the visually traced switch area; these are physical QFN pins 40 and 41 respectively, not physical pins 28 and 29. Which one reaches the single fitted switch still requires a continuity test. Dedicated power, ground, RF, crystal, reset, SWDIO, and SWDCLK package pins are otherwise not numbered in this table. LEDs and buttons are generic Newton M3 firmware definitions and may not all be physically populated on this exact 6-inch board revision.

### Experimental ESP32-free BLE path

The proposed BLE transport, packet format, CRC handling, and direct-to-panel streaming design are documented in [`docs/BLE_PHOTO_PROTOCOL.md`](docs/BLE_PHOTO_PROTOCOL.md). This path is not yet installed on the tag; the ESP32/SWD procedure in this manual remains the tested recovery and upload method.

## 4. Starting the system

1. Inspect the wiring for loose connections or solder bridges.
2. Connect the ESP32 to USB power.
3. Allow approximately 10–20 seconds for startup and Wi-Fi connection.
4. Open `http://swd.local/` in a browser.
5. If that hostname does not resolve, use the ESP32's router-assigned IP address. The previously observed address was `http://192.168.1.28/`, but DHCP may assign a different address later.

No PC application, PlatformIO session, or serial monitor needs to remain open for ordinary use.

## 5. Backup Wi-Fi hotspot

If the ESP32 cannot join its configured Wi-Fi network within 20 seconds, it creates its own access point:

- Network name: `SWD-Photo`
- Password: `epaper648`
- Web address: `http://192.168.4.1/`

To use it:

1. Open Wi-Fi settings on the phone or computer.
2. Join `SWD-Photo`.
3. Accept or ignore the expected **No Internet Connection** warning.
4. Open `http://192.168.4.1/`.

The phone communicates directly with the ESP32; internet access is unnecessary.

## 6. Uploading a photo

1. Open the webpage.
2. Under **E-paper Photo**, choose an image.
3. Select **Crop to fill** or **Fit entire image**.
4. Adjust rotation and horizontal/vertical mirroring if needed.
5. Select the display colors to use:
   - Black
   - White
   - Yellow
   - Red
6. Enable or disable **Dither**.
7. Press **Convert image**.
8. Inspect the 648×480 preview.
9. Press **Flash image to tag** and confirm.
10. Keep both boards powered and connected until the page reports:

   `Image verified; tag reset to refresh the display`

The panel refresh can continue after the upload response. Do not disturb power until the visible refresh has finished.

### Color selection

Only checked colors participate in quantization and dithering. The uploader prevents all four colors from being disabled simultaneously.

The preview uses a warm off-white to resemble the physical e-paper substrate. The packed panel data still uses the panel's correct white code.

### Dithering

- Enabled: creates more apparent tones by mixing selected colors; photographs usually look better.
- Disabled: produces cleaner solid areas; useful for logos, text, and flat artwork.

## 7. Downloading without flashing

After conversion, press **Download panel image** to save the packed 77,760-byte binary file. This does not change the tag.

The file format is:

- Resolution: 648×480
- Pixel depth: 2 bits per pixel
- Four pixels per byte, most-significant pair first
- Codes: black `00`, white `01`, yellow `10`, red `11`

## 8. Disconnecting and reconnecting

### Disconnecting

After the upload and panel refresh are complete:

1. Close the webpage if desired.
2. Unplug USB power.

The displayed image remains visible without power.

### Reconnecting later

1. Confirm the SWD and power wires are secure.
2. Reconnect ESP32 USB power.
3. Wait 10–20 seconds.
4. Open `http://swd.local/`, the router-assigned IP, or the fallback hotspot as described above.

All firmware and webpage files are stored in nonvolatile flash and survive power removal.

## 9. Connection check

The webpage's **Init SWD** button may be used for diagnosis. A healthy connection reports:

`0x2BA01477`

The nRF should also report as unlocked. Normal photo uploads perform their own guarded connection check, so manually pressing **Init SWD** is not required each time.

## 10. Troubleshooting

### Webpage does not open

1. Wait 20 seconds after applying power.
2. Try `http://swd.local/`.
3. Try the current router-assigned IP address.
4. Look for the `SWD-Photo` Wi-Fi network and use `http://192.168.4.1/`.
5. Confirm the ESP32 is powered and its USB cable supports power reliably.

### `ERROR: nRF52811 not connected`

This usually indicates target power or wiring—not erased firmware.

1. Disconnect USB power.
2. Inspect and reconnect:
   - SWDIO → GPIO19
   - SWDCLK → GPIO21
   - GND → GND
   - Target VCC → 3.3 V
3. Check for crossed wires, cold solder joints, and bridges.
4. Reconnect USB power.
5. Press **Init SWD** and confirm `0x2BA01477`.

Random or changing SWD IDs generally indicate a floating SWD wire. `0x00000000` generally indicates an unpowered or disconnected target.

### `ERROR: Photo Viewer firmware is not installed`

The safety signature at `0x1CFF0` was not detected. The server intentionally refuses to erase the image area. Do not bypass this guard; reinstall and verify the signed Photo Viewer firmware using the documented recovery process.

### Upload interrupted

The validity marker is committed only after all 77,760 bytes have been written and verified. An interrupted upload should not be treated as valid.

Reconnect, reload the webpage, reconvert the image, and upload it again. Do not use mass erase.

### Image orientation is wrong

Use the rotation and mirror controls, convert again, and inspect the preview before flashing.

### Colors look different from a phone screen

This is expected. The display has four physical pigments rather than RGB light-emitting pixels. Lighting, panel temperature, and the substrate affect appearance.

## 11. Recovery information

Important nRF52811 memory regions:

| Region | Address | Purpose |
|---|---:|---|
| Existing bootloader | `0x0000–0x3FFF` | Preserved recovery/boot code |
| Photo Viewer application | Starts at `0x4000` | Panel-driving firmware |
| Viewer signature | `0x1CFF0–0x1CFFF` | Upload safety gate |
| Image slot | `0x1D000–0x2FFBF` | 77,760-byte packed image |
| Validity marker | `0x2FFFC` | Committed-image marker |

Expected values:

- Viewer signature: `EPHOTO648V1` plus five binary signature bytes
- Image marker: `0x31475045` (`EPG1` in little-endian storage)
- Critical preserved UICR word at `0x10001088`: `0x58031700`

Recovery must preserve the bootloader and UICR. The ESP32/SWD connection remains the recovery and firmware-programming path.

## 12. Current wireless capabilities

- The ESP32 provides Wi-Fi access to the webpage.
- The nRF52811 hardware supports Bluetooth Low Energy, but the installed Photo Viewer does not currently advertise or accept BLE uploads.
- The board is factory-marked as having an external NFC device, but the current Photo Viewer does not use it for image transfer.
- With the current firmware, the ESP32 is needed to change images but is not needed to keep an image displayed.
