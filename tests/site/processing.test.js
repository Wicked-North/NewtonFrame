const test = require('node:test');
const assert = require('node:assert/strict');
const { crc32, packPixels, approximateBatteryPercent } = require('../../site/processing.js');

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
