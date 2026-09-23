-- Short sticker names for drugs with long names; shared by every machine.
-- drug_key = "INVS:<working_code>" or "NAME:<sha1 of name>" (fits a 767-byte index even on COMPACT rows).

CREATE TABLE IF NOT EXISTS drug_labels (
  drug_key      VARCHAR(64)  NOT NULL PRIMARY KEY,
  drug_name     VARCHAR(200) NOT NULL COMMENT 'ชื่อเต็ม (อ่านง่ายเวลาเปิดดูตาราง)',
  label_name    VARCHAR(100) NOT NULL DEFAULT '' COMMENT 'ชื่อบนฉลาก; ว่าง = ใช้ชื่อเต็ม',
  updated_at    DATETIME     NOT NULL,
  machine_name  VARCHAR(64)  NOT NULL DEFAULT ''
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
