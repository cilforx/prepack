-- Staff get a GUID so machines that worked offline (local SQLite) can sync the same person.

ALTER TABLE staff ADD COLUMN uid CHAR(36) NULL AFTER id;

UPDATE staff SET uid = UUID() WHERE uid IS NULL;

ALTER TABLE staff ADD UNIQUE KEY uq_staff_uid (uid);
