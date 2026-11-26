// Stage 2: the Connection sheet shows this phone's own totals (D3); online search also looks in From phone (C6).
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const out = {};
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  for (let i = 0; i < 20 && !$('header-status-dot').className.includes('online'); i++) await wait(500);
  document.querySelector('[data-act="open-profile"]').click(); await wait(500);
  Array.from(document.querySelectorAll('#profile-menu-list .m3-sheet-row')).find(r => /^Connection/.test(r.innerText.trim())).click();
  await wait(1500);
  out.bytes = $('modal-bytes-label').innerText + ': ' + $('modal-bytes-val').innerText;
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(400); }
  $('nav-home').click(); await wait(400);
  const input = $('global-search-input');
  for (const scope of ['folder', 'all']) {
    const chip = document.querySelector(`#search-scope-row .transfer-chip[data-scope="${scope}"]`);
    input.focus(); input.value = 'notes'; input.dispatchEvent(new Event('input', { bubbles: true }));
    await wait(600);
    if (chip) { chip.click(); await wait(300); }
    await wait(4000);
    out[scope] = { where: ($('search-cloud-where') || {}).innerText, count: ($('search-cloud-count') || {}).innerText,
      rows: Array.from(document.querySelectorAll('#search-cloud-results .explorer-item')).slice(0, 4).map(e => e.innerText.replace(/\s+/g, ' ')),
      empty: (document.querySelector('#search-cloud-results .recent-empty-card') || {}).innerText };
  }
  const clear = document.querySelector('[data-act="search-clear"]'); if (clear) clear.click();
  return out;
})()
