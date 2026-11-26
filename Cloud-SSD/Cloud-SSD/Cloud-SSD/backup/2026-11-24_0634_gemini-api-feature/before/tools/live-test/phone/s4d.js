// Stage 4 native notes: __CODE__ = "<lang> grant|revoke|lang". Sets the language on the page and in the app (as the
// Language chips do); "grant" lets the active (test) PC look for 2 minutes so the listening note shows; "revoke" ends it.
(async () => {
  const [lang, what] = '__CODE__'.split(' ');
  const native = window.AndroidBridge;
  const pc = localStorage.getItem('cloudssd_pc_id');
  window.CloudSsdI18n.setLang(lang);
  native.setLanguage(lang);
  if (what === 'grant') native.peerGrant(pc, 'shared', 2);
  if (what === 'revoke') native.peerRevoke(pc);
  await new Promise(r => setTimeout(r, 1500));
  return { lang: window.CloudSsdI18n.lang(), what, pc };
})()
