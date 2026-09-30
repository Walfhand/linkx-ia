import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';

export const analysisAssembly = 'tools/LinkxAi.Analysis/bin/Release/net10.0/LinkxAi.Analysis.dll';

export function analysisWorker(model, assembly = analysisAssembly) {
    const child = spawn('dotnet', [assembly, ...(model ? [model] : [])], { stdio: ['pipe', 'pipe', 'inherit'] });
    const pending = [];
    let stopped = false;
    function fail(error) {
        stopped = true;
        for (const request of pending.splice(0)) request.reject(error);
    }
    createInterface({ input: child.stdout }).on('line', line => {
        const request = pending.shift();
        if (!request) { fail(new Error('Unexpected analysis response.')); child.kill(); return; }
        try {
            const result = JSON.parse(line);
            if (result.error) throw new Error(result.error);
            request.resolve(result);
        } catch (error) { request.reject(error); }
    });
    child.on('error', fail);
    child.stdin.on('error', fail);
    child.on('exit', code => fail(new Error(`Analysis worker exited: ${code}`)));
    return {
        analyze(request) {
            if (stopped) return Promise.reject(new Error('Analysis worker has stopped.'));
            return new Promise((resolve, reject) => {
                pending.push({ resolve, reject });
                child.stdin.write(JSON.stringify(request) + '\n');
            });
        },
        stop() { fail(new Error('Analysis worker stopped.')); child.stdin.end(); child.kill(); },
    };
}
