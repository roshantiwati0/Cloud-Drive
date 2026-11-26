// Stage 5 E5: Test Cloud → the "Docs" folder's menu → "Keep in sync with a phone folder" (Android's folder picker opens).
// Runs in English; the language goes back to Hinglish in s5i.js.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  window.CloudSsdI18n.setLang('english');
  const out = {};
  for (let i = 0; i < 40 && !$('header-status-dot').className.includes('online'); i++) await wait(500);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  $('nav-home').click(); await wait(300);
  $('nav-ssd').click(); await wait(1500);
  const pc = localStorage.getItem('cloudssd_pc_id');
  if ($('ssd-devices') && $('ssd-devices').style.display !== 'none') {
    Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc).click(); await wait(2500);
  }
  Array.from(document.querySelectorAll('#ssd-drives-strip > *')).find(e => /From phone/.test(e.innerText)).click();
  await wait(2500);
  const folder = Array.from(document.querySelectorAll('#ssd-items-container .folder-item')).find(e => /Trip/.test(e.innerText));
  if (!folder) return { error: "no Trip folder" };
  folder.querySelector('[data-act="fs-folder-more"]').click(); await wait(800);
  const rows = Array.from(document.querySelectorAll('.m3-sheet-row')).filter(r => r.checkVisibility());
  out.menu = rows.map(r => r.innerText.replace(/\s+/g, ' ').trim());
  const sync = rows.find(r => /^Keep in sync with a phone folder/.test(r.innerText.trim()));
  if (sync) sync.click();
  window.__syncEvents = [];
  if (!window.__syncHooked) {
    window.__syncHooked = true;
    const t = $('toast');
    new MutationObserver(() => window.__syncEvents.push(t.textContent)).observe(t, { childList: true, characterData: true, subtree: true });
  }
  return out;
})()
