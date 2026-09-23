package domain

import (
	"testing"
	"time"
)

func d(s string) time.Time {
	t, err := time.Parse(DateLayout, s)
	if err != nil {
		panic(err)
	}
	return t
}

func TestComputeBUD(t *testing.T) {
	tests := []struct {
		name    string
		pack    string
		srcExp  string
		days    int
		want    string
		wantErr error
	}{
		{"pack date plus days", "2026-09-23", "2028-01-01", 180, "2027-03-22", nil},
		{"capped by source expiry", "2026-09-23", "2026-12-31", 180, "2026-12-31", nil},
		{"source already expired", "2026-09-23", "2026-09-23", 180, "", ErrSourceExpired},
		{"zero days rejected", "2026-09-23", "2028-01-01", 0, "", ErrBUDPastPack},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got, err := ComputeBUD(d(tt.pack), d(tt.srcExp), tt.days)
			if err != tt.wantErr {
				t.Fatalf("err = %v, want %v", err, tt.wantErr)
			}
			if tt.wantErr == nil && got.Format(DateLayout) != tt.want {
				t.Fatalf("got %s, want %s", got.Format(DateLayout), tt.want)
			}
		})
	}
}

func TestJobInputValidate(t *testing.T) {
	valid := JobInput{DrugID: 1, PackSizeID: 1, PackCount: 10, LotNo: " a123 ",
		SrcExpDate: "2028-01-01", PackDate: "2026-09-23", PackerID: 1}
	valid.Normalize()
	if valid.LotNo != "A123" {
		t.Fatalf("lot not normalized: %q", valid.LotNo)
	}
	if _, _, err := valid.Validate(); err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	bad := []func(*JobInput){
		func(j *JobInput) { j.DrugID = 0 },
		func(j *JobInput) { j.PackSizeID = 0 },
		func(j *JobInput) { j.PackerID = 0 },
		func(j *JobInput) { j.PackCount = 0 },
		func(j *JobInput) { j.PackCount = MaxPackCount + 1 },
		func(j *JobInput) { j.LotNo = "" },
		func(j *JobInput) { j.PackDate = "23/09/2026" },
		func(j *JobInput) { j.SrcExpDate = "" },
	}
	for i, mutate := range bad {
		j := valid
		mutate(&j)
		if _, _, err := j.Validate(); err == nil {
			t.Errorf("case %d: expected error", i)
		}
	}
}

func TestLabelLayout(t *testing.T) {
	l := DefaultLabelLayout()
	if err := l.Validate(); err != nil {
		t.Fatalf("default layout invalid: %v", err)
	}
	// 85 mm wide, 3 cols, 1 mm gaps -> (85-2)/3
	if w := l.CellWidth(); w < 27.66 || w > 27.67 {
		t.Fatalf("cell width = %v", w)
	}
	// 50 mm - 8 mm header - 2 mm gaps -> 40/3
	if h := l.CellHeight(); h < 13.33 || h > 13.34 {
		t.Fatalf("cell height = %v", h)
	}
	l.HeaderHeight = 50
	if l.Validate() == nil {
		t.Fatal("header as tall as frame should be rejected")
	}
}
