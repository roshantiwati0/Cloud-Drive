// Phase 7: phone → "Tab" through the PC. Find the received beach-photo.png on the phone, ⋮ → Send to another device → Tab.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  $('nav-home').click(); await wait(400);
  const input = $('global-search-input');
  input.focus(); input.value = 'beach-photo'; input.dispatchEvent(new Event('input', { bubbles: true }));
  let row = null;
  for (let i = 0; i < 30 && !row; i++) { await wait(500); row = Array.from(document.querySelectorAll('[data-act="search-phone"]')).find(r => /beach-photo\.png/.test(r.innerText)); }
  if (!row) return { error: 'beach-photo.png not found on the phone', phoneRows: Array.from(document.querySelectorAll('[data-act="search-phone"]')).map(r => r.innerText.slice(0, 60)) };
  row.click(); await wait(800);
  const send = Array.from(document.querySelectorAll('#sheet-actions-list .m3-sheet-row')).find(r => /Send to another device/.test(r.innerText));
  if (!send) return { actions: Array.from(document.querySelectorAll('#sheet-actions-list .m3-sheet-row')).map(r => r.innerText.split('\n')[0]) };
  send.click();
  for (let i = 0; i < 20 && !$('choice-sheet').classList.contains('active'); i++) await wait(300);
  const choices = Array.from(document.querySelectorAll('#choice-list .m3-sheet-row')).map(r => r.innerText.replace(/\s+/g, ' ').trim());
  const tab = Array.from(document.querySelectorAll('#choice-list .m3-sheet-row')).find(r => /Tab/.test(r.innerText));
  if (tab) tab.click();
  let rows = [];
  for (let i = 0; i < 30; i++) {
    await wait(1000);
    const t = JSON.parse(window.AndroidBridge.listTransfers());
    rows = (Array.isArray(t) ? t : t.list || []).filter(x => x.type === 'upload' && /beach-photo/.test(x.name || '')).map(x => x.name + ' ' + x.state + ' ' + (x.target || ''));
    if (rows.some(r => / done /.test(r + ' '))) break;
  }
  const clear = document.querySelector('[data-act="search-clear"]'); if (clear) clear.click();
  return { choices, uploads: rows };
})()
