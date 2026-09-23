// Package config loads runtime settings from environment variables.
//
// ค่าเชื่อมต่อ MySQL อ่านจาก environment (หรือไฟล์ .env ในโฟลเดอร์ที่รันโปรแกรม)
// เพื่อให้เปลี่ยน server ได้โดยไม่ต้องแก้โค้ด
package config

import (
	"bufio"
	"fmt"
	"os"
	"strings"
	"time"

	"github.com/go-sql-driver/mysql"
)

type Config struct {
	Addr     string // PREPACK_ADDR  เช่น ":8080"
	DBHost   string // PREPACK_DB_HOST
	DBPort   string // PREPACK_DB_PORT
	DBUser   string // PREPACK_DB_USER
	DBPass   string // PREPACK_DB_PASS
	DBName   string // PREPACK_DB_NAME
	TimeZone string // PREPACK_TZ    เช่น "Asia/Bangkok"
}

// Load reads an optional .env file, then environment variables.
// Real environment variables win over .env values.
func Load(envFile string) (Config, error) {
	if err := loadDotEnv(envFile); err != nil {
		return Config{}, err
	}
	c := Config{
		Addr:     get("PREPACK_ADDR", ":8080"),
		DBHost:   get("PREPACK_DB_HOST", "127.0.0.1"),
		DBPort:   get("PREPACK_DB_PORT", "3306"),
		DBUser:   get("PREPACK_DB_USER", ""),
		DBPass:   get("PREPACK_DB_PASS", ""),
		DBName:   get("PREPACK_DB_NAME", "prepack"),
		TimeZone: get("PREPACK_TZ", "Asia/Bangkok"),
	}
	if c.DBUser == "" {
		return c, fmt.Errorf("ยังไม่ได้ตั้งค่า PREPACK_DB_USER (ดูตัวอย่างใน .env.example)")
	}
	return c, nil
}

// DSN builds a go-sql-driver/mysql connection string.
func (c Config) DSN() (string, error) {
	loc, err := time.LoadLocation(c.TimeZone)
	if err != nil {
		return "", fmt.Errorf("PREPACK_TZ ไม่ถูกต้อง: %w", err)
	}
	m := mysql.NewConfig()
	m.User = c.DBUser
	m.Passwd = c.DBPass
	m.Net = "tcp"
	m.Addr = c.DBHost + ":" + c.DBPort
	m.DBName = c.DBName
	m.ParseTime = true
	m.Loc = loc
	m.Params = map[string]string{"charset": "utf8mb4"}
	m.Collation = "utf8mb4_unicode_ci"
	return m.FormatDSN(), nil
}

func get(key, def string) string {
	if v, ok := os.LookupEnv(key); ok && v != "" {
		return v
	}
	return def
}

func loadDotEnv(path string) error {
	f, err := os.Open(path)
	if os.IsNotExist(err) {
		return nil
	}
	if err != nil {
		return err
	}
	defer f.Close()
	sc := bufio.NewScanner(f)
	for sc.Scan() {
		line := strings.TrimSpace(sc.Text())
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		k, v, ok := strings.Cut(line, "=")
		if !ok {
			continue
		}
		k = strings.TrimSpace(k)
		v = strings.Trim(strings.TrimSpace(v), `"'`)
		if _, exists := os.LookupEnv(k); !exists {
			os.Setenv(k, v)
		}
	}
	return sc.Err()
}
