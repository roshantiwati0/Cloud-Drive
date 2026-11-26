// Turns the icon pictures made by make-icons.js into PNG files, with Microsoft Edge (headless) as the drawing tool:
//   mobile-android/src/main/res/mipmap-xxhdpi/ic_app_<key>.png   144 x 144, for Android 7 (no adaptive icons there)
//   src/CloudSSD.Desktop/Assets/clouddrive.png                   1024 x 1024, the PC app's picture (from icon-ocean.svg)
//
//   node tools/icons/png.js
const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync } = require('child_process');
const main = path.join(__dirname, '..', '..', 'mobile-android', 'src', 'main');
const edge = [process.env['ProgramFiles(x86)'], process.env.ProgramFiles].filter(Boolean)
  .map(p => path.join(p, 'Microsoft', 'Edge', 'Application', 'msedge.exe')).find(p => fs.existsSync(p));
if (!edge) { console.error('Microsoft Edge not found'); process.exit(1); }
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'cssd-icons-'));

function shot(html, size, out) {
  const page = path.join(tmp, 'p.html');
  fs.writeFileSync(page, `<!doctype html><meta charset="utf-8"><body style="margin:0;background:transparent">${html}</body>`, 'utf8');
  execFileSync(edge, ['--headless=new', '--disable-gpu', '--hide-scrollbars', '--default-background-color=00000000',
    '--user-data-dir=' + path.join(tmp, 'prof'), '--screenshot=' + out, `--window-size=${size},${size}`, 'file:///' + page.replace(/\\/g, '/')], { stdio: 'ignore' });
}

const svgs = fs.readdirSync(path.join(main, 'assets')).filter(f => /^icon-[a-z]+\.svg$/.test(f));
for (const f of svgs) {
  const key = f.slice(5, -4);
  const svg = fs.readFileSync(path.join(main, 'assets', f), 'utf8');
  shot(svg, 144, path.join(main, 'res', 'mipmap-xxhdpi', `ic_app_${key}.png`));
  console.log('png', key);
}
// The PC app's picture: the app's own icon (Ocean), big.
const own = fs.readFileSync(path.join(main, 'assets', 'icon-ocean.svg'), 'utf8').replace('width="144" height="144"', 'width="1024" height="1024"');
shot(own, 1024, path.join(main, '..', '..', '..', 'src', 'CloudSSD.Desktop', 'Assets', 'clouddrive.png'));
console.log('png pc');
fs.rmSync(tmp, { recursive: true, force: true });
