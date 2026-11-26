// Phase 2a: the device list (two PCs), opening the test PC, its places and its "From phone" folder; switching to the
// home PC and back; the profile menu.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const errors = [];
  window.addEventListener('error', e => errors.push(String(e.message)));
  const online = async () => { for (let i = 0; i < 40 && !$('header-status-dot').className.includes('online'); i++) await wait(500); return $('header-status-dot').className.includes('online'); };
  const rows = () => Array.from(document.querySelectorAll('#ssd-device-list .device-row'));
  const out = {};
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  const testId = localStorage.getItem('cloudssd_pc_id');
  out.activeIsTest = testId;
  $('nav-home').click(); await wait(300);
  $('nav-ssd').click(); await wait(1500);
  if ($('ssd-devices').style.display === 'none') { document.querySelector('[data-act="ssd-devices"]').click(); await wait(1000); }
  out.count = $('ssd-devices-count').innerText;
  out.list = rows().map(r => r.innerText.replace(/\s+/g, ' ').trim());
  // Open the test PC.
  rows().find(r => r.dataset.id === testId).click();
  await wait(2500);
  out.header = $('ssd-device-name').innerText + ' | ' + $('ssd-device-sub').innerText;
  out.places = Array.from(document.querySelectorAll('#ssd-drives-strip > *')).map(e => e.innerText.replace(/\s+/g, ' ').trim());
  const fromPhone = Array.from(document.querySelectorAll('#ssd-drives-strip > *')).find(e => /From phone/.test(e.innerText));
  if (fromPhone) fromPhone.click();
  for (let i = 0; i < 20 && document.querySelectorAll('#ssd-items-container .explorer-item').length === 0; i++) await wait(500);
  out.crumbs = $('ssd-breadcrumbs-path').innerText.replace(/\s+/g, ' ');
  out.items = Array.from(document.querySelectorAll('#ssd-items-container .explorer-item')).map(e => e.innerText.replace(/\s+/g, ' ').trim());
  // Switch to the home PC, then back.
  document.querySelector('[data-act="ssd-devices"]').click(); await wait(800);
  const home = rows().find(r => r.dataset.id !== testId);
  home.click(); await wait(1000);
  out.homeOnline = await online();
  out.afterSwitch = localStorage.getItem('cloudssd_pc_id') + ' | ' + $('home-conn-sub').innerText;
  document.querySelector('[data-act="ssd-devices"]') && document.querySelector('[data-act="ssd-devices"]').click(); await wait(800);
  rows().find(r => r.dataset.id === testId).click(); await wait(1000);
  out.backOnline = await online();
  out.afterBack = localStorage.getItem('cloudssd_pc_id') + ' | ' + $('home-conn-sub').innerText;
  // The profile menu.
  document.querySelector('[data-act="open-profile"]').click(); await wait(600);
  out.profile = Array.from(document.querySelectorAll('#profile-menu-list .m3-sheet-row')).map(r => r.innerText.split('\n')[0]);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  out.errors = errors;
  return out;
})()
