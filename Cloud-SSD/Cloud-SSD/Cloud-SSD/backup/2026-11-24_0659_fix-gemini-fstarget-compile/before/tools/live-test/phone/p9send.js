// Profile → Send clipboard to PC.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  document.querySelector('[data-act="open-profile"]').click();
  await wait(500);
  const row = Array.from(document.querySelectorAll('#profile-menu-list .m3-sheet-row')).find(r => /Send clipboard to PC/.test(r.innerText));
  if (!row) return 'no menu item';
  const label = row.innerText.replace(/\s+/g, ' ').trim();
  row.click();
  await wait(2500);
  return label;
})()
