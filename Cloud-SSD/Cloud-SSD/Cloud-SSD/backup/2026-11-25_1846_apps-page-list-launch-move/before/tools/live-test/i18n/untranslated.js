// Every human text in the phone page's scripts: quoted strings and template literals (their ${…} become {0}, {1}…),
// split at HTML tags, that has no Hinglish/Hindi yet in i18n-words.js. Usage: node untranslated.js <assetsDir> <out.json>
// Then add the new texts to i18n-words.js (both languages; tests/mobile-js/i18n.test.js checks them).
const fs = require('fs');
const [dir, outFile] = process.argv.slice(2);
const have = new Set(Object.keys(require(require('path').resolve(dir, 'i18n-words.js')).hinglish));

/** Scans JS source; calls found(text) for each string literal (templates with {n} holes), recursing into ${…}. */
function scan(src, found) {
  let i = 0;
  const n = src.length;
  let lastSig = '';
  function readQuoted(q) {
    let s = '';
    i++;
    while (i < n && src[i] !== q) {
      if (src[i] === '\\') { s += src[i + 1] === 'n' ? '\n' : src[i + 1]; i += 2; continue; }
      if (src[i] === '\n') break;
      s += src[i++];
    }
    i++;
    return s;
  }
  function readTemplate() {
    // returns text with {k} for each ${…}; scans the expressions too
    let s = '', k = 0;
    i++;
    while (i < n && src[i] !== '`') {
      if (src[i] === '\\') { s += src[i + 1]; i += 2; continue; }
      if (src[i] === '$' && src[i + 1] === '{') {
        i += 2;
        const start = i;
        let depth = 1;
        while (i < n && depth > 0) {
          const c = src[i];
          if (c === '\'' || c === '"') { readQuoted(c); continue; }
          if (c === '`') { readTemplate(); continue; }
          if (c === '{') depth++;
          else if (c === '}') { depth--; if (depth === 0) break; }
          i++;
        }
        scan(src.slice(start, i), found);
        i++;
        s += '{' + (k++) + '}';
        continue;
      }
      s += src[i++];
    }
    i++;
    return s;
  }
  while (i < n) {
    const c = src[i];
    if (c === '/' && src[i + 1] === '/') { while (i < n && src[i] !== '\n') i++; continue; }
    if (c === '/' && src[i + 1] === '*') { const e = src.indexOf('*/', i + 2); i = e < 0 ? n : e + 2; continue; }
    if (c === '\'' || c === '"') { found(readQuoted(c)); lastSig = 'x'; continue; }
    if (c === '`') { found(readTemplate()); lastSig = 'x'; continue; }
    if (c === '/' && (lastSig === '' || '(,=:[!&|?{};+-*%<>~^'.includes(lastSig))) {
      // a regular expression
      i++;
      let cls = false;
      while (i < n && src[i] !== '\n') {
        if (src[i] === '\\') { i += 2; continue; }
        if (src[i] === '[') cls = true; else if (src[i] === ']') cls = false;
        else if (src[i] === '/' && !cls) break;
        i++;
      }
      i++;
      lastSig = 'x';
      continue;
    }
    if (!/\s/.test(c)) lastSig = /[A-Za-z0-9_$)\]]/.test(c) ? 'x' : c;
    if (/[A-Za-z0-9_$]/.test(c)) { while (i < n && /[A-Za-z0-9_$]/.test(src[i])) i++; lastSig = 'x'; continue; }
    i++;
  }
}

function pieces(text) {
  // split at tags, then renumber the holes in each piece from {0}
  return text.replace(/&nbsp;/g, ' ').split(/<[^<>]*>/).map(p => {
    let k = 0;
    const map = {};
    return p.replace(/\{(\d+)\}/g, (_, d) => { if (!(d in map)) map[d] = k++; return '{' + map[d] + '}'; })
      .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&')
      .replace(/\s+/g, ' ').trim();
  });
}

function human(s) {
  if (!s || s.length < 2 || s.length > 300) return false;
  const letters = s.replace(/\{\d+\}/g, '');
  if (!/[A-Za-z]{2}/.test(letters)) return false;
  if (/^[a-z0-9_$.:\-\/#\[\]="'*>+~ ]+$/.test(s) && !/ [a-z]{2,} [a-z]{2,}/.test(s)) return false; // ids, classes, selectors
  if (/[{};]\s*$|=>|===|&&|\|\||\bfunction\b|\breturn\b|^\s*[.#\[]|\bvar\(|\bpx\b|:\s*\d+px|rgba?\(|^data:|^https?:|:\/\/|\.(js|css|png|svg|json|html)\b|^\/|\\u/.test(s)) return false;
  if (/^[A-Za-z]+\.[A-Za-z.]+$/.test(s)) return false; // dotted names
  if (/^[a-z]+([A-Z][a-z0-9]*)+$/.test(s)) return false; // camelCase
  if (/^[A-Z0-9_]+$/.test(s) && s.length > 1 && !/^[A-Z]{2,}( [A-Z]+)*$/.test(s)) return false;
  if (/^(GET|POST|PUT|DELETE|HEAD|OPTIONS)$/.test(s)) return false;
  if (/^[a-z]/.test(s) && !/ /.test(s)) return false; // single lower-case word: a key
  return true;
}

const out = new Map();
for (const f of ['app.js', 'fs.js', 'space.js', 'stream.js', 'data.js']) {
  const src = fs.readFileSync(dir + '/' + f, 'utf8');
  scan(src, raw => {
    for (const p of pieces(raw)) {
      if (!human(p) || have.has(p)) continue;
      if (!out.has(p)) out.set(p, f);
    }
  });
}
const list = [...out.keys()].sort();
fs.writeFileSync(outFile, JSON.stringify(list, null, 1));
console.log(list.length + ' new texts');
