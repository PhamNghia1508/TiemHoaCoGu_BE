'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const source = fs.readFileSync('HoaSap.API/wwwroot/shop/shop.js', 'utf8');

class Element {
  constructor(tag = 'div') { this.tagName = tag.toUpperCase(); this.children = []; this.listeners = {}; this.value = ''; this.name = ''; this.disabled = false; this.className = ''; this.classList = { add() {}, remove() {} }; }
  append(...nodes) { this.children.push(...nodes.flat().filter(Boolean)); }
  replaceChildren(...nodes) { this.children = nodes.filter(Boolean); }
  addEventListener(name, fn) { this.listeners[name] = fn; }
  querySelector(selector) {
    if (selector === 'button[type=submit]') return this.walk().find(x => x.tagName === 'BUTTON' && x.type === 'submit');
    if (selector === '.form-error') return this.walk().find(x => x.className === 'form-error');
    if (selector.includes('input')) return this.walk().find(x => ['INPUT', 'TEXTAREA', 'SELECT'].includes(x.tagName));
    return null;
  }
  walk() { return [this, ...this.children.flatMap(x => x?.walk ? x.walk() : [])]; }
  showModal() {}
  close() {}
  setAttribute(key, value) { this[key] = value; }
  focus() {}
}

function response(status, body = {}) {
  return { status, ok: status >= 200 && status < 300, json: async () => body, text: async () => JSON.stringify(body) };
}

function loadApp(fetchImpl) {
  const ids = new Map();
  const document = {
    querySelector(selector) {
      if (!ids.has(selector)) ids.set(selector, new Element());
      return ids.get(selector);
    },
    createElement: tag => new Element(tag),
  };
  const crypto = { randomUUID: () => `key-${Math.random()}` };
  const context = {
    console, document, crypto, fetch: fetchImpl, setTimeout, clearTimeout,
    URLSearchParams, URL, location: { search: '', pathname: '/shop/', origin: 'https://shop.test' },
    history: { replaceState() {} }, Intl, Math, Date,
    FormData: class FormData {
      constructor(form) { this.values = {}; for (const x of form.walk()) if (x.name) this.values[x.name] = x.value; }
      [Symbol.iterator]() { return Object.entries(this.values)[Symbol.iterator](); }
    },
  };
  vm.createContext(context);
  // Suppress only the final startup call, not the init function declaration.
  let instrumented = source.replace(/\binit\(\);(?=\s*\}\)\(\);\s*$)/, '');
  instrumented = instrumented.replace(/\}\)\(\);\s*$/, 'globalThis.__test={api,refreshAccess,beginSession,logout,workshopDetail,s};})();');
  vm.runInContext(instrumented, context, { filename: 'shop.js' });
  return { app: context.__test, document };
}

const tick = () => new Promise(resolve => setImmediate(resolve));

function deferred() { let resolve, reject; const promise = new Promise((r, j) => { resolve = r; reject = j; }); return { promise, resolve, reject }; }

test('concurrent 401 requests share exactly one refresh flight', async () => {
  let refreshes = 0, resources = 0; const calls = [];
  const { app } = loadApp(async (path, init) => {
    calls.push({ path, init });
    if (path === '/api/auth/refresh') { refreshes++; return response(200, { accessToken: 'rotated', refreshToken: 'r2' }); }
    if (path === '/resource') return ++resources <= 2 ? response(401) : response(200, { ok: true });
    return response(200, { ok: true });
  });
  app.s.token = 'old'; app.s.refresh = 'r1';
  const [a, b] = await Promise.all([app.api('/resource'), app.api('/resource')]);
  assert.deepEqual([a, b], [{ ok: true }, { ok: true }]);
  assert.equal(refreshes, 1);
  assert.equal(calls.filter(x => x.path === '/resource').length, 4);
});

test('delayed 401 from rotated-token request retries with new token without another refresh', async () => {
  const first = deferred(); let refreshes = 0; const auth = [];
  const { app } = loadApp(async (path, init) => {
    if (path === '/api/auth/refresh') { refreshes++; return response(200, { accessToken: 'unexpected', refreshToken: 'r2' }); }
    if (path === '/resource' && auth.length === 0) { auth.push(init.headers.Authorization); return first.promise; }
    if (path === '/resource') { auth.push(init.headers.Authorization); return response(200, { ok: true }); }
    return response(200, { ok: true });
  });
  app.s.token = 'old'; app.s.refresh = 'r1';
  const pending = app.api('/resource');
  await tick(); app.s.token = 'new';
  first.resolve(response(401));
  assert.deepEqual(await pending, { ok: true });
  assert.equal(refreshes, 0);
  assert.deepEqual(auth, ['Bearer old', 'Bearer new']);
});

