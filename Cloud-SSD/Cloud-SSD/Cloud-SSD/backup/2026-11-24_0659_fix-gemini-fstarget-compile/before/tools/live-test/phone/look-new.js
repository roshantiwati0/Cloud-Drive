// Home's System & Storage (RAM part, Storage part), the Phone tab (kinds of file, the slim row), and the transfer
// history grouped by kind of file, then one kind only. Runs in English.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const txt = (el) => el ? el.innerText.replace(/\s+/g, ' ').trim() : null;
  const out = {};
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  $('nav-home').click(); await wait(1200);
  out.homeGroups = Array.from(document.querySelectorAll('#tab-home .hw-group-title')).map(g => {
    const grid = g.nextElementSibling;
    return g.innerText + ': ' + Array.from(grid.querySelectorAll('.hw-widget-label')).map(l => l.innerText).join(', ');
  });
  $('nav-browser').click(); await wait(1500);
  out.phoneCards = Array.from(document.querySelectorAll('#phone-home .cat-grid-card .cat-grid-name')).map(e => e.innerText);
  out.phoneRow = Array.from(document.querySelectorAll('.phone-quick-btn')).map(txt);
  out.legendShown = $('browser-storage-legend').checkVisibility();
  $('nav-home').click(); await wait(600);
  document.querySelector('[data-act="open-profile"]').click(); await wait(600);
  document.querySelector('#profile-menu-list [data-i="4"]').click(); await wait(2500);   // Shared & received
  const list = $('transfers-modal-list');
  out.kindChips = Array.from(document.querySelectorAll('#transfers-kind-row .transfer-chip')).map(c => c.innerText + (c.classList.contains('active') ? ' *' : ''));
  out.groups = Array.from(list.querySelectorAll('.history-group-title')).map(txt);
  const photos = document.querySelector('#transfers-kind-row [data-kind="documents"]');
  photos.click(); await wait(600);
  out.documentsOnly = { groups: list.querySelectorAll('.history-group-title').length, rows: Array.from(list.querySelectorAll('.tc-name')).map(e => e.innerText).slice(0, 6) };
  document.querySelector('#transfers-kind-row [data-kind="all"]').click(); await wait(300);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  return out;
})()
