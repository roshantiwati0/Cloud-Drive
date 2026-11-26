// The saved record of the PC in use (as the app keeps it) and this phone's clock.
(() => {
  const id = localStorage.getItem('cloudssd_pc_id');
  const rec = JSON.parse(window.AndroidBridge.pcList() || '[]').find(r => r.id === id) || null;
  return { id, guestUntil: rec ? rec.guestUntil : 'no record', phoneNow: Date.now() };
})()
