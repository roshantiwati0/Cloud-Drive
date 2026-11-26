// PC back: the held upload goes by itself; the kept Trip folder catches up (notes.txt gone, added-later.txt new);
// then single.txt is removed from the Offline files sheet (with its confirm).
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const errors = [];
  window.addEventListener('error', e => errors.push(String(e.message)));
  const out = {};
  const pc = localStorage.getItem('cloudssd_pc_id');
  const t0 = Date.now();
  for (let i = 0; i < 90 && !$('header-status-dot').className.includes('online'); i++) await wait(1000);
  out.backOnlineAfter = Math.round((Date.now() - t0) / 1000) + ' s';
  const states = [];
  for (let i = 0; i < 60; i++) {
    const t = JSON.parse(window.AndroidBridge.listTransfers()).filter(x => x.name === 'cssd_held_test.txt').pop();
    if (t) { states.push(t.state); if (t.state === 'done' || t.state === 'failed') break; }
    await wait(1000);
  }
  out.heldUpload = Array.from(new Set(states));
  let st;
  for (let i = 0; i < 60; i++) {
    st = JSON.parse(window.AndroidBridge.offlineState(pc));
    if (st.files['received:Trip/added-later.txt'] && !st.files['received:Trip/notes.txt']) break;
    await wait(1000);
  }
  out.keptFiles = Object.keys(st.files);
  out.trip = st.pins.filter(p => p.id === 'received:Trip').map(p => `${p.state} ${p.files} files ${p.bytes} B`);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  document.querySelector('[data-act="open-profile"]').click(); await wait(500);
  Array.from(document.querySelectorAll('#profile-menu-list .m3-sheet-row')).find(r => /Offline files/.test(r.innerText)).click();
  await wait(1500);
  const rows = Array.from(document.querySelectorAll('#offline-list .offline-row'));
  out.sheet = rows.map(r => r.innerText.replace(/\s+/g, ' ').trim());
  const single = rows.find(r => /single\.txt/.test(r.innerText));
  single.querySelector('[data-act="offline-remove"]').click(); await wait(700);
  const yes = Array.from(document.querySelectorAll('button')).find(b => b.checkVisibility() && /^Remove$/.test(b.innerText.trim()) && !b.closest('#offline-list'));
  out.confirmShown = !!yes;
  if (yes) yes.click();
  await wait(1500);
  out.sheetAfter = Array.from(document.querySelectorAll('#offline-list .offline-row')).map(r => r.innerText.replace(/\s+/g, ' ').trim());
  out.keptAfterRemove = Object.keys(JSON.parse(window.AndroidBridge.offlineState(pc)).files);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  out.errors = errors;
  return out;
})()
