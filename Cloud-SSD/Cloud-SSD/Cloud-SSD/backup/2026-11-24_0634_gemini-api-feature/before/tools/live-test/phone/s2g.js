// D2 with the PC away: the kept beach-photo.png can be saved to the phone and shared (not greyed); __CODE__ = save|share.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const out = {};
  const pc = localStorage.getItem('cloudssd_pc_id');
  for (let i = 0; i < 60 && $('header-status-dot').className.includes('online'); i++) await wait(1000);
  out.state = $('home-conn-title').innerText;
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  $('nav-home').click(); await wait(300);
  $('nav-ssd').click(); await wait(1200);
  if ($('ssd-devices').style.display !== 'none') { Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc).click(); await wait(2000); }
  Array.from(document.querySelectorAll('#ssd-drives-strip > *')).find(e => /From phone/.test(e.innerText)).click();
  await wait(2000);
  Array.from(document.querySelectorAll('#ssd-items-container .folder-item')).find(e => /Trip/.test(e.innerText)).click();
  await wait(2000);
  const row = Array.from(document.querySelectorAll('#ssd-items-container .file-item')).find(e => /beach-photo\.png/.test(e.innerText));
  row.querySelector('[data-act="ssd-more"]').click(); await wait(800);
  out.sheet = Array.from(document.querySelectorAll('#sheet-actions-list .m3-sheet-row')).map(r => (r.classList.contains('needs-pc') ? '[grey] ' : '') + r.innerText.split('\n')[0]);
  const want = '__CODE__' === 'share' ? /^Share/ : /^Save to phone/;
  const act = Array.from(document.querySelectorAll('#sheet-actions-list .m3-sheet-row')).find(r => want.test(r.innerText.trim()));
  out.greyed = act ? act.classList.contains('needs-pc') : 'missing';
  if (act) act.click();
  await wait(2500);
  return out;
})()
