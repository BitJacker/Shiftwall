// Renders the brand assets from the hand-written SVG sources in assets/brand/.
// Dev-only helper (not part of the app). Requires Node.js + the `playwright` package with Chromium.
//   node tools/brand/render-brand.js
// Outputs: icon.ico (16,24,32,48,64,128,256 – PNG-compressed entries), logo-512.png,
//          logo-1024.png, banner-1280x640.png.
'use strict';
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const brand = path.resolve(__dirname, '..', '..', 'assets', 'brand');
const svg = (f) => fs.readFileSync(path.join(brand, f), 'utf8');

// Hand-hinted sources for the smallest sizes, master for the rest.
const sourceFor = (size) => (size <= 16 ? 'logo-16.svg' : size <= 24 ? 'logo-24.svg' : 'logo.svg');

async function renderPng(page, svgText, w, h) {
  await page.setViewportSize({ width: w, height: h });
  const html = `<!doctype html><html><body style="margin:0;background:transparent">` +
    svgText.replace('<svg ', `<svg width="${w}" height="${h}" style="display:block" `) + `</body></html>`;
  await page.setContent(html);
  return page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: w, height: h } });
}

function buildIco(images) { // images: [{size, png}]
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0); header.writeUInt16LE(1, 2); header.writeUInt16LE(images.length, 4);
  const dir = Buffer.alloc(16 * images.length);
  let offset = 6 + dir.length;
  images.forEach(({ size, png }, i) => {
    const o = i * 16;
    dir.writeUInt8(size >= 256 ? 0 : size, o);
    dir.writeUInt8(size >= 256 ? 0 : size, o + 1);
    dir.writeUInt8(0, o + 2); dir.writeUInt8(0, o + 3);
    dir.writeUInt16LE(1, o + 4); dir.writeUInt16LE(32, o + 6);
    dir.writeUInt32LE(png.length, o + 8); dir.writeUInt32LE(offset, o + 12);
    offset += png.length;
  });
  return Buffer.concat([header, dir, ...images.map((x) => x.png)]);
}

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ deviceScaleFactor: 1 });
  const icoSizes = [16, 24, 32, 48, 64, 128, 256];
  const images = [];
  for (const s of icoSizes) images.push({ size: s, png: await renderPng(page, svg(sourceFor(s)), s, s) });
  fs.writeFileSync(path.join(brand, 'icon.ico'), buildIco(images));
  for (const s of [512, 1024]) fs.writeFileSync(path.join(brand, `logo-${s}.png`), await renderPng(page, svg('logo.svg'), s, s));
  fs.writeFileSync(path.join(brand, 'banner-1280x640.png'), await renderPng(page, svg('banner.svg'), 1280, 640));
  await browser.close();
  console.log('Brand assets written to', brand);
})().catch((e) => { console.error(e); process.exit(1); });
