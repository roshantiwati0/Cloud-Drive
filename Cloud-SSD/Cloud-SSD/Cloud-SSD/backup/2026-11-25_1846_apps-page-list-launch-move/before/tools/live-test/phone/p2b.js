// Phase 2b: "last time" data. Online: open From phone and Trip (saved). Disconnect with the app's button: the saved
// lists, the offline banner, greyed PC-only actions, offline search. Then drop a name from the saved list, reconnect,
// and it must come back with a NEW badge.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const errors = [];
  window.addEventListener('error', e => errors.push(String(e.message)));
  const isOnline = () => $('header-status-dot').className.includes('online');
  const items = () => Array.from(document.querySelectorAll('#ssd-items-container .explorer-item')).map(e => e.innerText.replace(/\s+/g, ' ').trim());
  const waitItems = async () => { for (let i = 0; i < 20 && document.querySelectorAll('#ssd-items-container .explorer-item').length === 0; i++) await wait(500); };
  const pc = localStorage.getItem('cloudssd_pc_id');
  const openFromPhone = async () => {
    $('nav-home').click(); await wait(300);
    $('nav-ssd').click(); await wait(1200);
    if ($('ssd-devices').style.display !== 'none') { Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc).click(); await wait(2000); }
    const fp = Array.from(document.querySelectorAll('#ssd-drives-strip > *')).find(e => /From phone/.test(e.innerText));
    if (fp) fp.click();
    await wait(1500); await waitItems();
  };
  const out = {};
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  for (let i = 0; i < 30 && !isOnline(); i++) await wait(500);
  await openFromPhone();
  Array.from(document.querySelectorAll('#ssd-items-container .folder-item')).find(e => /Trip/.test(e.innerText)).click();
  await wait(1500); await waitItems();
  out.onlineTrip = items();
  // What the phone saved (IndexedDB).
  const db = await new Promise(res => { const r = indexedDB.open('cloudssd', 1); r.onsuccess = () => res(r.result); });
  const all = await new Promise(res => { const g = db.transaction(['lists']).objectStore('lists').getAll(); g.onsuccess = () => res(g.result); });
  const mine = all.filter(r => String(r.pc || '') === pc || (r.key && String(r.key).startsWith(pc)));
  out.savedLists = (mine.length ? mine : all).map(r => `${r.path}: ${r.folders.length} folders, ${r.files.length} files`).slice(0, 6);
  // Disconnect.
  $('nav-home').click(); await wait(400);
  const btn = $('home-conn-toggle-btn');
  out.button = btn.textContent.trim();
  btn.click(); await wait(1500);
  out.afterDisconnect = $('home-conn-title').innerText;
  await openFromPhone();
  out.offline = { banner: $('offline-banner').style.display, title: $('offline-banner-title').innerText, sub: $('offline-banner-sub').innerText, crumbs: $('ssd-breadcrumbs-path').innerText.replace(/\s+/g, ' '), items: items() };
  Array.from(document.querySelectorAll('#ssd-items-container .folder-item')).find(e => /Trip/.test(e.innerText)).click();
  await wait(1500); await waitItems();
  out.offlineTrip = items();
  // A file's menu: what needs the PC is greyed.
  const notes = Array.from(document.querySelectorAll('#ssd-items-container .file-item')).find(e => /notes\.txt/.test(e.innerText));
  notes.querySelector('[data-act="ssd-more"]').click(); await wait(800);
  out.fileMenu = Array.from(document.querySelectorAll('#sheet-actions-list .m3-sheet-row')).map(r => (r.classList.contains('needs-pc') ? '[grey] ' : '') + r.innerText.replace(/\s+/g, ' ').trim().slice(0, 50));
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(400); }
  // Offline search over the saved lists.
  const input = $('global-search-input');
  input.focus(); input.value = 'notes'; input.dispatchEvent(new Event('input', { bubbles: true }));
  await wait(4000);
  out.search = { count: ($('search-cloud-count') || {}).innerText, where: ($('search-cloud-where') || {}).innerText,
    first: Array.from(document.querySelectorAll('#search-cloud-results .explorer-item')).slice(0, 4).map(e => e.innerText.replace(/\s+/g, ' ')) };
  const clear = document.querySelector('[data-act="search-clear"]'); if (clear) clear.click();
  await wait(500);
  // Drop laptop-notes.txt from the saved From phone list, then connect again.
  const fromRec = all.find(r => /uploads$/i.test(r.path) && r.files.some(f => f.name === 'laptop-notes.txt'));
  if (fromRec) {
    fromRec.files = fromRec.files.filter(f => f.name !== 'laptop-notes.txt');
    await new Promise(res => { const t = db.transaction(['lists'], 'readwrite'); t.objectStore('lists').put(fromRec); t.oncomplete = res; t.onerror = res; });
  }
  out.droppedFromSaved = !!fromRec;
  db.close();
  $('nav-home').click(); await wait(400);
  $('home-conn-toggle-btn').click();
  for (let i = 0; i < 30 && !isOnline(); i++) await wait(500);
  out.reconnected = isOnline();
  await openFromPhone();
  await wait(2500);
  out.afterReconnect = { banner: $('offline-banner').style.display, withNew: items().filter(n => /NEW/.test(n)) };
  out.errors = errors;
  return out;
})()
