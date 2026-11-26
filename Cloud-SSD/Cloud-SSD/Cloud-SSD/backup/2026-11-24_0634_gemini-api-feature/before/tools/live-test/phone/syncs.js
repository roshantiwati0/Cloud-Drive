// The synced folders as the app keeps them; __CODE__ = "remove" removes them all (test cleanup).
(() => {
  const n = window.AndroidBridge;
  const all = JSON.parse(n.folderSyncs() || '[]');
  if ('__CODE__' === 'remove') all.forEach(p => n.folderSyncRemove(p.id));
  return { before: all.map(p => p.phoneLabel + ' <-> ' + p.pcLabel + ' (pc ' + p.pcId + ')'), after: JSON.parse(n.folderSyncs() || '[]').length };
})()
