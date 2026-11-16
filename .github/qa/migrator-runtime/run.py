"""Exact published migrator acceptance on an explicitly approved disposable CI runner.

Import is side-effect free. Only main() dispatches Docker. No API or image builds.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parent
PREDECESSOR = "20261113065122_HabitSortOrder"
TARGET = "20261115042211_RetainFriendRequestOccurrences"
SOURCE = "b2e55c82392e88aff3cf99221c1ad52e9ac9de12"
INDEX = "sha256:47fe54ffe569341c7ce055a85f8af51fefa53ae3ba7c13a0769c50d17f30d58a"
PINS = {
    "amd64": (
        "a1d8e5bbacae725261708464e4403537b63e1ee401daa6f1c0a03c27ee1ae4d7",
        "b536bc937d29cf22c21b92f0261141af5a8530a165dcb380182540e24a233011",
    ),
    "arm64": (
        "ec075373bed91b5ac88e7ea524ae605e42590a5f99356518037f98006f9c48a1",
        "19b1ba31f6e07cf508d2638e8eb4b75faf6d2cf56807f60602e77ab0e4e7d0b3",
    ),
}
REPOSITORY = "ghcr.io/atomicloud/alcohol.zinc/migrate"
POSTGRES_IMAGE = "docker.io/library/postgres@sha256:6f7593c161689b6b02c78668b314af7da8811b592c5519108c57a49d0b6ad788"
POSTGRES_CONFIG = "sha256:140936e8665d5274aa58df31b722693b4b451c541bb0f1a263a345d424bfacb4"
LABEL_KEY = "lazytax.migrator-qa.run"
DOWN_MESSAGE = b"Friend request outcomes must be retained. Recover forward; do not downgrade this migration."


class Failure(Exception):
    """Only fixed nonsecret reason codes are exported."""


def require(condition, reason):
    if not condition:
        raise Failure(reason)


def validate_inputs(platform, image, expected_config, postgres_image):
    member, config = PINS[platform]
    require(image == f"{REPOSITORY}@sha256:{member}", "CANDIDATE_MEMBER")
    require(expected_config == f"sha256:{config}", "CANDIDATE_CONFIG_PIN")
    require(postgres_image == POSTGRES_IMAGE, "POSTGRES_IMMUTABLE_REFERENCE")


def validate_run_id(value):
    require(bool(re.fullmatch(r"[0-9]+-[0-9]+-(?:amd64|arm64)", value)), "RUN_ID_ENV")
    return value


def canonical_ref(value):
    return value.removeprefix("docker.io/").removeprefix("library/")


def verify_image(info, platform, image, expected_config):
    require(info["Id"] == expected_config, "IMAGE_CONFIG_ID")
    require(info["Os"] == "linux" and info["Architecture"] == platform, "IMAGE_PLATFORM")
    require(image in info.get("RepoDigests", []), "IMAGE_MEMBER_DIGEST")
    config = info["Config"]
    require(config["User"] == "dotnet" and config["WorkingDir"] == "/app", "IMAGE_USER_CWD")
    require(not config["Entrypoint"] and config["Cmd"] == ["./efbundle"], "IMAGE_COMMAND")
    # Never export Config.Env. The mounted complete config has no inherited provider settings.


def schema_text(value):
    # PG17 maintenance releases randomize this pg_dump safety token, not schema content.
    return "\n".join(line for line in value.splitlines()
                     if not re.fullmatch(r"\\(?:un)?restrict [A-Za-z0-9]+", line))


def assert_equal(before, after):
    require(before == after, "FULL_SNAPSHOT_DIFFERENCE")


def assert_predecessor(snapshot, expected_tables, expected_history):
    require(set(snapshot["tables"]) == set(expected_tables), "TABLE_INVENTORY")
    history = json.loads(snapshot["tables"]["__EFMigrationsHistory"])
    require(history == expected_history, "PREDECESSOR_HISTORY")
    counts = {"Users": 4, "FriendRequests": 3, "Friendships": 2, "InboxNotifications": 3}
    for name, rows in snapshot["tables"].items():
        if name != "__EFMigrationsHistory":
            require(len(json.loads(rows)) == counts.get(name, 0), "SEED_OR_UNEXPECTED_ROWS")


def migration_args(platform, image, name, network, label, settings, target):
    require(target in (PREDECESSOR, TARGET), "TARGET")
    return ["create", "--pull=never", "--platform", f"linux/{platform}",
            "--name", name, "--label", label, "--network", network, "--restart=no",
            "--log-driver=none", "--cap-drop=ALL", "--security-opt=no-new-privileges",
            "--env", "LANDSCAPE=qa-ci", "--mount",
            f"type=bind,src={settings},dst=/app/Config/settings.yaml,readonly",
            "--mount", f"type=bind,src={settings},dst=/app/Config/settings.qa-ci.yaml,readonly",
            image, "./efbundle", target]


class Harness:
    def __init__(self, args):
        self.args = args
        self.out = args.output.resolve()
        self.phase = "setup"
        self.database = None
        self.containers = []
        self.network_created = False
        self.pg_ready = False
        self.quiescent = True
        self.report = {"status": "INCOMPLETE", "source": SOURCE, "index": INDEX,
                       "image": args.image, "expectedConfig": args.expected_config,
                       "postgresImage": args.postgres_image, "platform": args.platform,
                       "execution": "native" if args.platform == "amd64" else "QEMU-emulated",
                       "events": [], "assertions": [], "cleanup": [],
                       "cells": {name: "NOT_RUN" for name in (
                           "primary-predecessor", "primary-target", "primary-repeat",
                           "primary-refused-down", "primary-forward-noop", "recovery-predecessor",
                           "recovery-failed-up", "recovery-forward", "recovery-repeat")}}

    def write(self, name, value):
        (self.out / name).write_text(json.dumps(value, indent=2, sort_keys=True) + "\n")

    def event(self, **values):
        self.report["events"].append(dict(phase=self.phase, **values))
        self.write("result.json", self.report)

    def docker(self, argv, data=None, timeout=30, checked=True, cleanup=False):
        # No host provider/proxy/Atomi environment enters any container. Docker output is private
        # in memory; only fixed classifications, exits and counts reach uploadable evidence.
        deadline = self.cleanup_deadline if cleanup or self.phase == "cleanup" else self.work_deadline
        remaining = deadline - time.monotonic()
        require(remaining > 0, "HARNESS_BUDGET_TIMEOUT")
        try:
            result = subprocess.run(["docker", "--config", str(self.private / "docker-client"), *argv], input=data, stdout=subprocess.PIPE,
                                    stderr=subprocess.PIPE, timeout=min(timeout, remaining), check=False)
        except subprocess.TimeoutExpired:
            self.event(command=argv[0], clientExit=None, timedOut=True)
            raise Failure("DOCKER_COMMAND_TIMEOUT") from None
        self.event(command=argv[0], clientExit=result.returncode, timedOut=False,
                   stdoutBytes=len(result.stdout), stderrBytes=len(result.stderr))
        require(not checked or result.returncode == 0, "DOCKER_COMMAND_FAILED")
        return result

    def sql(self, database, sql):
        return self.docker(["exec", "-i", self.pg, "psql", "-X", "-qAt", "-v", "ON_ERROR_STOP=1",
                            "-U", "qa_admin", "-d", database], sql.encode()).stdout.decode().strip()

    def check(self, label, function, *args):
        try:
            function(*args)
        except Exception:
            self.report["assertions"].append({"phase": self.phase, "label": label, "passed": False})
            raise
        else:
            self.report["assertions"].append({"phase": self.phase, "label": label, "passed": True})
        finally:
            self.write("result.json", self.report)

    def snapshot(self, database, label):
        require(self.quiescent, "SNAPSHOT_CANDIDATE_NOT_QUIESCENT")
        relations = json.loads(self.sql(database, """
