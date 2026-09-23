# PrePack Dev Log

## ✅ ทำแล้ว

### Session 1 — 23 ก.ย. 2569
- วางโครงโปรเจกต์ Go + Gin + MySQL
- Schema: `staff`, `drugs`, `pack_sizes`, `pack_jobs`, `settings` + migration runner (embed)
- บันทึกแพ็ค: เลือกได้เฉพาะขนาดบรรจุมาตรฐาน, คำนวณ EXP หลังแบ่ง = min(วันบรรจุ + bud_days, EXP เดิม), ไม่ให้บันทึกยาที่หมดอายุแล้ว
- ตรวจ (ผู้ตรวจต้องไม่ใช่ผู้บรรจุ), ยกเลิกพร้อมเหตุผล, ค้นหาตาม Lot
- รายงานภาระงานรายคน (ซอง, แต้มงาน, จำนวนที่ตรวจให้ผู้อื่น) + export CSV
- พิมพ์ฉลากสติกเกอร์ 85×50 มม. 3×3 ดวง มีส่วนหัวทิ้ง ตั้ง layout ได้ในหน้าเว็บ มีโหมดเส้นขอบไว้ทดสอบตำแหน่ง
- ทดสอบ end-to-end กับ MariaDB 10.11 + Chromium (PDF ออกมาหน้าละ 85×50 มม.)

### Session 2 — 23 ก.ย. 2569 (คุยแนวทาง ยังไม่เขียนโค้ด)
- ผู้ใช้ขอหน้าจอเดียว: header (ผู้บรรจุ/ยา/Lot/EXP/จำนวนต่อซอง + ⚙), body = ตัวอย่างสติกเกอร์, footer = จำนวนหน้า/ดวง + พิมพ์
- ตกลงเปลี่ยนเป็น C# .NET 8 + WebView2 exe ไฟล์เดียว, silent print, ดึงยา/Lot/EXP จาก INVS (หา invs.ini อัตโนมัติแบบ BoxBox)
- config: seed ฝังใน exe → %APPDATA%\PrePack\config.json, log การพิมพ์เก็บใน MySQL (โปรแกรมสร้าง DB เอง)
- ประเภทยาตัดสินจากหน่วยใน INVS + ชื่อยา → อายุบนฉลาก เม็ด 1 ปี / ครีม 6 เดือน / น้ำ 30 วัน
- รายละเอียดทั้งหมดอยู่ใน `PLAN.md`

### Session 3 — 23 ก.ย. 2569 (ตกลงรายละเอียดเพิ่ม ยังไม่เขียนโค้ด)
- DB `prepack` แยกจาก HOSxP / JHCIS (ยืนยันแล้ว)
- วันบรรจุ = วันนี้เสมอ ไม่ต้องกรอก
- Template ฉลากแต่ละดวง 4 บรรทัด ชิดซ้ายทั้งหมด: `ยา: <ชื่อยา ตัดคำ> #<จำนวน ไม่มีหน่วย>` / `Lot: <lot> <ผู้บรรจุ ตัดคำ>` / `Mfd: <วันบรรจุ>` / `Exp: <วันหมดอายุ>`
- อ่านโค้ด BoxBox (`D:\COACH\source\repo\BoxBox`) แล้วพบว่าต่างจาก PLAN: เป็น net9.0, ไม่ใช้ single-file (ใช้ Inno Setup เพราะ AV), พิมพ์ด้วย GDI+ ไม่ใช่ PrintAsync, `QueryInvs` รับ SQL ดิบจาก JS
- ตัดสินใจ: ใช้ **.NET 10 LTS** (8/9 หมด support 10 พ.ย. 2569) และยังคง **exe ไฟล์เดียว** (ปิด compression ลด AV), พิมพ์ด้วย PrintAsync, INVS ใช้ method เฉพาะงาน + parameter
- อัปเดต `PLAN.md` ข้อ 1, 3, 4, 7, 9 ตามนี้

