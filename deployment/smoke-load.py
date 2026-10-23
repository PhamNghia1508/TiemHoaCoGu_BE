#!/usr/bin/env python3
"""Finite local/staging smoke load. Writes require --writes; never point at production."""
import argparse
import concurrent.futures
import http.client
import json
import os
import statistics
import time
import uuid
from collections import Counter
from urllib.parse import urlsplit


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--urls', default='http://127.0.0.1:5081,http://127.0.0.1:5082')
    parser.add_argument('--requests', type=int, default=600)
    parser.add_argument('--concurrency', type=int, default=20)
    parser.add_argument('--output', default='smoke-load-results.json')
    parser.add_argument('--writes', action='store_true', help='Only use on a disposable test database')
    parser.add_argument('--limit-urls', default='', help='Optional two instances configured with shared PermitLimit=5')
    args = parser.parse_args()
    urls = args.urls.split(',')
    if args.requests < 1 or args.concurrency < 1:
        parser.error('requests and concurrency must be positive')

    def request(base, path, method='GET', body=None, token=None, source=None):
        u = urlsplit(base)
        if u.scheme not in ('http', 'https') or not u.hostname:
            raise ValueError('Invalid test URL')
        cls = http.client.HTTPSConnection if u.scheme == 'https' else http.client.HTTPConnection
        connection = cls(u.hostname, u.port or (443 if u.scheme == 'https' else 80), timeout=30, source_address=(source, 0) if source else None)
        headers = {'Accept': 'application/json'}
        if token:
            headers['Authorization'] = 'Bearer ' + token
        data = None
        if body is not None:
            headers['Content-Type'] = 'application/json'
            data = json.dumps(body).encode()
        start = time.perf_counter()
        try:
            connection.request(method, path, data, headers)
            response = connection.getresponse()
            raw = response.read()
            try:
                value = json.loads(raw) if raw else None
            except ValueError:
                value = None
            return response.status, value, (time.perf_counter() - start) * 1000
        finally:
            connection.close()

    for base in urls:
        assert request(base, '/health/ready')[0] == 200, 'Test instance is not ready'
    paths = ['/api/catalog/products?pageSize=20', '/api/catalog/categories', '/api/workshops']
    for i in range(30):
        assert request(urls[i % len(urls)], paths[i % len(paths)])[0] == 200, 'Warmup failed'

    def work(i):
        try:
            status, _, elapsed = request(urls[i % len(urls)], paths[i % len(paths)])
            return status, elapsed
        except Exception:
            return 0, 30000.0

    start = time.perf_counter()
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        rows = list(pool.map(work, range(args.requests)))
    duration = time.perf_counter() - start
    latencies = sorted(x[1] for x in rows)
    percentile = lambda fraction: latencies[min(len(latencies) - 1, int((len(latencies) - 1) * fraction))]
    results = {
        'environment': 'local/staging finite smoke; not a production capacity certification',
        'urls': urls, 'requests': len(rows), 'concurrency': args.concurrency,
        'status_counts': dict(Counter(str(x[0]) for x in rows)),
        'duration_seconds': round(duration, 3), 'requests_per_second': round(len(rows) / duration, 2),
        'latency_ms': {'median': round(statistics.median(latencies), 2), 'p95': round(percentile(.95), 2), 'p99': round(percentile(.99), 2)},
        'errors': sum(x[0] != 200 for x in rows), 'checks': {}
    }
    if args.writes:
        unique = uuid.uuid4().hex
        password = 'Local-QA-only-password-123!'
        status, user, _ = request(urls[0], '/api/auth/register', 'POST', {'email': f'load-{unique}@example.test', 'fullName': 'Disposable load customer', 'phoneNumber': '09' + str(int(unique[:8], 16)).zfill(9)[-9:], 'password': password})
        assert status == 201, ('register', status)
        token = user['accessToken']
        _, catalog, _ = request(urls[0], '/api/catalog/products?pageSize=20')
        product = next(x for x in catalog['items'] if not x['isPreOrder'])
        _, before, _ = request(urls[0], '/api/catalog/products/' + product['id'])
        assert request(urls[0], '/api/cart', 'POST', {'productId': product['id'], 'quantity': 1}, token)[0] == 204
        code, cart, _ = request(urls[-1], '/api/cart', token=token)
        assert code == 200 and any(x['productId'] == product['id'] for x in cart), 'Shared cart/token did not work'
        checkout = {'shippingAddress': 'Disposable QA address', 'receiverName': 'QA', 'receiverPhone': '0900000008', 'idempotencyKey': unique}
        status, order, _ = request(urls[-1], '/api/orders/checkout', 'POST', checkout, token)
        assert status == 200, ('checkout', status, order)
        status, replay, _ = request(urls[0], '/api/orders/checkout', 'POST', checkout, token)
        assert status == 200 and replay['id'] == order['id'], 'Cross-instance checkout replay failed'
        assert request(urls[0], '/api/orders/' + order['id'] + '/cancel', 'POST', {}, token)[0] == 204
        assert request(urls[-1], '/api/orders/' + order['id'] + '/cancel', 'POST', {}, token)[0] == 204
        _, after, _ = request(urls[-1], '/api/catalog/products/' + product['id'])
        assert before['quantityAvailable'] == after['quantityAvailable'], 'Inventory was not restored exactly once'
        results['checks'].update({'cross_instance_auth_cart': True, 'cross_instance_checkout_replay': True, 'cross_instance_cancel_replay_restores_stock_once': True})

    if args.limit_urls:
        limit_urls = args.limit_urls.split(',')
        # A distinct loopback client keeps this proof independent of warmup/load counters.
        statuses = [request(limit_urls[i % len(limit_urls)], '/api/catalog/categories', source='127.0.0.29')[0] for i in range(8)]
        results['checks']['shared_limit_5_across_two_processes'] = statuses == [200] * 5 + [429] * 3
        results['shared_limit_statuses'] = statuses
        assert results['checks']['shared_limit_5_across_two_processes'], statuses
    results['passed'] = results['errors'] == 0 and all(results['checks'].values())
    with open(args.output, 'w', encoding='utf-8') as stream:
        json.dump(results, stream, ensure_ascii=False, indent=2)
        stream.write('\n')
    print(json.dumps(results, ensure_ascii=False, indent=2))
    return 0 if results['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
