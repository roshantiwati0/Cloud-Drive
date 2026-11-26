// After tapping the listening note: Settings is open at Find my phone; switch it off there.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  await wait(1500);
  const box = document.querySelector('[data-act="find-my-phone"]');
  const out = { settingsOpen: $('settings-modal').classList.contains('active'), toggle: !!box };
  if (box) {
    const r = box.closest('.settings-section').getBoundingClientRect();
    out.sectionOnScreen = r.top >= 0 && r.top < window.innerHeight;
    out.sectionText = box.closest('.settings-section').innerText.replace(/\s+/g, ' ').trim();
    if (box.checked) box.click();
    await wait(1500);
  }
  out.state = JSON.parse(window.AndroidBridge.findMyPhone());
  return out;
})()
