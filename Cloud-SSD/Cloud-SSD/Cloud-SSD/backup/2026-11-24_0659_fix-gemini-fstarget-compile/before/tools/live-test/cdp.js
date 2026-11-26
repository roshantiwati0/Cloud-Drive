// Evaluates JavaScript in the Cloud-SSD app's WebView over Chrome DevTools Protocol (adb forward to the
// webview_devtools_remote socket). Usage: node cdp.js <port> <file-with-js-expression>
const port = process.argv[2];
const expr = require('fs').readFileSync(process.argv[3], 'utf8');

(async () => {
  const targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
  const page = targets.find(t => t.type === 'page' && /index\.html/.test(t.url)) || targets.find(t => t.type === 'page');
  if (!page) { console.error('no page target', targets); process.exit(2); }
  const ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  const result = await new Promise((res) => {
    ws.onmessage = (m) => { const d = JSON.parse(m.data); if (d.id === 1) res(d); };
    ws.send(JSON.stringify({ id: 1, method: 'Runtime.evaluate', params: { expression: expr, awaitPromise: true, returnByValue: true } }));
  });
  ws.close();
  if (result.result && result.result.exceptionDetails) { console.error(JSON.stringify(result.result.exceptionDetails, null, 1)); process.exit(1); }
  console.log(JSON.stringify(result.result && result.result.result && result.result.result.value, null, 1));
})().catch(e => { console.error(e); process.exit(1); });