### Session 4 — 23 ก.ย. 2569 (implement v1 C#)
- สร้าง `src/PrePack` (WinForms + WebView2, exe ไฟล์เดียว) + `tests/PrePack.Tests` (xUnit 46 tests) + `PrePack.sln`
- **ฟังก์ชันสร้าง DB schema** `Data/Schema.cs` `EnsureAsync`: ตรวจชื่อ DB → `CREATE DATABASE IF NOT EXISTS` (ถ้าไม่มีสิทธิ์ บอกคำสั่งให้ admin) →
  **ปฏิเสธถ้าเจอตาราง HOSxP/JHCIS** → migration ฝังใน exe (`Data/Migrations/001_init.sql`: `staff`, `print_logs`)
  เรียกตอนเปิดโปรแกรม และตอนกด "ทดสอบ + สร้างตาราง + บันทึก" ใน ⚙ → ฐานข้อมูล
- INVS: หา invs.ini อัตโนมัติ/เลือกไฟล์ (port จาก BoxBox), ค้นหายา, Lot ที่มีของเรียง EXP, auto-detect คอลัมน์หน่วย — query ตายตัว + parameter
- หน้าหลัก: ผู้บรรจุ (จำคนล่าสุด) → ยา (INVS / Manual) → ประเภท (เดาจากชื่อ+หน่วย) → Lot/EXP เดิมเติมเอง → EXP ฉลากคำนวณ (แก้ได้) → #จำนวน + ปุ่มลัด → preview → พิมพ์ (F9)
- Silent print ด้วย `PrintAsync` (85×50 มม., margin 0) + ปุ่ม "ทดสอบตำแหน่ง" (เส้นขอบ ไม่บันทึก)
- log ทุกการพิมพ์ลง `print_logs`; ถ้า MySQL ล่ม เก็บคิวใน `%APPDATA%\PrePack\pending-logs.jsonl` แล้วส่งทีหลัง
- ⚙: รายงานภาระงาน (ตาม mockup) + ประวัติ/ค้น Lot + CSV, เจ้าหน้าที่, สติกเกอร์/เครื่องพิมพ์, ประเภทยา/อายุยา/ตารางหน่วย, ฐานข้อมูล
- **บั๊กที่เจอจาก test:** เครื่องใช้ th-TH (พ.ศ.) → parse/format วันที่ต้องใช้ InvariantCulture ไม่งั้น INVS date เพี้ยน 543 ปี (แก้แล้ว + มี test)
- **แต้มภาระงาน** (ผู้ใช้ขอระหว่าง session): แต้ม = ดวง × factor ตามประเภทยา + ช่วง #จำนวน, ตั้งที่ ⚙ → แต้มภาระงาน (ต้องใส่รหัส),
  ตาราง `work_factors` ใน MySQL (seed factor 1), snapshot `work_factor`/`work_points` ใน `print_logs`, รายงานแสดงแต้ม + % ภาระงานต่อคน
  (แก้ `001_init.sql` ตรง ๆ เพราะยังไม่เคย apply ที่ไหน — หลังจากนี้ต้องเพิ่ม migration ใหม่เท่านั้น)
- cache รายชื่อผู้บรรจุ + factor ใน C# → MySQL ล่มกลางวันยังพิมพ์ได้ (log เข้าคิว); insert ใช้ ON DUPLICATE KEY แทน INSERT IGNORE
- ตรวจแล้ว: build/test บน net9 (`-p:PrePackTfm=net9.0-windows`) 62 tests ผ่าน, publish single-file แล้วรัน `PrePack.exe --print-test-pdf`
  → กด "ทดสอบตำแหน่ง" ผ่านเส้นทางจริง (JS → bridge.Print → print settings) ได้ PDF 1 หน้า 85.0×50.1 มม. 3×3 ดวงถูกต้อง,
  UI ทดสอบในเบราว์เซอร์ด้วย mock bridge
- **ยังไม่ได้ตรวจ:** build ด้วย .NET 10 จริง, ต่อ MySQL/INVS จริง, `PrintAsync` ไปเครื่องพิมพ์จริง (self-test ใช้ PrintToPdfAsync)

