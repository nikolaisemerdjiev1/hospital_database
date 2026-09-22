"""Passive release checks: anonymous GETs only, no redirects, cookies, or mutations."""

import argparse
import http.client
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from email.utils import parsedate_to_datetime


class SmokeFailure(Exception):
    pass


@dataclass
class Response:
    status: int
    headers: dict
    body: bytes


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def origin(value, allow_local_http=False):
    try:
        parsed = urllib.parse.urlsplit(value)
        local_http = allow_local_http and parsed.scheme == "http" and parsed.hostname in ("localhost", "127.0.0.1", "::1")
        if (not parsed.hostname or parsed.username is not None or parsed.password is not None
                or parsed.query or parsed.fragment or parsed.path not in ("", "/")
                or (parsed.scheme != "https" and not local_http)
                or any(char.isspace() for char in value)):
            raise ValueError()
        _ = parsed.port
    except ValueError:
        raise SmokeFailure("Supply an HTTPS origin without credentials, path, query, or fragment") from None
    return value.rstrip("/")


def get(url, timeout, request_origin=None):
    headers = {"Accept": "application/json, text/plain, text/html", "User-Agent": "hospital-passive-smoke"}
    if request_origin:
        headers["Origin"] = request_origin
    request = urllib.request.Request(url, headers=headers, method="GET")
    opener = urllib.request.build_opener(NoRedirect)
    try:
        try:
            response = opener.open(request, timeout=timeout)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            body = response.read(65537)
            if len(body) > 65536:
                raise SmokeFailure("Response exceeds smoke-check size limit")
            return Response(response.code, {key.lower(): value for key, value in response.headers.items()}, body)
    except (urllib.error.URLError, OSError, TimeoutError, http.client.HTTPException):
        raise SmokeFailure("Endpoint connection failed") from None


def retry_delay(response, now):
    value = response.headers.get("retry-after")
    if value is not None:
        try:
            return min(2**31, max(0, int(value)))
        except ValueError:
            try:
                date = parsedate_to_datetime(value)
                return max(0, (date - now).total_seconds())
            except (TypeError, ValueError, OverflowError):
                pass
    return 60 if response.status == 429 else 3


def wait_ready(fetch, budget=90, clock=time.monotonic, sleep=time.sleep):
    deadline = clock() + budget
    for attempt in range(10):
        remaining = deadline - clock()
        if remaining <= 0:
            break
        try:
            response = fetch(min(10, remaining))
        except SmokeFailure:
            response = Response(503, {}, b"")
        if response.status == 200 and response.body.strip() == b"Healthy":
            return response
        if response.status not in (429, 500, 502, 503, 504):
            raise SmokeFailure("Readiness returned an unexpected response")
        delay = max(1, retry_delay(response, datetime.now(timezone.utc)))
        if attempt == 9 or clock() + delay >= deadline:
            break
        sleep(delay)
    raise SmokeFailure("Readiness did not recover within the bounded retry budget")


def security_headers(response, https):
    expected = {"x-content-type-options": "nosniff", "referrer-policy": "no-referrer"}
    if any(response.headers.get(key) != value for key, value in expected.items()):
        raise SmokeFailure("API security headers are missing")
    if "no-store" not in response.headers.get("cache-control", "").lower():
        raise SmokeFailure("API cache policy must include no-store")
    if "default-src 'none'" not in response.headers.get("content-security-policy", ""):
        raise SmokeFailure("API content security policy is missing")
    if https and not re.search(r"(?:^|;)\s*max-age=[1-9][0-9]*", response.headers.get("strict-transport-security", "")):
        raise SmokeFailure("HTTPS API must advertise HSTS")


def run_checks(api_url, expected_revision, frontend_origin, frontend_url=None,
               allow_local_http=False, budget=90):
    api = origin(api_url, allow_local_http)
    frontend = origin(frontend_origin, allow_local_http)
    site = origin(frontend_url, allow_local_http) if frontend_url else None
    if not re.fullmatch(r"[0-9a-f]{40}", expected_revision):
        raise SmokeFailure("Expected revision must be a lowercase 40-character Git SHA")
    ready = wait_ready(lambda timeout: get(api + "/health/ready", timeout, frontend), budget)
    if ready.headers.get("access-control-allow-origin") != frontend:
        raise SmokeFailure("Readiness CORS origin does not match the frontend")
    if "retry-after" not in {part.strip().lower() for part in ready.headers.get("access-control-expose-headers", "").split(",")}:
        raise SmokeFailure("Readiness must expose Retry-After to the frontend")
    live = get(api + "/health/live", 10)
    if live.status != 200 or live.body.strip() != b"Healthy":
        raise SmokeFailure("Liveness check failed")
    status = get(api + "/api/v1/system/status", 10)
    if status.status != 200 or "application/json" not in status.headers.get("content-type", ""):
        raise SmokeFailure("System status check failed")
    try:
        document = json.loads(status.body)
        timestamp = datetime.fromisoformat(document["timestamp"].replace("Z", "+00:00"))
        fresh = timestamp.utcoffset() is not None and abs((datetime.now(timezone.utc) - timestamp).total_seconds()) <= 600
    except (ValueError, KeyError, TypeError, AttributeError):
        raise SmokeFailure("System status payload is invalid") from None
    if (document.get("service") != "Hospital Coordination API" or document.get("status") != "online"
            or document.get("environment") != "Production" or document.get("revision") != expected_revision or not fresh):
        raise SmokeFailure("System identity, production environment, revision, or timestamp does not match")
    security_headers(status, api.startswith("https:"))
    protected = get(api + "/api/v1/identity/me", 10)
    if protected.status != 401:
        raise SmokeFailure("Protected identity endpoint did not reject anonymous access")
    security_headers(protected, api.startswith("https:"))
    if site:
        for path in ("/", "/app/pharmacy", "/auth/callback"):
            page = get(site + path, 10)
            if page.status != 200 or "text/html" not in page.headers.get("content-type", "") or not re.search(rb"id\s*=\s*['\"]root['\"]", page.body):
                raise SmokeFailure("Frontend application shell or deep link is unavailable")
            if ("script-src 'self'" not in page.headers.get("content-security-policy", "")
                    or "no-cache" not in page.headers.get("cache-control", "")):
                raise SmokeFailure("Frontend CSP or HTML cache policy is missing")
        for path in ("/api/missing", "/health/missing", "/assets/missing.js"):
            missing = get(site + path, 10)
            if missing.status != 404 or "text/html" in missing.headers.get("content-type", ""):
                raise SmokeFailure("Unknown API, health, or asset route returned an incorrect fallback")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api-url", required=True)
    parser.add_argument("--expected-revision", required=True)
    parser.add_argument("--frontend-origin", required=True)
    parser.add_argument("--frontend-url")
    parser.add_argument("--allow-local-http", action="store_true", help="Permit loopback HTTP for isolated container checks only")
    args = parser.parse_args()
    try:
        run_checks(**vars(args))
    except SmokeFailure as error:
        print(f"Smoke failed: {error}", file=sys.stderr)
        return 1
    print("Smoke passed: readiness, CORS, liveness, production revision, security headers, anonymous denial"
          + (", frontend shell." if args.frontend_url else ". Frontend URL not supplied; shell check skipped."))
    return 0


if __name__ == "__main__":
    sys.exit(main())
