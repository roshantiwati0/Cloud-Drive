// Tests for the phone page's languages (mobile-android/src/main/assets/i18n.js, i18n-words.js) and the phone's own
// notes (Tr.java). Run: node --test tests/mobile-js/data.test.js tests/mobile-js/i18n.test.js
'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const assets = path.join(__dirname, '..', '..', 'mobile-android', 'src', 'main', 'assets');
const words = require(path.join(assets, 'i18n-words.js'));
const i18n = require(path.join(assets, 'i18n.js'));
const LANGS = ['hinglish', 'hindi'];

const holes = s => (String(s).match(/\{\d\}/g) || []).sort().join(',');

test('every text is in both Hinglish and Hindi', () => {
  const a = Object.keys(words.hinglish).sort();
  const b = Object.keys(words.hindi).sort();
  assert.ok(a.length > 900, `${a.length} texts`);
  assert.deepEqual(a.filter(k => !words.hindi[k]), [], 'only in Hinglish');
  assert.deepEqual(b.filter(k => !words.hinglish[k]), [], 'only in Hindi');
});

test('translations keep every {n} and are not empty', () => {
  for (const lang of LANGS) {
    for (const [english, out] of Object.entries(words[lang])) {
      assert.ok(typeof out === 'string' && out.trim(), `${lang}: "${english}" has no text`);
      assert.equal(holes(out), holes(english), `${lang}: "${english}" → "${out}"`);
      assert.equal(out, out.trim(), `${lang}: "${english}" has spaces around it`);
    }
  }
});

