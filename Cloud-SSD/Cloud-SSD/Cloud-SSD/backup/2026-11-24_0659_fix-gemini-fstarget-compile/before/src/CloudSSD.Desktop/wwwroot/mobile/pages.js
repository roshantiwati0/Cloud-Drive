/*
 * Every sheet opens as a full page, like any phone app (pages.css): a bar at the top with a back arrow and the
 * page's title. The sheets are written in index.html; this gives each one its bar once, at start.
 *
 * The back arrow does what the phone's Back does (window.onAndroidBack in app.js): it closes the page on top.
 * Only the "Are you sure?" dialog (confirm-modal) stays a small box.
 */
(function () {
  'use strict';

  const BACK_SVG = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2z"/></svg>';

  function backButton() {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'page-back';
    b.setAttribute('aria-label', 'Back');
    b.innerHTML = BACK_SVG;
    b.addEventListener('click', (e) => {
      e.stopPropagation();
      if (typeof window.onAndroidBack === 'function') window.onAndroidBack();
    });
    return b;
  }

  /** Turns one sheet into a page: its own header row becomes the bar, or a new bar takes the sheet's title. */
  function makePage(root) {
    if (root.dataset.page) return;
    root.dataset.page = '1';
    root.classList.add('as-page');
    const box = root.firstElementChild;
    if (!box) return;
    // The viewer has its own bar with a back arrow already.
    const viewerHead = box.querySelector(':scope > .viewer-modal-header');
    if (viewerHead) { viewerHead.classList.add('page-bar'); return; }
    // A header row that is already there (title, sub line, its own buttons): the back arrow goes first in it.
    const head = box.querySelector(':scope > .conn-modal-header, :scope > .m3-sheet-file-header, :scope > .profile-menu-head');
    if (head) {
      head.classList.add('page-bar');
      head.insertBefore(backButton(), head.firstChild);
      return;
    }
    // Only a title line (access-head, sheet-subtitle): a new bar with the back arrow, and the title moves into it.
    const bar = document.createElement('div');
    bar.className = 'page-bar page-bar-new';
    bar.appendChild(backButton());
    const title = box.querySelector(':scope > .access-head > .access-title, :scope > .sheet-subtitle');
    if (title) {
      title.classList.add('page-bar-title');
      bar.appendChild(title);
    }
    const handle = box.querySelector(':scope > .m3-drag-handle');
    box.insertBefore(bar, handle ? handle.nextSibling : box.firstChild);
  }

  /*
   * The phone's status bar: on the page behind it is the look's top band (blue in Ocean); while a page is open, the
   * colour of the page's bar, so they read as one. app.js sets the look's bars; this sets them for pages and back.
   */
  const LOOK_BARS = {
    ac: { top: '#2563EB', rest: '#FFFFFF', statusDark: false, navDark: true },
    saaf: { top: '#F4F6FA', rest: '#FFFFFF', statusDark: true, navDark: true },
    raat: { top: '#0B0F17', rest: '#0B0F17', statusDark: false, navDark: false },
    kit: { top: '#F3F5F9', rest: '#FFFFFF', statusDark: true, navDark: true },
    paper: { top: '#FFFFFF', rest: '#FFFFFF', statusDark: true, navDark: true },
    glow: { top: '#0A0A0C', rest: '#0A0A0C', statusDark: false, navDark: false }
  };
  const PAGE_BARS = {
    ac: { top: '#FFFFFF', rest: '#F4F6FA', statusDark: true, navDark: true },
    saaf: { top: '#FFFFFF', rest: '#F4F6FA', statusDark: true, navDark: true },
    raat: { top: '#121826', rest: '#0B0F17', statusDark: false, navDark: false },
    kit: { top: '#FFFFFF', rest: '#F3F5F9', statusDark: true, navDark: true },
    paper: { top: '#FFFFFF', rest: '#FFFFFF', statusDark: true, navDark: true },
    glow: { top: '#17171A', rest: '#0A0A0C', statusDark: false, navDark: false }
  };
  let pageShown = null;
  function syncBars() {
    const look = document.documentElement.dataset.look || 'ac';
    const onPage = !!document.querySelector('.as-page.active:not(#file-viewer-modal)');
    const key = look + (onPage ? ':page' : '');
    if (key === pageShown) return;
    // Back on the main screen with no page shown before: app.js has the bars right already.
    if (!onPage && pageShown === null) return;
    pageShown = key;
    const b = (onPage ? PAGE_BARS : LOOK_BARS)[look] || LOOK_BARS.ac;
    const bridge = window.AndroidBridge;
    if (bridge && typeof bridge.setBars === 'function') {
      try { bridge.setBars(b.top, b.rest, b.statusDark, b.navDark); } catch (e) { }
    }
  }

  /*
   * A page opened from another page lies on top of it: each page that opens takes a place above the ones already
   * open (the style sheets gave each sheet a fixed height from when they were small sheets, so "Look and language"
   * opened behind Settings). The "Are you sure?" box (1300) and the toast (1400) stay above every page.
   */
  const STACK_BASE = 1210;
  function syncStack(roots) {
    let top = STACK_BASE;
    roots.forEach((r) => { if (r.classList.contains('active') && r.dataset.z) top = Math.max(top, +r.dataset.z); });
    roots.forEach((r) => {
      if (r.id === 'confirm-modal') return;
      const open = r.classList.contains('active');
      if (open && !r.dataset.z) { top += 1; r.dataset.z = String(top); r.style.zIndex = String(top); }
      else if (!open && r.dataset.z) { delete r.dataset.z; r.style.zIndex = ''; }
    });
  }

  function setup() {
    const roots = document.querySelectorAll('.modal-backdrop.sheet, .m3-bottom-sheet-backdrop, .profile-menu-backdrop, #file-viewer-modal');
    roots.forEach((r) => { if (r.id !== 'confirm-modal') makePage(r); });
    // A page opens or closes (its class "active"), or the look changes (data-look on <html>).
    const mo = new MutationObserver(() => { syncStack(roots); syncBars(); });
    syncStack(roots);
    roots.forEach((r) => mo.observe(r, { attributes: true, attributeFilter: ['class'] }));
    mo.observe(document.documentElement, { attributes: true, attributeFilter: ['data-look'] });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', setup);
  else setup();
})();
