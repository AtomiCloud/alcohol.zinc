\set ON_ERROR_STOP on
BEGIN;
INSERT INTO "Users" ("Id","Username","Email","EmailVerified","Scopes","Active") VALUES
 ('qa103-a','qa103-a','a@qa.invalid',false,ARRAY[]::text[],true),
 ('qa103-b','qa103-b','b@qa.invalid',false,ARRAY[]::text[],true),
 ('qa103-c','qa103-c','c@qa.invalid',false,ARRAY[]::text[],true),
 ('qa103-d','qa103-d','d@qa.invalid',false,ARRAY[]::text[],true);
INSERT INTO "FriendRequests" ("Id","FromUserId","ToUserId","CreatedAtUtc") VALUES
 ('00000103-0000-4000-8000-000000000001','qa103-a','qa103-b','2026-11-01T01:02:03.456789Z'),
 ('00000103-0000-4000-8000-000000000002','qa103-b','qa103-a','2026-11-01T01:02:03.456789Z'),
 ('00000103-0000-4000-8000-000000000003','qa103-c','qa103-d','2026-11-01T01:02:03.456789Z');
INSERT INTO "Friendships" ("Id","UserId","FriendUserId","CreatedAtUtc") VALUES
 ('00000103-0000-4000-8000-000000000004','qa103-c','qa103-d','2026-11-01T01:02:03Z'),
 ('00000103-0000-4000-8000-000000000005','qa103-d','qa103-c','2026-11-01T01:02:03Z');
INSERT INTO "InboxNotifications" ("Id","UserId","Kind","Title","Body","ActorUserId","Data","CreatedAtUtc","ReadAtUtc") VALUES
 ('00000103-0000-4000-8000-000000000006','qa103-d','friend_request','synthetic','synthetic','qa103-c','{"fromUserId":"qa103-c"}','2026-11-01T01:02:04Z',NULL),
 ('00000103-0000-4000-8000-000000000007','qa103-d','friend_request','synthetic','synthetic','qa103-c','{"fromUserId":"qa103-c"}','2026-11-01T01:02:04Z','2026-11-01T02:00:00Z'),
 ('00000103-0000-4000-8000-000000000008','qa103-d','friend_request','synthetic','synthetic','qa103-c','{"requestId":"00000103-0000-4000-8000-000000000003"}','2026-11-01T01:02:04Z',NULL);
COMMIT;
