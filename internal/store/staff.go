package store

import (
	"context"
	"database/sql"
	"strings"
)

type Staff struct {
	ID        int64   `json:"id"`
	Code      *string `json:"code"`
	Name      string  `json:"name"`
	ShortName string  `json:"short_name"`
	Role      string  `json:"role"`
	Active    bool    `json:"active"`
}

func (s *Staff) normalize() error {
	s.Name = strings.TrimSpace(s.Name)
	s.ShortName = strings.TrimSpace(s.ShortName)
	if s.Code != nil {
		c := strings.TrimSpace(*s.Code)
		s.Code = &c
	}
	if s.ShortName == "" {
		s.ShortName = s.Name
	}
	if s.Role == "" {
		s.Role = "packer"
	}
	switch {
	case s.Name == "":
		return invalid("กรุณากรอกชื่อเจ้าหน้าที่")
	case len([]rune(s.ShortName)) > 30:
		return invalid("ชื่อย่อยาวเกิน 30 ตัวอักษร")
	case s.Role != "packer" && s.Role != "pharmacist":
		return invalid("ตำแหน่งไม่ถูกต้อง")
	}
	return nil
}

func (st *Store) ListStaff(ctx context.Context, activeOnly bool) ([]Staff, error) {
	q := `SELECT id, code, name, short_name, role, active FROM staff`
	if activeOnly {
		q += ` WHERE active = 1`
	}
	q += ` ORDER BY active DESC, name`
	rows, err := st.DB.QueryContext(ctx, q)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []Staff{}
	for rows.Next() {
		var s Staff
		var code sql.NullString
		if err := rows.Scan(&s.ID, &code, &s.Name, &s.ShortName, &s.Role, &s.Active); err != nil {
			return nil, err
		}
		s.Code = strPtr(code)
		out = append(out, s)
	}
	return out, rows.Err()
}

func (st *Store) CreateStaff(ctx context.Context, s Staff) (Staff, error) {
	if err := s.normalize(); err != nil {
		return s, err
	}
	s.Active = true
	res, err := st.DB.ExecContext(ctx,
		`INSERT INTO staff (code, name, short_name, role, active) VALUES (?, ?, ?, ?, 1)`,
		nullStr(s.Code), s.Name, s.ShortName, s.Role)
	if err != nil {
		return s, err
	}
	s.ID, err = res.LastInsertId()
	return s, err
}

func (st *Store) UpdateStaff(ctx context.Context, s Staff) (Staff, error) {
	if err := s.normalize(); err != nil {
		return s, err
	}
	res, err := st.DB.ExecContext(ctx,
		`UPDATE staff SET code = ?, name = ?, short_name = ?, role = ?, active = ? WHERE id = ?`,
		nullStr(s.Code), s.Name, s.ShortName, s.Role, s.Active, s.ID)
	if err != nil {
		return s, err
	}
	return s, st.requireRow(ctx, res, "staff", s.ID)
}
