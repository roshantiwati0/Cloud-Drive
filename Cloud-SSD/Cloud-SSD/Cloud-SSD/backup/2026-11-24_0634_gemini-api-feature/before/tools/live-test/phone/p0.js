// Phase 0 on the phone: the saved test PC's address now answers as a different PC. The phone must not call it
// connected, and must keep its key (a different PC saying "not paired" must not wipe it).
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const errors = [];
  window.addEventListener('error', e => errors.push(String(e.message)));
  await wait(15000);
  return {
    pcs: JSON.parse(window.AndroidBridge.pcList()).map(d => `${d.id} ${d.name} active=${d.active} hasKey=${d.hasKey} ${d.lan}`),
    dot: $('header-status-dot').className,
    title: $('home-conn-title').innerText,
    sub: $('home-conn-sub').innerText,
    errors
  };
})()
