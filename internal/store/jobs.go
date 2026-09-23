package store

import (
	"context"
	"database/sql"
	"errors"
	"strings"
	"time"

	"github.com/cilforx/prepack/internal/domain"
)

type Job struct {
	ID          int64           `json:"id"`
	DrugID      int64           `json:"drug_id"`
	DrugName    string          `json:"drug_name"`
	Strength    string          `json:"strength"`
	Unit        string          `json:"unit"`
	PackSizeID  int64           `json:"pack_size_id"`
	PackType    domain.PackType `json:"pack_type"`
	QtyPerPack  float64         `json:"qty_per_pack"`
	PackCount   int             `json:"pack_count"`
	TotalQty    float64         `json:"total_qty"`
	WorkPoint   float64         `json:"work_point"`
	TotalPoints float64         `json:"total_points"`
	LotNo       string          `json:"lot_no"`
	SrcExpDate  string          `json:"src_exp_date"`
	PackDate    string          `json:"pack_date"`
	BUDDate     string          `json:"bud_date"`
	PackerID    int64           `json:"packer_id"`
	PackerName  string          `json:"packer_name"`
	PackerShort string          `json:"packer_short"`
	CheckerID   *int64          `json:"checker_id"`
	CheckerName *string         `json:"checker_name"`
	CheckedAt   *time.Time      `json:"checked_at"`
	Status      string          `json:"status"`
	Note        string          `json:"note"`
	CreatedAt   time.Time       `json:"created_at"`
}

type JobFilter struct {
	From     string // YYYY-MM-DD inclusive
	To       string // YYYY-MM-DD inclusive
	PackerID int64
	DrugID   int64
	Lot      string
	Status   string
	Limit    int
}

const jobSelect = `
SELECT j.id, j.drug_id, d.name, d.strength, d.unit, j.pack_size_id, j.pack_type,
       j.qty_per_pack, j.pack_count, j.work_point, j.lot_no, j.src_exp_date, j.pack_date,
       j.bud_date, j.packer_id, p.name, p.short_name, j.checker_id, c.name, j.checked_at,
       j.status, j.note, j.created_at
FROM pack_jobs j
JOIN drugs d ON d.id = j.drug_id
JOIN staff p ON p.id = j.packer_id
LEFT JOIN staff c ON c.id = j.checker_id`

func scanJob(sc interface{ Scan(...any) error }) (Job, error) {
	var j Job
	var srcExp, packDate, bud time.Time
	var checkerID sql.NullInt64
	var checkerName sql.NullString
	var checkedAt sql.NullTime
	err := sc.Scan(&j.ID, &j.DrugID, &j.DrugName, &j.Strength, &j.Unit, &j.PackSizeID, &j.PackType,
		&j.QtyPerPack, &j.PackCount, &j.WorkPoint, &j.LotNo, &srcExp, &packDate,
		&bud, &j.PackerID, &j.PackerName, &j.PackerShort, &checkerID, &checkerName, &checkedAt,
		&j.Status, &j.Note, &j.CreatedAt)
	if err != nil {
		return j, err
	}
	j.SrcExpDate, j.PackDate, j.BUDDate = fmtDate(srcExp), fmtDate(packDate), fmtDate(bud)
	j.TotalQty = j.QtyPerPack * float64(j.PackCount)
	j.TotalPoints = j.WorkPoint * float64(j.PackCount)
	if checkerID.Valid {
		j.CheckerID = &checkerID.Int64
	}
	j.CheckerName = strPtr(checkerName)
	if checkedAt.Valid {
		j.CheckedAt = &checkedAt.Time
	}
	return j, nil
}

func (st *Store) GetJob(ctx context.Context, id int64) (Job, error) {
	j, err := scanJob(st.DB.QueryRowContext(ctx, jobSelect+` WHERE j.id = ?`, id))
	if errors.Is(err, sql.ErrNoRows) {
		return j, ErrNotFound
	}
	return j, err
}

