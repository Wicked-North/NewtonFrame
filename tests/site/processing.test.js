const test = require('node:test');
const assert = require('node:assert/strict');
const {
  crc32, packPixels, approximateBatteryPercent, batteryMillivoltsFromStatus,
  diffusionKernel, orderedDitherOffset, snapArtworkToBlackPaper
} = require('../../site/processing.js');

test('CRC-32 matches the IEEE check vector', () => {
  assert.equal(crc32(new TextEncoder().encode('123456789')), 0xcbf43926);
});

test('four pigment codes pack high bits first', () => {
  assert.deepEqual([...packPixels(Uint8Array.from([0, 1, 2, 3]))], [0x1b]);
  assert.deepEqual([...packPixels(Uint8Array.from([1, 1, 1, 1]))], [0x55]);
});

test('battery estimate is clamped and monotonic', () => {
  assert.equal(approximateBatteryPercent(2400), 0);
  assert.equal(approximateBatteryPercent(2850), 50);
  assert.equal(approximateBatteryPercent(3300), 100);
});

test('battery voltage uses the RoggenCore 1.1.1 status extension', () => {
  const legacy = new DataView(new ArrayBuffer(12));
  assert.equal(batteryMillivoltsFromStatus(legacy), null);
  const current = new DataView(new ArrayBuffer(14));
  current.setUint16(12, 3190, true);
  assert.equal(batteryMillivoltsFromStatus(current), 3190);
  current.setUint16(12, 0, true);
  assert.equal(batteryMillivoltsFromStatus(current), null);
});

test('error-diffusion modes expose their expected kernels', () => {
  assert.equal(diffusionKernel('floyd-steinberg').reduce((sum, item) => sum + item[2], 0), 1);
  assert.equal(diffusionKernel('atkinson').reduce((sum, item) => sum + item[2], 0), 0.75);
  assert.equal(diffusionKernel('sierra-lite').reduce((sum, item) => sum + item[2], 0), 1);
  assert.equal(diffusionKernel('none'), null);
  assert.equal(diffusionKernel('bayer-4x4'), null);
});

test('Bayer 4x4 offsets form a centered 16-level threshold map', () => {
  const offsets = [];
  for (let y = 0; y < 4; y++) for (let x = 0; x < 4; x++) offsets.push(orderedDitherOffset('bayer-4x4', x, y));
  assert.equal(new Set(offsets).size, 16);
  assert.equal(offsets.reduce((sum, value) => sum + value, 0), 0);
  assert.equal(orderedDitherOffset('none', 0, 0), 0);
});

test('welcome artwork snaps antialias shades without changing pigment swatches', () => {
  const pixels = Uint8ClampedArray.from([
    134, 132, 116, 255,
    198, 194, 171, 255,
    228, 189, 45, 255
  ]);
  snapArtworkToBlackPaper(pixels, 3, 1, [{ x: 2, y: 0, width: 1, height: 1 }]);
  assert.deepEqual([...pixels], [
    23, 23, 23, 255,
    245, 240, 210, 255,
    228, 189, 45, 255
  ]);
});
