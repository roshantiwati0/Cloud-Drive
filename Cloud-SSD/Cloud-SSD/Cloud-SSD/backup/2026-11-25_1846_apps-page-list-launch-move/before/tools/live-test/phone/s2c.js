// D3: a real download (Save to phone of laptop-notes.txt, 26 B), then the Connection sheet.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const out = {};
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  const row = Array.from(document.querySelectorAll('#ssd-items-container .file-item')).find(e => /laptop-notes\.txt/.test(e.innerText));
  if (!row) return 'laptop-notes.txt not on screen';
  row.querySelector('[data-act="ssd-more"]').click(); await wait(700);
  Array.from(document.querySelectorAll('#sheet-actions-list .m3-sheet-row')).find(r => /^Save to phone/.test(r.innerText)).click();
  for (let i = 0; i < 30; i++) {
    const t = JSON.parse(window.AndroidBridge.listTransfers()).filter(x => x.name === 'laptop-notes.txt' && x.type !== 'upload').pop();
    if (t && (t.state === 'done' || t.state === 'failed')) { out.download = t.state; break; }
    await wait(500);
  }
  document.querySelector('[data-act="open-profile"]').click(); await wait(500);
  Array.from(document.querySelectorAll('#profile-menu-list .m3-sheet-row')).find(r => /^Connection/.test(r.innerText.trim())).click();
  await wait(5000);
  out.bytes = $('modal-bytes-label').innerText + ': ' + $('modal-bytes-val').innerText;
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  return out;
})()
