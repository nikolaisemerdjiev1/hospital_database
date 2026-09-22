"""Black-box maintenance checks against a NEW disposable Docker database only."""

import argparse
import json
import subprocess
import time
import uuid
from pathlib import Path

POSTGRES = "postgres:18.4-alpine3.24@sha256:9a8afca54e7861fd90fab5fdf4c42477a6b1cb7d293595148e674e0a3181de15"


def run(args, input_text=None, success=True):
    result = subprocess.run(["docker", *args], input=input_text, text=True, capture_output=True,
                            check=False, timeout=240)
    if success and result.returncode:
        raise RuntimeError("Isolated Docker fixture operation failed (raw diagnostics suppressed)")
    return result


def verify(image):
    suffix = uuid.uuid4().hex[:12]
    network = "hospital-release-" + suffix
    database = network + "-pg"
    label = "hospital.release.fixture=" + suffix
    jobs = []
    created_network = False
    created_database = False
    try:
        run(["network", "create", "--label", label, network])
        created_network = True
        run(["run", "--detach", "--name", database, "--label", label, "--network", network,
             "--network-alias", "release-db", "--tmpfs", "/var/lib/postgresql",
             "--env", "POSTGRES_DB=hospital_coordination", "--env", "POSTGRES_PASSWORD=fixture_only", POSTGRES])
        created_database = True
        for _ in range(30):
            # The entrypoint's temporary initialization server only listens on a socket.
            if run(["exec", database, "pg_isready", "-h", "127.0.0.1", "-U", "postgres"], success=False).returncode == 0:
                break
            time.sleep(1)
        else:
            raise RuntimeError("Fixture PostgreSQL startup timed out")

        def sql(statement, role="postgres", expected=True):
            return run(["exec", "-i", database, "psql", "-X", "-v", "ON_ERROR_STOP=1", "-At",
                        "-U", role, "-d", "hospital_coordination"], statement, expected)

        sql((Path(__file__).parent / "production" / "bootstrap-roles.sql").read_text(encoding="utf-8"))
        sql("ALTER ROLE hospital_runtime PASSWORD 'fixture_only'; ALTER ROLE hospital_maintenance PASSWORD 'fixture_only';")

        def prepare(role, expected, command="--prepare-release", overrides=None, detached=False):
            name = network + "-job-" + str(len(jobs))
            jobs.append(name)
            values = {
                "ConnectionStrings__HospitalMaintenanceDatabase":
                    f"Host=release-db;Database=hospital_coordination;Username={role};Password=fixture_only;Pooling=false;Application Name={name}",
                "Authentication__Auth0__Domain": "auth.example.invalid",
                "Authentication__Auth0__Audience": "https://api.example.invalid",
                "Authentication__Auth0__RoleClaim": "https://fixture.example/role",
                "Frontend__Origin": "https://fixture.example", "ReverseProxy__TrustForwardedHeaders": "true",
                "Release__Revision": "a" * 40, "DemoSeed__AnchorDate": "today",
                "DemoSeed__Subjects__Patient": "fixture|patient", "DemoSeed__Subjects__Doctor": "fixture|doctor",
                "DemoSeed__Subjects__Pharmacist": "fixture|pharmacist", "DemoSeed__Subjects__Administrator": "fixture|admin",
                "Logging__LogLevel__Microsoft.EntityFrameworkCore": "None",
                "Logging__LogLevel__Hospital.Infrastructure.Persistence.Initialization.DemoDataResetter": "None",
                "DemoReset__ExpectedDatabaseName": "hospital_coordination",
                "DemoReset__LockTimeoutSeconds": "30",
            }
            values.update(overrides or {})
            # Container-private tmpfs, not host temporary storage.
            tmpfs = "/tmp:rw,noexec,nosuid,size=64m"  # nosec B108
            args = ["run", "--detach" if detached else "--rm", "--name", name, "--label", label, "--network", network,
                    "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
                    "--cpus", "0.25", "--memory", "512m", "--tmpfs", tmpfs]
            for key, value in values.items():
                args += ["--env", key + "=" + value]
            result = run([*args, image, command], success=False)
            if detached:
                assert result.returncode == 0, "Fixture job failed to start"
                return name
            if (result.returncode == 0) != expected:
                raise RuntimeError("Maintenance command did not return the expected outcome")
            if not expected:
                # Alpine may first emit its native optional Kerberos-library diagnostic.
                message = ("Synthetic demo reset failed; inspect maintenance status before retry."
                           if command == "--reset-demo-data" else
                           "Release database preparation failed. Keep the previous web revision; review before retry.")
                assert result.stderr.strip().endswith(message)
                assert "Microsoft.EntityFrameworkCore" not in result.stdout + result.stderr
                assert "fixture_only" not in result.stdout + result.stderr
            return name

        def eventually(statement):
            for _ in range(100):
                if sql(statement).stdout.strip() == "t":
                    return
                time.sleep(0.2)
            raise RuntimeError("Expected fixture lock state was not observed")

        def block(key):
            run(["exec", "--detach", database, "psql", "-X", "-U", "postgres", "-d", "hospital_coordination",
                 "-c", f"SET application_name='fixture-blocker'; SELECT pg_advisory_lock({key}); SELECT pg_sleep(120)"])
            eventually("SELECT EXISTS (SELECT FROM pg_stat_activity WHERE application_name='fixture-blocker' AND wait_event='PgSleep')")

        def unblock():
            sql("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name='fixture-blocker'")

        def waiting(name):
            # Only UUID-derived container names created above, never external input.
            assert name in jobs
            eventually(f"SELECT EXISTS (SELECT FROM pg_stat_activity WHERE application_name='{name}' AND wait_event='advisory')")  # nosec B608

        def finished(name, expected=True):
            result = run(["wait", name])
            assert (result.stdout.strip() == "0") == expected, "Unexpected fixture execution outcome"

        prepare("hospital_runtime", False)
        assert sql("SELECT count(*) FROM pg_tables WHERE schemaname='public'").stdout.strip() == "0"
        prepare("hospital_maintenance", True)
        assert sql("SELECT count(*) FROM user_profile", "hospital_runtime").stdout.strip() == "51"
        sql("UPDATE appointment SET reason='release-preserve-fixture' WHERE id=(SELECT min(id) FROM appointment)", "hospital_runtime")
        prepare("hospital_maintenance", True)
        assert sql("SELECT count(*) FROM appointment WHERE reason='release-preserve-fixture'", "hospital_runtime").stdout.strip() == "1"
        assert sql("SELECT count(*) FROM user_profile", "hospital_runtime").stdout.strip() == "51"
        assert sql('SELECT * FROM "__EFMigrationsHistory"', "hospital_runtime", False).returncode != 0
        assert sql("DELETE FROM appointment", "hospital_runtime", False).returncode != 0

        history = sql('SELECT * FROM "__EFMigrationsHistory" ORDER BY "MigrationId"').stdout
        grants = sql("SELECT relname,relacl FROM pg_class WHERE relnamespace='public'::regnamespace ORDER BY relname").stdout
        prepare("hospital_runtime", False, "--reset-demo-data")
        prepare("postgres", False, "--reset-demo-data")
        prepare("hospital_maintenance", False, "--reset-demo-data", {"DemoReset__ExpectedDatabaseName": "wrong"})
        prepare("hospital_maintenance", False, "--reset-demo-data", {"DemoSeed__Subjects__Patient": "demo-seed|patient-002"})
        assert sql("SELECT count(*) FROM appointment WHERE reason='release-preserve-fixture'").stdout.strip() == "1"

        # Real release holds the shared session lock while waiting for the seed lock;
        # real reset must wait for it before inspecting history or truncating.
        block(4829531640271135)
        migration_job = prepare("hospital_maintenance", True, detached=True)
        waiting(migration_job)
        reset = prepare("hospital_maintenance", True, "--reset-demo-data", detached=True)
        waiting(reset)
        unblock()
        finished(migration_job)
        finished(reset)

        # Reverse direction: reset holds the shared transaction lock; release refuses.
        # A second reset queues, then both leave one canonical dataset.
        block(4829531640271135)
        reset = prepare("hospital_maintenance", True, "--reset-demo-data", detached=True)
        waiting(reset)
        prepare("hospital_maintenance", False)
        second_reset = prepare("hospital_maintenance", True, "--reset-demo-data", detached=True)
        waiting(second_reset)
        unblock()
        finished(reset)
        finished(second_reset)

        sql("UPDATE appointment SET reason='release-preserve-fixture' WHERE id=(SELECT min(id) FROM appointment)", "hospital_runtime")
        block(7239061401)
        prepare("hospital_maintenance", False, "--reset-demo-data", {"DemoReset__LockTimeoutSeconds": "1"})
        reset = prepare("hospital_maintenance", False, "--reset-demo-data", detached=True)
        waiting(reset)
        sql('INSERT INTO "__EFMigrationsHistory" VALUES (\'99999999999999_Unknown\',\'10.0.0\')', "hospital_maintenance")
        unblock()
        finished(reset, False)
        assert sql("SELECT count(*) FROM appointment WHERE reason='release-preserve-fixture'").stdout.strip() == "1"
        sql('DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId"=\'99999999999999_Unknown\'', "hospital_maintenance")
        prepare("hospital_maintenance", True, "--reset-demo-data")
        assert sql("SELECT count(*) FROM user_profile", "hospital_runtime").stdout.strip() == "51"
        assert sql("SELECT count(*) FROM audit_event WHERE action='DemoDataSeeded'").stdout.strip() == "1"
        assert sql("SELECT count(*) FROM appointment WHERE reason='release-preserve-fixture'").stdout.strip() == "0"
        assert sql('SELECT * FROM "__EFMigrationsHistory" ORDER BY "MigrationId"').stdout == history
        assert sql("SELECT relname,relacl FROM pg_class WHERE relnamespace='public'::regnamespace ORDER BY relname").stdout == grants
        assert sql('SELECT * FROM "__EFMigrationsHistory"', "hospital_runtime", False).returncode != 0
        assert sql("DELETE FROM appointment", "hospital_runtime", False).returncode != 0
        sql("UPDATE appointment SET reason='release-preserve-fixture' WHERE id=(SELECT min(id) FROM appointment)", "hospital_runtime")
        sql("CREATE TABLE unexpected_release_table (id int)", "hospital_maintenance")
        prepare("hospital_maintenance", False)
        assert sql("SELECT count(*) FROM appointment WHERE reason='release-preserve-fixture'", "hospital_runtime").stdout.strip() == "1"
        print("PASS: production maintenance/reset guards, sanitized failures, atomic rollback, shared locks in both directions, concurrent resets, lock timeout, history recheck after lock, canonical data, preserved history/grants and runtime denials.")
    finally:
        for name in jobs + ([database] if created_database else []):
            result = run(["inspect", "--format", "{{json .Config.Labels}}", name], success=False)
            if result.returncode == 0 and json.loads(result.stdout).get("hospital.release.fixture") == suffix:
                run(["rm", "--force", "--volumes", name])
        if created_network:
            labels = json.loads(run(["network", "inspect", "--format", "{{json .Labels}}", network]).stdout)
            if labels.get("hospital.release.fixture") != suffix:
                raise RuntimeError("Fixture network ownership mismatch")
            run(["network", "rm", network])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True, help="Already built local image; never pulls the hospital image")
    arguments = parser.parse_args()
    run(["image", "inspect", arguments.image])
    verify(arguments.image)
