// Stage 4: in Hindi, open each screen and sheet and list the visible texts still in Latin letters (names and file
// names aside). Ends on the language it found.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  const I = window.CloudSsdI18n;
  const before = I.lang();
  const SKIP = '.item-name, [data-no-i18n], script, style, pre, code, textarea, #viewer-text-content, #sheet-file-name, ' +
    '.device-row-title, .drive-card-name, .inbox-names, .inbox-title, .offline-row-name, .phone-item-name, .tc-name, .thumb-tile-name';
  // Names, units and words that read the same in Hindi.
  const keep = /^(Cloud-SSD|Cloud SSD|PC|OK|Wi-Fi|USB|RAM|CPU|GPU|LIVE|HUD|Live|DCIM|Tailscale|Google Drive|Android|Artemis|Free Fire|[A-Z]+-[A-Z0-9]{4,}|Tab|Test Cloud|[^A-Za-z]*|[\d.,]+ ?(B|KB|MB|GB|TB|KiB|MiB|GiB|TiB)( free)?|[\w.-]+\.(txt|pdf|png|jpg|bin|zip|apk|mp4))$/;
  // This phone's own name ("maker model") reads the same in every language.
  const me = (() => { try { const a = JSON.parse(window.AndroidBridge.getAppInfo()); return [a.manufacturer, a.model].filter(Boolean).join(' '); } catch (e) { return ''; } })();
  const mine = new Set(me.split(' ').filter(Boolean));
  const seen = new Set();
  // Hindi lines that still carry an English word (a template that took part of the text for a value).
  const NAMES = /^(PC|PCs|Cloud|SSD|Cloud-SSD|Google|Drive|Wi-Fi|USB|Android|Windows|Microsoft|Recycle|Bin|Downloads|Documents|Download|WhatsApp|[A-Z]+-[A-Z0-9]{4,}|Tab|Tailscale|Artemis|RAM|CPU|GPU|APK|APKs|DCIM|KiB|MiB|GiB|KB|MB|GB|TB|HUD|Phone|Backup|From|phone|Test|Trip|Free|Fire|HTTP|VPN|adb|OK|ZIP|zip|PDF)$/;
  const mixed = new Set();
  function gaps() {
    const found = [];
    const tw = document.createTreeWalker(document.body, 4);
    let n;
    while ((n = tw.nextNode())) {
      const p = n.parentElement;
      if (!p || p.closest(SKIP) || !p.checkVisibility || !p.checkVisibility()) continue;
      const s = n.nodeValue.replace(/\s+/g, ' ').trim();
      if (/[ऀ-ॿ]/.test(s) && !mixed.has(s)) {
        const latin = (s.match(/[A-Za-z][A-Za-z-]{2,}/g) || []).filter(w => !NAMES.test(w) && !mine.has(w) && !/\.\w+$/.test(w));
        if (latin.length) { mixed.add(s); found.push('MIXED: ' + s.slice(0, 100)); }
      }
      if (!/[A-Za-z]{3}/.test(s) || /[ऀ-ॿ]/.test(s) || keep.test(s) || s === me || seen.has(s)) continue;
      seen.add(s);
      found.push(s.length > 100 ? s.slice(0, 100) + '…' : s);
    }
    for (const el of document.querySelectorAll('[placeholder],[title],[aria-label]')) {
      if (el.closest(SKIP) || !el.checkVisibility || !el.checkVisibility()) continue;
      for (const a of ['placeholder', 'title', 'aria-label']) {
        const v = el.getAttribute(a);
        if (v && /[A-Za-z]{3}/.test(v) && !/[ऀ-ॿ]/.test(v) && !keep.test(v) && !seen.has('@' + v)) { seen.add('@' + v); found.push('@' + a + ': ' + v); }
      }
    }
    return found;
  }
  const back = async () => { if (window.onAndroidBack) { window.onAndroidBack(); await wait(400); } };
  const out = {};
  I.setLang('hindi');
  await back(); await back();
  for (const tab of ['home', 'browser', 'gaming']) { $('nav-' + tab).click(); await wait(2500); out[tab] = gaps(); }
  // The profile menu and each of its sheets (not "Add device", which starts pairing).
  const items = ['devices', 'add', 'connection', 'permissions', 'shared', 'offline', 'clip', 'settings'];
  $('nav-home').click(); await wait(800);
  document.querySelector('[data-act="open-profile"]').click(); await wait(800);
  out.profile = gaps();
  const rows = Array.from(document.querySelectorAll('#profile-menu-list .m3-sheet-row')).length;
  await back();
  for (const i of [2, 3, 4, 5, rows - 1]) {
    document.querySelector('[data-act="open-profile"]').click(); await wait(800);
    document.querySelector(`#profile-menu-list [data-i="${i}"]`).click(); await wait(2500);
    out['menu' + i] = gaps();
    await back(); await back();
  }
  // The PC's drives, a folder, and a file's menu.
  $('nav-ssd').click(); await wait(2000);
  out.ssd = gaps();
  const pc = localStorage.getItem('cloudssd_pc_id');
  if ($('ssd-devices') && $('ssd-devices').style.display !== 'none') {
    const row = Array.from(document.querySelectorAll('#ssd-device-list .device-row')).find(r => r.dataset.id === pc);
    if (row) { row.click(); await wait(3000); out.drives = gaps(); }
  }
  const folder = document.querySelector('#ssd-items-container .folder-item');
  if (folder) { folder.click(); await wait(3000); out.folder = gaps(); }
  const more = document.querySelector('#ssd-items-container .file-item [data-act="ssd-more"]');
  if (more) { more.click(); await wait(1200); out.fileSheet = gaps(); await back(); }
  await back(); await back();
  $('nav-home').click(); await wait(500);
  I.setLang('hinglish'); // what the phone had before these tests
  out.endLang = I.lang();
  out.total = seen.size;
  return out;
})()
