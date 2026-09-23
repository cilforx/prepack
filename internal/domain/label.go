package domain

import "errors"

// LabelLayout describes one printed frame of the sticker roll:
// a header strip that is torn off, then Rows x Cols small labels.
// All lengths are millimetres.
type LabelLayout struct {
	FrameWidth   float64 `json:"frame_width"`   // กว้างทั้งแผ่น (85)
	FrameHeight  float64 `json:"frame_height"`  // สูงทั้งแผ่น (50)
	HeaderHeight float64 `json:"header_height"` // ส่วนหัวที่ฉีกทิ้ง
	Rows         int     `json:"rows"`
	Cols         int     `json:"cols"`
	GapX         float64 `json:"gap_x"`    // ระยะห่างระหว่างดวงแนวนอน
	GapY         float64 `json:"gap_y"`    // ระยะห่างระหว่างดวงแนวตั้ง
	OffsetX      float64 `json:"offset_x"` // เลื่อนทั้งหน้า (ชดเชยเครื่องพิมพ์)
	OffsetY      float64 `json:"offset_y"`
	Padding      float64 `json:"padding"`   // ขอบในแต่ละดวง
	FontSize     float64 `json:"font_size"` // pt
	BuddhistYear bool    `json:"buddhist_year"`
	ShowPacker   bool    `json:"show_packer"`
}

func DefaultLabelLayout() LabelLayout {
	return LabelLayout{
		FrameWidth:   85,
		FrameHeight:  50,
		HeaderHeight: 8,
		Rows:         3,
		Cols:         3,
		GapX:         1,
		GapY:         1,
		Padding:      0.8,
		FontSize:     5.5,
		BuddhistYear: true,
		ShowPacker:   true,
	}
}

func (l LabelLayout) Validate() error {
	switch {
	case l.FrameWidth < 10 || l.FrameWidth > 300 || l.FrameHeight < 10 || l.FrameHeight > 300:
		return errors.New("ขนาดแผ่นต้องอยู่ระหว่าง 10-300 มม.")
	case l.Rows < 1 || l.Rows > 10 || l.Cols < 1 || l.Cols > 10:
		return errors.New("จำนวนแถว/คอลัมน์ต้องอยู่ระหว่าง 1-10")
	case l.HeaderHeight < 0 || l.HeaderHeight >= l.FrameHeight:
		return errors.New("ส่วนหัวต้องเล็กกว่าความสูงแผ่น")
	case l.GapX < 0 || l.GapY < 0 || l.Padding < 0:
		return errors.New("ระยะห่างต้องไม่ติดลบ")
	case l.FontSize < 3 || l.FontSize > 20:
		return errors.New("ขนาดตัวอักษรต้องอยู่ระหว่าง 3-20 pt")
	}
	if l.CellWidth() <= 0 || l.CellHeight() <= 0 {
		return errors.New("ระยะห่างมากเกินไป ดวงฉลากมีขนาดติดลบ")
	}
	return nil
}

// CellWidth is the width of one small label.
func (l LabelLayout) CellWidth() float64 {
	return (l.FrameWidth - l.GapX*float64(l.Cols-1)) / float64(l.Cols)
}

// CellHeight is the height of one small label (header excluded).
func (l LabelLayout) CellHeight() float64 {
	return (l.FrameHeight - l.HeaderHeight - l.GapY*float64(l.Rows-1)) / float64(l.Rows)
}
