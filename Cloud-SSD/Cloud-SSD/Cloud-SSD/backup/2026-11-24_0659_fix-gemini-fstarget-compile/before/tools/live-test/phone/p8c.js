(async () => {
  const wait = (ms) => new Promise(r => setTimeout(r, ms));
  if (window.onAndroidBack) { window.onAndroidBack(); await wait(300); }
  document.querySelector('[data-act="open-profile"]').click();
  await wait(500);
  Array.from(document.querySelectorAll('#profile-menu-list .m3-sheet-row')).find(r => /^Permissions/.test(r.innerText.trim())).click();
  await wait(1500);
  const rows = Array.from(document.querySelectorAll('#perm-list .perm-row')).map(r => r.innerText.replace(/\s+/g, ' ').trim());
  const peerRow = rows[rows.length - 1];
  document.querySelector('[data-act="peer-stop"]').click();
  await wait(1500);
  const after = Array.from(document.querySelectorAll('#perm-list .perm-row')).map(r => r.innerText.replace(/\s+/g, ' ').trim()).pop();
  return { before: peerRow, after, state: JSON.parse(window.AndroidBridge.peerState(localStorage.getItem('cloudssd_pc_id'))) };
})()
