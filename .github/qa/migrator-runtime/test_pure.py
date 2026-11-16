"""Side-effect-free contract tests; never instantiate/run the Docker harness."""

import copy
import json
from pathlib import Path
import unittest

import run as runner


class Contracts(unittest.TestCase):
    def test_exact_image_arguments(self):
        for arch, member, config in [
            ("amd64", "a1d8e5bbacae725261708464e4403537b63e1ee401daa6f1c0a03c27ee1ae4d7", "b536bc937d29cf22c21b92f0261141af5a8530a165dcb380182540e24a233011"),
            ("arm64", "ec075373bed91b5ac88e7ea524ae605e42590a5f99356518037f98006f9c48a1", "19b1ba31f6e07cf508d2638e8eb4b75faf6d2cf56807f60602e77ab0e4e7d0b3"),
        ]:
            image = f"ghcr.io/atomicloud/alcohol.zinc/migrate@sha256:{member}"
            runner.validate_inputs(arch, image, "sha256:" + config, runner.POSTGRES_IMAGE)
            for bad in [image.replace("migrate@", "api@"), image.replace(member, "0" * 64), image.split("@")[0] + ":latest"]:
                with self.subTest(arch=arch, bad=bad), self.assertRaises(runner.Failure):
                    runner.validate_inputs(arch, bad, "sha256:" + config, runner.POSTGRES_IMAGE)
            with self.assertRaises(runner.Failure):
                runner.validate_inputs(arch, image, "sha256:" + "0" * 64, runner.POSTGRES_IMAGE)
            with self.assertRaises(runner.Failure):
                runner.validate_inputs(arch, image, "sha256:" + config, "postgres:17")

    def test_actual_image_identity_not_just_cli_pin(self):
        member, config = runner.PINS["arm64"]
        image = f"{runner.REPOSITORY}@sha256:{member}"
        info = {"Id": "sha256:" + config, "Os": "linux", "Architecture": "arm64", "RepoDigests": [image],
                "Config": {"User": "dotnet", "WorkingDir": "/app", "Entrypoint": None, "Cmd": ["./efbundle"]}}
        runner.verify_image(info, "arm64", image, "sha256:" + config)
        for field, value in [("Id", "sha256:" + "f" * 64), ("Architecture", "amd64"), ("RepoDigests", [])]:
            wrong = copy.deepcopy(info)
            wrong[field] = value
            with self.subTest(field=field), self.assertRaises(runner.Failure):
                runner.verify_image(wrong, "arm64", image, "sha256:" + config)
        for field, value in [("User", "root"), ("WorkingDir", "/"), ("Entrypoint", ["sh"]), ("Cmd", ["other"] )]:
            wrong = copy.deepcopy(info)
            wrong["Config"][field] = value
            with self.subTest(field=field), self.assertRaises(runner.Failure):
                runner.verify_image(wrong, "arm64", image, "sha256:" + config)

    def test_command_preserves_packaged_entry_and_private_network(self):
        args = runner.migration_args("arm64", "pinned-image", "owned", "internal", "lazytax.migrator-qa.run=42-1-arm64",
                                     Path("/private/settings.yaml"), runner.TARGET)
        self.assertEqual(args[-3:], ["pinned-image", "./efbundle", "20261115042211_RetainFriendRequestOccurrences"])
        self.assertEqual(args[args.index("--platform") + 1], "linux/arm64")
        self.assertEqual(args[args.index("--network") + 1], "internal")
        self.assertIn("--pull=never", args)
        self.assertEqual(sum("readonly" in value for value in args), 2)
        for forbidden in ("--user", "--entrypoint", "--publish", "--privileged", "--connection"):
            self.assertNotIn(forbidden, args)
        with self.assertRaises(runner.Failure):
            runner.migration_args("amd64", "image", "n", "net", "l", Path("/p"), "0")

    def test_full_snapshot_comparison_catches_noncount_deltas(self):
        before = {"tables": {"HabitChecks": '[{"amount":1.00000000000000001}]',
                             "__EFMigrationsHistory": '[{"MigrationId":"first","ProductVersion":"8.0.2"}]'},
                  "sequences": {"s": '{"last_value":17,"is_called":false}'},
                  "schema": "CREATE INDEX lookup ON inbox (request);"}
        runner.assert_equal(before, copy.deepcopy(before))
        for section, key, changed in [
            ("tables", "HabitChecks", '[{"amount":1.00000000000000002}]'),
            ("tables", "__EFMigrationsHistory", '[{"MigrationId":"wrong","ProductVersion":"8.0.2"}]'),
            ("sequences", "s", '{"last_value":17,"is_called":true}'),
        ]:
            after = copy.deepcopy(before)
            after[section][key] = changed
            with self.subTest(section=section), self.assertRaises(runner.Failure):
                runner.assert_equal(before, after)
        after = copy.deepcopy(before)
        after["schema"] += " WHERE request IS NOT NULL;"
        with self.assertRaises(runner.Failure):
            runner.assert_equal(before, after)

    def test_predecessor_rejects_missing_tables_extra_rows_and_history(self):
        history = [{"MigrationId": "predecessor", "ProductVersion": "8.0.2"}]
        tables = {"Users": "[1,2,3,4]", "FriendRequests": "[1,2,3]", "Friendships": "[1,2]",
                  "InboxNotifications": "[1,2,3]", "CheckEvents": "[]", "__EFMigrationsHistory": json.dumps(history)}
        runner.assert_predecessor({"tables": tables}, list(tables), history)
        for name, rows in [("CheckEvents", "[1]"), ("Users", "[1,2,3]"), ("__EFMigrationsHistory", "[]")]:
            wrong = dict(tables, **{name: rows})
            with self.subTest(name=name), self.assertRaises(runner.Failure):
                runner.assert_predecessor({"tables": wrong}, list(tables), history)
        with self.assertRaises(runner.Failure):
            runner.assert_predecessor({"tables": tables}, list(tables) + ["Habits"], history)

    def test_only_pg_dump_random_token_is_removed(self):
        a = "header\n\\restrict AbC123\nCREATE TABLE t (x int);\n\\unrestrict AbC123\n"
        b = a.replace("AbC123", "different456")
        self.assertEqual(runner.schema_text(a), runner.schema_text(b))
        self.assertNotEqual(runner.schema_text(a), runner.schema_text(b.replace("x int", "x bigint")))
        self.assertIn("restrict", runner.schema_text("-- restrict metadata must remain\n"))

    def test_cleanup_scope_and_official_pg_alias(self):
        self.assertEqual(runner.validate_run_id("123-2-arm64"), "123-2-arm64")
        for bad in ["", "*", "../host", "123-2-arm64 extra", "123-2-s390x"]:
            with self.subTest(bad=bad), self.assertRaises(runner.Failure):
                runner.validate_run_id(bad)
        self.assertEqual(runner.canonical_ref(runner.POSTGRES_IMAGE),
                         runner.POSTGRES_IMAGE.removeprefix("docker.io/library/"))

    def test_synthetic_settings_have_only_fixture_destinations(self):
        config = json.loads((runner.ROOT / "settings.synthetic.json").read_text())
        self.assertEqual(config["App"]["Mode"], "Migration")
        self.assertFalse(config["App"]["GenerateConfig"])
        self.assertTrue(config["Auth"]["Enabled"])
        self.assertFalse(config["BlockStorage"]["MAIN"]["EnsureBucketCreation"])
        self.assertFalse(config["Database"]["MAIN"]["AutoMigrate"])
        self.assertEqual(config["Database"]["MAIN"]["Host"], "pg")
        for provider in config["HttpClient"].values():
            self.assertEqual(provider["BaseAddress"], "http://127.0.0.1:9")
        for exporter in ("Logs", "Trace", "Metrics"):
            self.assertFalse(config[exporter]["Exporter"]["Otlp"]["Enabled"])
            self.assertFalse(config[exporter]["Exporter"]["Console"]["Enabled"])


if __name__ == "__main__":
    unittest.main()
