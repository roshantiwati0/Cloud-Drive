// Tests for the phone page's data helpers (mobile-android/src/main/assets/data.js). Run: node --test tests/mobile-js
'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const nodeCrypto = require('node:crypto');
const path = require('node:path');

const data = require(path.join(__dirname, '..', '..', 'mobile-android', 'src', 'main', 'assets', 'data.js'));
const { sha256, hmacSha256, hex, utf8, randomHex, sameText } = data.crypto;

test('sha256 matches known vectors', () => {
  assert.equal(hex(sha256(utf8(''))), 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855');
  assert.equal(hex(sha256(utf8('abc'))), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
  assert.equal(hex(sha256(utf8('abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq'))),
    '248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1');
});

test('sha256 matches node for many lengths and non-ASCII text', () => {
  for (let n = 0; n < 300; n += 7) {
    const s = 'é☁️' + 'x'.repeat(n);
    assert.equal(hex(sha256(utf8(s))), nodeCrypto.createHash('sha256').update(s, 'utf8').digest('hex'), 'length ' + n);
  }
});

test('hmacSha256 matches RFC 4231 test case 2 and node for long keys', () => {
  assert.equal(hex(hmacSha256(utf8('Jefe'), utf8('what do ya want for nothing?'))),
    '5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843');
  const longKey = new Uint8Array(131).fill(0xaa);
  const msg = utf8('Test Using Larger Than Block-Size Key - Hash Key First');
  assert.equal(hex(hmacSha256(longKey, msg)), nodeCrypto.createHmac('sha256', Buffer.from(longKey)).update(Buffer.from(msg)).digest('hex'));
});

test('helloProof matches the PC (same vector as MobileBridgeHelloTests)', () => {
  assert.equal(data.helloProof('test-token-123', '00112233445566778899aabbccddeeff', 'a1b2c3d4e5f60708'),
    'a2d8b5b9440a419842bd3aa84e7ef7df2021b5c19aaebbb6e41e0cca0f1d7a34');
});

// ---- "last time" data ----------------------------------------------------------------------------------------

const list = (folders, files) => ({ folders: folders.map(n => ({ name: n, path: 'Z:\\' + n })), files: files.map(n => ({ name: n, path: 'Z:\\' + n, size: 1 })) });

test('diffNew: nothing is new on the first visit; added files are; deleted and old ones are not', () => {
  const now = 1_000_000_000_000;
  assert.deepEqual(data.diffNew(null, list(['A'], ['a.jpg']), now), { newNames: [], newAt: 0 });
  const prev = Object.assign(list(['A'], ['a.jpg', 'b.jpg']), { newNames: [], newAt: 0 });
  const d = data.diffNew(prev, list(['A', 'New folder'], ['a.jpg', 'c.jpg']), now);
  assert.deepEqual(d.newNames.sort(), ['New folder', 'c.jpg']);
  assert.equal(d.newAt, now);
  // A folder and a file with the same name are different things.
  const d2 = data.diffNew(Object.assign(list([], ['X']), {}), list(['X'], ['X']), now);
  assert.deepEqual(d2.newNames, ['X']);
});

test('diffNew: badges carry over for a day, then go', () => {
  const t0 = 1_000_000_000_000;
  const first = Object.assign(list([], ['a.jpg', 'new.jpg']), data.diffNew(Object.assign(list([], ['a.jpg']), {}), list([], ['a.jpg', 'new.jpg']), t0));
  const soon = data.diffNew(first, list([], ['a.jpg', 'new.jpg']), t0 + 60_000);
  assert.deepEqual(soon.newNames, ['new.jpg']);
  assert.equal(soon.newAt, t0);
  assert.ok(data.isNew(Object.assign({}, first, soon), 'new.jpg', t0 + 60_000));
  const later = data.diffNew(Object.assign({}, first, soon), list([], ['a.jpg', 'new.jpg']), t0 + 25 * 3600_000);
  assert.deepEqual(later.newNames, []);
  assert.ok(!data.isNew(first, 'new.jpg', t0 + 25 * 3600_000));
  // A NEW file deleted meanwhile is not carried.
  const gone = data.diffNew(first, list([], ['a.jpg']), t0 + 1000);
  assert.deepEqual(gone.newNames, []);
});

test('pickEvictions removes the least recently used first, never the folder just written', () => {
  const metas = [
    { k: 'a', usedAt: 1, size: 10 }, { k: 'b', usedAt: 5, size: 10 }, { k: 'c', usedAt: 3, size: 10 }, { k: 'd', usedAt: 9, size: 10 }
  ];
  assert.deepEqual(data.pickEvictions(metas, { maxLists: 2, maxBytes: 1e9 }), ['a', 'c']);
  assert.deepEqual(data.pickEvictions(metas, { maxLists: 2, maxBytes: 1e9 }, 'a'), ['c', 'b']);
  assert.deepEqual(data.pickEvictions(metas, { maxLists: 100, maxBytes: 25 }), ['a', 'c']);
  assert.deepEqual(data.pickEvictions(metas, { maxLists: 100, maxBytes: 1e9 }), []);
});

test('fmtLastUpdate reads naturally', () => {
  const now = new Date(2026, 10, 12, 10, 47, 0).getTime();
  assert.equal(data.fmtLastUpdate(new Date(2026, 10, 12, 10, 42, 0).getTime(), now), '10:42 (5 min ago)');
  assert.equal(data.fmtLastUpdate(new Date(2026, 10, 12, 10, 46, 40).getTime(), now), '10:46 (just now)');
  assert.equal(data.fmtLastUpdate(new Date(2026, 10, 11, 18, 5, 0).getTime(), now), 'yesterday 18:05 (16 h ago)');
  assert.equal(data.fmtLastUpdate(new Date(2026, 10, 3, 18, 5, 0).getTime(), now), '3 Nov 18:05 (8 days ago)');
  assert.equal(data.fmtLastUpdate(0, now), 'earlier');
});

test('matchName needs every word, any case and order', () => {
  assert.ok(data.matchName('holi photo', 'Photo_HOLI_2026.jpg'));
  assert.ok(!data.matchName('holi video', 'Photo_HOLI_2026.jpg'));
  assert.ok(!data.matchName('   ', 'x'));
});

test('trimRecord caps very big folders and says so', () => {
  const big = { folders: Array.from({ length: 6000 }, (_, i) => ({ name: 'd' + i })), files: Array.from({ length: 6000 }, (_, i) => ({ name: 'f' + i })) };
  const t = data.trimRecord(big);
  assert.equal(t.folders.length + t.files.length, 10000);
  assert.ok(t.truncated);
  assert.ok(!data.trimRecord(list(['a'], ['b'])).truncated);
});

/** A localStorage stand-in. */
function fakeStorage() {
  const m = new Map();
  return { getItem: k => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), removeItem: k => m.delete(k), _m: m };
}

test('list store without IndexedDB: saves per PC, gives NEW names, searches, forgets a PC', async () => {
  let t = 1_000_000_000_000;
  const storage = fakeStorage();
  const store = data.createListStore({ indexedDB: null, localStorage: storage, now: () => t });
  assert.equal(await store.ready(), 'ls');
  const first = await store.put('pcA', 'Z:\\Photos', list(['Holi'], ['a.jpg']));
  assert.equal(first.savedAt, t);
  assert.deepEqual(first.newNames, []);
  t += 60_000;
  const second = await store.put('pcA', 'Z:\\Photos', list(['Holi'], ['a.jpg', 'holi-dance.mp4']));
  assert.deepEqual(second.newNames, ['holi-dance.mp4']);
  await store.put('pcB', 'Z:\\Photos', list([], ['other.jpg']));

  const got = await store.get('pcA', 'Z:\\Photos');
  assert.equal(got.files.length, 2);
  assert.equal(got.savedAt, t);
  assert.equal(await store.get('pcA', 'Z:\\Nope'), null);
  assert.equal((await store.get('pcB', 'Z:\\Photos')).files[0].name, 'other.jpg');   // PCs never mix

  const hits = await store.search('pcA', 'holi', 10);
  assert.deepEqual(hits.map(h => h.name).sort(), ['Holi', 'holi-dance.mp4']);
  assert.equal(await store.count('pcA'), 1);

  await store.removePc('pcA');
  assert.equal(await store.get('pcA', 'Z:\\Photos'), null);
  assert.ok(await store.get('pcB', 'Z:\\Photos'));
});

test('list store without IndexedDB keeps only the last 12 folders', async () => {
  let t = 1;
  const store = data.createListStore({ indexedDB: null, localStorage: fakeStorage(), now: () => t++ });
  for (let i = 0; i < 20; i++) await store.put('pc', 'Z:\\F' + i, list([], ['x']));
  assert.equal(await store.count('pc'), 12);
  assert.equal(await store.get('pc', 'Z:\\F0'), null);
  assert.ok(await store.get('pc', 'Z:\\F19'));
});

test('importing an old list keeps "saved earlier" and shows no NEW badges', async () => {
  const store = data.createListStore({ indexedDB: null, localStorage: fakeStorage() });
  const recs = data.migrateDirCache({ 'Z:\\A': list([], ['a']), bad: { folders: 1 } });
  assert.equal(recs.length, 1);
  const r = await store.put('pc', recs[0].path, recs[0], 0);
  assert.equal(r.savedAt, 0);
  assert.equal(data.fmtLastUpdate(r.savedAt, Date.now()), 'earlier');
});

test('snapshots are per PC and merge patches', () => {
  const s = data.createSnapshots(fakeStorage());
  assert.equal(s.get('pcA'), null);
  s.set('pcA', { status: { pcName: 'DESKTOP' }, lastOnline: 5 });
  s.set('pcA', { history: [1, 2] });
  s.set('pcB', { lastOnline: 9 });
  assert.deepEqual(s.get('pcA'), { status: { pcName: 'DESKTOP' }, lastOnline: 5, history: [1, 2] });
  assert.equal(s.get('pcB').lastOnline, 9);
  s.remove('pcA');
  assert.equal(s.get('pcA'), null);
  assert.equal(s.set('', { x: 1 }), null);   // no PC id: nothing saved
});

// ---- several PCs -------------------------------------------------------------------------------------------------

test('registry (browser): the old single PC becomes the first saved device, once', () => {
  const storage = fakeStorage();
  const reg = data.createRegistry({ native: null, storage });
  reg.migrate({ server: 'http://192.168.1.5:8090/', pcId: 'aaaa1111', pcName: 'DESKTOP', away: ['http://100.1.2.3:8090'], deviceId: 'd1', token: 'tok-a' });
  const list = reg.list();
  assert.equal(list.length, 1);
  assert.equal(list[0].id, 'aaaa1111');
  assert.equal(list[0].lan, 'http://192.168.1.5:8090');
  assert.ok(list[0].active);
  assert.ok(list[0].hasKey);
  assert.equal(list[0].token, undefined);                 // the list never hands keys out
  reg.migrate({ server: 'http://other:8090', pcId: 'zzz' });   // only once
  assert.equal(reg.list().length, 1);
});

test('registry (browser): add a second PC, switch, rename, re-key, forget', () => {
  const reg = data.createRegistry({ native: null, storage: fakeStorage() });
  reg.migrate({ server: '' });
  assert.deepEqual(reg.list(), []);
  reg.upsert({ id: 'pcA', name: 'DESKTOP', lan: 'http://192.168.1.5:8090' }, 'tok-a', true);
  reg.upsert({ id: 'url:http://192.168.1.9:8090', name: 'LAPTOP', lan: 'http://192.168.1.9:8090' }, 'tok-b', false);
  assert.equal(reg.active().id, 'pcA');

  const k = reg.activate('url:http://192.168.1.9:8090');
  assert.deepEqual(k, { ok: true, server: 'http://192.168.1.9:8090', token: 'tok-b', id: 'url:http://192.168.1.9:8090' });
  assert.equal(reg.active().name, 'LAPTOP');
  assert.equal(reg.activate('nope'), null);

  // The laptop reports its real id: its entry (and the active mark) move under it.
  reg.upsert({ id: 'pcB', replaces: 'url:http://192.168.1.9:8090' });
  assert.equal(reg.active().id, 'pcB');
  assert.equal(reg.activate('pcB').token, 'tok-b');

  reg.upsert({ id: 'pcA', label: 'Ghar wala PC' });
  assert.equal(reg.get('pcA').label, 'Ghar wala PC');
  assert.equal(reg.get('pcA').name, 'DESKTOP');           // its own name stays

  assert.equal(reg.remove('pcB'), 'pcA');                 // the other one becomes active
  assert.equal(reg.list().length, 1);
  assert.equal(reg.remove('pcA'), '');
  assert.equal(reg.active(), null);
});

test('registry (app): goes through the native side and never keeps keys in the page', () => {
  const calls = [];
  const fakeNative = {
    pcList: () => JSON.stringify([{ id: 'pcA', name: 'DESKTOP', active: true, hasKey: true }]),
    pcSave: (j) => { calls.push(['save', JSON.parse(j)]); return true; },
    pcActivate: (id) => (id === 'pcA' ? JSON.stringify({ ok: true, server: 'http://x:8090', token: 't', id }) : ''),
    pcRemove: (id) => { calls.push(['remove', id]); return true; }
  };
  const storage = fakeStorage();
  const reg = data.createRegistry({ native: fakeNative, storage });
  assert.ok(reg.native);
  assert.equal(reg.active().id, 'pcA');
  reg.upsert({ id: 'pcB', name: 'LAPTOP' }, 'secret', true);
  assert.deepEqual(calls[0], ['save', { id: 'pcB', name: 'LAPTOP', token: 'secret', activate: true }]);
  assert.equal(reg.activate('pcA').server, 'http://x:8090');
  assert.equal(reg.activate('nope'), null);
  reg.remove('pcB');
  assert.deepEqual(calls[1], ['remove', 'pcB']);
  assert.equal(storage._m.size, 0);                       // nothing written to the page's storage
});

test('randomHex and sameText', () => {
  const a = randomHex(16), b = randomHex(16);
  assert.match(a, /^[0-9a-f]{32}$/);
  assert.notEqual(a, b);
  assert.ok(sameText('abc', 'abc'));
  assert.ok(!sameText('abc', 'abd'));
  assert.ok(!sameText('abc', 'abcd'));
});

test('offline: a kept folder covers what is inside it, and only that', () => {
  assert.ok(data.covers('cloudssd:Photos', 'cloudssd:Photos'));
  assert.ok(data.covers('cloudssd:Photos', 'cloudssd:Photos/2024/a.jpg'));
  assert.ok(!data.covers('cloudssd:Photos', 'cloudssd:Photos 2/a.jpg'));   // a sibling with a longer name
  assert.ok(data.covers('cloudssd:', 'cloudssd:Photos/a.jpg'));            // a whole drive
  assert.ok(!data.covers('cloudssd:', 'received:a.jpg'));
  assert.ok(!data.covers('', 'cloudssd:a'));
  const pins = [{ id: 'cloudssd:Docs' }, { id: 'cloudssd:Photos/a.jpg' }];
  assert.equal(data.pinFor(pins, 'cloudssd:Docs/x/y.pdf').id, 'cloudssd:Docs');
  assert.equal(data.pinFor(pins, 'cloudssd:Photos/a.jpg').id, 'cloudssd:Photos/a.jpg');
  assert.equal(data.pinFor(pins, 'cloudssd:Photos/b.jpg'), null);
});

test('offline: the kept copy is used only for the same version of the file', () => {
  const t = Date.UTC(2026, 10, 12, 10, 0, 0);
  assert.ok(data.sameCopy([100, t], { size: 100, modified: t }));
  assert.ok(data.sameCopy([100, t], { size: 100, modified: new Date(t + 1000).toISOString() }));  // dates as text too
  assert.ok(!data.sameCopy([100, t], { size: 101, modified: t }));
  assert.ok(!data.sameCopy([100, t], { size: 100, modified: t + 60000 }));
  assert.ok(data.sameCopy([100, 0], { size: 100 }));                        // no dates known: the size decides
  assert.ok(!data.sameCopy(null, { size: 100 }));
});

test('offline: a line for each kept folder', () => {
  const now = Date.UTC(2026, 10, 12, 12, 0, 0);
  assert.match(data.pinStatus({ state: 'done', files: 3, bytes: 3 * 1024 * 1024, synced: now - 5 * 60000 }, now), /^On this phone · 3 files · 3\.0 MB · updated .*5 min ago/);
  assert.equal(data.pinStatus({ state: 'syncing', files: 40, doneFiles: 12, bytes: 1000, doneBytes: 450 }, now), 'Getting files · 12 of 40 · 45%');
  assert.equal(data.pinStatus({ state: 'syncing' }, now), 'Getting files…');
  assert.equal(data.pinStatus({ state: 'wifi' }, now), 'Waits for Wi-Fi (or allow mobile data)');
  assert.equal(data.pinStatus({ state: 'error', error: 'Not enough room' }, now), 'Not enough room');
  assert.equal(data.pinStatus({ state: 'waiting' }, now), 'Waits for the PC');
  assert.equal(data.fmtSize(512), '512 B');
  assert.equal(data.fmtSize(1536), '1.5 KB');
  assert.equal(data.fmtSize(250 * 1024 * 1024), '250 MB');
});

test('offline: a single kept file does not say "1 file"', () => {
  const now = Date.UTC(2026, 10, 12, 12, 0, 0);
  assert.match(data.pinStatus({ state: 'done', dir: false, files: 1, bytes: 23, synced: now }, now), /^On this phone · 23 B · updated /);
});

test('offline: a kept folder never opened here is listed from its copies', () => {
  const files = {
    'received:Trip/a.jpg': [10, 1000],
    'received:Trip/sub/b.txt': [5, 0],
    'received:Trip/sub/deeper/c.txt': [7, 0],
    'received:Other/x.txt': [1, 0],
    'received:Trip 2/y.txt': [1, 0]
  };
  const toPath = (id) => 'C:\\up\\' + id.slice(id.indexOf(':') + 1).split('/').join('\\');
  const l = data.keptListing(files, 'received:Trip', toPath);
  assert.deepEqual(l.files, [{ name: 'a.jpg', path: 'C:\\up\\Trip\\a.jpg', size: 10, modified: 1000 }]);
  assert.deepEqual(l.folders, [{ name: 'sub', path: 'C:\\up\\Trip\\sub' }]);
  const root = data.keptListing(files, 'received:', toPath);
  assert.deepEqual(root.folders.map(f => f.name).sort(), ['Other', 'Trip', 'Trip 2']);
  assert.equal(root.files.length, 0);
});

test('offline: a place on the PC\'s own disk is named as a read-only drive id, and back', () => {
  assert.equal(data.diskId('C:\\Users\\me\\Documents'), 'disk-c:Users/me/Documents');
  assert.equal(data.diskId('D:\\'), 'disk-d:');
  assert.equal(data.diskId('c:'), 'disk-c:');
  assert.equal(data.diskId('C:\\Users\\me\\Documents\\'), 'disk-c:Users/me/Documents');   // a trailing slash changes nothing
  assert.equal(data.diskId('cloudssd:Photos'), null);                                      // already a drive id
  assert.equal(data.diskId('/storage/emulated/0/DCIM'), null);                             // a phone path
  assert.equal(data.diskId(''), null);
  assert.ok(data.isDiskId('disk-c:Users'));
  assert.ok(!data.isDiskId('cloudssd:Users') && !data.isDiskId('received:a') && !data.isDiskId('disk-cc:a'));
  assert.equal(data.diskPath('disk-c:Users/me/Documents'), 'C:\\Users\\me\\Documents');
  assert.equal(data.diskPath('disk-c:'), 'C:\\');
  assert.equal(data.diskPath('cloudssd:Photos'), null);
  for (const p of ['C:\\', 'C:\\Users', 'E:\\a b\\c.d\\e']) assert.equal(data.diskPath(data.diskId(p)), p);
});

test('offline: the way down to kept folders of a PC disk shows while the PC is off', () => {
  const pins = [{ id: 'disk-c:Users/me/Documents', synced: 5 }, { id: 'disk-c:Users/me/Pictures', synced: 9 }, { id: 'cloudssd:Photos', synced: 1 }];
  assert.deepEqual(data.pinsBelow(pins, 'disk-c:').map(p => p.id), ['disk-c:Users/me/Documents', 'disk-c:Users/me/Pictures']);
  assert.deepEqual(data.pinsBelow(pins, 'disk-c:Users/me').map(p => p.id), ['disk-c:Users/me/Documents', 'disk-c:Users/me/Pictures']);
  assert.deepEqual(data.pinsBelow(pins, 'disk-c:Users/me/Documents'), []);                  // the pin itself is not "below"
  assert.deepEqual(data.pinsBelow(pins, 'disk-c:Users/meg'), []);                           // a longer name beside it
  // Its list is made from the copies: only the folders that lead to something kept.
  const files = { 'disk-c:Users/me/Documents/a.pdf': [10, 1000], 'disk-c:Users/me/Pictures/2024/b.jpg': [5, 0] };
  const toPath = (id) => data.diskPath(id);
  assert.deepEqual(data.keptListing(files, 'disk-c:', toPath), { folders: [{ name: 'Users', path: 'C:\\Users' }], files: [] });
  assert.deepEqual(data.keptListing(files, 'disk-c:Users/me', toPath).folders.map(f => f.path), ['C:\\Users\\me\\Documents', 'C:\\Users\\me\\Pictures']);
  assert.deepEqual(data.keptListing(files, 'disk-c:Users/me/Documents', toPath).files, [{ name: 'a.pdf', path: 'C:\\Users\\me\\Documents\\a.pdf', size: 10, modified: 1000 }]);
});

test('offline: a folder too big to keep says why before anything is fetched', () => {
  const GB = 1024 * 1024 * 1024;
  assert.equal(data.keepTooBig({ files: 40, bytes: 200 * 1024 * 1024 }, 70 * GB), '');
  assert.equal(data.keepTooBig({ files: 5000, bytes: 1 }, 70 * GB), '');
  assert.match(data.keepTooBig({ files: 5001, bytes: 1 }, 70 * GB), /^Too many files \(5001\)/);
  assert.equal(data.keepTooBig({ files: 10, bytes: 2 * GB }, 2 * GB), 'Not enough room on this phone');   // 500 MB always stays free
  assert.equal(data.keepTooBig({ files: 10, bytes: 2 * GB }, 0), '');                                      // room not known: the app checks
  assert.equal(data.keepTooBig(null, GB), '');
  assert.equal(data.keepTooBig({ failed: 'PC did not answer' }, GB), '');
});

test('links: words for how the phone reached the PC, and which addresses are on a link the phone has', () => {
  assert.equal(data.linkLabel('usb'), 'USB cable');
  assert.equal(data.linkLabel('phone-hotspot'), "this phone's hotspot");
  assert.equal(data.linkLabel('pc-hotspot'), "the PC's hotspot");
  assert.equal(data.linkLabel('usb-adb'), 'USB cable (adb)');
  assert.equal(data.linkLabel(''), 'local Wi-Fi');
  assert.ok(data.sameSubnet('192.168.42.129', 'http://192.168.42.100:8090'));
  assert.ok(!data.sameSubnet('192.168.42.129', 'http://192.168.43.100:8090'));
  assert.ok(!data.sameSubnet('', 'http://192.168.42.100:8090'));
  assert.ok(!data.sameSubnet('192.168.42.129', 'http://pc.tailnet.ts.net:8090'));
});

test('permissions: words for each category and where it stands', () => {
  const now = new Date(2026, 10, 12, 15, 0, 0).getTime();
  assert.equal(data.accessName('manage'), 'Change files: new folders, rename, move, delete');
  assert.equal(data.accessName('unknown'), 'unknown');
  assert.equal(data.grantText({ switchOn: false, allowed: true }, now), 'Off on the PC for every device');
  assert.equal(data.grantText({ switchOn: true, allowed: false }, now), 'Not allowed');
  assert.equal(data.grantText({ switchOn: true, allowed: true, mode: 'always' }, now), 'Allowed');
  assert.equal(data.grantText({ switchOn: true, allowed: true, mode: 'session' }, now), 'Allowed until the PC app restarts');
  assert.equal(data.grantText({ switchOn: true, allowed: true, mode: 'timed', expiresAt: new Date(2026, 10, 12, 15, 45).toISOString() }, now), 'Allowed until 15:45');
  assert.equal(data.grantText({ switchOn: true, allowed: true, mode: 'timed', expiresAt: new Date(2026, 10, 12, 14, 0).toISOString() }, now), 'Not allowed');
});

test('sharing: a sender name becomes a safe folder name', () => {
  assert.equal(data.safeFolder('DESKTOP-AB12CD3'), 'DESKTOP-AB12CD3');
  assert.equal(data.safeFolder('My PC/2: "home"'), 'My PC_2_ _home_');
  assert.equal(data.safeFolder('..'), 'PC');
  assert.equal(data.safeFolder(''), 'PC');
  assert.equal(data.safeFolder(' .hidden. '), 'hidden');
});

test('peer: what the PC may see on this phone, in words', () => {
  const now = new Date(2026, 10, 12, 15, 0, 0).getTime();
  assert.equal(data.peerText({}, now), 'Not allowed');
  assert.equal(data.peerText({ scope: 'all', until: 0 }, now), 'Whole phone storage · until you stop it');
  assert.equal(data.peerText({ scope: 'shared', until: new Date(2026, 10, 12, 16, 0).getTime() }, now), 'Photos, downloads, documents and music · until 16:00');
  assert.equal(data.peerText({ scope: 'shared', until: now - 1 }, now), 'Not allowed');
  assert.equal(data.peerText({ scope: 'all', until: 0, write: true }, now), 'Whole phone storage · can add files · until you stop it');
});

test('friendlyPath names a folder by its place, not its Windows path', () => {
  const places = [
    { root: 'C:\\Users\\me\\Downloads\\Cloud-SSD\\uploads', label: 'From phone' },
    { root: 'received:', label: 'From phone' },
    { root: 'cloudssd:', label: 'Test Cloud' }
  ];
  assert.equal(data.friendlyPath('C:\\Users\\me\\Downloads\\Cloud-SSD\\uploads\\Trip\\Goa', places), 'From phone › Trip › Goa');
  assert.equal(data.friendlyPath('c:\\users\\me\\downloads\\cloud-ssd\\uploads', places), 'From phone');      // any case
  // "uploads2" is not inside "uploads": shown as it is.
  assert.equal(data.friendlyPath('C:\\Users\\me\\Downloads\\Cloud-SSD\\uploads2\\x', places), 'C:\\Users\\me\\Downloads\\Cloud-SSD\\uploads2\\x');
  // A root that ends in ":" (an id) also has folders inside.
  assert.equal(data.friendlyPath('received:Trip', places), 'From phone › Trip');
  assert.equal(data.friendlyPath('cloudssd:Docs/2026', places), 'Test Cloud › Docs › 2026');
  assert.equal(data.friendlyPath('D:\\Games', places), 'D:\\Games');
  assert.equal(data.friendlyPath('', []), '');
});

test('syncStatus says what the last run of a synced folder did', () => {
  const when = at => '10:42';
  assert.equal(data.syncStatus({}, when), 'Not synced yet');
  assert.equal(data.syncStatus({ last: { ok: false, error: 'Its PC is not the one in use.' } }, when), 'Last try failed: Its PC is not the one in use.');
  assert.equal(data.syncStatus({ last: { ok: true, at: 1, up: 3, down: 0 } }, when), 'Synced 10:42 · 3 up, 0 down');
  assert.equal(data.syncStatus({ last: { ok: true, at: 1, up: 0, down: 1, conflicts: 1, failed: 2 } }, when),
    'Synced 10:42 · 0 up, 1 down · 1 kept twice (conflict) · 2 failed');
});

test('wakeHint is honest about Wi-Fi cards', () => {
  assert.equal(data.wakeHint({ wired: true }), 'Sends a wake signal on this Wi-Fi');
  assert.match(data.wakeHint({ wired: false }), /Wi-Fi: waking it often only works over a cable/);
  assert.match(data.wakeHint(null), /cable/);
});

test('the transfer history is grouped by kind of file, in one fixed order', () => {
  assert.deepEqual(data.TRANSFER_KINDS, ['photos', 'videos', 'music', 'documents', 'apps', 'others']);
  assert.equal(data.transferKind('beach.PNG'), 'photos');
  assert.equal(data.transferKind('trip.mp4'), 'videos');
  assert.equal(data.transferKind('song.m4a'), 'music');
  assert.equal(data.transferKind('guide.pdf'), 'documents');
  assert.equal(data.transferKind('game.apk'), 'apps');
  assert.equal(data.transferKind('1b638429cd1f4815.bin'), 'others');
  assert.equal(data.transferKind('README'), 'others');
  assert.equal(data.transferKind(''), 'others');
  const list = ['a.mp4', 'b.pdf', 'c.jpg', 'd.bin', 'e.jpg'].map(n => ({ fileName: n }));
  const groups = data.groupTransfers(list, t => t.fileName);
  assert.deepEqual(groups.map(g => g.kind), ['photos', 'videos', 'documents', 'others']);
  assert.deepEqual(groups[0].items.map(t => t.fileName), ['c.jpg', 'e.jpg']);   // newest first, as given
  assert.deepEqual(data.groupTransfers([]), []);
});

test('a file of a sent folder is saved in the same place under the base, never outside it', () => {
  const base = 'Cloud-SSD/Received/DESKTOP';
  assert.equal(data.shareSavePath(base, ''), base);
  assert.equal(data.shareSavePath(base, 'MyApp/src/lib'), base + '/MyApp/src/lib');
  assert.equal(data.shareSavePath(base, 'MyApp\\src'), base + '/MyApp/src');
  assert.equal(data.shareSavePath(base, '../../Android/data'), base + '/Android/data');
  assert.equal(data.shareSavePath(base, 'C:/x'), base + '/C_/x');
  assert.equal(data.shareSavePath(base, 'MyApp/src', { MyApp: 'MyApp (2)' }), base + '/MyApp (2)/src');
});

test('an offer is summed up by its folders and loose files', () => {
  const s = data.offerSummary([{ name: 'a.js', folder: 'MyApp/src' }, { name: 'b.js', folder: 'MyApp' }, { name: 'n.txt', folder: '' }, { name: 'c', folder: 'Other' }]);
  assert.deepEqual(s.folders, ['MyApp', 'Other']);
  assert.equal(s.text, 'MyApp/, Other/, n.txt');
  assert.equal(data.offerSummary([{ name: '1' }, { name: '2' }, { name: '3' }], 2).text, '1, 2 and 1 more');
});

test('a task says how the phone reaches the PC in the shared words', () => {
  assert.equal(data.taskLink('tailnet', 'wifi'), 'tailscale');
  assert.equal(data.taskLink('lan', 'internet'), 'tailscale');
  assert.equal(data.taskLink('lan', 'usb-adb'), 'usb');
  assert.equal(data.taskLink('lan', 'phone-hotspot'), 'hotspot');
  assert.equal(data.taskLink('lan', 'lan'), 'wifi');
  assert.equal(data.taskLink('lan', ''), 'wifi');
});

test('a changed task replaces the old one and the list stays newest first', () => {
  let list = [];
  list = data.upsertTask(list, { id: 'a', started: '2026-11-13T10:00:00+05:30', state: 'running' });
  list = data.upsertTask(list, { id: 'b', started: '2026-11-13T11:00:00+05:30', state: 'waiting' });
  list = data.upsertTask(list, { id: 'a', started: '2026-11-13T10:00:00+05:30', state: 'done' });
  assert.deepEqual(list.map(t => t.id + ':' + t.state), ['b:waiting', 'a:done']);
  assert.equal(data.upsertTask(list, null).length, 2);
  for (let i = 0; i < 50; i++) list = data.upsertTask(list, { id: 'x' + i, started: '2026-11-14T00:00:' + String(i).padStart(2, '0') });
  assert.equal(list.length, 40);
});

test('files when it is off: what is missing before the PC\'s copy opens, and the line above it', () => {
  const on = { pc: true, device: true, active: true, key: true };
  const signedIn = { available: true, accounts: [{ email: 'a@x.com' }] };
  assert.equal(data.whenOffReady({ pc: true, device: false }, signedIn), '');            // not on: nothing to say
  assert.equal(data.whenOffReady({ ...on, key: false }, { available: false, accounts: [] }), 'shared-key');
  assert.equal(data.whenOffReady(on, { available: false, accounts: [] }), 'sign-in');    // the key is on its way
  assert.equal(data.whenOffReady(on, { available: true, accounts: [] }), 'sign-in');
  assert.equal(data.whenOffReady(on, { available: true, accounts: [{ email: 'a@x.com', needsReauth: true }] }), 'sign-in');
  assert.equal(data.whenOffReady(on, signedIn), 'ready');
  assert.equal(data.whenOffReady({ ...on, key: false }, signedIn), 'ready');             // the app was built with a key
  assert.match(data.whenOffReadyText('ready', 'My PC'), /"My PC \(cloud copy\)"/);
  assert.match(data.whenOffReadyText('shared-key', ''), /^the PC still signs in/);
  assert.equal(data.whenOffReadyText('', 'My PC'), '');

  const fmt = t => 'T' + t;
  const at = Date.parse('2026-11-14T09:02:00Z');
  assert.equal(data.copyNote(null, 'My PC', fmt), 'My PC is off or away · cloud copy');
  assert.equal(data.copyNote({ device: { lastSync: '2026-11-14T09:02:00Z', bytes: 200, uploadedBytes: 82 } }, 'My PC', fmt),
    'My PC is off or away · cloud copy up to T' + at + ' · 41% copied so far');
  assert.equal(data.copyNote({ device: { lastSync: '2026-11-14T09:02:00Z', bytes: 200, uploadedBytes: 200 } }, '', fmt),
    'PC is off or away · cloud copy up to T' + at);
});

test('files when it is off: on only when the phone and the PC both said yes', () => {
  assert.equal(data.whenOffState(undefined), 'none');                       // an older PC
  assert.equal(data.whenOffState({ pc: true, device: true, guest: true }), 'none');   // a guest never has it
  assert.equal(data.whenOffState({ pc: false, device: false }), 'off');
  assert.equal(data.whenOffState({ pc: false, device: true }), 'waiting-pc');
  assert.equal(data.whenOffState({ pc: true, device: false }), 'waiting-phone');
  assert.equal(data.whenOffState({ pc: true, device: true, active: true }), 'on');
  assert.equal(data.whenOffText('waiting-pc', 'My PC'), 'Waiting for a yes on My PC');
  assert.equal(data.whenOffText('waiting-phone', 'My PC'), 'My PC said yes · waiting for a yes here');
  assert.equal(data.whenOffText('on', ''), 'On: this phone and the PC both said yes');
  assert.equal(data.whenOffText('off', 'My PC'), 'Off');
});
