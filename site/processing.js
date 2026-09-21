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

  function batteryMillivoltsFromStatus(dataView) {
    if (!dataView || dataView.byteLength < 14) return null;
    const millivolts = dataView.getUint16(12, true);
    return millivolts >= 1500 && millivolts <= 3800 ? millivolts : null;
  }

  const DIFFUSION_KERNELS = Object.freeze({
    'floyd-steinberg': Object.freeze([
      Object.freeze([1, 0, 7 / 16]), Object.freeze([-1, 1, 3 / 16]),
      Object.freeze([0, 1, 5 / 16]), Object.freeze([1, 1, 1 / 16])
    ]),
    atkinson: Object.freeze([
      Object.freeze([1, 0, 1 / 8]), Object.freeze([2, 0, 1 / 8]),
      Object.freeze([-1, 1, 1 / 8]), Object.freeze([0, 1, 1 / 8]),
      Object.freeze([1, 1, 1 / 8]), Object.freeze([0, 2, 1 / 8])
    ]),
    'sierra-lite': Object.freeze([
      Object.freeze([1, 0, 1 / 2]), Object.freeze([-1, 1, 1 / 4]),
      Object.freeze([0, 1, 1 / 4])
    ])
  });

  const BAYER_4X4 = Object.freeze([
    Object.freeze([0, 8, 2, 10]),
    Object.freeze([12, 4, 14, 6]),
    Object.freeze([3, 11, 1, 9]),
    Object.freeze([15, 7, 13, 5])
  ]);

  function diffusionKernel(mode) {
    return DIFFUSION_KERNELS[mode] || null;
  }

  function orderedDitherOffset(mode, x, y) {
    if (mode !== 'bayer-4x4') return 0;
    return ((BAYER_4X4[y & 3][x & 3] + 0.5) / 16 - 0.5) * 64;
  }

  function snapArtworkToBlackPaper(pixels, width, height, preserveRects = []) {
    if (pixels.length !== width * height * 4)
      throw new RangeError('RGBA buffer size does not match its dimensions.');
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        if (preserveRects.some(rect => x >= rect.x && x < rect.x + rect.width &&
          y >= rect.y && y < rect.y + rect.height)) continue;
        const offset = (y * width + x) * 4;
        const luminance = (299 * pixels[offset] + 587 * pixels[offset + 1] + 114 * pixels[offset + 2]) / 1000;
        const color = luminance < 132 ? [23, 23, 23] : [245, 240, 210];
        pixels[offset] = color[0];
        pixels[offset + 1] = color[1];
        pixels[offset + 2] = color[2];
        pixels[offset + 3] = 255;
      }
    }
    return pixels;
  }

  const api = {
    crc32, packPixels, approximateBatteryPercent, batteryMillivoltsFromStatus,
    diffusionKernel, orderedDitherOffset, snapArtworkToBlackPaper
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  root.RoggenCoreProcessing = api;
})(typeof globalThis !== 'undefined' ? globalThis : this);
