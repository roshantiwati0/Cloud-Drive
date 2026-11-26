// Stage 5 E2: delete "e2-test-note.txt" in From phone from the phone's UI (menu → Delete → confirm). Runs in English.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  window.CloudSsdI18n.setLang('english');
  const out = {};
  try {
    for (let i = 0; i < 40 && !$('header-status-dot').className.includes('online'); i++) await wait(1000);
    if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
    $('nav-home').click(); await wait(300);
    $('nav-ssd').click(); await wait(1500);
    const pc = localStorage.getItem('cloudssd_pc_id');
    if ($('ssd-devices') && $('ssd-devices').style.display !== 'none') {
      Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc).click(); await wait(2500);
    }
    Array.from(document.querySelectorAll('#ssd-drives-strip > *')).find(e => /From phone/.test(e.innerText)).click();
    await wait(2500);
    const row = Array.from(document.querySelectorAll('#ssd-items-container .file-item')).find(e => /e2-test-note\.txt/.test(e.innerText));
    if (!row) return { error: 'file not listed', list: Array.from(document.querySelectorAll('#ssd-items-container .item-name')).map(e => e.innerText) };
    row.querySelector('[data-act="ssd-more"]').click(); await wait(800);
    const del = Array.from(document.querySelectorAll('#sheet-actions-list .m3-sheet-row')).find(r => /^Delete/.test(r.innerText.trim()));
    out.deleteSub = del ? del.innerText.replace(/\s+/g, ' ').trim() : 'no Delete';
    del.click(); await wait(800);
    out.confirm = ($('confirm-title') || {}).innerText + ' | ' + (($('confirm-text') || $('confirm-message') || {}).innerText || '');
    $('confirm-yes-btn').click(); await wait(3000);
    out.toast = $('toast').textContent;
    out.stillListed = Array.from(document.querySelectorAll('#ssd-items-container .item-name')).some(e => /e2-test-note/.test(e.innerText));
  } finally {
    window.CloudSsdI18n.setLang('hinglish');
  }
  return out;
})()
