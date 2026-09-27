// 读完第一个请求就退出的插件：用来验「死了重开」这条路径。
// 第一个请求必须回（否则测不出重开后能不能继续），第二个请求到达前进程已经没了。
process.stdin.setEncoding('utf8');
let buffer = '';
process.stdin.on('data', (chunk) => {
  buffer += chunk;
  let nl;
  while ((nl = buffer.indexOf('\n')) >= 0) {
    const line = buffer.slice(0, nl).trim();
    buffer = buffer.slice(nl + 1);
    if (!line) continue;
    const req = JSON.parse(line);
    process.stdout.write(JSON.stringify({ protocol: 2, id: req.id, result: { bye: true } }) + '\n');
    if (req.method === 'initialize') process.exit(7);
  }
});
