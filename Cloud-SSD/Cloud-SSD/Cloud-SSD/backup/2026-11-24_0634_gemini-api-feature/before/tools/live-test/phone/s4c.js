// Stage 4: puts the app in a language and on a screen for a screenshot. __CODE__ = "<lang> <screen>",
// screen = home | drives | sheet | settings | back (back: close everything, then set the language).
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const [lang, screen] = '__CODE__'.split(' ');
  const back = async () => { if (window.onAndroidBack) { window.onAndroidBack(); await wait(400); } };
  await back(); await back();
  window.CloudSsdI18n.setLang(lang);
  if (screen === 'home') { $('nav-home').click(); await wait(1500); window.scrollTo(0, 0); }
  if (screen === 'drives' || screen === 'sheet') {
    $('nav-ssd').click(); await wait(1500);
    const pc = localStorage.getItem('cloudssd_pc_id');
    if ($('ssd-devices') && $('ssd-devices').style.display !== 'none') {
      Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc).click(); await wait(2500);
    }
    if (screen === 'sheet') {
      const f = document.querySelector('#ssd-items-container .folder-item'); if (f) { f.click(); await wait(2500); }
      document.querySelector('#ssd-items-container .file-item [data-act="ssd-more"]').click(); await wait(1200);
    }
  }
  if (screen === 'settings') {
    document.querySelector('[data-act="open-profile"]').click(); await wait(700);
    document.querySelector('#profile-menu-list .m3-sheet-row:last-child').click(); await wait(2000);
  }
  if (screen === 'profile') { document.querySelector('[data-act="open-profile"]').click(); await wait(1000); }
  return { lang: window.CloudSsdI18n.lang(), screen };
})()
