// Stage 5 E4: Settings → App lock. __CODE__ = on|off|read. "on" ticks the box (right away); "off" goes through the bridge.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const what = '__CODE__';
  const out = {};
  if (what === 'off') {
    out.ok = window.AndroidBridge.setAppLock(false, 0);
    out.state = JSON.parse(window.AndroidBridge.appLock());
    return out;
  }
  window.CloudSsdI18n.setLang('english');
  try {
    if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
    document.querySelector('[data-act="open-profile"]').click(); await wait(600);
    document.querySelector('#profile-menu-list .m3-sheet-row:last-child').click(); await wait(2000);
    const box = document.querySelector('[data-act="app-lock"]');
    out.before = box ? box.closest('.settings-section').innerText.replace(/\s+/g, ' ').trim() : 'no App lock section';
    if (what === 'on' && box && !box.checked) { box.click(); await wait(1500); }
    const now = document.querySelector('[data-act="app-lock"]');
    out.after = now ? now.closest('.settings-section').innerText.replace(/\s+/g, ' ').trim() : '';
    out.state = JSON.parse(window.AndroidBridge.appLock());
    if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  } finally {
    window.CloudSsdI18n.setLang('hinglish');
  }
  return out;
})()
