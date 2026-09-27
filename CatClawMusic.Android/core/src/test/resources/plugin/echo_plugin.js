// 一个「非 Kotlin 写的」v2 插件，用来证明换格式真的跨语言。
// 一行一报文：读 stdin 一行，往 stdout 写一行。
const readline = require('readline');
const rl = readline.createInterface({ input: process.stdin });
function send(obj) { process.stdout.write(JSON.stringify(obj) + '\n'); }
rl.on('line', (line) => {
  let req;
  try { req = JSON.parse(line); } catch (e) { return send({ protocol: 2, id: 0, error: { code: -32700, message: 'bad json', retryable: false } }); }
  const { id, method } = req;
  if (req.protocol !== 2) return send({ protocol: 2, id, error: { code: -32600, message: 'protocol mismatch', retryable: false } });
  if (method === 'initialize') return send({ protocol: 2, id, result: { protocolVersion: 2, capabilities: ['search'] } });
  if (method === 'search') {
    return send({ protocol: 2, id, result: { songs: [{ id: 'n1', platform: 'node', title: '稻香', artist: '周杰伦', duration_ms: 227000 }] } });
  }
  return send({ protocol: 2, id, error: { code: -32601, message: 'method not found: ' + method, retryable: false } });
});
