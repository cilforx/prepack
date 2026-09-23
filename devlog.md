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
- [ ] ฝังฟอนต์ Sarabun (ตอนนี้ใช้ Leelawadee UI ของ Windows)
- [ ] ลบโค้ด Go v0.1 เมื่อ v1 ใช้งานจริงแล้ว

## 📋 Version History

### v1.0.0 — 23 กันยายน 2569 (ยังไม่ได้ใช้งานจริง)
- เขียนใหม่เป็น C# WinForms + WebView2 exe ไฟล์เดียว: หน้าจอเดียว, ดึงยา/Lot/EXP จาก INVS, silent print, log ภาระงานลง MySQL, รายงานรายคน

### v0.1.0 — 23 กันยายน 2569
- เวอร์ชันแรก: บันทึกแพ็ค, ตรวจ, รายงานภาระงาน, พิมพ์ฉลาก 3×3
