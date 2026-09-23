# PrePack — Drug Repackaging (Pre-pack / Unit dose) Label Printer

อ่าน `devlog.md` ก่อนเริ่มงานทุกครั้ง และอัปเดตเมื่อจบงาน · การออกแบบ/ข้อตกลงทั้งหมดอยู่ใน `PLAN.md`

> โค้ดหลักคือ **C# ใน `src/PrePack`** (v1)
> โค้ด Go + Gin ที่ root (`cmd/`, `internal/`, `web/`) เป็นต้นแบบ v0.1 — ใช้อ้างอิงเท่านั้น ไม่พัฒนาต่อ

## Stack
- C# .NET 10 (LTS) WinForms + WebView2, exe ไฟล์เดียว (self-contained, ไม่บีบอัด)
- UI: plain HTML/CSS/JS ใน `src/PrePack/wwwroot/` ฝังเป็น EmbeddedResource เสิร์ฟที่ `https://prepack.app/` — no build step, no CDN
- JS ↔ C#: `window.chrome.webview.hostObjects.bridge` → `Bridge/WebBridge.cs` (คืน JSON `{ok,data}` / `{ok:false,error}`)
- บันทึกลง **SQLite ในเครื่องก่อนเสมอ** (`Data/LocalDb.cs`, Microsoft.Data.Sqlite) แล้ว `Data/SyncService.cs` sync ขึ้น
  MySQL (ของ PrePack เอง, MySqlConnector) เบื้องหลัง — กฎ merge อยู่ใน `Domain/SyncRules.cs`
- INVS (SQL Server, อ่านอย่างเดียว) ผ่าน Microsoft.Data.SqlClient
- UI สไตล์ Gmail (Material 3 `gm3-sys-color` tokens อยู่ต้น `wwwroot/css/style.css`) — ช่องค้นหายา = search pill,
  ปุ่มพิมพ์ = ปุ่ม Compose, เมนู ⚙ = แถบซ้ายแบบ Gmail settings
- ฟอนต์ UI ฝังใน exe: Roboto (latin) + Noto Sans Thai (thai) แบบ variable woff2 ใน `wwwroot/fonts/` (OFL 1.1 — ต้องเก็บ OFL-*.txt ไว้)
- ดูตัวอย่างโดยไม่ build: เปิด `wwwroot/index.html#demo` / `#open=report` (mock bridge) และ `wwwroot/dev/label-preview.html`
- Config เครื่อง: `%APPDATA%\PrePack\config.json` (seed ฝังใน exe: `seed-config.json`), รหัสผ่านเข้ารหัส DPAPI

## Commands
```bash
# เครื่องที่ยังไม่มี .NET 10 SDK ให้เติม -p:PrePackTfm=net9.0-windows ทุกคำสั่ง
dotnet test PrePack.sln
dotnet publish src/PrePack/PrePack.csproj -c Release -o publish      # -> publish/PrePack.exe
publish/PrePack.exe --print-test-pdf test.pdf                        # เช็กขนาดหน้าโดยไม่ต้องมีเครื่องพิมพ์
```
เปิด UI ในเบราว์เซอร์ธรรมดา (ไม่มี C#) ได้ — `index.html` จะโหลด `wwwroot/dev/mock-bridge.js` เอง (ไม่ถูกฝังใน exe)

## Rules
- **DB:** เขียนได้เฉพาะ DB ของ PrePack (`Data/Schema.cs` ปฏิเสธ DB ที่มีตาราง HOSxP/JHCIS) · INVS / HOSxP / JHCIS = SELECT เท่านั้น
- Schema: เพิ่มไฟล์ใหม่ `src/PrePack/Data/Migrations/NNN_name.sql` ห้ามแก้ไฟล์ที่ apply แล้ว; SQL ต้องใช้ได้กับ MySQL 5.7 / MariaDB 10.3 (ไม่มี JSON column, ไม่มี CTE)
- INVS: query ทั้งหมดอยู่ใน `Data/InvsClient.cs` ใช้ parameter เสมอ — หน้าเว็บส่ง SQL ไม่ได้ · ชื่อคอลัมน์ที่ต่อ string ต้องผ่านการเทียบกับ INFORMATION_SCHEMA
- **วันที่:** เครื่องห้องยาใช้ th-TH (ปฏิทินพุทธ) — parse/format วันที่ใน C# ต้องใส่ `CultureInfo.InvariantCulture` เสมอ
- รหัสผ่านห้ามส่งไปหน้าเว็บ (`SafeConfig()` ส่งแค่ `hasPassword`)
- แต้มภาระงาน = ดวง × factor (`Domain/WorkFactors.cs` ↔ `workFactorFor()` ใน app.js ต้องตรงกัน); แก้ factor ต้องผ่านรหัส ตรวจใน C# เสมอ
- print_logs เก็บ snapshot (ชื่อผู้บรรจุ, ชื่อยา, หน่วย) — แก้ master ภายหลังไม่กระทบประวัติ
- เจ้าหน้าที่อ้างอิงด้วย `uid` (GUID) ทั้ง local, MySQL และหน้าเว็บ — id ตัวเลขของ MySQL ไม่ส่งไปหน้าเว็บ
- การพิมพ์และการเปิดโปรแกรมห้ามรอ MySQL; งาน MySQL ทั้งหมดผ่าน SyncService (มี backoff)
- ห้ามลบแถวใน local DB หลัง sync (เป็น backup); schema local เปลี่ยนด้วย `PRAGMA user_version` ใน LocalDb.Migrate
- ขนาดหน้าจอใช้ WebView2 `ZoomFactor` (config `uiZoomPercent`) — ห้ามใช้ CSS `zoom` ที่ body เพราะจะกระทบการพิมพ์
- `PREPACK_DATA_DIR` เปลี่ยนโฟลเดอร์ข้อมูล (self-test ใช้ เพื่อไม่แตะข้อมูลจริง)
- Business rules (อายุยา, ประเภทยา, layout) อยู่ใน `Domain/` และมี unit test ใน `tests/PrePack.Tests`
- ขนาดฉลากเป็นมิลลิเมตรทั้งระบบ: `Domain/LabelLayout.cs` ↔ `wwwroot/js/labels.js` ต้องตรงกัน
- ข้อความจากผู้ใช้/ฐานข้อมูลต้องผ่าน `esc()` ก่อนใส่ HTML
- Bridge method: parameter เป็น string/int/bool เท่านั้น, error ที่ผู้ใช้เห็นใช้ `UserError` (ข้อความไทย)
