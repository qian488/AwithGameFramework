import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { join, resolve } from 'node:path';
import { setTimeout } from 'node:timers/promises';

const [serverDll, reportDirectory] = process.argv.slice(2);
if (!serverDll || !reportDirectory) throw new Error('Pass the Test.Server DLL and a new evidence directory.');
const output = resolve(reportDirectory);
await mkdir(output);
const probe = createServer();
await new Promise((ready, fail) => probe.once('error', fail).listen(0, '127.0.0.1', ready));
const { port } = probe.address();
await new Promise(ready => probe.close(ready));
const config = join(output, 'server.json');
await writeFile(config, JSON.stringify({ onlineProbePort: port }));
const logs = [];
const child = spawn('dotnet', [resolve(serverDll), '--config', config, '--run-for-seconds', '30'], { windowsHide: true });
const stopped = new Promise((ready, fail) => child.once('error', fail).once('exit', ready));
child.stdout.on('data', data => logs.push(data.toString()));
child.stderr.on('data', data => logs.push(data.toString()));
const http = 'http://127.0.0.1:' + port;
const ws = 'ws://127.0.0.1:' + port + '/session';
const report = { ready: false, heartbeat: false, delivery: false, recovery: false, passed: false };

function frame(kind, payload = Buffer.alloc(0), heartbeat = 0) {
    const data = Buffer.alloc(26);
    data.write('AWRT', 0);
    data[4] = 1;
    data[5] = kind;
    data.writeBigInt64LE(BigInt(heartbeat), 14);
    data.writeInt32LE(payload.length, 22);
    return Buffer.concat([data, payload]);
}

function event(socket, type) {
    return new Promise((ready, fail) => {
        const signal = AbortSignal.timeout(5000);
        signal.addEventListener('abort', () => fail(new Error('WebSocket timed out waiting for ' + type)), { once: true });
        socket.addEventListener(type, ready, { once: true, signal });
        socket.addEventListener('error', () => fail(new Error('WebSocket failed waiting for ' + type)), { once: true, signal });
    });
}

async function exchange(socket, request) {
    const pending = event(socket, 'message');
    socket.send(request);
    const reply = Buffer.from((await pending).data);
    if (reply.length < 26 || reply.toString('ascii', 0, 4) !== 'AWRT') throw new Error('Unexpected realtime message.');
    return reply;
}

let socket;
try {
    for (let attempt = 0; attempt < 50; attempt++) {
        const reply = await fetch(http + '/status', { signal: AbortSignal.timeout(1000) }).catch(() => null);
        if (reply?.ok && (await reply.json()).ready) { report.ready = true; break; }
        await setTimeout(100);
    }
    if (!report.ready) throw new Error('The Test server did not start.');
    socket = new WebSocket(ws);
    socket.binaryType = 'arraybuffer';
    await event(socket, 'open');
    const pong = await exchange(socket, frame(3, Buffer.alloc(0), 41));
    if (pong[5] !== 4 || pong.readBigInt64LE(14) !== 41n) throw new Error('Server heartbeat did not match.');
    report.heartbeat = true;
    const push = await exchange(socket, frame(1, Buffer.from(JSON.stringify('probe'))));
    if (push[5] !== 2 || push.readBigInt64LE(6) !== 1n || JSON.parse(push.toString('utf8', 26)) !== 'probe-ack') {
        throw new Error('Server returned the wrong push.');
    }
    report.delivery = true;
    const closing = event(socket, 'close');
    socket.close();
    await closing;
    const recovery = await fetch(http + '/recovery?after=0', { signal: AbortSignal.timeout(5000) });
    if (!recovery.ok) throw new Error('Push recovery failed.');
    const snapshot = await recovery.json();
    if (snapshot.throughSequence !== 1 || snapshot.pushes.length !== 1 ||
        snapshot.pushes[0].sequence !== 1 ||
        JSON.parse(Buffer.from(snapshot.pushes[0].payload, 'base64').toString()) !== 'probe-ack') {
        throw new Error('Recovered push did not match the previously delivered result.');
    }
    report.recovery = true;
    report.passed = true;
} catch (error) {
    report.error = error.message;
} finally {
    socket?.close();
    // The server's timed stop exercises the same module shutdown path as the normal server command.
    report.serverExitCode = await stopped;
    report.passed &&= report.serverExitCode === 0;
    await writeFile(join(output, 'server.log'), logs.join(''));
    await writeFile(join(output, 'report.json'), JSON.stringify(report, null, 2));
}
console.log((report.passed ? 'TEST_ONLINE_PASSED ' : 'TEST_ONLINE_FAILED ') + output);
if (!report.passed) process.exitCode = 1;
