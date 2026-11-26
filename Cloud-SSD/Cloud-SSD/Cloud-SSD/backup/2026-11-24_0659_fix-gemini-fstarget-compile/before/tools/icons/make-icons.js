// Makes the phone app's launcher icons from one description, so the drawing in Android (vector drawables for an
// adaptive icon) and the picture the app shows in its icon picker (SVG) are the same shapes.
//
//   node tools/icons/make-icons.js            writes the files below
//   node tools/icons/make-icons.js --sheet f  also writes one HTML page with every icon (to look at them in a browser)
//
// Written per icon <key>:
//   mobile-android/src/main/res/drawable/ic_app_<key>_bg.xml      the adaptive icon's background (108 x 108)
//   mobile-android/src/main/res/drawable/ic_app_<key>_fg.xml      its foreground
//   mobile-android/src/main/res/mipmap-anydpi-v26/ic_app_<key>.xml the adaptive icon (Android 8+)
//   mobile-android/src/main/assets/icon-<key>.svg                 the picker's picture (the middle 72 of the 108)
// and, from the first icon (the app's own picture):
//   mobile-android/src/main/assets/icon-192.svg, icon-512.svg     the web page's icon (browser tab, "add to home screen")
// Android 7 has no adaptive icons: mipmap-xxhdpi/ic_app_<key>.png is made from the SVG by tools/icons/png.js, which
// also makes the PC app's picture (Assets/clouddrive.png); tools/icons/make-ico.ps1 makes the .ico files from that.
// The pictures the app had before 14 Nov 2026 are kept in backup/photo.
const fs = require('fs');
const path = require('path');
const root = path.join(__dirname, '..', '..', 'mobile-android', 'src', 'main');

// The cloud of Material Icons (Apache 2.0), in a 24 x 24 box, and a drive's slot and light inside it.
const CLOUD = 'M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z';
const SLOT = 'M6.9 14.2h5.4a1.15 1.15 0 0 1 0 2.3h-5.4a1.15 1.15 0 0 1 0-2.3z';
const LIGHT = 'M16.6 14.05a1.3 1.3 0 1 0 0 2.6a1.3 1.3 0 1 0 0-2.6z';

// bg: one colour, or [from, to] for a slope from the top left to the bottom right.
// tile: a rounded square behind the cloud (Kit). line: the cloud is drawn as an outline of that width (24-box units).
const ICONS = [
  { key: 'ocean', bg: ['#5B93FF', '#1A43D6'], cloud: '#FFFFFF', mark: '#2257E3', scale: 2.2 },
  { key: 'kit', bg: '#EDF0FB', tile: ['#5273FF', '#2A44C8'], cloud: '#FFFFFF', mark: '#3457F1', scale: 1.5 },
  { key: 'paper', bg: '#FFFFFF', cloud: '#111318', line: 1.5, mark: '#2563EB', scale: 2.1 },
  { key: 'glow', bg: '#0A0A0C', cloud: '#C6F26B', line: 1.5, mark: '#C6F26B', scale: 2.1 },
  { key: 'midnight', bg: ['#18233B', '#070B14'], cloud: '#5EEAD4', mark: '#0B1220', scale: 2.2 },
  { key: 'sunset', bg: ['#FFA24D', '#EE3F7F'], cloud: '#FFFFFF', mark: '#F0527A', scale: 2.2 }
];

const argb = (hex) => '#FF' + hex.slice(1).toUpperCase();
const move = (i) => (54 - 12 * i.scale).toFixed(2);

function androidFill(color, x1, y1, x2, y2) {
  if (typeof color === 'string') return { attr: ` android:fillColor="${argb(color)}"`, inner: '' };
  return { attr: '', inner: `
        <aapt:attr name="android:fillColor">
            <gradient android:type="linear" android:startX="${x1}" android:startY="${y1}" android:endX="${x2}" android:endY="${y2}" android:startColor="${argb(color[0])}" android:endColor="${argb(color[1])}" />
        </aapt:attr>
    ` };
}
function androidPath(d, fill, indent) {
  return fill.inner ? `${indent}<path android:pathData="${d}">${fill.inner}</path>` : `${indent}<path android:pathData="${d}"${fill.attr} />`;
}
const HEAD = `<?xml version="1.0" encoding="utf-8"?>
<!-- Made by tools/icons/make-icons.js: change the drawing there, not here. -->
<vector xmlns:android="http://schemas.android.com/apk/res/android" xmlns:aapt="http://schemas.android.com/aapt"
    android:width="108dp" android:height="108dp" android:viewportWidth="108" android:viewportHeight="108">
`;

