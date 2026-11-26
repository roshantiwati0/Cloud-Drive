// Phase 6: browsing was taken away on the PC. Opening From phone brings up "Ask the PC"; tap Ask; return what it says.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const pc = localStorage.getItem('cloudssd_pc_id');
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  $('nav-home').click(); await wait(300);
  $('nav-ssd').click(); await wait(1200);
  if ($('ssd-devices').style.display !== 'none') { Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc).click(); await wait(2000); }
  // Leave and re-enter the place so the PC is asked again.
  Array.from(document.querySelectorAll('#ssd-drives-strip > *')).find(e => /From phone/.test(e.innerText)).click();
  for (let i = 0; i < 20 && !$('access-sheet').classList.contains('active'); i++) await wait(500);
  const out = {
    sheet: $('access-sheet').classList.contains('active'),
    title: $('access-title').innerText, text: $('access-text').innerText,
    list: Array.from(document.querySelectorAll('#access-list .access-item')).map(e => e.innerText.trim()),
    actions: Array.from(document.querySelectorAll('#access-actions button')).map(b => b.innerText)
  };
  const ask = document.querySelector('[data-act="access-ask"]');
  if (ask) { ask.click(); await wait(1500); out.status = $('access-status').innerText; }
  return out;
})()
