// After the PC said "always": the sheet goes, and the refused folder loads by itself. Then the Permissions sheet.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  for (let i = 0; i < 30 && $('access-sheet').classList.contains('active'); i++) await wait(500);
  for (let i = 0; i < 20 && document.querySelectorAll('#ssd-items-container .explorer-item').length === 0; i++) await wait(500);
  const out = {
    sheetOpen: $('access-sheet').classList.contains('active'),
    crumbs: $('ssd-breadcrumbs-path').innerText.replace(/\s+/g, ' '),
    items: Array.from(document.querySelectorAll('#ssd-items-container .explorer-item')).map(e => e.innerText.replace(/\s+/g, ' ').trim()).slice(0, 3)
  };
  document.querySelector('[data-act="open-profile"]').click(); await wait(500);
  Array.from(document.querySelectorAll('#profile-menu-list .m3-sheet-row')).find(r => /^Permissions/.test(r.innerText.trim())).click();
  await wait(1500);
  out.permSheet = { open: $('perm-sheet').classList.contains('active'), sub: $('perm-sub').innerText,
    rows: Array.from(document.querySelectorAll('#perm-list .perm-row')).map(r => r.innerText.replace(/\s+/g, ' ').trim()) };
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  return out;
})()
