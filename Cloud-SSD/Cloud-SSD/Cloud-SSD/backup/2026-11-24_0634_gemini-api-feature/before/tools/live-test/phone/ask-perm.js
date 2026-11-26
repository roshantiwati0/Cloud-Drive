// Permissions sheet → "Ask" on the category whose text contains __CODE__ → "Ask the PC". Returns what the sheet says.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const $ = (id) => document.getElementById(id);
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  document.querySelector('[data-act="open-profile"]').click(); await wait(600);
  document.querySelector('#profile-menu-list [data-i="3"]').click(); await wait(2500);
  const row = Array.from(document.querySelectorAll('#perm-list > *')).find(r => r.innerText.includes('__CODE__'));
  if (!row) return { error: 'no such row', rows: Array.from(document.querySelectorAll('#perm-list > *')).map(r => r.innerText.replace(/\s+/g, ' ')) };
  const ask = Array.from(row.querySelectorAll('button, [data-act]')).find(b => /Ask/.test(b.innerText));
  if (!ask) return { error: 'no Ask on that row', row: row.innerText };
  ask.click(); await wait(1200);
  const go = Array.from(document.querySelectorAll('button')).find(b => b.checkVisibility() && /^Ask the PC$/.test(b.innerText.trim()));
  if (go) { go.click(); await wait(2500); }
  const status = Array.from(document.querySelectorAll('#access-status, .access-status')).map(e => e.innerText).join(' ');
  return { asked: !!go, status };
})()
