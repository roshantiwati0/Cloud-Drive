/*
 * Cloud-SSD phone page: data helpers with no screen code, so they can be tested on their own (node --test).
 *   CloudSsdData.crypto — SHA-256 and HMAC-SHA256 in plain JavaScript. A page opened from the PC over http:// is not a
 *   "secure context", so the browser's crypto.subtle is missing there; this works everywhere.
 *   CloudSsdData.helloProof — the answer the PC must give to /api/hello before this phone sends its key to an address.
 */
(function (root) {
  'use strict';

  // ------------------------------------------------------------------ SHA-256 (FIPS 180-4)
  const K = new Uint32Array([
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
    0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
    0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
    0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
    0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
    0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
  ]);

  /** UTF-8 bytes of a string. */
  function utf8(s) {
    if (typeof TextEncoder !== 'undefined') return new TextEncoder().encode(String(s));
    const out = [];
    const str = unescape(encodeURIComponent(String(s)));
    for (let i = 0; i < str.length; i++) out.push(str.charCodeAt(i));
    return new Uint8Array(out);
  }

  /** SHA-256 of bytes (Uint8Array or array of 0-255), as a 32-byte Uint8Array. */
  function sha256(bytes) {
    const msg = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
    const bitLen = msg.length * 8;
    const padded = new Uint8Array(((msg.length + 9 + 63) >> 6) << 6);
    padded.set(msg);
    padded[msg.length] = 0x80;
    const view = new DataView(padded.buffer);
    view.setUint32(padded.length - 8, Math.floor(bitLen / 0x100000000), false);
    view.setUint32(padded.length - 4, bitLen >>> 0, false);
    const H = new Uint32Array([0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19]);
    const W = new Uint32Array(64);
    for (let off = 0; off < padded.length; off += 64) {
      for (let i = 0; i < 16; i++) W[i] = view.getUint32(off + i * 4, false);
      for (let i = 16; i < 64; i++) {
        const w15 = W[i - 15], w2 = W[i - 2];
        const s0 = ((w15 >>> 7) | (w15 << 25)) ^ ((w15 >>> 18) | (w15 << 14)) ^ (w15 >>> 3);
        const s1 = ((w2 >>> 17) | (w2 << 15)) ^ ((w2 >>> 19) | (w2 << 13)) ^ (w2 >>> 10);
        W[i] = (W[i - 16] + s0 + W[i - 7] + s1) >>> 0;
      }
      let a = H[0], b = H[1], c = H[2], d = H[3], e = H[4], f = H[5], g = H[6], h = H[7];
      for (let i = 0; i < 64; i++) {
        const S1 = ((e >>> 6) | (e << 26)) ^ ((e >>> 11) | (e << 21)) ^ ((e >>> 25) | (e << 7));
        const ch = (e & f) ^ (~e & g);
        const t1 = (h + S1 + ch + K[i] + W[i]) >>> 0;
        const S0 = ((a >>> 2) | (a << 30)) ^ ((a >>> 13) | (a << 19)) ^ ((a >>> 22) | (a << 10));
        const maj = (a & b) ^ (a & c) ^ (b & c);
        const t2 = (S0 + maj) >>> 0;
        h = g; g = f; f = e; e = (d + t1) >>> 0; d = c; c = b; b = a; a = (t1 + t2) >>> 0;
      }
      H[0] = (H[0] + a) >>> 0; H[1] = (H[1] + b) >>> 0; H[2] = (H[2] + c) >>> 0; H[3] = (H[3] + d) >>> 0;
      H[4] = (H[4] + e) >>> 0; H[5] = (H[5] + f) >>> 0; H[6] = (H[6] + g) >>> 0; H[7] = (H[7] + h) >>> 0;
    }
    const out = new Uint8Array(32);
    const ov = new DataView(out.buffer);
    for (let i = 0; i < 8; i++) ov.setUint32(i * 4, H[i], false);
    return out;
  }

  /** HMAC-SHA256 (RFC 2104) of message bytes with key bytes, as a 32-byte Uint8Array. */
  function hmacSha256(key, message) {
    let k = key instanceof Uint8Array ? key : new Uint8Array(key);
    if (k.length > 64) k = sha256(k);
    const block = new Uint8Array(64);
    block.set(k);
    const inner = new Uint8Array(64 + message.length);
    const outer = new Uint8Array(64 + 32);
    for (let i = 0; i < 64; i++) { inner[i] = block[i] ^ 0x36; outer[i] = block[i] ^ 0x5c; }
    inner.set(message, 64);
    outer.set(sha256(inner), 64);
    return sha256(outer);
  }

  function hex(bytes) {
    let s = '';
    for (let i = 0; i < bytes.length; i++) s += (bytes[i] < 16 ? '0' : '') + bytes[i].toString(16);
    return s;
  }

  /** A random nonce of n bytes as hex (crypto.getRandomValues where available). */
  function randomHex(n) {
    const b = new Uint8Array(n || 16);
    const c = (typeof crypto !== 'undefined' && crypto.getRandomValues) ? crypto : null;
    if (c) c.getRandomValues(b); else for (let i = 0; i < b.length; i++) b[i] = Math.floor(Math.random() * 256);
    return hex(b);
  }

  /** Constant-time comparison of two strings of the same length. */
  function sameText(a, b) {
    a = String(a || ''); b = String(b || '');
    if (a.length !== b.length) return false;
    let diff = 0;
    for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
    return diff === 0;
  }

  /**
   * The proof the PC gives on /api/hello: HMAC-SHA256(SHA-256(key), nonce + "\n" + pcId) in lowercase hex. Only the
   * real PC knows the hash of this phone's key, so only it can answer a fresh nonce correctly.
   */
  function helloProof(token, nonce, pcId) {
    return hex(hmacSha256(sha256(utf8(token)), utf8(String(nonce) + '\n' + String(pcId))));
  }

  // ------------------------------------------------------------------ "last time" data: pure helpers
  const DAY = 24 * 3600 * 1000;
  /** How long a NEW badge stays on a file that appeared since the last visit. */
  const NEW_FOR_MS = DAY;

  /**
   * Names in {@code next} that were not in {@code prev}: the files and folders that appeared on the PC since this
   * folder was last saved. The first visit has nothing to compare with, so nothing is new then. Badges from the last
   * refresh carry over for a day, so a quick second refresh does not wipe them.
   */
  function diffNew(prev, next, now) {
    if (!prev) return { newNames: [], newAt: 0 };
    const before = new Set();
    (prev.folders || []).forEach(f => before.add('d:' + f.name));
    (prev.files || []).forEach(f => before.add('f:' + f.name));
    const fresh = [];
    (next.folders || []).forEach(f => { if (!before.has('d:' + f.name)) fresh.push(f.name); });
    (next.files || []).forEach(f => { if (!before.has('f:' + f.name)) fresh.push(f.name); });
    const stillThere = new Set((next.folders || []).map(f => f.name).concat((next.files || []).map(f => f.name)));
    const carried = (prev.newAt && now - prev.newAt < NEW_FOR_MS ? prev.newNames || [] : []).filter(n => stillThere.has(n));
    const names = Array.from(new Set(fresh.concat(carried)));
    return { newNames: names, newAt: fresh.length ? now : (carried.length ? prev.newAt : 0) };
  }

  /** True when the name should show a NEW badge now. */
  function isNew(rec, name, now) {
    return !!rec && !!rec.newAt && now - rec.newAt < NEW_FOR_MS && (rec.newNames || []).indexOf(name) >= 0;
  }

  /**
   * Which saved folders to forget so at most {@code maxLists} remain and their size stays under {@code maxBytes}:
   * the least recently used first, never the one in {@code keep}.
   */
  function pickEvictions(metas, caps, keep) {
    const maxLists = caps && caps.maxLists || 1500, maxBytes = caps && caps.maxBytes || 40e6;
    const sorted = metas.slice().sort((a, b) => (a.usedAt || 0) - (b.usedAt || 0));
    let count = sorted.length, bytes = sorted.reduce((s, m) => s + (m.size || 0), 0);
    const out = [];
    for (const m of sorted) {
      if (count <= maxLists && bytes <= maxBytes) break;
      if (m.k === keep) continue;
      out.push(m.k);
      count--; bytes -= (m.size || 0);
    }
    return out;
  }

  function pad2(n) { return (n < 10 ? '0' : '') + n; }
  /** "10:42 (5 min ago)", "yesterday 18:05 (20 h ago)", "3 Nov 18:05 (9 days ago)". */
  function fmtLastUpdate(ts, now) {
    if (!ts) return 'earlier';
    const d = new Date(ts), n = new Date(now);
    const hm = pad2(d.getHours()) + ':' + pad2(d.getMinutes());
    const s = Math.max(0, (now - ts) / 1000);
    const ago = s < 60 ? 'just now' : s < 3600 ? Math.floor(s / 60) + ' min ago' : s < 86400 ? Math.floor(s / 3600) + ' h ago' : Math.floor(s / 86400) + (s < 2 * 86400 ? ' day ago' : ' days ago');
    const sameDay = d.toDateString() === n.toDateString();
    const yesterday = new Date(now - DAY).toDateString() === d.toDateString();
    const when = sameDay ? hm : yesterday ? 'yesterday ' + hm : d.getDate() + ' ' + ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][d.getMonth()] + ' ' + hm;
    return when + ' (' + ago + ')';
  }

  /** Every word of the query appears in the name (any case, any order). */
  function matchName(q, name) {
    const words = String(q || '').toLowerCase().split(/\s+/).filter(Boolean);
    const n = String(name || '').toLowerCase();
    return words.length > 0 && words.every(w => n.indexOf(w) >= 0);
  }

  /** The key a saved folder is stored under: one PC's id and the folder's path. */
  function listKey(pc, path) { return String(pc || 'pc') + '\u0001' + String(path || ''); }

  /** Rough size of a saved folder, for the storage cap. */
  function sizeOf(rec) { try { return JSON.stringify(rec).length; } catch (e) { return 0; } }

  /** Keeps at most this many entries per folder (a folder with 50,000 files is saved cut short). */
  const MAX_ENTRIES = 10000;
  function trimRecord(data) {
    const folders = (data.folders || []).slice(0, MAX_ENTRIES);
    const files = (data.files || []).slice(0, Math.max(0, MAX_ENTRIES - folders.length));
    return { folders, files, truncated: folders.length + files.length < (data.folders || []).length + (data.files || []).length };
  }

  // ------------------------------------------------------------------ "last time" data: storage
  /**
   * Saved folder lists, per PC: IndexedDB where the WebView has it (many folders, tens of MB), else localStorage
   * (the last 12 folders, as before). Every call is safe to make and never throws; a failure just means "not saved".
   */
  function createListStore(opts) {
    opts = opts || {};
    const idb = opts.indexedDB !== undefined ? opts.indexedDB : (typeof indexedDB !== 'undefined' ? indexedDB : null);
    const storage = opts.localStorage !== undefined ? opts.localStorage : (typeof localStorage !== 'undefined' ? localStorage : null);
    const caps = opts.caps || { maxLists: 1500, maxBytes: 40e6 };
    const now = opts.now || (() => Date.now());
    let dbPromise = null;
    let mode = idb ? 'idb' : 'ls';

    function open() {
      if (mode !== 'idb') return Promise.resolve(null);
      if (dbPromise) return dbPromise;
      dbPromise = new Promise((resolve) => {
        let done = false;
        const finish = (db) => { if (!done) { done = true; if (!db) mode = 'ls'; resolve(db); } };
        setTimeout(() => finish(null), 3000);   // an old WebView that never answers: use localStorage
        try {
          const req = idb.open('cloudssd', 1);
          req.onupgradeneeded = () => {
            const db = req.result;
            if (!db.objectStoreNames.contains('lists')) db.createObjectStore('lists', { keyPath: 'k' });
            if (!db.objectStoreNames.contains('meta')) db.createObjectStore('meta', { keyPath: 'k' }).createIndex('pc', 'pc');
          };
          req.onsuccess = () => finish(req.result);
          req.onerror = () => finish(null);
          req.onblocked = () => finish(null);
        } catch (e) { finish(null); }
      });
      return dbPromise;
    }
    function tx(db, stores, kind, fn) {
      return new Promise((resolve) => {
        try {
          const t = db.transaction(stores, kind);
          let result;
          t.oncomplete = () => resolve(result);
          t.onerror = t.onabort = () => resolve(undefined);
          result = fn(t);
        } catch (e) { resolve(undefined); }
      });
    }
    function reqValue(r) { return new Promise(res => { r.onsuccess = () => res(r.result); r.onerror = () => res(undefined); }); }

    // ---- localStorage fallback: one JSON object per PC, last 12 folders, small folders only
    function lsKey(pc) { return 'cloudssd_lists:' + (pc || 'pc'); }
    function lsRead(pc) { try { return JSON.parse(storage.getItem(lsKey(pc)) || '{}') || {}; } catch (e) { return {}; } }
    function lsWrite(pc, all) {
      const entries = Object.entries(all).sort((a, b) => (a[1].usedAt || 0) - (b[1].usedAt || 0)).slice(-12)
        .filter(([, v]) => (v.folders.length + v.files.length) <= 400);
      try { storage.setItem(lsKey(pc), JSON.stringify(Object.fromEntries(entries))); } catch (e) { }
    }

    let trimTimer = null;
    function scheduleTrim(keep) {
      if (trimTimer) return;
      trimTimer = setTimeout(() => { trimTimer = null; trim(keep); }, opts.trimDelay != null ? opts.trimDelay : 10000);
    }
    async function trim(keep) {
      const db = await open();
      if (!db) return;
      const metas = await tx(db, ['meta'], 'readonly', t => reqValue(t.objectStore('meta').getAll()));
      const all = await metas;
      if (!Array.isArray(all)) return;
      const gone = pickEvictions(all, caps, keep);
      if (!gone.length) return;
      await tx(db, ['lists', 'meta'], 'readwrite', t => { gone.forEach(k => { t.objectStore('lists').delete(k); t.objectStore('meta').delete(k); }); });
    }

    return {
      get mode() { return mode; },
      ready: () => open().then(() => mode),

      /** The saved list of a folder, or null. Marks it as used (it is kept longest). */
      async get(pc, path) {
        const k = listKey(pc, path);
        const db = await open();
        if (!db) {
          if (!storage) return null;
          const all = lsRead(pc);
          return all[path] || null;
        }
        const rec = await tx(db, ['lists'], 'readonly', t => reqValue(t.objectStore('lists').get(k)));
        const value = await rec;
        if (value) tx(db, ['meta'], 'readwrite', t => { t.objectStore('meta').put({ k, pc: value.pc, path, usedAt: now(), size: value.size || 0 }); });
        return value || null;
      },

      /**
       * Saves a fresh list from the PC and returns it with its NEW names (compared with what was saved before).
       * {@code savedAt} is only given when importing an old list whose time is unknown (0).
       */
      async put(pc, path, data, savedAt) {
        const k = listKey(pc, path);
        const t0 = savedAt != null ? savedAt : now();
        const prev = await this.get(pc, path);
        const trimmed = trimRecord(data);
        const d = diffNew(prev, trimmed, t0);
        const rec = { k, pc: pc || 'pc', path, folders: trimmed.folders, files: trimmed.files, truncated: trimmed.truncated, savedAt: t0, newNames: d.newNames, newAt: d.newAt };
        rec.size = sizeOf(rec);
        const db = await open();
        const usedAt = now();
        if (!db) {
          if (storage) { const all = lsRead(pc); all[path] = Object.assign({}, rec, { usedAt }); lsWrite(pc, all); }
          return rec;
        }
        await tx(db, ['lists', 'meta'], 'readwrite', t => {
          t.objectStore('lists').put(rec);
          t.objectStore('meta').put({ k, pc: rec.pc, path, usedAt, size: rec.size });
        });
        scheduleTrim(k);
        return rec;
      },

      /** Forgets one folder (it was deleted on the PC). */
      async del(pc, path) {
        const k = listKey(pc, path);
        const db = await open();
        if (!db) { if (storage) { const all = lsRead(pc); delete all[path]; lsWrite(pc, all); } return; }
        await tx(db, ['lists', 'meta'], 'readwrite', t => { t.objectStore('lists').delete(k); t.objectStore('meta').delete(k); });
      },

      /** Forgets everything saved for one PC. */
      async removePc(pc) {
        const db = await open();
        if (!db) { try { storage && storage.removeItem(lsKey(pc)); } catch (e) { } return; }
        const metas = await (await tx(db, ['meta'], 'readonly', t => reqValue(t.objectStore('meta').index('pc').getAll(pc || 'pc'))));
        const keys = (metas || []).map(m => m.k);
        if (keys.length) await tx(db, ['lists', 'meta'], 'readwrite', t => keys.forEach(k => { t.objectStore('lists').delete(k); t.objectStore('meta').delete(k); }));
      },

      /** How many folders are saved for one PC. */
      async count(pc) {
        const db = await open();
        if (!db) return Object.keys(lsRead(pc)).length;
        const metas = await (await tx(db, ['meta'], 'readonly', t => reqValue(t.objectStore('meta').index('pc').getAll(pc || 'pc'))));
        return (metas || []).length;
      },

      /** Files and folders in the saved lists of one PC whose name matches every word of {@code q}. */
      async search(pc, q, max) {
        max = max || 150;
        const out = [];
        const push = (rec) => {
          for (const f of rec.folders || []) { if (out.length >= max) return; if (matchName(q, f.name)) out.push(Object.assign({ isFolder: true }, f)); }
          for (const f of rec.files || []) { if (out.length >= max) return; if (matchName(q, f.name)) out.push(Object.assign({ isFolder: false }, f)); }
        };
        const db = await open();
        if (!db) { Object.values(lsRead(pc)).forEach(push); return out; }
        const metas = await (await tx(db, ['meta'], 'readonly', t => reqValue(t.objectStore('meta').index('pc').getAll(pc || 'pc'))));
        for (const m of metas || []) {
          if (out.length >= max) break;
          const rec = await (await tx(db, ['lists'], 'readonly', t => reqValue(t.objectStore('lists').get(m.k))));
          if (rec) push(rec);
        }
        return out;
      },

      trimNow: (keep) => trim(keep)
    };
  }

  /** A snapshot of one PC's last answers (status, drives, history…), kept small in localStorage. */
  function createSnapshots(storage) {
    storage = storage !== undefined ? storage : (typeof localStorage !== 'undefined' ? localStorage : null);
    const key = (pc) => 'cloudssd_snap:' + (pc || 'pc');
    return {
      get(pc) { try { return JSON.parse(storage.getItem(key(pc)) || 'null'); } catch (e) { return null; } },
      set(pc, patch) {
        if (!storage || !pc) return null;
        const cur = this.get(pc) || {};
        const next = Object.assign(cur, patch);
        try { storage.setItem(key(pc), JSON.stringify(next)); } catch (e) { }
        return next;
      },
      remove(pc) { try { storage.removeItem(key(pc)); } catch (e) { } }
    };
  }

  // ------------------------------------------------------------------ the PCs this phone is paired with
  /**
   * Saved devices (PCs and laptops). In the Android app the list and every key live on the native side (keys
   * encrypted, never handed out except the active one); in a browser the list, with keys, is in localStorage as before.
   * Record: {id, name, label, kind, lan, away[], last, deviceId, features[], lastOnline, addedAt, active, hasKey}.
   */
  function createRegistry(opts) {
    opts = opts || {};
    const native = opts.native && typeof opts.native.pcList === 'function' ? opts.native : null;
    const storage = opts.storage !== undefined ? opts.storage : (typeof localStorage !== 'undefined' ? localStorage : null);
    const KEY = 'cloudssd_devices', ACTIVE = 'cloudssd_active_device';
    const readLs = () => { try { return JSON.parse(storage.getItem(KEY) || '[]') || []; } catch (e) { return []; } };
    const writeLs = (a) => { try { storage.setItem(KEY, JSON.stringify(a)); } catch (e) { } };
    const activeLs = () => { try { return storage.getItem(ACTIVE) || ''; } catch (e) { return ''; } };
    const setActiveLs = (id) => { try { if (id) storage.setItem(ACTIVE, id); else storage.removeItem(ACTIVE); } catch (e) { } };

    return {
      native: !!native,

      list() {
        if (native) { try { return JSON.parse(native.pcList() || '[]') || []; } catch (e) { return []; } }
        const act = activeLs();
        return readLs().map(d => { const c = Object.assign({}, d, { active: d.id === act, hasKey: !!d.token }); delete c.token; return c; });
      },
      get(id) { return this.list().find(d => d.id === id) || null; },
      active() { return this.list().find(d => d.active) || null; },

      /** Adds or updates {id, …}; a non-null token replaces the device's key; {replaces: oldId} renames an entry. */
      upsert(patch, token, activate) {
        if (!patch || !patch.id) return false;
        if (native) {
          const o = Object.assign({}, patch);
          if (token != null) o.token = token;
          if (activate) o.activate = true;
          try { return !!native.pcSave(JSON.stringify(o)); } catch (e) { return false; }
        }
        const all = readLs();
        if (patch.replaces && patch.replaces !== patch.id) {
          const old = all.find(d => d.id === patch.replaces);
          if (old && !all.some(d => d.id === patch.id)) old.id = patch.id;
          if (activeLs() === patch.replaces) setActiveLs(patch.id);
        }
        let rec = all.find(d => d.id === patch.id);
        if (!rec) { rec = { id: patch.id, kind: 'pc', addedAt: Date.now() }; all.push(rec); }
        Object.keys(patch).forEach(k => { if (k !== 'replaces' && k !== 'activate' && k !== 'id') rec[k] = patch[k]; });
        if (token != null) rec.token = token;
        writeLs(all);
        if (activate) setActiveLs(patch.id);
        return true;
      },

      /** Makes a saved device the active one: {ok, server, token, id}, or null. */
      activate(id) {
        if (native) { try { const r = native.pcActivate(id); return r ? JSON.parse(r) : null; } catch (e) { return null; } }
        const rec = readLs().find(d => d.id === id);
        if (!rec) return null;
        setActiveLs(id);
        return { ok: true, server: rec.last || rec.lan || '', token: rec.token || '', id };
      },

      /** Forgets a device and its key; the next saved one (if any) becomes active. Returns the new active id or ''. */
      remove(id) {
        if (native) { try { native.pcRemove(id); } catch (e) { } const a = this.active(); return a ? a.id : ''; }
        const all = readLs().filter(d => d.id !== id);
        writeLs(all);
        if (activeLs() === id) setActiveLs(all.length ? all[0].id : '');
        return activeLs();
      },

      /** Browser: the single PC older versions kept becomes the first saved device, once. */
      migrate(legacy) {
        if (native || !storage) return;
        try { if (storage.getItem(KEY) !== null) return; } catch (e) { return; }
        const lan = String(legacy && legacy.server || '').replace(/\/+$/, '');
        if (!lan) { writeLs([]); return; }
        const id = legacy.pcId || 'url:' + lan;
        writeLs([{ id, name: legacy.pcName || '', kind: 'pc', lan, last: lan, away: legacy.away || [], deviceId: legacy.deviceId || '', addedAt: Date.now(), token: legacy.token || '' }]);
        setActiveLs(id);
      }
    };
  }

  /**
   * The old single-PC list cache ("cloudssd_dircache", path → {folders, files}) as records for the new store, marked
   * as saved at an unknown time.
   */
  function migrateDirCache(old) {
    return Object.entries(old || {}).filter(([, v]) => v && Array.isArray(v.folders) && Array.isArray(v.files))
      .map(([path, v]) => ({ path, folders: v.folders, files: v.files }));
  }

  // ------------------------------------------------------------------ "Offline rakho" (kept on this phone)

  /** True when a kept item (a folder id, a drive "x:", or a file id) includes this id. */
  function covers(pin, id) {
    pin = String(pin || ''); id = String(id || '');
    if (!pin || !id) return false;
    return pin === id || id.startsWith(pin.endsWith(':') ? pin : pin + '/');
  }

  /** The pin that keeps this id (itself or a folder above it), or null. */
  function pinFor(pins, id) { return (pins || []).find(p => covers(p.id, id)) || null; }

  /** The kept items that lie inside this id (a folder above them), so the way down to them can be shown. */
  function pinsBelow(pins, id) { return (pins || []).filter(p => p.id !== id && covers(id, p.id)); }

  /**
   * A place on one of the PC's own disks as a drive id: "C:\Users\me\Documents" → "disk-c:Users/me/Documents".
   * The PC serves such a drive read-only, so a folder of it can be kept on this phone. null for anything else.
   */
  function diskId(path) {
    const m = /^([A-Za-z]):(?:[\\/](.*))?$/.exec(String(path || ''));
    return m ? 'disk-' + m[1].toLowerCase() + ':' + (m[2] || '').split(/[\\/]+/).filter(Boolean).join('/') : null;
  }
  function isDiskId(id) { return /^disk-[a-z]:/i.test(String(id || '')); }
  /** And back: "disk-c:Users/me/Documents" → "C:\Users\me\Documents" ("disk-c:" → "C:\"). null for another id. */
  function diskPath(id) {
    const m = /^disk-([a-z]):(.*)$/i.exec(String(id || ''));
    return m ? m[1].toUpperCase() + ':\\' + m[2].split('/').filter(Boolean).join('\\') : null;
  }

  /** As in the app (OfflinePins): one kept folder holds at most this many files, and this much always stays free. */
  const KEEP_MAX_FILES = 5000, KEEP_RESERVE = 500 * 1024 * 1024;
  /**
   * Why a folder cannot be kept on this phone as it is, or '' when it can. usage = {files, bytes} as the PC counted
   * it; freeBytes = room on this phone (0 = not known: the app still checks before it fetches).
   */
  function keepTooBig(usage, freeBytes) {
    if (!usage || usage.failed) return '';
    if (usage.files > KEEP_MAX_FILES) return 'Too many files (' + usage.files + '): keep a smaller folder inside it';
    if (freeBytes > 0 && usage.bytes > freeBytes - KEEP_RESERVE) return 'Not enough room on this phone';
    return '';
  }

  function msOf(t) { return typeof t === 'number' ? t : (Date.parse(t || '') || 0); }

  /** The kept copy ([size, modified]) is this version of the file: same size, and same time when both are known. */
  function sameCopy(entry, f) {
    if (!entry || !f) return false;
    if (f.size > 0 && entry[0] !== f.size) return false;
    const a = entry[1] || 0, b = msOf(f.modified);
    return !(a && b && Math.abs(a - b) > 2000);
  }

  function fmtSize(b) {
    b = Math.max(0, +b || 0);
    const u = ['B', 'KB', 'MB', 'GB', 'TB'];
    let i = 0;
    while (b >= 1024 && i < u.length - 1) { b /= 1024; i++; }
    return (i === 0 ? String(b) : b.toFixed(b < 10 ? 1 : 0)) + ' ' + u[i];
  }

  /**
   * The list of a kept folder made from the copies on this phone (files: id → [size, modified]), for when the PC is
   * away and the folder was never opened here. toPath turns an id into the page's path for it.
   */
  function keptListing(files, folderId, toPath) {
    const prefix = folderId.endsWith(':') ? folderId : folderId + '/';
    const folders = new Map();
    const list = [];
    for (const id of Object.keys(files || {})) {
      if (!id.startsWith(prefix)) continue;
      const rest = id.slice(prefix.length);
      const slash = rest.indexOf('/');
      if (slash < 0) list.push({ name: rest, path: toPath(id), size: files[id][0], modified: files[id][1] || 0 });
      else {
        const name = rest.slice(0, slash);
        if (!folders.has(name)) folders.set(name, { name, path: toPath(prefix + name) });
      }
    }
    return { folders: Array.from(folders.values()), files: list };
  }

  /** One line for a kept folder or file: where it stands. */
  function pinStatus(p, now) {
    const n = p.files || 0;
    switch (p.state) {
      case 'done': return 'On this phone · ' + (p.dir === false ? '' : n + (n === 1 ? ' file' : ' files') + ' · ') + fmtSize(p.bytes) + ' · updated ' + fmtLastUpdate(p.synced, now);
      case 'syncing': return 'Getting files' + (n ? ' · ' + (p.doneFiles || 0) + ' of ' + n : '') + (p.bytes ? ' · ' + Math.min(100, Math.floor(100 * (p.doneBytes || 0) / p.bytes)) + '%' : '…');
      case 'wifi': return 'Waits for Wi-Fi (or allow mobile data)';
      case 'error': return p.error || 'Could not keep it';
      default: return 'Waits for the PC';
    }
  }

  /** What the PC may see on this phone ({scope, until, write} from the app), in words. */
  function peerText(st, now) {
    if (!st || !st.scope) return 'Not allowed';
    const what = (st.scope === 'all' ? 'Whole phone storage' : 'Photos, downloads, documents and music') + (st.write ? ' · can add files' : '');
    if (!st.until) return what + ' · until you stop it';
    if (st.until <= now) return 'Not allowed';
    const d = new Date(st.until);
    return what + ' · until ' + pad2(d.getHours()) + ':' + pad2(d.getMinutes());
  }

  /** A sender's name made safe as a folder name ("My PC/1" → "My PC_1"); "PC" when nothing is left. */
  function safeFolder(name) {
    const s = String(name || '').replace(/[\\/:*?"<>|\u0000-\u001f]/g, '_').replace(/^[. ]+|[. ]+$/g, '').slice(0, 40).trim();
    return s && s !== '..' ? s : 'PC';
  }

  // ------------------------------------------------------------------ folders sent (a project…)

  /**
   * Where one file of an offer is saved, inside Downloads: the base ("Cloud-SSD/Received/DESKTOP") plus its place in the
   * folder that was sent ("MyApp/src"), with the top folder renamed when one of that name was there ("MyApp (2)").
   * Every part is cleaned and ".." is dropped: the sender never picks where outside the base a file lands.
   */
  function shareSavePath(base, folder, renames) {
    const parts = String(folder || '').replace(/\\/g, '/').split('/')
      .map(p => p.replace(/[:*?"<>|\u0000-\u001f]/g, '_').replace(/^[. ]+|[. ]+$/g, '').trim())
      .filter(p => p && p !== '..');
    if (!parts.length) return base;
    if (renames && renames[parts[0]]) parts[0] = renames[parts[0]];
    return base + '/' + parts.slice(0, 24).join('/');
  }

  /** "MyApp/ and 2 files" style summary of an offer's items: folders sent once by name, loose files by name. */
  function offerSummary(items, max) {
    const tops = [], loose = [];
    (items || []).forEach(it => {
      const top = String(it.folder || '').split('/')[0];
      if (top) { if (!tops.includes(top)) tops.push(top); } else loose.push(it.name);
    });
    const all = tops.map(t => t + '/').concat(loose);
    const n = max || 4;
    return { folders: tops, text: all.slice(0, n).join(', ') + (all.length > n ? ` and ${all.length - n} more` : '') };
  }

  // ------------------------------------------------------------------ files when it is off

  /**
   * How "Files when it is off" stands with a PC, from its status ({pc, device, active, guest}): 'none' (an older PC, or
   * a guest: not offered), 'off' (nobody said yes), 'waiting-pc' (this phone said yes, the PC has not), 'waiting-phone'
   * (the PC said yes, this phone has not), 'on' (both said yes).
   */
  function whenOffState(w) {
    if (!w || typeof w !== 'object' || w.guest) return 'none';
    if (w.active || (w.pc && w.device)) return 'on';
    if (w.device) return 'waiting-pc';
    if (w.pc) return 'waiting-phone';
    return 'off';
  }

  /**
   * Once both said yes: what is still missing before this phone can open the PC's copy while the PC is off.
   * 'shared-key' (the PC has no Google key of its own to hand over), 'sign-in' (no Google account is signed in on
   * this phone yet), 'ready'. '' while it is not on.
   */
  function whenOffReady(w, drive) {
    if (whenOffState(w) !== 'on') return '';
    const d = drive || {};
    const signedIn = (d.accounts || []).some(a => a && !a.needsReauth);
    if (d.available && signedIn) return 'ready';
    if (!d.available && !w.key) return 'shared-key';
    return 'sign-in';
  }

  function whenOffReadyText(ready, pcName) {
    const pc = pcName || 'the PC';
    switch (ready) {
      case 'shared-key': return pc + ' still signs in to Google with the shared key. On the PC open Cloud Drives and switch to your own Google key; then this phone can open its files while it is off.';
      case 'sign-in': return 'One step left: sign in to Google on this phone, so the files of ' + pc + ' open while it is off.';
      case 'ready': return 'Ready. While ' + pc + ' is off, its files are in the Cloud SSD tab as "' + pc + ' (cloud copy)".';
      default: return '';
    }
  }

  /**
   * The line above a PC's copy: "My PC is off or away · cloud copy up to 14 Nov, 14:32 · 41% copied so far", from what
   * the PC last wrote about its copy (device.json). Parts that are not known are left out.
   */
  function copyNote(info, pcName, fmtTime) {
    const dev = info && info.device;
    const parts = [(pcName || 'PC') + ' is off or away'];
    const at = dev && Date.parse(dev.lastSync);
    parts.push(at ? 'cloud copy up to ' + fmtTime(at) : 'cloud copy');
    if (dev && dev.bytes > 0) {
      const pct = Math.floor(100 * (dev.uploadedBytes || 0) / dev.bytes);
      if (pct < 100) parts.push(pct + '% copied so far');
    }
    return parts.join(' · ');
  }

  /** The line under the switch for each state (pcName as written on the PC). */
  function whenOffText(state, pcName) {
    const pc = pcName || 'the PC';
    switch (state) {
      case 'on': return 'On: this phone and ' + pc + ' both said yes';
      case 'waiting-pc': return 'Waiting for a yes on ' + pc;
      case 'waiting-phone': return pc + ' said yes · waiting for a yes here';
      default: return 'Off';
    }
  }

  // ------------------------------------------------------------------ transfers as tasks

  /** The model's link (wifi, usb, hotspot, tailscale) from how the page reaches the PC (route) and what the PC saw (kind). */
  function taskLink(route, kind) {
    if (route === 'tailnet' || kind === 'internet') return 'tailscale';
    if (kind === 'usb' || kind === 'usb-adb') return 'usb';
    if (kind === 'pc-hotspot' || kind === 'phone-hotspot') return 'hotspot';
    return 'wifi';
  }

  /** The task list with this task in it (replaced when known, else first), newest first, at most max. */
  function upsertTask(list, task, max) {
    if (!task || !task.id) return list || [];
    const out = (list || []).filter(t => t.id !== task.id);
    out.unshift(task);
    out.sort((a, b) => String(b.started || '').localeCompare(String(a.started || '')));
    return out.slice(0, max || 40);
  }

  // ------------------------------------------------------------------ permissions on the PC

  /** What a category lets this phone do, in words. */
  function accessName(c) {
    return ({
      browse: 'Browse the drives and folders', stream: 'Open, play and download files', upload: 'Send files and photo backups to the PC',
      manage: 'Change files: new folders, rename, move, delete', hud: 'Gaming HUD and the PC remote',
      control: 'Control the PC: drives, clean-up, game saves', space: 'See Space & RAM', game: 'Play PC games on this phone',
      relay: "See phones' files through the PC"
    })[c] || String(c || '');
  }

  /** Where one category stands ({allowed, mode, expiresAt, switchOn} from /api/access/mine). */
  function grantText(c, now) {
    if (!c.switchOn) return 'Off on the PC for every device';
    if (!c.allowed) return 'Not allowed';
    if (c.mode === 'timed' && c.expiresAt) {
      const d = new Date(c.expiresAt);
      return d.getTime() <= now ? 'Not allowed' : 'Allowed until ' + pad2(d.getHours()) + ':' + pad2(d.getMinutes());
    }
    if (c.mode === 'session') return 'Allowed until the PC app restarts';
    return 'Allowed';
  }

  // ------------------------------------------------------------------ links (how the phone reaches the PC)

  /** Words for the link the PC saw this phone come in on (its /api/status "link"). */
  function linkLabel(kind) {
    return ({
      wifi: 'Wi-Fi', lan: 'local network', 'pc-hotspot': "the PC's hotspot", 'phone-hotspot': "this phone's hotspot",
      usb: 'USB cable', 'usb-adb': 'USB cable (adb)', internet: 'Tailscale', local: 'this device'
    })[kind] || 'local Wi-Fi';
  }

  /** True when the host of url is in the same /24 as ip (both IPv4). */
  function sameSubnet(ip, url) {
    const host = String(url || '').replace(/^https?:\/\//, '').replace(/[:/].*$/, '');
    const a = String(ip || '').split('.'), b = host.split('.');
    return a.length === 4 && b.length === 4 && a[0] === b[0] && a[1] === b[1] && a[2] === b[2];
  }

  // ------------------------------------------------------------------ words built from values (shown on the page)

  /**
   * "From phone › Trip" for a folder inside one of the PC's places ({root, label}), else the path as it is. The
   * longest matching root wins; "uploads2" is not inside "uploads".
   */
  function friendlyPath(dir, places) {
    const d = String(dir || ''), low = d.toLowerCase();
    const pl = (places || []).filter(p => p && p.root && low.startsWith(String(p.root).toLowerCase()))
      .sort((a, b) => b.root.length - a.root.length)[0];
    if (!pl) return d;
    const rest = d.slice(pl.root.length);
    if (rest && !/^[\\/]/.test(rest) && !/[\\/:]$/.test(pl.root)) return d;
    return [pl.label].concat(rest.split(/[\\/]+/).filter(Boolean)).join(' › ');
  }

  /** A synced folder's last run: "Synced 10:42 (5 min ago) · 3 up, 1 down · 1 kept twice (conflict)". */
  function syncStatus(pair, when) {
    const l = pair && pair.last;
    if (!l) return 'Not synced yet';
    if (!l.ok) return 'Last try failed: ' + (l.error || '');
    const parts = ['Synced ' + (when ? when(l.at) : String(l.at)), (l.up || 0) + ' up, ' + (l.down || 0) + ' down'];
    if (l.conflicts) parts.push(l.conflicts + ' kept twice (conflict)');
    if (l.failed) parts.push(l.failed + ' failed');
    return parts.join(' · ');
  }

  /** What "Wake PC" says under it: a wired card usually wakes the PC, a Wi-Fi card seldom does. */
  function wakeHint(wol) {
    return wol && wol.wired ? 'Sends a wake signal on this Wi-Fi' : 'Its network card is Wi-Fi: waking it often only works over a cable';
  }

  // ------------------------------------------------------------------ transfer history by kind of file

  /** The groups of the transfer history, in the order shown (the PC app groups the same way). */
  const TRANSFER_KINDS = ['photos', 'videos', 'music', 'documents', 'apps', 'others'];
  const KIND_EXT = {
    photos: ['jpg', 'jpeg', 'png', 'gif', 'webp', 'heic', 'heif', 'bmp', 'tif', 'tiff', 'raw', 'dng', 'svg'],
    videos: ['mp4', 'mkv', 'mov', 'avi', 'webm', 'm4v', '3gp', 'wmv', 'flv', 'ts'],
    music: ['mp3', 'm4a', 'aac', 'flac', 'wav', 'ogg', 'opus', 'wma', 'amr'],
    documents: ['pdf', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'txt', 'md', 'csv', 'rtf', 'odt', 'ods', 'odp', 'epub', 'json', 'xml', 'html', 'htm'],
    apps: ['apk', 'apks', 'xapk', 'exe', 'msi', 'zip', 'rar', '7z', 'tar', 'gz', 'bz2', 'xz', 'iso']
  };

  /** "beach.PNG" → "photos"; no or unknown extension → "others". */
  function transferKind(name) {
    const n = String(name || ''), dot = n.lastIndexOf('.');
    if (dot < 0 || dot === n.length - 1) return 'others';
    const e = n.slice(dot + 1).toLowerCase();
    for (const k of TRANSFER_KINDS) if (KIND_EXT[k] && KIND_EXT[k].includes(e)) return k;
    return 'others';
  }

  /** [{kind, items}] for the kinds that have something, in the fixed order; items keep their order (newest first). */
  function groupTransfers(list, nameOf) {
    const by = {};
    for (const t of list || []) (by[transferKind(nameOf ? nameOf(t) : t)] = by[transferKind(nameOf ? nameOf(t) : t)] || []).push(t);
    return TRANSFER_KINDS.filter(k => by[k]).map(kind => ({ kind, items: by[kind] }));
  }

  const api = {
    crypto: { utf8, sha256, hmacSha256, hex, randomHex, sameText },
    helloProof,
    diffNew, isNew, pickEvictions, fmtLastUpdate, matchName, listKey, trimRecord, migrateDirCache,
    createListStore, createSnapshots, createRegistry,
    covers, pinFor, pinsBelow, diskId, isDiskId, diskPath, keepTooBig, sameCopy, fmtSize, pinStatus, keptListing,
    linkLabel, sameSubnet, accessName, grantText, safeFolder, peerText,
    friendlyPath, syncStatus, wakeHint,
    TRANSFER_KINDS, transferKind, groupTransfers, shareSavePath, offerSummary, taskLink, upsertTask, whenOffState, whenOffText, whenOffReady, whenOffReadyText, copyNote
  };

  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.CloudSsdData = Object.assign(root.CloudSsdData || {}, api);
})(typeof window !== 'undefined' ? window : this);
