-- PrePack schema v1

CREATE TABLE IF NOT EXISTS staff (
  id          INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  code        VARCHAR(20)  NULL,
  name        VARCHAR(100) NOT NULL,
  short_name  VARCHAR(30)  NOT NULL COMMENT 'ชื่อย่อที่พิมพ์บนฉลาก',
  role        ENUM('packer','pharmacist') NOT NULL DEFAULT 'packer',
  active      TINYINT(1)   NOT NULL DEFAULT 1,
  created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS drugs (
  id          INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  his_code    VARCHAR(30)  NULL COMMENT 'รหัสยาใน HIS (ถ้ามี)',
  name        VARCHAR(150) NOT NULL,
  strength    VARCHAR(50)  NOT NULL DEFAULT '',
  form        ENUM('tablet','capsule','cream','other') NOT NULL DEFAULT 'tablet',
  unit        VARCHAR(20)  NOT NULL DEFAULT 'เม็ด',
  bud_days    INT UNSIGNED NOT NULL DEFAULT 180 COMMENT 'อายุหลังแบ่งบรรจุ (วัน)',
  active      TINYINT(1)   NOT NULL DEFAULT 1,
  created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  KEY idx_drugs_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ขนาดบรรจุมาตรฐานของยาแต่ละตัว (ทำให้ทุกคนแพ็คแบบเดียวกัน)
CREATE TABLE IF NOT EXISTS pack_sizes (
  id          INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  drug_id     INT UNSIGNED NOT NULL,
  pack_type   ENUM('prepack','unitdose','cream') NOT NULL,
  qty         DECIMAL(10,2) NOT NULL COMMENT 'จำนวนต่อซอง (เม็ด หรือ g)',
  work_point  DECIMAL(6,2)  NOT NULL DEFAULT 1 COMMENT 'แต้มภาระงานต่อ 1 ซอง',
  active      TINYINT(1)   NOT NULL DEFAULT 1,
  UNIQUE KEY uq_pack_size (drug_id, pack_type, qty),
  CONSTRAINT fk_pack_sizes_drug FOREIGN KEY (drug_id) REFERENCES drugs(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- บันทึกการแพ็คแต่ละรอบ
CREATE TABLE IF NOT EXISTS pack_jobs (
  id            INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
  drug_id       INT UNSIGNED NOT NULL,
  pack_size_id  INT UNSIGNED NOT NULL,
  pack_type     ENUM('prepack','unitdose','cream') NOT NULL,
  qty_per_pack  DECIMAL(10,2) NOT NULL COMMENT 'snapshot จาก pack_sizes',
  pack_count    INT UNSIGNED  NOT NULL,
  work_point    DECIMAL(6,2)  NOT NULL COMMENT 'snapshot แต้มต่อซอง',
  lot_no        VARCHAR(40)   NOT NULL,
  src_exp_date  DATE          NOT NULL COMMENT 'วันหมดอายุของภาชนะเดิม',
  pack_date     DATE          NOT NULL,
  bud_date      DATE          NOT NULL COMMENT 'วันหมดอายุหลังแบ่งบรรจุ',
  packer_id     INT UNSIGNED  NOT NULL,
  checker_id    INT UNSIGNED  NULL,
  checked_at    DATETIME      NULL,
  status        ENUM('packed','checked','cancelled') NOT NULL DEFAULT 'packed',
  note          VARCHAR(255)  NOT NULL DEFAULT '',
  created_at    DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY idx_jobs_date (pack_date),
  KEY idx_jobs_lot (lot_no),
  KEY idx_jobs_packer (packer_id, pack_date),
  CONSTRAINT fk_jobs_drug    FOREIGN KEY (drug_id)      REFERENCES drugs(id),
  CONSTRAINT fk_jobs_size    FOREIGN KEY (pack_size_id) REFERENCES pack_sizes(id),
  CONSTRAINT fk_jobs_packer  FOREIGN KEY (packer_id)    REFERENCES staff(id),
  CONSTRAINT fk_jobs_checker FOREIGN KEY (checker_id)   REFERENCES staff(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS settings (
  k           VARCHAR(50) PRIMARY KEY,
  v           TEXT        NOT NULL COMMENT 'JSON',
  updated_at  DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
