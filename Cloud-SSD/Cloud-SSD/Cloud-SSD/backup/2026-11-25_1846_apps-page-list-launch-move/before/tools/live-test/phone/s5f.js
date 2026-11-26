// Stage 5 E3: wait (up to 4 minutes) until the guest's time is up and the page says so. English, back to Hinglish.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  window.CloudSsdI18n.setLang('english');
  const out = {};
  try {
    let i = 0;
    for (; i < 240 && $('pair-overlay').style.display !== 'flex' && !/no longer|ended/i.test(document.body.innerText); i++) await wait(1000);
    out.seconds = i;
    out.pairNote = (Array.from(document.querySelectorAll('#pair-error')).map(e => e.innerText.trim()).filter(Boolean)).slice(0, 4);
    out.title = $('home-conn-title').innerText;
    const all = document.body.innerText;
    const m = all.match(/[^\n]*(guest time|Guest time)[^\n]*/);
    out.found = m ? m[0] : null;
  } finally {
    window.CloudSsdI18n.setLang('hinglish');
  }
  return out;
})()
