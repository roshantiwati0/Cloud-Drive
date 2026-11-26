// Cloud-SSD phone client.
// Runs inside the Android app (window.AndroidBridge is present) and, with fewer features, in a phone browser
// opened from the PC's QR code. Every number on screen comes from the phone or the PC; when a value is not
// known yet the UI shows "—", never a placeholder figure.
(function () {
  'use strict';

  const native = window.AndroidBridge || null;
  const isApp = !!native;
  const $ = (id) => document.getElementById(id);

  // ------------------------------------------------------------------ icons
  const SVG = {
    folder: '<svg viewBox="0 0 24 24"><path d="M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/></svg>',
    doc: '<svg viewBox="0 0 24 24"><path d="M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z"/></svg>',
    pdf: '<svg viewBox="0 0 24 24"><path d="M20 2H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-8.5 7.5c0 .83-.67 1.5-1.5 1.5H9v2H7.5V7H10c.83 0 1.5.67 1.5 1.5v1zm5 2c0 .83-.67 1.5-1.5 1.5h-2.5V7H15c.83 0 1.5.67 1.5 1.5v3zm4-3H19v1h1.5V11H19v2h-1.5V7h3v1.5zM9 9.5h1v-1H9v1zm4.5 2H15v-3h-1.5v3z"/></svg>',
    image: '<svg viewBox="0 0 24 24"><path d="M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z"/></svg>',
    video: '<svg viewBox="0 0 24 24"><path d="M18 4l2 4h-3l-2-4h-2l2 4h-3l-2-4H8l2 4H7L5 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V4h-4z"/></svg>',
    audio: '<svg viewBox="0 0 24 24"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z"/></svg>',
    archive: '<svg viewBox="0 0 24 24"><path d="M20 6h-8l-2-2H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2zm-6 10h-4v-2h4v2zm0-4h-4v-2h4v2zm0-4h-4V6h4v2z"/></svg>',
    apk: '<svg viewBox="0 0 24 24"><path d="M6 18c0 .55.45 1 1 1h1v3.5c0 .83.67 1.5 1.5 1.5s1.5-.67 1.5-1.5V19h2v3.5c0 .83.67 1.5 1.5 1.5s1.5-.67 1.5-1.5V19h1c.55 0 1-.45 1-1V8H6v10zM3.5 8C2.67 8 2 8.67 2 9.5v7c0 .83.67 1.5 1.5 1.5S5 17.33 5 16.5v-7C5 8.67 4.33 8 3.5 8zm17 0c-.83 0-1.5.67-1.5 1.5v7c0 .83.67 1.5 1.5 1.5s1.5-.67 1.5-1.5v-7c0-.83-.67-1.5-1.5-1.5zm-4.97-5.84l1.3-1.3c.2-.2.2-.51 0-.71-.2-.2-.51-.2-.71 0l-1.48 1.48C13.85 1.23 12.95 1 12 1c-.96 0-1.86.23-2.66.63L7.85.15c-.2-.2-.51-.2-.71 0-.2.2-.2.51 0 .71l1.31 1.31C6.97 3.26 6 5.01 6 7h12c0-1.99-.97-3.75-2.47-4.84zM10 5H9V4h1v1zm5 0h-1V4h1v1z"/></svg>',
    star: '<svg viewBox="0 0 24 24"><path d="M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z"/></svg>',
    more: '<svg viewBox="0 0 24 24"><path d="M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z"/></svg>',
    download: '<svg viewBox="0 0 24 24"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>',
    upload: '<svg viewBox="0 0 24 24"><path d="M9 16h6v-6h4l-7-7-7 7h4zm-4 2h14v2H5z"/></svg>',
    play: '<svg viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg>',
    chevron: '<svg viewBox="0 0 24 24"><path d="M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z"/></svg>',
    close: '<svg viewBox="0 0 24 24"><path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z"/></svg>',
    check: '<svg viewBox="0 0 24 24"><path d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"/></svg>',
    open: '<svg viewBox="0 0 24 24"><path d="M19 19H5V5h7V3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z"/></svg>',
    pc: '<svg viewBox="0 0 24 24"><path d="M20 18c1.1 0 1.99-.9 1.99-2L22 6c0-1.1-.9-2-2-2H4c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2H0v2h24v-2h-4zM4 6h16v10H4V6z"/></svg>',
    desktop: '<svg viewBox="0 0 24 24"><path d="M21 2H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h7v2H8v2h8v-2h-2v-2h7c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm0 14H3V4h18v12z"/></svg>',
    person: '<svg viewBox="0 0 24 24"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z"/></svg>',
    devices: '<svg viewBox="0 0 24 24"><path d="M4 6h18V4H4c-1.1 0-2 .9-2 2v11H0v3h14v-3H4V6zm19 2h-6c-.55 0-1 .45-1 1v10c0 .55.45 1 1 1h6c.55 0 1-.45 1-1V9c0-.55-.45-1-1-1zm-1 9h-4v-7h4v7z"/></svg>',
    add: '<svg viewBox="0 0 24 24"><path d="M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z"/></svg>',
    settings: '<svg viewBox="0 0 24 24"><path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58a.49.49 0 0 0 .12-.61l-1.92-3.32a.49.49 0 0 0-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54a.484.484 0 0 0-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96a.49.49 0 0 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58a.49.49 0 0 0-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z"/></svg>',
    lock: '<svg viewBox="0 0 24 24"><path d="M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z"/></svg>',
    wifi: '<svg viewBox="0 0 24 24"><path d="M1 9l2 2c4.97-4.97 13.03-4.97 18 0l2-2C16.93 2.93 7.08 2.93 1 9zm8 8l3 3 3-3c-1.65-1.66-4.34-1.66-6 0zm-4-4l2 2c2.76-2.76 7.24-2.76 10 0l2-2C15.14 9.14 8.87 9.14 5 13z"/></svg>',
    pin: '<svg viewBox="0 0 24 24"><path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM10 17l-3.5-3.5 1.41-1.41L10 14.17 15.18 9l1.41 1.41L10 17z"/></svg>',
    share: '<svg viewBox="0 0 24 24"><path d="M18 16.08c-.76 0-1.44.3-1.96.77L8.91 12.7c.05-.23.09-.46.09-.7s-.04-.47-.09-.7l7.05-4.11c.54.5 1.25.81 2.04.81 1.66 0 3-1.34 3-3s-1.34-3-3-3-3 1.34-3 3c0 .24.04.47.09.7L8.04 9.81C7.5 9.31 6.79 9 6 9c-1.66 0-3 1.34-3 3s1.34 3 3 3c.79 0 1.5-.31 2.04-.81l7.12 4.16c-.05.21-.08.43-.08.65 0 1.61 1.31 2.92 2.92 2.92s2.92-1.31 2.92-2.92c0-1.61-1.31-2.92-2.92-2.92z"/></svg>',
    info: '<svg viewBox="0 0 24 24"><path d="M11 7h2v2h-2zm0 4h2v6h-2zm1-9C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z"/></svg>',
    cloud: '<svg viewBox="0 0 24 24"><path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96z"/></svg>',
    stream: '<svg viewBox="0 0 24 24"><path d="M10 16.5l6-4.5-6-4.5v9zM12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z"/></svg>'
  };

  const EXT = {
    images: ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'heic', 'heif', 'dng', 'svg'],
    videos: ['mp4', 'mkv', 'webm', 'avi', 'mov', 'm4v', '3gp', 'ts', 'flv', 'wmv'],
    audio: ['mp3', 'wav', 'flac', 'aac', 'ogg', 'm4a', 'opus', 'amr', 'wma'],
    text: ['txt', 'log', 'json', 'xml', 'md', 'cs', 'java', 'js', 'html', 'css', 'py', 'ini', 'cfg', 'csv', 'yml', 'yaml', 'bat', 'ps1', 'sh'],
    archive: ['zip', 'rar', '7z', 'tar', 'gz', 'iso'],
    apk: ['apk', 'apks', 'xapk', 'apkm']
  };
  // Formats Android's WebView can really play; other videos are handed to a player app.
  const PLAYABLE_VIDEO = ['mp4', 'webm', 'm4v', 'mov', '3gp'];

  // ------------------------------------------------------------------ small helpers
  const ls = {
    get(k) { try { return localStorage.getItem(k); } catch (e) { return null; } },
    set(k, v) { try { localStorage.setItem(k, v); } catch (e) { /* private mode or full: the app still works, just forgets */ } },
    del(k) { try { localStorage.removeItem(k); } catch (e) { } },
    json(k, fallback) { try { const v = localStorage.getItem(k); return v ? JSON.parse(v) : fallback; } catch (e) { return fallback; } }
  };

  function esc(s) {
    return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }
  function extOf(name) {
    const i = String(name || '').lastIndexOf('.');
    return i < 0 ? '' : String(name).slice(i + 1).toLowerCase();
  }
  function kindOf(name) {
    const e = extOf(name);
    for (const k of ['images', 'videos', 'audio', 'archive', 'apk', 'text']) if (EXT[k].includes(e)) return k;
    return e === 'pdf' ? 'pdf' : 'other';
  }
  function iconFor(name) {
    switch (kindOf(name)) {
      case 'images': return SVG.image;
      case 'videos': return SVG.video;
      case 'audio': return SVG.audio;
      case 'archive': return SVG.archive;
      case 'apk': return SVG.apk;
      case 'pdf': return SVG.pdf;
      default: return SVG.doc;
    }
  }
  function mimeOf(name) {
    const map = {
      mp4: 'video/mp4', m4v: 'video/mp4', mkv: 'video/x-matroska', webm: 'video/webm', avi: 'video/x-msvideo', mov: 'video/quicktime',
      mp3: 'audio/mpeg', wav: 'audio/wav', flac: 'audio/flac', m4a: 'audio/mp4', ogg: 'audio/ogg',
      jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', gif: 'image/gif', webp: 'image/webp',
      pdf: 'application/pdf', zip: 'application/zip', apk: 'application/vnd.android.package-archive', txt: 'text/plain'
    };
    return map[extOf(name)] || 'application/octet-stream';
  }
  /** 1024-based, the way Windows and the PC app show sizes. */
  function fmtBytes(b) {
    if (b == null || isNaN(b)) return '—';
    if (b < 1) return '0 B';
    const u = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
    const i = Math.min(u.length - 1, Math.floor(Math.log(b) / Math.log(1024)));
    const v = b / Math.pow(1024, i);
    return (i === 0 ? v.toFixed(0) : v >= 100 ? v.toFixed(0) : v >= 10 ? v.toFixed(1) : v.toFixed(2)) + ' ' + u[i];
  }
  /** 1000-based, the way Android's own Settings and Files app show phone storage (a "256 GB" phone). */
  function fmtDec(b) {
    if (b == null || isNaN(b)) return '—';
    if (b < 1) return '0 B';
    const u = ['B', 'kB', 'MB', 'GB', 'TB'];
    const i = Math.min(u.length - 1, Math.floor(Math.log(b) / Math.log(1000)));
    const v = b / Math.pow(1000, i);
    return (i === 0 ? v.toFixed(0) : v >= 100 ? v.toFixed(0) : v >= 10 ? v.toFixed(1) : v.toFixed(2)) + ' ' + u[i];
  }
  function pct(part, total) {
    return total > 0 ? Math.min(100, Math.max(0, (part / total) * 100)) : 0;
  }
  function fmtDuration(sec) {
    sec = Math.max(0, Math.floor(sec));
    const h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = sec % 60;
    return h > 0 ? `${h}h ${m}m` : m > 0 ? `${m}m ${s}s` : `${s}s`;
  }
  function timeAgo(when) {
    const t = typeof when === 'number' ? when : Date.parse(when);
    if (!t) return '';
    const s = Math.max(0, (Date.now() - t) / 1000);
    if (s < 60) return 'just now';
    if (s < 3600) return Math.floor(s / 60) + ' min ago';
    if (s < 86400) return Math.floor(s / 3600) + ' h ago';
    return new Date(t).toLocaleDateString(dateLocale(), { day: 'numeric', month: 'short' });
  }
  /** "now", "5m ago", "2h ago", "3d ago": short enough for a small badge. */
  function shortAgo(when) {
    const t = typeof when === 'number' ? when : Date.parse(when);
    if (!t) return 'earlier';
    const s = Math.max(0, (Date.now() - t) / 1000);
    return s < 60 ? 'now' : s < 3600 ? Math.floor(s / 60) + 'm ago' : s < 86400 ? Math.floor(s / 3600) + 'h ago' : Math.floor(s / 86400) + 'd ago';
  }
  function fmtDate(when) {
    const t = typeof when === 'number' ? when : Date.parse(when);
    if (!t) return '';
    const d = new Date(t);
    const opts = d.getFullYear() === new Date().getFullYear() ? { day: 'numeric', month: 'short' } : { day: 'numeric', month: 'short', year: 'numeric' };
    return d.toLocaleDateString(dateLocale(), opts);
  }
  /** Dates in the app's language: "12 नव॰" in Hindi, "12 Nov" in Hinglish, the phone's own way in English. */
  function dateLocale() {
    const lang = window.CloudSsdI18n ? window.CloudSsdI18n.lang() : 'english';
    return lang === 'hindi' ? 'hi-IN' : lang === 'hinglish' ? 'en-IN' : undefined;
  }
  /** The app's words in the language chosen (i18n.js); null on a page without it. */
  const I18N = window.CloudSsdI18n || null;
  /** Sets a text in the language chosen, and only when it changed (no churn between English and its translation). */
  function setText(id, text) {
    const el = $(id);
    if (!el) return;
    // Names (files, devices) stay as they are.
    const shown = I18N && !el.closest('[data-no-i18n]') ? I18N.t(text) : text;
    if (el.textContent === shown) return;
    el.textContent = shown;
    if (I18N) I18N.remember(el.firstChild, String(text));
  }
  /** A text in the language chosen, for places the translator does not look at (the file viewer, native dialogs). */
  function say(text) { return I18N ? I18N.t(text) : text; }
  function setWidth(id, percent) { const el = $(id); if (el) el.style.width = percent.toFixed(1) + '%'; }
  function show(id, on) { const el = $(id); if (el) el.style.display = on ? '' : 'none'; }

  let toastTimer = null;
  function toast(msg) {
    const el = $('toast');
    if (!el) return;
    el.textContent = msg;
    el.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => el.classList.remove('show'), 2800);
  }

  function emptyBlock(icon, title, sub, btnLabel, btnAct, extraAttrs) {
    return `<div class="empty-block">
      <div class="empty-block-icon">${icon}</div>
      <div class="empty-block-title">${esc(title)}</div>
      ${sub ? `<div class="empty-block-sub">${esc(sub)}</div>` : ''}
      ${btnLabel ? `<button class="m3-filled-btn" data-act="${btnAct}" ${extraAttrs || ''}>${esc(btnLabel)}</button>` : ''}
    </div>`;
  }
  function skeleton(rows) {
    return Array.from({ length: rows || 6 }, () => '<div class="skeleton-row"><div class="skeleton-icon"></div><div class="skeleton-lines"><div></div><div></div></div></div>').join('');
  }

  // ------------------------------------------------------------------ link to the PC
  const link = {
    // In the app the key lives encrypted on the phone (native side); a browser keeps it in its own storage.
    server: (ls.get('cloudssd_server_url') || '').replace(/\/+$/, ''),
    token: isApp ? '' : (ls.get('cloudssd_token') || ''),
    lan: (ls.get('cloudssd_server_url') || '').replace(/\/+$/, ''),   // the address it was paired on (home)
    away: ls.json('cloudssd_away_urls', []),                          // Tailscale addresses the PC reported
    direct: ls.json('cloudssd_direct_urls', []),                      // its other addresses without the internet (hotspot, cable)
    kind: '',                                                         // how the PC saw this phone arrive (wifi, usb, …)
    pcId: ls.get('cloudssd_pc_id') || '',
    deviceId: ls.get('cloudssd_device_id') || '',   // this phone's id on the PC (for /api/hello proofs)
    verified: '',         // the address proven to be our PC since the link last went down
    route: 'lan',         // lan | tailnet
    state: 'init',        // init | unpaired | offline | online | paused
    status: null,         // last /api/status from the PC
    pcName: ls.get('cloudssd_pc_name') || '',
    since: 0,
    ping: null,
    fails: 0,
    upgrading: false
  };
  let appInfo = {};
  let activeTab = 'home';

  // ---- "last time" data: what the PC showed when it was last reachable, kept per PC on this phone ----
  const DATA = window.CloudSsdData || null;
  const lists = DATA && DATA.createListStore ? DATA.createListStore() : null;     // folder lists (IndexedDB)
  const snaps = DATA && DATA.createSnapshots ? DATA.createSnapshots() : null;     // status, drives, history (small)
  link.lastOnline = 0;
  let snapSavedAt = 0;
  function snapOf() { return link.pcId && snaps ? snaps.get(link.pcId) : null; }
  function saveSnap(patch) { if (link.pcId && snaps) snaps.set(link.pcId, patch); }
  /** "10:42 (5 min ago)" for a time this phone last heard from the PC. */
  function lastUpdate(ts) { return DATA && DATA.fmtLastUpdate ? DATA.fmtLastUpdate(ts, Date.now()) : timeAgo(ts); }
  /** The PC is paired but not answering, and its last data is on this phone. */
  function showingSaved() { return link.state !== 'online' && !!link.pcId && !!link.status && cloudSource() !== 'drive'; }

  // ---- several PCs: the saved list (keys stay native in the app), one of them active at a time ----
  const registry = DATA && DATA.createRegistry ? DATA.createRegistry({ native }) : null;
  link.gen = 0;   // bumped when the active PC changes: answers that arrive later for the previous PC are dropped
  /** The active PC's id in the saved list (its public id, or its address for a PC too old to have one). */
  function currentDeviceId() { return link.pcId || (link.lan ? 'url:' + link.lan : ''); }
  /** The name shown for a saved device: the one given on this phone, else the PC's own. */
  function deviceLabel(d) { return (d && (d.label || d.name)) || 'PC'; }
  /** Keeps the active PC's entry in the saved list up to date (address, name, features, when it was last online). */
  function rememberDevice(extra) {
    const id = currentDeviceId();
    if (!registry || !id) return;
    registry.upsert(Object.assign({ id, name: link.pcName || '', lan: link.lan, last: link.server, away: link.away, deviceId: link.deviceId || '' }, extra || {}),
      isApp ? null : (link.token || null));
  }

  /** True when the PC told us it can do this (an older PC simply lacks newer features). */
  function pcHas(f) { return !!(link.status && Array.isArray(link.status.features) && link.status.features.includes(f)); }

  /** A random id for this phone (or browser), so pairing again updates its entry on the PC instead of adding one. */
  function installId() {
    if (appInfo.installId) return appInfo.installId;
    let id = ls.get('cloudssd_install_id');
    if (!id) {
      const b = new Uint8Array(16);
      (window.crypto || {}).getRandomValues ? crypto.getRandomValues(b) : b.forEach((_, i) => { b[i] = Math.random() * 256; });
      id = Array.from(b, x => x.toString(16).padStart(2, '0')).join('');
      ls.set('cloudssd_install_id', id);
    }
    return id;
  }
  function deviceName() {
    if (appInfo.meName) return appInfo.meName;
    if (!isApp && ls.get('cloudssd_me_name')) return ls.get('cloudssd_me_name');
    return appInfo.model ? `${appInfo.manufacturer || ''} ${appInfo.model}`.trim() : 'Phone browser';
  }
  /** What this device tells a PC when it pairs: its name, kind (phone/tablet/browser), Android version and app version. */
  function pairQuery() {
    const kind = isApp ? (appInfo.tablet ? 'tablet' : 'phone') : 'browser';
    const platform = isApp ? 'Android ' + (appInfo.android || '') : 'Web browser';
    return `name=${encodeURIComponent(deviceName())}&installId=${installId()}&kind=${kind}&platform=${encodeURIComponent(platform.trim())}` +
      (appInfo.version ? `&app=${encodeURIComponent(appInfo.version)}` : '');
  }

  function normalizeServer(input) {
    let v = String(input || '').trim().replace(/\/+$/, '');
    if (!v) return '';
    if (!/^https?:\/\//i.test(v)) v = 'http://' + v;
    if (!/:\d+$/.test(v.replace(/^https?:\/\//i, ''))) v += ':8090';
    return v;
  }
  /** Remembers the PC and this phone's key. {@code server} is the home address it was paired on. */
  function saveCreds(server, token, deviceId) {
    link.server = (server || '').replace(/\/+$/, '');
    link.lan = link.server;
    link.route = 'lan';
    if ((token || '') !== link.token) setDeviceId(deviceId || '');   // a new key belongs to a (re)paired device
    else if (deviceId) setDeviceId(deviceId);
    link.token = token || '';
    link.verified = '';   // callers that just paired here (or proved the address) mark it themselves
    ls.set('cloudssd_server_url', link.server);
    if (isApp) {
      ls.del('cloudssd_token');
      try { native.setServerBase(link.server, link.token); } catch (e) { }
    } else if (link.token) ls.set('cloudssd_token', link.token); else ls.del('cloudssd_token');
  }
  /** Switches the address in use (home ↔ Tailscale) without touching the key or the paired address. */
  function useServer(url, route) {
    link.server = url;
    link.route = route;
  }
  /** This phone's id on the PC, needed to check the PC's /api/hello proof. */
  function setDeviceId(id) {
    id = id || '';
    if (id === link.deviceId) return;
    link.deviceId = id;
    if (id) ls.set('cloudssd_device_id', id); else ls.del('cloudssd_device_id');
  }
  function apiUrl(path, params) {
    // The token rides in the query string: a custom header would make the WebView send an extra OPTIONS request
    // before every call, doubling the time each folder takes to open.
    // encodeURIComponent, not URLSearchParams: that one writes a space as "+", which older PC versions read as a
    // real plus sign ("Z:\My+Folder" → Folder not found).
    const all = Object.assign({}, params, { token: link.token });
    return link.server + path + '?' + Object.keys(all).map(k => encodeURIComponent(k) + '=' + encodeURIComponent(all[k])).join('&');
  }
  async function api(path, opts) {
    opts = opts || {};
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), opts.timeout || 8000);
    const gen = link.gen;
    try {
      const res = await fetch(apiUrl(path, opts.params), { method: opts.method || 'GET', signal: ctrl.signal, cache: 'no-store', body: opts.body });
      let data = null;
      try { data = await res.json(); } catch (e) { /* not JSON */ }
      if (res.status === 401 && gen === link.gen) confirmUnpaired();
      // This phone may not do that yet: ask the PC (one question at a time), and try once more when it says yes.
      if (res.status === 403 && data && data.code === 'needs-permission' && !opts.noAsk && gen === link.gen) {
        if (await askAccess(data.categories || [], opts.why)) return api(path, Object.assign({}, opts, { noAsk: true }));
      }
      return { ok: res.ok, status: res.status, data };
    } finally {
      clearTimeout(timer);
    }
  }
  // ------------------------------------------------------------------ asking the PC for access
  const access = { busy: null, quietUntil: {} };
  function closeAccessSheet() { if (isOpen('access-sheet')) closeSheet('access-sheet'); }
  /**
   * Shows "Ask <PC> for access?" for what this phone may not do yet; resolves true once someone at the PC allowed it.
   * One question at a time (others wait for the same answer); after "Not now" or a no, the same thing is not asked
   * again for a minute.
   */
  function askAccess(categories, why) {
    if (!pcHas('access') || !categories.length || document.visibilityState === 'hidden') return Promise.resolve(false);
    if (access.busy) return access.busy;
    const key = categories.slice().sort().join(',');
    if (Date.now() < (access.quietUntil[key] || 0)) return Promise.resolve(false);
    const name = link.pcName || 'the PC';
    access.busy = new Promise(resolve => {
      let done = false, reqId = '', timer = 0;
      const finish = (ok, quiet) => {
        if (done) return;
        done = true;
        clearInterval(timer);
        if (!ok && quiet) access.quietUntil[key] = Date.now() + 60000;
        access.finish = null;
        closeAccessSheet();
        resolve(ok);
      };
      access.finish = finish;
      setText('access-title', 'Ask ' + name + ' for access?');
      setText('access-text', 'This phone may not do this yet. Someone at ' + name + ' has to allow it there.');
      $('access-list').innerHTML = categories.map(c => `<div class="access-item">${SVG.lock}<span>${esc(DATA ? DATA.accessName(c) : c)}</span></div>`).join('');
      setText('access-status', why || '');
      $('access-actions').innerHTML = '<button class="m3-tonal-btn" data-act="access-no">Not now</button><button class="m3-filled-btn" data-act="access-ask">Ask the PC</button>';
      access.ask = async () => {
        $('access-actions').innerHTML = '<button class="m3-tonal-btn" data-act="access-no">Cancel</button>';
        setText('access-status', 'Asking…');
        const r = await api('/api/access/request', { method: 'POST', params: { categories: categories.join(','), reason: why || '' }, noAsk: true });
        if (done) return;
        if (!r.ok || !r.data || !r.data.request) { setText('access-status', apiError(r, 'The PC did not take the question')); setTimeout(() => finish(false, true), 2500); return; }
        reqId = r.data.request.id;
        const until = Date.now() + (r.data.request.expiresIn || 120) * 1000;
        const look = async () => {
          const left = Math.max(0, Math.round((until - Date.now()) / 1000));
          setText('access-status', `Waiting for ${name} to answer… ${Math.floor(left / 60)}:${String(left % 60).padStart(2, '0')}`);
          const m = await api('/api/access/mine', { noAsk: true }).catch(() => null);
          if (done || !m || !m.ok || !m.data || !m.data.request || m.data.request.id !== reqId) return;
          const st = m.data.request.state;
          if (st === 'allowed') { toast('Allowed on ' + name); finish(true); }
          else if (st === 'denied') { setText('access-status', name + ' said no.'); clearInterval(timer); setTimeout(() => finish(false, true), 2000); }
          else if (st !== 'pending') { setText('access-status', 'Nobody answered in time.'); clearInterval(timer); setTimeout(() => finish(false, true), 2000); }
        };
        timer = setInterval(look, 2000);
        look();
      };
      access.cancel = () => {
        if (reqId) api('/api/access/cancel', { method: 'POST', params: { id: reqId }, noAsk: true }).catch(() => { });
        finish(false, true);
      };
      openSheet('access-sheet');
    }).finally(() => { access.busy = null; });
    return access.busy;
  }

  // ---- the PC looking at this phone: this phone decides ----
  const peer = { pc: '', name: '', scope: 'shared', write: true };
  function showPeerAsk(ev) {
    peer.pc = ev.pc || currentDeviceId();
    peer.name = ev.name || link.pcName || 'Your PC';
    peer.scope = ev.scope === 'shared' ? 'shared' : 'all';
    // Both ways, like the phone's own PC suite: it may also add files and folders (untick to only let it look).
    peer.write = true;
    setText('peer-title', peer.name + ' wants to see this phone\'s files');
    renderPeerScope();
    openSheet('peer-sheet');
  }
  function renderPeerScope() {
    document.querySelectorAll('[data-act="peer-scope"]').forEach(b => b.classList.toggle('on', b.dataset.s === peer.scope));
    const w = $('peer-write');
    if (w) w.checked = peer.write;
  }
  function peerAnswer(minutes) {
    closeSheet('peer-sheet');
    if (minutes < 0) { toast('Not allowed'); return; }
    try {
      if (typeof native.peerGrantWrite === 'function') native.peerGrantWrite(peer.pc, peer.scope, minutes, !!peer.write);
      else native.peerGrant(peer.pc, peer.scope, minutes);
    } catch (e) { return; }
    toast(peer.name + ' may see ' + (peer.scope === 'all' ? 'this phone\'s storage' : 'photos, downloads and documents') + (minutes ? ' for an hour' : ''));
  }

  /** The text copied on this phone, onto the PC's clipboard. */
  async function sendClipboard() {
    let text = '';
    try { text = native.getClipboard() || ''; } catch (e) { }
    if (!text.trim()) { toast('Copy some text first'); return; }
    if (text.length > 4000) text = text.slice(0, 4000);
    const r = await api('/api/clip', { method: 'POST', params: { text } });
    toast(r.ok ? 'Sent to ' + (link.pcName || 'the PC') + '\'s clipboard' : apiError(r, 'Could not send the clipboard'));
  }

  /*
   * More → Permissions: every permission in one place.
   *   1. what this phone may do on the PC in use (someone at the PC decides; "Ask" for the rest),
   *   2. what that PC may do on this phone (this phone decides),
   *   3. what Android lets this app do (files, notifications, running in the background).
   */
  let permAccess = null;      // the PC's answer (/api/access/mine) and which PC gave it
  function permGroup(title, body, note) {
    return `<div class="perm-group"><div class="perm-group-title">${esc(title)}</div>${body}${note ? `<div class="perm-group-note">${esc(note)}</div>` : ''}</div>`;
  }
  function permLine(name, sub, on, button) {
    return `<div class="perm-row ${on ? 'on' : ''}">
        <div class="perm-row-text"><div>${esc(name)}</div><div class="perm-row-sub">${esc(sub)}</div></div>
        ${button || (on ? '<span class="settings-pill on">On</span>' : '')}
      </div>`;
  }
  /** What Android lets this app do: {files: all|media|none, notifications, background}. */
  function androidPerms() {
    let st = {};
    try { if (typeof native.permState === 'function') st = JSON.parse(native.permState() || '{}'); } catch (e) { }
    if (!st.files) { try { st.files = native.getStorageAccess(); } catch (e) { st.files = 'none'; } }
    return st;
  }
  function androidPermRows() {
    const st = androidPerms();
    const allow = (k) => `<button class="m3-tonal-btn" data-act="perm-android" data-k="${k}">Allow</button>`;
    let rows = permLine('All files access', 'Shows every folder, documents and APKs', st.files === 'all', st.files === 'all' ? '' : allow('files'));
    rows += permLine('Photos, videos and audio', 'For the Phone tab and camera backup', st.files !== 'none', st.files !== 'none' ? '' : allow('media'));
    if ('notifications' in st) rows += permLine('Notifications', 'Transfer progress, and questions from the PC', !!st.notifications, st.notifications ? '' : allow('notifications'));
    if ('background' in st) rows += permLine('Run in the background', 'Keeps the link to the PC while the screen is off', !!st.background, st.background ? '' : allow('background'));
    if (typeof native.openAppSettings === 'function') {
      rows += `<div class="perm-row perm-row-link" data-act="perm-android" data-k="settings" role="button" tabindex="0">
        <div class="perm-row-text"><div>Open this app in Android Settings</div><div class="perm-row-sub">Every Android permission of Cloud-SSD</div></div>${SVG.chevron}</div>`;
    }
    return rows;
  }
  function renderPermissions() {
    const now = Date.now();
    let html = '';
    // 1. This phone on the PC.
    let onPc;
    if (link.state !== 'online') {
      onPc = `<div class="perm-empty">${link.pcName ? 'The PC is not reachable right now. Its permissions show when it is back.' : 'No PC paired yet'}</div>`;
    } else if (permAccess && permAccess.pc === currentDeviceId() && Array.isArray(permAccess.categories)) {
      onPc = permAccess.categories.map(c => permLine(DATA ? DATA.accessName(c.category) : c.name, DATA ? DATA.grantText(c, now) : '', c.allowed && c.switchOn,
        !c.allowed && c.switchOn ? `<button class="m3-tonal-btn" data-act="perm-ask" data-c="${esc(c.category)}">Ask</button>` : '')).join('');
    } else {
      // An older PC (no "Ask"), or its answer is on the way: the switches it sent with its status.
      const perms = link.status && link.status.permissions;
      onPc = perms ? PERMS.filter(([k]) => k in perms).map(([k, l]) => permLine(l, perms[k] ? 'Allowed' : 'Switched off on the PC', !!perms[k], perms[k] ? '' : '<span class="settings-pill off">Off</span>')).join('')
        : '<div class="perm-empty">Loading…</div>';
    }
    html += permGroup('What this phone may do on the PC', onPc, 'Someone at the PC decides; it can take them back any time.');
    // 2. The PC on this phone.
    const peer = peerRow();
    // Its other switches (this phone's numbers on the PC, removing things, ringing it) are one tap further.
    const more = isApp ? `<div class="perm-row perm-row-link" data-act="open-settings-group" data-group="pcmay" role="button" tabindex="0">
        <div class="perm-row-text"><div>More the PC may do</div><div class="perm-row-sub">Phone storage and RAM numbers, removing things, ringing this phone</div></div>${SVG.chevron}</div>` : '';
    if (peer || more) html += permGroup('What the PC may do on this phone', peer + more, 'You decide this here, and can stop it any time.');
    // 2b. Files when it is off: its own yes, needed here AND on the PC.
    //     Also here: the PC's main folders kept on this phone itself (needs no cloud and no yes for cloud copies).
    const whenOff = whenOffRow();
    const mainRow = mainFoldersRow();
    if (whenOff || mainRow) html += permGroup('Files when it is off', mainRow + whenOff,
      whenOff ? 'Copies in the cloud work only when this phone and the PC both say yes. You can stop it any time.' : 'Kept folders are copies on this phone. They update whenever the PC is on.');
    // 3. Android.
    if (isApp) html += permGroup('Android permissions', androidPermRows(), '');
    setText('perm-sub', link.pcName ? 'This phone and ' + link.pcName : 'This phone');
    $('perm-list').innerHTML = html;
  }
  async function openPermissions() {
    renderPermissions();
    openSheet('perm-sheet');
    if (link.state !== 'online' || !pcHas('access')) return;
    const r = await api('/api/access/mine', { noAsk: true });
    if (r.ok && r.data) { permAccess = Object.assign({ pc: currentDeviceId() }, r.data); if (isOpen('perm-sheet')) renderPermissions(); }
  }

  /**
   * "Files when it is off": each device keeps a copy of its files in the cloud so the other can open them while it is
   * switched off. This phone's own yes; the PC has its own (in the device's permissions there). Not shown for an older
   * PC, a guest, or while the PC is away.
   */
  function whenOffRow() {
    if (!DATA || link.state !== 'online' || !pcHas('whenoff')) return '';
    const w = link.status && link.status.whenOff;
    const state = DATA.whenOffState(w);
    if (state === 'none') return '';
    const mine = !!(w && w.device);
    // Both said yes: what this phone still needs before it can open the PC's files while the PC is off.
    const ready = DATA.whenOffReady(w, drive);
    const more = ready ? `<div class="perm-row ${ready === 'ready' ? 'on' : ''}">
        <div class="perm-row-text"><div>${ready === 'ready' ? 'Opens while the PC is off' : 'Not ready yet'}</div><div class="perm-row-sub">${esc(DATA.whenOffReadyText(ready, link.pcName))}</div></div>
        ${ready === 'sign-in' ? '<button class="m3-tonal-btn" data-act="drive-add">Sign in</button>' : ''}
      </div>` : '';
    return `<div class="perm-row ${state === 'on' ? 'on' : ''}">
        <div class="perm-row-text"><div>Keep copies in the cloud</div><div class="perm-row-sub">${esc(DATA.whenOffText(state, link.pcName))}</div></div>
        <button class="m3-tonal-btn" data-act="whenoff-set" data-on="${mine ? '0' : '1'}">${mine ? 'Stop' : 'Allow…'}</button>
      </div>` + more;
  }
  let whenOffKeyAt = 0;
  /**
   * Tells the app whether this phone may open this PC's copy in the cloud (both said yes), so it still knows while
   * the PC is off. And once: fetches the PC's Google key (never a sign-in) when this phone has none, so the user can
   * sign in to Google here.
   */
  function syncWhenOff() {
    if (!isApp || !link.pcId || typeof native.whenOffSync !== 'function') return;
    const w = link.status && link.status.whenOff;
    if (!w) return;
    const on = DATA.whenOffState(w) === 'on';
    try { native.whenOffSync(link.pcId, link.pcName || '', on); } catch (e) { return; }
    if (!on || !w.key || drive.available || Date.now() - whenOffKeyAt < 60000) return;
    whenOffKeyAt = Date.now();
    api('/api/whenoff/key', { noAsk: true }).then(r => {
      if (!r || !r.ok || !r.data || !r.data.clientId) return;
      try { native.setGoogleKey(r.data.clientId, r.data.clientSecret || ''); } catch (e) { return; }
      refreshDrive();
      if (isOpen('perm-sheet')) renderPermissions();
    }).catch(() => { });
  }
  async function setWhenOff(on) {
    if (on && !await confirmDialog('Files when it is off',
      'This phone and ' + (link.pcName || 'the PC') + ' will each keep a copy of their files in your cloud drive, so one can open the other\'s files while it is switched off. It starts only when the PC says yes too, and you can stop it here any time.', 'Allow')) return;
    const r = await api('/api/whenoff/consent', { method: 'POST', params: { on: on ? '1' : '0' }, noAsk: true }).catch(() => null);
    if (!r || !r.ok || !r.data) { toast(r ? apiError(r, 'Could not change it') : 'PC not reachable'); return; }
    if (link.status) link.status.whenOff = r.data;
    syncWhenOff();
    if (isOpen('perm-sheet')) renderPermissions();
    toast(DATA.whenOffText(DATA.whenOffState(r.data), link.pcName));
  }

  // ---- the PC's main folders kept on this phone (no cloud, no Google key: a copy here, opened while the PC is off) ----
  const mainKeep = { rows: [], seq: 0 };
  function mainKept() {
    const main = pcHome.data && pcHome.pc === link.pcId && Array.isArray(pcHome.data.main) ? pcHome.data.main : [];
    ensureKept();
    return main.filter(f => f.path && kept.pins.some(p => p.id === DATA.diskId(f.path))).length;
  }
  /** Its line in Permissions → "Files when it is off". Only with the app, a PC that is here and can serve its disks. */
  function mainFoldersRow() {
    if (!DATA || !DATA.diskId || !canKeep() || link.state !== 'online' || !pcHas('pc-main-folders') || !pcHas('fs-disk-roots')) return '';
    const n = mainKept();
    return `<div class="perm-row ${n ? 'on' : ''}">
        <div class="perm-row-text"><div>Keep the PC's main folders on this phone</div><div class="perm-row-sub">${esc(n ? n + ' kept on this phone. They open here while the PC is off.' : 'Desktop, Documents, Pictures and more open here while the PC is off. No internet is needed.')}</div></div>
        <button class="m3-tonal-btn" data-act="main-keep">Choose…</button>
      </div>`;
  }
  async function openMainKeep() {
    if (link.state !== 'online') { toast('Needs ' + (link.pcName || 'the PC') + ' · it is offline'); return; }
    const seq = ++mainKeep.seq;
    const r = await api('/api/pc/home', { timeout: 15000 }).catch(() => null);
    if (seq !== mainKeep.seq) return;
    if (!r || !r.ok || !r.data) { toast(r ? apiError(r, 'Could not ask the PC') : 'PC did not answer'); return; }
    pcHome.data = r.data; pcHome.at = Date.now(); pcHome.pc = link.pcId;
    const main = Array.isArray(r.data.main) ? r.data.main : [];
    if (!main.some(f => f.shared && f.path)) {
      toast(main.length ? 'Turn on "PC disks" in Cloud-SSD on the PC first (Phone → Permissions)' : 'The PC did not name its main folders');
      return;
    }
    mainKeep.rows = main.filter(f => f.shared && f.path).map(f => ({ name: f.name, kind: f.kind, path: f.path, id: DATA.diskId(f.path), usage: null }));
    renderMainKeep(true);
    for (const row of mainKeep.rows) {
      api('/api/fs/usage', { params: { path: row.id }, timeout: 60000 }).then(u => {
        if (seq !== mainKeep.seq) return;
        row.usage = u.ok && u.data ? u.data : { failed: apiError(u, 'Could not count it') };
        if (isOpen('choice-sheet')) renderMainKeep(false);
      }).catch(() => { if (seq === mainKeep.seq) { row.usage = { failed: 'PC did not answer' }; if (isOpen('choice-sheet')) renderMainKeep(false); } });
    }
  }
  function renderMainKeep(open) {
    ensureKept();
    const free = phone.totals && phone.totals.storageFree > 0 ? phone.totals.storageFree : 0;
    const list = mainKeep.rows.map(row => {
      const own = kept.pins.find(p => p.id === row.id);
      const u = row.usage;
      const big = DATA.keepTooBig ? DATA.keepTooBig(u, free) : '';
      const size = !u ? 'Counting…' : u.failed ? u.failed : (u.truncated ? 'More than ' : '') + fmtBytes(u.bytes) + ' · ' + u.files + (u.files === 1 ? ' file' : ' files');
      return {
        row, own, big, icon: QA_ICONS[row.kind] || SVG.folder, title: row.name,
        sub: own ? 'Kept on this phone · ' + DATA.pinStatus(own, Date.now()) + ' · tap to remove' : big ? size + ' · ' + big : size,
        fn: () => {
          if (own) { unkeep(own).then(() => { if (isOpen('perm-sheet')) renderPermissions(); }); return; }
          if (big) { toast(big); return; }
          keep(row.id, { name: row.name, isFolder: true });
          if (isOpen('perm-sheet')) renderPermissions();
        }
      };
    });
    const todo = list.filter(a => !a.own && !a.big && a.row.usage && !a.row.usage.failed);
    const total = todo.reduce((n, a) => n + (a.row.usage.bytes || 0), 0);
    const counting = mainKeep.rows.some(row => !row.usage);
    const all = todo.length > 1 && !counting ? [{
      icon: SVG.pin, title: 'Keep all ' + todo.length + ' folders',
      sub: fmtBytes(total) + ' on this phone' + (free ? ' · ' + fmtBytes(free) + ' free' : ''),
      fn: () => {
        if (free && total > free - 500 * 1024 * 1024) { toast('Not enough room on this phone for all of them. Keep them one by one.'); return; }
        todo.forEach(a => native.offlinePin(currentDeviceId(), a.row.id, a.row.name, true));
        refreshKept();
        toast(todo.length + ' folders will be kept on this phone');
        rerenderSsd();
        if (isOpen('perm-sheet')) renderPermissions();
      }
    }] : [];
    const rows = all.concat(list);
    if (open) { choiceSheet('Keep on this phone', rows); return; }
    choices = rows;
    $('choice-list').innerHTML = rows.map((a, i) => `
      <div class="m3-sheet-row" data-act="choice-pick" data-i="${i}">
        ${a.icon || ''}
        <div class="m3-sheet-row-text"><div>${esc(a.title)}</div>${a.sub ? `<div class="m3-sheet-row-sub">${esc(a.sub)}</div>` : ''}</div>
      </div>`).join('');
  }

  /** The other direction, in the same sheet: what the PC may see on this phone (this phone decides). */
  function peerRow() {
    if (!isApp || typeof native.peerState !== 'function' || !pcHas('peer')) return '';
    let st = {};
    try { st = JSON.parse(native.peerState(currentDeviceId()) || '{}'); } catch (e) { }
    const on = !!st.scope;
    return `<div class="perm-row ${on ? 'on' : ''}">
        <div class="perm-row-text"><div>${esc((link.pcName || 'The PC') + ' sees this phone\'s files')}</div><div class="perm-row-sub">${esc(DATA ? DATA.peerText(st, Date.now()) : '')}</div></div>
        <button class="m3-tonal-btn" data-act="${on ? 'peer-stop' : 'peer-open'}">${on ? 'Stop' : 'Allow…'}</button>
      </div>`;
  }

  function apiError(r, fallback) {
    return (r && r.data && r.data.error) || fallback || (r ? 'PC answered HTTP ' + r.status : 'PC not reachable');
  }

  function setLinkState(state) {
    if (state !== 'offline') link.blocked = false;
    if (link.state === state) return;
    const was = link.state;
    link.state = state;
    if (state === 'online') {
      link.since = Date.now();
      link.fails = 0;
      if (was !== 'init') toast('Connected to ' + (link.pcName || 'PC'));
      onConnected();
    }
    renderLink();
    // The PC went away: the Cloud SSD tab carries on from Google Drive when this phone is signed in.
    if (state !== 'online' && was === 'online' && activeTab === 'ssd') openSsdTab();
  }

  let polling = false;
  async function pollStatus() {
    if (link.state === 'paused' || !link.server || !link.token || polling) return;
    polling = true;
    try { await pollStatusOnce(); } finally { polling = false; }
  }
  async function pollStatusOnce() {
    const gen = link.gen;   // another PC chosen meanwhile: this answer is for the previous one and is dropped
    // After the link was down (or on another network) the address must prove it is our PC before the key goes there:
    // on a café's Wi-Fi the same address can be someone else's device.
    if (link.verified !== link.server) {
      const v = await proveOurPc(link.server);
      if (gen !== link.gen) return;
      if (v === 'other') { onOtherPcHere(); return; }
      if (v === 'rejected') { onUnpaired(); return; }
      if (v === 'down') { onOffline(); return; }
      link.verified = link.server;
    }
    const t0 = performance.now();
    try {
      const r = await api('/api/status', { timeout: 4000 });
      if (gen !== link.gen) return;
      if (r.ok && r.data && r.data.isPaired) {
        link.ping = Math.round(performance.now() - t0);
        onStatus(r.data);
        setLinkState('online');
        renderLink();
        renderStatusTiles();
        // At home again while on Tailscale: switch back to the faster, free home address.
        if (link.route === 'tailnet' && link.lan && (ticks % 30 === 0)) tryHome();
      } else if (r.ok && r.data && r.data.isPaired === false) {
        // Another PC now has this address (routers hand old addresses out again): our key is not wrong, the PC is
        // elsewhere. Only our own PC saying "not paired" means the key was removed — unless it says "blocked".
        if (r.data.pcId && link.pcId && r.data.pcId !== link.pcId) onOtherPcHere();
        else if (r.data.blocked) onBlocked();
        else onUnpaired();
      } else if (r.status === 403 && r.data && r.data.code) {
        onOffline(r.data.error);
      } else if (r.status !== 401) {
        onOffline();
      }
    } catch (e) {
      if (gen === link.gen) onOffline();
    }
  }

  /** What the PC reports: remembered, and handed to the native side (Files app, camera backup, transfers). */
  function onStatus(st) {
    link.status = st;
    link.lastOnline = Date.now();
    if (st.pcName && st.pcName !== link.pcName) { link.pcName = st.pcName; ls.set('cloudssd_pc_name', link.pcName); }
    if (st.pcId === link.pcId) syncWhenOff();
    if (st.pcId && st.pcId !== link.pcId) {
      // A PC saved under its address reports its id: the saved entry (and its data) moves under the id.
      const oldId = currentDeviceId();
      link.pcId = st.pcId; ls.set('cloudssd_pc_id', st.pcId);
      if (registry && oldId && oldId !== st.pcId) registry.upsert({ id: st.pcId, replaces: oldId });
    }
    // A guest's end time is saved as soon as it is known (or gone): the PC removes a guest at that time, and the
    // page must then say so — the 30-second save below can be too late for a short guest.
    const rec = registry && registry.get(currentDeviceId());
    if (rec && (rec.guestUntil || null) !== (st.guestUntil || null)) rememberDevice({ guestUntil: st.guestUntil || null });
    // Kept for when the PC is away: the tiles, drives and folders then show the last numbers with their time.
    if (Date.now() - snapSavedAt > 30000) {
      snapSavedAt = Date.now();
      saveSnap({ status: st, lastOnline: link.lastOnline });
      rememberDevice({ features: st.features || [], version: st.version || '', lastOnline: link.lastOnline, wol: st.wol || null, guestUntil: st.guestUntil || null });
    }
    setDeviceId(st.auth === 'legacy' ? 'legacy' : (st.deviceId || link.deviceId));
    const port = (/:(\d+)$/.exec(link.lan || link.server) || [])[1] || '8090';
    const away = st.away && st.away.allowed ? (st.away.ips || []).map(ip => `http://${ip.includes(':') ? '[' + ip + ']' : ip}:${port}`)
      .concat(st.away.dnsName ? [`http://${st.away.dnsName}:${port}`] : []) : [];
    if (JSON.stringify(away) !== JSON.stringify(link.away)) { link.away = away; ls.set('cloudssd_away_urls', JSON.stringify(away)); }
    // Every other way to this PC without the internet (its hotspot, a USB cable, another network card).
    const direct = Array.isArray(st.addresses) ? st.addresses.map(a => String(a.url || '')).filter(u => /^http:\/\//.test(u)) : [];
    if (JSON.stringify(direct) !== JSON.stringify(link.direct)) { link.direct = direct; ls.set('cloudssd_direct_urls', JSON.stringify(direct)); }
    link.kind = st.link || '';
    // Tasks made on the phone say how it reaches the PC, and which PC.
    if (isApp && DATA && typeof native.setLinkInfo === 'function') { try { native.setLinkInfo(DATA.taskLink(link.route, link.kind), link.pcName || ''); } catch (e) { } }
    // Files offered to this phone: the sheet comes up when there are new ones.
    if (typeof st.inbox === 'number') {
      if (st.inbox > (inbox.seen || 0)) refreshInbox(true);
      inbox.seen = st.inbox;
    }
    if (isApp) {
      try { native.setServerCandidates(JSON.stringify({ lan: link.lan, away: link.away, pcId: link.pcId, deviceId: link.deviceId, direct: link.direct })); } catch (e) { }
      try { native.setPcInfo(JSON.stringify({ features: st.features || [], backupCustom: !!(st.backup && st.backup.custom) })); } catch (e) { }
    }
    // A phone paired before per-phone keys gets its own key once, without the user doing anything.
    if (st.auth === 'legacy' && pcHas('device-tokens') && !link.upgrading) upgradeKey();
  }
  async function upgradeKey() {
    link.upgrading = true;
    try {
      const r = await api('/api/pair/upgrade', { method: 'POST', params: { installId: installId(), name: deviceName(), kind: isApp ? (appInfo.tablet ? 'tablet' : 'phone') : 'browser' } });
      if (r.ok && r.data && r.data.token) {
        const server = link.server, route = link.route;
        saveCreds(link.lan || server, r.data.token, r.data.deviceId);
        useServer(server, route);
        link.verified = server;
      }
    } catch (e) { /* tried again at the next status */ }
    link.upgrading = false;
  }

  /** GET without the key, with a timeout; the parsed JSON (or null) and the HTTP status. */
  async function getPublic(url, ms) {
    const ctrl = new AbortController();
    const t = setTimeout(() => ctrl.abort(), ms || 3500);
    try {
      const res = await fetch(url, { cache: 'no-store', signal: ctrl.signal });
      let data = null;
      try { data = await res.json(); } catch (e) { }
      return { status: res.status, data };
    } finally { clearTimeout(t); }
  }
  /**
   * Is the PC at this address ours? Checked before the key is ever sent there. 'ours' | 'other' | 'rejected' | 'down'.
   * A PC with /api/hello must answer a fresh challenge with the hash of this phone's key, which a copycat cannot;
   * an older PC (or a phone that does not know its id on the PC yet) is checked by the PC's public id only.
   * 'rejected': it carries our PC's id but does not know this phone's key any more (removed or blocked there).
   */
  async function proveOurPc(url) {
    const D = window.CloudSsdData;
    try {
      if (D && link.token && link.deviceId) {
        const nonce = D.crypto.randomHex(16);
        const h = await getPublic(`${url}/api/hello?nonce=${nonce}&d=${encodeURIComponent(link.deviceId)}`);
        if (h.status === 200 && h.data && h.data.proof) {
          if (link.pcId && h.data.pcId !== link.pcId) return 'other';
          if (D.crypto.sameText(h.data.proof, D.helloProof(link.token, nonce, h.data.pcId || ''))) return 'ours';
          return link.pcId ? 'rejected' : 'other';
        }
        if (h.status === 429) return 'down';
      }
      const s = await getPublic(url + '/api/status');
      const d = s.data;
      if (!d || !d.pcName) return s.status ? 'other' : 'down';
      // A PC that has /api/hello but did not prove itself above is not trusted by its public id alone.
      if (link.deviceId && Array.isArray(d.features) && d.features.includes('hello-proof')) return 'other';
      return (!link.pcId || !d.pcId || d.pcId === link.pcId) ? 'ours' : 'other';
    } catch (e) { return 'down'; }
  }
  async function isOurPc(url) { return (await proveOurPc(url)) === 'ours'; }
  /**
   * The address in use stopped answering: try the PC's other direct addresses first (its hotspot, a USB cable — the
   * ones on a network this phone is on now come first), then Tailscale.
   */
  async function tryDirect() {
    const mine = isApp && typeof native.linkInfo === 'function' ? (() => { try { return JSON.parse(native.linkInfo()).links || []; } catch (e) { return []; } })() : [];
    const near = (u) => mine.some(l => DATA && DATA.sameSubnet && DATA.sameSubnet(l.ip, u));
    const list = (link.direct || []).filter(u => u !== link.server).sort((a, b) => (near(b) ? 1 : 0) - (near(a) ? 1 : 0));
    for (const url of list) {
      if (await isOurPc(url)) {
        useServer(url, 'lan');
        toast('Reaching ' + (link.pcName || 'your PC') + ' at ' + url.replace(/^https?:\/\//, ''));
        pollStatus();
        return true;
      }
    }
    return false;
  }
  /** Home address down: try the PC's Tailscale addresses (away from home, or Wi-Fi without the PC). */
  async function tryAway() {
    for (const url of link.away) {
      if (url === link.server) continue;
      if (await isOurPc(url)) {
        useServer(url, 'tailnet');
        toast('Reaching ' + (link.pcName || 'your PC') + ' through Tailscale');
        pollStatus();
        return true;
      }
    }
    return false;
  }
  async function tryHome() {
    if (!link.lan || link.lan === link.server) return;
    if (await isOurPc(link.lan)) { useServer(link.lan, 'lan'); pollStatus(); }
  }

  function onOffline(reason) {
    if (link.state === 'paused' || link.state === 'unpaired') return;
    if (link.state === 'online') { snapSavedAt = 0; saveSnap({ lastOnline: link.lastOnline }); rememberDevice({ lastOnline: link.lastOnline }); }
    link.fails++;
    link.ping = null;
    link.blocked = false;
    link.verified = '';   // whoever answers next at this address proves again that it is our PC
    link.offlineWhy = reason || '';
    setLinkState('offline');
    renderLink();
    renderStatusTiles();
    if (link.fails === 1 || link.fails % 5 === 0) {
      if (link.route === 'tailnet' && link.lan) tryHome();
      else tryDirect().then(ok => { if (!ok && link.away.length) tryAway(); });
    }
    // The PC's address changes when the router hands out a new one (or the link changed: hotspot, cable): look for
    // it on every network this phone is on now.
    if (isApp && (link.fails === 2 || link.fails % 20 === 0) && !(appInfo.network && appInfo.network.direct === false)) startDiscovery('relocate');
  }
  function onUnpaired() {
    if (link.state === 'unpaired') return;
    link.token = '';
    ls.del('cloudssd_token');
    if (isApp) { try { native.setServerBase(link.lan || link.server, ''); } catch (e) { } }
    setLinkState('unpaired');
    renderStatusTiles();
    const rec = registry && registry.get(currentDeviceId());
    // A guest's time ran out there: it takes a new guest code from that PC.
    if (rec && rec.guestUntil && guestOver(rec.guestUntil)) showPair(`Your guest time on ${link.pcName || 'that PC'} has ended. Ask for a new guest code to come back.`);
    else showPair(link.pcName ? `${link.pcName} no longer accepts this phone. Enter the new pairing code.` : '');
  }
  /**
   * Our PC blocked this phone for now (its Devices page): the key stays, so unblocking brings the phone straight back.
   * It is our PC at its address, so no searching; the status keeps being asked as usual.
   */
  function onBlocked() {
    if (link.state === 'paused' || link.state === 'unpaired') return;
    if (link.state === 'online') { snapSavedAt = 0; saveSnap({ lastOnline: link.lastOnline }); rememberDevice({ lastOnline: link.lastOnline }); }
    link.ping = null;
    link.blocked = true;
    link.offlineWhy = (link.pcName || 'The PC') + ' has blocked this phone for now. Someone at the PC can unblock it on its Devices page.';
    setLinkState('offline');
    renderLink();
    renderStatusTiles();
  }
  /** Some other device (or another PC) answers at our PC's address: keep the key, look for our PC elsewhere. */
  function onOtherPcHere() {
    onOffline('Another device answers at ' + link.server.replace(/^https?:\/\//, '') + ' — looking for ' + (link.pcName || 'your PC'));
    if (isApp && link.fails <= 1) startDiscovery('relocate');
  }
  /**
   * The key was refused (401). Only our own PC refusing it means this phone was removed there; a different PC that
   * now has the address must not make us forget the key.
   */
  let confirmingUnpaired = false;
  async function confirmUnpaired() {
    if (confirmingUnpaired || link.state === 'unpaired') return;
    confirmingUnpaired = true;
    try {
      const s = await getPublic(link.server + '/api/status').catch(() => null);
      const theirs = s && s.data && s.data.pcId;
      if (theirs && link.pcId && theirs !== link.pcId) onOtherPcHere();
      else if (s && s.data && s.data.pcName) {
        // Our PC refused the key: removed there, or only blocked for now (it says so to the key's owner).
        // (Accepted again meanwhile, e.g. just unblocked: nothing to do, the next status poll carries on.)
        const mine = await api('/api/status', { timeout: 4000 }).catch(() => null);
        if (mine && mine.data && mine.data.blocked) onBlocked();
        else if (!(mine && mine.data && mine.data.isPaired)) onUnpaired();
      }
      else onOffline();
    } finally { confirmingUnpaired = false; }
  }
  function onConnected() {
    // Files that waited for this PC go now.
    if (isApp && typeof native.resumeHeldTransfers === 'function') { try { native.resumeHeldTransfers(); } catch (e) { } }
    // The PC can ring and ask this phone again at once (after pairing, unblocking or a drop it waited up to a minute).
    if (isApp && typeof native.peerKick === 'function') { try { native.peerKick(); } catch (e) { } }
    // Kept folders catch up with what changed on the PC.
    if (canKeep()) { refreshKept(); try { native.offlineSync(); } catch (e) { } }
    refreshHistory();
    if (activeTab === 'ssd') openSsdTab();
    if (activeTab === 'gaming') { pollHud(); loadSaves(); }
  }

  function renderLink() {
    const online = link.state === 'online';
    const name = link.pcName || 'PC';
    const dotClass = online ? '' : 'offline';
    $('header-status-dot').className = 'profile-status-dot ' + (online ? 'online' : 'offline');
    $('home-conn-dot').className = 'pc-conn-pulse-dot ' + dotClass;
    $('modal-status-dot').className = 'conn-modal-pulse-dot ' + dotClass;
    // The round icon is this phone's profile (its menu has the devices); its dot shows the PC in use.
    setText('pc-avatar', (meName()[0] || '?').toUpperCase());

    const titles = {
      init: 'Connecting…', online: 'Connected to ' + name, offline: link.blocked ? 'Blocked by ' + name : name + ' is not reachable',
      paused: 'Disconnected from ' + name, unpaired: 'Not paired with a PC'
    };
    const metered = appInfo.network && appInfo.network.metered;
    const via = link.route === 'tailnet' ? ' · via Tailscale' + (metered ? ' (mobile data)' : '')
      : ' · ' + (DATA && DATA.linkLabel ? DATA.linkLabel(link.kind) : 'local Wi-Fi') + ', no mobile data';
    const subs = {
      init: link.server.replace(/^https?:\/\//, ''),
      online: link.server.replace(/^https?:\/\//, '') + via,
      offline: link.offlineWhy || (link.away.length
        ? 'Is Cloud-SSD open on the PC? At home: same Wi-Fi. Away: Tailscale on in this phone.'
        : 'Is Cloud-SSD open on the PC, and both on the same Wi-Fi?') + (cloudSource() === 'drive' ? ' Google Drive works meanwhile.' : ''),
      paused: 'Tap Connect to use the PC again',
      unpaired: 'Tap Connect and enter the code shown on the PC'
    };
    setText('home-conn-title', titles[link.state] || titles.init);
    setText('home-conn-sub', subs[link.state] || '');
    const btn = $('home-conn-toggle-btn'), mbtn = $('modal-connect-btn');
    btn.className = 'pc-conn-toggle-btn ' + (online ? 'disconnect' : 'connect');
    btn.textContent = online ? 'Disconnect' : link.state === 'offline' ? 'Retry' : 'Connect';
    mbtn.className = 'conn-master-btn ' + (online ? 'disconnect' : 'connect');
    mbtn.textContent = online ? 'Disconnect from PC' : link.state === 'offline' ? 'Try again' : 'Connect to PC';

    const tag = $('modal-status-tag');
    tag.className = 'conn-status-tag ' + (online ? 'connected' : 'offline');
    tag.textContent = { online: 'Connected', offline: 'PC not reachable', paused: 'Disconnected', unpaired: 'Not paired', init: 'Connecting…' }[link.state];
    setText('modal-pc-name', link.pcName || 'No PC');
    setText('modal-pc-os', (link.status && link.status.os ? link.status.os : 'Cloud-SSD') + (link.status && link.status.version ? ' · Cloud-SSD ' + link.status.version : ''));
    setText('modal-pc-ip', link.server ? link.server.replace(/^https?:\/\//, '') : '—');
    setText('modal-phone-ip', appInfo.phoneIp || (isApp ? 'Wi-Fi is off' : 'this browser'));
    // On a USB cable but USB tethering is off: one tap to Android's setting, then the PC is found over the cable.
    let usb = null;
    if (isApp && typeof native.linkInfo === 'function') { try { usb = JSON.parse(native.linkInfo()).usb; } catch (e) { } }
    show('usb-tether-hint', !online && !!usb && usb.connected && !usb.tethering);
    const ping = $('home-conn-ping');
    ping.style.display = online && link.ping != null ? '' : 'none';
    ping.textContent = link.ping + ' ms';
    setText('modal-ping-val', online && link.ping != null ? link.ping + ' ms' : '—');
    const st = link.status;
    if (online && st) {
      const drives = st.drives || [];
      setText('modal-drives-val', drives.length ? drives.map(d => d.driveLetter + (d.isMounted ? ' on' : ' off')).join(' · ') : (st.drivesCount || 0) + ' online');
      // A current PC counts this phone's own bytes, all time; an older one only the whole PC's since it started.
      const mine = st.myBytes;
      setText('modal-bytes-label', mine ? 'This phone with this PC (all time)' : 'Sent / received this PC session');
      setText('modal-bytes-val', mine ? `↓ ${fmtBytes(mine.toDevice || 0)} · ↑ ${fmtBytes(mine.fromDevice || 0)}`
        : st.stats ? fmtBytes(st.stats.totalTransferredBytes || 0) : '—');
    } else {
      setText('modal-drives-val', '—');
      setText('modal-bytes-val', '—');
    }
    renderUptime();
    setText('gaming-status-badge', online ? 'CONNECTED' : 'OFFLINE');
    $('gaming-status-badge').className = 'gaming-badge' + (online ? '' : ' offline');
    show('gaming-live-tag', online);
  }
  function renderUptime() {
    const online = link.state === 'online';
    const text = online ? fmtDuration((Date.now() - link.since) / 1000) : '—';
    setText('home-conn-time', online ? 'Connected for ' + text : { offline: 'Offline', paused: 'Paused', unpaired: 'Not paired', init: '…' }[link.state] || '—');
    setText('modal-conn-uptime', text);
  }

  // ------------------------------------------------------------------ home tiles
  function renderPhoneTiles() {
    if (!isApp) {
      setText('hw-ram-pct', 'App only'); setText('hw-ram-val', '—');
      setText('hw-phone-free', 'App only'); setText('hw-phone-val', '—');
      return;
    }
    let s;
    try { s = JSON.parse(native.getPhoneStats()); } catch (e) { return; }
    if (s.ramTotal > 0) {
      const p = pct(s.ramUsed, s.ramTotal);
      setText('hw-ram-pct', Math.round(p) + '%');
      setText('hw-ram-val', `${fmtBytes(s.ramUsed)} / ${fmtBytes(s.ramTotal)}`);
      setWidth('hw-ram-bar', p);
    }
    if (s.storageTotal > 0) {
      setText('hw-phone-free', fmtDec(s.storageFree) + ' free');
      setText('hw-phone-val', `${fmtDec(s.storageUsed)} / ${fmtDec(s.storageTotal)}`);
      setWidth('hw-phone-bar', pct(s.storageUsed, s.storageTotal));
      setText('browser-storage-free', fmtDec(s.storageFree) + ' free of ' + fmtDec(s.storageTotal));
      phone.totals = s;
    }
    if (s.storageAccess && s.storageAccess !== phone.access) { phone.access = s.storageAccess; renderPermBanner(); }
  }

  // ---- the PC's RAM and Windows disk on Home (live; the last numbers with their time when the PC is away) ----
  let pcLive = ls.json('cloudssd_pc_live', null);
  let pcLiveBusy = false;
  async function refreshPcLive() {
    if (pcLiveBusy || link.state !== 'online' || !pcHas('space')) { renderPcTiles(); return; }
    pcLiveBusy = true;
    try {
      const r = await api('/api/space/live', { timeout: 4000 });
      if (r.ok && r.data) { pcLive = Object.assign({ seen: Date.now() }, r.data); ls.set('cloudssd_pc_live', JSON.stringify(pcLive)); }
    } catch (e) { /* the last numbers stay */ }
    pcLiveBusy = false;
    renderPcTiles();
  }
  function renderPcTiles() {
    const live = link.state === 'online' && pcHas('space') && pcLive && Date.now() - pcLive.seen < 15000;
    if (!pcLive) {
      setText('hw-pcram-pct', link.state === 'online' ? (pcHas('space') ? '…' : 'Update PC') : 'PC offline'); setText('hw-pcram-val', '—'); setWidth('hw-pcram-bar', 0);
      setText('hw-pcdisk-free', link.state === 'online' ? '…' : 'PC offline'); setText('hw-pcdisk-val', '—'); setWidth('hw-pcdisk-bar', 0);
      return;
    }
    const ram = pcLive.ram || {}, disk = pcLive.disk || {};
    const ramUsed = ram.total - ram.available, diskUsed = disk.total - disk.free;
    setText('hw-pcram-pct', live ? Math.round(pct(ramUsed, ram.total)) + '%' : 'seen ' + timeAgo(pcLive.seen));
    setText('hw-pcram-val', `${fmtBytes(ramUsed)} / ${fmtBytes(ram.total)}`);
    setWidth('hw-pcram-bar', pct(ramUsed, ram.total));
    setText('hw-pcdisk-free', live ? fmtBytes(disk.free) + ' free' : 'seen ' + timeAgo(pcLive.seen));
    setText('hw-pcdisk-val', `${fmtBytes(diskUsed)} / ${fmtBytes(disk.total)}`);
    setWidth('hw-pcdisk-bar', pct(diskUsed, disk.total));
    ['hw-pcram-pct', 'hw-pcdisk-free'].forEach(id => { const el = $(id); if (el) el.classList.toggle('stale', !live); });
  }

  /**
   * Space over all accounts. "Free" is what each provider reports (for Google it already subtracts Gmail and
   * Photos), and "used" is the rest, so used + free always equals the total shown.
   */
  function cloudTotals(c) {
    if (!c) return null;
    const accounts = (c.accounts || []).map(a => {
      if (!(a.totalBytes > 0)) return a;
      const free = a.freeBytes != null ? a.freeBytes : a.totalBytes - (a.usedBytes || 0);
      return Object.assign({}, a, { freeBytes: free, usedBytes: a.totalBytes - free });
    });
    const known = accounts.filter(a => a.totalBytes > 0);
    const total = known.reduce((s, a) => s + a.totalBytes, 0);
    const free = known.reduce((s, a) => s + a.freeBytes, 0);
    return Object.assign({}, c, { accounts, totalBytes: total, freeBytes: free, usedBytes: total - free });
  }

  function renderStatusTiles() {
    const st = link.status;
    const online = link.state === 'online' && st;
    // The PC is away but its last numbers are on this phone: show them, marked with when they were seen.
    if (!online && st && link.pcId && link.state !== 'unpaired' && cloudSource() !== 'drive') { renderTilesFrom(st, false); return; }
    ['hw-ssd-badge', 'hw-pc-status-badge'].forEach(id => { const el = $(id); if (el) el.classList.remove('stale'); });
    if (!online) {
      const why = { offline: 'PC offline', paused: 'Disconnected', unpaired: 'Not paired', init: '…' }[link.state] || '—';
      const accs = cloudSource() === 'drive' ? (drive.accounts || []).filter(a => a.totalBytes > 0) : [];
      if (accs.length) {
        const total = accs.reduce((s, a) => s + a.totalBytes, 0), free = accs.reduce((s, a) => s + (a.freeBytes || 0), 0);
        setText('hw-ssd-badge', fmtBytes(free) + ' free'); setText('hw-ssd-val', `${fmtBytes(total - free)} / ${fmtBytes(total)} · Google`);
        setWidth('hw-ssd-bar', pct(total - free, total));
      } else { setText('hw-ssd-badge', why); setText('hw-ssd-val', '—'); setWidth('hw-ssd-bar', 0); }
      setText('hw-pc-status-badge', why); setText('hw-pc-drives-val', '—'); setWidth('hw-pc-bar', 0);
      show('laptop-storage-saver-card', false);
      show('home-game-banner', false);
      return;
    }
    renderTilesFrom(st, true);
  }

  /** The Cloud SSD, PC drives and laptop tiles from a status: live, or the saved one from when the PC was last seen. */
  function renderTilesFrom(st, live) {
    const seen = live ? '' : (link.state === 'init' ? 'connecting…' : 'seen ' + shortAgo(link.lastOnline));
    ['hw-ssd-badge', 'hw-pc-status-badge'].forEach(id => { const el = $(id); if (el) el.classList.toggle('stale', !live); });

    // Cloud SSD: real quota of the connected accounts.
    const c = cloudTotals(st.cloud);
    if (c && c.totalBytes > 0) {
      setText('hw-ssd-badge', fmtBytes(c.freeBytes) + ' free');
      setText('hw-ssd-val', `${fmtBytes(c.usedBytes)} / ${fmtBytes(c.totalBytes)}`);
      setWidth('hw-ssd-bar', Math.max(pct(c.usedBytes, c.totalBytes), c.usedBytes > 0 ? 1.5 : 0));
    } else if (c && c.accountsCount === 0) {
      setText('hw-ssd-badge', 'No account'); setText('hw-ssd-val', 'Add one on the PC'); setWidth('hw-ssd-bar', 0);
    } else {
      setText('hw-ssd-badge', c ? c.accountsCount + ' accounts' : 'Online'); setText('hw-ssd-val', 'Reading sizes…'); setWidth('hw-ssd-bar', 0);
    }

    if (!live && c) setText('hw-ssd-badge', seen);

    // PC drives: which letters are really connected.
    const drives = st.drives || [];
    const on = drives.filter(d => d.isMounted);
    setText('hw-pc-status-badge', live ? (link.pcName || 'PC') : seen);
    if (drives.length) {
      setText('hw-pc-drives-val', on.length ? on.map(d => d.driveLetter).join(' ') + (live ? ' online' : ' (last seen)') : 'All disconnected');
      setWidth('hw-pc-bar', pct(on.length, drives.length));
    } else {
      setText('hw-pc-drives-val', (st.drivesCount || 0) + ' online');
      setWidth('hw-pc-bar', st.drivesCount ? 100 : 0);
    }

    // Laptop space saver.
    const sv = st.storage;
    show('laptop-storage-saver-card', !!sv);
    if (sv) {
      setText('laptop-free-pill', sv.laptopFreeGb + ' GB free');
      setText('laptop-threshold-pill', 'Starts below ' + sv.thresholdGb + ' GB');
      // The PC says "No cloud drive connected" in place of a drive name when there is none.
      const target = sv.targetDrive && !/^No cloud drive/i.test(sv.targetDrive) ? sv.targetDrive : '';
      setText('laptop-storage-sub', !sv.autoOffloadEnabled ? 'Automatic moving is switched off on the PC'
        : target ? 'Moves old files to ' + target + ' when the laptop is nearly full'
        : 'Connect a cloud drive on the PC to move old files there');
      const pill = $('laptop-status-pill');
      pill.textContent = sv.isUnderThreshold ? 'Low space' : sv.autoOffloadEnabled ? 'On' : 'Off';
      pill.className = 'pc-conn-pill ' + (sv.isUnderThreshold ? 'warn' : 'ping');
    }

    show('home-game-banner', live && !!(st.gameRunning && st.activeGame));
    if (live && st.gameRunning && st.activeGame) setText('banner-game-name', st.activeGame);
    if (live && !ssd.path && activeTab === 'ssd') openSsdTab();
    renderSsdDrives();
  }

  // ------------------------------------------------------------------ sheets
  const SHEETS = ['connection-modal', 'file-actions-sheet', 'file-viewer-modal', 'ram-breakdown-modal', 'storage-modal', 'cloud-modal',
    'drives-manager-modal', 'transfers-modal', 'history-sheet', 'sort-sheet', 'confirm-modal', 'settings-modal', 'details-sheet', 'choice-sheet'];
  const openSheets = [];
  function openSheet(id) {
    const el = $(id);
    if (!el) return;
    el.classList.add('active');
    const i = openSheets.indexOf(id);
    if (i >= 0) openSheets.splice(i, 1);
    openSheets.push(id);
    document.body.classList.add('sheet-open');
  }
  function closeSheet(id) {
    const el = $(id);
    if (!el) return;
    el.classList.remove('active');
    const i = openSheets.indexOf(id);
    if (i >= 0) openSheets.splice(i, 1);
    if (!openSheets.length) document.body.classList.remove('sheet-open');
    if (id === 'file-viewer-modal') stopViewer();
    if (id === 'confirm-modal' && confirmResolve) { confirmResolve(false); confirmResolve = null; }
    // Closed with Back while asking the PC: the question is taken back.
    if (id === 'access-sheet' && access.finish) { if (access.cancel) access.cancel(); else access.finish(false, true); }
  }
  function isOpen(id) { return openSheets.includes(id); }

  let confirmResolve = null;
  function confirmDialog(title, text, yesLabel) {
    setText('confirm-title', title);
    setText('confirm-text', text);
    setText('confirm-yes-btn', yesLabel || 'OK');
    if (confirmResolve) { confirmResolve(false); confirmResolve = null; }
    openSheet('confirm-modal');
    return new Promise(resolve => { confirmResolve = resolve; });
  }

  /** A short list of choices in a bottom sheet: [{icon, title, sub, fn}]. */
  let choices = [];
  let choicesAt = 0;
  function choiceSheet(title, list) {
    choices = list;
    choicesAt = Date.now();
    setText('choice-title', title);
    $('choice-list').innerHTML = list.map((a, i) => `
      <div class="m3-sheet-row ${a.danger ? 'danger' : ''}" data-act="choice-pick" data-i="${i}">
        ${a.icon || ''}
        <div class="m3-sheet-row-text"><div>${esc(a.title)}</div>${a.sub ? `<div class="m3-sheet-row-sub">${esc(a.sub)}</div>` : ''}</div>
      </div>`).join('');
    openSheet('choice-sheet');
  }

  // ------------------------------------------------------------------ pairing
  const pair = { pcs: [], selected: '', scanning: false, purpose: '' };

  function showPair(message) {
    $('pair-overlay').style.display = 'flex';
    const err = $('pair-error');
    err.style.display = message ? '' : 'none';
    err.textContent = message || '';
    if (isApp) {
      startDiscovery('pair');
    } else if (/^https?:/.test(location.protocol)) {
      // Opened from the PC itself: that PC is the one to pair with.
      pair.pcs = [{ url: location.origin, ip: location.host, pcName: link.pcName || 'This PC' }];
      pair.selected = location.origin;
      fetch(location.origin + '/api/status').then(r => r.json()).then(d => { if (d.pcName) { pair.pcs[0].pcName = d.pcName; renderPairPcs(); } }).catch(() => { });
    }
    renderPairPcs();
  }
  function hidePair() { $('pair-overlay').style.display = 'none'; }

  function startDiscovery(purpose) {
    if (!isApp) return;
    pair.purpose = purpose;
    pair.scanning = true;
    if (purpose === 'pair') { renderPairPcs(); }
    let port = 8090;
    const m = /:(\d+)$/.exec(link.server);
    if (m) port = parseInt(m[1], 10);
    try { native.discoverPcs(port); } catch (e) { pair.scanning = false; }
  }
  function onDiscovery(d) {
    pair.pcs = d.found || [];
    pair.noWifi = !!d.noWifi;
    if (d.done) pair.scanning = false;
    if (!pair.selected && pair.pcs.length) pair.selected = pair.pcs[0].url;
    if (pair.selected && !pair.pcs.some(p => p.url === pair.selected)) pair.selected = pair.pcs.length ? pair.pcs[0].url : '';
    renderPairPcs();
    if (pair.purpose === 'relocate' && d.done) relocate(pair.pcs);
  }
  /**
   * Our PC got a new address. Found PCs whose public id is not ours are skipped, and the key is only sent to an
   * address after it proved it is our PC (never to every device that happens to answer on the port).
   */
  async function relocate(pcs) {
    if (link.state !== 'offline' || !link.token) return;
    const candidates = pcs.filter(pc => pc.url !== link.server && !(link.pcId && pc.pcId && pc.pcId !== link.pcId));
    for (const pc of candidates) {
      if ((await proveOurPc(pc.url)) !== 'ours') continue;
      try {
        const res = await fetch(pc.url + '/api/status?token=' + encodeURIComponent(link.token), { cache: 'no-store' });
        const data = await res.json();
        if (data && data.isPaired) {
          saveCreds(pc.url, link.token);
          link.verified = pc.url;
          toast('Found ' + (data.pcName || 'your PC') + ' at its new address');
          pollStatus();
          return;
        }
      } catch (e) { /* not ours */ }
    }
  }
  function renderPairPcs() {
    const box = $('pair-pc-list');
    if (!box) return;
    let html = pair.pcs.map(p => `
      <button class="pair-pc ${p.url === pair.selected ? 'selected' : ''}" data-act="pair-pick" data-url="${esc(p.url)}">
        <span class="pair-pc-icon">${SVG.pc}</span>
        <span class="pair-pc-text"><strong>${esc(p.pcName)}</strong><span>${esc(p.ip)}</span></span>
        <span class="pair-pc-check">${SVG.check}</span>
      </button>`).join('');
    if (pair.scanning) html += '<div class="pair-scan"><span class="spinner"></span>Looking for PCs on this Wi-Fi…</div>';
    else if (!pair.pcs.length) {
      html += `<div class="pair-scan none">${isApp
        ? (pair.noWifi ? 'This phone is not on Wi-Fi. Join the same Wi-Fi as the PC (or connect the PC to this phone\'s hotspot), then tap Search.'
          : 'No PC found yet. Open Cloud-SSD on the PC, make sure both are on the same Wi-Fi, then tap Search — or type the PC address below.')
        : 'Type the address shown in Cloud-SSD on the PC.'}</div>`;
    }
    box.innerHTML = html;
    const btn = $('pair-rescan-btn');
    if (btn) btn.textContent = pair.scanning ? '…' : 'Search';
  }
  async function submitPair() {
    const err = $('pair-error');
    const fail = (m) => { err.textContent = m; err.style.display = ''; $('pair-submit-btn').disabled = false; $('pair-submit-btn').textContent = 'Connect'; };
    const typed = normalizeServer($('pair-address').value);
    const url = typed || pair.selected;
    const code = $('pair-code').value.replace(/\D/g, '');
    if (!url) return fail('Choose your PC from the list or type its address.');
    if (code.length !== 6) return fail('Enter the 6-digit pairing code shown on the PC.');
    err.style.display = 'none';
    $('pair-submit-btn').disabled = true;
    $('pair-submit-btn').textContent = 'Connecting…';
    try {
      const res = await fetch(`${url}/api/pair?code=${code}&${pairQuery()}`, { method: 'POST', cache: 'no-store' });
      let data = {};
      try { data = await res.json(); } catch (e) { }
      if (res.ok && data.token) {
        adoptPairing(url, data);
        $('pair-code').value = '';
        $('pair-submit-btn').disabled = false;
        $('pair-submit-btn').textContent = 'Connect';
        hidePair();
        link.state = 'init';
        toast('Paired with ' + (link.pcName || 'PC'));
        pollStatus();
      } else if (res.status === 404) {
        fail('This PC runs an older Cloud-SSD without pairing codes. Update Cloud-SSD on the PC.');
      } else {
        fail(data.error || 'The PC refused the code (HTTP ' + res.status + ').');
      }
    } catch (e) {
      fail('Could not reach ' + url.replace(/^https?:\/\//, '') + '. Is Cloud-SSD open on the PC and is this the same Wi-Fi?');
    }
  }
  /**
   * A PC accepted our code: it becomes a saved device and the active one. Pairing with another PC adds it to the list —
   * the PCs paired before stay saved, with their keys and their saved data.
   */
  function adoptPairing(url, data) {
    url = String(url || '').replace(/\/+$/, '');
    const newId = data.pcId || 'url:' + url;
    const switching = newId !== currentDeviceId();
    if (switching) leaveDevice();
    // The saved entry first (in the app this also makes it the active one on the native side), then the single values.
    if (registry) registry.upsert({ id: newId, name: data.pcName || '', kind: 'pc', lan: url, last: url, deviceId: data.deviceId || '' }, data.token, true);
    saveCreds(url, data.token, data.deviceId);
    link.verified = link.server;   // it just took our code: this address is our PC
    if (data.pcName) { link.pcName = data.pcName; ls.set('cloudssd_pc_name', data.pcName); }
    if (data.pcId) { link.pcId = data.pcId; ls.set('cloudssd_pc_id', data.pcId); } else { link.pcId = ''; ls.del('cloudssd_pc_id'); }
    if (switching) restoreLastTime();
  }

  /**
   * A pairing link: {host, pair} (the PC's one-time QR secret, swapped here for this phone's own key) or, from an
   * older PC, {host, token}.
   */
  async function applyPairLink(o) {
    if (!o || !o.host || !(o.token || o.pair)) return false;
    const url = normalizeServer(o.host);
    if (o.pair) {
      try {
        const res = await fetch(`${url}/api/pair?pair=${encodeURIComponent(o.pair)}&${pairQuery()}`, { method: 'POST', cache: 'no-store' });
        const data = await res.json().catch(() => ({}));
        if (!res.ok || !data.token) { showPair(data.error || 'This pairing link could not be used. Enter the 6-digit code instead.'); return false; }
        adoptPairing(url, data);
      } catch (e) { showPair('Could not reach the PC. Is this the same Wi-Fi?'); return false; }
    } else saveCreds(url, o.token);
    hidePair();
    link.state = 'init';
    pollStatus();
    return true;
  }

  // ------------------------------------------------------------------ transfers
  const transfers = new Map();   // id → live transfer (from the app, or a browser upload)
  let xferHistory = [];          // finished transfers as recorded by the PC
  let historyFilter = 'all';
  /** "all" (grouped by kind of file) or one kind: photos, videos, music, documents, apps, others. */
  let historyKind = 'all';
  const KIND_TITLES = { photos: 'Photos', videos: 'Videos', music: 'Music', documents: 'Documents', apps: 'Apps & archives', others: 'Others' };
  let localXferId = 0;

  function onTransfer(t) {
    const isNew = !transfers.has(t.id);
    const prev = transfers.get(t.id) || {};
    const cur = Object.assign(prev, t);
    // A transfer that just started on Home must be seen without scrolling.
    if (isNew && activeTab === 'home' && !openSheets.length) {
      setTimeout(() => { const s = $('transfers-section'); if (s) s.scrollIntoView({ block: 'nearest', behavior: 'smooth' }); }, 50);
    }
    if (!cur.startedAt && t.state === 'active') cur.startedAt = Date.now();
    if (t.state === 'waiting' && t.retryIn != null) cur.retryAt = Date.now() + t.retryIn * 1000;
    transfers.set(t.id, cur);
    const was = prev.state;
    if (t.state === 'done' && was !== 'done') {
      // Many files at once: one toast for the batch instead of one per file.
      const running = Array.from(transfers.values()).filter(x => x.state === 'active' || x.state === 'queued').length;
      if (running >= 2) xferDoneBatch++;
      else {
        toast(xferDoneBatch > 0 ? `${xferDoneBatch + 1} files done` : (cur.note ? cur.note + ': ' : (cur.type === 'upload' ? 'Sent to PC: ' : 'Saved to Downloads: ')) + cur.name);
        xferDoneBatch = 0;
      }
      setTimeout(() => { transfers.delete(t.id); renderTransfers(); }, 4000);
      setTimeout(refreshHistory, 600);
      if (cur.type === 'download') phone.report = null; // a new file landed on the phone
    } else if (t.state === 'cancelled') {
      setTimeout(() => { transfers.delete(t.id); renderTransfers(); }, 2500);
    } else if (t.state === 'failed' && was !== 'failed') {
      toast((cur.type === 'upload' ? 'Could not send ' : 'Could not download ') + cur.name + (cur.canRetry !== false ? ' — tap Retry' : ''));
    } else if (t.state === 'held' && was !== 'held') {
      toast(cur.name + ' waits for ' + (link.pcName || 'the PC') + ' — it goes as soon as the PC is back');
    }
    renderTransfers();
  }
  let xferDoneBatch = 0;

  function transferLine(t) {
    const dir = t.type === 'upload' ? 'To PC' : 'To phone';
    if (t.state === 'queued') return dir + ' · waiting…';
    if (t.state === 'done') return dir + ' · done · ' + fmtBytes(t.bytes);
    if (t.state === 'cancelled') return dir + ' · cancelled';
    if (t.state === 'waiting') {
      const s = t.retryAt ? Math.max(0, Math.round((t.retryAt - Date.now()) / 1000)) : 0;
      return 'Connection lost · trying again' + (s > 0 ? ' in ' + s + ' s' : '…');
    }
    // The PC is away: it waits, and goes by itself when the PC is back.
    if (t.state === 'held') return t.error || 'Waiting for the PC · sends when it is back';
    if (t.state === 'failed') {
      const where = t.total > 0 && t.bytes > 0 ? `Stopped at ${Math.floor(pct(t.bytes, t.total))}% · ` : '';
      return where + (t.error || 'Failed') + (t.resumable ? ' · Retry continues from there' : '');
    }
    const parts = [dir, fmtBytes(t.bytes) + (t.total > 0 ? ' / ' + fmtBytes(t.total) : '')];
    if (t.speed > 0) {
      parts.push(fmtBytes(t.speed) + '/s');
      if (t.total > 0) parts.push(fmtDuration((t.total - t.bytes) / t.speed) + ' left');
    }
    return parts.join(' · ');
  }

  function renderTransfers() {
    const list = Array.from(transfers.values()).sort((a, b) => Math.abs(a.id) - Math.abs(b.id));
    const busy = list.some(t => t.state === 'active' || t.state === 'queued');
    show('transfers-live-dot', busy);
    for (const boxId of ['transfers-active', 'transfers-modal-active']) {
      const box = $(boxId);
      if (!box) continue;
      const seen = new Set();
      for (const t of list) {
        seen.add(String(t.id));
        let card = box.querySelector(`[data-xid="${t.id}"]`);
        if (!card) {
          card = document.createElement('div');
          card.className = 'xfer-card';
          card.dataset.xid = t.id;
          card.innerHTML = `<div class="xfer-icon"></div>
            <div class="xfer-body">
              <div class="xfer-top"><span class="xfer-name"></span><span class="xfer-pct"></span></div>
              <div class="transfer-progress-track"><div class="transfer-progress-fill"></div></div>
              <div class="xfer-sub"></div>
            </div>
            <button class="xfer-retry" data-act="xfer-retry" data-id="${t.id}" title="Retry">Retry</button>
            <button class="xfer-cancel" data-act="xfer-cancel" data-id="${t.id}">${SVG.close}</button>`;
          box.appendChild(card);
        }
        const p = t.state === 'done' ? 100 : pct(t.bytes, t.total);
        card.className = 'xfer-card ' + t.type + ' ' + t.state;
        card.querySelector('.xfer-icon').innerHTML = t.state === 'done' ? SVG.check : t.type === 'upload' ? SVG.upload : SVG.download;
        card.querySelector('.xfer-name').textContent = t.name;
        card.querySelector('.xfer-pct').textContent = t.state === 'active' && t.total > 0 ? Math.floor(p) + '%' : t.state === 'done' ? '100%' : '';
        const fill = card.querySelector('.transfer-progress-fill');
        fill.style.width = p + '%';
        fill.classList.toggle('indeterminate', t.state === 'active' && !(t.total > 0));
        card.querySelector('.xfer-sub').textContent = transferLine(t);
        card.querySelector('.xfer-cancel').title = t.state === 'failed' ? 'Dismiss' : 'Cancel';
        card.querySelector('.xfer-cancel').style.display = t.state === 'done' || t.state === 'cancelled' ? 'none' : '';
        card.querySelector('.xfer-retry').style.display = (t.state === 'failed' || t.state === 'held') && t.canRetry !== false ? '' : 'none';
      }
      box.querySelectorAll('[data-xid]').forEach(el => { if (!seen.has(el.dataset.xid)) el.remove(); });
    }
    renderTransferPill(list);
  }

  /** A small pill above the bottom bar on every tab, so a transfer started anywhere stays visible. */
  function renderTransferPill(list) {
    const pill = $('transfer-pill');
    if (!pill) return;
    const running = list.filter(t => t.state === 'active' || t.state === 'queued');
    if (!running.length || (activeTab === 'home' && !openSheets.length)) { pill.style.display = 'none'; return; }
    const cur = running.find(t => t.state === 'active') || running[0];
    pill.style.display = 'flex';
    const p = pct(cur.bytes, cur.total);
    pill.querySelector('.transfer-pill-text').textContent =
      (cur.type === 'upload' ? 'Sending ' : 'Downloading ') + (running.length > 1 ? running.length + ' files' : cur.name) +
      (cur.total > 0 ? ' · ' + Math.floor(p) + '%' : '') + (cur.speed > 0 ? ' · ' + fmtBytes(cur.speed) + '/s' : '');
    pill.querySelector('.transfer-pill-fill').style.width = p + '%';
  }

  async function refreshHistory() {
    if (link.state !== 'online') return;
    try {
      const r = await api('/api/transfers');
      if (r.ok && Array.isArray(r.data)) { xferHistory = r.data; saveSnap({ history: r.data.slice(0, 100) }); renderHistory(); }
    } catch (e) { /* keep the last list */ }
  }
  const DIRS = {
    upload: { label: 'Sent to PC', cls: 'sent', icon: SVG.upload },
    download: { label: 'Saved to phone', cls: 'received', icon: SVG.download },
    stream: { label: 'Streamed', cls: 'stream', icon: SVG.stream },
    offload: { label: 'Space saver', cls: 'offload', icon: SVG.cloud }
  };
  function historyRow(t, i) {
    const d = DIRS[t.direction] || DIRS.upload;
    const failed = t.status && !/^Completed/i.test(t.status);
    return `<div class="transfer-card-item" data-act="history-open" data-i="${i}">
      <div class="tc-left">
        <div class="tc-icon-wrap ${d.cls}">${d.icon}</div>
        <div class="tc-info">
          <div class="tc-name">${esc(t.fileName)}</div>
          <div class="tc-meta">
            <span class="tc-dir-badge ${d.cls}">${d.label}</span>
            <span>${esc(t.sizeFormatted || fmtBytes(t.bytes))}</span>
            ${t.speedMBps > 0.1 && t.direction !== 'offload' ? `<span class="tc-speed-pill">${t.speedMBps} MB/s</span>` : ''}
            <span>${esc(timeAgo(t.timestamp))}</span>
            ${failed ? `<span class="tc-failed">${esc(t.status)}</span>` : ''}
          </div>
        </div>
      </div>
    </div>`;
  }
  function renderHistory() {
    const recent = $('transfers-recent');
    const mine = xferHistory.filter(t => t.direction !== 'offload');
    if (recent) {
      recent.innerHTML = mine.length
        ? mine.slice(0, 5).map(t => historyRow(t, xferHistory.indexOf(t))).join('')
        : '<div class="recent-empty-card">Nothing sent yet. Use "Send Any File" to send something to the PC.</div>';
    }
    const all = $('transfers-modal-list');
    if (all) {
      const rows = xferHistory.filter(t => (historyFilter === 'all' || t.direction === historyFilter)
        && (historyKind === 'all' || DATA.transferKind(t.fileName) === historyKind)).slice(0, 100);
      // All kinds: one block per kind of file ("Photos · 12"), not one mixed list.
      all.innerHTML = !rows.length ? '<div class="recent-empty-card">No transfers here yet.</div>'
        : historyKind !== 'all' ? rows.map(t => historyRow(t, xferHistory.indexOf(t))).join('')
        : DATA.groupTransfers(rows, t => t.fileName).map(g =>
          `<div class="history-group-title">${esc(KIND_TITLES[g.kind])} · ${g.items.length}</div>` + g.items.map(t => historyRow(t, xferHistory.indexOf(t))).join('')).join('');
    }
    setText('transfers-sub', xferHistory.length + ' recorded on ' + (link.pcName || 'the PC') +
      (link.state !== 'online' && link.lastOnline && xferHistory.length ? ' · saved ' + lastUpdate(link.lastOnline) : ''));
  }

  function sendFiles(kind) {
    if (link.state === 'unpaired' || !link.token) { toast('Pair with your PC first'); showPair(''); return; }
    // In the app a file chosen while the PC is away waits for it and goes by itself when it is back.
    if (link.state !== 'online' && !isApp) { toast('PC is not connected'); return; }
    if (link.state !== 'online') toast('The PC is away — chosen files wait and go when it is back');
    if (isApp) {
      try { native.pickAndUploadFiles(kind); } catch (e) { toast('Cannot open the file picker'); }
    } else {
      $(kind === 'photos' ? 'photo-uploader' : 'file-uploader').click();
    }
  }
  function randomHex(n) {
    const b = new Uint8Array(n);
    crypto.getRandomValues(b);
    return Array.from(b, x => x.toString(16).padStart(2, '0')).join('');
  }
  /**
   * Browser (no app): upload with XMLHttpRequest so the same live progress card can be shown. On a PC that can
   * continue uploads, a broken one goes on from where it stopped when Retry is tapped.
   */
  async function uploadInBrowser(file, existingId) {
    const id = existingId || --localXferId;
    const prev = transfers.get(id) || {};
    const uploadId = prev.uploadId || (pcHas('upload-resume') ? randomHex(16) : '');
    onTransfer({ id, type: 'upload', name: file.name, state: 'queued', bytes: prev.bytes || 0, total: file.size, speed: 0, file, uploadId, canRetry: true });
    let offset = 0;
    if (uploadId && existingId) {
      try {
        const st = await api('/api/upload/status', { params: { uploadId } });
        if (st.ok && st.data && st.data.total === file.size) offset = st.data.offset || 0;
      } catch (e) { /* start over */ }
    }
    const xhr = new XMLHttpRequest();
    let lastT = Date.now(), lastB = 0;
    const params = { filename: file.name };
    if (uploadId) Object.assign(params, { uploadId, offset, total: file.size });
    xhr.open('POST', apiUrl('/api/upload', params));
    xhr.upload.onprogress = (e) => {
      const now = Date.now();
      const t = transfers.get(id) || {};
      let speed = t.speed || 0;
      if (now - lastT >= 300) { speed = (e.loaded - lastB) * 1000 / (now - lastT); lastT = now; lastB = e.loaded; }
      onTransfer({ id, type: 'upload', name: file.name, state: 'active', bytes: offset + e.loaded, total: file.size, speed });
    };
    xhr.onload = () => {
      if (xhr.status === 200) onTransfer({ id, type: 'upload', name: file.name, state: 'done', bytes: file.size, total: file.size, speed: 0 });
      else {
        let msg = 'PC answered HTTP ' + xhr.status;
        try { msg = JSON.parse(xhr.responseText).error || msg; } catch (e) { }
        onTransfer({ id, type: 'upload', name: file.name, state: 'failed', total: file.size, speed: 0, error: msg, resumable: !!uploadId });
      }
    };
    xhr.onerror = () => onTransfer({ id, type: 'upload', name: file.name, state: 'failed', total: file.size, speed: 0, error: 'PC not reachable', resumable: !!uploadId });
    xhr.onabort = () => onTransfer({ id, type: 'upload', name: file.name, state: 'cancelled', bytes: 0, total: file.size, speed: 0 });
    transfers.get(id).xhr = xhr;
    xhr.send(offset > 0 ? file.slice(offset) : file);
  }
  function cancelTransfer(id) {
    const t = transfers.get(id);
    if (!t) return;
    if (t.state === 'failed') {
      transfers.delete(id);
      if (isApp && id > 0) { try { native.dismissTransfer(id); } catch (e) { } }
      renderTransfers();
      return;
    }
    if (t.xhr) t.xhr.abort();
    else if (isApp) { try { native.cancelTransfer(id); } catch (e) { } }
  }
  function retryTransfer(id) {
    const t = transfers.get(id);
    if (!t) return;
    if (t.file) { uploadInBrowser(t.file, id); return; }
    if (isApp && typeof native.retryTransfer === 'function') { try { native.retryTransfer(id); } catch (e) { toast('Could not retry'); } }
    else toast('Send the file again');
  }
  // ------------------------------------------------------------------ transfers as tasks (shown by transfers.js)
  /** The transfers as tasks (an offer, a folder, a batch), newest first; the native side sends each change. */
  let transferTasks = [];
  function showTasks() { if (window.CSSD_renderTasks) { try { window.CSSD_renderTasks(transferTasks); } catch (e) { } } }
  function onTask(t) {
    transferTasks = DATA ? DATA.upsertTask(transferTasks, t) : [t].concat(transferTasks.filter(x => x.id !== t.id));
    showTasks();
  }
  /** The tasks so far (the page just opened, or came back). */
  function relistTasks() {
    if (!isApp || typeof native.tasks !== 'function') return;
    try { transferTasks = JSON.parse(native.tasks()) || []; } catch (e) { transferTasks = []; }
    showTasks();
  }
  /** Transfers the app kept going in the background (or that stopped while it was closed). */
  function relistTransfers() {
    relistTasks();
    if (!isApp || typeof native.listTransfers !== 'function') return;
    try { JSON.parse(native.listTransfers()).forEach(t => { if (!transfers.has(t.id) || transfers.get(t.id).state !== t.state) onTransfer(t); }); } catch (e) { }
  }

  // ------------------------------------------------------------------ starred (kept on this phone)
  let starred = ls.json('cloudssd_starred', []);
  function isStarred(f) { return starred.some(s => s.path === f.path && s.src === f.src); }
  function toggleStar(f) {
    if (isStarred(f)) starred = starred.filter(s => !(s.path === f.path && s.src === f.src));
    else starred.unshift({ name: f.name, path: f.path, size: f.size, modified: f.modified, src: f.src });
    ls.set('cloudssd_starred', JSON.stringify(starred.slice(0, 300)));
    setText('cat-stat-starred', starred.length ? starred.length + ' files' : 'None yet');
  }

  // ------------------------------------------------------------------ file actions + viewer
  let sheetFile = null;
  let sheetActions = [];

  function streamUrl(path) { return apiUrl('/api/stream', { path }); }
  function fileUrl(path) { return 'file://' + path.split('/').map(encodeURIComponent).join('/'); }
  function thumbUrl(f) { return 'http://cloudssd.thumb/?p=' + encodeURIComponent(f.path) + '&m=' + (f.modified || 0); }

  /** "cloudssd:Photos/a.jpg" (a cloud item by drive id) rather than "Z:\Photos\a.jpg" (a path on the PC). */
  function isCloudId(p) { return /^[a-z0-9_-]{2,}:/i.test(String(p || '')) && !/^[a-z]:\\/i.test(String(p || '')); }

  // ---- Google Drive on the phone (no PC) ----
  const drive = { available: false, direct: true, accounts: [], roots: [] };
  function refreshDrive() {
    if (!isApp || typeof native.driveStatus !== 'function') return;
    try { Object.assign(drive, JSON.parse(native.driveStatus())); } catch (e) { }
  }
  /** Where cloud files come from right now: the PC, Google Drive directly, or nothing. */
  function cloudSource() {
    if (link.state === 'online') return 'pc';
    if (isApp && drive.direct && (drive.accounts || []).some(a => !a.needsReauth)) return 'drive';
    return 'none';
  }
  const cloudReqs = new Map();
  let cloudSeq = 0;
  /** A file operation on Google Drive through the app: resolves to {ok, status, data, error}. */
  function cloudOp(op, args) {
    return new Promise(resolve => {
      const id = ++cloudSeq;
      cloudReqs.set(id, resolve);
      try { native.cloudOp(id, op, JSON.stringify(args || {})); } catch (e) { cloudReqs.delete(id); resolve({ ok: false, error: 'Update the Cloud-SSD app' }); }
    });
  }

  /**
   * A link to one file that can be given to a player or another app. On a current PC it is a signed, short-lived
   * link without this phone's key; on an older PC the old kind; for Google Drive the app serves it itself.
   */
  async function mediaUrl(f, kind) {
    if (isCloudId(f.path) && cloudSource() === 'drive') return 'http://cloudssd.cloud/read?id=' + encodeURIComponent(f.path);
    if (pcHas('media-url')) {
      const params = isCloudId(f.path) ? { id: f.path, kind } : { path: f.path, kind };
      const r = await api('/api/media-url', { method: 'POST', params });
      if (r.ok && r.data && r.data.url) return link.server + r.data.url;
      throw new Error(apiError(r, 'The PC did not give a link'));
    }
    return isCloudId(f.path) ? apiUrl('/api/fs/read', { path: f.path }) : apiUrl(kind === 'download' ? '/api/download' : '/api/stream', { path: f.path });
  }

  /** f = {name, path, size, modified, src: 'cloud' | 'phone'}; list = the files around it (for swiping through photos) */
  function openFileSheet(f, list) {
    sheetFile = f;
    const k = kindOf(f.name);
    const cloud = f.src === 'cloud';
    const viaDrive = cloud && isCloudId(f.path) && cloudSource() === 'drive';
    const pdf = extOf(f.name) === 'pdf';
    const canView = k === 'images' || k === 'audio' || (k === 'videos' && PLAYABLE_VIDEO.includes(extOf(f.name))) || (cloud && k === 'text' && !viaDrive) || pdf;
    // A copy kept on this phone opens with or without the PC.
    const keptHere = cloud && !!keptCopyOf(f);
    sheetActions = [];
    if (canView) sheetActions.push({
      local: keptHere, icon: SVG.play, title: k === 'images' ? 'View' : k === 'text' || pdf ? 'Read' : 'Play',
      sub: keptHere ? 'From this phone (kept offline)' : !cloud ? 'On this phone' : viaDrive ? 'Streams from Google Drive' : 'Streams from the PC', fn: () => openViewer(f, list)
    });
    if (isApp) sheetActions.push({ local: keptHere, icon: SVG.share, title: 'Open with another app', sub: keptHere ? 'From this phone (kept offline)' : cloud ? 'Video player, gallery, PDF reader…' : 'Choose an app', fn: () => openExternally(f) });
    if (cloud) {
      // A kept copy is copied from this phone (no PC needed, and quicker); the rest comes from the PC.
      const keptId = keptHere ? keptCopyOf(f) : '';
      if (keptId && isApp && typeof native.offlineSaveToPhone === 'function') {
        sheetActions.push({ local: true, icon: SVG.download, title: 'Save to phone', sub: 'From this phone (kept offline) · into Downloads/Cloud-SSD', fn: () => native.offlineSaveToPhone(kept.pc, keptId, f.name) });
        if (typeof native.cloudShare === 'function') sheetActions.push({ local: true, icon: SVG.share, title: 'Share', sub: 'From this phone (kept offline) · WhatsApp, mail…', fn: () => native.cloudShare(keptId, f.name) });
      } else {
        sheetActions.push({ icon: SVG.download, title: 'Save to phone', sub: 'Into the Downloads folder' + (f.size > 0 ? ' · ' + fmtBytes(f.size) : ''), fn: () => downloadToPhone(f) });
      }
      if (!viaDrive && !isCloudId(f.path)) sheetActions.push({ icon: SVG.pc, title: 'Open on the PC screen', sub: 'Opens in its Windows app', fn: () => openOnPc(f) });
    } else {
      sheetActions.push({ icon: SVG.upload, title: 'Send to PC', sub: fmtBytes(f.size), fn: () => sendPhoneFiles([f]) });
      if (isApp && pcHas('share')) sheetActions.push({ icon: SVG.share, title: 'Send to another device', sub: 'A phone or tablet paired with ' + (link.pcName || 'the PC'), fn: () => sendToOtherDevice([f]) });
    }
    // Rename / move / delete on a cloud drive (fs.js).
    if (cloud && window.CloudSsdFs) window.CloudSsdFs.fileActions(f).forEach(a => sheetActions.push(a));
    sheetActions.push({ local: true, icon: SVG.star, title: isStarred(f) ? 'Remove star' : 'Add star', sub: 'Starred files are listed on the Phone tab', fn: () => { toggleStar(f); toast(isStarred(f) ? 'Starred' : 'Star removed'); } });
    if (cloud) keepActions(Object.assign({ isFolder: false }, f)).forEach(a => sheetActions.push(a));
    sheetActions.push({ local: true, icon: SVG.info, title: 'Details', sub: 'Size, dates, where it is', fn: () => openDetails(f) });
    // The PC is away: what needs it stays in the list, greyed, and says why instead of failing.
    const pcAway = cloud && !viaDrive && link.state !== 'online';
    if (pcAway) {
      const name = link.pcName || 'the PC';
      const why = `Needs ${name} · it is offline`;
      sheetActions = sheetActions.map(a => a.local ? a : Object.assign({}, a, { needsPc: true, sub: why, fn: () => toast(`${a.title} needs ${name}. It works again as soon as the PC is back.`) }));
    }

    setText('sheet-file-name', f.name);
    setText('sheet-file-meta', [f.size > 0 ? fmtBytes(f.size) : '', fmtDate(f.modified), keptHere ? 'kept on this phone' : !cloud ? 'on this phone' : viaDrive ? 'on Google Drive' : pcAway ? 'on ' + (link.pcName || 'the PC') + ' (offline)' : 'on the PC'].filter(Boolean).join(' · '));
    $('sheet-file-icon').innerHTML = iconFor(f.name);
    $('sheet-actions-list').innerHTML = sheetActions.map((a, i) => `
      <div class="m3-sheet-row ${a.needsPc ? 'needs-pc' : ''}" data-act="sheet-action" data-i="${i}">
        ${a.icon}
        <div class="m3-sheet-row-text"><div>${esc(a.title)}</div><div class="m3-sheet-row-sub">${esc(a.sub)}</div></div>
      </div>`).join('');
    openSheet('file-actions-sheet');
  }
  async function openExternally(f) {
    try {
      if (f.src !== 'cloud') { native.openLocalFile(f.path); return; }
      // The kept copy, through the app's Files provider (which opens it from this phone).
      const keptId = keptCopyOf(f);
      if (keptId && typeof native.cloudOpenWith === 'function') { native.cloudOpenWith(keptId, f.name); return; }
      if (isCloudId(f.path) && cloudSource() === 'drive') { native.cloudOpenWith(f.path, f.name); return; }
      if (!pcHas('media-url') && isCloudId(f.path) && typeof native.cloudOpenWith === 'function') { native.cloudOpenWith(f.path, f.name); return; }
      if (!pcHas('media-url')) { toast('Update Cloud-SSD on the PC to open files in other apps'); return; }
      native.openWith(await mediaUrl(f, 'stream'), mimeOf(f.name), f.name);
    } catch (e) { toast(e && e.message ? e.message : 'Cannot open this file'); }
  }
  async function confirmBigOnMobileData(f) {
    const n = appInfo.network || {};
    if (!isApp || !n.metered || !(f.size > 100 * 1024 * 1024)) return true;
    return confirmDialog('Use mobile data?', `${f.name} is ${fmtBytes(f.size)} and this phone is on mobile data.`, 'Download');
  }
  async function downloadToPhone(f) {
    if (!await confirmBigOnMobileData(f)) return;
    if (isApp) {
      try {
        if (isCloudId(f.path) && cloudSource() === 'drive') native.downloadFile('cloud:' + f.path, f.name);
        else native.downloadFile(isCloudId(f.path) ? apiUrl('/api/fs/read', { path: f.path }) : apiUrl('/api/download', { path: f.path }), f.name);
        toast('Downloading ' + f.name);
      } catch (e) { toast('Download could not start'); }
    } else {
      try {
        const a = document.createElement('a');
        a.href = await mediaUrl(f, 'download'); a.download = f.name;
        document.body.appendChild(a); a.click(); a.remove();
        toast('Download started in the browser');
      } catch (e) { toast(e.message || 'Download could not start'); }
    }
  }

  // ---- Details ----
  async function openDetails(f) {
    const cloud = f.src === 'cloud';
    const where = !cloud ? prettyPhonePath(f.path.slice(0, f.path.lastIndexOf('/')))
      : isCloudId(f.path) ? f.path.slice(0, Math.max(f.path.lastIndexOf('/'), f.path.indexOf(':') + 1))
        : f.path.slice(0, f.path.lastIndexOf('\\'));
    const rows = [
      ['Type', f.isFolder ? 'Folder' : (extOf(f.name).toUpperCase() || 'File') + ' · ' + mimeOf(f.name)],
      ['Size', f.isFolder ? '—' : f.size > 0 ? fmtBytes(f.size) + ' (' + f.size.toLocaleString() + ' bytes)' : '—'],
      ['Modified', f.modified ? new Date(typeof f.modified === 'number' ? f.modified : Date.parse(f.modified)).toLocaleString(dateLocale()) : '—'],
      ['Where', (!cloud ? 'This phone · ' : isCloudId(f.path) && cloudSource() === 'drive' ? 'Google Drive · ' : (link.pcName || 'PC') + ' · ') + where]
    ];
    const render = (extra) => {
      $('details-icon').innerHTML = f.isFolder ? SVG.folder : iconFor(f.name);
      setText('details-name', f.name);
      setText('details-kind', f.isFolder ? 'Folder' : kindOf(f.name) === 'other' ? 'File' : kindOf(f.name).replace(/s$/, ''));
      $('details-rows').innerHTML = rows.concat(extra || []).map(([k, v]) => `<div class="details-row"><span>${esc(k)}</span><strong>${esc(v)}</strong></div>`).join('') +
        `<div class="details-actions">
          <button class="m3-tonal-btn" data-act="details-copy">Copy location</button>
          ${f.isFolder && cloud && (pcHas('fs-details') || cloudSource() === 'drive') ? '<button class="m3-tonal-btn" data-act="details-size">Calculate size</button>' : ''}
        </div>`;
    };
    detailsFile = f;
    render();
    openSheet('details-sheet');
    if (cloud && pcHas('fs-details') && cloudSource() === 'pc' && !f.isFolder) {
      const id = isCloudId(f.path) ? f.path : (window.CloudSsdFs && window.CloudSsdFs.idOf(f.path));
      if (!id) return;
      try {
        const r = await api('/api/fs/stat', { params: { path: id, details: 1 } });
        if (!r.ok || !r.data || detailsFile !== f) return;
        const e = r.data.entry || {}, m = r.data.media || {};
        const extra = [];
        if (e.created) extra.push(['Created', new Date(e.created).toLocaleString(dateLocale())]);
        if (m.width) extra.push(['Picture', m.width + ' × ' + m.height + ' px']);
        if (m.dateTaken) extra.push(['Taken', m.dateTaken]);
        if (m.camera) extra.push(['Camera', m.camera]);
        if (r.data.fullPath) rows[3] = ['Where', (link.pcName || 'PC') + ' · ' + r.data.fullPath];
        render(extra);
      } catch (e) { /* the basic rows stay */ }
    }
  }
  let detailsFile = null;
  async function openOnPc(f) {
    try {
      const r = await api('/api/open-pc', { method: 'POST', params: { path: f.path } });
      toast(r.ok ? 'Opened on the PC: ' + f.name : apiError(r));
    } catch (e) { toast('PC not reachable'); }
  }
  // ------------------------------------------------------------------ files sent to this phone, and to other devices
  const inbox = { offers: [], seen: 0 };
  async function refreshInbox(show) {
    if (!pcHas('share')) return;
    const r = await api('/api/share/inbox', { noAsk: true }).catch(() => null);
    if (!r || !r.ok || !r.data) return;
    inbox.offers = r.data.offers || [];
    renderInbox();
    if (show && inbox.offers.some(o => o.state === 'offered') && !isOpen('inbox-sheet') && document.visibilityState === 'visible') openSheet('inbox-sheet');
    if (!inbox.offers.length && isOpen('inbox-sheet')) closeSheet('inbox-sheet');
  }
  function renderInbox() {
    const n = inbox.offers.length;
    setText('inbox-sub', n ? n + (n === 1 ? ' offer waits' : ' offers wait') + ' for you' : 'Nothing waiting');
    $('inbox-list').innerHTML = inbox.offers.map((o, i) => {
      // A folder (a project…) shows once by its name, not as a list of its first files.
      const names = DATA ? DATA.offerSummary(o.items).text : o.items.slice(0, 4).map(it => it.name).join(', ');
      const left = o.skipped && o.skipped.files ? ' · build output left out' : '';
      return `<div class="inbox-card">
        <div class="inbox-from">${SVG.share}<div><div class="inbox-title">${esc(o.from)}${o.fromPc ? '' : ' <span class="inbox-via">via ' + esc(link.pcName || 'the PC') + '</span>'}</div>
          <div class="inbox-meta">${o.items.length} ${o.items.length === 1 ? 'file' : 'files'} · ${fmtBytes(o.total)}${left}${o.state === 'accepted' ? ' · saving…' : ''}</div></div></div>
        <div class="inbox-names">${esc(names)}</div>
        <div class="inbox-actions">
          <button class="m3-tonal-btn" data-act="inbox-decline" data-i="${i}">Decline</button>
          <button class="m3-filled-btn" data-act="inbox-accept" data-i="${i}">${o.state === 'accepted' ? 'Save again' : 'Accept'}</button>
        </div>
      </div>`;
    }).join('') || '<div class="offline-empty">Nothing is waiting for this phone.</div>';
  }
  /** Saves an offer's files in Downloads › Cloud-SSD › Received › <sender> (with resume, through the transfers list). */
  async function acceptOffer(o) {
    const r = await api('/api/share/accept', { method: 'POST', params: { id: o.id }, noAsk: true });
    if (!r.ok) { toast(apiError(r, 'Could not accept')); return; }
    // Each file of a sent folder keeps its place (Downloads › Cloud-SSD › Received › PC › MyApp › src); a second "MyApp"
    // goes into "MyApp (2)" instead of mixing with the first.
    const base = 'Cloud-SSD/Received/' + (DATA ? DATA.safeFolder(o.from) : 'PC');
    const renames = {};
    if (DATA && isApp && typeof native.freeFolderName === 'function') {
      DATA.offerSummary(o.items).folders.forEach(top => { renames[top] = native.freeFolderName(base, top) || top; });
    }
    const files = o.items.map(it => ({ url: apiUrl('/api/share/file', { id: o.id, item: it.id }), name: it.name, size: it.size,
      folder: DATA ? DATA.shareSavePath(base, it.folder, renames) : base }));
    if (isApp && typeof native.receiveFiles === 'function') {
      // One task for the whole offer: who sent it, how it comes, where it is saved.
      const tops = DATA ? DATA.offerSummary(o.items).folders : [];
      const top = tops.length === 1 && o.items.every(it => String(it.folder || '').split('/')[0] === tops[0]) ? (renames[tops[0]] || tops[0]) : '';
      const where = base + (top ? '/' + top : '');
      const task = {
        id: 'of-' + o.id, dir: o.fromPc ? 'toPhone' : 'fromDevice', device: o.from || link.pcName || 'PC',
        link: o.fromPc ? (DATA ? DATA.taskLink(link.route, link.kind) : 'wifi') : 'relay',
        kind: top ? 'folder' : 'files', folder: top, files: String(o.items.length), bytes: String(o.total || 0),
        saveTo: 'Downloads › ' + where.split('/').join(' › '), openPath: 'Download/' + where
      };
      native.receiveFiles(JSON.stringify({ folder: base, files, task }));
    } else {
      files.forEach(f => { const a = document.createElement('a'); a.href = f.url; a.download = f.name; document.body.appendChild(a); a.click(); a.remove(); });
    }
    toast(`Saving ${files.length} ${files.length === 1 ? 'file' : 'files'} from ${o.from}`);
    // It is on its way (the transfers list shows it): off the list, and the sheet goes when nothing else waits.
    inbox.offers = inbox.offers.filter(x => x.id !== o.id);
    renderInbox();
    if (!inbox.offers.some(x => x.state === 'offered')) closeSheet('inbox-sheet');
  }
  async function declineOffer(o) {
    await api('/api/share/decline', { method: 'POST', params: { id: o.id }, noAsk: true }).catch(() => null);
    toast('Declined');
    refreshInbox(false);
  }
  /** Phone files to another phone or tablet, through the PC (it holds them until that device takes them). */
  async function sendToOtherDevice(files) {
    if (!isApp || typeof native.sendToDevice !== 'function') { toast('Update the Cloud-SSD app'); return; }
    if (link.state !== 'online') { toast('Needs ' + (link.pcName || 'the PC') + ' · it is offline'); return; }
    const r = await api('/api/share/devices');
    if (!r.ok || !r.data) { toast(apiError(r, 'Could not list the devices')); return; }
    if (!r.data.allowed) { toast('Sending to other devices is switched off on ' + (link.pcName || 'the PC')); return; }
    const list = (r.data.devices || []).filter(d => d.canReceive);
    if (!list.length) { toast('No other phone, tablet or laptop is paired with ' + (link.pcName || 'the PC')); return; }
    choiceSheet('Send to', list.map(d => ({
      icon: d.kind === 'laptop' || d.kind === 'desktop' ? SVG.pc : (SVG.phone || SVG.share), title: d.name, sub: d.online ? 'Online now' : 'Gets it when it is back (within a day)',
      fn: async () => {
        const items = files.map(f => ({ name: f.name, size: f.size || 0 }));
        const o = await api('/api/share/offer', { method: 'POST', params: { to: d.id, items: JSON.stringify(items) } });
        if (!o.ok || !o.data || !o.data.offer) { toast(apiError(o, 'Could not send')); return; }
        const offer = o.data.offer;
        if (typeof native.nextTask === 'function') native.nextTask(JSON.stringify({ device: d.name, saveTo: d.name }));
        native.sendToDevice(JSON.stringify({ offer: offer.id, items: offer.items.map((it, i) => ({ id: it.id, path: files[i].path })) }));
        toast(`Sending ${files.length === 1 ? files[0].name : files.length + ' files'} to ${d.name}`);
      }
    })));
  }

  // ------------------------------------------------------------------ sending chosen folders and files (Phone tab)
  /**
   * Folders and files chosen in the Phone tab go in one go: the phone walks the folders itself (no picker), keeps
   * their tree and leaves out build output; to the PC (each folder gets a free name there) or to another device.
   */
  /** Of what is about to be sent (folders at the top, loose files): the names the PC's upload folder already has. An older PC is not asked. */
  async function pcClashes(folders, files) {
    const none = { folders: [], files: [], free: Infinity };
    if (!pcHas('upload-clash') || (!folders.length && !files.length)) return none;
    const r = await api('/api/upload/clashes', { method: 'POST', body: JSON.stringify({ folders, files }), timeout: 15000 }).catch(() => null);
    return r && r.ok && r.data ? { folders: r.data.folders || [], files: r.data.files || [], free: typeof r.data.free === 'number' ? r.data.free : Infinity } : none;
  }
  /**
   * Names the receiver (where, for a longer text some day) already has: asks once for all of them. Gives 'keep' (both stay, the new one gets a number),
   * 'replace' or 'skip'; nothing comes back when the sheet is closed without a choice, and nothing is sent then.
   */
  function askClash(names, where) {
    return new Promise((resolve) => {
      const title = names.length === 1 ? '"' + names[0] + '" is already there' : names.length + ' of these are already there';
      choiceSheet(title, [
        { icon: SVG.check, title: 'Keep both', sub: 'The new one gets a number, like "name (2)"', fn: () => resolve('keep') },
        { icon: SVG.upload || SVG.check, title: 'Replace', sub: 'The one that is there gives way to the new one', danger: true, fn: () => resolve('replace') },
        { icon: SVG.close, title: 'Skip', sub: 'Leave those out and send the rest', fn: () => resolve('skip') }
      ]);
    });
  }

  async function sendPaths(paths) {
    let p;
    try { p = JSON.parse(native.preparePaths(JSON.stringify({ paths }))); } catch (e) { p = { error: String(e) }; }
    if (p.error) { toast('Could not send: ' + p.error); return; }
    if (!p.count) { toast('Nothing to send'); return; }
    const pcName = link.pcName || 'the PC';
    if (link.state !== 'online') { toast('Needs ' + pcName + ' · it is offline'); return; }
    const what = (p.folders.length ? p.folders.length + (p.folders.length === 1 ? ' folder' : ' folders') + ' · ' : '')
      + p.count + (p.count === 1 ? ' file' : ' files') + ' · ' + fmtBytes(p.bytes);
    const choices = [{
      icon: SVG.pc, title: pcName, sub: 'Into its From phone folder',
      fn: async () => {
        // Names the PC already has: one question for all of them (keep both, replace, skip).
        const loose = (p.items || []).filter(i => !i.folder).map(i => i.name);
        const canAsk = typeof native.sendPreparedToPc2 === 'function';
        const clash = canAsk ? await pcClashes(p.folders, loose) : { folders: [], files: [], free: Infinity };
        // It does not fit on the PC: said now, not after half of it went.
        if (p.bytes > clash.free) { toast('Not enough space on ' + pcName + ': needs ' + fmtBytes(p.bytes) + ', ' + fmtBytes(clash.free) + ' free'); return; }
        const taken = clash.folders.concat(clash.files);
        const onClash = taken.length ? await askClash(taken, pcName) : 'keep';
        const skip = onClash === 'skip' ? taken : [];
        if (skip.length >= p.folders.length + loose.length) { toast('Nothing to send'); return; }
        const renames = {};
        for (const top of p.folders) {
          if (skip.includes(top)) continue;
          const params = onClash === 'replace' && clash.folders.includes(top) ? { name: top, onClash: 'replace' } : { name: top };
          const r = await api('/api/upload/folder', { params });
          if (!r.ok || !r.data || !r.data.name) { toast(apiError(r, 'Could not send')); return; }
          renames[top] = r.data.name;
        }
        if (canAsk) native.sendPreparedToPc2(JSON.stringify(renames), JSON.stringify(skip), onClash);
        else native.sendPreparedToPc(JSON.stringify(renames));
        toast(skip.length ? 'Sending to ' + pcName + ' · ' + skip.length + ' left out' : 'Sending ' + what + ' to ' + pcName);
      }
    }];
    if (pcHas('share')) {
      const r = await api('/api/share/devices').catch(() => null);
      if (r && r.ok && r.data && r.data.allowed) (r.data.devices || []).filter(d => d.canReceive).forEach(d => choices.push({
        icon: d.kind === 'laptop' || d.kind === 'desktop' ? SVG.pc : (SVG.phone || SVG.share), title: d.name,
        sub: (d.online ? 'Online now' : 'Gets it when it is back (within a day)') + ' · through ' + pcName,
        fn: async () => {
          const o = await api('/api/share/offer', { method: 'POST', params: { to: d.id }, body: JSON.stringify(p.items), timeout: 30000 });
          if (!o.ok || !o.data || !o.data.offer) { toast(apiError(o, 'Could not send')); return; }
          if (typeof native.nextTask === 'function') native.nextTask(JSON.stringify({ device: d.name, saveTo: d.name }));
          native.sendFolderToDevice(o.data.offer.id, JSON.stringify(o.data.offer.items.map(it => it.id)));
          toast('Sending ' + what + ' to ' + d.name);
        }
      }));
    }
    if (p.skipped && p.skipped.folders && p.skipped.folders.length) toast('Build output left out: ' + p.skipped.folders.slice(0, 4).join(', '));
    choiceSheet('Send to', choices);
  }

  // ------------------------------------------------------------------ sending a folder (a project…)
  /**
   * A folder from this phone, whole: every file keeps its place in it, as the PC and other devices save it again. What a
   * build or a package manager makes again (.git, node_modules, build…) is left out; the phone walks the folder.
   */
  function sendFolderStart() {
    if (!isApp || typeof native.pickFolderToSend !== 'function') { toast('Update the Cloud-SSD app'); return; }
    if (!link.token) { toast('Pair with your PC first'); return; }
    native.pickFolderToSend(false);
  }
  async function onFolderPicked(p) {
    if (!p) return;
    if (p.error) { toast('Could not send: ' + p.error); return; }
    if (!p.count) { toast('Nothing to send in ' + p.name); return; }
    const pcName = link.pcName || 'the PC';
    if (link.state !== 'online') { toast('Needs ' + pcName + ' · it is offline'); return; }
    const what = p.name + '/ · ' + p.count + (p.count === 1 ? ' file' : ' files') + ' · ' + fmtBytes(p.bytes);
    const choices = [{
      icon: SVG.pc, title: pcName, sub: 'Into its From phone folder',
      fn: async () => {
        // The PC has a folder of this name already: keep both, replace, or do not send.
        const canAsk = typeof native.sendFolderToPc2 === 'function';
        const clash = canAsk ? await pcClashes([p.name], []) : { folders: [], free: Infinity };
        if (p.bytes > clash.free) { toast('Not enough space on ' + pcName + ': needs ' + fmtBytes(p.bytes) + ', ' + fmtBytes(clash.free) + ' free'); return; }
        const onClash = clash.folders.length ? await askClash([p.name], pcName) : 'keep';
        if (onClash === 'skip') { toast('Nothing to send'); return; }
        const r = await api('/api/upload/folder', { params: onClash === 'replace' ? { name: p.name, onClash: 'replace' } : { name: p.name } });
        if (!r.ok || !r.data || !r.data.name) { toast(apiError(r, 'Could not send')); return; }
        if (canAsk) native.sendFolderToPc2(r.data.name, onClash); else native.sendFolderToPc(r.data.name);
        toast('Sending ' + what + ' to ' + pcName);
      }
    }];
    if (pcHas('share')) {
      const r = await api('/api/share/devices').catch(() => null);
      if (r && r.ok && r.data && r.data.allowed) (r.data.devices || []).filter(d => d.canReceive).forEach(d => choices.push({
        icon: d.kind === 'laptop' || d.kind === 'desktop' ? SVG.pc : (SVG.phone || SVG.share), title: d.name,
        sub: (d.online ? 'Online now' : 'Gets it when it is back (within a day)') + ' · through ' + pcName,
        fn: async () => {
          // The list goes in the body: thousands of names do not fit in an address.
          const o = await api('/api/share/offer', { method: 'POST', params: { to: d.id }, body: JSON.stringify(p.items), timeout: 30000 });
          if (!o.ok || !o.data || !o.data.offer) { toast(apiError(o, 'Could not send')); return; }
          if (typeof native.nextTask === 'function') native.nextTask(JSON.stringify({ device: d.name, saveTo: d.name }));
          native.sendFolderToDevice(o.data.offer.id, JSON.stringify(o.data.offer.items.map(it => it.id)));
          toast('Sending ' + what + ' to ' + d.name);
        }
      }));
    }
    if (p.skipped && p.skipped.folders && p.skipped.folders.length) toast('Build output left out: ' + p.skipped.folders.slice(0, 4).join(', '));
    choiceSheet('Send to', choices);
  }

  /**
   * Files shared from another app with "Send to a device…": where to? The PC in use, or a phone, tablet or laptop
   * paired with it (through the PC, which keeps them until that device takes them).
   */
  async function onShareIn() {
    let files = [];
    try { files = JSON.parse(native.shareInFiles() || '[]'); } catch (e) { }
    if (!files.length) return;
    const label = files.length === 1 ? files[0].name : files.length + ' files';
    const pcName = link.pcName || 'the PC';
    const choices = [];
    if (link.token) choices.push({
      icon: SVG.pc, title: pcName, sub: link.state === 'online' ? 'Into its From phone folder' : 'Waits here and goes when it is back',
      fn: () => { native.shareInToPc(); toast((link.state === 'online' ? 'Sending ' : 'Will send ') + label + ' to ' + pcName); }
    });
    if (link.state === 'online' && pcHas('share')) {
      const r = await api('/api/share/devices').catch(() => null);
      if (r && r.ok && r.data && r.data.allowed) (r.data.devices || []).filter(d => d.canReceive).forEach(d => choices.push({
        icon: d.kind === 'laptop' || d.kind === 'desktop' ? SVG.pc : (SVG.phone || SVG.share), title: d.name,
        sub: (d.online ? 'Online now' : 'Gets it when it is back (within a day)') + ' · through ' + pcName,
        fn: async () => {
          const items = files.map(f => ({ name: f.name, size: f.size > 0 ? f.size : 0 }));
          const o = await api('/api/share/offer', { method: 'POST', params: { to: d.id, items: JSON.stringify(items) } });
          if (!o.ok || !o.data || !o.data.offer) { toast(apiError(o, 'Could not send')); return; }
          if (typeof native.nextTask === 'function') native.nextTask(JSON.stringify({ device: d.name, saveTo: d.name }));
          native.shareInToDevice(o.data.offer.id, JSON.stringify(o.data.offer.items.map(it => it.id)));
          toast(`Sending ${label} to ${d.name}`);
        }
      }));
    }
    if (!choices.length) { try { native.shareInCancel(); } catch (e) { } toast('Pair with your PC first'); showPair(''); return; }
    choiceSheet('Send ' + label + ' to', choices);
  }

  async function sendPhoneFiles(files) {
    if (!isApp) return;
    if (link.state === 'unpaired' || !link.token) { toast('Pair with your PC first'); showPair(''); return; }
    // The PC is there: one send for all of them, after asking about names it already has and whether they fit.
    if (link.state === 'online' && pcHas('upload-clash') && typeof native.sendPreparedToPc2 === 'function' && typeof native.preparePaths === 'function') {
      let p = null;
      try { p = JSON.parse(native.preparePaths(JSON.stringify({ paths: files.map(f => f.path) }))); } catch (e) { }
      if (p && p.count && !p.error) {
        const pcName = link.pcName || 'the PC';
        const names = (p.items || []).map(i => i.name);
        const clash = await pcClashes([], names);
        if (p.bytes > clash.free) { toast('Not enough space on ' + pcName + ': needs ' + fmtBytes(p.bytes) + ', ' + fmtBytes(clash.free) + ' free'); return; }
        const onClash = clash.files.length ? await askClash(clash.files, pcName) : 'keep';
        const skip = onClash === 'skip' ? clash.files : [];
        if (skip.length >= names.length) { toast('Nothing to send'); return; }
        native.sendPreparedToPc2('{}', JSON.stringify(skip), onClash);
        toast(skip.length ? 'Sending to ' + pcName + ' · ' + skip.length + ' left out' : files.length === 1 ? 'Sending ' + files[0].name : 'Sending ' + files.length + ' files to the PC');
        return;
      }
    }
    files.forEach(f => { try { native.uploadFileByPath(f.path); } catch (e) { } });
    // The PC away: they wait in the transfers list and go by themselves when it is back.
    toast(link.state !== 'online' ? (files.length === 1 ? files[0].name : files.length + ' files') + ' will go when ' + (link.pcName || 'the PC') + ' is back'
      : files.length === 1 ? 'Sending ' + files[0].name : 'Sending ' + files.length + ' files to the PC');
  }

  let viewerFile = null;
  /** Where the viewer inside this page reads a file from (the key never leaves the app here). */
  // ---- "Offline rakho": folders and files of the PC kept on this phone (in the app) ----
  const kept = { pc: null, pins: [], files: {}, busy: false, mobileData: false, used: 0, sig: '' };
  function canKeep() { return isApp && typeof native.offlinePin === 'function' && !!DATA && !!DATA.covers; }
  function refreshKept() {
    if (!canKeep()) return;
    const pc = currentDeviceId();
    let st = {};
    try { st = JSON.parse(native.offlineState(pc) || '{}') || {}; } catch (e) { }
    kept.pc = pc;
    kept.pins = st.pins || [];
    kept.files = st.files || {};
    kept.busy = !!st.busy;
    kept.mobileData = !!st.mobileData;
    kept.used = st.used || 0;
  }
  function ensureKept() { if (canKeep() && kept.pc !== currentDeviceId()) refreshKept(); }
  /**
   * The drive id of a PC item ("cloudssd:Photos/a.jpg"; on the PC's own disk "disk-c:Users/me/Documents/a.pdf"),
   * or null for what cannot be kept.
   */
  function keepIdOf(f) {
    if (!f || f.src === 'phone') return null;
    if (isCloudId(f.path)) return f.path;
    return (window.CloudSsdFs ? window.CloudSsdFs.idOf(f.path) : null) || diskIdOf(f.path);
  }
  /**
   * A place on one of the PC's own disks as a drive id, when the PC serves its disks that way (read-only): such a
   * folder can be kept on this phone, never changed from it. null for a cloud drive, the "From phone" folder, an older PC.
   */
  function diskIdOf(path) {
    if (!DATA || !DATA.diskId || !pcHas('fs-disk-roots')) return null;
    const pl = placeOf(path);
    return pl && pl.disk ? DATA.diskId(path) : null;
  }
  /** The page's path of a kept item ("Z:\Photos", "C:\Users\me\Documents"), or '' when its drive is not known here. */
  function pathOfKeepId(id) {
    id = String(id || '');
    if (DATA && DATA.isDiskId && DATA.isDiskId(id)) return DATA.diskPath(id);
    const colon = id.indexOf(':');
    const pl = colon > 0 ? ssdPlaces().find(p => p.id === id.slice(0, colon) && !p.drive) : null;
    if (!pl) return '';
    const rest = id.slice(colon + 1).split('/').filter(Boolean).join('\\');
    return rest ? pl.root.replace(/\\+$/, '') + '\\' + rest : pl.root;
  }
  function keptPin(id) { ensureKept(); return id && canKeep() ? DATA.pinFor(kept.pins, id) : null; }
  /** The id of a kept copy of this file that may be used now: any copy while the PC is away, else the same version. */
  function keptCopyOf(f) {
    if (!canKeep() || !f || f.src !== 'cloud') return null;
    ensureKept();
    const id = keepIdOf(f);
    const entry = id && kept.files[id];
    if (!entry) return null;
    return link.state !== 'online' || DATA.sameCopy(entry, f) ? id : null;
  }
  function keptUrl(id) { return 'http://cloudssd.offline/f?pc=' + encodeURIComponent(kept.pc || '') + '&id=' + encodeURIComponent(id); }
  /** "Keep offline" / "Remove from this phone" for a file or folder of the PC (sheet rows). */
  function keepActions(item) {
    if (!canKeep()) return [];
    const id = keepIdOf(Object.assign({ src: 'cloud' }, item));
    if (!id) return [];
    ensureKept();
    const own = kept.pins.find(p => p.id === id);
    const above = !own && DATA.pinFor(kept.pins, id);
    if (above) return [{ local: true, icon: SVG.pin, title: 'Kept on this phone', sub: 'Its folder ' + (above.name || '') + ' is kept offline', fn: () => openOfflineSheet() }];
    if (own) return [{ local: true, icon: SVG.pin, title: 'Remove from this phone', sub: DATA.pinStatus(own, Date.now()), fn: () => unkeep(own) }];
    const size = !item.isFolder && item.size > 0 ? ' · ' + fmtBytes(item.size) : '';
    return [{
      local: true, icon: SVG.pin, title: 'Keep offline' + size,
      sub: item.isFolder ? 'Everything in it stays on this phone and opens without the PC' : 'Stays on this phone and opens without the PC',
      fn: () => keep(id, item)
    }];
  }
  // ---- synced folders: a phone folder and a PC folder kept the same, both ways (the app only) ----
  function folderSyncs() {
    if (!isApp || typeof native.folderSyncs !== 'function') return [];
    try { return JSON.parse(native.folderSyncs() || '[]'); } catch (e) { return []; }
  }
  function syncStatus(p) { return DATA.syncStatus(p, lastUpdate); }
  /** "Keep in sync with a phone folder" for a folder of the PC (or what it is synced with already). */
  function syncActions(item) {
    if (!item.isFolder || !isApp || typeof native.folderSyncAdd !== 'function') return [];
    const id = keepIdOf(Object.assign({ src: 'cloud' }, item));
    // A sync writes to the PC folder too, and the PC's own disks are read-only from the phone.
    if (!id || (DATA.isDiskId && DATA.isDiskId(id))) return [];
    const has = folderSyncs().find(p => p.pcFolder === id && p.pcId === currentDeviceId());
    if (has) return [{ icon: SVG.share, title: 'Synced with ' + has.phoneLabel, sub: syncStatus(has), fn: () => openSettings() }];
    return [{
      icon: SVG.share, title: 'Keep in sync with a phone folder',
      sub: 'New and changed files go both ways, every hour on Wi-Fi',
      fn: () => { toast('Choose the folder on this phone'); native.folderSyncAdd(id, item.name); }
    }];
  }
  function onFolderSync(e) {
    if (!e) return;
    if (e.state === 'added') toast('Syncing the folder for the first time…');
    if (e.state === 'done' && e.result) {
      const r = e.result;
      toast(r.ok ? 'Folder synced · ' + r.up + ' up, ' + r.down + ' down' + (r.conflicts ? ' · ' + r.conflicts + ' kept twice (conflict)' : '')
        : 'Folder sync stopped: ' + (r.error || ''));
      if (r.down && activeTab === 'ssd') rerenderSsd();
    }
    if (isOpen('settings-modal')) renderSettings();
  }
  function keep(id, item) {
    const why = native.offlinePin(currentDeviceId(), id, item.name, !!item.isFolder);
    if (why) { toast(why); return; }
    refreshKept();
    toast(link.state === 'online' ? item.name + ' will be kept on this phone' : item.name + ' will be kept when ' + (link.pcName || 'the PC') + ' is back');
    rerenderSsd();
  }
  async function unkeep(p) {
    if (!await confirmDialog('Remove from this phone?', `The copy of ${p.name || 'this item'} on this phone is deleted. It stays on ${link.pcName || 'the PC'}.`, 'Remove')) return;
    native.offlineUnpin(currentDeviceId(), p.id);
    refreshKept();
    toast('Removed from this phone');
    rerenderSsd();
    if (isOpen('offline-sheet')) renderOfflineSheet();
  }
  function rerenderSsd() {
    if (!ssd.path && pcHomeOn()) { if (activeTab === 'ssd') renderPcHome(); return; }
    const c = ssd.cache.get(ssd.path); if (c && activeTab === 'ssd') renderSsd(c);
  }
  function openOfflineSheet() {
    if (!canKeep()) { toast('Keeping files offline needs the Cloud-SSD app'); return; }
    refreshKept();
    renderOfflineSheet();
    openSheet('offline-sheet');
    try { native.offlineSync(); } catch (e) { }
  }
  function renderOfflineSheet() {
    const now = Date.now();
    setText('offline-sub', kept.pins.length ? `${fmtBytes(kept.used)} on this phone` : 'Nothing kept yet');
    $('offline-list').innerHTML = kept.pins.length ? kept.pins.map((p, i) => `
      <div class="offline-row ${p.state === 'error' ? 'is-error' : ''}">
        <div class="offline-row-icon" data-act="offline-open" data-i="${i}">${p.dir ? SVG.folder : iconFor(p.name || '')}</div>
        <div class="offline-row-text" data-act="offline-open" data-i="${i}"><div class="offline-row-name" data-no-i18n>${esc(p.name || p.id)}</div><div class="offline-row-sub">${esc(DATA.pinStatus(p, now))}</div></div>
        <button class="m3-tonal-btn offline-row-btn" data-act="offline-remove" data-i="${i}">Remove</button>
      </div>`).join('')
      : '<div class="offline-empty">Open a folder of the PC, tap ⋮ on a folder or a file and choose “Keep offline”. It then opens on this phone even when the PC is off.</div>';
    setText('offline-mobile-sub', kept.mobileData ? 'Kept folders also update on mobile data' : 'Kept folders update on Wi-Fi only');
    const sw = $('offline-mobile-switch');
    if (sw) sw.classList.toggle('on', kept.mobileData);
  }
  /** News from the app: a kept folder moved on, finished, or waits. */
  function onKeptEvent(ev) {
    if (!ev || !canKeep() || (ev.pc && ev.pc !== currentDeviceId())) return;
    if (ev.type === 'progress') {
      const p = kept.pins.find(x => x.id === ev.id);
      if (p) Object.assign(p, { state: 'syncing', files: ev.files, bytes: ev.bytes, doneFiles: ev.doneFiles, doneBytes: ev.doneBytes });
      if (isOpen('offline-sheet')) renderOfflineSheet();
      return;
    }
    refreshKept();
    if (isOpen('offline-sheet')) renderOfflineSheet();
    // The list shows badges for kept items: draw it again only when they changed.
    const sig = Object.keys(kept.files).length + '|' + kept.pins.map(p => p.id + ':' + p.state).join(',');
    if (sig !== kept.sig) { kept.sig = sig; rerenderSsd(); }
  }

  function viewUrl(f) {
    if (f.src !== 'cloud') return fileUrl(f.path);
    const keptId = keptCopyOf(f);
    if (keptId) return keptUrl(keptId);
    if (isCloudId(f.path)) return cloudSource() === 'drive' ? 'http://cloudssd.cloud/read?id=' + encodeURIComponent(f.path) : apiUrl('/api/fs/read', { path: f.path });
    return streamUrl(f.path);
  }
  function viewerWhere(f) {
    if (keptCopyOf(f)) return ' · kept on this phone';
    return f.src !== 'cloud' ? ' · on this phone' : isCloudId(f.path) && cloudSource() === 'drive' ? ' · from Google Drive' : ' · streaming from the PC';
  }

  // Photos: swipe to the next one, pinch or double-tap to zoom.
  const gallery = { list: [], i: 0, scale: 1, x: 0, y: 0 };
  function showGalleryImage() {
    const f = gallery.list[gallery.i];
    if (!f) return;
    viewerFile = f;
    setText('viewer-filename', f.name);
    setText('viewer-meta', (f.size > 0 ? fmtBytes(f.size) : '') + viewerWhere(f));
    setText('gallery-count', gallery.list.length > 1 ? `${gallery.i + 1} / ${gallery.list.length}` : '');
    document.querySelectorAll('.gallery-bar .viewer-btn-icon').forEach(b => { b.style.visibility = gallery.list.length > 1 ? '' : 'hidden'; });
    const img = $('viewer-image-elem');
    zoomTo(1, 0, 0);
    img.onerror = () => {
      if (viewerFile !== f) return;
      // In a gallery the others can still be swiped to; a single picture gets the "open with" choices.
      if (gallery.list.length > 1) setText('gallery-count', `${gallery.i + 1} / ${gallery.list.length} · could not be shown`);
      else viewerFallback(f, 'This picture could not be shown.');
    };
    img.src = viewUrl(f);
    // The neighbours load quietly so swiping feels instant.
    [gallery.i + 1, gallery.i - 1].forEach(j => { const n = gallery.list[j]; if (n) new Image().src = viewUrl(n); });
  }
  function galleryStep(d) {
    if (gallery.list.length < 2) return;
    const j = gallery.i + d;
    if (j < 0 || j >= gallery.list.length) { toast(d > 0 ? 'Last picture' : 'First picture'); return; }
    gallery.i = j;
    show('viewer-section-generic', false);
    show('viewer-section-image', true);
    showGalleryImage();
  }
  function zoomTo(scale, x, y) {
    gallery.scale = Math.min(5, Math.max(1, scale));
    if (gallery.scale === 1) { x = 0; y = 0; }
    gallery.x = x; gallery.y = y;
    const img = $('viewer-image-elem');
    if (img) img.style.transform = gallery.scale === 1 ? '' : `translate(${x}px, ${y}px) scale(${gallery.scale})`;
  }
  function wireGallery() {
    const stage = $('gallery-stage');
    if (!stage) return;
    let start = null, pinch = null, lastTap = 0;
    const dist = (t) => Math.hypot(t[0].clientX - t[1].clientX, t[0].clientY - t[1].clientY);
    stage.addEventListener('touchstart', (e) => {
      if (e.touches.length === 2) { pinch = { d: dist(e.touches), s: gallery.scale }; start = null; return; }
      const t = e.touches[0];
      start = { x: t.clientX, y: t.clientY, ox: gallery.x, oy: gallery.y, t: Date.now() };
    }, { passive: true });
    stage.addEventListener('touchmove', (e) => {
      if (pinch && e.touches.length === 2) { e.preventDefault(); zoomTo(pinch.s * dist(e.touches) / pinch.d, gallery.x, gallery.y); return; }
      if (start && gallery.scale > 1) { e.preventDefault(); const t = e.touches[0]; zoomTo(gallery.scale, start.ox + t.clientX - start.x, start.oy + t.clientY - start.y); }
    }, { passive: false });
    stage.addEventListener('touchend', (e) => {
      if (pinch) { if (e.touches.length === 0) pinch = null; return; }
      if (!start) return;
      const t = e.changedTouches[0];
      const dx = t.clientX - start.x, dy = t.clientY - start.y;
      const quick = Date.now() - start.t < 600;
      if (gallery.scale === 1 && quick && Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.5) galleryStep(dx < 0 ? 1 : -1);
      else if (Math.abs(dx) < 10 && Math.abs(dy) < 10) {
        const now = Date.now();
        if (now - lastTap < 300) { zoomTo(gallery.scale > 1 ? 1 : 2.5, 0, 0); lastTap = 0; } else lastTap = now;
      }
      start = null;
    });
  }

  // PDFs: the app draws each page with Android's own PDF renderer; a browser uses its built-in reader.
  const pdf = { req: 0, key: '', file: null };
  function openPdfViewer(f) {
    const status = $('pdf-status'), pages = $('pdf-pages');
    show('viewer-section-pdf', true);
    pages.innerHTML = '';
    status.style.display = '';
    status.textContent = 'Opening…';
    const req = ++pdf.req;
    pdf.file = f;
    let source;
    const keptId = keptCopyOf(f);
    const keptPath = keptId ? native.offlinePath(kept.pc, keptId) : '';
    if (f.src !== 'cloud') source = f.path;
    else if (keptPath) source = keptPath;
    else if (isCloudId(f.path) && cloudSource() === 'drive') source = 'cloud:' + f.path;
    // Read inside the app, so the PC lists it as streamed, not as a file saved to the phone.
    else source = 'pc:' + (isCloudId(f.path) ? apiUrl('/api/fs/read', { path: f.path }) : apiUrl('/api/stream', { path: f.path }));
    try { native.openPdf(req, source, f.name, f.size || -1); } catch (e) { viewerFallback(f, 'Update the Cloud-SSD app to read PDFs here.'); }
  }
  function onPdf(ev) {
    if (ev.reqId !== pdf.req || !pdf.file) return;
    const status = $('pdf-status');
    if (ev.state === 'loading') {
      status.textContent = 'Loading… ' + fmtBytes(ev.bytes) + (ev.total > 0 ? ' of ' + fmtBytes(ev.total) : '');
    } else if (ev.state === 'error') {
      viewerFallback(pdf.file, ev.error);
    } else if (ev.state === 'ready') {
      pdf.key = ev.key;
      const n = (ev.pages || []).length;
      status.textContent = n + (n === 1 ? ' page' : ' pages');
      const w = Math.min(2400, Math.round(Math.min(window.innerWidth, 900) * (window.devicePixelRatio || 2)));
      $('pdf-pages').innerHTML = (ev.pages || []).map((p, i) =>
        `<img class="pdf-page" loading="lazy" decoding="async" alt="Page ${i + 1}" style="aspect-ratio:${p.w} / ${p.h}"
          src="http://cloudssd.pdf/?k=${encodeURIComponent(ev.key)}&p=${i}&w=${w}">`).join('');
    }
  }

  /** list: the files around this one (a folder), so photos can be swiped through. */
  function openViewer(f, list) {
    viewerFile = f;
    const k = kindOf(f.name);
    const url = viewUrl(f);
    setText('viewer-filename', f.name);
    setText('viewer-meta', (f.size > 0 ? fmtBytes(f.size) : '') + viewerWhere(f));
    ['video', 'image', 'audio', 'text', 'generic', 'pdf'].forEach(s => show('viewer-section-' + s, false));

    if (k === 'pdf') {
      if (!isApp) {
        // A phone browser has its own PDF reader: give it a short-lived link.
        mediaUrl(f, 'stream').then(u => window.open(u, '_blank')).catch(e => toast(e.message || 'Cannot open this PDF'));
        return;
      }
      openPdfViewer(f);
    } else if (k === 'videos') {
      const v = $('native-video-player');
      show('viewer-section-video', true);
      v.onerror = () => { if (viewerFile === f) viewerFallback(f, 'This phone cannot play this video format here.'); };
      v.src = url;
      v.play().catch(() => { });
    } else if (k === 'images') {
      show('viewer-section-image', true);
      gallery.list = (list || [f]).filter(x => kindOf(x.name) === 'images');
      gallery.i = Math.max(0, gallery.list.findIndex(x => x.path === f.path));
      if (!gallery.list.length) gallery.list = [f];
      showGalleryImage();
    } else if (k === 'audio') {
      show('viewer-section-audio', true);
      setText('viewer-audio-title', f.name);
      const a = $('viewer-audio-player');
      a.onerror = () => { if (viewerFile === f) viewerFallback(f, 'This audio format cannot be played here.'); };
      a.src = url;
      a.play().catch(() => { });
    } else if (k === 'text' && f.src === 'cloud') {
      show('viewer-section-text', true);
      const pre = $('viewer-text-content');
      pre.textContent = say('Loading…');
      const keptText = link.state !== 'online' && keptCopyOf(f);
      if (keptText) {
        fetch(keptUrl(keptText)).then(r => r.text()).then(t => {
          if (viewerFile === f) pre.textContent = t.length > 262144 ? t.slice(0, 262144) + '\n\n… ' + say('(first 256 KB shown)') : t;
        }).catch(() => { if (viewerFile === f) pre.textContent = say('Could not read the copy on this phone'); });
      } else api('/api/preview', { params: { path: f.path }, timeout: 30000 }).then(r => {
        if (viewerFile !== f) return;
        if (r.ok && r.data && r.data.success) pre.textContent = r.data.isBinary ? say('(This file is not text.)') : r.data.content + (r.data.truncated ? '\n\n… ' + say('(first 256 KB shown)') : '');
        else pre.textContent = say(apiError(r, 'Could not read the file'));
      }).catch(() => { if (viewerFile === f) pre.textContent = say('PC not reachable'); });
    } else {
      viewerFallback(f, '');
      return;
    }
    openSheet('file-viewer-modal');
  }
  function viewerFallback(f, why) {
    ['video', 'image', 'audio', 'text', 'pdf'].forEach(s => show('viewer-section-' + s, false));
    show('viewer-section-generic', true);
    $('viewer-generic-icon').innerHTML = iconFor(f.name);
    setText('viewer-generic-name', f.name);
    const sub = document.querySelector('#viewer-section-generic .generic-viewer-sub');
    if (sub) sub.textContent = why || 'No built-in preview for this kind of file';
    $('viewer-generic-actions').innerHTML =
      (isApp ? `<button class="m3-filled-btn" data-act="viewer-external">${SVG.share}<span>Open with another app</span></button>` : '') +
      (f.src === 'cloud' ? `<button class="m3-tonal-btn" data-act="viewer-download">${SVG.download}<span>Save to phone</span></button>` : '');
    openSheet('file-viewer-modal');
  }
  function stopViewer() {
    viewerFile = null;
    for (const id of ['native-video-player', 'viewer-audio-player']) {
      const el = $(id);
      if (el) { el.onerror = null; el.pause(); el.removeAttribute('src'); el.load(); }
    }
    const img = $('viewer-image-elem');
    if (img) { img.onerror = null; img.removeAttribute('src'); img.style.transform = ''; }
    gallery.list = []; gallery.scale = 1;
    pdf.req++;
    pdf.file = null;
    if (pdf.key) { try { native.closePdf(pdf.key); } catch (e) { } pdf.key = ''; }
    const pages = $('pdf-pages');
    if (pages) pages.innerHTML = '';
  }

  // ------------------------------------------------------------------ Phone tab (files on this phone)
  const phone = {
    access: '', totals: null, report: ls.json('cloudssd_storage_report', null), scanning: false,
    stack: [], view: null, items: { folders: [], files: [] }, seq: 0, grid: null,
    selecting: false, selected: new Set()
  };
  const CATS = [
    { key: 'images', label: 'Images', color: '#F48FB1', icon: SVG.image },
    { key: 'videos', label: 'Videos', color: '#EF9A9A', icon: SVG.video },
    { key: 'audio', label: 'Audio', color: '#FDD663', icon: SVG.audio },
    { key: 'documents', label: 'Documents', color: '#78D9EC', icon: SVG.doc },
    { key: 'apps', label: 'APKs & archives', color: '#6DD58C', icon: SVG.apk },
    { key: 'other', label: 'Other files', color: '#B39DDB', icon: SVG.folder }
  ];

  function renderPermBanner() {
    if (!isApp) { show('phone-perm-banner', false); return; }
    const a = phone.access;
    show('phone-perm-banner', a !== 'all');
    if (a === 'none') {
      setText('phone-perm-title', 'Allow access to phone files');
      setText('phone-perm-sub', 'Needed to show your photos, videos, documents and folders. Android opens a screen with one switch: turn it on and come back.');
      setText('phone-perm-btn', 'Allow');
    } else if (a === 'media') {
      setText('phone-perm-title', 'Only photos, videos and audio are visible');
      setText('phone-perm-sub', 'Turn on "All files access" to also see documents, APKs and every folder, like the phone\'s own Files app.');
      setText('phone-perm-btn', 'Allow all files');
    }
  }
  function requestScan() {
    if (!isApp || phone.scanning) return;
    phone.scanning = true;
    setText('storage-scan-tag', 'Reading…');
    try { native.scanStorage(); } catch (e) { phone.scanning = false; }
  }
  function onStorageReport(r) {
    phone.scanning = false;
    if (r.error && !r.cats) return;
    phone.report = r;
    phone.access = r.access || phone.access;
    ls.set('cloudssd_storage_report', JSON.stringify(r));
    renderPermBanner();
    renderStorage();
  }
  function storageSegments(r) {
    const segs = CATS.map(c => ({ label: c.label, color: c.color, bytes: (r.cats[c.key] || {}).bytes || 0, count: (r.cats[c.key] || {}).count || 0, key: c.key, icon: c.icon }));
    segs.push({ label: 'Apps & system', color: '#8E918F', bytes: r.appsSystemBytes || 0, key: 'system', icon: SVG.apk });
    return segs;
  }
  function segBar(segs, total) {
    return segs.filter(s => s.bytes > 0).map(s =>
      `<div class="bar-seg" style="width:${Math.max(0.8, pct(s.bytes, total)).toFixed(2)}%;background:${tint(s.color)}" title="${esc(s.label)}"></div>`).join('');
  }
  function renderStorage() {
    const r = phone.report;
    if (!r || !r.cats) return;
    const segs = storageSegments(r);
    // Phone tab: category tiles + the small panel.
    for (const c of CATS) {
      const s = r.cats[c.key];
      if (s && $('cat-stat-' + c.key)) setText('cat-stat-' + c.key, s.count ? `${s.count.toLocaleString()} · ${fmtDec(s.bytes)}` : 'None');
    }
    const dl = r.cats.downloads;
    if (dl) setText('cat-stat-downloads', dl.count ? `${dl.count.toLocaleString()} · ${fmtDec(dl.bytes)}` : 'Empty');
    setText('cat-stat-starred', starred.length ? starred.length + ' files' : 'None yet');
    $('browser-storage-bar').innerHTML = segBar(segs, r.total);
    $('browser-storage-legend').innerHTML = segs.filter(s => s.bytes > 0).map(s =>
      `<span><span class="legend-dot" style="background:${tint(s.color)}"></span>${esc(s.label)}<span>${fmtDec(s.bytes)}</span></span>`).join('');
    setText('browser-storage-free', fmtDec(r.free) + ' free of ' + fmtDec(r.total));

    // Storage sheet.
    setText('storage-val-used', fmtDec(r.used));
    setText('storage-val-free', fmtDec(r.free));
    setText('storage-val-total', fmtDec(r.total));
    setText('storage-device-sub', [appInfo.manufacturer, appInfo.model].filter(Boolean).join(' ') || 'Internal storage');
    setText('storage-scan-tag', 'Live');
    $('storage-bar').innerHTML = segBar(segs, r.total);
    $('storage-rows').innerHTML = segs.map(s => `
      <div class="usage-row ${s.key !== 'system' && s.key !== 'other' && s.count ? 'tappable' : ''}" ${s.key !== 'system' && s.key !== 'other' ? `data-act="phone-cat" data-cat="${s.key}" data-title="${esc(s.label)}"` : ''}>
        <span class="usage-dot" style="background:${tint(s.color)}"></span>
        <div class="usage-main">
          <div class="usage-name">${esc(s.label)}</div>
          <div class="usage-sub">${s.key === 'system' ? 'Installed apps, their data and Android' : s.count ? s.count.toLocaleString() + ' files' : 'None'}</div>
          <div class="usage-bar"><div style="width:${pct(s.bytes, r.used).toFixed(1)}%;background:${tint(s.color)}"></div></div>
        </div>
        <div class="usage-val">${fmtDec(s.bytes)}</div>
      </div>`).join('') + `
      <div class="usage-row">
        <span class="usage-dot free"></span>
        <div class="usage-main"><div class="usage-name">Free</div><div class="usage-sub">Ready to use</div></div>
        <div class="usage-val">${fmtDec(r.free)}</div>
      </div>`;
    $('storage-note').innerHTML = r.access === 'all'
      ? 'Same numbers as Settings → Storage. Tap a kind to see its files.'
      : (r.access === 'none'
        ? 'Android has not let this app see your files yet, so everything is counted inside "Apps &amp; system". '
        : 'Android is showing this app only photos, videos and audio, so documents, APKs and other files are counted inside "Apps &amp; system". ')
        + '<button class="link-btn" data-act="grant-storage">Allow all files</button>';
  }

  function phoneOpen(view) {
    if (!isApp) return;
    if (phone.view) phone.stack.push(phone.view);
    phoneShow(view);
  }
  function phoneShow(view) {
    phone.view = view;
    phone.selecting = false;
    phone.selected.clear();
    renderSelectBar();
    show('phone-home', false);
    show('phone-list', true);
    setText('phone-list-title', view.title);
    setText('phone-current-path', view.type === 'dir' ? prettyPhonePath(view.path) : view.type === 'cat' ? 'Newest first' : '');
    const box = $('phone-explorer-container');
    box.className = 'phone-file-list';
    box.innerHTML = skeleton(7);
    window.scrollTo(0, 0);
    if (view.type === 'starred') {
      phone.items = { folders: [], files: starred.map(s => Object.assign({}, s)) };
      renderPhoneItems({});
      return;
    }
    const req = ++phone.seq;
    try {
      if (view.type === 'dir') native.listDir(req, view.path || '');
      else native.listCategory(req, view.cat);
    } catch (e) {
      box.innerHTML = emptyBlock(SVG.folder, 'Could not read the phone', String(e && e.message || e));
    }
  }
  function phoneBack() {
    if (phone.selecting) { phone.selecting = false; phone.selected.clear(); renderSelectBar(); renderPhoneItems({}); return true; }
    if (!phone.view) return false;
    const prev = phone.stack.pop();
    if (prev) { phoneShow(prev); return true; }
    phone.view = null;
    show('phone-list', false);
    show('phone-home', true);
    return true;
  }
  function prettyPhonePath(path) {
    return String(path || '').replace(/^\/storage\/emulated\/0\/?/, 'Internal storage/').replace(/\/$/, '').split('/').join(' › ');
  }
  function onPhoneList(d) {
    if (d.reqId === search.phoneReq) { renderSearchPhone(d); return; }
    if (d.reqId !== phone.seq || !phone.view) return;
    if (phone.view.type === 'dir' && d.path) {
      phone.view.path = d.path;
      setText('phone-current-path', prettyPhonePath(d.path));
      if (!phone.view.titled) setText('phone-list-title', d.path === d.root ? 'Internal storage' : d.path.split('/').pop());
    }
    phone.items = {
      folders: d.folders || [],
      files: (d.files || []).map(f => Object.assign(f, { src: 'phone' }))
    };
    renderPhoneItems(d);
  }
  function phoneGrid() {
    if (phone.grid !== null) return phone.grid;
    return !!(phone.view && phone.view.type === 'cat' && (phone.view.cat === 'images' || phone.view.cat === 'videos'));
  }
  function renderPhoneItems(d) {
    const box = $('phone-explorer-container');
    const { folders, files } = phone.items;
    if (d.error && !folders.length && !files.length) {
      box.className = 'phone-file-list';
      box.innerHTML = emptyBlock(SVG.folder, d.error, phone.access !== 'all' ? 'Allow "All files access" so this app can read every folder.' : '', phone.access !== 'all' ? 'Allow all files' : '', 'grant-storage');
      return;
    }
    if (!folders.length && !files.length) {
      box.className = 'phone-file-list';
      const limited = phone.access !== 'all' && phone.view && (phone.view.type === 'dir' || ['documents', 'apps', 'downloads'].includes(phone.view.cat));
      box.innerHTML = emptyBlock(SVG.folder, phone.view && phone.view.type === 'starred' ? 'No starred files yet' : 'Nothing here',
        limited ? 'Android hides documents, APKs and other files from this app until "All files access" is on.' : '',
        limited ? 'Allow all files' : '', 'grant-storage');
      return;
    }
    const grid = phoneGrid();
    box.className = grid ? 'thumb-grid' : 'phone-file-list';
    let html = '';
    folders.forEach((fo, i) => {
      // In select mode a folder can be chosen too: it goes whole, with everything in it.
      const fsel = phone.selected.has('d' + i);
      html += `<div class="phone-item-row ${fsel ? 'selected' : ''}" data-act="phone-folder" data-i="${i}">
        <div class="phone-item-icon folder">${SVG.folder}<div class="row-check">${SVG.check}</div></div>
        <div class="phone-item-info">
          <div class="phone-item-name">${esc(fo.name)}</div>
          <div class="phone-item-meta">${fo.itemCount != null ? fo.itemCount + (fo.itemCount === 1 ? ' item' : ' items') : 'Folder'}</div>
        </div>
        <div class="phone-item-chevron">${SVG.chevron}</div>
      </div>`;
    });
    files.forEach((f, i) => {
      const k = kindOf(f.name);
      const hasThumb = f.src === 'phone' && (k === 'images' || k === 'videos');
      const sel = phone.selected.has(i);
      if (grid) {
        html += `<div class="thumb-tile ${sel ? 'selected' : ''}" data-act="phone-file" data-i="${i}">
          <div class="thumb-tile-icon">${iconFor(f.name)}</div>
          ${hasThumb ? `<img loading="lazy" decoding="async" src="${esc(thumbUrl(f))}" onerror="this.remove()" alt="">` : ''}
          ${k === 'videos' ? `<div class="thumb-tile-play">${SVG.play}</div>` : ''}
          <div class="thumb-tile-check">${SVG.check}</div>
          ${hasThumb ? '' : `<div class="thumb-tile-name">${esc(f.name)}</div>`}
        </div>`;
      } else {
        html += `<div class="phone-item-row ${sel ? 'selected' : ''}" data-act="phone-file" data-i="${i}">
          <div class="phone-item-icon">${iconFor(f.name)}${hasThumb ? `<img loading="lazy" decoding="async" src="${esc(thumbUrl(f))}" onerror="this.remove()" alt="">` : ''}<div class="row-check">${SVG.check}</div></div>
          <div class="phone-item-info">
            <div class="phone-item-name">${esc(f.name)}</div>
            <div class="phone-item-meta">${fmtBytes(f.size)}${f.modified ? ' · ' + fmtDate(f.modified) : ''}${f.src === 'cloud' ? ' · on the PC' : ''}</div>
          </div>
          ${f.src === 'phone' ? `<button class="btn-file-action send" data-act="phone-send" data-i="${i}">Send</button>` : ''}
        </div>`;
      }
    });
    if (d.truncated) html += '<div class="list-note">Showing the newest files only.</div>';
    box.innerHTML = html;
    show('phone-select-btn', files.some(f => f.src === 'phone') || (folders.length > 0 && phone.view && phone.view.type === 'dir'));
  }
  function renderSelectBar() {
    show('phone-select-bar', phone.selecting);
    setText('phone-select-count', phone.selected.size + ' selected');
    setText('phone-select-btn', phone.selecting ? 'Done' : 'Select');
    document.body.classList.toggle('selecting', phone.selecting || ssd.selecting);
  }

  // ------------------------------------------------------------------ Cloud SSD tab (drives on the PC)
  const ssd = {
    // In memory for this session; what lasts across restarts (and while the PC is away) is in `lists`, per PC.
    path: '', root: '', cache: new Map(lists ? [] : Object.entries(ls.json('cloudssd_dircache', {}))), view: { folders: [], files: [] },
    level: 'devices',   // 'devices': the list of saved PCs (the tab starts here) · 'device': inside the active one
    seq: 0, grid: ls.get('cloudssd_layout') === 'grid', sort: ls.get('cloudssd_sort') || 'name-asc', localDrives: [], showAll: false,
    selecting: false, selected: new Set(), suppressClick: 0,
    kind: ''            // a kind of file shown from the open folder and the folders inside it ('' = the folder itself)
  };
  const SORTS = [
    { key: 'name-asc', label: 'Name (A to Z)' }, { key: 'name-desc', label: 'Name (Z to A)' },
    { key: 'date-desc', label: 'Newest first' }, { key: 'date-asc', label: 'Oldest first' },
    { key: 'size-desc', label: 'Largest first' }, { key: 'size-asc', label: 'Smallest first' }
  ];

  /** Path separator: "\" for PC paths, "/" inside a drive id ("cloudssd:Photos/2024"). */
  function sepOf(path) { return isCloudId(path) ? '/' : '\\'; }
  function whenOf(v) { return typeof v === 'number' ? v : (Date.parse(v) || 0); }

  function ssdPlaces() {
    // PC off but Google signed in on this phone: the same drives, straight from Google.
    if (cloudSource() === 'drive') return (drive.roots || []).map(r => ({ root: r.id + ':', label: r.title, cloud: true, mounted: true, id: r.id, drive: true }));
    const st = link.status || {};
    const places = (st.drives || []).map(d => ({ root: d.driveLetter + '\\', label: `${d.driveLetter} ${d.displayName}`, cloud: true, mounted: d.isMounted, id: d.id }));
    // A current PC also serves this folder as the "received" drive, so it can be managed and kept offline like one.
    if (st.uploadDirectory) places.push({ root: st.uploadDirectory, label: 'From phone', cloud: false, mounted: true, id: pcHas('fs-local-roots') ? 'received' : undefined });
    // The PC's own disks: browsed by Windows path; `disk` marks them so a folder can be kept on this phone (keepIdOf).
    for (const d of ssd.localDrives) places.push({ root: d.root, label: `${d.driveLetter} PC disk`, cloud: false, mounted: true, disk: true });
    return places;
  }
  function placeOf(path) {
    const p = String(path || '').toLowerCase();
    return ssdPlaces().filter(pl => p.startsWith(pl.root.toLowerCase())).sort((a, b) => b.root.length - a.root.length)[0] || null;
  }
  function renderSsdDrives() {
    const strip = $('ssd-drives-strip');
    if (!strip) return;
    const cur = placeOf(ssd.path);
    const home = pcHomeOn() ? `<button class="ssd-drive-pill ${ssd.path ? '' : 'active'}" data-act="ssd-home">${HOME_ICON}<span>Home</span></button>` : '';
    const html = home + ssdPlaces().map(pl => `
      <button class="ssd-drive-pill ${cur && cur.root === pl.root ? 'active' : ''} ${pl.mounted ? '' : 'off'}" data-act="ssd-place" data-root="${esc(pl.root)}">
        ${pl.cloud ? SVG.cloud : pl.label === 'From phone' ? SVG.upload : SVG.pc}<span>${esc(pl.label)}${pl.mounted ? '' : ' · off'}</span>
      </button>`).join('');
    if (strip.dataset.html !== html) { strip.innerHTML = html; strip.dataset.html = html; }
    renderSsdKinds();
  }
  // ---- kinds of file: photos, videos… of the folder that is open (the PC looks in the folders inside it too) ----
  const SSD_KINDS = [['', 'All'], ['images', 'Photos'], ['videos', 'Videos'], ['audio', 'Audio'], ['documents', 'Documents'], ['apps', 'Apps and archives']];
  function renderSsdKinds() {
    const strip = $('ssd-kinds-strip');
    if (!strip) return;
    // Only a folder of a PC that is reachable now: Google Drive without the PC has no such search.
    const can = !!ssd.path && link.state === 'online' && cloudSource() !== 'drive';
    strip.style.display = can ? '' : 'none';
    if (!can) return;
    const html = SSD_KINDS.map(([k, name]) => `<button class="transfer-chip ssd-kind-chip ${ssd.kind === k ? 'active' : ''}" data-act="ssd-kind" data-kind="${k}">${name}</button>`).join('');
    if (strip.dataset.html !== html) { strip.innerHTML = html; strip.dataset.html = html; }
  }
  async function loadSsdKind(kind) {
    if (!ssd.path) return;
    if (!kind) { loadSsd(ssd.path, true); return; }
    endSsdSelect();
    ssd.kind = kind;
    renderSsdKinds();
    const box = $('ssd-items-container');
    const seq = ++ssd.seq;
    box.className = 'ssd-items-container list-view';
    box.innerHTML = skeleton(8);
    try {
      const r = await api('/api/files', { params: { path: ssd.path, category: kind }, timeout: 60000 });
      if (seq !== ssd.seq) return;
      const files = r.ok && r.data ? r.data.files || [] : null;
      if (!files) box.innerHTML = emptyBlock(SVG.folder, apiError(r, 'Could not open this folder'), ssd.path, 'Try again', 'ssd-refresh');
      else if (!files.length) box.innerHTML = emptyBlock(SVG.folder, 'No files of this kind here', 'Looked in this folder and the folders inside it.');
      else renderSsd({ folders: [], files });
    } catch (e) {
      if (seq === ssd.seq) box.innerHTML = emptyBlock(SVG.pc, 'PC did not answer', 'The folder could not be loaded. Check the Wi-Fi and try again.', 'Try again', 'ssd-refresh');
    }
  }
  const copyInfo = new Map();   // pcId → {at, data}: what a PC last wrote about its copy
  function copyPcOf(path) { const m = /^pc-([^:]+):/i.exec(String(path || '')); return m ? m[1] : ''; }
  /** The line above the files while the PC is away: where they come from; for a PC's copy also how old it is. */
  function renderDriveNote(path) {
    const el = $('ssd-drive-note');
    if (!el) return;
    const pc = copyPcOf(path);
    if (!pc) { el.textContent = 'PC is off — your files come straight from Google Drive'; return; }
    const hit = copyInfo.get(pc);
    const name = pc === link.pcId && link.pcName ? link.pcName : hit && hit.data && hit.data.name;
    el.textContent = DATA.copyNote(hit && hit.data, name, fmtDate);
    if (hit && Date.now() - hit.at < 5 * 60000) return;
    copyInfo.set(pc, { at: Date.now(), data: hit && hit.data });
    cloudOp('copy-info', { pc }).then(r => {
      if (!r.ok || !r.data) return;
      copyInfo.set(pc, { at: Date.now(), data: r.data });
      if (copyPcOf(ssd.path) === pc) renderDriveNote(ssd.path);
    });
  }
  async function loadDriveRoots() {
    const r = await cloudOp('roots', {});
    if (r.ok && Array.isArray(r.data)) drive.roots = r.data;
    return drive.roots || [];
  }
  /** The drive a fresh visit starts on: the combined cloud drive, else any connected one. */
  function firstPlaceRoot() {
    const places = ssdPlaces();
    const first = places.find(p => p.id === 'cloudssd' && p.mounted) || places.find(p => p.cloud && p.mounted) || places[0];
    return first ? first.root : '';
  }
  // ---- the PC's start, as File Explorer shows it: Quick access (pinned folders), then This PC's drives ----
  const pcHome = { data: null, at: 0, pc: '' };
  /** The PC is here and can tell its Quick access: a device opens on its "Home", like Explorer. */
  function pcHomeOn() { return link.state === 'online' && cloudSource() !== 'drive' && pcHas('pc-home'); }
  const QA_ICONS = { desktop: SVG.desktop, downloads: SVG.download, documents: SVG.doc, pictures: SVG.image, music: SVG.audio, videos: SVG.video, folder: SVG.folder };
  const HOME_ICON = '<svg viewBox="0 0 24 24"><path d="M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z"/></svg>';
  const HDD_ICON = '<svg viewBox="0 0 24 24"><path d="M20 13H4c-.55 0-1 .45-1 1v6c0 .55.45 1 1 1h16c.55 0 1-.45 1-1v-6c0-.55-.45-1-1-1zM7 19c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zM20 3H4c-.55 0-1 .45-1 1v6c0 .55.45 1 1 1h16c.55 0 1-.45 1-1V4c0-.55-.45-1-1-1zM7 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2z"/></svg>';
  async function loadPcHome(force) {
    endSsdSelect();
    ssd.path = '';
    ssd.kind = '';
    ssd.root = '';
    ssd.seq++;
    renderCrumbs();
    renderSsdDrives();
    const mine = pcHome.data && pcHome.pc === link.pcId;
    if (mine) renderPcHome();
    else { $('ssd-items-container').className = 'ssd-items-container pc-home'; $('ssd-items-container').innerHTML = skeleton(6); }
    if (!force && mine && Date.now() - pcHome.at < 30000) return;
    if (!ssd.localDrives.length) loadLocalDrives();
    const r = await api('/api/pc/home', { timeout: 15000 }).catch(() => null);
    if (ssd.path) return;
    if (r && r.ok && r.data) { pcHome.data = r.data; pcHome.at = Date.now(); pcHome.pc = link.pcId; }
    renderPcHome();
  }
  /** Explorer's drive name: "ACER (C:)", "Cloud SSD (Z:)". */
  function pcDriveName(d, pl) {
    const letter = d.driveLetter || pl.root.slice(0, 2);
    return (d.displayName || (pl.cloud ? 'Cloud drive' : 'Local disk')) + ' (' + letter + ')';
  }
  function renderPcHome() {
    if (ssd.path || ssd.level !== 'device') return;
    const box = $('ssd-items-container');
    const d = pcHome.data && pcHome.pc === link.pcId ? pcHome.data : { pinned: [], hidden: 0 };
    const st = link.status || {};
    // Quick access: what is pinned on the PC, in its order; then the folder phone files land in.
    let tiles = (d.pinned || []).map(f => `<div class="cat-grid-card pc-qa-card" data-act="ssd-place" data-root="${esc(f.path)}">
        <div class="cat-grid-icon-box pc-qa-icon pc-qa-${esc(f.kind)}">${QA_ICONS[f.kind] || SVG.folder}</div>
        <div class="cat-grid-name" data-no-i18n>${esc(f.name)}</div><div class="cat-grid-meta">Pinned</div></div>`).join('');
    if (st.uploadDirectory) tiles += `<div class="cat-grid-card pc-qa-card" data-act="ssd-place" data-root="${esc(st.uploadDirectory)}">
        <div class="cat-grid-icon-box pc-qa-icon pc-qa-received">${SVG.upload}</div>
        <div class="cat-grid-name">From phone</div><div class="cat-grid-meta">Cloud-SSD</div></div>`;
    const note = d.hidden ? `<div class="pc-home-note">${esc(d.hidden + ' pinned folders are on the PC\'s own disk. Turn on "PC disks" in Cloud-SSD on the PC to open them here.')}</div>` : '';
    // This PC: its disks and the Cloud SSD drives, by letter, like "Devices and drives".
    const drives = drivesData || st.drives || [];
    const seen = new Set();
    const rows = ssdPlaces().filter(pl => pl.label !== 'From phone' && /^[A-Za-z]:\\/.test(pl.root))
      .filter(pl => { const k = pl.root.toLowerCase(); if (seen.has(k)) return false; seen.add(k); return true; })
      .map(pl => ({ pl, d: drives.find(x => x.root && x.root.toLowerCase() === pl.root.toLowerCase()) || drives.find(x => x.id && x.id === pl.id) || {} }))
      .sort((a, b) => a.pl.root.localeCompare(b.pl.root))
      .map(({ pl, d: dd }) => {
        const cloud = dd.isCloud != null ? !!dd.isCloud : pl.cloud;
        const used = dd.totalBytes > 0 ? dd.totalBytes - dd.freeBytes : 0;
        const space = !pl.mounted ? 'Not connected on the PC' : dd.totalBytes > 0 ? fmtBytes(dd.freeBytes) + ' free of ' + fmtBytes(dd.totalBytes) : '';
        return `<div class="pc-drive-row ${pl.mounted ? '' : 'off'}" data-act="ssd-place" data-root="${esc(pl.root)}">
          <div class="pc-drive-icon ${cloud ? 'cloud' : ''}">${cloud ? SVG.cloud : HDD_ICON}</div>
          <div class="pc-drive-text"><div class="pc-drive-name" data-no-i18n>${esc(pcDriveName(dd, pl))}</div>
          ${pl.mounted && dd.totalBytes > 0 ? `<div class="usage-bar"><div style="width:${Math.max(1, pct(used, dd.totalBytes)).toFixed(1)}%"></div></div>` : ''}
          ${space ? `<div class="pc-drive-sub">${esc(space)}</div>` : ''}</div></div>`;
      }).join('');
    box.className = 'ssd-items-container pc-home';
    box.innerHTML = `<div class="section-container pc-home-sec"><div class="section-header"><div class="section-title">Quick access</div></div>
        ${tiles ? `<div class="categories-grid-m3 pc-qa-grid">${tiles}</div>` : `<div class="pc-home-note">Nothing is pinned to Quick access on the PC.</div>`}${note}</div>
      <div class="section-container pc-home-sec"><div class="section-header"><div class="section-title">This PC</div></div>
        ${rows ? `<div class="pc-drive-list">${rows}</div>` : `<div class="pc-home-note">No drives yet.</div>`}</div>`;
  }

  function openSsdTab() {
    // The tab starts with the list of devices; a device's folders show once one is opened.
    if (ssd.level !== 'device') { showDevices(); return; }
    show('ssd-devices', false);
    show('ssd-browser', true);
    document.body.classList.remove('ssd-at-devices');
    renderDeviceHeader();
    const source = cloudSource();
    // Coming from the PC to Google Drive (or back): the old folder is named the other way.
    if (ssd.path && isCloudId(ssd.path) !== (source === 'drive')) { ssd.path = ''; ssd.cache.clear(); }
    show('ssd-drive-note', source === 'drive');
    if (source !== 'drive' && link.state !== 'online') {
      // The PC is away: everything it showed last time is still here, with when it was saved.
      if (link.state === 'unpaired' || !showingSaved()) { hideOfflineBanner(); renderSsdOffline(); return; }
      if (!ssd.path) { const s = snapOf(); ssd.path = (s && s.ssdPath) || firstPlaceRoot(); }
      if (!ssd.path) { renderSsdOffline(); return; }
      loadSsd(ssd.path);
      return;
    }
    hideOfflineBanner();
    if (source === 'drive') {
      if (!(drive.roots || []).length) {
        $('ssd-items-container').innerHTML = skeleton(6);
        loadDriveRoots().then(roots => { if (roots.length && cloudSource() === 'drive') openSsdTab(); else renderSsdOffline(); });
        return;
      }
      loadSsd(ssd.path || ssdPlaces()[0].root);
      return;
    }
    if (!ssd.localDrives.length) loadLocalDrives();
    if (!ssd.path && pcHomeOn()) { loadPcHome(); return; }
    if (!ssd.path) {
      const root = firstPlaceRoot();
      if (!root) { renderSsdDrives(); $('ssd-items-container').innerHTML = skeleton(6); return; }
      loadSsd(root);
    } else {
      loadSsd(ssd.path);
    }
  }

  // ---- level 1: the devices this phone is paired with ----
  const deviceLive = {};    // id → {at, url}: a saved, not-active PC that answered just now (public id only, no key)
  const deviceSaved = {};   // id → number of folders saved on this phone
  let probing = false;

  function deviceIcon(kind) { return kind === 'laptop' ? SVG.pc : SVG.desktop; }
  /** One line under a device's name: live and how, or offline with when it was last seen and what is saved. */
  function deviceStatus(d, short) {
    const saved = !short && deviceSaved[d.id] ? ` · ${deviceSaved[d.id]} ${deviceSaved[d.id] === 1 ? 'folder' : 'folders'} saved` : '';
    if (d.active) {
      if (link.state === 'online') {
        const drives = link.status && Array.isArray(link.status.drives) ? link.status.drives.filter(x => x.isMounted).map(x => x.driveLetter).join(' ') : '';
        // How the PC sees this phone come in (USB cable, a hotspot, Wi-Fi), as the Home card says it.
        const via = link.route === 'tailnet' ? 'Internet (Tailscale)' : DATA && DATA.linkLabel ? DATA.linkLabel(link.kind) : 'Wi-Fi';
        const guest = link.status && link.status.guestUntil ? ' · Guest until ' + clockTime(link.status.guestUntil) : '';
        return { live: true, text: 'Live · ' + via + (drives ? ' · ' + drives : '') + guest };
      }
      if (link.state === 'init') return { live: false, text: 'Connecting…' + saved };
      if (link.state === 'unpaired') {
        // A guest whose time on that PC is up was removed there: a new guest code is needed, not the usual pairing.
        if (d.guestUntil && guestOver(d.guestUntil)) return { live: false, warn: true, text: 'Guest time ended — ask for a new guest code at that PC' };
        return { live: false, warn: true, text: 'Pair again — this PC no longer knows this phone' };
      }
      const when = link.lastOnline || d.lastOnline;
      return { live: false, text: (link.state === 'paused' ? 'Disconnected' : 'Offline') + (when ? ' · saved ' + lastUpdate(when) : '') + saved };
    }
    const p = deviceLive[d.id];
    if (p && Date.now() - p.at < 45000) return { live: true, text: 'Reachable now · tap to open' + saved };
    return { live: false, text: (d.lastOnline ? 'Last seen ' + lastUpdate(d.lastOnline) : 'Not connected yet') + saved };
  }
  /** "18:05" in the app's language (the end of a guest's time). */
  /** A guest's time is up (or nearly: the phone's clock can be behind the PC's, which ends it on its own clock). */
  function guestOver(until) { return !!until && Date.now() >= until - 60000; }
  function clockTime(ms) { return new Date(ms).toLocaleTimeString(dateLocale(), { hour: '2-digit', minute: '2-digit' }); }
  function devicesList() {
    const all = registry ? registry.list() : [];
    // Nothing saved yet (an older start, or a browser): the PC in use still shows.
    if (!all.length && (link.lan || link.server)) all.push({ id: currentDeviceId(), name: link.pcName, lan: link.lan, active: true });
    return all.sort((a, b) => (b.active ? 1 : 0) - (a.active ? 1 : 0) || (b.lastOnline || 0) - (a.lastOnline || 0));
  }
  function showDevices() {
    ssd.level = 'devices';
    endSsdSelect();
    hideOfflineBanner();
    show('ssd-browser', false);
    show('ssd-devices', true);
    document.body.classList.add('ssd-at-devices');
    renderDeviceList();
    countSavedFolders();
    probeDevices();
  }
  function renderDeviceList() {
    const box = $('ssd-device-list');
    if (!box) return;
    const all = devicesList();
    setText('ssd-devices-count', String(all.length));
    if (!all.length) {
      box.innerHTML = emptyBlock(SVG.pc, 'No PC paired yet', 'Open Cloud-SSD on your PC or laptop and pair once with the 6-digit code shown there.', 'Add device', 'device-add');
      box.dataset.html = '';
      setText('ssd-devices-note', '');
      return;
    }
    const html = all.map(d => {
      const st = deviceStatus(d);
      return `<div class="device-row ${d.active ? 'active' : ''}" data-act="device-open" data-id="${esc(d.id)}">
        <div class="device-row-icon ${st.live ? 'live' : ''}">${deviceIcon(d.kind)}</div>
        <div class="device-row-text">
          <div class="device-row-name"><span class="device-row-title">${esc(deviceLabel(d))}</span>${d.active ? '<span class="device-current-tag">In use</span>' : ''}</div>
          <div class="device-row-sub ${st.warn ? 'warn' : ''}"><span class="device-dot ${st.live ? 'live' : 'off'}"></span>${esc(st.text)}</div>
        </div>
        ${!st.live && canWake(d) ? `<button class="transfer-chip device-wake" data-act="device-wake" data-id="${esc(d.id)}">Wake</button>` : ''}
        <button class="item-more-btn" data-act="device-more" data-id="${esc(d.id)}">${SVG.more}</button>
      </div>`;
    }).join('');
    // Redrawn every few seconds for the times: only when something changed, so a tap is never lost.
    if (box.dataset.html !== html) { box.innerHTML = html; box.dataset.html = html; }
    setText('ssd-devices-note', all.length > 1 ? 'Tap a device to open its drives. Offline devices show what was saved last time.' : 'Tap your PC to open its drives. Pair more PCs or laptops with “+ Add device”.');
  }
  async function countSavedFolders() {
    if (!lists) return;
    const all = devicesList();
    for (const d of all) { try { deviceSaved[d.id] = await lists.count(d.id); } catch (e) { } }
    if (ssd.level === 'devices') renderDeviceList();
  }
  /** Saved PCs that are not in use: do they answer right now? Asked by their public id only — no key is sent. */
  async function probeDevices() {
    if (probing || !registry) return;
    probing = true;
    try {
      for (const d of devicesList().filter(x => !x.active)) {
        let ok = null;
        for (const url of [d.last, d.lan].concat(d.away || []).filter((u, i, a) => u && a.indexOf(u) === i)) {
          try {
            const s = await getPublic(url + '/api/status', 2500);
            if (s.data && s.data.pcId && s.data.pcId === d.id) { ok = url; break; }
          } catch (e) { }
        }
        if (ok) deviceLive[d.id] = { at: Date.now(), url: ok }; else delete deviceLive[d.id];
      }
    } finally { probing = false; }
    if (ssd.level === 'devices' && activeTab === 'ssd') renderDeviceList();
  }
  /** Opens a device: switching to it first when it is not the one in use. */
  function openDevice(id) {
    if (id !== currentDeviceId()) {
      if (!activateDevice(id)) return;
      // It answered a moment ago at another address: start there.
      const p = deviceLive[id];
      if (p && p.url && p.url !== link.server) { link.server = p.url; link.route = /:\/\/100\.|\.ts\.net/.test(p.url) ? 'tailnet' : 'lan'; }
    }
    ssd.level = 'device';
    if (pcHas('pc-home')) ssd.path = '';
    openSsdTab();
  }
  function renderDeviceHeader() {
    const rec = registry && registry.get(currentDeviceId());
    const d = rec || { id: currentDeviceId(), name: link.pcName, active: true };
    $('ssd-device-icon').innerHTML = deviceIcon(d.kind);
    $('ssd-device-icon').className = 'device-row-icon' + (link.state === 'online' ? ' live' : '');
    setText('ssd-device-name', deviceLabel(d) === 'PC' && link.pcName ? link.pcName : deviceLabel(d));
    setText('ssd-device-sub', deviceStatus(Object.assign({}, d, { active: true }), true).text);
  }
  function deviceMenu(id) {
    const rec = registry && registry.get(id);
    if (!rec) return;
    const items = [
      { icon: SVG.open, title: 'Open', sub: rec.active ? 'Its drives and folders' : 'Switch to this PC and open its drives', fn: () => openDevice(id) },
      { icon: SVG.doc, title: 'Rename on this phone', sub: 'Only the name shown here changes', fn: () => renameDevice(id) }
    ];
    if (rec.active) items.push({ icon: SVG.pc, title: 'Connection', sub: 'Address, Tailscale, find it again', fn: () => { renderLink(); openSheet('connection-modal'); } });
    if (canWake(rec) && !deviceAwake(rec)) {
      items.push({ icon: SVG.wifi, title: 'Wake PC', sub: DATA.wakeHint(rec.wol), fn: () => wakeDevice(id) });
    }
    items.push({ icon: SVG.close, title: 'Forget this device', sub: 'Removes it, its key and its saved lists from this phone', danger: true, fn: () => forgetDevice(id) });
    choiceSheet(deviceLabel(rec), items);
  }
  /** Wake-on-LAN: the PC told this phone its network card ("wol" in its status); only the app can send the packet. */
  function canWake(rec) { return !!(rec && rec.wol && rec.wol.mac && isApp && typeof native.wakePc === 'function'); }
  function deviceAwake(rec) {
    if (rec.active) return link.state === 'online';
    const p = deviceLive[rec.id];
    return !!(p && Date.now() - p.at < 45000);
  }
  let waking = '';
  async function wakeDevice(id) {
    const rec = registry && registry.get(id);
    if (!canWake(rec) || waking === id) return;
    let sent = false;
    try { sent = native.wakePc(rec.wol.mac, rec.wol.broadcast || ''); } catch (e) { }
    if (!sent) { toast('Could not send the wake signal. Is this phone on the same Wi-Fi as the PC?'); return; }
    toast('Wake signal sent to ' + deviceLabel(rec) + ' — it can take a minute to start');
    waking = id;
    try {
      // Look for it for up to 2 minutes.
      for (let i = 0; i < 24; i++) {
        await new Promise(r => setTimeout(r, 5000));
        const now = registry.get(id);
        if (!now) return;
        if (now.active) await pollStatus(); else await probeDevices();
        if (deviceAwake(now)) { toast(deviceLabel(now) + ' is awake'); return; }
      }
    } finally { waking = ''; }
  }
  async function renameDevice(id) {
    const rec = registry && registry.get(id);
    if (!rec || !window.CloudSsdFs || !window.CloudSsdFs.ask) return;
    const name = await window.CloudSsdFs.ask('Name on this phone', deviceLabel(rec), 'Save');
    if (name == null) return;
    registry.upsert({ id, label: name.trim().slice(0, 60) });
    if (ssd.level === 'devices') renderDeviceList(); else renderDeviceHeader();
  }

  // ---- the profile menu (round icon at the top right) ----
  function meName() { return appInfo.meName || ls.get('cloudssd_me_name') || (appInfo.model ? `${appInfo.manufacturer || ''} ${appInfo.model}`.trim() : 'This browser'); }
  function profileItems() {
    const n = devicesList().length;
    return [
      { icon: SVG.devices, title: 'Device list', sub: n ? `${n} ${n === 1 ? 'PC' : 'PCs and laptops'} paired with this phone` : 'No PC paired yet', fn: () => { if (activeTab !== 'ssd') switchTab('ssd'); else showDevices(); } },
      { icon: SVG.add, title: 'Add device', sub: 'Pair another PC or laptop with its 6-digit code', fn: () => { pair.purpose = 'add'; showPair(''); } },
      { icon: SVG.wifi, title: 'Connection', sub: link.pcName ? `${link.pcName} · ${({ online: 'connected', offline: 'not reachable', paused: 'disconnected', unpaired: 'not paired', init: 'connecting…' })[link.state] || ''}` : 'Address, Tailscale, find your PC', fn: () => { renderLink(); openSheet('connection-modal'); } },
      { icon: SVG.lock, title: 'Permissions', sub: 'What this phone may do on the PC', fn: () => openPermissions() },
      {
        icon: SVG.share, title: 'Shared & received',
        sub: inbox.offers.length ? `${inbox.offers.length} waiting for you` : 'Files sent to and saved from the PC',
        fn: () => { if (inbox.offers.length) { renderInbox(); openSheet('inbox-sheet'); return; } renderHistory(); renderTransfers(); openSheet('transfers-modal'); refreshHistory(); }
      },
      { icon: SVG.pin, title: 'Offline files', sub: !canKeep() ? 'In the Cloud-SSD app' : kept.pins.length ? `${kept.pins.length} kept on this phone · ${fmtBytes(kept.used)}` : 'Keep folders on this phone to open them without the PC', fn: openOfflineSheet },
      ...(isApp && pcHas('clip') ? [{ icon: SVG.share, title: 'Send clipboard to PC', sub: 'The text you copied goes to ' + (link.pcName || 'the PC') + '\'s clipboard', fn: () => sendClipboard() }] : []),
      { icon: SVG.settings, title: 'Settings', sub: 'Theme, Google Drive, storage saver, about', fn: () => openSettings() }
    ];
  }
  function renderProfileMenu() {
    const name = meName();
    setText('profile-me-avatar', (name[0] || '?').toUpperCase());
    setText('profile-me-name', name);
    setText('profile-me-sub', isApp ? `This phone · Android ${appInfo.android || ''}` : 'This browser');
    $('profile-menu-list').innerHTML = profileItems().map((a, i) => `
      <div class="m3-sheet-row" data-act="profile-item" data-i="${i}">
        ${a.icon}
        <div class="m3-sheet-row-text"><div>${esc(a.title)}</div><div class="m3-sheet-row-sub">${esc(a.sub)}</div></div>
      </div>`).join('');
  }
  function openProfile() { renderProfileMenu(); openSheet('profile-menu'); }

  // ---- the More tab: sections like a phone's Settings — a section opens its page, or a list of rows that open theirs ----
  function moreSections() {
    const n = devicesList().length;
    const state = ({ online: 'connected', offline: 'not reachable', paused: 'disconnected', unpaired: 'not paired', init: 'connecting…' })[link.state] || '';
    const lookName = (LOOK_CARDS.find(l => l[0] === lookMode()) || LOOK_CARDS[0])[1];
    const backup = isApp && typeof native.setBackupOptions === 'function' && typeof actions['backup-settings'] === 'function';
    return [
      {
        key: 'devices', icon: SVG.devices, title: 'Devices and connection', sub: link.pcName ? `${link.pcName} · ${state}` : 'No PC paired yet',
        rows: () => [
          { icon: SVG.devices, title: 'Device list', sub: n ? `${n} ${n === 1 ? 'PC' : 'PCs and laptops'} paired with this phone` : 'No PC paired yet', fn: () => { closeSheet('more-sheet'); switchTab('ssd'); } },
          { icon: SVG.add, title: 'Add device', sub: 'Pair another PC or laptop with its 6-digit code', fn: () => { closeSheet('more-sheet'); pair.purpose = 'add'; showPair(''); } },
          { icon: SVG.wifi, title: 'Connection', sub: link.pcName ? `${link.pcName} · ${state}` : 'Address, Tailscale, find your PC', fn: () => { renderLink(); openSheet('connection-modal'); } },
          { icon: SVG.cloud, title: 'Away from home', sub: 'Reach the PC on mobile data with Tailscale', fn: () => openSettings('away') },
          { icon: SVG.person, title: 'Phones paired with the PC', sub: 'See them, or remove this phone from the PC', fn: () => openSettings('paired') }
        ]
      },
      { key: 'perms', icon: SVG.lock, title: 'Permissions', sub: 'This phone on the PC, the PC on this phone, Android', fn: () => openPermissions() },
      {
        key: 'files', icon: SVG.share, title: 'Files and sharing', sub: inbox.offers.length ? `${inbox.offers.length} waiting for you` : 'Transfers, received files, offline files',
        rows: () => [
          { icon: SVG.upload, title: 'Transfers', sub: 'What is moving now and what finished today', fn: () => actions['open-transfers']() },
          { icon: SVG.download, title: 'Files to receive', sub: inbox.offers.length ? `${inbox.offers.length} waiting for you` : 'Nothing is waiting', fn: () => { renderInbox(); openSheet('inbox-sheet'); } },
          { icon: SVG.info, title: 'Transfer history', sub: 'Everything sent and received before', fn: () => actions['open-history']() },
          { icon: SVG.pin, title: 'Offline files', sub: !canKeep() ? 'In the Cloud-SSD app' : kept.pins.length ? `${kept.pins.length} kept on this phone · ${fmtBytes(kept.used)}` : 'Keep folders on this phone to open them without the PC', fn: openOfflineSheet },
          ...(backup ? [{ icon: SVG.cloud, title: 'Camera backup', sub: 'Which folders go to the cloud drive, and when', fn: () => actions['backup-settings']() }] : []),
          ...(isApp && pcHas('clip') ? [{ icon: SVG.share, title: 'Send clipboard to PC', sub: 'The text you copied goes to ' + (link.pcName || 'the PC') + '\'s clipboard', fn: () => sendClipboard() }] : [])
        ]
      },
      { key: 'look', icon: SVG.image, title: 'Look and language', sub: lookName + (I18N ? ' · ' + (I18N.LANGS[I18N.lang()] || '') : ''), plain: true, fn: () => openCustomize() },
      { key: 'settings', icon: SVG.settings, title: 'Settings', sub: 'Space saver, app lock, Google Drive, about', fn: () => openSettings() }
    ];
  }
  function moreRow(a, act, i) {
    // A row's second line that is only names (the look, the language) is not translated.
    return `<button class="more-row" data-act="${act}" data-i="${i}">
        <span class="more-row-icon">${a.icon}</span>
        <span class="more-row-text"><span class="more-row-title">${esc(a.title)}</span><span class="more-row-sub"${a.plain ? ' data-no-i18n' : ''}>${esc(a.plain && I18N ? a.sub.split(' · ').map(x => I18N.t(x)).join(' · ') : a.sub)}</span></span>
        <span class="more-row-go">${SVG.chevron}</span>
      </button>`;
  }
  let moreOpen = '';      // the section whose page is open
  function renderMoreSheet() {
    const sec = moreSections().find(x => x.key === moreOpen);
    if (!sec || !sec.rows) return;
    setText('more-sheet-title', sec.title);
    $('more-sheet-list').innerHTML = sec.rows().map((a, i) => moreRow(a, 'more-item', i)).join('');
  }
  function renderMore() {
    const name = meName();
    setText('more-me-avatar', (name[0] || '?').toUpperCase());
    setText('more-me-name', name);
    setText('more-me-sub', isApp ? `This phone · Android ${appInfo.android || ''}` : 'This browser');
    const list = $('more-list');
    if (list) list.innerHTML = moreSections().map((a, i) => moreRow(a, 'more-open', i)).join('');
    if (isOpen('more-sheet')) renderMoreSheet();
  }
  /** This phone's name, as the PCs it pairs with from now on will show it. */
  async function editMe() {
    if (!window.CloudSsdFs || !window.CloudSsdFs.ask) return;
    const v = await window.CloudSsdFs.ask('Name of this phone', meName(), 'Save');
    if (v == null) return;
    const name = v.trim().slice(0, 60);
    if (isApp) { try { native.setMeName(name); } catch (e) { } refreshAppInfo(); } else if (name) ls.set('cloudssd_me_name', name); else ls.del('cloudssd_me_name');
    renderProfileMenu();
    renderMore();
    renderLink();
    toast(name ? 'This phone is now called ' + name + ' (shown on PCs it pairs with from now on)' : 'Name reset');
  }

  // ---- the "Offline · last update 10:42 (5 min ago)" bar over the saved list ----
  let bannerSavedAt = 0, bannerWhy = '';
  function showOfflineBanner(savedAt, why) {
    const el = $('offline-banner');
    if (!el) return;
    bannerSavedAt = savedAt || link.lastOnline || bannerSavedAt;
    bannerWhy = why || '';
    renderOfflineBanner();
    el.style.display = '';
  }
  function hideOfflineBanner() { show('offline-banner', false); bannerWhy = ''; }
  function renderOfflineBanner() {
    const name = link.pcName || 'the PC';
    const what = link.state === 'online' ? 'Not refreshed' : link.state === 'init' ? 'Connecting' : 'Offline';
    setText('offline-banner-title', `${what} · last update ${bannerSavedAt ? lastUpdate(bannerSavedAt) : 'earlier'}`);
    setText('offline-banner-sub', bannerWhy || `Showing what was saved on this phone. Opening files needs ${name}.`);
  }
  async function loadLocalDrives() {
    try {
      const r = await api('/api/drives', { timeout: 15000 });
      if (r.ok && Array.isArray(r.data)) {
        drivesData = r.data;
        ssd.localDrives = r.data.filter(d => !d.isCloud && d.root);
        saveSnap({ drives: r.data, drivesAt: Date.now() });
        renderSsdDrives();
        if (!ssd.path && activeTab === 'ssd') renderPcHome();
      }
    } catch (e) { }
  }
  function renderSsdOffline() {
    renderSsdDrives();
    $('ssd-breadcrumbs-path').textContent = '';
    $('ssd-items-container').className = 'ssd-items-container list-view';
    const googleHint = isApp && drive.available && !(drive.accounts || []).length
      ? ' Or sign in to Google in Settings to open your Drive without the PC.' : '';
    $('ssd-items-container').innerHTML = link.state === 'unpaired'
      ? emptyBlock(SVG.pc, 'Not paired with a PC', 'Pair once with the code shown in Cloud-SSD on the PC.' + googleHint, 'Pair now', 'open-pair')
      : emptyBlock(SVG.pc, 'PC is not connected', (link.away.length ? 'Cloud-SSD must be open on the PC. Away from home, turn on Tailscale on this phone.' : 'Cloud-SSD must be open on the PC, on the same Wi-Fi.') + googleHint, 'Try again', 'toggle-link');
  }
  function renderCrumbs() {
    const pl = placeOf(ssd.path);
    const box = $('ssd-breadcrumbs-path');
    if (!ssd.path) {
      box.innerHTML = pcHomeOn() ? '<span class="crumb">Home</span>' : '';
      $('ssd-back-btn').classList.add('disabled');
      return;
    }
    const sep = sepOf(ssd.path);
    const root = pl ? pl.root : isCloudId(ssd.path) ? ssd.path.slice(0, ssd.path.indexOf(':') + 1) : ssd.path.slice(0, 3);
    const rest = ssd.path.slice(root.length).split(sep).filter(Boolean);
    let acc = root.replace(/\\$/, '');
    let html = (pcHomeOn() ? '<span class="crumb" data-act="ssd-home">Home</span><span class="crumb-sep">›</span>' : '')
      + `<span class="crumb" data-act="ssd-crumb" data-path="${esc(root)}">${esc(pl ? pl.label : root)}</span>`;
    for (const seg of rest) {
      acc += (acc.endsWith(':') && sep === '/' ? '' : sep) + seg;
      html += `<span class="crumb-sep">›</span><span class="crumb" data-act="ssd-crumb" data-path="${esc(acc)}" data-no-i18n>${esc(seg)}</span>`;
    }
    box.innerHTML = html;
    box.scrollLeft = box.scrollWidth;
    ssd.root = root;
    $('ssd-back-btn').classList.toggle('disabled', !rest.length && !pcHomeOn());
  }
  /** A browser without IndexedDB keeps the 12 most recent folders here (as before); the app uses `lists`. */
  function persistDirCache() {
    if (lists) return;
    const entries = Array.from(ssd.cache.entries()).slice(-12).filter(([, v]) => (v.folders.length + v.files.length) <= 400);
    ls.set('cloudssd_dircache', JSON.stringify(Object.fromEntries(entries)));
  }
  /** The saved list of a folder: this session's copy, else the one kept on the phone from last time. */
  async function savedList(path) {
    const mem = ssd.cache.get(path);
    if (mem) return mem;
    if (!lists || !link.pcId) return null;
    const rec = await lists.get(link.pcId, path);
    if (rec) ssd.cache.set(path, rec);
    return rec;
  }
  async function loadSsd(path, force) {
    if (ssd.path !== path) endSsdSelect();
    ssd.path = path;
    ssd.kind = '';
    ssd.showAll = false;
    renderCrumbs();
    if (isCloudId(path) && cloudSource() === 'drive') { renderDriveNote(path); loadDriveFolder(path); return; }
    renderSsdDrives();
    const box = $('ssd-items-container');
    const seq = ++ssd.seq;
    if (!ssd.cache.has(path)) { box.className = 'ssd-items-container list-view'; box.innerHTML = skeleton(8); }
    const cached = await savedList(path);
    if (seq !== ssd.seq) return;
    if (cached) renderSsd(cached);

    // The PC is away: the saved list (with when it was saved), or a clear note that this folder was never saved.
    if (link.state !== 'online') {
      const fromKept = !cached && link.state !== 'init' && keptList(path);
      if (fromKept) {
        renderSsd(fromKept);
        showOfflineBanner(fromKept.savedAt, `Kept on this phone: these files open without ${link.pcName || 'the PC'}.`);
        return;
      }
      if (!cached) {
        box.className = 'ssd-items-container list-view';
        box.innerHTML = link.state === 'init' ? skeleton(8)
          : emptyBlock(SVG.pc, 'Not saved on this phone', `This folder was not opened while ${link.pcName || 'the PC'} was on, so there is no list to show. It opens as soon as the PC is back.`, 'Try again', 'ssd-refresh');
      }
      if (link.state !== 'init') showOfflineBanner(cached && cached.savedAt);
      return;
    }

    $('ssd-refresh-btn').classList.add('spinning');
    try {
      const r = await api('/api/files', { params: { path }, timeout: 60000 });
      if (seq !== ssd.seq) return;
      if (r.ok && r.data) {
        const data = { folders: r.data.folders || [], files: r.data.files || [] };
        // Saved for next time and for when the PC is away; compared with last time for the NEW badges.
        const rec = lists && link.pcId ? await lists.put(link.pcId, path, data) : Object.assign({ savedAt: Date.now() }, data);
        if (seq !== ssd.seq) return;
        ssd.cache.delete(path);
        ssd.cache.set(path, rec);
        persistDirCache();
        saveSnap({ ssdPath: path });
        hideOfflineBanner();
        renderSsd(rec);
      } else {
        const pl = placeOf(path);
        if (r.data && r.data.driveMissing && pl && pl.cloud) {
          box.innerHTML = emptyBlock(SVG.cloud, `${pl.label} is not connected`, 'The drive is switched off on the PC. Connect it to browse its files.', 'Connect drive', 'drive-mount', `data-id="${esc(pl.id)}"`);
        } else if (r.status === 403) {
          box.innerHTML = emptyBlock(SVG.pc, 'Browsing is switched off', apiError(r));
        } else if (r.status === 404) {
          // Gone on the PC: the saved copy goes too.
          ssd.cache.delete(path);
          if (lists && link.pcId) lists.del(link.pcId, path);
          box.innerHTML = emptyBlock(SVG.folder, apiError(r, 'This folder is not there any more'), path, 'Try again', 'ssd-refresh');
        } else if (r.status !== 401) {
          if (cached) toast(apiError(r, 'Could not refresh this folder'));
          else box.innerHTML = emptyBlock(SVG.folder, apiError(r, 'Could not open this folder'), path, 'Try again', 'ssd-refresh');
        }
      }
    } catch (e) {
      if (seq !== ssd.seq) return;
      if (!cached) box.innerHTML = emptyBlock(SVG.pc, 'PC did not answer', 'The folder could not be loaded. Check the Wi-Fi and try again.', 'Try again', 'ssd-refresh');
      else showOfflineBanner(cached.savedAt, `${link.pcName || 'The PC'} did not answer — showing the list saved on this phone.`);
    } finally {
      if (seq === ssd.seq) $('ssd-refresh-btn').classList.remove('spinning');
    }
  }
  /** A kept folder never opened here: its list as made from the copies on this phone (while the PC is away). */
  function keptList(path) {
    if (!canKeep() || !window.CloudSsdFs) return null;
    const id = keepIdOf({ path });
    const pl = placeOf(path);
    if (!id || (!pl && !isCloudId(path))) return null;
    // Kept itself (or inside a kept folder); else a folder on the way down to kept ones: it shows only that way.
    const pin = keptPin(id);
    const below = pin || !DATA.pinsBelow ? [] : DATA.pinsBelow(kept.pins, id);
    if (!pin && !below.length) return null;
    const toPath = (cid) => isCloudId(path) ? cid : pl.root.replace(/\\+$/, '') + '\\' + cid.slice(cid.indexOf(':') + 1).split('/').join('\\');
    const list = DATA.keptListing(kept.files, id, toPath);
    if (!pin && !list.folders.length && !list.files.length) return null;
    return Object.assign(list, { savedAt: pin ? pin.synced || 0 : Math.max(0, ...below.map(p => p.synced || 0)) });
  }
  /** A folder on Google Drive, read by the app (PC off). */
  async function loadDriveFolder(path) {
    renderSsdDrives();
    const box = $('ssd-items-container');
    const cached = ssd.cache.get(path);
    if (cached) renderSsd(cached);
    else { box.className = 'ssd-items-container list-view'; box.innerHTML = skeleton(8); }
    const seq = ++ssd.seq;
    $('ssd-refresh-btn').classList.add('spinning');
    const r = await cloudOp('list', { path });
    if (seq !== ssd.seq) return;
    $('ssd-refresh-btn').classList.remove('spinning');
    if (r.ok && r.data) {
      const entries = r.data.entries || [];
      const data = {
        folders: entries.filter(e => e.isDirectory).map(e => ({ name: e.name, path: e.id, modified: e.modified })),
        files: entries.filter(e => !e.isDirectory).map(e => ({ name: e.name, path: e.id, size: e.size, modified: e.modified, exported: e.exported, readOnly: !e.canEdit }))
      };
      ssd.cache.set(path, data);
      renderSsd(data);
    } else if (!cached) {
      box.innerHTML = emptyBlock(SVG.cloud, r.error || 'Google Drive did not answer', 'Check the internet connection, or sign in again in Settings.', 'Try again', 'ssd-refresh');
    } else toast(r.error || 'Showing the saved list');
  }
  function sortFiles(files) {
    const [by, dir] = ssd.sort.split('-');
    const m = dir === 'desc' ? -1 : 1;
    const cmp = {
      name: (a, b) => a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' }),
      date: (a, b) => whenOf(a.modified) - whenOf(b.modified),
      size: (a, b) => (a.size || 0) - (b.size || 0)
    }[by] || (() => 0);
    return files.slice().sort((a, b) => m * cmp(a, b));
  }
  function renderSsd(data) {
    const box = $('ssd-items-container');
    const [by] = ssd.sort.split('-');
    const folders = by === 'size' ? data.folders.slice() : sortFiles(data.folders);
    const files = sortFiles(data.files).map(f => Object.assign({}, f, { src: 'cloud' }));
    ssd.view = { folders, files };
    const saved = showingSaved();
    box.className = 'ssd-items-container ' + (ssd.grid ? 'grid-view' : 'list-view') + (saved ? ' saved-mode' : '');
    if (!folders.length && !files.length) { box.innerHTML = emptyBlock(SVG.folder, 'This folder is empty', saved ? 'As saved ' + lastUpdate(data.savedAt) : ''); return; }
    const LIMIT = 300;
    const total = folders.length + files.length;
    // On a cloud drive a folder can be renamed, moved and deleted (fs.js), so it gets a "more" button.
    const manage = !!window.CloudSsdFs && window.CloudSsdFs.canManage(ssd.path);
    // Kept on this phone: a folder can be kept (or let go) even while the PC is away.
    const keepHere = canKeep() && !!window.CloudSsdFs && !!keepIdOf({ path: ssd.path });
    const keptBadge = (item, isFolder) => {
      if (!keepHere) return '';
      const id = keepIdOf(Object.assign({ src: 'cloud' }, item));
      if (!id) return '';
      if (!isFolder && kept.files[id]) return '<span class="ssd-pin-badge">OFFLINE</span>';
      return keptPin(id) ? '<span class="ssd-pin-badge' + (isFolder ? '' : ' pending') + '">' + (isFolder ? 'OFFLINE' : 'OFFLINE…') + '</span>' : '';
    };
    // Files and folders that appeared on the PC since this folder was last opened get a NEW badge (for a day).
    const now = Date.now();
    const badge = (name) => DATA && DATA.isNew && DATA.isNew(data, name, now) ? '<span class="ssd-new-badge">NEW</span>' : '';
    let n = 0, html = '';
    for (let i = 0; i < folders.length && (ssd.showAll || n < LIMIT); i++, n++) {
      const sel = ssd.selected.has('d' + i);
      html += `<div class="explorer-item folder-item ${sel ? 'selected' : ''}" data-act="ssd-folder" data-i="${i}" data-key="d${i}">
        <div class="item-icon folder-icon">${SVG.folder}<div class="row-check">${SVG.check}</div></div>
        <div class="item-details"><div class="item-name">${esc(folders[i].name)}</div><div class="item-meta">${badge(folders[i].name)}${keptBadge(folders[i], true)}Folder${folders[i].modified ? ' · ' + fmtDate(folders[i].modified) : ''}</div></div>
        ${manage || keepHere ? `<button class="item-more-btn" data-act="fs-folder-more" data-i="${i}">${SVG.more}</button>` : `<div class="item-action-icon">${SVG.chevron}</div>`}
      </div>`;
    }
    for (let i = 0; i < files.length && (ssd.showAll || n < LIMIT); i++, n++) {
      const f = files[i];
      const thumb = ssdThumb(f);
      const sel = ssd.selected.has('f' + i);
      html += `<div class="explorer-item file-item ${thumb ? 'has-thumb' : ''} ${sel ? 'selected' : ''}" data-act="ssd-file" data-i="${i}" data-key="f${i}">
        <div class="item-icon kind-${kindOf(f.name)}">${iconFor(f.name)}${thumb ? `<img loading="lazy" decoding="async" src="${esc(thumb)}" onerror="this.remove()" alt="">` : ''}${kindOf(f.name) === 'videos' && thumb ? `<div class="thumb-tile-play">${SVG.play}</div>` : ''}<div class="row-check">${SVG.check}</div></div>
        <div class="item-details"><div class="item-name">${esc(f.name)}</div><div class="item-meta">${badge(f.name)}${keptBadge(f, false)}${f.size > 0 ? fmtBytes(f.size) : f.exported ? 'Google file' : '—'}${f.modified ? ' · ' + fmtDate(f.modified) : ''}</div></div>
        <button class="item-more-btn" data-act="ssd-more" data-i="${i}">${SVG.more}</button>
      </div>`;
    }
    if (n < total) html += `<button class="m3-tonal-btn list-more" data-act="ssd-show-all">Show all ${total} items</button>`;
    else html += `<div class="list-note">${folders.length} ${folders.length === 1 ? 'folder' : 'folders'} · ${files.length} ${files.length === 1 ? 'file' : 'files'}${cloudSource() === 'drive' ? ' · from Google Drive' : saved ? ' · saved ' + esc(lastUpdate(data.savedAt)) : ''}${data.truncated ? ' · list cut short' : ''}</div>`;
    box.innerHTML = html;
    box.classList.toggle('selecting', ssd.selecting);
  }
  /** A small picture for photos and videos: made by the PC (and kept there), or by Google Drive. */
  function ssdThumb(f) {
    const k = kindOf(f.name);
    if (k !== 'images' && k !== 'videos') return '';
    if (isCloudId(f.path)) return cloudSource() === 'drive' ? 'http://cloudssd.cloud/thumb?id=' + encodeURIComponent(f.path) : '';
    if (!pcHas('fs-thumb') || !window.CloudSsdFs) return '';
    // Also for photos on the PC's own disk, which the PC serves as a read-only drive ("disk-c:…").
    const id = window.CloudSsdFs.idOf(f.path) || diskIdOf(f.path);
    if (!id) return '';
    // In the app the picture comes through the app, which keeps it: it also shows while the PC is away, and the
    // page's image addresses never carry the key.
    if (isApp) return `http://cloudssd.pcthumb/?id=${encodeURIComponent(id)}&w=${ssd.grid ? 320 : 160}&m=${whenOf(f.modified)}&s=${f.size || 0}`;
    return apiUrl('/api/fs/thumb', { path: id, w: ssd.grid ? 320 : 160 });
  }
  function ssdUp() {
    if (ssd.path && ssd.path.length <= ssd.root.length && pcHomeOn()) { loadPcHome(); return true; }
    if (!ssd.path || ssd.path.length <= ssd.root.length) return false;
    const sep = sepOf(ssd.path);
    const trimmed = sep === '\\' ? ssd.path.replace(/\\+$/, '') : ssd.path.replace(/\/+$/, '');
    const cut = trimmed.lastIndexOf(sep);
    let parent = cut < 0 ? ssd.root : trimmed.slice(0, cut);
    if (parent.length < ssd.root.length) parent = ssd.root;
    if (/^[A-Za-z]:$/.test(parent)) parent += '\\';
    loadSsd(parent);
    return true;
  }

  // ---- several items at once on a cloud drive ----
  function ssdSelectedItems() {
    return Array.from(ssd.selected).map(k => {
      const i = +k.slice(1);
      if (k[0] === 'd') { const fo = ssd.view.folders[i]; return fo && { name: fo.name, path: fo.path, isFolder: true }; }
      const f = ssd.view.files[i];
      return f && Object.assign({}, f, { isFolder: false });
    }).filter(Boolean);
  }
  function renderSsdSelectBar() {
    show('ssd-select-bar', ssd.selecting);
    setText('ssd-select-count', ssd.selected.size + ' selected');
    document.body.classList.toggle('selecting', ssd.selecting || phone.selecting);
    const box = $('ssd-items-container');
    if (box) box.classList.toggle('selecting', ssd.selecting);
  }
  function startSsdSelect(key) {
    if (!ssd.selecting) { ssd.selecting = true; ssd.selected.clear(); }
    if (key) toggleSsdItem(key);
    renderSsdSelectBar();
  }
  function endSsdSelect() {
    if (!ssd.selecting && !ssd.selected.size) return;
    ssd.selecting = false;
    ssd.selected.clear();
    document.querySelectorAll('#ssd-items-container .selected').forEach(el => el.classList.remove('selected'));
    renderSsdSelectBar();
  }
  function toggleSsdItem(key) {
    if (ssd.selected.has(key)) ssd.selected.delete(key); else ssd.selected.add(key);
    const el = document.querySelector(`#ssd-items-container [data-key="${key}"]`);
    if (el) el.classList.toggle('selected', ssd.selected.has(key));
    renderSsdSelectBar();
  }
  async function ssdBulk(what) {
    const items = ssdSelectedItems();
    if (!items.length) { toast('Tap files to select them'); return; }
    if (what === 'download') {
      const files = items.filter(i => !i.isFolder && !i.exported);
      if (!files.length) {
        toast(items.some(i => i.isFolder) ? 'Folders cannot be saved at once; open them and select the files' : 'Google Docs files cannot be saved here; open them with another app');
        return;
      }
      const big = files.reduce((s, f) => s + (f.size || 0), 0);
      if (!await confirmBigOnMobileData({ name: files.length + ' files', size: big })) return;
      if (isApp) {
        const list = files.map(f => ({
          url: isCloudId(f.path) && cloudSource() === 'drive' ? 'cloud:' + f.path
            : isCloudId(f.path) ? apiUrl('/api/fs/read', { path: f.path }) : apiUrl('/api/download', { path: f.path }),
          name: f.name, size: f.size || -1
        }));
        try { native.downloadFiles(JSON.stringify(list)); toast('Saving ' + files.length + (files.length === 1 ? ' file' : ' files') + ' to Downloads'); }
        catch (e) { files.forEach(f => downloadToPhone(f)); }
      } else {
        for (const f of files) { await downloadToPhone(f); await new Promise(r => setTimeout(r, 700)); }
      }
      endSsdSelect();
      return;
    }
    if (!window.CloudSsdFs || !window.CloudSsdFs.canManage(ssd.path)) { toast('Files here cannot be changed from the phone'); return; }
    if (what === 'move') { window.CloudSsdFs.startMove(items); endSsdSelect(); }
    else if (what === 'delete') { if (await window.CloudSsdFs.bulkDelete(items)) endSsdSelect(); }
  }
  /** Long press on a cloud item starts selecting. */
  function wireSsdLongPress() {
    const box = $('ssd-items-container');
    if (!box) return;
    let timer = null;
    box.addEventListener('touchstart', (e) => {
      const el = e.target.closest('[data-key]');
      if (!el || e.target.closest('.item-more-btn')) return;
      timer = setTimeout(() => {
        timer = null;
        ssd.suppressClick = Date.now();
        if (navigator.vibrate) { try { navigator.vibrate(20); } catch (x) { } }
        startSsdSelect(el.dataset.key);
      }, 520);
    }, { passive: true });
    const cancel = () => { if (timer) { clearTimeout(timer); timer = null; } };
    box.addEventListener('touchmove', cancel, { passive: true });
    box.addEventListener('touchend', cancel);
    box.addEventListener('contextmenu', (e) => { if (e.target.closest('[data-key]')) e.preventDefault(); });
  }

  // ------------------------------------------------------------------ PC drives + cloud storage sheets
  let drivesData = null;
  const driveBusy = {};

  async function loadDrives() {
    if (link.state !== 'online') { renderDrives(); renderCloud(); return; }
    try {
      const r = await api('/api/drives', { timeout: 15000 });
      if (r.ok && Array.isArray(r.data)) {
        drivesData = r.data;
        ssd.localDrives = r.data.filter(d => !d.isCloud && d.root);
        saveSnap({ drives: r.data, drivesAt: Date.now() });
      } else if (r.status === 403) {
        drivesData = { error: apiError(r) };
      }
    } catch (e) { /* keep the last list; the tag shows offline */ }
    renderDrives();
    renderCloud();
  }
  function driveSpace(d) {
    if (!d.isMounted) return 'Not connected on the PC';
    if (d.totalBytes > 0) return `${fmtBytes(d.totalBytes - d.freeBytes)} used of ${fmtBytes(d.totalBytes)} · <strong>${fmtBytes(d.freeBytes)} free</strong>`;
    return 'Connected';
  }
  function driveCard(d, compact, saved) {
    // Starting/Stopping/Failed come from the PC: it is working on the drive (Failed = it is retrying by itself).
    const busy = saved ? '' : driveBusy[d.id] || (!d.isMounted && d.state === 'Starting' ? 'mount' : d.state === 'Stopping' ? 'unmount' : '');
    const retrying = !saved && !d.isMounted && d.state === 'Failed' && !busy;
    let actions = '';
    if (d.canToggle && !saved) {
      actions += d.isMounted
        ? `<button class="drive-btn unmount" data-act="drive-unmount" data-id="${esc(d.id)}" ${busy ? 'disabled' : ''}>${busy === 'unmount' ? '<span class="spinner"></span>Disconnecting…' : 'Disconnect'}</button>`
        : `<button class="drive-btn mount" data-act="drive-mount" data-id="${esc(d.id)}" ${busy ? 'disabled' : ''}>${busy === 'mount' ? '<span class="spinner"></span>Connecting…' : 'Connect'}</button>`;
    }
    if (d.isMounted && d.root) actions += `<button class="drive-btn browse" data-act="drive-browse" data-root="${esc(d.root)}">Browse files</button>`;
    return `<div class="drive-manager-card ${d.isMounted ? 'mounted' : ''}">
      <div class="drive-card-header">
        <span class="drive-letter-pill ${d.isCloud ? 'cloud' : 'local'}">${esc(d.driveLetter)}</span>
        <span class="drive-conn-badge ${d.isMounted ? 'mounted' : 'disconnected'}">${busy ? (busy === 'mount' ? 'Connecting…' : 'Disconnecting…') : d.isMounted ? '● Connected' : retrying ? 'PC is retrying…' : '○ Disconnected'}</span>
      </div>
      <div class="drive-card-name">${esc(d.displayName)}${d.readOnly ? ' <span class="ro-tag">read-only</span>' : ''}</div>
      <div class="drive-card-type">${esc(d.connectionType || '')}</div>
      ${d.isCloud && d.remoteSpec && !compact ? `<div class="drive-card-accounts">${esc(d.remoteSpec).split(' + ').join('<br>')}</div>` : ''}
      <div class="drive-card-space">${driveSpace(d)}</div>
      ${d.isMounted && d.totalBytes > 0 ? `<div class="usage-bar wide"><div style="width:${Math.max(1, pct(d.totalBytes - d.freeBytes, d.totalBytes)).toFixed(1)}%"></div></div>` : ''}
      <div class="drive-card-actions">${actions}</div>
    </div>`;
  }
  function renderDrives() {
    const box = $('drives-list-container');
    if (!box) return;
    // The PC is away: the drives as it last showed them (no connect/disconnect), with when that was.
    const saved = link.state !== 'online';
    if (saved && !(Array.isArray(drivesData) && link.pcId)) { box.innerHTML = emptyBlock(SVG.pc, 'PC is not connected', 'Drives are shown live from the PC.', 'Try again', 'toggle-link'); return; }
    if (!drivesData) { box.innerHTML = skeleton(3); return; }
    if (drivesData.error) { box.innerHTML = emptyBlock(SVG.pc, 'Switched off on the PC', drivesData.error); return; }
    const cloud = drivesData.filter(d => d.isCloud), local = drivesData.filter(d => !d.isCloud);
    const s = saved ? snapOf() : null;
    setText('drives-sub', `${cloud.filter(d => d.isMounted).length} of ${cloud.length} cloud drives connected on ${link.pcName || 'the PC'}` +
      (saved ? ' · last known ' + lastUpdate((s && s.drivesAt) || link.lastOnline) : ''));
    box.innerHTML = (saved ? `<div class="offline-inline-note">${esc((link.pcName || 'The PC') + ' is offline — this is how the drives were last time.')}</div>` : '') +
      (cloud.length ? '<div class="sheet-subtitle">Cloud drives</div>' + cloud.map(d => driveCard(d, false, saved)).join('')
      : emptyBlock(SVG.cloud, 'No cloud drive yet', 'Add a cloud account in Cloud-SSD on the PC.')) +
      (local.length ? '<div class="sheet-subtitle">Disks in the PC</div>' + local.map(d => driveCard(d, false, saved)).join('') : '');
  }
  function renderCloud() {
    const st = link.status;
    const c = cloudTotals(st && st.cloud);
    const online = link.state === 'online';
    // The PC is away: its last numbers, marked as saved.
    const saved = !online && !!c && !!link.pcId;
    setText('cloud-live-tag', online ? 'Live from the PC' : saved ? 'Saved ' + timeAgo(link.lastOnline || Date.now()) : 'PC offline');
    $('cloud-live-tag').className = 'conn-status-tag ' + (online ? 'connected' : 'offline');
    if ((!online && !saved) || !c) {
      ['cloud-val-used', 'cloud-val-free', 'cloud-val-total'].forEach(id => setText(id, '—'));
      $('cloud-bar').innerHTML = '';
      $('cloud-accounts').innerHTML = online ? '<div class="recent-empty-card">Update Cloud-SSD on the PC to see account sizes here.</div>' : '';
      $('cloud-drives').innerHTML = '';
      setText('cloud-note', online ? '' : 'Connect to the PC to see your cloud storage.');
      return;
    }
    setText('cloud-val-used', c.totalBytes > 0 ? fmtBytes(c.usedBytes) : '—');
    setText('cloud-val-free', c.totalBytes > 0 ? fmtBytes(c.freeBytes) : '—');
    setText('cloud-val-total', c.totalBytes > 0 ? fmtBytes(c.totalBytes) : '—');
    setText('cloud-sub', c.accountsCount + (c.accountsCount === 1 ? ' account' : ' accounts') + ' joined into one drive');
    const colors = ['#A8C7FA', '#6DD58C', '#FDD663', '#F48FB1', '#78D9EC', '#B39DDB'];
    const accounts = c.accounts || [];
    $('cloud-bar').innerHTML = c.totalBytes > 0
      ? accounts.filter(a => a.usedBytes > 0).map((a, i) => `<div class="bar-seg" style="width:${Math.max(0.8, pct(a.usedBytes, c.totalBytes)).toFixed(2)}%;background:${colors[i % colors.length]}"></div>`).join('')
      : '';
    $('cloud-accounts').innerHTML = accounts.length ? accounts.map((a, i) => `
      <div class="usage-row">
        <span class="usage-dot" style="background:${colors[i % colors.length]}"></span>
        <div class="usage-main">
          <div class="usage-name">${esc(a.name)}</div>
          <div class="usage-sub">${a.totalBytes > 0 ? `${fmtBytes(a.usedBytes)} used of ${fmtBytes(a.totalBytes)}` : a.reachable === false ? 'Could not be reached just now' : 'Reading size…'}</div>
          ${a.totalBytes > 0 ? `<div class="usage-bar"><div style="width:${Math.max(1, pct(a.usedBytes, a.totalBytes)).toFixed(1)}%;background:${colors[i % colors.length]}"></div></div>` : ''}
        </div>
        <div class="usage-val">${a.totalBytes > 0 ? fmtBytes(a.freeBytes) + '<small>free</small>' : '—'}</div>
      </div>`).join('') : '<div class="recent-empty-card">No cloud account is connected. Add one in Cloud-SSD on the PC.</div>';
    const cloudDrives = Array.isArray(drivesData) ? drivesData.filter(d => d.isCloud) : [];
    $('cloud-drives').innerHTML = cloudDrives.length ? cloudDrives.map(d => driveCard(d, true, saved)).join('') : saved ? '' : skeleton(2);
    setText('cloud-note', saved ? `Last numbers from ${link.pcName || 'the PC'}, saved ${lastUpdate(link.lastOnline)}. They refresh when it is back.`
      : c.refreshedAt ? 'Sizes checked with the cloud ' + timeAgo(c.refreshedAt) + '. The PC re-checks while this is open.' : 'The PC is asking the cloud for sizes…');
  }
  async function toggleDrive(id, mount) {
    if (driveBusy[id]) return;
    const d = Array.isArray(drivesData) ? drivesData.find(x => x.id === id) : null;
    const label = d ? `${d.driveLetter} ${d.displayName}` : 'this drive';
    if (!mount) {
      const ok = await confirmDialog('Disconnect ' + label + '?',
        'The drive letter disappears on the PC until you connect it again. Programs using files on it will lose them; files still uploading are finished first.', 'Disconnect');
      if (!ok) return;
    }
    driveBusy[id] = mount ? 'mount' : 'unmount';
    renderDrives(); renderCloud();
    try {
      const r = await api(mount ? '/api/drives/mount' : '/api/drives/unmount', { method: 'POST', params: { id }, timeout: 120000 });
      if (r.ok && r.data && r.data.success) toast(r.data.message);
      // A failed connect is usually the cloud asking the PC to slow down; the PC retries by itself and this list follows it.
      else toast(apiError(r, 'The PC could not do that') + (mount ? ' The PC keeps trying; watch the status here.' : ''));
    } catch (e) {
      toast('PC did not answer');
    }
    delete driveBusy[id];
    await pollStatus();
    await loadDrives();
    if (activeTab === 'ssd' && ssd.path) loadSsd(ssd.path, true);
  }

  // ------------------------------------------------------------------ RAM sheet
  function renderRam() {
    if (!isApp) {
      $('ram-rows').innerHTML = '';
      setText('ram-note', 'Phone memory can only be read by the Cloud-SSD Android app.');
      return;
    }
    let d;
    try { d = JSON.parse(native.getPhoneRamBreakdown()); } catch (e) { return; }
    const total = d.totalBytes || 0;
    setText('ram-val-used', fmtBytes(d.usedBytes));
    setText('ram-val-free', fmtBytes(d.availableBytes));
    setText('ram-val-total', fmtBytes(total));
    setText('ram-device-sub', ([appInfo.manufacturer, appInfo.model].filter(Boolean).join(' ') || 'This phone') + (d.lowMemory ? ' · low on memory' : ''));
    const rows = d.detailed ? [
      { label: 'Apps & services', sub: 'Memory apps are holding right now', bytes: d.appsBytes, color: '#60A5FA' },
      { label: 'Cached files', sub: 'Kept to reopen things fast; freed when needed', bytes: d.cachedBytes, color: '#34D399' },
      { label: 'System & hardware', sub: 'Android, graphics and drivers', bytes: d.otherBytes, color: '#C084FC' },
      { label: 'Kernel', sub: 'Core of the operating system', bytes: d.kernelBytes, color: '#FBBF24' },
      { label: 'Free', sub: 'Not used by anything', bytes: d.freeBytes, color: 'rgb(var(--ink) / 0.28)' }
    ] : [
      { label: 'In use', sub: 'Everything except what Android can free', bytes: d.usedBytes, color: '#60A5FA' },
      { label: 'Available', sub: 'Free plus what Android can free', bytes: d.availableBytes, color: 'rgb(var(--ink) / 0.28)' }
    ];
    $('ram-bar').innerHTML = rows.map(r => `<div class="ram-seg" style="width:${pct(r.bytes, total).toFixed(2)}%;background:${tint(r.color)}"></div>`).join('');
    let html = rows.map(r => `
      <div class="usage-row">
        <span class="usage-dot" style="background:${tint(r.color)}"></span>
        <div class="usage-main"><div class="usage-name">${r.label}</div><div class="usage-sub">${r.sub}</div></div>
        <div class="usage-val">${fmtBytes(r.bytes)}<small>${Math.round(pct(r.bytes, total))}%</small></div>
      </div>`).join('');
    if (d.swapTotalBytes > 0) {
      html += `<div class="usage-row">
        <span class="usage-dot" style="background:#F472B6"></span>
        <div class="usage-main"><div class="usage-name">Extended RAM (swap)</div><div class="usage-sub">Storage used as extra memory</div>
          <div class="usage-bar"><div style="width:${pct(d.swapUsedBytes, d.swapTotalBytes).toFixed(1)}%;background:#F472B6"></div></div></div>
        <div class="usage-val">${fmtBytes(d.swapUsedBytes)}<small>of ${fmtBytes(d.swapTotalBytes)}</small></div>
      </div>`;
    }
    for (const p of (d.processes || [])) {
      html += `<div class="usage-row">
        <span class="usage-dot" style="background:#A8C7FA"></span>
        <div class="usage-main"><div class="usage-name">${esc(p.name)}</div><div class="usage-sub">${esc(p.pkg)}</div></div>
        <div class="usage-val">${fmtBytes(p.bytes)}</div>
      </div>`;
    }
    $('ram-rows').innerHTML = html;
    setText('ram-note', 'Read from the phone every second. Android only lets an app see its own memory by name, so other apps are counted together under "Apps & services".');
  }

  // ------------------------------------------------------------------ gaming
  async function pollHud() {
    if (link.state !== 'online') {
      ['hud-cpu-val', 'hud-gpu-val', 'hud-ram-val', 'hud-ssd-read', 'hud-cache-hit', 'hud-ping-val'].forEach(id => setText(id, '—'));
      setText('gaming-hero-title', 'PC Gaming Companion');
      setText('gaming-hero-sub', link.state === 'unpaired' ? 'Pair with your PC to see live numbers' : 'PC is not connected');
      return;
    }
    const num = (v, suffix) => typeof v === 'number' ? Math.round(v) + suffix : '—';
    try {
      const t0 = performance.now();
      const r = await api('/api/hud', { timeout: 4000 });
      const rtt = Math.round(performance.now() - t0);
      if (r.status === 403) { setText('gaming-hero-sub', apiError(r)); return; }
      if (!r.ok || !r.data) return;
      const d = r.data;
      setText('hud-cpu-val', num(d.cpuLoad, '%'));
      setText('hud-cpu-label', typeof d.cpuTempC === 'number' ? `CPU load · ${Math.round(d.cpuTempC)}°C` : 'CPU load');
      setText('hud-gpu-val', num(d.gpuLoad, '%'));
      setText('hud-ram-val', num(d.ramUsedPercent, '%'));
      setText('hud-ssd-read', typeof d.ssdReadMBps === 'number' ? d.ssdReadMBps.toFixed(1) : '—');
      setText('hud-cache-hit', num(d.cacheHitRate, '%'));
      setText('hud-ping-val', rtt + ' ms');
      setText('gaming-hero-title', d.isGaming ? d.activeGame : 'No game running');
      setText('gaming-hero-sub', d.isGaming ? 'Running on ' + (link.pcName || 'the PC') + ' from the cloud drive' : 'Live numbers from ' + (link.pcName || 'the PC'));
    } catch (e) { /* the status poll reports the outage */ }
  }
  const REMOTE_LABELS = { volup: 'Volume up', voldown: 'Volume down', mute: 'Mute toggled', playpause: 'Play / pause', next: 'Next track', prev: 'Previous track' };
  async function remote(key) {
    if (link.state !== 'online') { toast('PC is not connected'); return; }
    try {
      const r = await api('/api/remote', { method: 'POST', params: { action: key }, timeout: 5000 });
      if (r.ok) toast(REMOTE_LABELS[key] + ' on ' + (link.pcName || 'PC'));
      else if (r.status === 404) toast('Update Cloud-SSD on the PC to use the remote');
      else toast(apiError(r));
    } catch (e) { toast('PC did not answer'); }
  }
  async function loadSaves() {
    const box = $('game-saves-list');
    if (!box) return;
    if (link.state !== 'online') { box.innerHTML = '<div class="recent-empty-card">PC is not connected.</div>'; return; }
    try {
      const r = await api('/api/saves', { timeout: 15000 });
      if (r.ok && Array.isArray(r.data) && r.data.length) {
        saves = r.data.slice(0, 50);
        box.innerHTML = saves.map((s, i) => `
          <div class="transfer-card-item ${s.gameId ? 'tappable' : ''}" ${s.gameId ? `data-act="save-open" data-i="${i}"` : ''}><div class="tc-left">
            <div class="tc-icon-wrap stream">${SVG.archive}</div>
            <div class="tc-info"><div class="tc-name">${esc(s.name)}</div><div class="tc-meta"><span>${esc(s.sizeFormatted || fmtBytes(s.size))}</span><span>${esc(fmtDate(s.modified))}</span>${s.kind === 'safety' ? '<span class="tc-dir-badge stream">Before a restore</span>' : ''}</div></div>
          </div></div>`).join('');
      } else if (r.ok) {
        box.innerHTML = '<div class="recent-empty-card">No save backups on the PC yet. Turn on cloud save sync for a game in Cloud-SSD.</div>';
      } else {
        box.innerHTML = `<div class="recent-empty-card">${esc(apiError(r))}</div>`;
      }
    } catch (e) {
      box.innerHTML = '<div class="recent-empty-card">PC did not answer.</div>';
    }
  }
  let saves = [];
  function openSave(s) {
    const list = [{
      icon: SVG.download, title: 'Save to phone', sub: 'A copy of this backup (.zip) in Downloads · ' + (s.sizeFormatted || fmtBytes(s.size)),
      fn: () => {
        const name = `${s.gameName || 'game'} saves ${String(s.file).replace(/^(save|safety_pre_restore)_/i, '')}`.replace(/[\\/:*?"<>|]/g, '_');
        if (isApp) { try { native.downloadFile(apiUrl('/api/saves/download', { gameId: s.gameId, file: s.file }), name); toast('Downloading ' + name); } catch (e) { toast('Download could not start'); } }
        else {
          const a = document.createElement('a');
          a.href = apiUrl('/api/saves/download', { gameId: s.gameId, file: s.file }); a.download = name;
          document.body.appendChild(a); a.click(); a.remove();
        }
      }
    }];
    if (pcHas('save-restore')) list.push({
      icon: SVG.pc, title: 'Restore on the PC', sub: 'Puts these saves back for ' + (s.gameName || 'the game') + '. The current saves are kept as a copy first.',
      fn: async () => {
        if (!await confirmDialog('Restore these saves?', `${s.gameName || 'The game'} goes back to the saves from ${new Date(s.modified).toLocaleString(dateLocale())}. The game must be closed on the PC. Today's saves are kept as a safety copy.`, 'Restore')) return;
        toast('Restoring on the PC…');
        try {
          const r = await api('/api/saves/restore', { method: 'POST', params: { gameId: s.gameId, file: s.file }, timeout: 300000 });
          toast(r.ok && r.data && r.data.success ? (r.data.message || 'Saves restored') : apiError(r, 'Could not restore'));
          loadSaves();
        } catch (e) { toast('PC did not answer'); }
      }
    });
    choiceSheet(s.name, list);
  }

  // ------------------------------------------------------------------ look (light / dark)
  /**
   * The app's look (looks.css): "ac" — A's calm page with C's curved blue band and one colour (the default); "saaf" —
   * light with colourful icons; "raat" — dark navy with teal; "auto" — A+C by day, Raat when the phone is dark.
   * The old Light / Dark choice carries over (Dark → Raat).
   */
  // Three more (newlooks.css): "kit" — pale page, a blue PC card, rounded-square tiles; "paper" — black on white with
  // thin lines; "glow" — black with lime and round gauges.
  const LOOKS = ['kit', 'paper', 'glow', 'ac', 'saaf', 'raat', 'auto'];
  // What the system bars show under each look: the colour behind the status bar (the top band), behind the
  // navigation bar (the bottom bar), and whether their icons are dark.
  const LOOK_BARS = {
    ac: { top: '#2563EB', rest: '#FFFFFF', statusDark: false, navDark: true },
    saaf: { top: '#F4F6FA', rest: '#FFFFFF', statusDark: true, navDark: true },
    raat: { top: '#0B0F17', rest: '#0B0F17', statusDark: false, navDark: false },
    kit: { top: '#F3F5F9', rest: '#FFFFFF', statusDark: true, navDark: true },
    paper: { top: '#FFFFFF', rest: '#FFFFFF', statusDark: true, navDark: true },
    glow: { top: '#0A0A0C', rest: '#0A0A0C', statusDark: false, navDark: false }
  };
  function lookMode() {
    const m = ls.get('cloudssd_look');
    if (LOOKS.includes(m)) return m;
    return ls.get('cloudssd_theme') === 'dark' ? 'raat' : 'ac';
  }
  // A+C has one colour: each part of a storage or RAM bar gets a shade of blue in place of its own colour (darker for
  // the first parts, so they still read apart).
  const AC_SHADES = {
    '#F48FB1': '#1E3A8A', '#EF9A9A': '#1D4ED8', '#FDD663': '#2563EB', '#78D9EC': '#3B82F6', '#6DD58C': '#60A5FA',
    '#B39DDB': '#93C5FD', '#8E918F': '#94A3B8', '#60A5FA': '#2563EB', '#34D399': '#60A5FA', '#C084FC': '#93C5FD', '#FBBF24': '#1E3A8A'
  };
  // Kit has one colour too: the same shades.
  function tint(color) { return ['ac', 'kit'].includes(document.documentElement.dataset.look) ? (AC_SHADES[String(color).toUpperCase()] || color) : color; }
  function phoneIsDark() {
    if (isApp && typeof appInfo.night === 'boolean') return appInfo.night;
    return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
  }
  /** "system" while the look follows the phone (it is applied again when the phone switches). */
  function themeMode() { return lookMode() === 'auto' ? 'system' : 'fixed'; }
  function applyTheme() {
    const mode = lookMode();
    const look = mode === 'auto' ? (phoneIsDark() ? 'raat' : 'ac') : mode;
    const root = document.documentElement;
    root.dataset.look = look;
    root.dataset.theme = look === 'raat' || look === 'glow' ? 'dark' : 'light';
    // Glass effect (glass.css): frosted bars and see-through cards, in whichever look is chosen.
    root.dataset.glass = ls.get('cloudssd_glass') === 'on' ? 'on' : 'off';
    const bars = LOOK_BARS[look];
    document.querySelectorAll('meta[name="theme-color"]').forEach(m => { m.content = bars.top; });
    if (isApp && typeof native.setBars === 'function') {
      try { native.setBars(bars.top, bars.rest, bars.statusDark, bars.navDark); } catch (e) { }
    }
  }

  // ------------------------------------------------------------------ settings sheet
  let settingsData = null;
  const PERMS = [
    ['driveBrowsing', 'Browse the cloud drives'], ['streaming', 'Play and download files'], ['uploads', 'Send files to the PC'],
    ['fileManagement', 'Rename, move and delete on the cloud'], ['gamingHud', 'Gaming numbers and media remote'],
    ['pcControl', 'Control the PC (drives, Space saver, game saves)'], ['pcDisks', 'See the PC\'s own disks'], ['space', 'See the PC\'s storage and RAM'], ['tailscale', 'Reach the PC away from home (Tailscale)']
  ];
  /** Settings, scrolled to Find my phone (from the "listening for your PC" note: switching it off is there). */
  function openFindMyPhone() {
    openSettings('pcmay');
    setTimeout(() => {
      const el = document.querySelector('[data-act="find-my-phone"]');
      if (el) el.closest('.settings-section').scrollIntoView({ block: 'center' });
    }, 700);
  }
  /*
   * The settings are shown in parts, each opened from the section of More it belongs to:
   *   app    — Settings: space saver, synced folders, app lock, Google Drive, about
   *   paired — Devices and connection → Phones paired with the PC
   *   away   — Devices and connection → Away from home (Tailscale)
   *   pcmay  — Permissions → More the PC may do (its numbers, removing things, ringing this phone)
   * "-" is not shown here at all: it has its own page (Look and language, Permissions).
   */
  const SETTINGS_GROUP = {
    'Customize': '-', 'What this phone may do': '-',
    'Paired phones': 'paired',
    'Away from home': 'away', 'Tailscale on this phone': 'away',
    'This phone on the PC': 'pcmay', 'Find my phone': 'pcmay'
  };
  const SETTINGS_TITLE = { app: 'Settings', paired: 'Phones paired with the PC', away: 'Away from home', pcmay: 'More the PC may do' };
  let settingsGroup = 'app';
  async function openSettings(group) {
    settingsGroup = typeof group === 'string' && SETTINGS_TITLE[group] ? group : 'app';
    closeSheet('connection-modal');
    refreshDrive();
    renderSettings();
    openSheet('settings-modal');
    if (link.state === 'online' && pcHas('settings')) {
      try {
        const r = await api('/api/settings', { timeout: 8000 });
        if (r.ok && r.data) { settingsData = r.data; if (isOpen('settings-modal')) renderSettings(); }
      } catch (e) { /* the parts known from the status stay */ }
    }
  }
  function settingsSection(title, body) { return `<div class="settings-section" data-group="${SETTINGS_GROUP[title] || 'app'}"><div class="sheet-subtitle">${esc(title)}</div>${body}</div>`; }
  function settingsRow(label, value, extra) {
    return `<div class="settings-row"><div class="settings-row-text"><div>${esc(label)}</div>${extra ? `<div class="settings-row-sub">${esc(extra)}</div>` : ''}</div><div class="settings-row-val">${value}</div></div>`;
  }
  function onOff(on) { return `<span class="settings-pill ${on ? 'on' : 'off'}">${on ? 'On' : 'Off'}</span>`; }
  /** The app's looks: [saved key, name, what it is like]. The keys (ac, saaf, raat) are what is saved; only the names people see changed. */
  const LOOK_CARDS = [
    ['kit', 'Kit', 'Blue PC card, soft square tiles'],
    ['paper', 'Paper', 'Black on white, thin lines'],
    ['glow', 'Glow', 'Black with lime, round gauges'],
    ['ac', 'Ocean', 'One calm blue, curved band'],
    ['saaf', 'Colourful', 'Light page, colourful icons'],
    ['raat', 'Midnight', 'Dark navy with teal'],
    ['auto', 'Like the phone', 'Ocean by day, Midnight at night']
  ];
  /** The icons the app can have on the phone's home screen: [key (MainActivity.APP_ICONS), name, its picture]. */
  const ICON_CARDS = [
    ['ocean', 'Ocean', 'icon-ocean.svg'],
    ['kit', 'Kit', 'icon-kit.svg'],
    ['paper', 'Paper', 'icon-paper.svg'],
    ['glow', 'Glow', 'icon-glow.svg'],
    ['midnight', 'Midnight', 'icon-midnight.svg'],
    ['sunset', 'Sunset', 'icon-sunset.svg']
  ];
  /** The key of the icon the app has now; '' in a browser and in an app from before the icons. */
  function appIconNow() {
    try { return isApp && typeof native.appIcon === 'function' ? String(native.appIcon() || '') : ''; } catch (e) { return ''; }
  }
  /** Settings → Customize: everything about how the app looks, on one page. */
  function renderCustomize() {
    let html = '';
    // The app's look: a card each with a small picture of its colours.
    const look = lookMode();
    html += settingsSection('App look', `<div class="look-picker">${LOOK_CARDS.map(([k, name, sub]) => `<button class="look-card ${look === k ? 'active' : ''}" data-act="look" data-look="${k}" aria-pressed="${look === k}">
        <span class="look-swatch look-swatch-${k}" aria-hidden="true"><i></i><i></i><i></i></span>
        <span class="look-name">${name}</span><span class="look-sub">${sub}</span></button>`).join('')}</div>`);
    // The app's icon on the home screen (the Android app only: a page in a browser has no icon to change).
    const icon = appIconNow();
    if (icon) {
      html += settingsSection('App icon', `<div class="icon-picker">${ICON_CARDS.map(([k, name, pic]) => `<button class="icon-card ${icon === k ? 'active' : ''}" data-act="app-icon" data-icon="${k}" aria-pressed="${icon === k}">
        <img src="${pic}" alt="" width="52" height="52"><span class="icon-name">${name}</span></button>`).join('')}</div>
        <div class="settings-row-sub icon-note">The app closes once when its icon changes. Open it again from the new icon.</div>`);
    }
    // Glass effect: one switch, for every look.
    html += settingsSection('Window', `<label class="settings-row glass-row"><div class="settings-row-text"><div>Glass effect</div><div class="settings-row-sub">Frosted glass: see-through bars and cards, like on an iPhone</div></div>
        <input type="checkbox" data-act="glass" ${ls.get('cloudssd_glass') === 'on' ? 'checked' : ''}></label>`);
    // The app's words: English, Hinglish or Hindi (i18n.js); the names stay as they are written.
    if (I18N) {
      html += settingsSection('Language', `<div class="transfer-chips-row settings-chips" data-no-i18n>${Object.keys(I18N.LANGS).map(k =>
        `<button class="transfer-chip ${I18N.lang() === k ? 'active' : ''}" data-act="lang" data-lang="${k}">${I18N.LANGS[k]}</button>`).join('')}</div>`);
    }
    $('customize-body').innerHTML = html;
  }
  function openCustomize() { renderCustomize(); openSheet('customize-modal'); }
  function renderSettings() {
    const s = settingsData && link.state === 'online' ? settingsData : null;
    const st = link.status || {};
    const online = link.state === 'online';
    setText('settings-sub', online ? (link.pcName || 'PC') + (link.route === 'tailnet' ? ' · via Tailscale' : ' · home Wi-Fi') : 'This phone');
    let html = '';

    // How the app looks (look and language) is on its own page: one row here says what is chosen and opens it.
    // "Like the phone" also says which look it is right now (Midnight at night, Ocean by day).
    const lookName = (LOOK_CARDS.find(l => l[0] === lookMode()) || LOOK_CARDS[0])[1] + (lookMode() === 'auto' ? ' · ' + (phoneIsDark() ? 'Midnight' : 'Ocean') : '');
    html += settingsSection('Customize', `<div class="settings-row" data-act="open-customize" role="button" tabindex="0">
        <div class="settings-row-text"><div>Look and language</div><div class="settings-row-sub">${esc(lookName)}${I18N ? ' · ' + esc(I18N.LANGS[I18N.lang()] || '') : ''}</div></div>
        <div class="settings-row-val" aria-hidden="true">›</div></div>`);

    if (online) {
      const p = (s && s.permissions) || st.permissions;
      if (p) {
        html += settingsSection('What this phone may do', PERMS.filter(([k]) => k in p).map(([k, l]) => settingsRow(l, onOff(!!p[k]))).join('') +
          '<div class="sheet-note">Change these in Cloud-SSD on the PC → Mobile.</div>');
      }
      if (s && Array.isArray(s.devices)) {
        html += settingsSection('Paired phones', s.devices.map(d => settingsRow(d.name || 'Phone',
          d.isThisPhone ? '<span class="settings-pill on">This phone</span>' : d.isBlocked ? '<span class="settings-pill off">Blocked</span>' : '',
          (d.lastSeen ? 'Last seen ' + timeAgo(d.lastSeen) : 'Paired ' + fmtDate(d.pairedAt)) + (d.legacy ? ' · old shared key' : ''))).join('') +
          `<button class="m3-tonal-btn danger-text" data-act="unpair">Remove this phone from ${esc(link.pcName || 'the PC')}</button>`);
      }
      const off = s && s.offload;
      if (off) {
        const enabled = off.enabled != null ? off.enabled : off.Enabled;
        const threshold = off.minFreeSpaceThresholdGB != null ? off.minFreeSpaceThresholdGB : off.MinFreeSpaceThresholdGB;
        const targetName = off.targetDriveName || off.TargetDriveName || '';
        const total = off.laptopTotalSpaceGB || off.LaptopTotalSpaceGB || 0;
        const canEdit = !!(s.permissions && s.permissions.pcControl);
        html += settingsSection('Space saver on the PC', canEdit ? `
          <label class="settings-row"><div class="settings-row-text"><div>Move old files to the cloud when the PC is nearly full</div></div><input type="checkbox" id="set-offload-on" ${enabled ? 'checked' : ''}></label>
          <label class="settings-row"><div class="settings-row-text"><div>Start below (GB free)</div>${total ? `<div class="settings-row-sub">The PC disk has ${Math.round(total)} GB</div>` : ''}</div>
            <input type="number" class="modal-input settings-num" id="set-offload-gb" min="1" max="${Math.max(1, Math.floor(total || 9999))}" step="1" value="${threshold != null ? threshold : ''}"></label>
          ${(s.offloadTargets || []).length ? `<label class="settings-row"><div class="settings-row-text"><div>Move to</div></div>
            <select class="modal-input settings-select" id="set-offload-target">${s.offloadTargets.map(t => `<option value="${esc(t.id)}" ${t.name === targetName ? 'selected' : ''}>${esc(t.name)}</option>`).join('')}</select></label>` : ''}
          <button class="m3-filled-btn" data-act="settings-offload-save">Save</button>`
          : settingsRow('Moves old files to the cloud', onOff(!!enabled), threshold != null ? 'Starts below ' + threshold + ' GB free · ' + targetName : targetName) +
            '<div class="sheet-note">Turn on "Control the PC" on the PC to change this from the phone.</div>');
      }
      const away = (s && s.away) || st.away;
      if (away) {
        html += settingsSection('Away from home', away.allowed
          ? settingsRow('Tailscale', '<span class="settings-pill on">Allowed</span>', (away.dnsName || (away.ips || [])[0] || 'PC is not signed in to Tailscale') +
            (link.route === 'tailnet' ? ' · in use now' : '')) + '<div class="sheet-note">Install Tailscale on this phone and sign in with the same account as the PC. The app switches to it by itself when the home Wi-Fi is not there.</div>'
          : settingsRow('Tailscale', '<span class="settings-pill off">Off</span>', 'On the PC: Cloud-SSD → Mobile → tick "Let paired phones reach this PC through Tailscale"'));
      }
    } else {
      html += settingsSection(link.pcName || 'PC', `<div class="sheet-note">${esc(link.state === 'unpaired' ? 'Not paired with a PC yet.' : (link.pcName || 'The PC') + ' is not reachable right now; its settings show here when it is.')}</div>`);
    }

    if (isApp) {
      // Tailscale on this phone: what it needs to reach the PC away from home.
      const ts = appInfo.tailscale || {};
      html += settingsSection('Tailscale on this phone', settingsRow('Tailscale',
        `<button class="drive-btn browse" data-act="tailscale-open">${ts.installed ? 'Open' : 'Install'}</button>`,
        !ts.installed ? 'Install it and sign in with the same account as the PC.' : ts.vpn ? 'Connected (VPN on).' : 'Installed. Turn it on when you are away from home.'));
      // What the PC may see and do with this phone (Space & RAM on the PC).
      let share = {};
      try { share = JSON.parse(native.getSpaceSharing()); } catch (e) { }
      if ('share' in share) {
        html += settingsSection('This phone on the PC',
          `<label class="settings-row"><div class="settings-row-text"><div>Show this phone's storage and RAM on the PC</div><div class="settings-row-sub">Sent while this app is open; when off, the PC keeps only the last numbers</div></div>
            <input type="checkbox" data-act="space-share" data-key="share" ${share.share ? 'checked' : ''}></label>
          <label class="settings-row"><div class="settings-row-text"><div>Let the PC remove things on this phone</div><div class="settings-row-sub">Delete, move to the cloud, uninstall — Android still asks on the phone where it must</div></div>
            <input type="checkbox" data-act="space-share" data-key="control" ${share.control ? 'checked' : ''}></label>` +
          (share.usageAccess ? '' : `<button class="m3-tonal-btn" data-act="space-usage">Allow usage access (app sizes)</button>`));
      }
      // Find my phone: the PC can ring this phone (and send its clipboard) with the app closed; a quiet note shows.
      let fmp = null;
      try { fmp = typeof native.findMyPhone === 'function' ? JSON.parse(native.findMyPhone()) : null; } catch (e) { }
      if (fmp) {
        html += settingsSection('Find my phone',
          `<label class="settings-row"><div class="settings-row-text"><div>Let the PC ring this phone even when the app is closed</div><div class="settings-row-sub">${fmp.on
            ? (fmp.listening ? 'Listening now — a quiet note shows while it does' : 'On — starts listening the next time it can') + (fmp.batteryOk ? '' : '. Android may still stop it to save battery')
            : 'Also its clipboard. Off: the PC reaches this phone only while the app is open'}</div></div>
            <input type="checkbox" data-act="find-my-phone" ${fmp.on ? 'checked' : ''}></label>` +
          (fmp.on && !fmp.batteryOk ? `<button class="m3-tonal-btn" data-act="battery-exempt">Let it run in the background (battery)</button>` : '') +
          // Android 14+: the ring's big "Found it" screen over the lock screen needs the person's yes once.
          (fmp.lockScreen === false ? `<div class="sheet-note">On a locked phone the ring shows only as a note until full-screen notes are allowed.</div><button class="m3-tonal-btn" data-act="ring-lockscreen">Show the ring on the lock screen</button>` : ''));
      }
      // Synced folders: each pair with its last result.
      const syncs = folderSyncs();
      if (syncs.length) {
        html += settingsSection('Synced folders', syncs.map(p => `
          <div class="settings-row"><div class="settings-row-text"><div data-no-i18n>${esc(p.phoneLabel)} ↔ ${esc(p.pcLabel)}</div><div class="settings-row-sub">${esc(syncStatus(p))}</div></div></div>
          <div class="transfer-chips-row settings-chips"><button class="transfer-chip" data-act="sync-now" data-id="${esc(p.id)}">Sync now</button><button class="transfer-chip" data-act="sync-stop" data-id="${esc(p.id)}">Stop syncing</button></div>`).join('') +
          '<div class="sheet-note">Deleting a file on one side does not delete it on the other. A folder syncs only while its PC is the one in use.</div>');
      }
      // App lock: the phone's fingerprint or screen lock when the app opens, and when it comes back after a while.
      let lock = null;
      try { lock = typeof native.appLock === 'function' ? JSON.parse(native.appLock()) : null; } catch (e) { }
      if (lock) {
        const afters = [[0, 'Right away'], [60, 'After 1 minute'], [300, 'After 5 minutes']];
        html += settingsSection('App lock',
          `<label class="settings-row"><div class="settings-row-text"><div>Ask for the fingerprint or screen lock to open the app</div><div class="settings-row-sub">${!lock.available
            ? 'Set a screen lock (PIN, pattern or password) on this phone first'
            : lock.on ? 'On — when it opens, and when it comes back' : 'Off: anyone holding the unlocked phone can open the app'}</div></div>
            <input type="checkbox" data-act="app-lock" ${lock.on ? 'checked' : ''} ${lock.available ? '' : 'disabled'}></label>` +
          (lock.on ? `<div class="transfer-chips-row settings-chips">${afters.map(([sec, label]) =>
            `<button class="transfer-chip ${lock.after === sec ? 'active' : ''}" data-act="app-lock-after" data-after="${sec}">${label}</button>`).join('')}</div>` : ''));
      }
    }

    if (isApp && drive.available) {
      const accs = drive.accounts || [];
      html += settingsSection('Google Drive on this phone', (accs.length ? accs.map(a => settingsRow(a.email,
        `<button class="drive-btn unmount" data-act="drive-remove" data-email="${esc(a.email)}">Remove</button>`,
        a.needsReauth ? 'Sign in again' : a.totalBytes > 0 ? fmtBytes(a.freeBytes) + ' free of ' + fmtBytes(a.totalBytes) : 'Signed in')).join('')
        : '<div class="sheet-note">Sign in with the same Google accounts as on the PC to open your files when the PC is off.</div>') +
        `<label class="settings-row"><div class="settings-row-text"><div>Use Google Drive directly when the PC is off</div><div class="settings-row-sub">Very new changes made on the PC may take a moment to appear</div></div>
          <input type="checkbox" data-act="drive-direct" ${drive.direct ? 'checked' : ''}></label>
        <button class="m3-tonal-btn" data-act="drive-add">${accs.length ? 'Add another Google account' : 'Sign in to Google'}</button>`);
    }

    html += settingsSection('About', settingsRow('Phone app', esc(isApp ? (appInfo.version || '—') : 'Browser')) +
      settingsRow('Cloud-SSD on the PC', esc(st.version || (s && s.version) || '—')) +
      (s ? settingsRow('This phone\'s key', esc(s.auth === 'legacy' ? 'Old shared key' : 'Its own key')) : ''));
    // Only the part that was asked for; a part with nothing in it (the PC is away) says so.
    setText('settings-title', SETTINGS_TITLE[settingsGroup]);
    const body = $('settings-body');
    body.innerHTML = html + '<div class="perm-empty settings-none" hidden>Nothing to show here while the PC is not reachable.</div>';
    let shown = 0;
    body.querySelectorAll('.settings-section').forEach(el => { el.hidden = el.dataset.group !== settingsGroup; if (!el.hidden) shown++; });
    body.querySelector('.settings-none').hidden = shown > 0;
  }
  async function saveOffload() {
    const params = { enabled: $('set-offload-on').checked ? 'true' : 'false' };
    const gb = $('set-offload-gb').value.trim();
    if (gb) params.thresholdGb = gb;
    const t = $('set-offload-target');
    if (t && t.value) params.targetMountId = t.value;
    try {
      const r = await api('/api/storage/offload-config', { method: 'POST', params, timeout: 15000 });
      toast(r.ok ? 'Space saver updated on the PC' : apiError(r));
      if (r.ok) { pollStatus(); openSettings(settingsGroup); }
    } catch (e) { toast('PC did not answer'); }
  }

  // ------------------------------------------------------------------ search
  const search = { q: '', timer: null, seq: 0, phoneReq: -1, cloud: [], phone: [], scope: ls.get('cloudssd_search_scope') === 'all' ? 'all' : 'folder' };
  function onSearchInput(value) {
    const q = value.trim();
    show('clear-search-btn', value.length > 0);
    clearTimeout(search.timer);
    if (q.length < 2) { closeSearch(false); return; }
    search.timer = setTimeout(() => runSearch(q), 350);
  }
  function closeSearch(clearInput) {
    search.q = '';
    search.seq++;
    $('search-panel').classList.remove('active');
    document.body.classList.remove('searching');
    if (clearInput) { $('global-search-input').value = ''; show('clear-search-btn', false); $('global-search-input').blur(); }
  }
  async function runSearch(q) {
    search.q = q;
    const seq = ++search.seq;
    $('search-panel').classList.add('active');
    document.body.classList.add('searching');
    const cloudBox = $('search-cloud-results'), phoneBox = $('search-phone-results');
    show('search-phone-section', isApp);
    if (isApp) {
      phoneBox.innerHTML = skeleton(3);
      search.phoneReq = 1000000 + seq;
      try { native.searchPhone(search.phoneReq, q); } catch (e) { phoneBox.innerHTML = ''; }
    }
    const source = cloudSource();
    const scope = search.scope;
    document.querySelectorAll('#search-scope-row .transfer-chip').forEach(c => c.classList.toggle('active', c.dataset.scope === scope));
    if (source === 'none') {
      // The PC is away: search what was saved on this phone from every paired PC.
      cloudBox.innerHTML = skeleton(3);
      setText('search-cloud-where', 'in lists saved on this phone');
      const saved = await searchSaved(q, false);
      if (seq !== search.seq) return;
      search.cloud = saved;
      setText('search-cloud-count', String(saved.length));
      cloudBox.innerHTML = saved.length ? saved.map(searchRow).join('') + '<div class="list-note">From the lists saved last time — the PC is not connected now.</div>'
        : '<div class="recent-empty-card">PC is not connected, and nothing named like that is in the lists saved on this phone.</div>';
      return;
    }
    // The PC's folder for files sent to it ("From phone") is searched too: with "All", and when no cloud drive is there.
    const places = ssdPlaces();
    const fromPhone = places.find(p => p.label === 'From phone');
    const anyCloud = places.some(p => p.cloud && p.mounted);
    const pl = placeOf(ssd.path) || places.find(p => p.cloud && p.mounted) || fromPhone;
    if (!pl) { cloudBox.innerHTML = '<div class="recent-empty-card">No cloud drive is connected.</div>'; return; }
    const where = ssd.path && placeOf(ssd.path) ? ssd.path : pl.root;
    const all = scope === 'all';
    setText('search-cloud-where', all ? (anyCloud ? 'in all cloud drives' + (fromPhone ? ' and From phone' : '') : 'in From phone')
      : 'in ' + friendlyPath(where));
    cloudBox.innerHTML = skeleton(4);
    let found = [], truncated = false, error = '';
    try {
      if (source === 'drive') {
        const r = await cloudOp('search', { path: all ? (ssdPlaces()[0] || pl).root : where, q, max: 150 });
        if (r.ok && r.data) {
          found = (r.data.entries || []).map(e => ({ name: e.name, path: e.id, size: e.size, modified: e.modified, isFolder: e.isDirectory, src: 'cloud' }));
        } else error = r.error || 'Google Drive did not answer';
      } else {
        // "All": every cloud drive (one request) plus From phone; else the folder open now.
        const firstCloud = places.find(p => p.cloud && p.mounted);
        const targets = !all ? [{ path: where }]
          : (firstCloud ? [{ path: firstCloud.root, scope: 'all' }] : []).concat(fromPhone ? [{ path: fromPhone.root }] : []);
        for (const t of targets) {
          const params = { path: t.path, search: q, folders: 1, depth: 6 };
          if (t.scope) params.scope = t.scope;
          const r = await api('/api/files', { params, timeout: 45000 });
          if (seq !== search.seq) return;
          if (r.ok && r.data) {
            found = found.concat((r.data.folders || []).map(fo => ({ name: fo.name, path: fo.path, modified: fo.modified, isFolder: true, src: 'cloud' })))
              .concat((r.data.files || []).map(f => Object.assign(f, { src: 'cloud' })));
            truncated = truncated || !!r.data.truncated;
          } else if (!found.length) error = apiError(r);
        }
        if (found.length) error = '';
      }
    } catch (e) { error = 'PC did not answer.'; }
    if (seq !== search.seq) return;
    // The other paired PCs (not in use now): what was saved from them matches too.
    const others = source === 'pc' ? await searchSaved(q, true) : [];
    if (seq !== search.seq) return;
    search.cloud = found.concat(others);
    setText('search-cloud-count', String(search.cloud.length));
    cloudBox.innerHTML = search.cloud.length ? search.cloud.map(searchRow).join('') + (truncated ? '<div class="list-note">More matches exist; open a folder to search inside it.</div>' : '')
      : `<div class="recent-empty-card">${error ? esc(error) : all ? 'Nothing named like that on ' + (link.pcName || 'the PC') + '.' : 'Nothing named like that in this folder. Try "All cloud drives".'}</div>`;
  }
  function searchParent(p) { return p.slice(0, Math.max(p.lastIndexOf(sepOf(p)), isCloudId(p) ? p.indexOf(':') + 1 : 0)); }
  /** "From phone › Trip" for a folder inside one of the PC's places (not its Windows path); the path as it is otherwise. */
  function friendlyPath(dir) { return DATA.friendlyPath(dir, ssdPlaces()); }
  /** Where a search result is: its folder, named as above. */
  function searchWhere(p) { return friendlyPath(searchParent(p)); }
  /** One search result; results from a saved list say which PC they are on. */
  function searchRow(f, i) {
    const where = f.saved ? `${f.deviceName} · saved · ${searchWhere(f.path)}` : searchWhere(f.path);
    return `<div class="explorer-item ${f.isFolder ? 'folder-item' : 'file-item'}" data-act="search-cloud" data-i="${i}">
        <div class="item-icon ${f.isFolder ? 'folder-icon' : 'kind-' + kindOf(f.name)}">${f.isFolder ? SVG.folder : iconFor(f.name)}</div>
        <div class="item-details"><div class="item-name">${esc(f.name)}</div><div class="item-meta">${f.isFolder ? 'Folder' : f.size > 0 ? fmtBytes(f.size) : '—'} · ${esc(where)}</div></div>
      </div>`;
  }
  /** Matches in the folder lists saved on this phone, from every paired PC (or all but the one in use). */
  async function searchSaved(q, otherPcsOnly) {
    if (!lists) return [];
    const out = [];
    for (const d of devicesList()) {
      if (otherPcsOnly && d.active) continue;
      try {
        const hits = await lists.search(d.id, q, 60);
        hits.forEach(h => out.push(Object.assign({}, h, { src: 'cloud', pcId: d.id, deviceName: deviceLabel(d), saved: true })));
      } catch (e) { }
    }
    return out;
  }
  function renderSearchPhone(d) {
    search.phone = (d.files || []).map(f => Object.assign(f, { src: 'phone' }));
    setText('search-phone-count', String(search.phone.length));
    $('search-phone-results').innerHTML = search.phone.length ? search.phone.map((f, i) => `
      <div class="phone-item-row" data-act="search-phone" data-i="${i}">
        <div class="phone-item-icon">${iconFor(f.name)}</div>
        <div class="phone-item-info"><div class="phone-item-name">${esc(f.name)}</div><div class="phone-item-meta">${fmtBytes(f.size)} · ${esc(prettyPhonePath(f.path.slice(0, f.path.lastIndexOf('/'))))}</div></div>
      </div>`).join('') : '<div class="recent-empty-card">No file named like that on this phone.</div>';
  }

  // ------------------------------------------------------------------ tabs
  /** {@code keepLevel}: opening a folder from elsewhere (drives sheet, search) goes straight into the device. */
  function switchTab(tab, keepLevel) {
    closeSearch(true);
    // The Cloud SSD tab starts with the list of devices when it is chosen in the bottom bar.
    if (tab === 'ssd' && !keepLevel) ssd.level = 'devices';
    activeTab = tab;
    document.querySelectorAll('.tab-screen').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.nav-tab').forEach(el => el.classList.remove('active'));
    $('tab-' + tab).classList.add('active');
    $('nav-' + tab).classList.add('active');
    document.body.dataset.tab = tab;
    window.scrollTo(0, 0);
    if (tab === 'home') { renderPhoneTiles(); refreshHistory(); }
    else if (tab === 'browser') openPhoneTab();
    else if (tab === 'ssd') openSsdTab();
    else if (tab === 'gaming') { pollHud(); loadSaves(); }
    else if (tab === 'more') renderMore();
    renderTransfers();
  }
  function openPhoneTab() {
    show('phone-webonly-note', !isApp);
    show('phone-home', isApp && !phone.view);
    if (!isApp) return;
    try { phone.access = native.getStorageAccess(); } catch (e) { }
    renderPermBanner();
    renderStorage();
    requestScan();
  }

  // ------------------------------------------------------------------ actions (one click handler for the whole page)
  const actions = {
    'tab': (el) => switchTab(el.dataset.tab),
    'search-clear': () => closeSearch(true),
    'open-connection': () => { renderLink(); openSheet('connection-modal'); },

    // ---- devices (Cloud SSD tab) and the profile menu ----
    'device-open': (el) => openDevice(el.dataset.id),
    'device-more': (el) => deviceMenu(el.dataset.id || currentDeviceId()),
    'device-wake': (el) => wakeDevice(el.dataset.id),
    'device-add': () => { closeSheet('profile-menu'); pair.purpose = 'add'; showPair(''); },
    'ssd-devices': () => showDevices(),
    // The round picture at the top right, and "More" in the bottom bar: the More tab.
    'open-profile': () => switchTab('more'),
    'more-open': (el) => {
      const sec = moreSections()[+el.dataset.i];
      if (!sec) return;
      if (sec.rows) { moreOpen = sec.key; renderMoreSheet(); openSheet('more-sheet'); } else sec.fn();
    },
    'more-item': (el) => {
      const sec = moreSections().find(x => x.key === moreOpen);
      const it = sec && sec.rows()[+el.dataset.i];
      if (it && it.fn) it.fn();
    },
    'perm-android': (el) => {
      const k = el.dataset.k;
      try {
        if (k === 'files') actions['grant-storage']();
        else if (k === 'background') native.askBatteryExemption();
        else if (k === 'settings') native.openAppSettings();
        else native.requestAllPermissions();
      } catch (e) { }
    },
    'ssd-kind': (el) => loadSsdKind(el.dataset.kind || ''),
    'open-settings-group': (el) => openSettings(el.dataset.group),
    'profile-edit': () => editMe(),
    'profile-item': (el) => { const it = profileItems()[+el.dataset.i]; closeSheet('profile-menu'); if (it && it.fn) it.fn(); },
    'access-ask': () => { if (access.ask) access.ask(); },
    'access-no': () => { if (access.cancel) access.cancel(); else closeAccessSheet(); },
    'perm-ask': async (el) => { closeSheet('perm-sheet'); if (await askAccess([el.dataset.c])) openPermissions(); },
    'open-tethering': () => { try { native.openTetherSettings(); } catch (e) { toast('Open Settings → Hotspot & tethering → USB tethering'); } },
    'offline-remove': (el) => { const p = kept.pins[+el.dataset.i]; if (p) unkeep(p); },
    // A kept folder opens in the Cloud SSD tab (a kept file: the folder it is in), with or without the PC.
    'offline-open': (el) => {
      const p = kept.pins[+el.dataset.i];
      let path = p ? pathOfKeepId(p.id) : '';
      if (path && !p.dir) path = path.slice(0, path.lastIndexOf('\\')) || path;
      if (/^[A-Za-z]:$/.test(path)) path += '\\';
      if (!path) { toast('Open it from the Cloud SSD tab'); return; }
      closeSheet('offline-sheet');
      ssd.level = 'device';
      ssd.path = path;
      if (activeTab !== 'ssd') switchTab('ssd', true); else openSsdTab();
    },
    'peer-scope': (el) => { peer.scope = el.dataset.s; renderPeerScope(); },
    'peer-write': (el) => { peer.write = !!el.checked; },
    'peer-deny': () => peerAnswer(-1),
    'peer-hour': () => peerAnswer(60),
    'peer-always': () => peerAnswer(0),
    'whenoff-set': (el) => setWhenOff(el.dataset.on === '1'),
    'main-keep': () => openMainKeep(),
    'peer-stop': () => { try { native.peerRevoke(currentDeviceId()); } catch (e) { } toast((link.pcName || 'The PC') + ' can no longer see this phone'); openPermissions(); },
    'peer-open': () => { closeSheet('perm-sheet'); showPeerAsk({ pc: currentDeviceId(), name: link.pcName }); },
    'inbox-accept': (el) => { const o = inbox.offers[+el.dataset.i]; if (o) acceptOffer(o); },
    'inbox-decline': (el) => { const o = inbox.offers[+el.dataset.i]; if (o) declineOffer(o); },
    'offline-sync': () => { try { native.offlineSync(); } catch (e) { } toast(link.state === 'online' ? 'Updating kept folders…' : 'Kept folders update when ' + (link.pcName || 'the PC') + ' is back'); },
    'offline-mobile': () => { try { native.offlineMobileData(!kept.mobileData); } catch (e) { } refreshKept(); renderOfflineSheet(); },
    'toggle-link': () => {
      if (link.state === 'online') { setLinkState('paused'); renderStatusTiles(); toast('Disconnected from ' + (link.pcName || 'PC')); }
      else if (!link.token || !link.server) { closeSheet('connection-modal'); showPair(''); }
      else { link.state = 'init'; renderLink(); toast('Connecting…'); pollStatus(); }
    },
    'open-pair': () => showPair(''),
    'save-address': () => {
      const v = normalizeServer($('manual-server-ip').value);
      if (!v) return;
      saveCreds(v, link.token);
      link.state = 'init';
      renderLink();
      toast('Trying ' + v.replace(/^https?:\/\//, ''));
      if (link.token) pollStatus(); else { closeSheet('connection-modal'); $('pair-address').value = v.replace(/^https?:\/\//, ''); showPair(''); }
    },
    'find-pc': () => { if (isApp) { toast('Searching this Wi-Fi…'); link.state = 'offline'; startDiscovery('relocate'); } else toast('Only in the Android app'); },
    'unpair': async () => {
      if (!await confirmDialog('Unpair this phone?', 'The PC forgets this phone. You will need the pairing code from the PC to connect again.', 'Unpair')) return;
      closeSheet('connection-modal');
      closeSheet('settings-modal');
      // Tell the PC first, so the key stops working there too (an older PC just forgets it on its own list).
      if (link.state === 'online' && pcHas('device-tokens')) {
        try { await api('/api/devices/remove', { method: 'POST', params: { id: 'self' }, timeout: 5000 }); } catch (e) { }
      }
      onUnpaired();
    },
    'open-settings': () => openSettings(),
    'open-customize': () => openCustomize(),
    'lang': (el) => {
      if (!I18N) return;
      I18N.setLang(el.dataset.lang);
      // The phone's own notes (ring, requests) follow the same choice.
      if (isApp && typeof native.setLanguage === 'function') { try { native.setLanguage(el.dataset.lang); } catch (e) { } }
      renderSettings();
      if (isOpen('customize-modal')) renderCustomize();
    },
    'glass': (el) => { ls.set('cloudssd_glass', el.checked ? 'on' : 'off'); applyTheme(); },
    'app-icon': async (el) => {
      if (el.classList.contains('active')) return;
      // Android closes the app's screen when its icon is changed (the old icon is what opened it): said first.
      if (!await confirmDialog('Change the app icon?', 'The app closes once. Open it again from its new icon on the home screen.', 'Change')) return;
      let ok = false;
      try { ok = !!native.setAppIcon(el.dataset.icon); } catch (e) { }
      toast(ok ? 'App icon changed' : 'Could not change it');
      if (isOpen('customize-modal')) renderCustomize();
    },
    'look': (el) => {
      ls.set('cloudssd_look', el.dataset.look);
      ls.del('cloudssd_theme');
      applyTheme();
      renderSettings();
      if (isOpen('customize-modal')) renderCustomize();
    },
    'settings-offload-save': () => saveOffload(),
    'drive-add': () => { try { native.driveAddAccount(); toast('Opening Google sign-in…'); } catch (e) { toast('Update the Cloud-SSD app'); } },
    'drive-remove': async (el) => {
      if (!await confirmDialog('Remove ' + el.dataset.email + '?', 'This phone signs out of that Google account. Nothing is deleted from Google Drive.', 'Remove')) return;
      try { native.driveRemoveAccount(el.dataset.email); } catch (e) { }
    },
    'drive-direct': (el) => {
      try { native.setDriveDirect(!!el.checked); } catch (e) { }
      drive.direct = !!el.checked;
      if (activeTab === 'ssd') openSsdTab();
    },
    'details-copy': () => {
      const f = detailsFile;
      if (!f) return;
      const text = f.path;
      if (isApp) { try { native.copyText(text); toast('Location copied'); return; } catch (e) { } }
      if (navigator.clipboard) navigator.clipboard.writeText(text).then(() => toast('Location copied'), () => toast(text));
      else toast(text);
    },
    'details-size': async (el) => {
      const f = detailsFile;
      if (!f) return;
      el.disabled = true;
      el.textContent = 'Counting…';
      if (cloudSource() === 'pc') {
        const id = isCloudId(f.path) ? f.path : (window.CloudSsdFs && window.CloudSsdFs.idOf(f.path));
        try {
          const r = await api('/api/fs/usage', { params: { path: id }, timeout: 60000 });
          el.textContent = r.ok && r.data ? `${r.data.bytesFormatted || fmtBytes(r.data.bytes)} · ${r.data.files} files${r.data.truncated ? ' (still counting stopped early)' : ''}` : apiError(r);
        } catch (e) { el.textContent = 'PC did not answer'; }
      } else el.textContent = 'Only with the PC';
    },
    'choice-pick': (el, e) => {
      // A touch left over from the screen just closed (the folder picker's "Allow") can land here as the sheet opens:
      // a touch in its first second is not taken (nobody reads and taps that fast). Clicks made by the page are.
      if (e && e.isTrusted && Date.now() - choicesAt < 1000) return;
      const a = choices[+el.dataset.i]; closeSheet('choice-sheet'); if (a) a.fn();
    },
    'save-open': (el) => { const s = saves[+el.dataset.i]; if (s) openSave(s); },
    'transfers-clear': async () => {
      if (!await confirmDialog('Clear the transfer history?', 'The list of finished transfers on ' + (link.pcName || 'the PC') + ' is emptied. Files are not touched.', 'Clear')) return;
      try {
        const r = await api('/api/transfers/clear', { method: 'POST' });
        if (r.ok) { xferHistory = []; renderHistory(); toast('History cleared'); } else toast(apiError(r));
      } catch (e) { toast('PC did not answer'); }
      // Failed transfers on this phone go too.
      Array.from(transfers.values()).filter(t => t.state === 'failed').forEach(t => cancelTransfer(t.id));
    },
    'xfer-retry': (el) => retryTransfer(+el.dataset.id),
    'search-scope': (el) => {
      search.scope = el.dataset.scope === 'all' ? 'all' : 'folder';
      ls.set('cloudssd_search_scope', search.scope);
      if (search.q) runSearch(search.q);
      else document.querySelectorAll('#search-scope-row .transfer-chip').forEach(c => c.classList.toggle('active', c === el));
    },
    'gallery-prev': () => galleryStep(-1),
    'gallery-next': () => galleryStep(1),
    'ssd-select': () => { if (ssd.selecting) endSsdSelect(); else { startSsdSelect(); toast('Tap files and folders to select them'); } },
    'ssd-select-all': () => {
      const all = ssd.view.folders.map((_, i) => 'd' + i).concat(ssd.view.files.map((_, i) => 'f' + i));
      const every = all.every(k => ssd.selected.has(k));
      ssd.selected = new Set(every ? [] : all);
      document.querySelectorAll('#ssd-items-container [data-key]').forEach(el => el.classList.toggle('selected', ssd.selected.has(el.dataset.key)));
      renderSsdSelectBar();
    },
    'ssd-select-cancel': () => endSsdSelect(),
    'ssd-bulk': (el) => ssdBulk(el.dataset.what),
    'pair-pick': (el) => { pair.selected = el.dataset.url; $('pair-address').value = ''; renderPairPcs(); },
    'pair-rescan': () => {
      const typed = normalizeServer($('pair-address').value);
      if (typed) { pair.selected = ''; submitPair(); } else if (isApp) startDiscovery('pair');
    },
    'pair-submit': () => submitPair(),
    'pair-close': () => hidePair(),

    // Home tiles open Space & RAM (space.js): this phone's storage or RAM, with the PC one tap away.
    'open-ram': () => {
      if (window.CloudSsdSpace) { window.CloudSsdSpace.open(isApp ? 'phone' : 'pc', 'ram'); return; }
      renderRam(); openSheet('ram-breakdown-modal');
    },
    'open-storage': () => {
      if (window.CloudSsdSpace) { window.CloudSsdSpace.open(isApp ? 'phone' : 'pc', 'overview'); return; }
      if (!isApp) { toast('Phone storage is shown in the Android app'); return; }
      renderStorage(); requestScan(); openSheet('storage-modal');
    },
    'open-storage-sheet': () => { if (isApp) { renderStorage(); requestScan(); openSheet('storage-modal'); } },
    'space-share': (el) => {
      const o = {};
      o[el.dataset.key] = !!el.checked;
      try { native.setSpaceSharing(JSON.stringify(o)); } catch (e) { toast('Update the Cloud-SSD app'); }
    },
    'tailscale-open': () => { try { native.openTailscale(); } catch (e) { toast('Update the Cloud-SSD app'); } },
    'find-my-phone': (el) => {
      try { native.setFindMyPhone(!!el.checked); } catch (e) { toast('Update the Cloud-SSD app'); return; }
      toast(el.checked ? 'The PC can ring this phone even with the app closed' : 'The PC reaches this phone only while the app is open');
      setTimeout(openSettings, 800);
    },
    'sync-now': (el) => { try { native.folderSyncNow(el.dataset.id); toast('Syncing…'); } catch (e) { } },
    'sync-stop': async (el) => {
      if (!await confirmDialog('Stop syncing this folder?', 'Nothing is deleted: the files stay on the phone and on the PC.', 'Stop syncing')) return;
      try { native.folderSyncRemove(el.dataset.id); } catch (e) { }
      renderSettings();
    },
    'app-lock': (el) => {
      let ok = false;
      try { ok = native.setAppLock(!!el.checked, 0); } catch (e) { toast('Update the Cloud-SSD app'); return; }
      if (!ok) { el.checked = false; toast('Set a screen lock (PIN, pattern or password) on this phone first'); return; }
      toast(el.checked ? 'App lock is on' : 'App lock is off');
      renderSettings();
    },
    'app-lock-after': (el) => {
      try { native.setAppLock(true, parseInt(el.dataset.after, 10) || 0); } catch (e) { return; }
      renderSettings();
    },
    'ring-lockscreen': () => { try { native.askLockScreenRing(); } catch (e) { toast('Update the Cloud-SSD app'); } },
    'battery-exempt': () => { try { native.askBatteryExemption(); } catch (e) { toast('Update the Cloud-SSD app'); } },
    'open-cloud': () => { renderCloud(); openSheet('cloud-modal'); loadDrives(); },
    'open-drives': () => { renderDrives(); openSheet('drives-manager-modal'); loadDrives(); },
    'open-transfers': () => { renderHistory(); renderTransfers(); openSheet('transfers-modal'); refreshHistory(); },
    'open-history': () => { renderHistory(); openSheet('history-sheet'); refreshHistory(); },
    'transfers-kind': (el) => {
      historyKind = el.dataset.kind;
      document.querySelectorAll('#transfers-kind-row .transfer-chip').forEach(c => c.classList.toggle('active', c === el));
      renderHistory();
    },
    'transfers-filter': (el) => {
      historyFilter = el.dataset.filter;
      document.querySelectorAll('#transfers-filter-row .transfer-chip').forEach(c => c.classList.toggle('active', c === el));
      renderHistory();
    },
    'history-open': (el) => {
      const t = xferHistory[+el.dataset.i];
      if (!t || !t.filePath || t.direction === 'offload') return;
      openFileSheet({ name: t.fileName, path: t.filePath, size: t.bytes, modified: t.timestamp, src: 'cloud' });
    },
    'xfer-cancel': (el) => cancelTransfer(+el.dataset.id),
    'send': (el) => sendFiles(el.dataset.kind),
    'offload-now': async () => {
      if (link.state !== 'online') { toast('PC is not connected'); return; }
      if (!await confirmDialog('Free up space on the laptop?', 'Cloud-SSD moves old files from the laptop to the cloud drive now. They stay available through the cloud drive.', 'Start')) return;
      toast('The PC is moving files…');
      try {
        const r = await api('/api/storage/offload-now', { method: 'POST', timeout: 300000 });
        toast(r.data && r.data.message ? r.data.message : apiError(r));
      } catch (e) { toast('PC did not answer'); }
      pollStatus(); refreshHistory();
    },

    'grant-storage': () => {
      if (!isApp) return;
      // Android 11+: one switch ("All files access") covers photos, documents and folders. Older: the classic dialog.
      try { if (appInfo.sdk >= 30) native.requestAllFilesAccess(); else native.requestAllPermissions(); } catch (e) { }
    },
    'phone-rescan': () => { requestScan(); toast('Reading phone storage…'); },
    'phone-cat': (el) => {
      if (!isApp) return;
      closeSheet('storage-modal');
      if (activeTab !== 'browser') switchTab('browser');
      phone.stack = []; phone.view = null; phone.grid = null;
      phoneOpen({ type: 'cat', cat: el.dataset.cat, title: el.dataset.title });
    },
    'phone-starred': () => { phone.stack = []; phone.view = null; phone.grid = null; phoneOpen({ type: 'starred', title: 'Starred' }); },
    'phone-dir': (el) => {
      if (!isApp) { toast('Only in the Android app'); return; }
      closeSheet('storage-modal');
      if (activeTab !== 'browser') switchTab('browser');
      phone.stack = []; phone.view = null; phone.grid = null;
      phoneOpen({ type: 'dir', path: el.dataset.path || '', title: 'Internal storage' });
    },
    'phone-folder': (el) => {
      const i = +el.dataset.i, fo = phone.items.folders[i];
      if (!fo) return;
      if (phone.selecting) {
        const k = 'd' + i;
        if (phone.selected.has(k)) phone.selected.delete(k); else phone.selected.add(k);
        el.classList.toggle('selected', phone.selected.has(k));
        renderSelectBar();
        return;
      }
      phoneOpen({ type: 'dir', path: fo.path, title: fo.name, titled: true });
    },
    'phone-file': (el) => {
      const i = +el.dataset.i, f = phone.items.files[i];
      if (!f) return;
      if (phone.selecting) {
        if (phone.selected.has(i)) phone.selected.delete(i); else phone.selected.add(i);
        el.classList.toggle('selected', phone.selected.has(i));
        renderSelectBar();
      } else openFileSheet(f, phone.items.files);
    },
    'phone-send': (el) => { const f = phone.items.files[+el.dataset.i]; if (f) sendPhoneFiles([f]); },
    'phone-back': () => phoneBack(),
    'phone-layout': () => { phone.grid = !phoneGrid(); renderPhoneItems({}); },
    'phone-select': () => { phone.selecting = !phone.selecting; phone.selected.clear(); renderSelectBar(); renderPhoneItems({}); },
    'phone-select-cancel': () => { phone.selecting = false; phone.selected.clear(); renderSelectBar(); renderPhoneItems({}); },
    'send-folder': () => sendFolderStart(),
    'phone-select-send': () => {
      const sel = Array.from(phone.selected);
      const folders = sel.filter(k => typeof k === 'string' && k[0] === 'd').map(k => phone.items.folders[+k.slice(1)]).filter(Boolean);
      const files = sel.filter(k => typeof k === 'number').map(i => phone.items.files[i]).filter(f => f && f.src === 'phone');
      if (!files.length && !folders.length) { toast('Tap files to select them'); return; }
      // Folders (or a mix): one task, the tree kept, to the PC or another device. Only files: as before.
      if (folders.length && isApp && typeof native.preparePaths === 'function') sendPaths(folders.map(f => f.path).concat(files.map(f => f.path)));
      else sendPhoneFiles(files);
      phone.selecting = false; phone.selected.clear(); renderSelectBar(); renderPhoneItems({});
    },

    'ssd-place': (el) => loadSsd(el.dataset.root),
    'ssd-crumb': (el) => loadSsd(el.dataset.path),
    'ssd-up': () => ssdUp(),
    'ssd-home': () => loadPcHome(true),
    'ssd-refresh': () => {
      if (cloudSource() === 'drive') { if (ssd.path) { ssd.cache.delete(ssd.path); loadSsd(ssd.path, true); } else openSsdTab(); if (link.state !== 'paused') pollStatus(); }
      else if (link.state !== 'online') pollStatus(); else if (ssd.path && ssd.kind) loadSsdKind(ssd.kind); else if (ssd.path) loadSsd(ssd.path, true); else openSsdTab();
    },
    'ssd-layout': () => { ssd.grid = !ssd.grid; ls.set('cloudssd_layout', ssd.grid ? 'grid' : 'list'); const c = ssd.kind ? { folders: [], files: ssd.view.files } : ssd.cache.get(ssd.path); if (c) renderSsd(c); },
    'ssd-sort': () => {
      $('sort-options').innerHTML = SORTS.map(s => `<div class="m3-sheet-row ${s.key === ssd.sort ? 'chosen' : ''}" data-act="ssd-sort-pick" data-key="${s.key}">${s.key === ssd.sort ? SVG.check : '<svg viewBox="0 0 24 24"></svg>'}<div class="m3-sheet-row-text">${s.label}</div></div>`).join('');
      openSheet('sort-sheet');
    },
    'ssd-sort-pick': (el) => { ssd.sort = el.dataset.key; ls.set('cloudssd_sort', ssd.sort); closeSheet('sort-sheet'); const c = ssd.cache.get(ssd.path); if (c) renderSsd(c); },
    'ssd-show-all': () => { ssd.showAll = true; const c = ssd.cache.get(ssd.path); if (c) renderSsd(c); },
    'ssd-folder': (el) => {
      if (Date.now() - ssd.suppressClick < 700) return;
      if (ssd.selecting) { toggleSsdItem(el.dataset.key); return; }
      const fo = ssd.view.folders[+el.dataset.i]; if (fo) loadSsd(fo.path);
    },
    'ssd-file': (el) => {
      if (Date.now() - ssd.suppressClick < 700) return;
      if (ssd.selecting) { toggleSsdItem(el.dataset.key); return; }
      const f = ssd.view.files[+el.dataset.i];
      if (!f) return;
      // Photos open straight in the viewer, so they can be swiped through like a gallery (not from a saved list).
      if (kindOf(f.name) === 'images' && !showingSaved()) openViewer(f, ssd.view.files);
      else openFileSheet(f, ssd.view.files);
    },
    'ssd-more': (el) => { const f = ssd.view.files[+el.dataset.i]; if (f) openFileSheet(f, ssd.view.files); },
    'search-cloud': (el) => {
      const f = search.cloud[+el.dataset.i];
      if (!f) return;
      if (f.pcId && f.pcId !== currentDeviceId() && !activateDevice(f.pcId)) return;
      if (f.isFolder) { closeSearch(true); ssd.level = 'device'; if (activeTab !== 'ssd') { ssd.path = f.path; switchTab('ssd', true); } else { ssd.path = f.path; openSsdTab(); } return; }
      openFileSheet(f);
    },
    'search-phone': (el) => { const f = search.phone[+el.dataset.i]; if (f) openFileSheet(f); },

    'drive-mount': (el) => toggleDrive(el.dataset.id, true),
    'drive-unmount': (el) => toggleDrive(el.dataset.id, false),
    'drive-browse': (el) => {
      ['drives-manager-modal', 'cloud-modal'].forEach(closeSheet);
      ssd.level = 'device';
      if (activeTab !== 'ssd') { ssd.path = el.dataset.root; switchTab('ssd', true); } else { ssd.path = el.dataset.root; openSsdTab(); }
    },

    'remote': (el) => remote(el.dataset.key),
    'saves-refresh': () => loadSaves(),

    'sheet-action': (el) => {
      const a = sheetActions[+el.dataset.i];
      if (!a) return;
      if (!a.keep) closeSheet('file-actions-sheet');
      a.fn();
    },
    'viewer-more': () => { if (viewerFile) { const f = viewerFile; closeSheet('file-viewer-modal'); openFileSheet(f); } },
    'viewer-external': () => { if (viewerFile) openExternally(viewerFile); },
    'viewer-download': () => { if (viewerFile) downloadToPhone(viewerFile); },
    'sheet-close': (el, e) => { if (e.target === el) closeSheet(el.id); },
    'sheet-close-btn': (el) => { const s = el.closest('.modal-backdrop, .m3-bottom-sheet-backdrop'); if (s) closeSheet(s.id); },
    'confirm-yes': () => { const r = confirmResolve; confirmResolve = null; closeSheet('confirm-modal'); if (r) r(true); },
    'confirm-no': () => closeSheet('confirm-modal'),
    'transfer-pill': () => actions['open-transfers']()
  };

  document.addEventListener('click', (e) => {
    const el = e.target.closest('[data-act]');
    if (!el || el.disabled) return;
    const fn = actions[el.dataset.act];
    if (fn) fn(el, e);
  });

  // ------------------------------------------------------------------ Android hooks
  window.onAndroidBack = function () {
    if (openSheets.length) { closeSheet(openSheets[openSheets.length - 1]); return true; }
    if ($('pair-overlay').style.display !== 'none') { hidePair(); return true; }
    if (document.body.classList.contains('searching') || $('global-search-input').value) { closeSearch(true); return true; }
    if (activeTab === 'browser' && phoneBack()) return true;
    if (activeTab === 'ssd' && ssd.selecting) { endSsdSelect(); return true; }
    // Inside a device: up one folder, and from its top back to the list of devices.
    if (activeTab === 'ssd' && ssd.level === 'device') { if (!ssdUp()) showDevices(); return true; }
    if (activeTab !== 'home') { switchTab('home'); return true; }
    return false;
  };

  /** Results from the app's native side. */
  window.onNative = function (event, data) {
    try {
      switch (event) {
        case 'transfer': onTransfer(data); break;
        case 'task': onTask(data); break;
        case 'folderPicked': onFolderPicked(data); break;
        case 'discovery': onDiscovery(data); break;
        case 'storage': onStorageReport(data); break;
        case 'list': onPhoneList(data); break;
        case 'pair': {
          // The same link is also kept for the start of the page; take it once so a one-time code is not used twice.
          let o = data;
          try { const p = native.consumePairLink(); o = p ? JSON.parse(p) : null; } catch (e) { }
          if (o) applyPairLink(o).then(ok => { if (ok) toast('Paired with ' + (link.pcName || 'your PC')); });
          break;
        }
        case 'pdf': onPdf(data); break;
        case 'offline': onKeptEvent(data); break;
        case 'peer': if (data && data.type === 'ask') showPeerAsk(data); break;
        case 'folderSync': onFolderSync(data); break;
        case 'findMyPhone': try { native.consumeFindMyPhone(); } catch (e) { } openFindMyPhone(); break;
        case 'shareIn': onShareIn(); break;
        case 'cloud': { const r = cloudReqs.get(data.reqId); if (r) { cloudReqs.delete(data.reqId); r(data); } break; }
        case 'drive':
          refreshDrive();
          if (data.ok === false) toast(data.error || 'Google sign-in did not finish');
          else if (data.email) toast('Signed in as ' + data.email);
          else if (data.removed) toast('Signed out of ' + data.removed);
          drive.roots = [];
          if (isOpen('settings-modal')) renderSettings();
          if (isOpen('perm-sheet')) renderPermissions();
          if (activeTab === 'ssd' && link.state !== 'online') openSsdTab();
          break;
        case 'mediaFolders': if (window.CloudSsdFs && window.CloudSsdFs.onMediaFolders) window.CloudSsdFs.onMediaFolders(data); break;
        case 'resume':
          // Back from Android's own permission screens: the Permissions page shows what is allowed now.
          if (isOpen('perm-sheet')) renderPermissions();
          if (data.storageAccess !== phone.access) {
            phone.access = data.storageAccess;
            renderPermBanner();
            requestScan();
            if (phone.view && activeTab === 'browser') phoneShow(phone.view);
          }
          refreshAppInfo();
          refreshDrive();
          applyTheme();
          relistTransfers();
          if (link.state !== 'paused' && link.state !== 'unpaired') pollStatus();
          break;
      }
    } catch (err) {
      console.error('onNative ' + event + ': ' + (err && err.stack || err));
    }
  };

  function refreshAppInfo() {
    if (!isApp) return;
    const netBefore = appInfo.network && appInfo.network.key;
    try { appInfo = JSON.parse(native.getAppInfo()); } catch (e) { appInfo = {}; }
    // Another network than before: the same address may now be a different device, so it proves itself again.
    if (netBefore && appInfo.network && appInfo.network.key !== netBefore) link.verified = '';
    setText('modal-app-version', `Cloud-SSD app ${appInfo.version || ''} · ${[appInfo.manufacturer, appInfo.model].filter(Boolean).join(' ')} · Android ${appInfo.android || ''}`);
  }

  // ------------------------------------------------------------------ the clock: everything "live" runs from here
  let ticks = 0;
  function tick() {
    if (document.hidden) return;
    ticks++;
    renderUptime();
    if (ticks % 3 === 0) pollStatus();
    if (activeTab === 'home' && ticks % 2 === 0) renderPhoneTiles();
    if (activeTab === 'home' && ticks % 3 === 0) refreshPcLive();
    if (isOpen('ram-breakdown-modal')) renderRam();
    if (isOpen('storage-modal')) { renderPhoneTiles(); if (ticks % 15 === 0) requestScan(); }
    if ((isOpen('drives-manager-modal') || isOpen('cloud-modal')) && ticks % 4 === 0 && !Object.keys(driveBusy).length) loadDrives();
    if (activeTab === 'gaming' && ticks % 2 === 0) pollHud();
    if (activeTab === 'home' && ticks % 20 === 0) refreshHistory();
    if (activeTab === 'browser' && !phone.view && ticks % 30 === 0) requestScan();
    // "Trying again in 12 s" counts down.
    for (const t of transfers.values()) { if (t.state === 'waiting') { renderTransfers(); break; } }
    // The PC is off: the Google Drive figures on Home come from the phone itself.
    if (link.state !== 'online' && isApp && ticks % 30 === 0) { refreshDrive(); renderStatusTiles(); }
    // "last update 10:42 (5 min ago)" keeps counting.
    if (ticks % 30 === 0 && $('offline-banner') && $('offline-banner').style.display !== 'none') renderOfflineBanner();
    // The list of devices: live dots for saved PCs that are not in use, and the times move on.
    if (activeTab === 'ssd' && ssd.level === 'devices') {
      if (ticks % 15 === 0) probeDevices();
      else if (ticks % 5 === 0) renderDeviceList();
    }
    if (activeTab === 'ssd' && ssd.level === 'device' && ticks % 5 === 0) renderDeviceHeader();
  }

  // ------------------------------------------------------------------ start
  /**
   * What the PC showed last time, read back at start: the Home tiles, drives, transfer history and Cloud SSD tab work
   * from it until the PC answers (or while it is away). The old single list cache is moved into the new store once.
   */
  function restoreLastTime() {
    const s = snapOf();
    if (s) {
      if (s.status && !link.status) link.status = s.status;
      link.lastOnline = s.lastOnline || 0;
      if (Array.isArray(s.drives)) { drivesData = s.drives; ssd.localDrives = s.drives.filter(d => !d.isCloud && d.root); }
      if (Array.isArray(s.history) && !xferHistory.length) xferHistory = s.history;
      if (s.ssdPath && !ssd.path) ssd.path = s.ssdPath;
    }
    const old = ls.json('cloudssd_dircache', null);
    if (old && lists && link.pcId && DATA.migrateDirCache) {
      DATA.migrateDirCache(old).forEach(r => lists.put(link.pcId, r.path, r, 0));
      ls.del('cloudssd_dircache');
    }
  }

  // ------------------------------------------------------------------ several PCs
  /**
   * Before another PC becomes active: keep this one's last data, and drop everything on screen that belongs to it.
   * forgotten: it was just forgotten, so nothing of it is saved again (that would bring it back, without its key).
   */
  function leaveDevice(forgotten) {
    // A forgotten PC: its copy in the cloud is not this phone's to open any more.
    if (forgotten && link.pcId && isApp && typeof native.whenOffSync === 'function') { try { native.whenOffSync(link.pcId, '', false); } catch (e) { } }
    if (!forgotten) {
      if (link.pcId) saveSnap({ lastOnline: link.lastOnline, ssdPath: ssd.path });
      if (link.lastOnline) rememberDevice({ lastOnline: link.lastOnline });
    }
    link.gen++;
    ssd.seq++;
    ssd.cache.clear();
    ssd.path = '';
    ssd.localDrives = [];
    drivesData = null;
    xferHistory = [];
    pcLive = null;
    link.status = null;
    link.lastOnline = 0;
    link.verified = '';
    link.fails = 0;
    link.ping = null;
    snapSavedAt = 0;
    hideOfflineBanner();
  }
  /** Fills the link from a saved device and its key, and starts talking to it. */
  function useDevice(rec, keyInfo) {
    link.server = String((keyInfo && keyInfo.server) || rec.last || rec.lan || '').replace(/\/+$/, '');
    link.lan = String(rec.lan || link.server).replace(/\/+$/, '');
    link.away = rec.away || [];
    link.direct = [];   // the PC tells its own on its first answer
    link.kind = '';
    ls.del('cloudssd_direct_urls');
    link.route = 'lan';
    link.pcId = String(rec.id || '').startsWith('url:') ? '' : rec.id;
    link.pcName = rec.name || '';
    link.token = (keyInfo && keyInfo.token) || '';
    link.deviceId = '';
    setDeviceId(rec.deviceId || '');
    ls.set('cloudssd_server_url', link.lan);
    if (link.pcId) ls.set('cloudssd_pc_id', link.pcId); else ls.del('cloudssd_pc_id');
    ls.set('cloudssd_pc_name', link.pcName);
    ls.set('cloudssd_away_urls', JSON.stringify(link.away));
    if (!isApp) { if (link.token) ls.set('cloudssd_token', link.token); else ls.del('cloudssd_token'); }
    link.state = link.token ? 'init' : 'unpaired';
    restoreLastTime();
    refreshKept();
    renderLink(); renderStatusTiles(); renderHistory(); renderPcTiles();
    if (link.token) pollStatus();
  }
  /** Switches to another saved PC: its last data shows at once, and it is asked for fresh data. */
  function activateDevice(id) {
    if (!registry || !id) return false;
    if (id === currentDeviceId()) return true;
    const rec = registry.get(id);
    const key = rec && registry.activate(id);
    if (!rec || !key) { toast('That PC is not saved on this phone any more'); return false; }
    leaveDevice();
    useDevice(rec, key);
    return true;
  }
  /** Forgets a saved PC on this phone (and tells it first when it is the one connected now). */
  async function forgetDevice(id) {
    const rec = registry && registry.get(id);
    if (!rec) return;
    const name = deviceLabel(rec);
    if (!await confirmDialog('Forget ' + name + '?', `This phone forgets ${name}, its key and the files list saved from it. To use it again, pair with the code shown on that PC.`, 'Forget')) return;
    const wasActive = id === currentDeviceId();
    if (wasActive && link.state === 'online' && pcHas('device-tokens')) {
      try { await api('/api/devices/remove', { method: 'POST', params: { id: 'self' }, timeout: 5000 }); } catch (e) { }
    }
    if (lists) lists.removePc(id);
    if (snaps) snaps.remove(id);
    const next = registry.remove(id);
    toast(name + ' forgotten');
    if (wasActive) {
      leaveDevice(true);
      const nrec = next && registry.get(next);
      if (nrec) useDevice(nrec, registry.activate(next));
      else {
        link.token = ''; link.server = ''; link.lan = ''; link.pcId = ''; link.pcName = ''; link.away = []; setDeviceId('');
        ['cloudssd_server_url', 'cloudssd_pc_id', 'cloudssd_pc_name', 'cloudssd_away_urls', 'cloudssd_direct_urls', 'cloudssd_token'].forEach(k => ls.del(k));
        if (isApp) { try { native.setServerBase('', ''); } catch (e) { } }
        link.state = 'unpaired';
        renderLink(); renderStatusTiles();
      }
    }
    if (activeTab === 'ssd') showDevices();
  }

  function boot() {
    document.body.dataset.tab = 'home';
    document.body.classList.toggle('in-app', isApp);
    refreshAppInfo();
    applyTheme();
    if (window.matchMedia) {
      const mq = window.matchMedia('(prefers-color-scheme: light)');
      const onChange = () => { if (themeMode() === 'system') applyTheme(); };
      if (mq.addEventListener) mq.addEventListener('change', onChange); else if (mq.addListener) mq.addListener(onChange);
    }

    // A leftover service worker from old versions served stale files: remove it.
    if ('serviceWorker' in navigator && /^https?:/.test(location.protocol)) {
      navigator.serviceWorker.getRegistrations().then(rs => rs.forEach(r => r.unregister())).catch(() => { });
    }

    // Opened from the PC's QR code in a browser: http://<pc>:8090/?pair=… (one-time code) or, from an older PC, ?token=…
    const params = new URLSearchParams(location.search);
    const urlToken = params.get('token');
    const urlPair = params.get('pair');
    let pairing = null;
    if (/^https?:/.test(location.protocol)) {
      if (urlPair || urlToken) {
        try { window.history.replaceState(null, '', location.pathname); } catch (e) { }
        if (urlToken) saveCreds(location.origin, urlToken);
        if (urlPair) {
          // A one-time code: this browser uses it now (the app pairs with the 6-digit code or finds the PC itself).
          pairing = applyPairLink({ host: location.host, pair: urlPair });
        } else if (!isApp && /Android/i.test(navigator.userAgent)) {
          // An older PC's link with its key: the same phone may have the app installed, so offer to hand it over.
          $('open-in-app-link').href = `intent://pair?host=${encodeURIComponent(location.host)}&token=${encodeURIComponent(urlToken)}#Intent;scheme=cloudssd;package=com.cloudssd.mobile;end`;
          show('browser-app-banner', true);
        }
      } else if (!link.server) {
        link.server = location.origin;
      }
    }
    if (isApp) {
      // Older versions kept the key in the page's storage: move it into the phone's encrypted store once.
      const oldToken = ls.get('cloudssd_token');
      try {
        const p = JSON.parse(native.getPairing() || '{}');
        if (oldToken && !p.token) native.setServerBase(link.server, oldToken);
        link.token = p.token || oldToken || '';
        if (!link.server && p.server) { link.server = p.server; link.lan = p.server; }
      } catch (e) { link.token = oldToken || ''; }
      ls.del('cloudssd_token');
      try {
        const pending = native.consumePairLink();
        if (pending) pairing = applyPairLink(JSON.parse(pending));
      } catch (e) { }
      // The phone's own notes speak the page's language.
      try { if (I18N && typeof native.setLanguage === 'function') native.setLanguage(I18N.lang()); } catch (e) { }
      // Opened from the "listening for your PC" note.
      try { if (typeof native.consumeFindMyPhone === 'function' && native.consumeFindMyPhone()) setTimeout(openFindMyPhone, 600); } catch (e) { }
      // Opened by "Share → Send to a device…" from another app: ask where once the PC has answered.
      try { if (typeof native.shareInFiles === 'function' && JSON.parse(native.shareInFiles() || '[]').length) setTimeout(onShareIn, 2500); } catch (e) { }
      try { phone.access = native.getStorageAccess(); } catch (e) { }
      refreshDrive();
      relistTransfers();
    }
    // The saved list of PCs: older versions knew one PC, which becomes its first entry.
    if (registry) {
      if (!isApp) registry.migrate({ server: link.lan || link.server, pcId: link.pcId, pcName: link.pcName, away: link.away, deviceId: link.deviceId, token: link.token });
      if ((link.lan || link.server) && !registry.get(currentDeviceId())) rememberDevice({});
    }
    restoreLastTime();
    wireGallery();
    wireSsdLongPress();

    $('global-search-input').addEventListener('input', (e) => onSearchInput(e.target.value));
    $('pair-code').addEventListener('input', (e) => {
      const d = e.target.value.replace(/\D/g, '').slice(0, 6);
      e.target.value = d.length > 3 ? d.slice(0, 3) + ' ' + d.slice(3) : d;
    });
    $('pair-code').addEventListener('keydown', (e) => { if (e.key === 'Enter') submitPair(); });
    for (const id of ['file-uploader', 'photo-uploader']) {
      $(id).addEventListener('change', (e) => {
        Array.from(e.target.files || []).forEach(uploadInBrowser);
        e.target.value = '';
      });
    }
    document.addEventListener('visibilitychange', () => { if (!document.hidden && link.state !== 'paused' && link.state !== 'unpaired') pollStatus(); });

    renderLink();
    renderStatusTiles();
    renderPhoneTiles();
    renderPcTiles();
    renderPermBanner();
    renderStorage();
    renderHistory();
    setText('cat-stat-starred', starred.length ? starred.length + ' files' : 'None yet');

    if (pairing) { /* the pairing link decides: it polls when it worked and shows the pairing screen when not */ }
    else if (link.server && link.token) pollStatus();
    else { link.state = 'unpaired'; renderLink(); renderStatusTiles(); showPair(''); }
    setInterval(tick, 1000);
  }

  // File management on the cloud drives (new folder, upload here, rename, move, delete) lives in fs.js.
  // Space & RAM (space.js): storage and RAM of this phone and of the PC, with what can be removed.
  if (window.CloudSsdSpace) {
    window.CloudSsdSpace.init({
      api, apiError, toast, esc, native, isApp, link, pcHas, openSheet, closeSheet, isOpen, confirmDialog, choiceSheet, actions,
      fmtBytes, fmtDate, timeAgo, info: () => appInfo
    });
  }
  if (window.CloudSsdFs) {
    window.CloudSsdFs.init({
      api, apiUrl, apiError, toast, esc, SVG, native, isApp, link, ssd, placeOf, loadSsd, openSheet, closeSheet, confirmDialog, actions,
      isCloudId, cloudSource, cloudOp, pcHas, choiceSheet, drive, keepActions, syncActions
    });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
  else boot();
})();