function androidBg(i) {
  return HEAD + androidPath('M0 0h108v108H0z', androidFill(i.bg, 0, 0, 108, 108), '    ') + '\n</vector>\n';
}
function androidFg(i) {
  let s = HEAD;
  if (i.tile) s += androidPath('M44 30h20a14 14 0 0 1 14 14v20a14 14 0 0 1 -14 14h-20a14 14 0 0 1 -14 -14v-20a14 14 0 0 1 14 -14z', androidFill(i.tile, 30, 30, 78, 78), '    ') + '\n';
  s += `    <group android:translateX="${move(i)}" android:translateY="${move(i)}" android:scaleX="${i.scale}" android:scaleY="${i.scale}">\n`;
  s += i.line
    ? `        <path android:pathData="${CLOUD}" android:strokeColor="${argb(i.cloud)}" android:strokeWidth="${i.line}" android:strokeLineJoin="round" />\n`
    : `        <path android:pathData="${CLOUD}" android:fillColor="${argb(i.cloud)}" />\n`;
  s += `        <path android:pathData="${SLOT}" android:fillColor="${argb(i.mark)}" />\n`;
  s += `        <path android:pathData="${LIGHT}" android:fillColor="${argb(i.mark)}" />\n`;
  return s + '    </group>\n</vector>\n';
}
function androidIcon(i) {
  return `<?xml version="1.0" encoding="utf-8"?>
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@drawable/ic_app_${i.key}_bg" />
    <foreground android:drawable="@drawable/ic_app_${i.key}_fg" />
</adaptive-icon>
`;
}

/** The icon as a launcher shows it: the middle 72 of the 108, with round corners. */
function svg(i) {
  const grad = (id, c, x1, y1, x2, y2) => typeof c === 'string' ? '' : `<linearGradient id="${id}" gradientUnits="userSpaceOnUse" x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}"><stop offset="0" stop-color="${c[0]}"/><stop offset="1" stop-color="${c[1]}"/></linearGradient>`;
  const paint = (id, c) => typeof c === 'string' ? c : `url(#${id})`;
  const cloud = i.line
    ? `<path d="${CLOUD}" fill="none" stroke="${i.cloud}" stroke-width="${i.line}" stroke-linejoin="round"/>`
    : `<path d="${CLOUD}" fill="${i.cloud}"/>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="18 18 72 72" width="144" height="144">
<defs>${grad('b', i.bg, 0, 0, 108, 108)}${i.tile ? grad('t', i.tile, 30, 30, 78, 78) : ''}<clipPath id="m"><rect x="18" y="18" width="72" height="72" rx="17"/></clipPath></defs>
<g clip-path="url(#m)">
<rect width="108" height="108" fill="${paint('b', i.bg)}"/>
${i.tile ? `<rect x="30" y="30" width="48" height="48" rx="14" fill="${paint('t', i.tile)}"/>` : ''}
<g transform="translate(${move(i)} ${move(i)}) scale(${i.scale})">${cloud}<path d="${SLOT}" fill="${i.mark}"/><path d="${LIGHT}" fill="${i.mark}"/></g>
</g>
</svg>
`;
}

function write(rel, text) {
  const p = path.join(root, rel);
  fs.mkdirSync(path.dirname(p), { recursive: true });
  fs.writeFileSync(p, text.replace(/\r\n/g, '\n'), 'utf8');
}
for (const i of ICONS) {
  write(`res/drawable/ic_app_${i.key}_bg.xml`, androidBg(i));
  write(`res/drawable/ic_app_${i.key}_fg.xml`, androidFg(i));
  write(`res/mipmap-anydpi-v26/ic_app_${i.key}.xml`, androidIcon(i));
  write(`assets/icon-${i.key}.svg`, svg(i));
}
// The web page's own icon is the app's picture: the first icon, at the two sizes the page names.
for (const size of [192, 512]) write(`assets/icon-${size}.svg`, svg(ICONS[0]).replace('width="144" height="144"', `width="${size}" height="${size}"`));
const sheetAt = process.argv.indexOf('--sheet');
if (sheetAt > 0) {
  const cell = (i, size) => `<div style="text-align:center;font:12px sans-serif"><div style="width:${size}px;height:${size}px">${svg(i).replace('width="144" height="144"', `width="${size}" height="${size}"`).replace(/id="(b|t|m)"/g, `id="$1-${i.key}-${size}"`).replace(/#(b|t|m)\)/g, `#$1-${i.key}-${size})`)}</div>${i.key}</div>`;
  const row = (size, bg) => `<div style="display:flex;gap:18px;padding:18px;background:${bg}">${ICONS.map(i => cell(i, size)).join('')}</div>`;
  fs.writeFileSync(process.argv[sheetAt + 1], `<!doctype html><meta charset="utf-8"><body style="margin:0">${row(144, '#DDE3EE')}${row(56, '#DDE3EE')}${row(144, '#15181F;color:#fff')}${row(56, '#15181F;color:#fff')}</body>`, 'utf8');
}
console.log('icons:', ICONS.map(i => i.key).join(', '));
