# RoggenCore Manager guide

## What it manages

RoggenCore Manager is a native Windows application for the ESP32 SWD bridge and nRF52811 display controller. It discovers the bridge over USB serial or Wi-Fi, identifies the target and supported display profile, catalogs timestamped backups, verifies firmware packages, performs safe upgrades, and provides a guarded factory recovery workflow.

## Discovery

New bridge firmware responds to `ROGGENCORE?` at 115200 baud with its current HTTP address. The manager monitors serial-port changes every two seconds. It also checks the saved address, `swd.local`, `192.168.4.1`, and local IPv4 subnets. If the bridge reports `192.168.4.1`, join its `SWD-Photo` fallback access point before connecting.

The Device tab can save Wi-Fi credentials directly over the local USB cable. They are stored in ESP32 NVS, never embedded in a firmware image, and never written to Manager logs. The bridge restarts after saving them. Its startup identity probe immediately releases an unlocked nRF CPU, so attaching or restarting the bridge does not leave the display controller halted.

## Backups and state

Managed data is stored under `%APPDATA%\RoggenCore`:

- `backups\<device-and-UTC-time>\flash.bin`
- `backups\<device-and-UTC-time>\uicr.bin`
- `backups\<device-and-UTC-time>\backup.json`
- `firmware-state.json`

Every backup records CRC-32 and SHA-256. The state file records the most recent backup and last-known-good image per physical device. Its format is documented by `docs/firmware-state.schema.json`. Existing 192 KiB `.bin` files can be imported; the known `D:\NewtonFrame-Backups` folder is imported automatically when present.

Select a cataloged backup and choose **Restore selected backup** to roll back. Manager verifies its CRC-32, SHA-256, and target flash size, preserves UICR, and compares the complete post-write readback before releasing the CPU.

## Normal upgrade

1. Connect ESP32 SWD with tag batteries disconnected.
2. Connect and inspect the target.
3. Run post-unlock tests.
4. Select `firmware/RoggenCore/<version>/upgrade-manifest.json`.
5. Choose **Backup, install & verify**.

The manager creates a backup, erases only manifest-approved pages, uploads the HEX, reads all flash, verifies every byte, and only then resets and releases the nRF52811.

Firmware bytes are sent as bounded 256-byte binary writes rather than one long multipart transfer. The ESP32 does not probe or release the SWD target during its own startup; each Manager operation explicitly acquires SWD and read-only operations resume the CPU afterward. A reset or failed upload therefore cannot start a partially written application.

## Factory recovery

Factory recovery clears APPROTECT and all target flash/UICR. It is only allowed with a recovery-capable package containing MBR/S112, RoggenCore, and profile-specific UICR defaults. The operation requires a target-specific typed phrase and a second confirmation. If the target is already readable, a backup is created first. After installation, the manager runs hardware diagnostics and creates the new last-known-good backup.

## Retained startup picture

The e-paper image is physical and remains visible without power. Firmware installation therefore cannot silently clear the old factory/Newton picture. Open RoggenCore Studio, choose **RoggenCore welcome**, convert it, and send it through the normal CRC-verified BLE path; or send your first photograph directly.
