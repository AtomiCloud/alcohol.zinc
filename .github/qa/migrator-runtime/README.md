# Published migrator CI acceptance

This is the real migration-only test runner for the PM-approved disposable CI evidence model. Local source preparation is complete; neither architecture has been executed by this preparation. Publication, CI dispatch, Docker/QEMU setup and billed runner use still need explicit user approval. It does not run an API, rebuild images or use product/provider credentials.

## Workflow interface

Use Python 3.10+ on a fresh GitHub-hosted Ubuntu22.04 AMD64 runner. The workflow owns checkout, pinned ARM64 QEMU setup in the ARM cell only, anonymous image prefetch, a 20-minute job limit, outer command timeout, final matching-label cleanup and sanitized artifact upload. Do not copy publishing steps or inject repository/environment/provider secrets. A failed anonymous pull is a concrete blocker, not permission to log in.

```
python3 .github/qa/migrator-runtime/run.py \
  --platform amd64|arm64 \
  --image ghcr.io/atomicloud/alcohol.zinc/migrate@sha256:EXACT_MEMBER \
  --expected-config sha256:EXACT_CONFIG \
  --postgres-image docker.io/library/postgres@sha256:6f7593c161689b6b02c78668b314af7da8811b592c5519108c57a49d0b6ad788 \
  --output NEW_SANITIZED_EVIDENCE_DIRECTORY
```

Required environment: `MIGRATOR_QA_RUN_ID=<github.run_id>-<github.run_attempt>-<platform>`. Every created network/container has label `lazytax.migrator-qa.run=$MIGRATOR_QA_RUN_ID`; names are unique to that cell. The output directory must not exist. It contains only result/identity metadata and synthetic schema/data snapshots. Private configuration, independent random synthetic admin/migrator passwords and empty Docker-client configuration live in a separate private temporary directory, never in evidence. No raw logs, inspect Env, connection strings or credential files are exported. Passwords are not arguments; SQL inputs and rendered settings stay private.

| Cell                | Published member                                                 | Expected config ID                                               |
| ------------------- | ---------------------------------------------------------------- | ---------------------------------------------------------------- |
| AMD64 native        | a1d8e5bbacae725261708464e4403537b63e1ee401daa6f1c0a03c27ee1ae4d7 | b536bc937d29cf22c21b92f0261141af5a8530a165dcb380182540e24a233011 |
| ARM64 QEMU-emulated | ec075373bed91b5ac88e7ea524ae605e42590a5f99356518037f98006f9c48a1 | 19b1ba31f6e07cf508d2638e8eb4b75faf6d2cf56807f60602e77ab0e4e7d0b3 |

Source b2e55c82392e88aff3cf99221c1ad52e9ac9de12; reviewed migration index47fe54ffe569341c7ce055a85f8af51fefa53ae3ba7c13a0769c50d17f30d58a. Runtime checks local member RepoDigest, exact config ID, Linux/architecture, packaged dotnet user, /app cwd and ./efbundle Cmd. The reviewed parent index is a provenance pin, not an index pull or a new registry attestation. Explicit `./efbundle TARGET` retains packaged user/cwd; no entrypoint/user override. PG is the same native AMD6417.12 member in both jobs, config140936e8665d5274aa58df31b722693b4b451c541bb0f1a263a345d424bfacb4; actual server major must be17. Official PG repository aliases are normalized for Docker RepoDigests only.

## Executed sequence after approval

Create one labelled internal Docker network, no published ports; start only the disposable PG17 and exact candidate containers on it. PG data is disposable tmpfs. Candidate files are complete synthetic JSON-as-YAML settings bound read-only over both config paths. No host credentials or engine socket are mounted. Disabled providers point to loopback port9, DB to the private PG alias. This is an internal-network/synthetic-fixture claim, not globally offline CI: setup and anonymous image retrieval use network.

1. Run published bundle to predecessor20261113065122. Insert unchanged reviewed r2 legacy seed:4 users,3 opposing/legacy requests,2 friendships,3 read/unread/legacy-JSON inbox rows. Require the34-table inventory,15 exact-source EF history entries and no unexpected application rows.
2. Clone that predecessor to an oracle DB. Apply independently authored expected-up.sql there, not EF-generated runtime output. It permits only the declared column/default/index/check/FK changes, pending/unresolved legacy defaults, null legacy linkage and one target/history version8.0.2 entry.
3. Run target20261115042211 in primary; compare exact full schema dump, all public table rows/history and every public sequence's last_value/is_called against the oracle. PostgreSQL JSON strings retain decimal precision; only pg_dump's random restrict tokens are normalized. Run unchanged DBv2 SQL controls in a rolled-back transaction and verify the full state is unchanged. These cover generated UUID/defaults, duplicate pending rejection, opposing pending, repeated same-pair same-terminal-status occurrences, check failures, FK rejection/SET NULL and full lookup index.
4. Repeat target: unchanged. Request predecessor: nonzero with exact retained-outcome refusal message, unchanged. Re-run forward target: unchanged.
5. Independently create/seed recovery at predecessor; require full equality to primary's predecessor. Add only the named lookup-index collision on the existing UserId column. Target must fail nonzero with42P07 and the exact index name; full state must equal the post-obstacle snapshot. Remove only that obstacle and prove full equality to the pre-obstacle snapshot. Run the same target/member forward, then repeat; require oracle equality both times. Repeat DBv2 controls and full-state comparison.

`result.json` records original client/container exits separately, fixed output classifications, phases, timings, assertion outcomes, input hashes and9 bundle-call cells. A call's PASS means its exit contract passed; the whole platform passes only after all full-state assertions and owned cleanup succeed. Any unexpected failure stops the sequence, leaves remaining cells NOT_RUN and attempts a sanitized fresh failure snapshot only after candidate exit/removal is confirmed. Snapshot failure is unavailable evidence, never equality.

## Timeouts, cleanup and known limits

Work budget780s (13min), cleanup deadline890s from harness start, command cap30s and bundle attach cap150s, each clipped to remaining budget. This fits Backend A's outer `timeout --signal=TERM --kill-after=20s 15m` and20-minute job without the former source-orb2400s/API watchdog machinery. Nine worst-case150s calls do not fit; exhaustion is INCOMPLETE, not an extended job or a pass. Cleanup failures do not overwrite original failure metadata. The harness attempts only labelled owned removals and its own network; no prune, shared-image deletion or retained-fixture cleanup.

Workflow `always()` cleanup removes only matching-label containers with volumes, then matching networks. Upload only `--output`, not private temp directories or arbitrary logs. Cancellation may skip evidence upload/cleanup, and runner disposal is the accepted final fallback; a missing report, RUNNING cell, timeout or cancellation is incomplete. No host/kernel-death guarantee is claimed. Runtime Docker mount/EF configuration, exact schema-oracle formatting and ARM/QEMU execution are unverified until the approved first CI run; failures must be returned as concrete evidence, not repaired with unreviewed local overrides.

## Preparation checks

Only syntax/JSON/source-pin comparisons and pure helper tests are authorized locally:

```
python3 -B -m unittest discover -s .github/qa/migrator-runtime -p test_pure.py -v
```

These tests exercise wrong image/config/platform/user/cwd/command, private-network argv, non-count schema/history/high-precision row/sequence changes, missing tables/unexpected rows/history, dump-token normalization and synthetic config. They never instantiate Harness or call Docker/SQL/entropy/runtime paths. Existing unit/integration acceptance is separate; this harness is not product re-verification. `pls lint` is unavailable in the preparation orb and must be run in the repository's Nix environment; no green lint claim.
