// Stage 4: the language on first start, Settings → Language chips, and every visible text still in English (in Hindi
// the gaps show up as Latin letters) on each main screen, the profile menu and Settings. Ends back on Hinglish.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const I = window.CloudSsdI18n;
  const out = { start: I.lang(), htmlLang: document.documentElement.lang, title: $('home-conn-title').innerText };
  const SKIP = '.item-name, [data-no-i18n], script, style, pre, code, textarea, #viewer-text-content, #sheet-file-name';
  const keep = /^(Cloud-SSD|Cloud SSD|PC|OK|Wi-Fi|USB|RAM|CPU|GPU|LIVE|HUD|Live|DCIM|Tailscale|[^A-Za-z]*)$/;
  function gaps() {
    const found = new Set();
    const tw = document.createTreeWalker(document.body, 4);
    let n;
    while ((n = tw.nextNode())) {
      const p = n.parentElement;
      if (!p || p.closest(SKIP)) continue;
      if (!p.checkVisibility || !p.checkVisibility()) continue;
      const s = n.nodeValue.replace(/\s+/g, ' ').trim();
      if (!/[A-Za-z]{3}/.test(s) || /[ऀ-ॿ]/.test(s) || keep.test(s)) continue;
      found.add(s.length > 90 ? s.slice(0, 90) + '…' : s);
    }
    for (const el of document.querySelectorAll('[placeholder],[title],[aria-label]')) {
      if (el.closest(SKIP) || !el.checkVisibility || !el.checkVisibility()) continue;
      for (const a of ['placeholder', 'title', 'aria-label']) {
        const v = el.getAttribute(a);
        if (v && /[A-Za-z]{3}/.test(v) && !/[ऀ-ॿ]/.test(v) && !keep.test(v)) found.add('@' + a + ': ' + v);
      }
    }
    return [...found];
  }
  async function openSettings() {
    if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
    document.querySelector('[data-act="open-profile"]').click(); await wait(600);
    out.profileGaps = gaps();
    // Settings is always the last row of the profile menu.
    document.querySelector('#profile-menu-list .m3-sheet-row:last-child').click();
    await wait(2000);
  }
  const chip = (k) => document.querySelector(`[data-act="lang"][data-lang="${k}"]`);

  await openSettings();
  out.chips = Array.from(document.querySelectorAll('[data-act="lang"]')).map(c => c.innerText + (c.classList.contains('active') ? ' *' : ''));
  if (!chip('hindi')) return { ...out, error: 'no language chips' };
  chip('hindi').click(); await wait(1500);
  out.hindi = { htmlLang: document.documentElement.lang, nativeLang: null, settingsGaps: gaps(),
    sections: Array.from(document.querySelectorAll('#settings-body .sheet-subtitle')).map(s => s.innerText).slice(0, 12) };
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(400); }
  out.hindi.screens = {};
  for (const tab of ['home', 'browser', 'ssd', 'gaming']) {
    $('nav-' + tab).click(); await wait(2500);
    out.hindi.screens[tab] = { nav: $('nav-' + tab).innerText.replace(/\s+/g, ' ').trim(), gaps: gaps() };
  }
  $('nav-home').click(); await wait(800);
  out.hindi.title = $('home-conn-title').innerText;

  await openSettings();
  chip('english').click(); await wait(1500);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(400); }
  out.english = { htmlLang: document.documentElement.lang, title: $('home-conn-title').innerText, nav: $('nav-ssd').innerText.trim() };

  await openSettings();
  chip('hinglish').click(); await wait(1500);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(400); }
  out.hinglish = { htmlLang: document.documentElement.lang, title: $('home-conn-title').innerText, nav: $('nav-ssd').innerText.trim(),
    saved: localStorage.getItem('cloudssd_lang') };
  return out;
})()
