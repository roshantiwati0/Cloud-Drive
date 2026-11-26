// Fixed texts in the PC app's XAML (Text, Content, ToolTip, Header, Title) that have no Hinglish entry in any
// LocalizationManager*.cs. Usage: node pc-gaps.js <src/CloudSSD.Desktop>
const fs = require('fs'), path = require('path');
const dir = process.argv[2];
const keys = new Set();
for (const f of fs.readdirSync(dir).filter(f => /^LocalizationManager.*\.cs$/.test(f))) {
  const s = fs.readFileSync(path.join(dir, f), 'utf8');
  for (const m of s.matchAll(/\["((?:[^"\\]|\\.)*)"\]\s*=/g)) keys.add(m[1].replace(/\\"/g, '"').replace(/\\\\/g, '\\'));
  for (const m of s.matchAll(/\{\s*"((?:[^"\\]|\\.)*)",\s*"/g)) keys.add(m[1].replace(/\\"/g, '"'));
}
const out = {};
function walk(d) {
  for (const f of fs.readdirSync(d)) {
    const p = path.join(d, f);
    if (/\\(bin|obj)$/.test(p)) continue;
    if (fs.statSync(p).isDirectory()) { walk(p); continue; }
    if (!f.endsWith('.xaml')) continue;
    const s = fs.readFileSync(p, 'utf8');
    for (const m of s.matchAll(/\b(?:Text|Content|ToolTip|Header|Title)="([^"{][^"]*)"/g)) {
      const t = m[1].replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&#x0a;|&#10;/gi, '\n').trim();
      if (!/[A-Za-z]{2}/.test(t) || keys.has(t)) continue;
      (out[path.relative(dir, p)] = out[path.relative(dir, p)] || []).push(t);
    }
  }
}
walk(dir);
let n = 0;
for (const [f, list] of Object.entries(out)) { n += list.length; console.log(`${f} (${list.length})`); for (const t of list) console.log('   ' + t.slice(0, 110)); }
console.log(n + ' texts without Hinglish, ' + keys.size + ' keys');
