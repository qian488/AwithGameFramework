import { spawn } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { join, resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const [serverDll, playerExe, reportPath, mode = 'rain-pass'] = process.argv.slice(2);
if (!serverDll || !playerExe || !reportPath || !['rain-pass', 'bbads'].includes(mode))
    throw new Error('Pass Test.Server.dll, the Windows Player, a new report directory and the mode.');

const output = resolve(reportPath);
await mkdir(output);
const portProbe = createServer();
await new Promise((ready, fail) => portProbe.once('error', fail).listen(0, '127.0.0.1', ready));
const { port } = portProbe.address();
await new Promise(ready => portProbe.close(ready));
const serverUrl = 'http://127.0.0.1:' + port;
const config = join(output, 'server.json');
await writeFile(config, JSON.stringify({ pvpPort: port }));

const processes = [];
const report = { mode, passed: false };
const logs = [];

function launch(executable, args) {
    const child = spawn(executable, args, { windowsHide: true });
    const finished = new Promise((ready, fail) => child.once('error', fail).once('exit', ready));
    finished.catch(() => {});
    const process = { child, finished };
    processes.push(process);
    return process;
}

try {
    const server = launch('dotnet', [resolve(serverDll), '--config', config, '--run-for-seconds', '55']);
    server.child.stdout.on('data', data => logs.push(data.toString()));
    server.child.stderr.on('data', data => logs.push(data.toString()));
    let ready = false;
    for (let attempt = 0; attempt < 80; attempt++) {
        const response = await fetch(serverUrl + '/pvp/status', { signal: AbortSignal.timeout(500) }).catch(() => null);
        if (response?.ok) { ready = true; break; }
        if (server.child.exitCode !== null) break;
        await delay(100);
    }
    if (!ready) throw new Error('Test.Server PvP endpoint did not become ready.');

    const clients = ['alpha', 'beta'].map(player => {
        const result = join(output, player + '.json');
        const launched = launch(resolve(playerExe), [
            '-batchmode', '-nographics', '-logFile', join(output, player + '.log'),
            '-pvp-smoke', mode, '-pvp-server', serverUrl, '-pvp-room', 'player-smoke',
            '-pvp-player', player, '-pvp-report', result
        ]);
        launched.child.stdout.resume();
        launched.child.stderr.resume();
        return { ...launched, result };
    });
    const timeout = new AbortController();
    try {
        const exits = await Promise.race([
            Promise.all(clients.map(client => client.finished)),
            delay(45000, undefined, { signal: timeout.signal }).then(() => { throw new Error('Windows PvP clients timed out.'); })
        ]);
        report.clientExits = exits;
        report.clients = await Promise.all(clients.map(async client => JSON.parse(await readFile(client.result, 'utf8'))));
        const [first, second] = report.clients;
        if (exits.some(code => code !== 0) || !first.passed || !second.passed ||
            first.slot === second.slot || !first.matchId || first.matchId !== second.matchId ||
            !first.finalSnapshot?.stateHash || first.finalSnapshot.stateHash !== second.finalSnapshot?.stateHash ||
            first.winner !== second.winner || first.finalSnapshot.serverTick !== second.finalSnapshot.serverTick)
            throw new Error('The two Windows clients did not report the same completed match.');
    } finally { timeout.abort(); }
    report.passed = true;
} catch (error) {
    report.error = error.message;
} finally {
    for (const { child, finished } of processes.reverse()) {
        if (child.exitCode === null && child.signalCode === null) child.kill();
        await finished.catch(() => {});
    }
    await writeFile(join(output, 'server.log'), logs.join(''));
    await writeFile(join(output, 'report.json'), JSON.stringify(report, null, 2));
}
console.log((report.passed ? 'TEST_PVP_PLAYERS_PASSED ' : 'TEST_PVP_PLAYERS_FAILED ') + output);
if (!report.passed) process.exitCode = 1;
