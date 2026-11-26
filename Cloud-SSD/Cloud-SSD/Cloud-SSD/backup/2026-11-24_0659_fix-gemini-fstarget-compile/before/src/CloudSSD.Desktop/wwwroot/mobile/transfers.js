/*
 * The Transfers page, like a phone maker's PC suite: what is moving now (a card per send — a folder or many files —
 * with how it goes: Wi-Fi, USB, hotspot; speed, time left, the file on the way, where it is saved), what finished
 * today (Open folder, Retry what failed), and History as its own page.
 *
 * app.js keeps the tasks (the same model as on the PC: {id, dir, kind, folder, device, link, state, files, doneFiles,
 * bytes, doneBytes, speed, current, failed[], saveTo, openPath, started, ended, canRetry, canCancel}) and calls
 * window.CSSD_renderTasks(tasks) when they change. Until it does, the page's own active list (#transfers-modal-active)
 * stays in "Now".
 */
(function () {
  'use strict';

  const $ = (id) => document.getElementById(id);
  const esc = (s) => String(s == null ? '' : s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const native = window.AndroidBridge || null;
  let tasks = null;          // null: app.js has not sent any yet
  let filter = 'all';

  function size(n) {
    if (!(n > 0)) return '0 B';
    const u = ['B', 'KB', 'MB', 'GB', 'TB'];
    let i = 0;
    while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
    return (n >= 100 || i === 0 ? Math.round(n) : n.toFixed(1)) + ' ' + u[i];
  }
  function incoming(t) { return t.dir === 'toPhone' || t.dir === 'fromDevice'; }
  function linkText(l) {
    return ({ usb: 'USB', hotspot: 'Hotspot', tailscale: 'Internet', relay: 'Through the PC' })[l] || 'Wi-Fi';
  }
  /** "1 file", "12 files". */
  function count(n) { return n === 1 ? '1 file' : n + ' files'; }
  function what(t) {
    if (t.kind === 'folder' && t.folder) return t.folder;
    if (t.files === 1 && (t.name || t.current)) return t.name || t.current;
    return count(t.files);
  }
  function title(t) { return what(t) + (incoming(t) ? ' ← ' : ' → ') + (t.device || 'PC'); }
  function open(t) { return t.state === 'waiting' || t.state === 'running' || t.state === 'paused'; }
  function shows(t) {
    if (filter === 'toPc') return t.dir === 'toPc' || t.dir === 'toDevice';
    if (filter === 'toPhone') return t.dir === 'toPhone' || t.dir === 'fromDevice';
    return true;
  }
  function left(t) {
    if (t.state !== 'running' || !(t.speed > 0) || !(t.bytes > t.doneBytes)) return '';
    const s = (t.bytes - t.doneBytes) / t.speed;
    return s < 60 ? Math.max(1, Math.round(s)) + ' s left' : s < 3600 ? Math.ceil(s / 60) + ' min left' : (s / 3600).toFixed(1) + ' h left';
  }
  function reason(t) {
    // Why most of them failed (one file caught mid-way may say something else than the rest).
    const count = {};
    (t.failed || []).forEach(f => { count[f.reason] = (count[f.reason] || 0) + 1; });
    const r = Object.keys(count).sort((a, b) => count[b] - count[a])[0] || 'other';
    return ({ denied: 'not allowed on the device', network: 'the connection dropped', space: 'not enough space', missing: 'the file is gone', cancelled: 'cancelled' })[r] || 'something went wrong';
  }
  const UP = '<path d="M12 19V5M5 12l7-7 7 7"/>', DOWN = '<path d="M12 5v14M5 12l7 7 7-7"/>';
  const FOLDER = '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>';

  function nowCard(t) {
    const pct = t.bytes > 0 ? Math.min(100, t.doneBytes * 100 / t.bytes) : t.files > 0 ? t.doneFiles * 100 / t.files : 0;
    const status = t.state === 'waiting' ? 'Waiting for the one before it'
      : t.state === 'paused' ? 'Paused'
      : Math.round(pct) + '% · ' + size(t.doneBytes) + ' of ' + size(t.bytes);
    const speed = t.state === 'running' && t.speed > 0 ? size(t.speed) + '/s' + (left(t) ? ' · ' + left(t) : '') : '';
    const fast = t.link === 'wifi' || t.link === 'usb' || t.link === 'hotspot' ? ' · fast' : '';
    return `<div class="xt-card">
      <div class="xt-top">
        <div class="xt-icon"><svg viewBox="0 0 24 24" aria-hidden="true">${incoming(t) ? DOWN : UP}</svg></div>
        <div class="xt-names"><div class="xt-title">${esc(title(t))}</div><div class="xt-sub">${esc(count(t.files) + ' · ' + size(t.bytes))}</div></div>
        <div class="xt-link">${esc(linkText(t.link) + fast)}</div>
      </div>
      <div class="xt-bar"><div style="width:${pct.toFixed(1)}%"></div></div>
      <div class="xt-line"><span>${esc(status)}</span><span>${esc(speed)}</span></div>
      ${t.current ? `<div class="xt-sub xt-current">${esc('File ' + Math.min(t.doneFiles + 1, t.files) + ' of ' + t.files + ': ' + t.current)}</div>` : ''}
      <div class="xt-foot">
        <svg viewBox="0 0 24 24" aria-hidden="true">${FOLDER}</svg>
        <div class="xt-save">${esc(t.saveTo ? 'Saving to ' + t.saveTo : '')}</div>
        ${t.canCancel ? `<button class="xt-btn ghost" data-xt="cancel" data-id="${esc(t.id)}">Cancel</button>` : ''}
      </div>
    </div>`;
  }

  function doneRow(t) {
    const bad = t.state === 'partial' || t.state === 'failed';
    const when = new Date(t.ended || t.started || Date.now());
    const hm = String(when.getHours()).padStart(2, '0') + ':' + String(when.getMinutes()).padStart(2, '0');
    const sub = t.state === 'done' ? 'Done ' + hm + ' · ' + linkText(t.link)
      : t.state === 'partial' ? t.doneFiles + ' sent, ' + (t.failed || []).length + ' failed · ' + reason(t)
      : t.state === 'failed' ? 'Failed · ' + reason(t)
      : 'Cancelled · ' + hm;
    const btn = t.canRetry && (t.failed || []).length
      ? `<button class="xt-btn" data-xt="retry" data-id="${esc(t.id)}">Retry ${(t.failed || []).length}</button>`
      : t.openPath ? `<button class="xt-btn" data-xt="open" data-path="${esc(t.openPath)}">Open folder</button>` : '';
    return `<div class="xt-row"><span class="xt-dot ${bad ? 'bad' : t.state === 'cancelled' ? 'off' : ''}"></span>
      <div class="xt-names"><div class="xt-row-title">${esc(title(t))}</div><div class="xt-sub ${bad ? 'bad' : ''}">${esc(sub)}</div></div>${btn}</div>`;
  }

  /** Home: what is moving now and the last few that finished today (the Transfers page has the rest, and History). */
  function renderHome() {
    const now = $('home-xfer-now'), done = $('home-xfer-done'), active = $('transfers-active');
    if (!now || !done) return;
    const empty = '<div class="xt-empty xt-empty-card">Nothing sent today. Pick Photos, Files or Folder above to send to the PC.</div>';
    if (!tasks) {
      now.innerHTML = '';
      done.innerHTML = active && active.children.length ? '' : empty;
      if (active) active.hidden = false;
      return;
    }
    if (active) active.hidden = true;
    const moving = tasks.filter(open);
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const finished = tasks.filter(t => !open(t) && new Date(t.ended || t.started || 0) >= today).slice(0, 3);
    now.innerHTML = moving.map(nowCard).join('');
    done.innerHTML = finished.length ? '<div class="xt-list">' + finished.map(doneRow).join('') + '</div>' : moving.length ? '' : empty;
  }

  function render() {
    renderHome();
    const now = $('xfer-now'), done = $('xfer-done'), active = $('transfers-modal-active');
    if (!now || !done) return;
    if (!tasks) {
      // No task list from app.js yet: its own active rows stay the "Now" part.
      now.innerHTML = '';
      done.innerHTML = '<div class="xt-empty">Finished sends show here. Older ones are in History.</div>';
      if (active) active.hidden = false;
      return;
    }
    if (active) active.hidden = true;
    const list = tasks.filter(shows);
    const moving = list.filter(open);
    const today = new Date(); today.setHours(0, 0, 0, 0);
    const finished = list.filter(t => !open(t) && new Date(t.ended || t.started || 0) >= today).slice(0, 12);
    now.innerHTML = moving.length ? moving.map(nowCard).join('')
      : '<div class="xt-empty">Nothing is moving right now. Send from Home, or open the PC\'s files from this phone.</div>';
    done.innerHTML = finished.length ? '<div class="xt-list">' + finished.map(doneRow).join('') + '</div>'
      : '<div class="xt-empty">Nothing yet today. Older sends are in History.</div>';
  }

  /** app.js: the tasks changed (the whole list, newest first). */
  window.CSSD_renderTasks = function (list) {
    tasks = Array.isArray(list) ? list : [];
    render();
  };

  document.addEventListener('click', (e) => {
    const tab = e.target.closest('[data-xt-filter]');
    if (tab) {
      filter = tab.dataset.xtFilter;
      document.querySelectorAll('[data-xt-filter]').forEach(b => b.classList.toggle('active', b === tab));
      render();
      return;
    }
    const b = e.target.closest('[data-xt]');
    if (!b || !native) return;
    try {
      if (b.dataset.xt === 'cancel' && native.taskCancel) native.taskCancel(b.dataset.id);
      if (b.dataset.xt === 'retry' && native.taskRetry) native.taskRetry(b.dataset.id);
      if (b.dataset.xt === 'open' && native.openFolder) native.openFolder(b.dataset.path);
    } catch (err) { /* the app reports problems itself */ }
  });

  // A fresh page asks the app for the tasks it already has.
  function start() {
    try { if (native && typeof native.tasks === 'function') window.CSSD_renderTasks(JSON.parse(native.tasks() || '[]')); } catch (e) { }
    render();
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
