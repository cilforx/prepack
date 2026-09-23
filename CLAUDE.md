# PrePack — Drug Repackaging (Pre-pack / Unit dose) Tracker

อ่าน `devlog.md` ก่อนเริ่มงานทุกครั้ง และอัปเดตเมื่อจบงาน

> ⚠️ **อ่าน `PLAN.md` ก่อน** — ตกลงกันแล้วว่าจะเปลี่ยนไปใช้ C# .NET 8 + WebView2 (exe ไฟล์เดียว, silent print แบบ BoxBox)
> โค้ด Go + Gin ด้านล่างเป็นต้นแบบ v0.1 ที่จะถูกแทนที่ ยังไม่ได้เริ่ม implement แผนใหม่

## Stack
- Go 1.23 + Gin, MySQL/MariaDB via `go-sql-driver/mysql`
- UI: plain HTML/CSS/JS in `web/`, embedded with `go:embed` — no build step, no framework
- Config from env / `.env` (`internal/config`); never commit `.env`

## Commands
```bash
go vet ./... && go test ./...
go build -o prepack ./cmd/prepack
./prepack -env .env            # runs migrations then serves
```

## Rules
- Schema changes: add a new `internal/db/migrations/NNN_name.sql`; never edit an applied migration.
  Keep SQL compatible with MySQL 5.7 and MariaDB 10.3 (no JSON column type, no CTEs).
- Business rules (BUD calc, validation, label layout) live in `internal/domain` with unit tests.
- `pack_jobs` stores snapshots (`qty_per_pack`, `work_point`) so master-data edits never rewrite history.
- User-facing errors are Thai `store.ValidationError` → HTTP 400; other errors are logged and return a generic 500.
- Label dimensions are millimetres end-to-end (`LabelLayout` ↔ `web/common.js buildFrames`); keep both in sync.
- Escape all user data with `esc()` before inserting into HTML.
