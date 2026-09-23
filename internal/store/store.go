// Package store implements MySQL persistence for PrePack.
package store

import (
	"database/sql"
	"errors"
	"time"

	"github.com/cilforx/prepack/internal/domain"
)

var ErrNotFound = errors.New("ไม่พบข้อมูล")

// ValidationError is a user-facing input error (HTTP 400).
type ValidationError struct{ Msg string }

func (e ValidationError) Error() string { return e.Msg }

func invalid(msg string) error { return ValidationError{Msg: msg} }

type Store struct {
	DB *sql.DB
}

func New(db *sql.DB) *Store { return &Store{DB: db} }

func fmtDate(t time.Time) string { return t.Format(domain.DateLayout) }

func nullStr(s *string) sql.NullString {
	if s == nil || *s == "" {
		return sql.NullString{}
	}
	return sql.NullString{String: *s, Valid: true}
}

func strPtr(ns sql.NullString) *string {
	if !ns.Valid {
		return nil
	}
	return &ns.String
}
