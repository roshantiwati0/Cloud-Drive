// What the Cloud SSD tab shows now (no clicks).
(() => {
  const $ = (id) => document.getElementById(id);
  const pc = localStorage.getItem('cloudssd_pc_id');
  return {
    state: $('home-conn-title').innerText,
    crumbs: $('ssd-breadcrumbs-path').innerText.replace(/\s+/g, ' '),
    banner: $('offline-banner').style.display + ' ' + $('offline-banner-title').innerText,
    places: Array.from(document.querySelectorAll('#ssd-drives-strip > *')).map(e => e.innerText.replace(/\s+/g, ' ').trim()),
    items: Array.from(document.querySelectorAll('#ssd-items-container .explorer-item')).map(e => e.innerText.replace(/\s+/g, ' ').trim()),
    note: (document.querySelector('#ssd-items-container .list-note, #ssd-items-container .recent-empty-card') || {}).innerText,
    kept: Object.keys(JSON.parse(window.AndroidBridge.offlineState(pc)).files || {})
  };
})()