### Session 5 — 23 ก.ย. 2569 (local-first + sync, icon, สไตล์ Google)
- **บันทึกใน SQLite ในเครื่องก่อน** (`prepack-local.db`) แล้ว sync ขึ้น MySQL เบื้องหลังทุก 2 นาที, local เก็บต่อเป็น backup
  - เจ้าหน้าที่ใช้ `uid` (GUID) เป็น key ร่วม — migration `002_staff_uid.sql`; sync สองทาง ใครแก้ทีหลังชนะ
  - factor sync ทั้งตารางตาม version; ค่าเริ่มต้นทั้งสองฝั่ง = epoch → ของที่เคยตั้งแล้วชนะเสมอ
  - log ส่งขึ้นอย่างเดียว mark ทีละแถวหลัง insert สำเร็จ; ปุ่มกู้คืน = ส่งทั้งหมดขึ้นใหม่ (ไม่ซ้ำเพราะ client_uid)
  - รายงานใช้ MySQL ถ้าได้ ไม่งั้นใช้ local + แถบเตือน; ประวัติแสดง "รอ sync"
  - ลบ `PendingLogs` (jsonl) — แทนด้วย local DB
- ⚙ → ฐานข้อมูล / sync: สถานะ, Sync ตอนนี้, กู้คืน; footer มีชิปสถานะ sync
- ไอคอนเม็ดยา `app.ico` (exe + title bar)
- เปลี่ยน UI เป็นสไตล์ Google Material 3, ปุ่มปิด ⚙ ย้ายไปขวาบน
- ตรวจแล้ว: 80 tests (รวม LocalDb round-trip ภายใต้ th-TH + SyncRules), publish single-file แล้ว self-test ผ่าน
  (SQLite native โหลดได้ใน exe เดียว, ใช้ `PREPACK_DATA_DIR` ชี้ temp)
- **ยังไม่ได้ตรวจ:** sync กับ MySQL จริง
- (ต่อ) เปลี่ยนเป็น **server ก่อน**: Init / ListStaff / SaveStaff / factor / Print เรียก `SyncService.TryNowAsync()` รอผลก่อนตอบ,
  server ไม่ตอบ → ใช้ local ทันที (backoff 60 วิ); ข้อความหน้าจอบอกว่า "บันทึกลง server แล้ว" หรือ "บันทึกในเครื่อง"
- seed เจ้าหน้าที่ 21 คน (`seed-config.json` → `LocalDb.SeedStaff`, uid จากชื่อ) — self-test exe ได้ staff=21, 82 tests ผ่าน
- **ชื่อบนฉลาก** สำหรับยาชื่อยาว: ช่องข้างช่องยา, จำต่อยา, sync สองทาง (MySQL `003_drug_labels.sql`, local schema v2
  — LocalDb.Migrate เปลี่ยนเป็นทีละ step), เลือกยาแล้วอ่านจาก server ตรง ๆ ก่อน (fallback local)
- ฉลากบรรทัด 2: Lot ชิดซ้าย · ผู้บรรจุชิดขวา; header ปรับความกว้างให้พอดีแถวเดียวที่ 1280 px (แคบกว่านั้นขึ้นบรรทัดใหม่)
- 90 tests ผ่าน, publish + self-test ผ่าน (PDF เห็น Lot ซ้าย / ผู้บรรจุขวา)
- ฉลากบรรทัด 2 เปลี่ยนเป็น `Lot: A12345 ผู้บรรจุ:สมชาย` (ชิดซ้าย, ตัดคำ) — ดูตัวอย่างได้ที่ `wwwroot/dev/label-preview.html`
- UI เปลี่ยนเป็น**สไตล์ Gmail** (gm3 tokens, พื้น #f6f8fc, ช่องค้นหายาแบบ search pill, ปุ่มพิมพ์แบบ Compose,
  เมนู ⚙ แบบ Gmail settings, ตารางแบบรายการอีเมล); mock รองรับ `index.html#demo` / `#open=<tab>` ไว้ถ่ายภาพโดยไม่ build
- **ฝังฟอนต์** Roboto (latin) + Noto Sans Thai (thai) แบบ variable woff2 รวม ~68 KB ใน `wwwroot/fonts/` (OFL 1.1)
  ใช้กับ UI; สติกเกอร์ยังใช้ Leelawadee UI
- build ล่าสุด: 90 tests ผ่าน, exe 120.4 MB, self-test ผ่าน (staff=21, fonts=Roboto+Noto Sans Thai โหลดใน exe, PDF 85×50 มม.)
- ⚙ → **การแสดงผล**: ปรับขนาดหน้าจอ 50–150% แบบ BoxBox (ปุ่ม 50…100 ทีละ 5 + 110/125/150), จำใน `config.json`
  (`uiZoomPercent`) ต่อเครื่อง, Ctrl+ลูกกลิ้งก็จำ; ใช้ WebView2 `ZoomFactor` (ไม่ใช่ CSS zoom แบบ BoxBox เพราะจะทำให้
  สติกเกอร์ใน print area เพี้ยน) — ทดสอบ self-test ที่ 60% ได้ PDF เหมือน 100% ทุกอย่าง
