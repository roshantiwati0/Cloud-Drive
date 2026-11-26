// F5 re-test: offline search over the saved lists names the place ("From phone › Trip"), not the Windows path.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  $('nav-home').click(); await wait(400);
  $('home-conn-toggle-btn').click(); await wait(1500);
  const input = $('global-search-input');
  input.focus(); input.value = 'notes'; input.dispatchEvent(new Event('input', { bubbles: true }));
  await wait(4000);
  const out = { where: ($('search-cloud-where') || {}).innerText, rows: Array.from(document.querySelectorAll('#search-cloud-results .explorer-item')).slice(0, 4).map(e => e.innerText.replace(/\s+/g, ' ')) };
  const clear = document.querySelector('[data-act="search-clear"]'); if (clear) clear.click();
  await wait(300);
  $('nav-home').click(); await wait(300);
  $('home-conn-toggle-btn').click();
  for (let i = 0; i < 30 && !$('header-status-dot').className.includes('online'); i++) await wait(500);
  out.reconnected = $('header-status-dot').className.includes('online');
  return out;
})()
