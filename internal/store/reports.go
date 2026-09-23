package store

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"

	"github.com/cilforx/prepack/internal/domain"
)

// WorkloadRow summarises one staff member's packing work in a date range.
type WorkloadRow struct {
	StaffID    int64   `json:"staff_id"`
	Name       string  `json:"name"`
	Jobs       int     `json:"jobs"`
	Packs      int     `json:"packs"`
	Prepack    int     `json:"prepack_packs"`
	UnitDose   int     `json:"unitdose_packs"`
	Cream      int     `json:"cream_packs"`
	Points     float64 `json:"points"`
	CheckedFor int     `json:"checked_for"` // จำนวนรายการที่เป็นผู้ตรวจให้คนอื่น
}

// Workload aggregates non-cancelled jobs between from and to (inclusive).
func (st *Store) Workload(ctx context.Context, from, to string) ([]WorkloadRow, error) {
	rows, err := st.DB.QueryContext(ctx, `
		SELECT s.id, s.name,
		       COALESCE(p.jobs, 0), COALESCE(p.packs, 0),
		       COALESCE(p.prepack, 0), COALESCE(p.unitdose, 0), COALESCE(p.cream, 0),
		       COALESCE(p.points, 0), COALESCE(c.checked, 0)
		FROM staff s
		LEFT JOIN (
		  SELECT packer_id,
		         COUNT(*) jobs, SUM(pack_count) packs,
		         SUM(IF(pack_type = 'prepack',  pack_count, 0)) prepack,
		         SUM(IF(pack_type = 'unitdose', pack_count, 0)) unitdose,
		         SUM(IF(pack_type = 'cream',    pack_count, 0)) cream,
		         SUM(pack_count * work_point) points
		  FROM pack_jobs
		  WHERE status <> 'cancelled' AND pack_date BETWEEN ? AND ?
		  GROUP BY packer_id
		) p ON p.packer_id = s.id
		LEFT JOIN (
		  SELECT checker_id, COUNT(*) checked
		  FROM pack_jobs
		  WHERE status = 'checked' AND pack_date BETWEEN ? AND ?
		  GROUP BY checker_id
		) c ON c.checker_id = s.id
		WHERE s.active = 1 OR p.jobs IS NOT NULL OR c.checked IS NOT NULL
		ORDER BY points DESC, s.name`, from, to, from, to)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []WorkloadRow{}
	for rows.Next() {
		var r WorkloadRow
		if err := rows.Scan(&r.StaffID, &r.Name, &r.Jobs, &r.Packs, &r.Prepack, &r.UnitDose,
			&r.Cream, &r.Points, &r.CheckedFor); err != nil {
			return nil, err
		}
		out = append(out, r)
	}
	return out, rows.Err()
}

const labelLayoutKey = "label_layout"

func (st *Store) GetLabelLayout(ctx context.Context) (domain.LabelLayout, error) {
	l := domain.DefaultLabelLayout()
	var raw string
	err := st.DB.QueryRowContext(ctx, `SELECT v FROM settings WHERE k = ?`, labelLayoutKey).Scan(&raw)
	if errors.Is(err, sql.ErrNoRows) {
		return l, nil
	}
	if err != nil {
		return l, err
	}
	// Unmarshal over defaults so fields added later keep a sensible value.
	if err := json.Unmarshal([]byte(raw), &l); err != nil {
		return domain.DefaultLabelLayout(), nil
	}
	return l, nil
}

func (st *Store) SaveLabelLayout(ctx context.Context, l domain.LabelLayout) error {
	if err := l.Validate(); err != nil {
		return invalid(err.Error())
	}
	raw, err := json.Marshal(l)
	if err != nil {
		return err
	}
	_, err = st.DB.ExecContext(ctx,
		`INSERT INTO settings (k, v) VALUES (?, ?) ON DUPLICATE KEY UPDATE v = VALUES(v)`,
		labelLayoutKey, string(raw))
	return err
}
