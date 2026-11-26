// Stage 5 E5: Settings → Synced folders → "Stop syncing" → confirm. Runs in English (s5i.js "end" goes back).
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  window.CloudSsdI18n.setLang('english');
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  document.querySelector('[data-act="open-profile"]').click(); await wait(600);
  document.querySelector('#profile-menu-list .m3-sheet-row:last-child').click(); await wait(2000);
  const stop = document.querySelector('[data-act="sync-stop"]');
  if (!stop) return { error: 'no Stop syncing' };
  stop.click(); await wait(800);
  const out = { confirm: $('confirm-yes-btn') ? $('confirm-yes-btn').innerText : null };
  $('confirm-yes-btn').click(); await wait(1500);
  out.pairs = JSON.parse(window.AndroidBridge.folderSyncs() || '[]').length;
  out.sectionGone = !document.querySelector('[data-act="sync-now"]');
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  return out;
})()
