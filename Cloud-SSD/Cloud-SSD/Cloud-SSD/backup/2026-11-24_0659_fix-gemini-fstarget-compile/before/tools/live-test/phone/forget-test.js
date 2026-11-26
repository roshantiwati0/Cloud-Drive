// Cleanup: forget the test PC (active) from its device menu; the real PC is in use again.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const pc = '__CODE__';   // the test PC's id (lib.ps1: PcId)
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(400); }
  $('nav-home').click();
  await wait(400);
  $('nav-ssd').click();
  await wait(1000);
  if ($('ssd-devices').style.display === 'none') { document.querySelector('[data-act="ssd-devices"]').click(); await wait(1000); }
  const row = Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc);
  if (!row) return 'not saved';
  row.querySelector('[data-act="device-more"]').click();
  await wait(700);
  Array.from(document.querySelectorAll('.m3-sheet-row, button')).find(e => e.checkVisibility() && /^Forget/.test(e.innerText.trim())).click();
  await wait(700);
  Array.from(document.querySelectorAll('button')).find(b => b.checkVisibility() && /^Forget$/.test(b.innerText.trim())).click();
  await wait(6000);
  return { pcs: JSON.parse(window.AndroidBridge.pcList()).map(d => d.id + ' active=' + d.active), kept: Object.keys(JSON.parse(window.AndroidBridge.offlineState(pc)).files), state: $('header-status-dot').className, sub: $('home-conn-sub').innerText };
})()

