// Adds or changes words of the phone app (mobile-android/src/main/assets/i18n-words.js) and copies the file to the
// PC's copy of the page (src/CloudSSD.Desktop/wwwroot/mobile). The file itself is the whole list: nothing else keeps it.
//
//   node tools/i18n/add-words.js <words.json>       add / change: {"English text": {"hinglish": "...", "hindi": "..."}}
//   node tools/i18n/add-words.js --check            only rewrite the file in its usual form and say how many texts
//
// "{0}", "{1}" stand for values (a name, a number): each translation must have the same ones as the English text.
// A Hindi sentence ends in "।", not "." (only after a Devanagari letter, so "1.5" and "PC." stay).
const fs = require('fs');
const path = require('path');

const repo = path.join(__dirname, '..', '..');
const file = path.join(repo, 'mobile-android', 'src', 'main', 'assets', 'i18n-words.js');
const copy = path.join(repo, 'src', 'CloudSSD.Desktop', 'wwwroot', 'mobile', 'i18n-words.js');

const arg = process.argv[2];
if (!arg) { console.log('usage: node tools/i18n/add-words.js <words.json> | --check'); process.exit(1); }

delete require.cache[require.resolve(file)];
const words = require(file);
const before = Object.keys(words.hinglish);

/** The words as one list: english → {hinglish, hindi}. */
const all = {};
for (const k of before) all[k] = { hinglish: words.hinglish[k], hindi: (words.hindi || {})[k] };

const holes = (s) => (String(s).match(/\{\d\}/g) || []).sort().join(',');
const hindiStop = (s) => s.replace(/([ऀ-ॿ])\.(?=$|[\s"')\]])/g, '$1।');
// On screen "&amp;" and "&lt;" are already "&" and "<": the words must match what the screen holds.
const plain = (s) => String(s).replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&');

let added = 0, changed = 0;
if (arg !== '--check') {
  const extra = JSON.parse(fs.readFileSync(arg, 'utf8'));
  for (const [raw, v] of Object.entries(extra)) {
    const k = plain(raw);
    if (!v || !v.hinglish || !v.hindi) { console.log('missing hinglish or hindi: ' + k); process.exit(1); }
    const hinglish = plain(v.hinglish), hindi = hindiStop(plain(v.hindi));
    if (holes(hinglish) !== holes(k) || holes(hindi) !== holes(k)) { console.log('{0} holes do not match: ' + k); process.exit(1); }
    if (!all[k]) added++;
    else if (all[k].hinglish !== hinglish || all[k].hindi !== hindi) changed++;
    all[k] = { hinglish, hindi };
  }
}
for (const [k, v] of Object.entries(all)) {
  if (!v.hinglish || !v.hindi) { console.log('a text without both translations: ' + k); process.exit(1); }
}

const keys = Object.keys(all).sort((a, b) => a.localeCompare(b, 'en'));
const lost = before.filter((k) => !(k in all));
if (lost.length) { console.log('texts would be lost: ' + lost.slice(0, 5).join(' | ')); process.exit(1); }

const side = (lang) => '{\n' + keys.map((k) => '    ' + JSON.stringify(k) + ': ' + JSON.stringify(all[k][lang])).join(',\n') + '\n  }';
const text = `/*
 * The phone app's words: the English text on screen → Hinglish and हिन्दी. Used by i18n.js (load this first).
 * "{0}" stands for a value (a name, a number). Texts not listed here stay English.
 */
(function (root) {
  'use strict';
  const words = {
  hinglish: ${side('hinglish')},
  hindi: ${side('hindi')}
  };
  root.CloudSsdWords = words;
  if (typeof module !== 'undefined' && module.exports) module.exports = words;
})(typeof window !== 'undefined' ? window : globalThis);
`;
fs.writeFileSync(file, text, 'utf8');
fs.copyFileSync(file, copy);
console.log(`${keys.length} texts (${added} new, ${changed} changed) → i18n-words.js and the PC copy`);
