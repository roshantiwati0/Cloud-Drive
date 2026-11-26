// Stage 5 E1: the active PC's saved record has its card ("wol"), and the app sends the wake packet over Wi-Fi.
(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  const native = window.AndroidBridge;
  const pc = localStorage.getItem('cloudssd_pc_id');
  let rec = null;
  for (let i = 0; i < 40; i++) {
    rec = JSON.parse(native.pcList() || '[]').find(r => r.id === pc);
    if (rec && rec.wol) break;
    await wait(1000);
  }
  if (!rec || !rec.wol) return { error: 'no wol saved', rec };
  const sent = native.wakePc(rec.wol.mac, rec.wol.broadcast);
  return { pc, wol: rec.wol, sent };
})()
