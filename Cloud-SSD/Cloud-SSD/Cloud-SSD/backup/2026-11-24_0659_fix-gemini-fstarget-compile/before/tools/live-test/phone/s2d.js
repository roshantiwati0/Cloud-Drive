// C1: after "Share → Send to a device…" from another app, the page asks where; pick the device named __CODE__.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  for (let i = 0; i < 30 && !$('choice-sheet').classList.contains('active'); i++) await wait(500);
  const out = {
    open: $('choice-sheet').classList.contains('active'),
    title: ($('choice-title') || {}).innerText,
    choices: Array.from(document.querySelectorAll('#choice-list .m3-sheet-row')).map(r => r.innerText.replace(/\s+/g, ' ').trim())
  };
  const pick = Array.from(document.querySelectorAll('#choice-list .m3-sheet-row')).find(r => r.innerText.split('\n')[0].trim() === '__CODE__');
  if (pick) pick.click();
  let rows = [];
  for (let i = 0; i < 30; i++) {
    await wait(1000);
    const t = JSON.parse(window.AndroidBridge.listTransfers());
    rows = (Array.isArray(t) ? t : t.list || []).filter(x => x.type === 'upload' && /beach-photo/.test(x.name || '')).map(x => x.name + ' ' + x.state + ' ' + (x.target || ''));
    if (rows.some(r => / done /.test(r + ' ') && /share:/.test(r))) break;
  }
  out.uploads = rows.slice(-2);
  return out;
})()
