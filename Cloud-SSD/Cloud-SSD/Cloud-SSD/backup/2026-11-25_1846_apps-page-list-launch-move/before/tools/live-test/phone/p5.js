// Phase 5: how the phone reaches the test PC now; the Connection sheet.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  for (let i = 0; i < 30 && !$('header-status-dot').className.includes('online'); i++) await wait(500);
  const out = {
    online: $('header-status-dot').className,
    server: localStorage.getItem('cloudssd_server_url'),
    direct: JSON.parse(localStorage.getItem('cloudssd_direct_urls') || '[]'),
    homeSub: $('home-conn-sub').innerText,
    network: JSON.parse(window.AndroidBridge.getAppInfo()).network
  };
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  document.querySelector('[data-act="open-profile"]').click(); await wait(500);
  Array.from(document.querySelectorAll('#profile-menu-list .m3-sheet-row')).find(r => /^Connection/.test(r.innerText.trim())).click();
  await wait(1500);
  const sheet = Array.from(document.querySelectorAll('.m3-bottom-sheet.active, .sheet.active, [id$="-sheet"].active')).map(s => s.id + ': ' + s.innerText.replace(/\s+/g, ' ').trim().slice(0, 400));
  out.connectionSheet = sheet;
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  return out;
})()
