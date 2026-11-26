/*
 * The phone app's words in English, Hinglish or हिन्दी. The page is written in English; whatever shows on screen is
 * looked up here (the English text is the key, as in the PC app) and swapped in place — text, placeholders, tooltips —
 * also for what the page adds later (a MutationObserver translates it before the screen is drawn). Texts with values
 * in them ("Connected to DESKTOP-1") match a template ("Connected to {0}"). What is not in the lists stays English.
 * File names and other things people typed are never touched. The words themselves are in i18n-words.js.
 */
(function (root) {
  'use strict';
  const STORE = 'cloudssd_lang';
  const LANGS = { english: 'English', hinglish: 'Hinglish', hindi: 'हिन्दी' };
  const words = root.CloudSsdWords || { hinglish: {}, hindi: {} };

  function pick() {
    try {
      const saved = root.localStorage && root.localStorage.getItem(STORE);
      if (saved && LANGS[saved]) return saved;
    } catch (e) { /* no storage: the default */ }
    const nav = String((root.navigator && root.navigator.language) || '').toLowerCase();
    // Like the PC app: Hinglish unless the phone itself is in Hindi.
    return nav.startsWith('hi') ? 'hindi' : 'hinglish';
  }
  let current = pick();

  /** Templates with values ({0}…) as regular expressions, per language, longest first. */
  const compiled = {};
  function patterns(lang) {
    if (compiled[lang]) return compiled[lang];
    const list = [];
    const src = words[lang] || {};
    for (const key of Object.keys(src)) {
      if (!/\{\d\}/.test(key)) continue;
      // "{0}" or "{0} · {1}" alone would match every text.
      if (!/[A-Za-zऀ-ॿ]{2}/.test(key.replace(/\{\d\}/g, ''))) continue;
      const re = new RegExp('^' + key.replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/\\\{(\d)\\\}/g, '(.+?)') + '$');
      list.push({ re, out: src[key], len: key.length, joined: key.includes(' · ') });
    }
    list.sort((a, b) => b.len - a.len);
    return (compiled[lang] = list);
  }

  /** The translation of one text: its exact words, else the longest template it fits (values translated too). */
  function lookup(key, inner) {
    const hit = (words[current] || {})[key];
    if (hit !== undefined) return hit;
    const joined = key.includes(' · ');
    for (const p of patterns(current)) {
      const m = p.re.exec(key);
      if (!m) continue;
      // A value does not reach over " · " into the next part, unless the template has parts too.
      if (joined && !p.joined && m.slice(1).some(v => v && v.includes(' · '))) continue;
      return p.out.replace(/\{(\d)\}/g, (_, i) => {
        const v = m[+i + 1];
        if (v === undefined) return '';
        // A value that is itself one of the app's texts ("1 h ago", "No cloud drive connected"); one word (a name
        // like "Home") stays as it is.
        if (inner || !/ /.test(v.trim()) || !/[A-Za-z]{2}/.test(v)) return v;
        const h = lookup(v.trim(), true);
        return h === undefined ? v : v.replace(v.trim(), () => h);
      });
    }
    return undefined;
  }

  /** What each text became, per language (the page redraws the same texts again and again). */
  const memo = {};

  /** The text in the language chosen (the English text when there is no translation for it). */
  function t(text) {
    if (current === 'english' || text == null) return text;
    const s = String(text);
    const key = s.trim();
    if (!key) return s;
    const seen = memo[current] || (memo[current] = new Map());
    let hit = seen.get(key);
    if (hit === undefined) {
      hit = lookup(key);
      // "DESKTOP-1 · connected · 2 PCs": the page joins short texts with " · "; each part on its own.
      if (hit === undefined && key.includes(' · ')) {
        const parts = key.split(' · ');
        const outs = parts.map(p => { const k = p.trim(), h = k ? lookup(k) : undefined; return h === undefined ? p : p.replace(k, () => h); });
        if (outs.some((o, i) => o !== parts[i])) hit = outs.join(' · ');
      }
      if (hit === undefined) hit = null;
      if (seen.size > 5000) seen.clear();
      seen.set(key, hit);
    }
    return hit === null ? s : s.replace(key, () => hit);
  }

  // ---- the screen ------------------------------------------------------------------------------------------------
  const ORIGINAL = new WeakMap();     // text node → its English text
  const SHOWN = new WeakMap();        // text node → what this put there last (to tell it from a change by the page)
  const ATTR_ORIGINAL = new WeakMap(); // element → {placeholder: English, _placeholder: shown, …}
  const ATTRS = ['placeholder', 'title', 'aria-label'];
  // What people named or typed (file names, the text viewer, inputs) is never translated.
  const SKIP = '.item-name, [data-no-i18n], script, style, pre, code, textarea, #viewer-text-content, #sheet-file-name, ' +
    '.device-row-title, .drive-card-name, .inbox-names, .inbox-title, .offline-row-name, .phone-item-name, .tc-name, .thumb-tile-name';

  function skipped(el) { return !el || (el.closest && el.closest(SKIP)); }

  function doText(node) {
    const parent = node.parentElement;
    if (!parent || skipped(parent)) return;
    const now = node.nodeValue;
    let orig = ORIGINAL.get(node);
    if (orig === undefined || (now !== orig && now !== SHOWN.get(node))) { orig = now; ORIGINAL.set(node, orig); }
    const out = t(orig);
    SHOWN.set(node, out);
    if (out !== now) node.nodeValue = out;
  }

  function doAttrs(el) {
    if (!el.hasAttribute) return;
    for (const a of ATTRS) {
      if (!el.hasAttribute(a)) continue;
      const m = ATTR_ORIGINAL.get(el) || {};
      const now = el.getAttribute(a);
      if (m[a] === undefined || (now !== m[a] && now !== m['_' + a])) m[a] = now;
      const out = t(m[a]);
      m['_' + a] = out;
      ATTR_ORIGINAL.set(el, m);
      if (out !== now) el.setAttribute(a, out);
    }
  }

  function walk(node) {
    if (!node) return;
    if (node.nodeType === 3) { doText(node); return; }
    if (node.nodeType !== 1 || skipped(node)) return;
    doAttrs(node);
    const doc = node.ownerDocument;
    const tw = doc.createTreeWalker(node, 5 /* elements and text */, {
      acceptNode: n => n.nodeType === 1 && skipped(n) ? 2 /* reject */ : 1 /* accept */
    });
    let n;
    while ((n = tw.nextNode())) { if (n.nodeType === 3) doText(n); else doAttrs(n); }
  }

  function start() {
    const doc = root.document;
    doc.documentElement.lang = current === 'hindi' ? 'hi' : current === 'hinglish' ? 'hi-Latn' : 'en';
    walk(doc.body);
    new root.MutationObserver(list => {
      for (const m of list) {
        if (m.type === 'characterData') doText(m.target);
        else if (m.type === 'attributes') doAttrs(m.target);
        else m.addedNodes.forEach(walk);
      }
    }).observe(doc.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ATTRS });
  }

  /** Switches the language and redraws every text from its English. */
  function setLang(lang) {
    if (!LANGS[lang]) return;
    current = lang;
    try { root.localStorage.setItem(STORE, lang); } catch (e) { /* only for this visit */ }
    if (root.document && root.document.body) {
      root.document.documentElement.lang = lang === 'hindi' ? 'hi' : lang === 'hinglish' ? 'hi-Latn' : 'en';
      walk(root.document.body);
    }
  }

  /** The page set a text node to the translation of <english> itself (setText): remember the English behind it. */
  function remember(node, english) {
    if (node && node.nodeType === 3) { ORIGINAL.set(node, english); SHOWN.set(node, node.nodeValue); }
  }

  const api = { t, lang: () => current, setLang, LANGS, remember, words };
  root.CloudSsdI18n = api;
  if (root.document && root.MutationObserver) {
    if (root.document.readyState === 'loading') root.document.addEventListener('DOMContentLoaded', start);
    else start();
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
