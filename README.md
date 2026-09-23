# PrePack — ระบบแบ่งบรรจุยา Pre-pack / Unit dose

เว็บแอปในเครือข่ายโรงพยาบาลสำหรับห้องยา ใช้บันทึกการแบ่งบรรจุยา พิมพ์ฉลากสติกเกอร์ และสรุปภาระงานของเจ้าหน้าที่แต่ละคน

- **ยาเม็ด Pre-pack** — แบ่งจากกระปุก 1000 เม็ดเป็นซอง 30 / 60 / 100 … เม็ด
- **Unit dose** — ซองละ 1–5 เม็ด
- **ครีม** — แบ่งจากกระปุก 500 g เป็น 5 g

## แก้ปัญหาอะไร

| ปัญหาเดิม | ใน PrePack |
|---|---|
| ไม่ได้บันทึกภาระงาน | ทุกรอบที่แพ็คเก็บลง MySQL ระบุผู้บรรจุ, Lot, จำนวน และวันที่ |
| ปริมาณงานแต่ละคนไม่เท่ากัน | หน้า **ภาระงาน** สรุปจำนวนซองและ "แต้มงาน" ต่อคน export เป็น CSV ได้ |
| รูปแบบไม่เป็นมาตรฐาน | ขนาดบรรจุต่อยากำหนดไว้ในข้อมูลยา ผู้บรรจุเลือกได้เฉพาะขนาดมาตรฐาน วันหมดอายุคำนวณให้อัตโนมัติ |
| พิมพ์ฉลากจาก Excel ยาก | หน้าพิมพ์ฉลากตั้งขนาดเป็นมิลลิเมตรตรงกับสติกเกอร์ 8.5 × 5 ซม. แบ่ง 3×3 ดวง ข้ามส่วนหัวที่ฉีกทิ้งให้ |

## ฉลาก

แต่ละดวงประมาณ 27.7 × 13.3 มม. มีข้อมูล:

```
Paracetamol 500 mg
#30 เม็ด     L:A12345
บรรจุ 23/09/69
EXP 22/03/70
ผู้บรรจุ สมชาย
```

**EXP หลังแบ่งบรรจุ** = วันบรรจุ + "อายุหลังแบ่ง (วัน)" ของยานั้น แต่ต้องไม่เกิน EXP ของภาชนะเดิม ถ้ายาเดิมหมดอายุแล้ว ระบบจะไม่ให้บันทึก

ขนาดแผ่น, ความสูงส่วนหัว, จำนวนแถวและคอลัมน์, ระยะห่าง, การเลื่อนตำแหน่ง, ขนาดตัวอักษร และปี พ.ศ./ค.ศ. ปรับได้ที่แท็บ **ตั้งค่าฉลาก** โดยไม่ต้องแก้โค้ด

### ตั้งค่าเครื่องพิมพ์ครั้งแรก
1. ที่ Windows → Printer preferences ตั้ง paper size เป็น 85 × 50 มม. (หรือขนาดสติกเกอร์จริง)
2. ในหน้าพิมพ์ของ Chrome/Edge: Margins = **None**, Scale = **100%**, ปิด Headers and footers
3. ติ๊ก "แสดงเส้นขอบ" แล้วพิมพ์ทดสอบ ถ้าเลื่อน ให้แก้ที่ "เลื่อนขวา / เลื่อนลง" ในแท็บตั้งค่าฉลาก

## การติดตั้ง

### 1. เตรียมฐานข้อมูล (MySQL 5.7+ / MariaDB 10.3+)

```sql
CREATE DATABASE prepack CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'prepack'@'%' IDENTIFIED BY 'รหัสผ่าน';
GRANT ALL PRIVILEGES ON prepack.* TO 'prepack'@'%';
```

ไม่ต้องสร้างตารางเอง โปรแกรมจะสร้างให้ตอนเริ่มทำงาน (migrations อยู่ใน `internal/db/migrations/`)

### 2. ตั้งค่าการเชื่อมต่อ

```bash
cp .env.example .env   # แล้วแก้ host / user / password
```

