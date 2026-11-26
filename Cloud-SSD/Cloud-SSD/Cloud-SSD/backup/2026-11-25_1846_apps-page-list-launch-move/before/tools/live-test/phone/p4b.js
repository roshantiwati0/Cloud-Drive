// Phase 4 + 2c with the test PC switched off: the kept Trip folder opens; its photo, text and PDF open from the phone;
// a file sent now waits ("held") instead of failing.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const errors = [];
  window.addEventListener('error', e => errors.push(String(e.message)));
  const out = {};
  const pc = localStorage.getItem('cloudssd_pc_id');
  for (let i = 0; i < 60 && $('header-status-dot').className.includes('online'); i++) await wait(1000);
  out.state = $('header-status-dot').className + ' | ' + $('home-conn-title').innerText;
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  $('nav-home').click(); await wait(300);
  $('nav-ssd').click(); await wait(1200);
  if ($('ssd-devices').style.display !== 'none') {
    out.row = Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc).innerText.replace(/\s+/g, ' ').trim();
    Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc).click(); await wait(2000);
  }
  Array.from(document.querySelectorAll('#ssd-drives-strip > *')).find(e => /From phone/.test(e.innerText)).click();
  await wait(2500);
  out.banner = $('offline-banner').innerText.replace(/\s+/g, ' ');
  Array.from(document.querySelectorAll('#ssd-items-container .folder-item')).find(e => /Trip/.test(e.innerText)).click();
  await wait(2500);
  out.trip = Array.from(document.querySelectorAll('#ssd-items-container .explorer-item')).map(e => e.innerText.replace(/\s+/g, ' ').trim());
  const fileRow = (name) => Array.from(document.querySelectorAll('#ssd-items-container .file-item')).find(e => e.innerText.includes(name));
  const sheetRow = (re) => Array.from(document.querySelectorAll('#sheet-actions-list .m3-sheet-row')).find(r => re.test(r.innerText));
  // Photo.
  fileRow('beach-photo.png').querySelector('[data-act="ssd-more"]').click(); await wait(700);
  out.photoSheet = Array.from(document.querySelectorAll('#sheet-actions-list .m3-sheet-row')).map(r => (r.classList.contains('needs-pc') ? '[grey] ' : '') + r.innerText.split('\n')[0]);
  sheetRow(/^View/).click(); await wait(2500);
  const img = $('viewer-image-elem');
  out.photo = { loaded: img.complete && img.naturalWidth > 0, w: img.naturalWidth, src: img.src.slice(0, 40) };
  if (window.onAndroidBack) window.onAndroidBack(); await wait(800);
  // Text.
  fileRow('notes.txt').querySelector('[data-act="ssd-more"]').click(); await wait(700);
  sheetRow(/^Read/).click(); await wait(2500);
  out.text = $('viewer-text-content').textContent.trim();
  if (window.onAndroidBack) window.onAndroidBack(); await wait(800);
  // PDF.
  fileRow('guide.pdf').querySelector('[data-act="ssd-more"]').click(); await wait(700);
  sheetRow(/^Read/).click();
  for (let i = 0; i < 20 && !document.querySelector('#pdf-pages img'); i++) await wait(500);
  await wait(1500);
  const p = document.querySelector('#pdf-pages img');
  out.pdf = { status: $('pdf-status').innerText, pages: document.querySelectorAll('#pdf-pages img').length, loaded: !!(p && p.complete && p.naturalWidth > 0) };
  if (window.onAndroidBack) window.onAndroidBack(); await wait(800);
  // A file that is not kept: its Read is greyed.
  const back = document.querySelector('[data-act="ssd-up"]') || null;
  out.nonKept = 'see list';
  // Held upload.
  window.AndroidBridge.uploadFileByPath('/data/user/0/com.cloudssd.mobile/files/cssd_held_test.txt');
  const states = [];
  let last = null;
  for (let i = 0; i < 30; i++) {
    await wait(1000);
    const t = JSON.parse(window.AndroidBridge.listTransfers()).filter(x => x.name === 'cssd_held_test.txt').pop();
    if (t) { states.push(t.state); last = t; if (t.state === 'held' || t.state === 'failed') break; }
  }
  out.upload = { states: Array.from(new Set(states)), last: last && last.state, error: last && last.error };
  out.errors = errors;
  return out;
})()
