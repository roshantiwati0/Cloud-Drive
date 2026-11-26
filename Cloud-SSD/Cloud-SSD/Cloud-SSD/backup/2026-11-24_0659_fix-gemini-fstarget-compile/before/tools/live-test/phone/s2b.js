// D3: one upload (the held-test file, 17 B) and one read of a PC file (report-2026.pdf, 25 B), then the Connection sheet.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const out = {};
  window.AndroidBridge.uploadFileByPath('/data/user/0/com.cloudssd.mobile/files/cssd_held_test.txt');
  for (let i = 0; i < 30; i++) {
    const t = JSON.parse(window.AndroidBridge.listTransfers()).filter(x => x.name === 'cssd_held_test.txt').pop();
    if (t && t.state === 'done') { out.upload = 'done'; break; }
    await wait(500);
  }
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  $('nav-home').click(); await wait(300);
  $('nav-ssd').click(); await wait(1200);
  const pc = localStorage.getItem('cloudssd_pc_id');
  if ($('ssd-devices').style.display !== 'none') { Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc).click(); await wait(2000); }
  Array.from(document.querySelectorAll('#ssd-drives-strip > *')).find(e => /From phone/.test(e.innerText)).click();
  for (let i = 0; i < 20 && document.querySelectorAll('#ssd-items-container .explorer-item').length === 0; i++) await wait(500);
  await wait(800);
  const row = Array.from(document.querySelectorAll('#ssd-items-container .file-item')).find(e => /laptop-notes\.txt/.test(e.innerText));
  row.querySelector('[data-act="ssd-more"]').click(); await wait(700);
  Array.from(document.querySelectorAll('#sheet-actions-list .m3-sheet-row')).find(r => /^Read/.test(r.innerText)).click();
  await wait(3000);
  out.read = ($('viewer-text-content') || {}).textContent;
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(600); }
  document.querySelector('[data-act="open-profile"]').click(); await wait(500);
  Array.from(document.querySelectorAll('#profile-menu-list .m3-sheet-row')).find(r => /^Connection/.test(r.innerText.trim())).click();
  await wait(4000);   // the next status brings the new totals
  out.bytes = $('modal-bytes-label').innerText + ': ' + $('modal-bytes-val').innerText;
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  return out;
})()
