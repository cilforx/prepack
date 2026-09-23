// Package domain holds PrePack business rules that do not depend on the database.
package domain

import (
	"errors"
	"fmt"
	"strings"
	"time"
)

const DateLayout = "2006-01-02"

type PackType string

const (
	PackPrepack  PackType = "prepack"  // แบ่งจากกระปุกเป็นซอง 30/60/100 เม็ด
	PackUnitDose PackType = "unitdose" // ซอง unit dose 1-5 เม็ด
	PackCream    PackType = "cream"    // แบ่งครีมจากกระปุกใหญ่
)

func (p PackType) Valid() bool {
	return p == PackPrepack || p == PackUnitDose || p == PackCream
}

const MaxPackCount = 2000

var (
	ErrSourceExpired = errors.New("ยาในภาชนะเดิมหมดอายุแล้ว ห้ามแบ่งบรรจุ")
	ErrBUDPastPack   = errors.New("วันหมดอายุหลังแบ่งบรรจุต้องอยู่หลังวันบรรจุ")
)

// ComputeBUD returns the beyond-use date for a repackaged item:
// pack date + budDays, but never later than the source container's expiry.
func ComputeBUD(packDate, srcExp time.Time, budDays int) (time.Time, error) {
	packDate = truncDay(packDate)
	srcExp = truncDay(srcExp)
	if !srcExp.After(packDate) {
		return time.Time{}, ErrSourceExpired
	}
	if budDays < 1 {
		return time.Time{}, ErrBUDPastPack
	}
	bud := packDate.AddDate(0, 0, budDays)
	if srcExp.Before(bud) {
		bud = srcExp
	}
	return bud, nil
}

// JobInput is what a packer submits for one packing round.
type JobInput struct {
	DrugID     int64  `json:"drug_id"`
	PackSizeID int64  `json:"pack_size_id"`
	PackCount  int    `json:"pack_count"`
	LotNo      string `json:"lot_no"`
	SrcExpDate string `json:"src_exp_date"`
	PackDate   string `json:"pack_date"`
	PackerID   int64  `json:"packer_id"`
	Note       string `json:"note"`
}

// Normalize trims text fields and upper-cases the lot number.
func (in *JobInput) Normalize() {
	in.LotNo = strings.ToUpper(strings.TrimSpace(in.LotNo))
	in.Note = strings.TrimSpace(in.Note)
	in.SrcExpDate = strings.TrimSpace(in.SrcExpDate)
	in.PackDate = strings.TrimSpace(in.PackDate)
}

// Validate checks required fields and parses the dates.
func (in JobInput) Validate() (packDate, srcExp time.Time, err error) {
	switch {
	case in.DrugID <= 0:
		return packDate, srcExp, errors.New("กรุณาเลือกยา")
	case in.PackSizeID <= 0:
		return packDate, srcExp, errors.New("กรุณาเลือกขนาดบรรจุ")
	case in.PackerID <= 0:
		return packDate, srcExp, errors.New("กรุณาเลือกผู้บรรจุ")
	case in.PackCount < 1 || in.PackCount > MaxPackCount:
		return packDate, srcExp, fmt.Errorf("จำนวนซองต้องอยู่ระหว่าง 1-%d", MaxPackCount)
	case in.LotNo == "":
		return packDate, srcExp, errors.New("กรุณากรอก Lot")
	case len(in.LotNo) > 40:
		return packDate, srcExp, errors.New("Lot ยาวเกิน 40 ตัวอักษร")
	case len(in.Note) > 255:
		return packDate, srcExp, errors.New("หมายเหตุยาวเกิน 255 ตัวอักษร")
	}
	if packDate, err = time.Parse(DateLayout, in.PackDate); err != nil {
		return packDate, srcExp, errors.New("วันบรรจุไม่ถูกต้อง (YYYY-MM-DD)")
	}
	if srcExp, err = time.Parse(DateLayout, in.SrcExpDate); err != nil {
		return packDate, srcExp, errors.New("วันหมดอายุของภาชนะเดิมไม่ถูกต้อง (YYYY-MM-DD)")
	}
	return packDate, srcExp, nil
}

func truncDay(t time.Time) time.Time {
	y, m, d := t.Date()
	return time.Date(y, m, d, 0, 0, 0, 0, time.UTC)
}
