// Stage 5 E3: pair this phone with the test PC through a guest code (__CODE__), then read what the phone shows: the
// device row ("Guest until …") and what Permissions says it may do. Runs in English, ends in Hinglish.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const online = async () => { for (let i = 0; i < 40 && !$('header-status-dot').className.includes('online'); i++) await wait(500); return $('header-status-dot').className.includes('online'); };
  window.CloudSsdI18n.setLang('english');
  const out = {};
  try {
    if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
    document.querySelector('[data-act="open-profile"]').click(); await wait(500);
    document.querySelector('#profile-menu-list [data-i="1"]').click(); await wait(800);   // Add device
    $('pair-address').value = '127.0.0.1:8091';
    $('pair-code').value = '__CODE__';
    $('pair-submit-btn').click();
    await wait(2000);
    out.paired = await online();
    await wait(3000);
    out.title = $('home-conn-title').innerText;
    out.guestUntil = window.__lastStatus ? window.__lastStatus.guestUntil : null;
    // The device row.
    document.querySelector('[data-act="open-profile"]').click(); await wait(600);
    document.querySelector('#profile-menu-list [data-i="0"]').click(); await wait(2000);
    const pc = localStorage.getItem('cloudssd_pc_id');
    const row = Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc);
    out.row = row ? row.innerText.replace(/\s+/g, ' ').trim() : 'no row';
    // Permissions sheet: what it may do.
    if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
    document.querySelector('[data-act="open-profile"]').click(); await wait(600);
    document.querySelector('#profile-menu-list [data-i="3"]').click(); await wait(2500);
    out.permissions = Array.from(document.querySelectorAll('#perm-list > *')).map(e => e.innerText.replace(/\s+/g, ' ').trim()).filter(Boolean).slice(0, 12);
    if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  } finally {
    window.CloudSsdI18n.setLang('hinglish');
  }
  return out;
})()
