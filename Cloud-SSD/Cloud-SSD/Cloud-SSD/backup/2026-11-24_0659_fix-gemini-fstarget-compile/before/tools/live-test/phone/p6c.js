// Deny path in one go: Permissions → Ask (stream) → Ask the PC; the PC says no (cmd.txt) while this waits.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  document.querySelector('[data-act="open-profile"]').click();
  await wait(500);
  Array.from(document.querySelectorAll('#profile-menu-list .m3-sheet-row')).find(r => /^Permissions/.test(r.innerText.trim())).click();
  for (let i = 0; i < 10 && !document.querySelector('[data-act="perm-ask"][data-c="stream"]'); i++) await wait(300);
  document.querySelector('[data-act="perm-ask"][data-c="stream"]').click();
  for (let i = 0; i < 10 && !document.querySelector('[data-act="access-ask"]'); i++) await wait(100);
  document.querySelector('[data-act="access-ask"]').click();
  const seen = [];
  for (let i = 0; i < 60; i++) {
    const s = $('access-status').innerText;
    if (s && !seen.some(x => x.slice(0, 20) === s.slice(0, 20))) seen.push(s);
    if (!$('access-sheet').classList.contains('active')) break;
    await wait(500);
  }
  return { seen, closed: !$('access-sheet').classList.contains('active') };
})()
