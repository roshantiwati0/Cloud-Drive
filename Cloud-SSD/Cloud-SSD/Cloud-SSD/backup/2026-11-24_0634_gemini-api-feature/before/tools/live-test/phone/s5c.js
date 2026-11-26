// Stage 5 E1 UI: after the test PC is back, the waiting "Wake" says it is awake (within 2 minutes). Back to Hinglish.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  let i = 0;
  for (; i < 100 && !(window.__toasts || []).some(t => /is awake/.test(t)); i++) await wait(1000);
  const out = { seconds: i, toasts: (window.__toasts || []).slice(), title: $('home-conn-title').innerText };
  if (window.__toastWatch) { window.__toastWatch.disconnect(); delete window.__toastWatch; }
  window.CloudSsdI18n.setLang('hinglish');
  return out;
})()
