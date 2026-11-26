// Phase 5: pair the test PC over adb reverse (USB debugging); the PC must say "usb-adb" and list its direct addresses.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const online = async () => { for (let i = 0; i < 40 && !$('header-status-dot').className.includes('online'); i++) await wait(500); return $('header-status-dot').className.includes('online'); };
  const out = {};
  await online();
  document.querySelector('[data-act="open-profile"]').click();
  await wait(400);
  document.querySelector('#profile-menu-list [data-i="1"]').click();   // Add device (any language)
  await wait(800);
  $('pair-address').value = '127.0.0.1:8091';
  $('pair-code').value = '__CODE__';
  $('pair-submit-btn').click();
  await wait(2000);
  out.paired = await online();
  await wait(4000);
  out.server = localStorage.getItem('cloudssd_server_url');
  out.direct = JSON.parse(localStorage.getItem('cloudssd_direct_urls') || '[]');
  out.homeSub = $('home-conn-sub').innerText;
  out.linkInfo = JSON.parse(window.AndroidBridge.linkInfo());
  out.network = JSON.parse(window.AndroidBridge.getAppInfo()).network;
  return out;
})()
