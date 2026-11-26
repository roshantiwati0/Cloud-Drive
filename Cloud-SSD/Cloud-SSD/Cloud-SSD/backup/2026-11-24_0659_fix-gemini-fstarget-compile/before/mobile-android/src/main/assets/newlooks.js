/*
 * The looks Kit, Paper and Glow (newlooks.css) show two parts the older looks do not have:
 *   #nl-top  — a title row above every tab: the tab's name, a search button (it opens the search bar), and on Home
 *              the PC's state (Paper: a line under the name; Glow: the PC's name with a "Live" mark).
 *   #nl-home — the top of Home: the PC as a card (Kit), the three storages (bars in Kit, numbers in Paper, round
 *              gauges in Glow), and under them three cards of the same kind for the phone's RAM, the PC's RAM and
 *              the PC's drives.
 *
 * Both are made here once and shown only in those looks (CSS). Their numbers are taken from the older Home tiles,
 * which app.js keeps filling (they are only hidden), so nothing is fetched twice and a tap here does what a tap on
 * the tile it stands for does.
 */
(function () {
  'use strict';

  const $ = (id) => document.getElementById(id);
  const text = (id) => { const e = $(id); return e ? e.textContent.trim() : ''; };
  const NEW_LOOKS = ['kit', 'paper', 'glow'];
  const RING = 2 * Math.PI * 30;

  /** The three storages: [key, the tile's "free" badge, its used/total line, its bar]. */
  const STORES = [
    ['phone', 'hw-phone-free', 'hw-phone-val', 'hw-phone-bar'],
    ['pc', 'hw-pcdisk-free', 'hw-pcdisk-val', 'hw-pcdisk-bar'],
    ['cloud', 'hw-ssd-badge', 'hw-ssd-val', 'hw-ssd-bar']
  ];
  /**
   * The three cards under them, drawn the same way: [key, the tile's value (a percentage, or the drives), the line
   * under it, its bar]. A RAM tile's line is "used / total"; the drives' is the PC's name.
   */
  const MORE = [
    ['ram', 'hw-ram-pct', 'hw-ram-val', 'hw-ram-bar'],
    ['pcram', 'hw-pcram-pct', 'hw-pcram-val', 'hw-pcram-bar'],
    ['drives', 'hw-pc-drives-val', 'hw-pc-status-badge', 'hw-pc-bar']
  ];
  /**
   * One card: a ring (Glow), its name, the big value, a word after it, a line under it, a bar (Kit). Its parts are
   * named nl-s-<key>-<part>: the PC's own card has an "nl-pc-sub" already, and the storage card of the key "pc"
   * took that name before, so its line under the number was never filled.
   */
  const card = ([key]) => `
        <button type="button" class="nl-store" data-nl-open="${key}">
          <span class="nl-ring"><svg viewBox="0 0 72 72" aria-hidden="true"><circle cx="36" cy="36" r="30"/><circle cx="36" cy="36" r="30" transform="rotate(-90 36 36)" id="nl-s-${key}-arc"/></svg><b id="nl-s-${key}-pct" data-no-i18n></b></span>
          <span class="nl-store-label" id="nl-s-${key}-label" data-no-i18n></span>
          <span class="nl-store-big" id="nl-s-${key}-big" data-no-i18n></span>
          <span class="nl-store-word" id="nl-s-${key}-word" data-no-i18n></span>
          <span class="nl-store-sub" id="nl-s-${key}-sub" data-no-i18n></span>
          <span class="nl-store-bar"><i id="nl-s-${key}-bar"></i></span>
        </button>`;
  const SOURCE = { phone: 'hw-phone-val', pc: 'hw-pcdisk-val', cloud: 'hw-ssd-val', ram: 'hw-ram-val', pcram: 'hw-pcram-val', drives: 'hw-pc-drives-val' };

  const tileOf = (key) => { const e = $(SOURCE[key]); return e ? e.closest('.hw-widget-card') : null; };
  const labelOf = (key) => { const t = tileOf(key); const l = t && t.querySelector('.hw-widget-label'); return l ? l.textContent.trim() : ''; };
  const look = () => document.documentElement.dataset.look || '';
  const online = () => { const d = $('header-status-dot'); return !!d && !d.classList.contains('offline'); };
  /** The PC's name as the Home tile shows it ("—" until one is known). */
  const pcName = () => { const n = text('hw-pc-status-badge'); return n && n !== '—' ? n : 'PC'; };

  function build() {
    const header = document.querySelector('.search-header');
    const home = $('tab-home');
    if (!header || !home || $('nl-top')) return false;

    const top = document.createElement('div');
    top.id = 'nl-top';
    top.className = 'nl-top';
    top.innerHTML = `
      <button type="button" class="nl-top-text" id="nl-top-text">
        <span class="nl-over" id="nl-over" data-no-i18n></span>
        <span class="nl-title" id="nl-title" data-no-i18n></span>
        <span class="nl-status"><span class="nl-dot"></span><span id="nl-status-text" data-no-i18n></span></span>
      </button>
      <span class="nl-live"><span class="nl-dot"></span><span id="nl-live-text">Live</span></span>
      <button type="button" class="nl-icon-btn" id="nl-search" aria-label="Search"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="M21 21l-4.3-4.3"/></svg></button>
      <button type="button" class="nl-icon-btn nl-me" id="nl-me" data-act="open-profile" aria-label="More" data-no-i18n></button>`;
    header.parentNode.insertBefore(top, header.nextSibling);

    const box = document.createElement('div');
    box.id = 'nl-home';
    box.className = 'nl-home';
    box.innerHTML = `
      <div class="nl-pc" id="nl-pc">
        <div class="nl-pc-row">
          <div class="nl-pc-icon"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 4h18v12H3zM8 20h8M12 16v4"/></svg></div>
          <div class="nl-pc-text"><div class="nl-pc-name" id="nl-pc-name" data-no-i18n></div><div class="nl-pc-sub" id="nl-pc-sub" data-no-i18n></div></div>
          <span class="nl-pc-chip" id="nl-pc-chip">Live</span>
        </div>
        <div class="nl-pc-actions">
          <button type="button" data-nl="send">Send to PC</button>
          <button type="button" data-nl="pcfiles">Open PC files</button>
        </div>
      </div>
      <div class="section-header nl-head">
        <div class="section-title">System &amp; Storage</div>
        <div class="section-action nl-head-live"><span class="nl-dot"></span><span id="nl-head-live">Live</span></div>
      </div>
      <div class="hw-group-title" id="nl-lab-stores" data-no-i18n></div>
      <div class="nl-stores">${STORES.map(card).join('')}
      </div>
      <div class="hw-group-title" id="nl-lab-more" data-no-i18n></div>
      <div class="nl-stores nl-more">${MORE.map(card).join('')}
      </div>`;
    const banner = $('home-game-banner');
    home.insertBefore(box, banner ? banner.nextSibling : home.firstChild);

    // The older tiles stay in the page (app.js fills them) and are hidden in these looks.
    const grid = home.querySelector('.hardware-widgets-grid');
    if (grid && grid.closest('.section-container')) grid.closest('.section-container').classList.add('nl-old-tiles');
    const send = home.querySelector('.send-tiles');
    if (send && send.closest('.section-container')) send.closest('.section-container').classList.add('nl-send-sec');
    return true;
  }

  const set = (id, value) => { const e = $(id); if (e && e.textContent !== value) e.textContent = value; };

  /** "70.0 GB free" → the number with its unit, and the word after it (so the number can be big). */
  function split(badge) {
    const parts = badge.split(/\s+/);
    if (parts.length >= 2 && /^[\d.,]+$/.test(parts[0])) return [parts[0] + ' ' + parts[1], parts.slice(2).join(' ')];
    // "Z: Y: online" → the drive letters big, what follows them small.
    const after = parts.findIndex(p => !/^[A-Za-z]:$/.test(p));
    if (after > 0) return [parts.slice(0, after).join(' '), parts.slice(after).join(' ')];
    return [badge, ''];
  }

  /** Home's top part, from what the tiles show now. */
  function updateHome() {
    for (const [key, badgeId, valId, barId] of STORES.concat(MORE)) {
      const badge = text(badgeId);
      const [big, word] = split(badge);
      const bar = $(barId);
      const pct = Math.max(0, Math.min(100, parseFloat(bar && bar.style.width) || 0));
      set('nl-s-' + key + '-label', labelOf(key));
      set('nl-s-' + key + '-big', big);
      set('nl-s-' + key + '-word', word);
      // The gauges of Glow have no big number: their line under the name says what is free. A RAM gauge has its
      // percentage in the ring already, so its line stays "used / total".
      const ram = key === 'ram' || key === 'pcram';
      set('nl-s-' + key + '-sub', look() === 'glow' && !ram ? badge : text(valId));
      set('nl-s-' + key + '-pct', Math.round(pct) + '%');
      const fill = $('nl-s-' + key + '-bar');
      if (fill) fill.style.width = pct + '%';
      const arc = $('nl-s-' + key + '-arc');
      if (arc) arc.setAttribute('stroke-dasharray', (RING * pct / 100).toFixed(1) + ' ' + RING.toFixed(1));
    }
    // The small headings over the two rows are the older tiles' own ("RAM", "Storage"), in the language they are
    // shown in; the second row holds the RAM cards and the PC's drives, so its heading names both.
    const groups = Array.from(document.querySelectorAll('.nl-old-tiles .hw-group-title')).map(e => e.textContent.trim());
    set('nl-lab-stores', groups[1] || '');
    set('nl-lab-more', [groups[0], labelOf('drives')].filter(Boolean).join(' · '));
    set('nl-pc-name', pcName());
    set('nl-pc-sub', text('hw-pc-drives-val'));
    updateTop();
  }

  /** The title row: the open tab's name; on Home what the look puts there. */
  function updateTop() {
    const top = $('nl-top');
    if (!top) return;
    const tab = document.body.dataset.tab || 'home';
    const onHome = tab === 'home';
    const tabName = text('nav-' + tab) || tab;
    const on = online();
    top.classList.toggle('nl-on-home', onHome);
    top.classList.toggle('nl-off', !on);
    const box = $('nl-home');
    if (box) box.classList.toggle('nl-off', !on);
    let title = tabName;
    if (onHome && look() === 'paper') title = 'Cloud-SSD';
    if (onHome && look() === 'glow') title = pcName();
    set('nl-title', title);
    set('nl-over', text('hw-pc-drives-val'));
    set('nl-status-text', pcName() + ' · ' + text('hw-pc-drives-val'));
    set('nl-live-text', on ? 'Live' : 'Offline');
    set('nl-head-live', on ? 'Live' : 'Offline');
    set('nl-pc-chip', on ? 'Live' : 'Offline');
    set('nl-me', text('pc-avatar') || '?');
  }

  let queued = false;
  function soon() {
    if (queued) return;
    queued = true;
    requestAnimationFrame(() => { queued = false; updateHome(); });
  }

  function openSearch() {
    document.body.classList.add('nl-search');
    const input = $('global-search-input');
    if (input) input.focus();
  }
  /** The search bar goes away again when nothing is typed and it is left. */
  function maybeCloseSearch() {
    const input = $('global-search-input');
    if (!document.body.classList.contains('nl-search')) return;
    if (document.body.classList.contains('searching') || (input && (input.value || document.activeElement === input))) return;
    document.body.classList.remove('nl-search');
  }

  function wire() {
    $('nl-search').addEventListener('click', openSearch);
    const input = $('global-search-input');
    if (input) input.addEventListener('blur', () => setTimeout(maybeCloseSearch, 200));

    // On Home the name row (Paper, Glow) and the PC card (Kit) open the connection page, as the PC's card in More does.
    const openPc = () => { const c = $('home-connection-card'); if (c) c.click(); };
    $('nl-top-text').addEventListener('click', () => { if ((document.body.dataset.tab || 'home') === 'home' && look() !== 'kit') openPc(); });
    $('nl-home').addEventListener('click', (e) => {
      const act = e.target.closest('[data-nl]');
      if (act) {
        e.stopPropagation();
        const target = act.dataset.nl === 'send' ? document.querySelector('.send-tile[data-kind="all"]') : $('nav-ssd');
        if (target) target.click();
        return;
      }
      const open = e.target.closest('[data-nl-open]');
      if (open) { const tile = tileOf(open.dataset.nlOpen); if (tile) tile.click(); return; }
      if (e.target.closest('#nl-pc')) openPc();
    });

    // The tiles change (new numbers, another language): the copies follow.
    const old = document.querySelector('.nl-old-tiles');
    if (old) new MutationObserver(soon).observe(old, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['style'] });
    for (const id of ['header-status-dot', 'pc-avatar']) {
      const e = $(id);
      if (e) new MutationObserver(soon).observe(e, { attributes: true, childList: true, characterData: true, subtree: true });
    }
    const nav = document.querySelector('.material-bottom-nav');
    if (nav) new MutationObserver(soon).observe(nav, { subtree: true, childList: true, characterData: true });
    new MutationObserver(() => { soon(); maybeCloseSearch(); }).observe(document.body, { attributes: true, attributeFilter: ['data-tab', 'class'] });
    new MutationObserver(soon).observe(document.documentElement, { attributes: true, attributeFilter: ['data-look'] });
  }

  function start() {
    if (!build()) return;
    wire();
    updateHome();
  }
  window.CSSD_NEW_LOOKS = NEW_LOOKS;
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
