// Phase 4 online: keep the Trip folder (⋮ menu) and single.txt (file sheet) offline, wait for the copies, see the badges.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const errors = [];
  window.addEventListener('error', e => errors.push(String(e.message)));
  const out = {};
  const pc = localStorage.getItem('cloudssd_pc_id');
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  $('nav-home').click(); await wait(300);
  $('nav-ssd').click(); await wait(1200);
  if ($('ssd-devices').style.display !== 'none') { Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc).click(); await wait(2000); }
  Array.from(document.querySelectorAll('#ssd-drives-strip > *')).find(e => /From phone/.test(e.innerText)).click();
  for (let i = 0; i < 20 && document.querySelectorAll('#ssd-items-container .explorer-item').length === 0; i++) await wait(500);
  await wait(800);
  const trip = Array.from(document.querySelectorAll('#ssd-items-container .folder-item')).find(e => /Trip/.test(e.innerText));
  trip.querySelector('[data-act="fs-folder-more"]').click(); await wait(700);
  out.folderMenu = Array.from(document.querySelectorAll('[data-act="fs-row"]')).map(r => r.innerText.replace(/\s+/g, ' ').trim().slice(0, 40));
  Array.from(document.querySelectorAll('[data-act="fs-row"]')).find(r => /Keep offline/.test(r.innerText)).click();
  await wait(1000);
  const single = Array.from(document.querySelectorAll('#ssd-items-container .file-item')).find(e => /single\.txt/.test(e.innerText));
  single.querySelector('[data-act="ssd-more"]').click(); await wait(700);
  Array.from(document.querySelectorAll('#sheet-actions-list .m3-sheet-row')).find(r => /Keep offline/.test(r.innerText)).click();
  await wait(1000);
  let st = {};
  for (let i = 0; i < 60; i++) {
    st = JSON.parse(window.AndroidBridge.offlineState(pc));
    if ((st.pins || []).length >= 2 && st.pins.every(p => p.state === 'done' || p.state === 'error')) break;
    await wait(1000);
  }
  out.pins = (st.pins || []).map(p => `${p.name} ${p.state} ${p.files} files ${p.bytes} B ${p.error || ''}`);
  out.files = Object.keys(st.files || {});
  await wait(800);
  out.items = Array.from(document.querySelectorAll('#ssd-items-container .explorer-item')).map(e => e.innerText.replace(/\s+/g, ' ').trim());
  out.errors = errors;
  return out;
})()
