import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
const base = process.argv[2] ?? 'http://localhost:5211';
const manifestText = await (await fetch(base + '/service-worker-assets.js')).text();
const manifest = JSON.parse(manifestText.slice(manifestText.indexOf('{'), manifestText.lastIndexOf('}') + 1));
const failed = [];
for (let i = 0; i < manifest.assets.length; i += 12) {
    await Promise.all(manifest.assets.slice(i, i + 12).map(async asset => {
        const response = await fetch(base + '/' + asset.url);
        const bytes = Buffer.from(await response.arrayBuffer());
        const hash = 'sha256-' + createHash('sha256').update(bytes).digest('base64');
        if (!response.ok || hash !== asset.hash) failed.push({url:asset.url,status:response.status,hashMatches:hash===asset.hash});
    }));
}
assert.deepEqual(failed, [], 'All offline-cache assets must be reachable and match their integrity hashes.');
const html = await (await fetch(base + '/')).text();
assert.ok(!html.includes('{fingerprint}'), 'Published HTML cannot contain unresolved SDK placeholders.');
assert.ok(html.includes('_framework/blazor.webassembly.js'));
console.log('Verified ' + manifest.assets.length + ' published assets and startup script.');
