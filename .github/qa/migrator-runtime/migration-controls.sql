\set ON_ERROR_STOP on
-- Run only on newly migrated seeded primary/recovery, NEVER the empty smokeDB.
-- All control mutations roll back; compare full snapshots independently afterward.
BEGIN;
DO $qa$
DECLARE r uuid; first_terminal uuid; second_terminal uuid;
        terminal_ids uuid[] := ARRAY[]::uuid[];
        outcome integer; bad record; violated text;
BEGIN
 IF current_database() NOT IN ('qa103_amd64_primary','qa103_amd64_recovery',
                              'qa103_arm64_primary','qa103_arm64_recovery') THEN
   RAISE EXCEPTION 'wrong migration fixture';
 END IF;
 IF (SELECT count(*) FROM "__EFMigrationsHistory"
     WHERE "MigrationId"='20261115042211_RetainFriendRequestOccurrences')<>1 THEN
   RAISE EXCEPTION 'target history missing or duplicated';
 END IF;
 -- Defaults independently exercised by omitting Id, Status and ResolvedAtUtc.
 INSERT INTO "FriendRequests" ("FromUserId","ToUserId","CreatedAtUtc")
 VALUES ('qa103-a','qa103-c','2026-11-15T00:00:00Z') RETURNING "Id" INTO r;
 IF r IS NULL OR NOT EXISTS(SELECT 1 FROM "FriendRequests" WHERE "Id"=r
                            AND "Status"=0 AND "ResolvedAtUtc" IS NULL) THEN
   RAISE EXCEPTION 'request defaults';
 END IF;
 BEGIN
   INSERT INTO "FriendRequests" ("FromUserId","ToUserId","CreatedAtUtc")
   VALUES ('qa103-a','qa103-c','2026-11-15T00:00:01Z');
   RAISE EXCEPTION 'duplicate pending unexpectedly allowed';
 EXCEPTION WHEN unique_violation THEN
   GET STACKED DIAGNOSTICS violated=CONSTRAINT_NAME;
   IF violated<>'IX_FriendRequests_FromUserId_ToUserId' THEN RAISE; END IF;
 END;
 -- Opposing pending is a different direction, and terminal history may repeat a pair.
 INSERT INTO "FriendRequests" ("FromUserId","ToUserId","CreatedAtUtc")
 VALUES ('qa103-c','qa103-a','2026-11-15T00:00:00Z');
 FOR outcome IN 1..3 LOOP
   INSERT INTO "FriendRequests" ("FromUserId","ToUserId","CreatedAtUtc","Status","ResolvedAtUtc")
   VALUES ('qa103-a','qa103-c','2026-11-15T00:00:00Z',outcome,'2026-11-15T01:00:00Z')
   RETURNING "Id" INTO first_terminal;
   INSERT INTO "FriendRequests" ("FromUserId","ToUserId","CreatedAtUtc","Status","ResolvedAtUtc")
   VALUES ('qa103-a','qa103-c','2026-11-15T00:00:00Z',outcome,'2026-11-15T01:00:00Z')
   RETURNING "Id" INTO second_terminal;
   IF first_terminal IS NULL OR second_terminal IS NULL OR first_terminal=second_terminal THEN
     RAISE EXCEPTION 'terminal occurrence IDs not distinct';
   END IF;
   terminal_ids := terminal_ids || ARRAY[first_terminal,second_terminal];
 END LOOP;
 -- Check all six generated occurrences remain, including each repeated same-outcome pair.
 FOR outcome IN 1..3 LOOP
   IF (SELECT count(*) FROM "FriendRequests"
       WHERE "Id"=ANY(terminal_ids) AND "FromUserId"='qa103-a' AND "ToUserId"='qa103-c'
       AND "Status"=outcome AND "CreatedAtUtc"='2026-11-15T00:00:00Z'
       AND "ResolvedAtUtc"='2026-11-15T01:00:00Z')<>2 THEN
     RAISE EXCEPTION 'repeated terminal occurrences not retained';
   END IF;
 END LOOP;
 FOR bad IN SELECT * FROM (VALUES (-1,NULL::timestamptz),(4,'2026-11-15T01:00:00Z'::timestamptz),
     (0,'2026-11-15T01:00:00Z'::timestamptz),(1,NULL::timestamptz),
     (2,NULL::timestamptz),(3,NULL::timestamptz)) AS invalid(status,resolved) LOOP
   BEGIN
     INSERT INTO "FriendRequests" ("FromUserId","ToUserId","CreatedAtUtc","Status","ResolvedAtUtc")
     VALUES ('qa103-b','qa103-d','2026-11-15T00:00:00Z',bad.status,bad.resolved);
     RAISE EXCEPTION 'invalid outcome unexpectedly allowed';
   EXCEPTION WHEN check_violation THEN
     GET STACKED DIAGNOSTICS violated=CONSTRAINT_NAME;
     IF violated<>'CK_FriendRequests_Outcome' THEN RAISE; END IF;
   END;
 END LOOP;
 -- Known inbox row must exist, so a zero-row update cannot falsely pass the FK checks.
 IF NOT EXISTS(SELECT 1 FROM "InboxNotifications"
               WHERE "Id"='00000103-0000-4000-8000-000000000006') THEN
   RAISE EXCEPTION 'legacy inbox fixture missing';
 END IF;
 BEGIN
   UPDATE "InboxNotifications" SET "FriendRequestId"='00000103-ffff-4fff-8fff-ffffffffffff'
    WHERE "Id"='00000103-0000-4000-8000-000000000006';
   RAISE EXCEPTION 'unknown request FK unexpectedly allowed';
 EXCEPTION WHEN foreign_key_violation THEN
   GET STACKED DIAGNOSTICS violated=CONSTRAINT_NAME;
   IF violated<>'FK_InboxNotifications_FriendRequests_FriendRequestId' THEN RAISE; END IF;
 END;
 UPDATE "InboxNotifications" SET "FriendRequestId"=r
  WHERE "Id"='00000103-0000-4000-8000-000000000006';
 DELETE FROM "FriendRequests" WHERE "Id"=r;
 IF NOT EXISTS(SELECT 1 FROM "InboxNotifications"
   WHERE "Id"='00000103-0000-4000-8000-000000000006' AND "FriendRequestId" IS NULL) THEN
   RAISE EXCEPTION 'request deletion did not set inbox FK null';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid
   JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attname='FriendRequestId'
   WHERE i.indrelid='public."InboxNotifications"'::regclass
   AND c.relname='IX_InboxNotifications_FriendRequestId' AND i.indkey[0]=a.attnum
   AND i.indpred IS NULL
   AND i.indnatts=1 AND i.indisvalid AND i.indisready AND NOT i.indisunique) THEN
   RAISE EXCEPTION 'inbox FK lookup index missing';
 END IF;
END $qa$;
ROLLBACK;
SELECT 'MIGRATION_CONTROLS_ROLLED_BACK';