SELECT coalesce(jsonb_agg(jsonb_build_object('name',c.relname,'kind',c.relkind)
 ORDER BY c.relname),'[]') FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
 WHERE n.nspname='public';"""))
        tables, sequences = {}, {}
        for row in relations:
            name = '"' + row["name"].replace('"', '""') + '"'
            if row["kind"] in ("r", "p"):
                # Keep PostgreSQL canonical JSON text: Python floats must not round away a delta.
                tables[row["name"]] = self.sql(database, f"SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text),'[]') FROM {name} t;")
            elif row["kind"] == "S":
                sequences[row["name"]] = self.sql(database, f"SELECT jsonb_build_object('last_value',last_value,'is_called',is_called) FROM {name};")
            else:
                require(row["kind"] in ("i", "I"), "UNEXPECTED_RELATION")
        dump = self.docker(["exec", self.pg, "pg_dump", "--schema-only", "-U", "qa_admin", "-d", database]).stdout.decode()
        snapshot = {"tables": tables, "sequences": sequences, "schema": schema_text(dump)}
        self.write(f"{label}.snapshot.json", snapshot)
        return snapshot

    def owned_remove(self, name):
        info = self.docker(["inspect", "--format", '{{json .Config.Labels}}', name], checked=False, cleanup=True)
        if info.returncode:
            return False
        require(json.loads(info.stdout).get(LABEL_KEY) == self.run_id, "CLEANUP_OWNERSHIP")
        self.docker(["rm", "--force", "--volumes", name], cleanup=True)
        self.containers.remove(name)
        return True

    def bundle(self, database, target, phase, expected="success"):
        self.phase, self.database = phase, database
        self.report["cells"][phase] = "RUNNING"
        settings = json.loads((ROOT / "settings.synthetic.json").read_text())
        settings["Database"]["MAIN"].update(Host="pg", Database=database, User="qa103_migrator", Password=self.password)
        config = self.private / f"{phase}.yaml"
        config.write_text(json.dumps(settings))  # JSON is a YAML subset accepted by AddYamlFile.
        config.chmod(0o444)
        name = f"{self.run_id}-{phase}"
        self.containers.append(name)
        start = time.monotonic()
        try:
            self.docker(migration_args(self.args.platform, self.args.image, name, self.network,
                                       self.label, config, target))
            self.quiescent = False
            result = self.docker(["start", "--attach", name], timeout=150, checked=False)
            state = json.loads(self.docker(["inspect", "--format", '{{json .State}}', name]).stdout)
            require(not state["Running"] and state["Status"] == "exited", "CANDIDATE_NOT_EXITED")
            self.quiescent = True
            raw = result.stdout + result.stderr
            classification = "SUCCESS" if state["ExitCode"] == 0 else "UNEXPECTED_NONZERO"
            if state["ExitCode"] != 0 and b"42P07" in raw and b"IX_InboxNotifications_FriendRequestId" in raw:
                classification = "EXPECTED_INDEX_COLLISION"
            if state["ExitCode"] != 0 and DOWN_MESSAGE in raw:
                classification = "EXPECTED_REFUSED_DOWN"
            self.event(candidateExit=state["ExitCode"], classification=classification,
                       elapsedSeconds=round(time.monotonic() - start, 3), container=name)
            wanted = {"success": "SUCCESS", "collision": "EXPECTED_INDEX_COLLISION", "down": "EXPECTED_REFUSED_DOWN"}[expected]
            require(result.returncode == state["ExitCode"] and classification == wanted, "BUNDLE_OUTCOME")
            self.report["cells"][phase] = "PASS"
        finally:
            # CLI timeout does not mean container exit. Explicit owned removal is attempted before
            # any failure snapshot; hosted-runner disposal is the final, separately accepted fallback.
            original = sys.exc_info()[1]
            if original is not None:
                reason = str(original) if isinstance(original, Failure) else "HARNESS_UNEXPECTED_ERROR"
                self.report.setdefault("firstFailure", {"phase": self.phase, "reason": reason})
                self.report["cells"][phase] = "INCOMPLETE" if reason.endswith("TIMEOUT") else "FAIL"
            try:
                if not self.quiescent:
                    self.docker(["stop", "--time", "5", name], timeout=15, checked=False, cleanup=True)
                    state = json.loads(self.docker(["inspect", "--format", '{{json .State}}', name], cleanup=True).stdout)
                    self.quiescent = not state["Running"] and state["Status"] == "exited"
                    self.event(candidateExit=state["ExitCode"] if self.quiescent else None,
                               postFailureStop=True, exitConfirmed=self.quiescent)
                removed = self.owned_remove(name)
                self.quiescent = self.quiescent or removed
            except Exception:
                self.report["cleanup"].append({"container": name, "removed": False})
                if original is None:
                    raise Failure("CANDIDATE_CLEANUP_FAILED") from None

    def execute(self):
        a = self.args
        info = json.loads(self.docker(["image", "inspect", a.image]).stdout)[0]
        verify_image(info, a.platform, a.image, a.expected_config)
        self.write("image-identity.json", {k: info[k] for k in ("Id", "Os", "Architecture", "RepoDigests", "RootFS")})
        pg_info = json.loads(self.docker(["image", "inspect", a.postgres_image]).stdout)[0]
        require(pg_info["Os"] == "linux" and pg_info["Architecture"] == "amd64" and
                pg_info["Id"] == POSTGRES_CONFIG and
                canonical_ref(a.postgres_image) in
                [canonical_ref(ref) for ref in pg_info.get("RepoDigests", [])], "POSTGRES_IMAGE_IDENTITY")
        self.write("postgres-identity.json", {k: pg_info[k] for k in ("Id", "Os", "Architecture", "RepoDigests")})
        self.network_created = True
        self.docker(["network", "create", "--internal", "--label", self.label, self.network])
        password_file = self.private / "pg-password"
        password_file.write_text(secrets.token_hex(32))
        password_file.chmod(0o444)
        self.containers.append(self.pg)
        self.docker(["run", "--detach", "--pull=never", "--platform=linux/amd64", "--name", self.pg,
                     "--label", self.label, "--network", self.network, "--network-alias", "pg",
                     "--restart=no", "--log-driver=none", "--tmpfs", "/var/lib/postgresql/data:rw,size=536870912",
                     "--env", "POSTGRES_USER=qa_admin", "--env", "POSTGRES_PASSWORD_FILE=/run/qa/password",
                     "--env", "POSTGRES_INITDB_ARGS=--auth-host=scram-sha-256",
                     "--mount", f"type=bind,src={password_file},dst=/run/qa/password,readonly",
                     a.postgres_image, "postgres", "-c", "log_statement=none", "-c", "log_min_error_statement=panic"])
        ready = False
        for _ in range(45):
            if self.docker(["exec", self.pg, "pg_isready", "-h", "127.0.0.1", "-U", "qa_admin", "-d", "postgres"], checked=False).returncode == 0:
                ready = True
                break
            time.sleep(1)
        require(ready, "POSTGRES_STARTUP")
        self.pg_ready = True
        require(170000 <= int(self.sql("postgres", "SHOW server_version_num")) < 180000, "POSTGRES_NOT_17")
        self.sql("postgres", f"CREATE ROLE qa103_migrator LOGIN PASSWORD '{self.password}';")
        tables = json.loads((ROOT / "expectations.json").read_text())
        seed = (ROOT / "seed.sql").read_text()
        controls = (ROOT / "migration-controls.sql").read_text()
        primary = f"qa103_{a.platform}_primary"
        recovery = f"qa103_{a.platform}_recovery"
        oracle = f"qa103_{a.platform}_oracle"
        self.sql("postgres", f'CREATE DATABASE "{primary}" OWNER qa103_migrator TEMPLATE template0;')
        self.bundle(primary, PREDECESSOR, "primary-predecessor")
        self.sql(primary, seed)
        before = self.snapshot(primary, "primary-predecessor-seeded")
        self.check("exact predecessor tables/history/seed counts", assert_predecessor, before, tables["tables"], tables["history"])
        # A source-independent, reviewed SQL oracle on an identical clone permits ONLY the
        # declared schema/row/history delta. No live migration output supplies expected values.
        self.sql("postgres", f'CREATE DATABASE "{oracle}" OWNER qa103_migrator TEMPLATE "{primary}";')
        self.sql(oracle, (ROOT / "expected-up.sql").read_text())
        expected = self.snapshot(oracle, "expected-target")
        self.bundle(primary, TARGET, "primary-target")
        migrated = self.snapshot(primary, "primary-target")
        self.check("full Up schema/rows/history/sequences", assert_equal, expected, migrated)
        self.sql(primary, controls)
        self.check("DBv2 controls roll back completely", assert_equal, migrated, self.snapshot(primary, "primary-controls"))
        for target, phase, outcome in [(TARGET, "primary-repeat", "success"),
                                        (PREDECESSOR, "primary-refused-down", "down"),
                                        (TARGET, "primary-forward-noop", "success")]:
            self.bundle(primary, target, phase, outcome)
            self.check("unchanged full state", assert_equal, migrated, self.snapshot(primary, phase))
        self.sql("postgres", f'CREATE DATABASE "{recovery}" OWNER qa103_migrator TEMPLATE template0;')
        self.bundle(recovery, PREDECESSOR, "recovery-predecessor")
        self.sql(recovery, seed)
        base = self.snapshot(recovery, "recovery-seeded")
        self.check("independent recovery predecessor", assert_equal, before, base)
        obstacle = 'CREATE INDEX "IX_InboxNotifications_FriendRequestId" ON "InboxNotifications" ("UserId");'
        self.sql(recovery, obstacle)
        obstructed = self.snapshot(recovery, "recovery-obstacle")
        self.bundle(recovery, TARGET, "recovery-failed-up", "collision")
        self.check("failed Up rolls back full state", assert_equal, obstructed, self.snapshot(recovery, "recovery-failed-up"))
        self.sql(recovery, 'DROP INDEX "IX_InboxNotifications_FriendRequestId";')
        self.check("only owned obstacle removed", assert_equal, base, self.snapshot(recovery, "recovery-obstacle-removed"))
        for phase in ("recovery-forward", "recovery-repeat"):
            self.bundle(recovery, TARGET, phase)
            actual = self.snapshot(recovery, phase)
            self.check("recovered exact target", assert_equal, expected, actual)
        self.sql(recovery, controls)
        self.check("recovery DBv2 controls rollback", assert_equal, expected, self.snapshot(recovery, "recovery-controls"))

    def run(self):
        self.run_id = validate_run_id(os.environ.get("MIGRATOR_QA_RUN_ID", ""))
        require(self.run_id.endswith("-" + self.args.platform), "RUN_ID_PLATFORM")
        self.out.mkdir(parents=True, exist_ok=False)
        self.network = "qa-migrate-" + self.run_id + "-net"
        self.pg = "qa-migrate-" + self.run_id + "-pg"
        self.label = LABEL_KEY + "=" + self.run_id
        self.report["runId"] = self.run_id
        self.password = secrets.token_hex(32)
        with tempfile.TemporaryDirectory(prefix=self.run_id + "-") as private:
            self.private = Path(private)
            require(self.out not in self.private.parents, "PRIVATE_OUTPUT_OVERLAP")
            (self.private / "docker-client").mkdir()
            self.report["inputSha256"] = {
                name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()
                for name in ("run.py", "seed.sql", "expected-up.sql", "migration-controls.sql",
                             "expectations.json", "settings.synthetic.json")}
            start = time.monotonic()
            self.work_deadline = start + 780
            self.cleanup_deadline = start + 890
            self.report["workBudgetSeconds"] = 780
            self.report["cleanupDeadlineSeconds"] = 890
            try:
                self.execute()
                self.report["status"] = "PASS"
            except Exception as error:
                reason = str(error) if isinstance(error, Failure) else "HARNESS_UNEXPECTED_ERROR"
                self.report["status"] = "INCOMPLETE" if reason.endswith("TIMEOUT") else "FAIL"
                self.report.setdefault("firstFailure", {"phase": self.phase, "reason": reason})
                if self.pg_ready and self.database and self.quiescent:
                    try:
                        self.snapshot(self.database, "failure")
                        self.report["failureSnapshot"] = "CAPTURED"
                    except Exception:
                        self.report["failureSnapshot"] = "UNAVAILABLE_CAPTURE_FAILED"
                else:
                    self.report["failureSnapshot"] = "UNAVAILABLE_NOT_READY_OR_UNCONFIRMED_EXIT"
            finally:
                self.phase = "cleanup"
                for name in list(reversed(self.containers)):
                    try:
                        removed = self.owned_remove(name)
                        self.report["cleanup"].append({"container": name, "removed": removed})
                    except Exception:
                        self.report["cleanup"].append({"container": name, "removed": False})
                if self.network_created:
                    try:
                        labels = json.loads(self.docker(["network", "inspect", "--format", '{{json .Labels}}', self.network]).stdout)
                        require(labels.get(LABEL_KEY) == self.run_id, "NETWORK_OWNERSHIP")
                        self.docker(["network", "rm", self.network])
                        self.report["cleanup"].append({"network": self.network, "removed": True})
                    except Exception:
                        self.report["cleanup"].append({"network": self.network, "removed": False})
                if any(not item["removed"] for item in self.report["cleanup"]):
                    if self.report["status"] == "PASS":
                        self.report["status"] = "INCOMPLETE"
                    self.report["cleanupFailed"] = True
                self.write("result.json", self.report)
        print(json.dumps({"status": self.report["status"], "platform": self.args.platform,
                          "assertions": len(self.report["assertions"])}))
        return 0 if self.report["status"] == "PASS" else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--platform", choices=tuple(PINS), required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--expected-config", required=True)
    parser.add_argument("--postgres-image", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        validate_inputs(args.platform, args.image, args.expected_config, args.postgres_image)
    except Failure as error:
        parser.error(str(error))
    return Harness(args).run()


if __name__ == "__main__":
    raise SystemExit(main())
