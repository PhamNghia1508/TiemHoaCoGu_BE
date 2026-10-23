import http from 'k6/http';
import { check, sleep } from 'k6';

// Run only against disposable local/staging data, with suitable staging quotas.
export const options = {
  vus: Number(__ENV.VUS || 5),
  duration: __ENV.DURATION || '30s',
  thresholds: { http_req_failed: ['rate<0.02'], http_req_duration: ['p(95)<800'] },
};
const bases = (__ENV.BASE_URLS || __ENV.BASE_URL || 'http://localhost:5080').split(',');
const writes = __ENV.RUN_WRITES === 'true';

export function setup() {
  if (!__ENV.EMAIL || !__ENV.PASSWORD) {
    if (writes) throw new Error('Write tests require a dedicated EMAIL/PASSWORD and PRODUCT_ID');
    return { token: null, run: Date.now().toString() };
  }
  const response = http.post(`${bases[0]}/api/auth/login`, JSON.stringify({ email: __ENV.EMAIL, password: __ENV.PASSWORD }), { headers: { 'Content-Type': 'application/json' } });
  if (response.status !== 200) throw new Error('Dedicated staging login failed');
  return { token: response.json('accessToken'), run: Date.now().toString() };
}

export default function (data) {
  const base = bases[(__VU + __ITER) % bases.length];
  const params = { headers: { 'Content-Type': 'application/json', ...(data.token ? { Authorization: `Bearer ${data.token}` } : {}) } };
  const catalog = http.get(`${base}/api/catalog/products?pageSize=20`, params);
  check(catalog, { 'catalog readable': r => r.status === 200 });
  const workshops = http.get(`${base}/api/workshops`, params);
  check(workshops, { 'workshops readable': r => r.status === 200 });
  if (data.token) {
    const bookings = http.get(`${base}/api/bookings`, params);
    check(bookings, { 'own bookings readable': r => r.status === 200 });
  }
  if (writes) {
    if (!__ENV.PRODUCT_ID) throw new Error('Set PRODUCT_ID on a disposable staging database');
    const order = http.post(`${base}/api/orders/checkout`, JSON.stringify({
      items: [{ productId: __ENV.PRODUCT_ID, quantity: 1 }],
      shippingAddress: 'Disposable load-test address', receiverName: 'Load QA', receiverPhone: '0900000008',
      idempotencyKey: `k6-${data.run}-${__VU}-${__ITER}`,
    }), params);
    check(order, { 'checkout succeeds': r => r.status === 200 });
    if (order.status === 200) {
      const cancel = http.post(`${base}/api/orders/${order.json('id')}/cancel`, '{}', params);
      check(cancel, { 'unpaid test order cleaned up': r => r.status === 204 });
    }
  }
  sleep(1);
}
