\set ON_ERROR_STOP on
-- Independent acceptance oracle from the reviewed schema contract, not EF-generated SQL.
-- Run only on a clone of the seeded disposable predecessor, never on the tested database.
BEGIN;
DROP INDEX "IX_FriendRequests_FromUserId_ToUserId";
ALTER TABLE "InboxNotifications" ADD "FriendRequestId" uuid;
ALTER TABLE "FriendRequests" ALTER COLUMN "Id" SET DEFAULT gen_random_uuid();
ALTER TABLE "FriendRequests" ADD "ResolvedAtUtc" timestamp with time zone;
ALTER TABLE "FriendRequests" ADD "Status" integer NOT NULL DEFAULT 0;
CREATE INDEX "IX_InboxNotifications_FriendRequestId" ON "InboxNotifications" ("FriendRequestId");
CREATE UNIQUE INDEX "IX_FriendRequests_FromUserId_ToUserId"
 ON "FriendRequests" ("FromUserId", "ToUserId") WHERE "Status" = 0;
ALTER TABLE "FriendRequests" ADD CONSTRAINT "CK_FriendRequests_Outcome"
 CHECK ("Status" BETWEEN 0 AND 3 AND (("Status" = 0 AND "ResolvedAtUtc" IS NULL)
 OR ("Status" <> 0 AND "ResolvedAtUtc" IS NOT NULL)));
ALTER TABLE "InboxNotifications" ADD CONSTRAINT "FK_InboxNotifications_FriendRequests_FriendRequestId"
 FOREIGN KEY ("FriendRequestId") REFERENCES "FriendRequests" ("Id") ON DELETE SET NULL;
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
 VALUES ('20261115042211_RetainFriendRequestOccurrences', '8.0.2');
COMMIT;
