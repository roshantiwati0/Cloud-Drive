// Stage 1 B1: Settings → Find my phone on (the toggle), then read what the section says.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  document.querySelector('[data-act="open-profile"]').click(); await wait(500);
  Array.from(document.querySelectorAll('#profile-menu-list .m3-sheet-row')).find(r => /^Settings/.test(r.innerText.trim())).click();
  await wait(2500);
  const box = document.querySelector('[data-act="find-my-phone"]');
  if (!box) return { error: 'no Find my phone toggle', sections: Array.from(document.querySelectorAll('#settings-body .sheet-subtitle')).map(s => s.innerText) };
  const before = box.closest('.settings-section').innerText.replace(/\s+/g, ' ').trim();
  if (!box.checked) box.click();
  await wait(1500);
  return { before, state: JSON.parse(window.AndroidBridge.findMyPhone()) };
})()