test('a rejected old refresh cannot log out a newer login', async () => {
  const refresh = deferred();
  const { app } = loadApp(async path => path === '/api/auth/refresh' ? refresh.promise : response(401));
  app.s.token = 'old'; app.s.refresh = 'r1';
  const pending = app.api('/resource').catch(error => error);
  await tick(); app.beginSession(); app.s.token = 'new'; app.s.refresh = 'r-new'; app.s.user = { id: 2 };
  refresh.reject(new Error('expired'));
  const error = await pending;
  assert.equal(error.message, 'expired');
  assert.equal(app.s.token, 'new');
  assert.equal(app.s.refresh, 'r-new');
  assert.deepEqual(app.s.user, { id: 2 });
});

test('logout invalidates auth immediately and a pending refresh cannot resurrect it', async () => {
  const refresh = deferred(); let logoutCalls = 0;
  const { app } = loadApp(async path => {
    if (path === '/api/auth/refresh') return refresh.promise;
    if (path === '/api/auth/logout') { logoutCalls++; return response(204); }
    return response(401);
  });
  app.s.token = 'old'; app.s.refresh = 'r1'; app.s.user = { id: 1 };
  const pending = app.api('/resource').catch(error => error);
  await tick();
  const loggingOut = app.logout(false);
  assert.equal(app.s.token, null); assert.equal(app.s.refresh, null); assert.equal(app.s.user, null);
  refresh.resolve(response(200, { accessToken: 'resurrected', refreshToken: 'bad' }));
  await loggingOut; await pending;
  assert.equal(logoutCalls, 0);
  assert.equal(app.s.token, null); assert.equal(app.s.refresh, null); assert.equal(app.s.user, null);
});

test('duplicate workshop submissions are skipped while one request is in flight', async () => {
  const booking = deferred(); let calls = 0;
  const { app, document } = loadApp(async path => { if (path.startsWith('/api/bookings/')) { calls++; return booking.promise; } return response(200, {}); });
  app.s.user = { fullName: 'A', phoneNumber: '0900', email: 'a@test' };
  app.workshopDetail({ name: 'Workshop' }, [{ id: 'session-1', sessionDate: '2026-01-01', startTime: '09:00' }]);
  const form = document.querySelector('#modalBody').children[0];
  const submit = { preventDefault() {} };
  const event = { preventDefault() {} };
  form.listeners.submit(event); form.listeners.submit(event);
  await tick(); assert.equal(calls, 1);
  booking.resolve(response(200, {})); await tick();
});

test('workshop uncertain retry preserves key and payload; semantic change gets a new key', async () => {
  const requests = []; const { app, document } = loadApp(async (path, init) => { requests.push({ path, init }); return response(503, { message: 'uncertain' }); });
  app.s.user = { fullName: 'A', phoneNumber: '0900', email: 'a@test' };
  app.workshopDetail({ name: 'Workshop' }, [{ id: 'session-1', sessionDate: '2026-01-01', startTime: '09:00' }]);
  const form = document.querySelector('#modalBody').children[0], event = { preventDefault() {} };
  await form.listeners.submit(event); await form.listeners.submit(event);
  const bookingRequests = () => requests.filter(x => x.path.startsWith('/api/bookings/'));
  const first = bookingRequests()[0], second = bookingRequests()[1];
  assert.equal(first.init.headers['Idempotency-Key'], second.init.headers['Idempotency-Key']);
  assert.equal(first.init.body, second.init.body);
  const fields = form.walk().filter(x => x.name); fields.find(x => x.name === 'ticketCount').value = '2';
  await form.listeners.submit(event);
  const third = bookingRequests()[2];
  assert.ok(third);
  assert.notEqual(JSON.parse(third.init.body).idempotencyKey, JSON.parse(first.init.body).idempotencyKey);
  assert.equal(third.init.headers['Idempotency-Key'], JSON.parse(third.init.body).idempotencyKey);
});
