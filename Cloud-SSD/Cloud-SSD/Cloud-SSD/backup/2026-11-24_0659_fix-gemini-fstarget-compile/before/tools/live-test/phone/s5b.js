// Stage 5 E1 UI: with the test PC away, its row shows "Wake" and its menu "Wake PC"; tapping it says the signal went.
// __CODE__ = row|menu (which one to tap). Toasts are kept in window.__toasts for s5c.js. Runs in English.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  window.CloudSsdI18n.setLang('english');
  if (!window.__toastWatch) {
    window.__toasts = [];
    window.__toastWatch = new MutationObserver(() => { const t = $('toast').textContent; if (t && window.__toasts[window.__toasts.length - 1] !== t) window.__toasts.push(t); });
    window.__toastWatch.observe($('toast'), { childList: true, characterData: true, subtree: true });
  }
  const out = {};
  for (let i = 0; i < 90 && $('header-status-dot').className.includes('online'); i++) await wait(1000);
  out.title = $('home-conn-title').innerText;
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  $('nav-home').click(); await wait(300);
  // Profile menu → "Device list" (its first row).
  document.querySelector('[data-act="open-profile"]').click(); await wait(700);
  document.querySelector('#profile-menu-list [data-i="0"]').click(); await wait(2000);
  const pc = localStorage.getItem('cloudssd_pc_id');
  const row = Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc);
  out.row = row ? row.innerText.replace(/\s+/g, ' ').trim() : 'no row';
  const chip = row && row.querySelector('[data-act="device-wake"]');
  out.chip = chip ? chip.innerText : null;
  if ('__CODE__' === 'row') {
    if (chip) chip.click();
  } else {
    row.querySelector('[data-act="device-more"]').click(); await wait(800);
    const rows = Array.from(document.querySelectorAll('#sheet-actions-list .m3-sheet-row, .m3-sheet-row')).filter(r => r.checkVisibility());
    out.menu = rows.map(r => r.innerText.replace(/\s+/g, ' ').trim());
    const wake = rows.find(r => /^Wake PC/.test(r.innerText.trim()));
    if (wake) wake.click();
  }
  await wait(1500);
  out.toasts = window.__toasts.slice();
  return out;
})()
