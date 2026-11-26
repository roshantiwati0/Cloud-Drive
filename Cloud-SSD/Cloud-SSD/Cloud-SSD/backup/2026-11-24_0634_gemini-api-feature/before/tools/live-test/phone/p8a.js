// Wait for the "PC wants to see this phone's files" sheet.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  for (let i = 0; i < 60 && !$('peer-sheet').classList.contains('active'); i++) await wait(500);
  return {
    open: $('peer-sheet').classList.contains('active'),
    title: $('peer-title').innerText,
    scopes: Array.from(document.querySelectorAll('[data-act="peer-scope"]')).map(b => (b.classList.contains('on') ? '[x] ' : '[ ] ') + b.innerText.replace(/\s+/g, ' ').trim()),
    state: JSON.parse(window.AndroidBridge.peerState(localStorage.getItem('cloudssd_pc_id')))
  };
})()
