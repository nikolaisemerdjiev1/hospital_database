import hashlib
import io
import json
import unittest
import zipfile
from datetime import datetime, timedelta, timezone
from email.utils import format_datetime
from unittest.mock import patch

import smoke
from audit_nuget import check_report
from install_tool import binary_from_archive


class ToolTests(unittest.TestCase):
    def test_checksum_is_required_before_extracting_executable(self):
        data = io.BytesIO()
        with zipfile.ZipFile(data, "w") as archive:
            archive.writestr("actionlint.exe", b"test-only-tool")
            archive.writestr("../../unexpected", b"must-not-extract")
        payload = data.getvalue()
        asset = {"asset": "fixture.zip", "sha256": hashlib.sha256(payload).hexdigest()}
        self.assertEqual(binary_from_archive(payload, asset, "actionlint.exe"), b"test-only-tool")
        asset["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "checksum mismatch"):
            binary_from_archive(payload, asset, "actionlint.exe")

    def test_missing_executable_is_not_replaced_by_other_archive_entries(self):
        data = io.BytesIO()
        with zipfile.ZipFile(data, "w") as archive:
            archive.writestr("../actionlint.exe", b"test-only-tool")
        payload = data.getvalue()
        with self.assertRaises(KeyError):
            binary_from_archive(payload, {"asset": "fixture.zip", "sha256": hashlib.sha256(payload).hexdigest()}, "actionlint.exe")


class AuditTests(unittest.TestCase):
    def test_clean_report_and_reports_without_framework_findings(self):
        self.assertEqual(check_report({"version": 1, "projects": [{"path": "test.csproj"}]}), 1)

    def test_direct_and_transitive_findings_fail(self):
        for group in ("topLevelPackages", "transitivePackages"):
            with self.subTest(group=group), self.assertRaisesRegex(ValueError, "vulnerable"):
                check_report({"version": 1, "projects": [{"path": "test.csproj", "frameworks": [{
                    group: [{"id": "test-package", "vulnerabilities": [{"severity": "Low"}]}],
                }]}]})

    def test_empty_or_errored_reports_fail(self):
        for report in ({}, {"version": 1, "projects": []}, {"version": 1, "projects": [{}]},
                       {"version": 1, "projects": [{"path": "test"}], "problems": ["offline"]}):
            with self.subTest(report=report), self.assertRaises(ValueError):
                check_report(report)


class SmokeTests(unittest.TestCase):
    def setUp(self):
        self.revision = "a" * 40
        self.headers = {
            "content-type": "application/json", "x-content-type-options": "nosniff",
            "referrer-policy": "no-referrer", "cache-control": "no-store",
            "content-security-policy": "default-src 'none'", "strict-transport-security": "max-age=31536000",
        }
        self.document = {"service": "Hospital Coordination API", "status": "online", "environment": "Production",
                         "revision": self.revision, "timestamp": datetime.now(timezone.utc).isoformat()}
        self.responses = {
            "https://api.example/health/ready": smoke.Response(200, {
                "access-control-allow-origin": "https://web.example", "access-control-expose-headers": "Retry-After",
            }, b"Healthy"),
            "https://api.example/health/live": smoke.Response(200, {}, b"Healthy"),
            "https://api.example/api/v1/system/status": smoke.Response(200, self.headers, json.dumps(self.document).encode()),
            "https://api.example/api/v1/identity/me": smoke.Response(401, self.headers, b"test-only-sensitive-body"),
        }
        for path in ("/", "/app/pharmacy", "/auth/callback"):
            self.responses["https://web.example" + path] = smoke.Response(200, {
                "content-type": "text/html", "cache-control": "no-cache",
                "content-security-policy": "default-src 'none'; script-src 'self'",
            }, b'<div id="root"></div>')
        for path in ("/api/missing", "/health/missing", "/assets/missing.js"):
            self.responses["https://web.example" + path] = smoke.Response(404, {
                "content-type": "application/problem+json",
            }, b'{}')

    def run_checks(self):
        with patch("smoke.get", side_effect=lambda url, *_: self.responses[url]) as fetch:
            smoke.run_checks("https://api.example", self.revision, "https://web.example", "https://web.example")
            return fetch

    def test_success_checks_only_the_passive_endpoint_allowlist(self):
        fetch = self.run_checks()
        self.assertEqual([call.args[0] for call in fetch.call_args_list], list(self.responses))

    def test_spa_fallback_on_api_or_missing_assets_fails(self):
        for path in ("/api/missing", "/health/missing", "/assets/missing.js"):
            with self.subTest(path=path):
                original = self.responses["https://web.example" + path]
                self.responses["https://web.example" + path] = self.responses["https://web.example/"]
                with self.assertRaisesRegex(smoke.SmokeFailure, "fallback"):
                    self.run_checks()
                self.responses["https://web.example" + path] = original

    def test_missing_frontend_csp_fails(self):
        self.responses["https://web.example/"].headers.pop("content-security-policy")
        with self.assertRaisesRegex(smoke.SmokeFailure, "Frontend CSP"):
            self.run_checks()

    def test_wrong_revision_environment_or_timestamp_fails(self):
        for key, value in (("revision", "b" * 40), ("environment", "Development"), ("timestamp", "invalid")):
            with self.subTest(key=key):
                document = {**self.document, key: value}
                self.responses["https://api.example/api/v1/system/status"] = smoke.Response(200, self.headers, json.dumps(document).encode())
                with self.assertRaises(smoke.SmokeFailure):
                    self.run_checks()

    def test_missing_security_headers_cors_or_frontend_shell_fails(self):
        for key in ("x-content-type-options", "cache-control", "strict-transport-security"):
            with self.subTest(key=key), self.assertRaises(smoke.SmokeFailure):
                smoke.security_headers(smoke.Response(200, {k: v for k, v in self.headers.items() if k != key}, b""), True)
        self.responses["https://api.example/health/ready"].headers["access-control-allow-origin"] = "*"
        with self.assertRaisesRegex(smoke.SmokeFailure, "CORS"):
            self.run_checks()
        self.responses["https://api.example/health/ready"].headers["access-control-allow-origin"] = "https://web.example"
        self.responses["https://web.example/"].body = b"wrong site"
        with self.assertRaisesRegex(smoke.SmokeFailure, "shell"):
            self.run_checks()

    def test_anonymous_access_and_redirects_fail_without_echoing_bodies(self):
        for status in (200, 302):
            self.responses["https://api.example/api/v1/identity/me"].status = status
            with self.assertRaises(smoke.SmokeFailure) as result:
                self.run_checks()
            self.assertNotIn("test-only-sensitive-body", str(result.exception))
        self.assertIsNone(smoke.NoRedirect().redirect_request(None, None, 302, "", {}, "https://other.example"))

    def test_url_validation_rejects_credentials_and_nonlocal_http(self):
        for value in ("https://user:test-only-password@example.com", "http://api.example", "https://api.example/path", "https://api.example?token=test"):
            with self.subTest(value=value), self.assertRaises(smoke.SmokeFailure):
                smoke.origin(value, True)
        self.assertEqual(smoke.origin("http://127.0.0.1:8080/", True), "http://127.0.0.1:8080")

    def test_readiness_retries_are_bounded_and_honor_cooldown(self):
        now = [0]
        waits = []
        def sleep(seconds):
            waits.append(seconds)
            now[0] += seconds
        responses = iter([smoke.Response(503, {}, b""), smoke.Response(429, {"retry-after": "5"}, b""), smoke.Response(200, {}, b"Healthy")])
        smoke.wait_ready(lambda _: next(responses), 30, lambda: now[0], sleep)
        self.assertEqual(waits, [3, 5])
        with self.assertRaises(smoke.SmokeFailure):
            smoke.wait_ready(lambda _: smoke.Response(429, {"retry-after": "120"}, b""), 30, lambda: 0, sleep)
        self.assertEqual(waits, [3, 5])

    def test_readiness_does_not_accept_spa_html_or_retry_auth_errors(self):
        for status, body in ((200, b"<html>Healthy</html>"), (401, b""), (302, b"")):
            with self.subTest(status=status), self.assertRaises(smoke.SmokeFailure):
                smoke.wait_ready(lambda _, status=status, body=body: smoke.Response(status, {}, body))

    def test_retry_after_http_date_and_invalid_rate_limit_header(self):
        now = datetime.now(timezone.utc).replace(microsecond=0)
        date = format_datetime(now + timedelta(seconds=20), usegmt=True)
        self.assertEqual(smoke.retry_delay(smoke.Response(429, {"retry-after": date}, b""), now), 20)
        self.assertEqual(smoke.retry_delay(smoke.Response(429, {"retry-after": "invalid"}, b""), now), 60)
        with self.assertRaises(smoke.SmokeFailure):
            smoke.wait_ready(lambda _: smoke.Response(429, {"retry-after": "9" * 400}, b""))


if __name__ == "__main__":
    unittest.main()
