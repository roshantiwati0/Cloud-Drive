// What the Cloud SSD tab shows now (level, device list, drive strip, items).
(async () => {
  const $ = (id) => document.getElementById(id);
  return {
    lang: window.CloudSsdI18n.lang(),
    state: $('home-conn-title').innerText,
    devicesShown: $('ssd-devices') ? $('ssd-devices').style.display : 'none?',
    strip: Array.from(document.querySelectorAll('#ssd-drives-strip > *')).map(e => e.innerText.replace(/\s+/g, ' ').trim()),
    items: Array.from(document.querySelectorAll('#ssd-items-container .item-name')).map(e => e.innerText).slice(0, 10),
    crumbs: ($('ssd-breadcrumbs-path') || {}).innerText
  };
})()