หรือตั้งเป็น environment variable `PREPACK_DB_HOST`, `PREPACK_DB_PORT`, `PREPACK_DB_USER`, `PREPACK_DB_PASS`, `PREPACK_DB_NAME`, `PREPACK_ADDR`, `PREPACK_TZ`

### 3. Build และรัน

ต้องใช้ Go 1.23 ขึ้นไป หน้าเว็บทั้งหมดฝังอยู่ในไฟล์โปรแกรมไฟล์เดียว

```bash
go build -o prepack ./cmd/prepack          # Linux
GOOS=windows go build -o prepack.exe ./cmd/prepack   # Windows server

./prepack                 # ใช้ .env ในโฟลเดอร์ปัจจุบัน
./prepack -env /path/.env # ระบุไฟล์ config
./prepack -migrate        # สร้าง/อัปเดตตารางแล้วออก
```

จากนั้นเปิด `http://<ip-server>:8080` จากเครื่องใดก็ได้ในโรงพยาบาล

### 4. เริ่มใช้งาน
1. แท็บ **เจ้าหน้าที่** — เพิ่มผู้บรรจุและเภสัชกร
2. แท็บ **ข้อมูลยา** — เพิ่มยาและขนาดบรรจุ เช่น `prepack:30,60,100 unitdose:1,2 cream:5`
3. แท็บ **บันทึกแพ็ค** — เลือกผู้บรรจุ ยา ขนาด จำนวนซอง Lot และ EXP เดิม แล้วกด **บันทึก + พิมพ์ฉลาก**
4. แท็บ **ประวัติ / ตรวจ** — เภสัชกรกด "ตรวจ ✓" (ผู้ตรวจต้องไม่ใช่คนเดียวกับผู้บรรจุ) ค้นหาตาม Lot ได้เมื่อมี recall

## แต้มงาน (work point)

แต่ละขนาดบรรจุมีแต้มต่อซอง เพื่อให้เทียบงานที่ยากต่างกันได้ เช่น นับ 100 เม็ดใช้เวลามากกว่า 30 เม็ด ค่าเริ่มต้นคือ pre-pack = qty/30, unit dose และครีม = 1 ซึ่งแก้ได้ในแท็บข้อมูลยา แต้มจะถูกบันทึกติดไปกับงานแต่ละรอบ ถ้าแก้ภายหลัง ข้อมูลย้อนหลังจะไม่เปลี่ยน

## API

| Method | Path | ใช้ทำ |
|---|---|---|
| GET | `/api/health` | ตรวจการเชื่อมต่อ DB |
| GET/POST | `/api/staff` · PUT `/api/staff/:id` | เจ้าหน้าที่ |
| GET/POST | `/api/drugs` · PUT `/api/drugs/:id` | ข้อมูลยา |
| POST | `/api/drugs/:id/sizes` · PUT `/api/sizes/:id` | ขนาดบรรจุ |
| GET/POST | `/api/jobs` (filter: `from,to,packer_id,drug_id,lot,status`) | บันทึกแพ็ค |
| GET | `/api/jobs/:id` | รายการเดียว |
| POST | `/api/jobs/:id/check` `{checker_id}` | ตรวจ |
| POST | `/api/jobs/:id/cancel` `{reason}` | ยกเลิก |
| GET | `/api/reports/workload?from&to` | สรุปภาระงาน |
| GET/PUT | `/api/settings/label` | ตั้งค่าฉลาก |

## โครงสร้าง

```
cmd/prepack/          main — อ่าน config, migrate, เปิด HTTP server
internal/config/      โหลด .env / environment
internal/db/          เชื่อมต่อ MySQL + migrations (embed)
internal/domain/      กฎธุรกิจ: คำนวณ EXP, ตรวจข้อมูล, layout ฉลาก (มี unit test)
internal/store/       SQL ของแต่ละตาราง
internal/api/         Gin routes + handlers
web/                  หน้าเว็บ (HTML/CSS/JS ล้วน ไม่ต้อง build) ฝังเข้า binary
```