- ผู้ใช้แจ้ง "รหัสไม่ถูกต้อง" ตอนปลดล็อก factor — hash ในโค้ดตรงกับรหัสจริง (test ผ่าน) สาเหตุน่าจะเป็นการพิมพ์
  แก้: ตัดช่องว่างหน้า/หลังรหัส, เตือนเมื่อพิมพ์เป็นภาษาไทย / Caps Lock เปิด, ปุ่ม 👁 แสดงรหัส
- ตั้งค่าดวง: เพิ่ม**ส่วนท้าย (ฉีกทิ้ง)**, ความสูง**แต่ละแถว** และความกว้าง**แต่ละคอลัมน์** หลายค่า (`LabelLayout.RowHeights/ColWidths/
  FooterHeight` ↔ `labels.js rowHeightsOf/colWidthsOf`), ฟอร์มบอกที่ว่างเหลือ + ปุ่ม "ใส่ค่าแบ่งเท่ากัน", preview วาดหัว/ท้าย
  — 97 tests ผ่าน, ดูตัวอย่างแล้ว (mock `#open=label&custom`)
- build exe (120.4 MB, 13:40): self-test ผ่านทั้ง layout ค่าเริ่มต้น และ layout หัว 6 / ท้าย 4 / แถว 12,13,13 / คอลัมน์ 30,25,28
  — PDF 85×50 มม. แสดงหัว-ท้ายและขนาดดวงไม่เท่ากันตรงตามที่ตั้ง

## 📌 Pending
- [ ] ติดตั้ง .NET 10 SDK บนเครื่อง dev (ตอนนี้มีแค่ 9.0.318) แล้ว build/test โดยไม่ต้องใส่ `-p:PrePackTfm`
- [ ] ได้ connection config ของ MySQL server โรงพยาบาล → กด ⚙ → ฐานข้อมูล ทดสอบสร้างตารางจริง
- [ ] ต่อ INVS จริง → ดูว่า auto-detect คอลัมน์หน่วยใน `DRUG_GN` ได้คอลัมน์ไหน, ชนิดของ `EXPIRED_DATE` เป็นข้อความ YYYYMMDD จริงไหม
- [ ] ทดสอบพิมพ์กับเครื่องพิมพ์สติกเกอร์จริง แล้วปรับ offset / ขนาดส่วนหัว
- [ ] ตั้ง factor จริงใน ⚙ → แต้มภาระงาน (ตอนนี้ = 1 ทุกช่วง)
- [ ] ตัดสินใจ: ขั้นตอนเภสัชกรตรวจ?
- [ ] อัปเดต README.md (ยังอธิบายการติดตั้ง Go v0.1)
- [ ] auto-update (exe เดียว: โหลด exe ใหม่ + ตรวจ SHA-256 + สลับไฟล์ตอนปิด)
- [ ] override ประเภท/หน่วยต่อยาใน MySQL (PLAN ข้อ 8) — ตอนนี้แก้ได้แค่ต่อครั้งที่พิมพ์
- [ ] ตัดสินใจ: สติกเกอร์จะใช้ Noto Sans Thai + Roboto แทน Leelawadee UI ไหม (ต้องดูตัวอย่างการตัดคำก่อน)
- [ ] ลบโค้ด Go v0.1 เมื่อ v1 ใช้งานจริงแล้ว

## 📋 Version History

### v1.0.0 — 23 กันยายน 2569 (ยังไม่ได้ใช้งานจริง)
- เขียนใหม่เป็น C# WinForms + WebView2 exe ไฟล์เดียว: หน้าจอเดียว, ดึงยา/Lot/EXP จาก INVS, silent print, log ภาระงานลง MySQL, รายงานรายคน

### v0.1.0 — 23 กันยายน 2569
- เวอร์ชันแรก: บันทึกแพ็ค, ตรวจ, รายงานภาระงาน, พิมพ์ฉลาก 3×3
