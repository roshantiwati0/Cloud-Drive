// What the phone shows about the test PC right now (no clicks).
(() => {
  const $ = (id) => document.getElementById(id);
  const pc = localStorage.getItem('cloudssd_pc_id');
  const me = JSON.parse(window.AndroidBridge.pcList()).find(d => d.id === pc) || {};
  return { dot: $('header-status-dot').className, title: $('home-conn-title').innerText, sub: $('home-conn-sub').innerText, hasKey: me.hasKey, active: me.active };
})()
