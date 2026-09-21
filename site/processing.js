(root => {
  'use strict';

  function crc32(bytes) {
    let crc = 0xffffffff;
    for (const value of bytes) {
      crc ^= value;
      for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
    }
    return (crc ^ 0xffffffff) >>> 0;
  }

  function packPixels(codes) {
    if (codes.length % 4 !== 0) throw new RangeError('Pixel count must be divisible by four.');
    const packed = new Uint8Array(codes.length / 4);
    for (let pixel = 0; pixel < codes.length; pixel++) {
      const code = codes[pixel];
      if (code < 0 || code > 3) throw new RangeError('Pigment code must be between 0 and 3.');
      packed[pixel >> 2] |= code << (6 - (pixel & 3) * 2);
    }
    return packed;
  }

  function approximateBatteryPercent(millivolts) {
    return Math.max(0, Math.min(100, Math.round((millivolts - 2500) / 7)));
  }

  const api = { crc32, packPixels, approximateBatteryPercent };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  root.RoggenCoreProcessing = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
