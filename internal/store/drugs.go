package store

import (
	"context"
	"database/sql"
	"errors"
	"strings"

	"github.com/cilforx/prepack/internal/domain"
	"github.com/go-sql-driver/mysql"
)

type PackSize struct {
	ID        int64           `json:"id"`
	DrugID    int64           `json:"drug_id"`
	PackType  domain.PackType `json:"pack_type"`
	Qty       float64         `json:"qty"`
	WorkPoint float64         `json:"work_point"`
	Active    bool            `json:"active"`
}

type Drug struct {
	ID       int64      `json:"id"`
	HISCode  *string    `json:"his_code"`
	Name     string     `json:"name"`
	Strength string     `json:"strength"`
	Form     string     `json:"form"`
	Unit     string     `json:"unit"`
	BUDDays  int        `json:"bud_days"`
	Active   bool       `json:"active"`
	Sizes    []PackSize `json:"sizes"`
}

var validForms = map[string]bool{"tablet": true, "capsule": true, "cream": true, "other": true}

func (d *Drug) normalize() error {
	d.Name = strings.TrimSpace(d.Name)
	d.Strength = strings.TrimSpace(d.Strength)
	d.Unit = strings.TrimSpace(d.Unit)
	if d.HISCode != nil {
		c := strings.TrimSpace(*d.HISCode)
		d.HISCode = &c
	}
	if d.Form == "" {
		d.Form = "tablet"
	}
	if d.Unit == "" {
		if d.Form == "cream" {
			d.Unit = "g"
		} else {
			d.Unit = "เม็ด"
		}
	}
	switch {
	case d.Name == "":
		return invalid("กรุณากรอกชื่อยา")
	case !validForms[d.Form]:
		return invalid("รูปแบบยาไม่ถูกต้อง")
	case d.BUDDays < 1 || d.BUDDays > 3650:
		return invalid("อายุหลังแบ่งบรรจุต้องอยู่ระหว่าง 1-3650 วัน")
	}
	return nil
}

func (p *PackSize) validate() error {
	switch {
	case !p.PackType.Valid():
		return invalid("ประเภทการแบ่งบรรจุไม่ถูกต้อง")
	case p.Qty <= 0 || p.Qty > 100000:
		return invalid("จำนวนต่อซองต้องมากกว่า 0")
	case p.WorkPoint < 0 || p.WorkPoint > 1000:
		return invalid("แต้มภาระงานต้องอยู่ระหว่าง 0-1000")
	}
	return nil
}

func (st *Store) ListDrugs(ctx context.Context, activeOnly bool) ([]Drug, error) {
	q := `SELECT id, his_code, name, strength, form, unit, bud_days, active FROM drugs`
	if activeOnly {
		q += ` WHERE active = 1`
	}
	q += ` ORDER BY active DESC, name, strength`
	rows, err := st.DB.QueryContext(ctx, q)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	out := []Drug{}
	idx := map[int64]int{}
	for rows.Next() {
		var d Drug
		var code sql.NullString
		if err := rows.Scan(&d.ID, &code, &d.Name, &d.Strength, &d.Form, &d.Unit, &d.BUDDays, &d.Active); err != nil {
			return nil, err
		}
		d.HISCode = strPtr(code)
		d.Sizes = []PackSize{}
		idx[d.ID] = len(out)
		out = append(out, d)
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}

	sq := `SELECT id, drug_id, pack_type, qty, work_point, active FROM pack_sizes`
	if activeOnly {
		sq += ` WHERE active = 1`
	}
	sq += ` ORDER BY pack_type, qty`
	srows, err := st.DB.QueryContext(ctx, sq)
	if err != nil {
		return nil, err
	}
	defer srows.Close()
	for srows.Next() {
		var p PackSize
		if err := srows.Scan(&p.ID, &p.DrugID, &p.PackType, &p.Qty, &p.WorkPoint, &p.Active); err != nil {
			return nil, err
		}
		if i, ok := idx[p.DrugID]; ok {
			out[i].Sizes = append(out[i].Sizes, p)
		}
	}
	return out, srows.Err()
}

