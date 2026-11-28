import { spawn } from 'node:child_process';
import { cp, mkdir, readFile, writeFile, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import { basename, dirname, join, resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const [serverDll, playerExe, reportPath, mode = 'rain-pass', scenario] = process.argv.slice(2);
if (!serverDll || !playerExe || !reportPath || !['rain-pass', 'bbads', 'tournament'].includes(mode) ||
    scenario && (mode !== 'tournament' || scenario !== 'split-restart'))
    throw new Error('Pass Test.Server.dll, the Windows Player, a new report directory and the mode.');

const output = resolve(reportPath);
await mkdir(output);
const portProbe = createServer();
await new Promise((ready, fail) => portProbe.once('error', fail).listen(0, '127.0.0.1', ready));
const { port } = portProbe.address();
await new Promise(ready => portProbe.close(ready));
const serverUrl = 'http://127.0.0.1:' + port;
const config = join(output, 'server.json');
const saveDirectory = join(output, 'tournaments');
const stagedDirectory = join(output, 'server-runtime');
const serverAssembly = join(stagedDirectory, basename(serverDll));
await writeFile(config, JSON.stringify({ pvpPort: port, pvpTournamentDirectory: saveDirectory }));

const processes = [];
const report = { mode, scenario, passed: false };
const logs = [];

function launch(executable, args) {
    const child = spawn(executable, args, { windowsHide: true });
    const finished = new Promise((ready, fail) => child.once('error', fail).once('close', ready));
    finished.catch(() => {});
    const process = { child, finished };
    processes.push(process);
    return process;
}

async function startServer() {
    const server = launch('dotnet', [serverAssembly, '--config', config, '--run-for-seconds', '105']);
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
    return server;
}

async function interruptedRound(clients) {
    for (let attempt = 0; attempt < 300; attempt++) {
        const text = await readFile(clients[0].log, 'utf8').catch(() => '');
        const match = text.match(/PVP_SMOKE_ROUND_ACTIVE mode=bbads matchId=([a-z0-9]+)/);
        if (match) return match[1];
        if (clients.some(client => client.child.exitCode !== null))
            throw new Error('A Player stopped before BBads started.');
        await delay(100);
    }
    throw new Error('BBads did not start before the restart deadline.');
}

try {
    await cp(dirname(resolve(serverDll)), stagedDirectory, { recursive: true });
    const server = await startServer();
    const clients = ['alpha', 'beta'].map(player => {
        const result = join(output, player + '.json');
        const log = join(output, player + '.log');
        const launched = launch(resolve(playerExe), [
            '-batchmode', '-nographics', '-logFile', log,
            '-pvp-smoke', mode, '-pvp-server', serverUrl, '-pvp-room', 'player-smoke',
            '-pvp-player', player, '-pvp-report', result, ...(scenario ? ['-pvp-split'] : [])
        ]);
        launched.child.stdout.resume();
        launched.child.stderr.resume();
        return { ...launched, result, log };
    });
    if (scenario === 'split-restart') {
        report.interruptedMatchId = await interruptedRound(clients);
        server.child.kill();
        await server.finished;
        await startServer();
    }
    const timeout = new AbortController();
    try {
        const exits = await Promise.race([
            Promise.all(clients.map(client => client.finished)),
            delay(mode === 'tournament' ? 95000 : 45000, undefined, { signal: timeout.signal })
                .then(() => { throw new Error('Windows PvP clients timed out.'); })
        ]);
        report.clientExits = exits;
        report.clients = await Promise.all(clients.map(async client => JSON.parse(await readFile(client.result, 'utf8'))));
        const [first, second] = report.clients;
        if (exits.some(code => code !== 0) || !first.passed || !second.passed || first.slot === second.slot ||
            first.winner !== second.winner) throw new Error('A Windows client did not report the expected result.');
        if (mode === 'tournament') {
            const summary = client => client.roundFinals.map(round =>
                [round.matchId, round.serverTick, round.stateHash, round.localSimulationTick]);
            if (!first.tournamentId || first.tournamentId !== second.tournamentId ||
                first.firstScore !== second.firstScore || first.secondScore !== second.secondScore ||
                JSON.stringify(summary(first)) !== JSON.stringify(summary(second)))
                throw new Error('The two Windows clients did not report the same tournament.');
            if (report.interruptedMatchId) {
                const replayed = first.roundFinals[1].matchId;
                if (replayed === report.interruptedMatchId ||
                    !first.snapshots.some(snapshot => snapshot.matchId === report.interruptedMatchId) ||
                    !report.clients.every(client => client.tournamentStages.includes(`Running:2:${replayed}:1:0`)))
                    throw new Error('The interrupted BBads match was not replayed with a fresh match ID and the saved score.');
            }
            const verifier = launch('dotnet', [serverAssembly, '--config', config, '--verify-pvp-tournament', 'player-smoke']);
            let verification = '';
            verifier.child.stdout.on('data', data => { verification += data.toString(); });
            verifier.child.stderr.on('data', data => { verification += data.toString(); });
            const exit = await verifier.finished;
            report.replayVerification = verification.trim();
            if (exit !== 0 || !verification.includes('pvp_tournament_replay_verified'))
                throw new Error('Offline Rain Pass replay verification failed.');
        } else if (!first.matchId || first.matchId !== second.matchId || !first.finalSnapshot?.stateHash ||
            first.finalSnapshot.stateHash !== second.finalSnapshot?.stateHash ||
            first.finalSnapshot.serverTick !== second.finalSnapshot?.serverTick) {
            throw new Error('The two Windows clients did not report the same completed match.');
        }
    } finally { timeout.abort(); }
    report.passed = true;
} catch (error) {
    report.error = error.message;
} finally {
    for (const { child, finished } of processes.reverse()) {
        if (child.exitCode === null && child.signalCode === null) child.kill();
        await finished.catch(() => {});
    }
    await Promise.all([saveDirectory, stagedDirectory].map(path => rm(path, { recursive: true, force: true })));
    await writeFile(join(output, 'server.log'), logs.join(''));
    await writeFile(join(output, 'report.json'), JSON.stringify(report, null, 2));
}
console.log((report.passed ? 'TEST_PVP_PLAYERS_PASSED ' : 'TEST_PVP_PLAYERS_FAILED ') + output);
if (!report.passed) process.exitCode = 1;
