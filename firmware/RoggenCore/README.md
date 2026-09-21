# RoggenCore firmware packages

Each version directory contains two independently integrity-checked packages:

- `upgrade-manifest.json` updates only the RoggenCore application pages on an already working S112 installation.
- `recovery-manifest.json` is for intentional factory APPROTECT recovery and includes MBR, S112, RoggenCore, and the verified `EL060H6W4A` board UICR word.

RoggenCore Manager validates the manifest, Intel HEX record checksums, CRC-32, SHA-256, target part, flash size, and allowed address ranges before writing. It then reads all 192 KiB back and compares every byte before releasing the CPU.

A recovery erase is destructive. Never use a recovery manifest for another panel profile, and never connect ESP32 VCC while batteries are fitted.
