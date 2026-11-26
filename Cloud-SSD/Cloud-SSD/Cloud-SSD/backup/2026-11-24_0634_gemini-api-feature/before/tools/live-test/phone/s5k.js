// Stage 5 E6: what Find my phone says about the lock-screen ring (the bridge, and the section in English).
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const out = { fmp: JSON.parse(window.AndroidBridge.findMyPhone()) };
  window.CloudSsdI18n.setLang('english');
  try {
    if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
    document.querySelector('[data-act="open-profile"]').click(); await wait(600);
    document.querySelector('#profile-menu-list .m3-sheet-row:last-child').click(); await wait(2000);
    const box = document.querySelector('[data-act="find-my-phone"]');
    out.section = box ? box.closest('.settings-section').innerText.replace(/\s+/g, ' ').trim() : 'none';
    if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  } finally { window.CloudSsdI18n.setLang('hinglish'); }
  return out;
})()
