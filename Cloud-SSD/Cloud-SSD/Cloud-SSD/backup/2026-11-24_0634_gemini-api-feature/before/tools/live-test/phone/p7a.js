// Phase 7: the PC's offer brings up the inbox sheet; Accept; the files download into Download/Cloud-SSD/Received.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const out = {};
  for (let i = 0; i < 60 && !$('inbox-sheet').classList.contains('active'); i++) await wait(500);
  out.open = $('inbox-sheet').classList.contains('active');
  out.sub = $('inbox-sub').innerText;
  out.cards = Array.from(document.querySelectorAll('#inbox-list .inbox-card')).map(c => c.innerText.replace(/\s+/g, ' ').trim());
  const accept = document.querySelector('[data-act="inbox-accept"]');
  if (accept) accept.click();
  await wait(1500);
  out.afterAccept = { open: $('inbox-sheet').classList.contains('active'), cards: document.querySelectorAll('#inbox-list .inbox-card').length };
  const names = ['report-2026.pdf', 'beach-photo.png'];
  let rows = [];
  for (let i = 0; i < 40; i++) {
    const t = JSON.parse(window.AndroidBridge.listTransfers());
    rows = (Array.isArray(t) ? t : t.list || []).filter(x => x.type !== 'upload' && names.includes(x.name)).map(x => `${x.name} ${x.state} ${x.target || ''}`);
    if (rows.length >= 2 && rows.every(r => / (done|failed) /.test(r + ' '))) break;
    await wait(1000);
  }
  out.downloads = rows;
  return out;
})()