test('Hindi sentences end in "।", not "."', () => {
  const wrong = Object.entries(words.hindi).filter(([, out]) => /[\u0900-\u097F]\.(?=$|[\s"')\]])/.test(out));
  assert.deepEqual(wrong, []);
});

test('no template is only values (it would match every text)', () => {
  const empty = Object.keys(words.hinglish).filter(k => /\{\d\}/.test(k) && !/[A-Za-z]{2}/.test(k.replace(/\{\d\}/g, '')));
  assert.deepEqual(empty, []);
});

test('the page uses the words file the i18n code reads', () => {
  assert.equal(i18n.words, words);
  const html = fs.readFileSync(path.join(assets, 'index.html'), 'utf8');
  const order = ['i18n-words.js', 'i18n.js', 'data.js', 'app.js'].map(f => html.indexOf(`src="${f}`));
  assert.ok(order.every(i => i > 0), 'all scripts are on the page');
  assert.deepEqual([...order].sort((x, y) => x - y), order, 'the words load first, the app last');
});

test('t() swaps known texts, fills in values and leaves the rest alone', () => {
  try {
    i18n.setLang('hinglish');
    assert.equal(i18n.lang(), 'hinglish');
    assert.equal(i18n.t('+ Add device'), words.hinglish['+ Add device']);
    // Spaces around the text stay where they were.
    assert.equal(i18n.t('  + Add device '), '  ' + words.hinglish['+ Add device'] + ' ');
    // A text with a value in it matches its template.
    assert.equal(i18n.t('Connected to DESKTOP-1'), 'DESKTOP-1 se jude hue');
    assert.equal(i18n.t('Offline · last update 10:42 (5 min ago)'), 'Offline · aakhri update 10:42 (5 min pehle)');
    // Short texts joined with " · " are translated part by part; unknown parts (names) stay.
    assert.equal(i18n.t('DESKTOP-1 · Online now'), 'DESKTOP-1 · Abhi online');
    assert.equal(i18n.t('Online now · Connected to DESKTOP-1'), 'Abhi online · DESKTOP-1 se jude hue');
    // A value with "$" in it is copied as it is (not read as a replace pattern).
    assert.equal(i18n.t('Connected to $1-PC$&'), '$1-PC$& se jude hue');
    // Not known: unchanged. Nothing: nothing.
    assert.equal(i18n.t('holiday-photo.jpg'), 'holiday-photo.jpg');
    assert.equal(i18n.t('notes.txt · 12 KB'), 'notes.txt · 12 KB');
    assert.equal(i18n.t(''), '');
    assert.equal(i18n.t(null), null);

    i18n.setLang('hindi');
    assert.equal(i18n.t('Connected to DESKTOP-1'), words.hindi['Connected to {0}'].replace('{0}', 'DESKTOP-1'));
    const hi = (key, ...values) => values.reduce((s, v, i) => s.replace('{' + i + '}', v), words.hindi[key]);
    // A value that is itself a text of the app is translated too; a one-word value (a name) is not.
    assert.equal(i18n.t('Last seen 1 h ago'), hi('Last seen {0}', hi('{0} h ago', '1')));
    assert.equal(i18n.t('Connected to Online'), hi('Connected to {0}', 'Online'));
    // "{0} files" does not take "1 folder" with it: each part on its own.
    assert.equal(i18n.t('1 folder · 9 files'), words.hindi['1 folder'] + ' · ' + hi('{0} files', '9'));
    assert.equal(i18n.t('2 kept on this phone'), hi('{0} kept on this phone', '2'));
    assert.equal(i18n.t('+ Add device'), words.hindi['+ Add device']);

    i18n.setLang('english');
    assert.equal(i18n.t('Connected to DESKTOP-1'), 'Connected to DESKTOP-1');

    // Something that is not a language changes nothing.
    i18n.setLang('klingon');
    assert.equal(i18n.lang(), 'english');
  } finally {
    i18n.setLang('hinglish');
  }
});

test('every fixed text on the page has a translation', () => {
  const html = fs.readFileSync(path.join(assets, 'index.html'), 'utf8')
    .replace(/<script[\s\S]*?<\/script>/g, '').replace(/<style[\s\S]*?<\/style>/g, '').replace(/<svg[\s\S]*?<\/svg>/g, '');
  const texts = new Set();
  for (const m of html.matchAll(/>([^<>]+)</g)) texts.add(m[1].replace(/\s+/g, ' ').trim().replace(/&amp;/g, '&'));
  for (const m of html.matchAll(/(?:placeholder|title|aria-label)="([^"]+)"/g)) texts.add(m[1].trim().replace(/&amp;/g, '&'));
  // Names and symbols read the same in every language.
  const same = /^(Cloud-SSD|Cloud SSD|PC|OK|Wi-Fi|USB|[^A-Za-z]*)$/;
  const missing = [...texts].filter(s => /[A-Za-z]{2}/.test(s) && !same.test(s) && !words.hinglish[s]);
  assert.deepEqual(missing, []);
});

test("the phone's own notes (Tr.java) have both languages and keep every {n}", () => {
  const java = fs.readFileSync(path.join(assets, '..', 'java', 'com', 'cloudssd', 'mobile', 'Tr.java'), 'utf8');
  const str = '"((?:[^"\\\\]|\\\\.)*)"';
  const puts = [...java.matchAll(new RegExp(`put\\(\\s*${str},\\s*${str},\\s*${str}\\s*\\)`, 'g'))];
  assert.ok(puts.length >= 10, 'found the texts');
  for (const [, english, hinglish, hindi] of puts) {
    // Hinglish may keep an English word as it is ("Camera backup"); Hindi always differs.
    assert.ok(hinglish.trim() && hindi.trim() && hindi !== english, `"${english}" is translated`);
    for (const out of [hinglish, hindi]) assert.equal(holes(out), holes(english), `"${english}" → "${out}"`);
  }
  // Every text the notes and messages ask for is in the list.
  const known = new Set(puts.map(p => p[1]));
  const dir = path.join(assets, '..', 'java', 'com', 'cloudssd', 'mobile');
  let uses = 0;
  for (const f of fs.readdirSync(dir).filter(f => f.endsWith('.java') && f !== 'Tr.java')) {
    const src = fs.readFileSync(path.join(dir, f), 'utf8');
    for (const m of src.matchAll(new RegExp(`Tr\\.t\\([^,]+,\\s*(?:[^"(),]+\\?\\s*)?${str}(?:\\s*:\\s*${str})?`, 'g'))) {
      for (const used of [m[1], m[2]].filter(Boolean)) { uses++; assert.ok(known.has(used), `${f}: "${used}" is not in Tr.java`); }
    }
    // toast("…") translates its fixed text itself.
    for (const m of src.matchAll(new RegExp(`\\btoast\\(${str}\\)`, 'g'))) { uses++; assert.ok(known.has(m[1]), `${f}: toast "${m[1]}" is not in Tr.java`); }
  }
  assert.ok(uses >= 40, `found ${uses} texts in use`);
});
