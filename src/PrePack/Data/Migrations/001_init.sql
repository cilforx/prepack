-- PrePack v1 schema. MySQL 5.7+ / MariaDB 10.3+ (no JSON columns, no CTEs).

CREATE TABLE IF NOT EXISTS staff (
  id          INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  name        VARCHAR(100) NOT NULL,
  short_name  VARCHAR(30)  NOT NULL COMMENT 'ชื่อที่พิมพ์บนฉลาก',
  active      TINYINT(1)   NOT NULL DEFAULT 1,
  created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- one row per print; names are snapshots so later edits never rewrite history
CREATE TABLE IF NOT EXISTS print_logs (
  id              BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  client_uid      CHAR(36)      NOT NULL COMMENT 'กันบันทึกซ้ำตอนส่ง log ที่ค้างไว้',
  staff_id        INT UNSIGNED  NOT NULL,
  staff_name      VARCHAR(100)  NOT NULL,
  source          ENUM('INVS','MANUAL') NOT NULL,
  working_code    VARCHAR(30)   NULL COMMENT 'NULL เมื่อเป็นยา Manual',
  drug_name       VARCHAR(200)  NOT NULL,
  drug_type       ENUM('tablet','cream','liquid') NOT NULL,
  unit            VARCHAR(20)   NOT NULL,
  qty_per_pack    DECIMAL(10,2) NOT NULL,
  lot_no          VARCHAR(40)   NOT NULL,
  pack_date       DATE          NOT NULL,
  src_exp_date    DATE          NULL COMMENT 'EXP ภาชนะเดิม',
  label_exp_date  DATE          NOT NULL COMMENT 'EXP ที่พิมพ์บนฉลาก',
  pages           INT UNSIGNED  NOT NULL,
  stickers        INT UNSIGNED  NOT NULL,
  work_factor     DECIMAL(6,2)  NOT NULL COMMENT 'snapshot factor ต่อดวง ณ เวลาพิมพ์',
  work_points     DECIMAL(12,2) NOT NULL COMMENT 'stickers × work_factor',
  printed_at      DATETIME      NOT NULL,
  machine_name    VARCHAR(64)   NOT NULL,
  UNIQUE KEY uq_print_logs_uid (client_uid),
  KEY idx_print_logs_date (pack_date),
  KEY idx_print_logs_staff (staff_id, pack_date),
  KEY idx_print_logs_lot (lot_no),
  KEY idx_print_logs_code (working_code),
  CONSTRAINT fk_print_logs_staff FOREIGN KEY (staff_id) REFERENCES staff(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- workload points per sticker by drug type and #qty band (shared by every machine; edited with the ⚙ password)
CREATE TABLE IF NOT EXISTS work_factors (
  id          INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  drug_type   ENUM('tablet','cream','liquid') NOT NULL,
  max_qty     DECIMAL(10,2) NULL COMMENT '#จำนวนต่อซองสูงสุดของช่วง (NULL = ขึ้นไป)',
  factor      DECIMAL(6,2)  NOT NULL DEFAULT 1,
  updated_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  KEY idx_work_factors_type (drug_type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
