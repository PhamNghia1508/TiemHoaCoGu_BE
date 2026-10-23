#!/usr/bin/env python3
"""Finite two-instance workshop booking race; local disposable DB only.

This script performs writes only with an explicit --writes flag. It is deliberately
restricted to loopback HTTP URLs and requires an admin account from environment
variables (ADMIN_EMAIL and ADMIN_PASSWORD, or the explicit flag equivalents).
"""
import argparse
import concurrent.futures
import http.client
import json
import os
import sys
import time
import uuid
from urllib.parse import urlsplit


def fail(message):
    raise RuntimeError(message)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--urls", default="http://127.0.0.1:5081,http://127.0.0.1:5082",
                        help="exactly two HTTP loopback API URLs")
    parser.add_argument("--admin-email", default=os.getenv("ADMIN_EMAIL"),
                        help="bootstrap admin email (default: ADMIN_EMAIL)")
    parser.add_argument("--admin-password", default=os.getenv("ADMIN_PASSWORD"),
                        help="bootstrap admin password (default: ADMIN_PASSWORD)")
    parser.add_argument("--output", default="deployment/results/workshop-race.json")
    parser.add_argument("--writes", action="store_true",
                        help="required: use only with a disposable local test database")
    args = parser.parse_args()

    if not args.writes:
        parser.error("refusing write smoke test without --writes")
    if not args.admin_email or not args.admin_password:
        parser.error("set ADMIN_EMAIL and ADMIN_PASSWORD (or pass both flags); credentials are never printed")
    urls = [x.strip().rstrip("/") for x in args.urls.split(",") if x.strip()]
    if len(urls) != 2:
        parser.error("--urls must contain exactly two comma-separated URLs")

    parsed = []
    for base in urls:
        u = urlsplit(base)
        if u.scheme != "http" or u.hostname not in {"127.0.0.1", "localhost", "::1"}:
            parser.error("all URLs must be plain HTTP loopback addresses; production/network URLs are refused")
        parsed.append(u)

    def request(base, path, method="GET", body=None, token=None):
        u = urlsplit(base)
        conn = http.client.HTTPConnection(u.hostname, u.port or 80, timeout=30)
        headers = {"Accept": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        payload = None
        if body is not None:
            headers["Content-Type"] = "application/json"
            payload = json.dumps(body).encode("utf-8")
        try:
            conn.request(method, path, payload, headers)
            response = conn.getresponse()
            raw = response.read()
            try:
                value = json.loads(raw) if raw else None
            except (ValueError, UnicodeDecodeError):
                value = None
            return response.status, value
        finally:
            conn.close()

    def expect(status, expected, label, value=None):
        if status not in expected:
            detail = "" if value is None else " response=" + json.dumps(value, ensure_ascii=False)[:500]
            fail(f"{label}: expected {sorted(expected)}, got {status}{detail}")

    # Readiness is intentionally checked before any write.
    for url in urls:
        status, _ = request(url, "/health/ready")
        expect(status, {200}, f"{url} readiness")

    # Admin login is the only use of the supplied credential. Do not include it in output.
    status, admin = request(urls[0], "/api/auth/login", "POST",
                            {"email": args.admin_email, "password": args.admin_password})
    expect(status, {200}, "admin login", admin)
    admin_token = (admin or {}).get("accessToken")
    if not admin_token:
        fail("admin login response did not contain accessToken")

    stamp = uuid.uuid4().hex
    workshop_body = {
        "code": "RACE-" + stamp[:12], "name": "Disposable workshop race " + stamp[:8],
        "basePrice": 100000, "duration": 60, "maxCapacity": 1,
        "description": "Finite local acceptance smoke data", "isActive": True,
    }
    status, workshop = request(urls[0], "/api/workshops", "POST", workshop_body, admin_token)
    expect(status, {200, 201}, "create workshop", workshop)
    workshop_id = (workshop or {}).get("id")
    if not workshop_id:
        fail("create workshop response did not contain id")

    session_body = {
        "sessionDate": "2099-12-31", "startTime": "10:00:00",
        "endTime": "11:00:00", "location": "Disposable QA Studio",
        "capacity": 1,
    }
    status, session = request(urls[1], f"/api/workshops/{workshop_id}/sessions",
                             "POST", session_body, admin_token)
    expect(status, {200, 201}, "create session", session)
    session_id = (session or {}).get("id")
    if not session_id:
        fail("create session response did not contain id")

    def register(label):
        email = f"workshop-race-{stamp}-{label}@example.test"
        phone = 100000000 + (int(stamp[:8], 16) % 899999998) + (0 if label == "one" else 1)
        body = {"email": email, "fullName": f"Disposable {label}",
                "phoneNumber": "09" + str(phone).zfill(9),
                "password": "Disposable-QA-Only-123!"}
        status, value = request(urls[0], "/api/auth/register", "POST", body)
        expect(status, {200, 201}, f"register {label}", value)
        token = (value or {}).get("accessToken")
        if not token:
            fail(f"register {label} response did not contain accessToken")
        return token

    customer_one = register("one")
    customer_two = register("two")
    booking_body = {"ticketCount": 1, "customerName": "Race customer",
                    "customerPhone": "0901234567"}

    # Distinct customers, distinct keys, and one request per API process.
    def race_call(args_tuple):
        url, token, key = args_tuple
        body = dict(booking_body, idempotencyKey=key)
        status, value = request(url, f"/api/bookings/sessions/{session_id}",
                                "POST", body, token)
        return {"url": url, "status": status, "value": value}

    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        race = list(pool.map(race_call, [
            (urls[0], customer_one, "race-" + stamp + "-one"),
            (urls[1], customer_two, "race-" + stamp + "-two"),
        ]))
    winners = [x for x in race if x["status"] in {200, 201}]
    conflicts = [x for x in race if x["status"] == 409]
    if len(winners) != 1 or len(conflicts) != 1:
        fail("concurrent booking did not produce exactly one winner and one 409 conflict: " +
             json.dumps([{"url": x["url"], "status": x["status"]} for x in race]))
    winner = winners[0]
    winner_id = (winner["value"] or {}).get("id")
    if not winner_id:
        fail("winning booking response did not contain id")

    # Replay the winning request on the peer instance: same customer/key must return same id.
    winner_token = customer_one if winner["value"] is not None and winner["url"] == urls[0] else customer_two
    winner_key = "race-" + stamp + ("-one" if winner["url"] == urls[0] else "-two")
    replay_status, replay = request(urls[1] if winner["url"] == urls[0] else urls[0],
                                    f"/api/bookings/sessions/{session_id}", "POST",
                                    dict(booking_body, idempotencyKey=winner_key), winner_token)
    expect(replay_status, {200}, "winner replay on peer", replay)
    if (replay or {}).get("id") != winner_id:
        fail("winner replay returned a different booking id")

    # Cancel twice across peers. Both must be successful and only one release may occur.
    cancel_peer = urls[1] if winner["url"] == urls[0] else urls[0]
    cancel_status, _ = request(winner["url"], f"/api/bookings/{winner_id}/cancel",
                               "POST", "acceptance smoke cancel", winner_token)
    expect(cancel_status, {200, 204}, "first cancel", None)
    cancel_replay_status, _ = request(cancel_peer, f"/api/bookings/{winner_id}/cancel",
                                      "POST", "acceptance smoke cancel replay", winner_token)
    expect(cancel_replay_status, {200, 204}, "cancel replay", None)

    session_status, sessions = request(urls[0], f"/api/workshops/{workshop_id}/sessions",
                                       token=admin_token)
    expect(session_status, {200}, "session state after cancel replay", sessions)
    current = next((x for x in (sessions or []) if x.get("id") == session_id), None)
    if current is None or current.get("bookedSlots") != 0:
        fail("cancel replay did not leave exactly zero booked slots")

    fresh_status, fresh = request(urls[0], f"/api/bookings/sessions/{session_id}", "POST",
                                  dict(booking_body, customerName="Fresh second customer",
                                       idempotencyKey="fresh-" + stamp), customer_two if winner["url"] == urls[0] else customer_one)
    expect(fresh_status, {200, 201}, "fresh second customer after cancel", fresh)
    if not (fresh or {}).get("id"):
        fail("fresh booking response did not contain id")

    result = {
        "environment": "local disposable finite workshop acceptance smoke; not production",
        "urls": urls, "workshop_id": workshop_id, "session_id": session_id,
        "race_statuses": [x["status"] for x in race], "winner_booking_id": winner_id,
        "checks": {
            "one_winner_one_conflict": True,
            "winner_replay_same_id_across_peer": True,
            "cancel_replay_successful": True,
            "fresh_customer_gets_seat_after_cancel": True,
            "cancel_release_once": True,
        },
        "passed": True,
    }
    os.makedirs(os.path.dirname(args.output) or ".", exist_ok=True)
    with open(args.output, "w", encoding="utf-8") as stream:
        json.dump(result, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (AssertionError, RuntimeError, OSError, ValueError) as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
