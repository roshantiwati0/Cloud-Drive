// Puts a screen up for a screenshot: __CODE__ = home | phone | history.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  const what = '__CODE__';
  if (what === 'home') { $('nav-home').click(); await wait(800); document.querySelector('#tab-home .hw-group-title').scrollIntoView({ block: 'start' }); window.scrollBy(0, -60); }
  if (what === 'phone') { $('nav-browser').click(); await wait(1200); window.scrollTo(0, 0); }
  if (what === 'history') {
    $('nav-home').click(); await wait(500);
    document.querySelector('[data-act="open-profile"]').click(); await wait(600);
    document.querySelector('#profile-menu-list [data-i="4"]').click(); await wait(2000);
  }
  await wait(600);
  return what;
})()