func (st *Store) ListJobs(ctx context.Context, f JobFilter) ([]Job, error) {
	var where []string
	var args []any
	if f.From != "" {
		where, args = append(where, "j.pack_date >= ?"), append(args, f.From)
	}
	if f.To != "" {
		where, args = append(where, "j.pack_date <= ?"), append(args, f.To)
	}
	if f.PackerID > 0 {
		where, args = append(where, "j.packer_id = ?"), append(args, f.PackerID)
	}
	if f.DrugID > 0 {
		where, args = append(where, "j.drug_id = ?"), append(args, f.DrugID)
	}
	if f.Lot != "" {
		where, args = append(where, "j.lot_no LIKE ?"), append(args, "%"+strings.ToUpper(f.Lot)+"%")
	}
	if f.Status != "" {
		where, args = append(where, "j.status = ?"), append(args, f.Status)
	}
	q := jobSelect
	if len(where) > 0 {
		q += " WHERE " + strings.Join(where, " AND ")
	}
	if f.Limit <= 0 || f.Limit > 1000 {
		f.Limit = 200
	}
	q += " ORDER BY j.pack_date DESC, j.id DESC LIMIT ?"
	args = append(args, f.Limit)

	rows, err := st.DB.QueryContext(ctx, q, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []Job{}
	for rows.Next() {
		j, err := scanJob(rows)
		if err != nil {
			return nil, err
		}
		out = append(out, j)
	}
	return out, rows.Err()
}

// CreateJob records one packing round. Quantity and work point are copied from
// the pack size so later master-data edits do not rewrite history.
func (st *Store) CreateJob(ctx context.Context, in domain.JobInput) (Job, error) {
	in.Normalize()
	packDate, srcExp, err := in.Validate()
	if err != nil {
		return Job{}, invalid(err.Error())
	}

	var (
		sizeDrugID int64
		packType   domain.PackType
		qty, point float64
		sizeActive bool
		budDays    int
		drugActive bool
	)
	err = st.DB.QueryRowContext(ctx, `
		SELECT s.drug_id, s.pack_type, s.qty, s.work_point, s.active, d.bud_days, d.active
		FROM pack_sizes s JOIN drugs d ON d.id = s.drug_id WHERE s.id = ?`, in.PackSizeID).
		Scan(&sizeDrugID, &packType, &qty, &point, &sizeActive, &budDays, &drugActive)
	if errors.Is(err, sql.ErrNoRows) || (err == nil && sizeDrugID != in.DrugID) {
		return Job{}, invalid("ขนาดบรรจุไม่ตรงกับยาที่เลือก")
	}
	if err != nil {
		return Job{}, err
	}
	if !sizeActive || !drugActive {
		return Job{}, invalid("ยาหรือขนาดบรรจุนี้ถูกปิดใช้งานแล้ว")
	}

	var packerActive bool
	err = st.DB.QueryRowContext(ctx, `SELECT active FROM staff WHERE id = ?`, in.PackerID).Scan(&packerActive)
	if errors.Is(err, sql.ErrNoRows) || (err == nil && !packerActive) {
		return Job{}, invalid("ไม่พบผู้บรรจุ หรือถูกปิดใช้งาน")
	}
	if err != nil {
		return Job{}, err
	}

	bud, err := domain.ComputeBUD(packDate, srcExp, budDays)
	if err != nil {
		return Job{}, invalid(err.Error())
	}

	res, err := st.DB.ExecContext(ctx, `
		INSERT INTO pack_jobs (drug_id, pack_size_id, pack_type, qty_per_pack, pack_count, work_point,
		                       lot_no, src_exp_date, pack_date, bud_date, packer_id, note)
		VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
		in.DrugID, in.PackSizeID, packType, qty, in.PackCount, point,
		in.LotNo, fmtDate(srcExp), fmtDate(packDate), fmtDate(bud), in.PackerID, in.Note)
	if err != nil {
		return Job{}, err
	}
	id, err := res.LastInsertId()
	if err != nil {
		return Job{}, err
	}
	return st.GetJob(ctx, id)
}

// CheckJob records the second-person check. The checker must differ from the packer.
func (st *Store) CheckJob(ctx context.Context, id, checkerID int64) (Job, error) {
	j, err := st.GetJob(ctx, id)
	if err != nil {
		return j, err
	}
	if j.Status != "packed" {
		return j, invalid("รายการนี้ตรวจแล้วหรือถูกยกเลิกแล้ว")
	}
	if checkerID == j.PackerID {
		return j, invalid("ผู้ตรวจต้องไม่ใช่คนเดียวกับผู้บรรจุ")
	}
	var active bool
	err = st.DB.QueryRowContext(ctx, `SELECT active FROM staff WHERE id = ?`, checkerID).Scan(&active)
	if errors.Is(err, sql.ErrNoRows) || (err == nil && !active) {
		return j, invalid("ไม่พบผู้ตรวจ หรือถูกปิดใช้งาน")
	}
	if err != nil {
		return j, err
	}
	res, err := st.DB.ExecContext(ctx,
		`UPDATE pack_jobs SET status = 'checked', checker_id = ?, checked_at = NOW() WHERE id = ? AND status = 'packed'`,
		checkerID, id)
	if err != nil {
		return j, err
	}
	if n, _ := res.RowsAffected(); n == 0 {
		return j, invalid("รายการนี้ถูกเปลี่ยนสถานะไปแล้ว")
	}
	return st.GetJob(ctx, id)
}

func (st *Store) CancelJob(ctx context.Context, id int64, reason string) (Job, error) {
	reason = strings.TrimSpace(reason)
	if reason == "" {
		return Job{}, invalid("กรุณาระบุเหตุผลที่ยกเลิก")
	}
	res, err := st.DB.ExecContext(ctx,
		`UPDATE pack_jobs SET status = 'cancelled', note = LEFT(CONCAT_WS(' | ', NULLIF(note, ''), CONCAT('ยกเลิก: ', ?)), 255)
		 WHERE id = ? AND status <> 'cancelled'`, reason, id)
	if err != nil {
		return Job{}, err
	}
	if n, _ := res.RowsAffected(); n == 0 {
		if _, err := st.GetJob(ctx, id); err != nil {
			return Job{}, err
		}
		return Job{}, invalid("รายการนี้ถูกยกเลิกไปแล้ว")
	}
	return st.GetJob(ctx, id)
}
