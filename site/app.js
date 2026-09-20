(() => {
  'use strict';

  const WIDTH = 648;
  const HEIGHT = 480;
  const FRAME_BYTES = WIDTH * HEIGHT / 4;
  const SERVICE_UUID = '7b1e0000-6e8a-4f4b-a2b7-2c648480e001';
  const CONTROL_UUID = '7b1e0001-6e8a-4f4b-a2b7-2c648480e001';
  const DATA_UUID = '7b1e0002-6e8a-4f4b-a2b7-2c648480e001';
  const STATUS_UUID = '7b1e0003-6e8a-4f4b-a2b7-2c648480e001';
  const CHUNK_BYTES = 176;
  const ACK_INTERVAL = 2816;

  const COLORS = [
    { code: 0, name: 'Black', rgb: [23, 23, 23] },
    { code: 1, name: 'Paper', rgb: [245, 240, 210] },
    { code: 2, name: 'Yellow', rgb: [228, 189, 45] },
    { code: 3, name: 'Red', rgb: [181, 45, 54] }
  ];

  const STATE_NAMES = {
    0: 'Idle',
    1: 'Preparing panel',
    2: 'Receiving',
    3: 'Verifying',
    4: 'Refreshing display',
    5: 'Complete',
    255: 'Error'
  };

  const ERROR_NAMES = {
    1: 'Invalid transfer header',
    2: 'Unexpected data offset',
    3: 'Too much image data',
    4: 'CRC mismatch',
    5: 'Tag is not ready',
    6: 'Display controller timed out'
  };

  const elements = Object.fromEntries([
    'browser-badge', 'compatibility', 'install-button', 'image-input', 'drop-zone',
    'change-image', 'canvas-shell', 'canvas-overlay', 'preview', 'drag-hint', 'image-name', 'zoom',
    'zoom-value', 'rotation', 'flip-horizontal', 'flip-vertical', 'dither',
    'reset-button', 'convert-button', 'download-button', 'connect-button',
    'send-button', 'cancel-button', 'device-name', 'device-detail', 'status-text',
    'progress-label', 'progress-bar', 'crc-label', 'tag-state', 'accepted-offset', 'toast'
  ].map(id => [id, document.getElementById(id)]));

  const context = elements.preview.getContext('2d', { willReadFrequently: true });
  const editor = {
    image: null,
    objectUrl: null,
    fileStem: 'newtonframe',
    fit: 'cover',
    zoom: 1,
    rotation: 0,
    flipH: false,
    flipV: false,
    panX: 0,
    panY: 0,
    packed: null,
    crc: null,
    converting: false
  };

  const ble = {
    device: null,
    server: null,
    control: null,
    data: null,
    statusCharacteristic: null,
    notifications: false,
    status: { version: 0, state: 0, error: 0, offset: 0, crc: 0 },
    waiters: new Set(),
    transferring: false,
    cancelled: false
  };

  let deferredInstallPrompt = null;
  let dragState = null;
  let toastTimer = 0;

  function showToast(message) {
    clearTimeout(toastTimer);
    elements.toast.textContent = message;
    elements.toast.classList.add('show');
    toastTimer = setTimeout(() => elements.toast.classList.remove('show'), 3200);
  }

  function setStatus(message, percent = null) {
    elements['status-text'].textContent = message;
    if (percent !== null) {
      const value = Math.max(0, Math.min(100, Math.round(percent)));
      elements['progress-label'].textContent = `${value}%`;
      elements['progress-bar'].style.width = `${value}%`;
      elements['progress-bar'].parentElement.setAttribute('aria-valuenow', String(value));
    }
  }

  function setEditorEnabled(enabled) {
    for (const item of [elements.zoom, elements.rotation, elements['flip-horizontal'],
      elements['flip-vertical'], elements.dither, elements['reset-button'], elements['convert-button']]) {
      item.disabled = !enabled;
    }
  }

  function markDirty(message = 'Preview changed — convert again before sending.') {
    editor.packed = null;
    editor.crc = null;
    elements['download-button'].disabled = true;
    elements['send-button'].disabled = true;
    elements['crc-label'].textContent = 'CRC —';
    elements['canvas-overlay'].textContent = 'Preview';
    setStatus(message, 0);
  }

  function enabledColors() {
    return COLORS.filter(color => document.querySelector(`[data-color="${color.code}"]`).checked);
  }

  function nearestColor(r, g, b, palette) {
    let best = palette[0];
    let bestDistance = Infinity;
    for (const color of palette) {
      const dr = r - color.rgb[0];
      const dg = g - color.rgb[1];
      const db = b - color.rgb[2];
      const distance = dr * dr * 0.299 + dg * dg * 0.587 + db * db * 0.114;
      if (distance < bestDistance) {
        best = color;
        bestDistance = distance;
      }
    }
    return best;
  }

  function baseScale() {
    const rotated = editor.rotation === 90 || editor.rotation === 270;
    const sourceWidth = rotated ? editor.image.height : editor.image.width;
    const sourceHeight = rotated ? editor.image.width : editor.image.height;
    return editor.fit === 'cover'
      ? Math.max(WIDTH / sourceWidth, HEIGHT / sourceHeight)
      : Math.min(WIDTH / sourceWidth, HEIGHT / sourceHeight);
  }

  function drawSource() {
    if (!editor.image) return;
    context.save();
    context.fillStyle = 'rgb(245, 240, 210)';
    context.fillRect(0, 0, WIDTH, HEIGHT);
    context.translate(WIDTH / 2 + editor.panX, HEIGHT / 2 + editor.panY);
    context.scale(editor.flipH ? -1 : 1, editor.flipV ? -1 : 1);
    context.rotate(editor.rotation * Math.PI / 180);
    const scale = baseScale() * editor.zoom;
    context.imageSmoothingEnabled = true;
    context.imageSmoothingQuality = 'high';
    context.drawImage(
      editor.image,
      -editor.image.width * scale / 2,
      -editor.image.height * scale / 2,
      editor.image.width * scale,
      editor.image.height * scale
    );
    context.restore();
  }

  function resetTransform(redraw = true) {
    editor.fit = 'cover';
    editor.zoom = 1;
    editor.rotation = 0;
    editor.flipH = false;
    editor.flipV = false;
    editor.panX = 0;
    editor.panY = 0;
    elements.zoom.value = '100';
    elements['zoom-value'].textContent = '100%';
    elements.rotation.value = '0';
    for (const button of document.querySelectorAll('[data-fit]')) {
      const active = button.dataset.fit === 'cover';
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', String(active));
    }
    elements['flip-horizontal'].setAttribute('aria-pressed', 'false');
    elements['flip-vertical'].setAttribute('aria-pressed', 'false');
    if (redraw && editor.image) {
      drawSource();
      markDirty();
    }
  }

  async function loadImage(file) {
    if (!file || !file.type.startsWith('image/')) {
      showToast('Choose a supported image file.');
      return;
    }
    if (editor.objectUrl) URL.revokeObjectURL(editor.objectUrl);
    editor.objectUrl = URL.createObjectURL(file);
    const image = new Image();
    image.decoding = 'async';
    image.onload = () => {
      editor.image = image;
      editor.fileStem = (file.name.replace(/\.[^.]+$/, '') || 'newtonframe').replace(/[^a-z0-9_-]+/gi, '-');
      elements['image-name'].textContent = file.name;
      elements['change-image'].hidden = false;
      elements['drop-zone'].hidden = true;
      elements['canvas-shell'].hidden = false;
      elements['drag-hint'].hidden = false;
      setEditorEnabled(true);
      resetTransform(false);
      drawSource();
      markDirty('Image loaded — frame it, then convert.');
    };
    image.onerror = () => showToast('This browser could not decode that image.');
    image.src = editor.objectUrl;
  }

  async function convertImage() {
    if (!editor.image || editor.converting) return;
    editor.converting = true;
    elements['convert-button'].disabled = true;
    elements['send-button'].disabled = true;
    drawSource();
    await new Promise(requestAnimationFrame);

    const image = context.getImageData(0, 0, WIDTH, HEIGHT);
    const pixels = image.data;
    const useDither = elements.dither.checked;
    const errors = useDither ? new Float32Array(WIDTH * HEIGHT * 3) : null;
    const packed = new Uint8Array(FRAME_BYTES);
    const palette = enabledColors();

    for (let y = 0; y < HEIGHT; y++) {
      for (let x = 0; x < WIDTH; x++) {
        const pixel = y * WIDTH + x;
        const rgba = pixel * 4;
        const error = pixel * 3;
        const r = Math.max(0, Math.min(255, pixels[rgba] + (errors ? errors[error] : 0)));
        const g = Math.max(0, Math.min(255, pixels[rgba + 1] + (errors ? errors[error + 1] : 0)));
        const b = Math.max(0, Math.min(255, pixels[rgba + 2] + (errors ? errors[error + 2] : 0)));
        const color = nearestColor(r, g, b, palette);
        pixels[rgba] = color.rgb[0];
        pixels[rgba + 1] = color.rgb[1];
        pixels[rgba + 2] = color.rgb[2];
        pixels[rgba + 3] = 255;
        packed[pixel >> 2] |= color.code << (6 - (pixel & 3) * 2);

        if (errors) {
          const er = r - color.rgb[0];
          const eg = g - color.rgb[1];
          const eb = b - color.rgb[2];
          const spread = (targetX, targetY, weight) => {
            if (targetX < 0 || targetX >= WIDTH || targetY >= HEIGHT) return;
            const target = (targetY * WIDTH + targetX) * 3;
            errors[target] += er * weight;
            errors[target + 1] += eg * weight;
            errors[target + 2] += eb * weight;
          };
          spread(x + 1, y, 7 / 16);
          spread(x - 1, y + 1, 3 / 16);
          spread(x, y + 1, 5 / 16);
          spread(x + 1, y + 1, 1 / 16);
        }
      }
      if (y % 24 === 23) {
        setStatus(`Converting row ${y + 1} of ${HEIGHT}…`, (y + 1) * 100 / HEIGHT);
        await new Promise(requestAnimationFrame);
      }
    }

    context.putImageData(image, 0, 0);
    editor.packed = packed;
    editor.crc = crc32(packed);
    editor.converting = false;
    elements['convert-button'].disabled = false;
    elements['download-button'].disabled = false;
    elements['send-button'].disabled = !isConnected();
    elements['crc-label'].textContent = `CRC ${hex32(editor.crc)}`;
    elements['canvas-overlay'].textContent = useDither ? 'Dithered' : 'Quantized';
    setStatus('Image ready to send', 100);
  }

  function crc32(bytes) {
    let crc = 0xffffffff;
    for (const value of bytes) {
      crc ^= value;
      for (let bit = 0; bit < 8; bit++) {
        crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
      }
    }
    return (crc ^ 0xffffffff) >>> 0;
  }

  function hex32(value) {
    return value.toString(16).toUpperCase().padStart(8, '0');
  }

  function isConnected() {
    return Boolean(ble.device?.gatt?.connected && ble.control && ble.data && ble.statusCharacteristic);
  }

  function withTimeout(promise, timeoutMs, message) {
    return Promise.race([
      promise,
      new Promise((_, reject) => setTimeout(() => reject(new Error(message)), timeoutMs))
    ]);
  }

  function parseStatus(dataView) {
    if (!dataView || dataView.byteLength < 12) return;
    ble.status = {
      version: dataView.getUint8(0),
      state: dataView.getUint8(1),
      error: dataView.getUint16(2, true),
      offset: dataView.getUint32(4, true),
      crc: dataView.getUint32(8, true)
    };
    elements['tag-state'].textContent = STATE_NAMES[ble.status.state] || `Unknown (${ble.status.state})`;
    elements['accepted-offset'].textContent = `${ble.status.offset.toLocaleString()} / ${FRAME_BYTES.toLocaleString()} bytes`;
    for (const notify of [...ble.waiters]) notify();
  }

  function onStatusChanged(event) {
    parseStatus(event.target.value);
  }

  function onDisconnected() {
    const completed = ble.status.state === 5;
    ble.server = null;
    ble.control = null;
    ble.data = null;
    ble.statusCharacteristic = null;
    ble.notifications = false;
    ble.transferring = false;
    elements['device-name'].textContent = 'No tag connected';
    elements['device-detail'].textContent = 'Wake your tag, then connect';
    elements['connect-button'].textContent = 'Connect tag';
    elements['connect-button'].disabled = false;
    elements['send-button'].disabled = true;
    elements['cancel-button'].hidden = true;
    for (const notify of [...ble.waiters]) notify();
    if (completed) {
      setStatus('Done — your new picture is on the frame', 100);
    } else if (!ble.cancelled) {
      setStatus('Tag disconnected', null);
    }
  }

  async function connectTag() {
    if (!navigator.bluetooth) {
      elements.compatibility.hidden = false;
      showToast('Open this page in a Web Bluetooth browser.');
      return;
    }
    if (isConnected()) {
      ble.device.gatt.disconnect();
      return;
    }

    elements['connect-button'].disabled = true;
    setStatus('Choose EPHOTO-648 in the Bluetooth prompt…', 0);
    try {
      const device = await navigator.bluetooth.requestDevice({
        filters: [{ name: 'EPHOTO-648' }],
        optionalServices: [SERVICE_UUID]
      });
      ble.device = device;
      device.addEventListener('gattserverdisconnected', onDisconnected);
      elements['device-name'].textContent = device.name || 'EPHOTO-648';
      elements['device-detail'].textContent = 'Opening Bluetooth link…';
      ble.server = await withTimeout(device.gatt.connect(), 10000, 'Bluetooth link timed out');
      elements['device-detail'].textContent = 'Enumerating GATT services…';
      const services = await withTimeout(
        ble.server.getPrimaryServices(), 12000, 'GATT service enumeration timed out'
      );
      const service = services.find(item => item.uuid.toLowerCase() === SERVICE_UUID);
      if (!service) throw new Error('The tag connected but did not expose the photo service');
      elements['device-detail'].textContent = 'Finding control channel…';
      ble.control = await withTimeout(
        service.getCharacteristic(CONTROL_UUID), 10000, 'Control channel discovery timed out'
      );
      elements['device-detail'].textContent = 'Finding data channel…';
      ble.data = await withTimeout(
        service.getCharacteristic(DATA_UUID), 10000, 'Data channel discovery timed out'
      );
      elements['device-detail'].textContent = 'Finding status channel…';
      ble.statusCharacteristic = await withTimeout(
        service.getCharacteristic(STATUS_UUID), 10000, 'Status channel discovery timed out'
      );
      elements['device-detail'].textContent = 'Starting status updates…';
      try {
        await withTimeout(
          ble.statusCharacteristic.startNotifications(), 4000, 'Notifications unavailable'
        );
        ble.statusCharacteristic.addEventListener('characteristicvaluechanged', onStatusChanged);
        ble.notifications = true;
      } catch (_) {
        ble.notifications = false;
      }
      elements['device-detail'].textContent = 'Reading tag status…';
      parseStatus(await withTimeout(
        ble.statusCharacteristic.readValue(), 5000, 'Initial status read timed out'
      ));
      elements['device-detail'].textContent = ble.notifications
        ? 'Connected and ready'
        : 'Connected and ready (polling status)';
      elements['connect-button'].textContent = 'Disconnect';
      elements['connect-button'].disabled = false;
      elements['send-button'].disabled = !editor.packed;
      setStatus(editor.packed ? 'Image ready to send' : 'Connected — convert an image', editor.packed ? 100 : 0);
    } catch (error) {
      if (ble.device?.gatt?.connected) ble.device.gatt.disconnect();
      ble.server = null;
      ble.control = null;
      ble.data = null;
      ble.statusCharacteristic = null;
      ble.notifications = false;
      elements['device-name'].textContent = 'No tag connected';
      elements['device-detail'].textContent = error.message || String(error);
      elements['connect-button'].disabled = false;
      if (error.name !== 'NotFoundError') showToast(error.message || String(error));
      setStatus(error.name === 'NotFoundError' ? 'Connection cancelled' : `Connection failed: ${error.message || error}`, 0);
    }
  }

  function writeWithResponse(characteristic, value) {
    if (characteristic.writeValueWithResponse) return characteristic.writeValueWithResponse(value);
    return characteristic.writeValue(value);
  }

  function writeWithoutResponse(characteristic, value) {
    if (characteristic.writeValueWithoutResponse) return characteristic.writeValueWithoutResponse(value);
    return writeWithResponse(characteristic, value);
  }

  function transferError() {
    const detail = ERROR_NAMES[ble.status.error] || `Tag error ${ble.status.error}`;
    return new Error(`${detail} at byte ${ble.status.offset.toLocaleString()}`);
  }

  function waitForStatus(predicate, timeoutMs) {
    if (ble.cancelled) return Promise.reject(new DOMException('Transfer cancelled', 'AbortError'));
    if (ble.status.state === 255) return Promise.reject(transferError());
    if (predicate(ble.status)) return Promise.resolve(ble.status);
    return new Promise((resolve, reject) => {
      let polling = false;
      const cleanup = () => {
        clearTimeout(timeout);
        clearInterval(poll);
        ble.waiters.delete(check);
      };
      const timeout = setTimeout(() => {
        cleanup();
        reject(new Error('Timed out waiting for the tag. Wake it and try again.'));
      }, timeoutMs);
      const check = () => {
        if (ble.cancelled) {
          cleanup();
          reject(new DOMException('Transfer cancelled', 'AbortError'));
        } else if (!isConnected()) {
          cleanup();
          reject(new Error('The tag disconnected.'));
        } else if (ble.status.state === 255) {
          cleanup();
          reject(transferError());
        } else if (predicate(ble.status)) {
          cleanup();
          resolve(ble.status);
        }
      };
      const poll = setInterval(async () => {
        if (polling || !isConnected()) {
          check();
          return;
        }
        polling = true;
        try {
          parseStatus(await ble.statusCharacteristic.readValue());
        } catch (_) {
          // The connection check below reports a disconnect; transient read errors retry.
        } finally {
          polling = false;
          check();
        }
      }, ble.notifications ? 1000 : 250);
      ble.waiters.add(check);
    });
  }

  async function abortTransfer() {
    ble.cancelled = true;
    for (const notify of [...ble.waiters]) notify();
    if (isConnected()) {
      try { await writeWithResponse(ble.control, new Uint8Array([3])); } catch (_) { /* Connection may already be gone. */ }
    }
  }

  async function sendFrame() {
    if (!editor.packed || !isConnected() || ble.transferring) return;
    ble.transferring = true;
    ble.cancelled = false;
    elements['send-button'].disabled = true;
    elements['download-button'].disabled = true;
    elements['connect-button'].disabled = true;
    elements['cancel-button'].hidden = false;
    let wakeLock = null;

    try {
      if ('wakeLock' in navigator) {
        try { wakeLock = await navigator.wakeLock.request('screen'); } catch (_) { /* Optional enhancement. */ }
      }

      const start = new ArrayBuffer(16);
      const header = new DataView(start);
      header.setUint8(0, 1);
      header.setUint8(1, 1);
      header.setUint16(2, WIDTH, true);
      header.setUint16(4, HEIGHT, true);
      header.setUint8(6, 1);
      header.setUint8(7, 0);
      header.setUint32(8, FRAME_BYTES, true);
      header.setUint32(12, editor.crc, true);
      setStatus('Preparing the e-paper controller…', 0);
      await writeWithResponse(ble.control, start);
      await waitForStatus(status => status.state === 2 && status.offset === 0, 15000);

      const transferStarted = performance.now();
      for (let offset = 0; offset < FRAME_BYTES; offset += CHUNK_BYTES) {
        if (ble.cancelled) throw new DOMException('Transfer cancelled', 'AbortError');
        const count = Math.min(CHUNK_BYTES, FRAME_BYTES - offset);
        const packet = new Uint8Array(4 + count);
        new DataView(packet.buffer).setUint32(0, offset, true);
        packet.set(editor.packed.subarray(offset, offset + count), 4);
        await writeWithoutResponse(ble.data, packet);
        const nextOffset = offset + count;
        if (nextOffset % ACK_INTERVAL === 0 || nextOffset === FRAME_BYTES) {
          await waitForStatus(status => status.state === 2 && status.offset >= nextOffset, 8000);
          const elapsedSeconds = Math.max((performance.now() - transferStarted) / 1000, 0.001);
          const kibPerSecond = nextOffset / elapsedSeconds / 1024;
          setStatus(`Sending ${nextOffset.toLocaleString()} of ${FRAME_BYTES.toLocaleString()} bytes · ${kibPerSecond.toFixed(1)} KiB/s`, nextOffset * 100 / FRAME_BYTES);
        }
      }

      setStatus('Verifying image checksum…', 100);
      await writeWithResponse(ble.control, new Uint8Array([2]));
      await waitForStatus(status => status.state === 3 || status.state === 4 || status.state === 5, 5000);
      setStatus('Refreshing the e-paper display…', 100);
      await waitForStatus(status => status.state === 5, 120000);
      if (ble.status.crc !== editor.crc) throw new Error('The tag completed with an unexpected CRC.');
      setStatus('Done — your new picture is on the frame', 100);
      showToast('Transfer complete. The e-paper will retain this image without power.');
    } catch (error) {
      if (error.name === 'AbortError') {
        setStatus('Transfer cancelled — the previous display image was kept', 0);
      } else {
        setStatus(error.message || String(error), 0);
        try { await abortTransfer(); } catch (_) { /* Best effort. */ }
      }
    } finally {
      if (wakeLock) await wakeLock.release();
      ble.transferring = false;
      ble.cancelled = false;
      elements['cancel-button'].hidden = true;
      elements['connect-button'].disabled = false;
      elements['download-button'].disabled = !editor.packed;
      elements['send-button'].disabled = !editor.packed || !isConnected();
    }
  }

  function downloadFrame() {
    if (!editor.packed) return;
    const blob = new Blob([editor.packed], { type: 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${editor.fileStem}-648x480-bwry.bin`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  elements['image-input'].addEventListener('change', () => loadImage(elements['image-input'].files[0]));
  for (const eventName of ['dragenter', 'dragover']) {
    elements['drop-zone'].addEventListener(eventName, event => {
      event.preventDefault();
      elements['drop-zone'].classList.add('dragging');
    });
  }
  for (const eventName of ['dragleave', 'drop']) {
    elements['drop-zone'].addEventListener(eventName, event => {
      event.preventDefault();
      elements['drop-zone'].classList.remove('dragging');
    });
  }
  elements['drop-zone'].addEventListener('drop', event => loadImage(event.dataTransfer.files[0]));

  document.querySelectorAll('[data-fit]').forEach(button => button.addEventListener('click', () => {
    editor.fit = button.dataset.fit;
    document.querySelectorAll('[data-fit]').forEach(item => {
      const active = item === button;
      item.classList.toggle('active', active);
      item.setAttribute('aria-pressed', String(active));
    });
    editor.panX = 0;
    editor.panY = 0;
    drawSource();
    markDirty();
  }));

  elements.zoom.addEventListener('input', () => {
    editor.zoom = Number(elements.zoom.value) / 100;
    elements['zoom-value'].textContent = `${elements.zoom.value}%`;
    drawSource();
    markDirty();
  });
  elements.rotation.addEventListener('change', () => {
    editor.rotation = Number(elements.rotation.value);
    editor.panX = 0;
    editor.panY = 0;
    drawSource();
    markDirty();
  });

  for (const [id, property] of [['flip-horizontal', 'flipH'], ['flip-vertical', 'flipV']]) {
    elements[id].addEventListener('click', () => {
      editor[property] = !editor[property];
      elements[id].setAttribute('aria-pressed', String(editor[property]));
      drawSource();
      markDirty();
    });
  }

  elements['canvas-shell'].addEventListener('pointerdown', event => {
    if (!editor.image || editor.fit !== 'cover') return;
    dragState = { id: event.pointerId, x: event.clientX, y: event.clientY, panX: editor.panX, panY: editor.panY };
    elements['canvas-shell'].setPointerCapture(event.pointerId);
    elements['canvas-shell'].classList.add('dragging');
  });
  elements['canvas-shell'].addEventListener('pointermove', event => {
    if (!dragState || dragState.id !== event.pointerId) return;
    const scaleX = WIDTH / elements['canvas-shell'].clientWidth;
    const scaleY = HEIGHT / elements['canvas-shell'].clientHeight;
    editor.panX = dragState.panX + (event.clientX - dragState.x) * scaleX;
    editor.panY = dragState.panY + (event.clientY - dragState.y) * scaleY;
    drawSource();
    markDirty();
  });
  const endDrag = event => {
    if (!dragState || dragState.id !== event.pointerId) return;
    dragState = null;
    elements['canvas-shell'].classList.remove('dragging');
  };
  elements['canvas-shell'].addEventListener('pointerup', endDrag);
  elements['canvas-shell'].addEventListener('pointercancel', endDrag);

  document.querySelectorAll('[data-color]').forEach(input => input.addEventListener('change', () => {
    if (!enabledColors().length) {
      input.checked = true;
      showToast('At least one pigment must stay enabled.');
      return;
    }
    if (editor.image) {
      drawSource();
      markDirty('Palette changed — convert again to apply it.');
    }
  }));
  elements.dither.addEventListener('change', () => editor.image && markDirty('Dithering changed — convert again to apply it.'));
  elements['reset-button'].addEventListener('click', () => resetTransform());
  elements['convert-button'].addEventListener('click', convertImage);
  elements['download-button'].addEventListener('click', downloadFrame);
  elements['connect-button'].addEventListener('click', connectTag);
  elements['send-button'].addEventListener('click', sendFrame);
  elements['cancel-button'].addEventListener('click', abortTransfer);

  if (navigator.bluetooth) {
    elements['browser-badge'].classList.add('ready');
    elements['browser-badge'].querySelector('span').textContent = 'Bluetooth ready';
  } else {
    elements['browser-badge'].classList.add('unsupported');
    elements['browser-badge'].querySelector('span').textContent = 'Bluetooth unavailable';
    elements.compatibility.hidden = false;
  }

  window.addEventListener('beforeinstallprompt', event => {
    event.preventDefault();
    deferredInstallPrompt = event;
    elements['install-button'].hidden = false;
  });
  elements['install-button'].addEventListener('click', async () => {
    if (!deferredInstallPrompt) return;
    deferredInstallPrompt.prompt();
    await deferredInstallPrompt.userChoice;
    deferredInstallPrompt = null;
    elements['install-button'].hidden = true;
  });

  if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => navigator.serviceWorker.register('sw.js').catch(() => {}));
  }
})();
