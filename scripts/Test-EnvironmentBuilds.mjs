import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile, realpath, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { createRequire } from 'node:module';

const require = createRequire(new URL('../frontend/made-in-minas/package.json', import.meta.url));
const { chromium, devices } = require('@playwright/test');
const buildRoot = path.resolve(process.argv[2] ?? '.local/environment-builds');
const browser = await chromium.launch({ channel: process.env.PLAYWRIGHT_CHANNEL || (process.env.CI ? undefined : 'msedge') });
const results = [];
const mimeTypes = { '.html':'text/html', '.js':'text/javascript', '.css':'text/css', '.json':'application/json', '.svg':'image/svg+xml', '.png':'image/png', '.woff2':'font/woff2' };

try {
  for (const environment of ['production', 'staging']) {
    const root = await realpath(path.join(buildRoot, environment, 'browser'));
    const server = createServer(async (request, response) => {
      try {
        const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
        const file = path.resolve(root, '.' + pathname);
        if (!file.startsWith(root + path.sep) && file !== root) {
          response.writeHead(400).end();
          return;
        }
        if (pathname.startsWith('/api/')) {
          response.writeHead(404).end();
          return;
        }
        const isFile = await stat(file).then(info => info.isFile()).catch(() => false);
        const target = isFile ? file : path.join(root, 'index.html');
        response.writeHead(200, { 'Content-Type':mimeTypes[path.extname(target)] ?? 'application/octet-stream', 'Cache-Control':'no-store' });
        response.end(await readFile(target));
      } catch {
        response.writeHead(500).end();
      }
    });
    await new Promise((resolve, reject) => {
      server.once('error', reject);
      server.listen(0, '127.0.0.1', resolve);
    });
    const origin = `http://127.0.0.1:${server.address().port}`;
    try {
      for (const [device, options] of [['desktop',{viewport:{width:1280,height:900}}], ['mobile',devices['iPhone 13']]]) {
        const context = await browser.newContext(options);
        try {
          const apiCalls = [];
          const errors = [];
          await context.route('**/api/**', async route => {
            const request = route.request();
            const url = new URL(request.url());
            apiCalls.push({origin:url.origin,method:request.method()});
            const data = url.pathname === '/api/menu' ? {categories:[],items:[],page:1,pageSize:24,totalCount:0}
              : url.pathname === '/api/store-attendance/public' ? {isPaused:false,isOpen:false,acceptingOrders:false,hours:[],paymentMethods:['Card','Cash','Pix'],deliveryEstimateMessage:'Prazo conforme a demanda.'}
              : url.pathname === '/api/public-checkout/delivery-areas' ? []
              : url.pathname === '/api/system/status' ? {status:'available',environment:environment === 'staging' ? 'Staging' : 'Production',version:'test-build'} : null;
            await route.fulfill({status:data === null ? 404 : 200,json:data});
          });
          const page = await context.newPage();
          page.on('pageerror', error => errors.push(error.name));
          for (const route of ['/entrar','/pedido','/privacidade']) {
            await page.goto(origin + route);
            await page.locator('h1').first().waitFor();
            const notice = page.getByRole('note', {name:'Ambiente do sistema'});
            assert.equal(await notice.count(), environment === 'staging' ? 1 : 0);
            if (environment === 'staging') assert.match(await notice.innerText(), /TESTE.*HOMOLOGA/);
            const bounds = await page.evaluate(() => {
              const box = document.querySelector('ion-app > .ion-page').getBoundingClientRect();
              return {top:box.top,bottom:box.bottom,height:innerHeight,width:innerWidth,scrollWidth:document.documentElement.scrollWidth};
            });
            assert.ok(Math.abs(bounds.top - (environment === 'staging' ? 32 : 0)) <= 1, 'Page must start below the environment notice.');
            assert.ok(bounds.bottom <= bounds.height + 1, 'Page must fit without hiding its bottom.');
            assert.ok(bounds.scrollWidth <= bounds.width + 1, 'Page must not overflow horizontally.');
          }
          assert.ok(apiCalls.length > 0, 'Public menu must load through the API.');
          assert.ok(apiCalls.every(call => call.origin === origin && call.method === 'GET'), 'Built frontend must use its own API origin without writes.');
          assert.deepEqual(errors, []);
          await page.screenshot({path:path.join(buildRoot, `${environment}-${device}.png`)});
          results.push({environment,device,routes:3,passed:true});
        } finally { await context.close(); }
      }
    } finally { await new Promise(resolve => server.close(resolve)); }
  }
  await writeFile(path.join(buildRoot, 'verification.json'), JSON.stringify({checkedAt:new Date().toISOString(),results},null,2));
  console.log(JSON.stringify({builds:2,devices:2,routesVerified:12,apiWrites:0,results}));
} finally { await browser.close(); }
