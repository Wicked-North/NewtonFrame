# NewtonFrame Studio

A dependency-free static web app for preparing and transferring 648×480 BWRY images to the experimental NewtonFrame BLE firmware.

## Features

- Local image decoding—photos are never uploaded to a server
- Fill or contain framing, drag positioning, zoom, rotation, and mirroring
- Selectable black, paper, yellow, and red pigments
- Optional Floyd–Steinberg error-diffusion dithering
- Exact 2-bpp panel packing and IEEE CRC-32
- BLE transfer with offset acknowledgements, cancellation, CRC validation, and progress
- Packed `.bin` download when Bluetooth is unavailable
- Installable/offline PWA shell

## Browser support

Web Bluetooth requires a secure context (`https://` or localhost) and a compatible browser. Chrome and Edge support it on major desktop/Android platforms. Safari does not expose Web Bluetooth; on iPhone, use a compatible browser such as Bluefy.

The BLE path requires the experimental firmware in `Tag_BLE_Photo_Viewer/`. Until that firmware is physically validated, keep ESP32 SWD connected as the recovery path.

## Local preview

Serve this directory over localhost rather than opening `index.html` as a `file://` URL. Any static HTTP server works. Bluetooth access is permitted on localhost.

## Deployment

The repository's Pages workflow publishes this directory whenever `main` changes. In the GitHub repository settings, select **GitHub Actions** as the Pages source if Pages has not already been enabled.
