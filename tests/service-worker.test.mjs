import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import { test } from 'node:test';
const worker = readFileSync(new URL('../src/Nagapie.BraindumpLite.Client/wwwroot/service-worker.published.js', import.meta.url), 'utf8');
function setup() {
    const events = {}, entries = new Map(), cached = [], network = [];
    const context = {
        self: { origin:'https://nagapie.test', assetsManifest:{version:'test',assets:[{url:'index.html',hash:'sha256-test'},{url:'app.css',hash:'sha256-test'},{url:'icon.svg',hash:'sha256-test'},{url:'service-worker.js',hash:'sha256-test'}]}, importScripts() {}, addEventListener(name, handler) {events[name] = handler;} },
        URL, console:{info() {}},
        Request: class { constructor(url, options) {this.url = url; this.options = options;} },
        caches: {
            async keys() {return ['offline-cache-old','another-app-cache'];},
            async delete(key) {cached.push('delete:' + key);},
            async open() {return {
                async addAll(requests) {for (const r of requests) {cached.push(r.url); entries.set(r.url, {body:r.url});}},
                async match(request) {return entries.get(typeof request === 'string' ? request : new URL(request.url).pathname.slice(1));}
            };}
        },
        async fetch(request) {network.push(request); return {body:'network'};}
    };
    vm.runInNewContext(worker, context);
    const install = async () => {let done; events.install({waitUntil(p){done=p;}}); await done;};
    const get = async request => {let response; events.fetch({request, respondWith(p){response=p;}}); return await response;};
    return {events,cached,network,install,get};
}
test('installation caches app assets and SVG but never the worker itself',async () => {
    const s=setup(); await s.install(); assert.deepEqual(s.cached,['index.html','app.css','icon.svg']);
});
test('offline navigation serves cached app shell',async () => {
    const s=setup(); await s.install(); const response=await s.get({method:'GET',mode:'navigate',url:'https://nagapie.test/list'});
    assert.equal(response.body,'index.html'); assert.equal(s.network.length,0);
});
test('API calls always use network and never app-shell fallback',async () => {
    const s=setup(); await s.install(); const response=await s.get({method:'GET',mode:'navigate',url:'https://nagapie.test/api/config'});
    assert.equal(response.body,'network'); assert.equal(s.network.length,1);
});
test('activation deletes only older Nagapie offline caches',async () => {
    const s=setup(); let done; s.events.activate({waitUntil(p){done=p;}}); await done;
    assert.deepEqual(s.cached,['delete:offline-cache-old']);
});