func (st *Store) CreateDrug(ctx context.Context, d Drug) (Drug, error) {
	if err := d.normalize(); err != nil {
		return d, err
	}
	for i := range d.Sizes {
		if err := d.Sizes[i].validate(); err != nil {
			return d, err
		}
	}
	tx, err := st.DB.BeginTx(ctx, nil)
	if err != nil {
		return d, err
	}
	defer tx.Rollback()

	res, err := tx.ExecContext(ctx,
		`INSERT INTO drugs (his_code, name, strength, form, unit, bud_days, active) VALUES (?, ?, ?, ?, ?, ?, 1)`,
		nullStr(d.HISCode), d.Name, d.Strength, d.Form, d.Unit, d.BUDDays)
	if err != nil {
		return d, err
	}
	if d.ID, err = res.LastInsertId(); err != nil {
		return d, err
	}
	d.Active = true
	for i := range d.Sizes {
		p := &d.Sizes[i]
		p.DrugID = d.ID
		p.Active = true
		r, err := tx.ExecContext(ctx,
			`INSERT INTO pack_sizes (drug_id, pack_type, qty, work_point, active) VALUES (?, ?, ?, ?, 1)`,
			p.DrugID, p.PackType, p.Qty, p.WorkPoint)
		if err != nil {
			return d, mapDup(err)
		}
		if p.ID, err = r.LastInsertId(); err != nil {
			return d, err
		}
	}
	if d.Sizes == nil {
		d.Sizes = []PackSize{}
	}
	return d, tx.Commit()
}

// UpdateDrug updates drug fields only; pack sizes are managed separately.
func (st *Store) UpdateDrug(ctx context.Context, d Drug) error {
	if err := d.normalize(); err != nil {
		return err
	}
	res, err := st.DB.ExecContext(ctx,
		`UPDATE drugs SET his_code = ?, name = ?, strength = ?, form = ?, unit = ?, bud_days = ?, active = ? WHERE id = ?`,
		nullStr(d.HISCode), d.Name, d.Strength, d.Form, d.Unit, d.BUDDays, d.Active, d.ID)
	if err != nil {
		return err
	}
	return st.requireRow(ctx, res, "drugs", d.ID)
}

func (st *Store) AddPackSize(ctx context.Context, p PackSize) (PackSize, error) {
	if err := p.validate(); err != nil {
		return p, err
	}
	var exists int
	if err := st.DB.QueryRowContext(ctx, `SELECT COUNT(*) FROM drugs WHERE id = ?`, p.DrugID).Scan(&exists); err != nil {
		return p, err
	}
	if exists == 0 {
		return p, ErrNotFound
	}
	res, err := st.DB.ExecContext(ctx,
		`INSERT INTO pack_sizes (drug_id, pack_type, qty, work_point, active) VALUES (?, ?, ?, ?, 1)`,
		p.DrugID, p.PackType, p.Qty, p.WorkPoint)
	if err != nil {
		return p, mapDup(err)
	}
	p.Active = true
	p.ID, err = res.LastInsertId()
	return p, err
}

// UpdatePackSize changes work point / active flag. Qty and type are fixed once
// created because past jobs reference them; add a new size instead.
func (st *Store) UpdatePackSize(ctx context.Context, p PackSize) error {
	if p.WorkPoint < 0 || p.WorkPoint > 1000 {
		return invalid("แต้มภาระงานต้องอยู่ระหว่าง 0-1000")
	}
	res, err := st.DB.ExecContext(ctx,
		`UPDATE pack_sizes SET work_point = ?, active = ? WHERE id = ?`, p.WorkPoint, p.Active, p.ID)
	if err != nil {
		return err
	}
	return st.requireRow(ctx, res, "pack_sizes", p.ID)
}

// requireRow returns ErrNotFound when an UPDATE matched no row.
// MySQL reports 0 affected rows when values are unchanged, so re-check existence.
func (st *Store) requireRow(ctx context.Context, res sql.Result, table string, id int64) error {
	if n, _ := res.RowsAffected(); n > 0 {
		return nil
	}
	var exists int
	if err := st.DB.QueryRowContext(ctx, `SELECT COUNT(*) FROM `+table+` WHERE id = ?`, id).Scan(&exists); err != nil {
		return err
	}
	if exists == 0 {
		return ErrNotFound
	}
	return nil
}

func mapDup(err error) error {
	var me *mysql.MySQLError
	if errors.As(err, &me) && me.Number == 1062 {
		return invalid("มีขนาดบรรจุนี้อยู่แล้ว")
	}
	return err
}
