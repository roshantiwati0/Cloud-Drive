// Stage 5 E5: __CODE__ = now|settings|end. "now": Sync now on the (only) pair and wait for its result; "settings":
// the Synced folders section as shown; "end": back to Hinglish.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const native = window.AndroidBridge;
  const what = '__CODE__';
  const pairs = () => JSON.parse(native.folderSyncs() || '[]');
  const out = {};
  if (what === 'now') {
    const p = pairs()[0];
    const before = p.last ? p.last.at : 0;
    window.__syncEvents = [];
    native.folderSyncNow(p.id);
    for (let i = 0; i < 60; i++) { await wait(1000); const l = pairs()[0].last; if (l && l.at !== before) { out.last = l; break; } }
    await wait(1200);
    out.toasts = (window.__syncEvents || []).filter((t, i, a) => t && a.indexOf(t) === i);
  } else if (what === 'settings') {
    if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
    document.querySelector('[data-act="open-profile"]').click(); await wait(600);
    document.querySelector('#profile-menu-list .m3-sheet-row:last-child').click(); await wait(2000);
    const btn = document.querySelector('[data-act="sync-now"]');
    out.section = btn ? btn.closest('.settings-section').innerText.replace(/\s+/g, ' ').trim() : 'no Synced folders section';
    if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  } else {
    window.CloudSsdI18n.setLang('hinglish');
    out.lang = window.CloudSsdI18n.lang();
  }
  return out;
})()
