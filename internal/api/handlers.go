package api

import (
	"net/http"
	"time"

	"github.com/cilforx/prepack/internal/domain"
	"github.com/cilforx/prepack/internal/store"
	"github.com/gin-gonic/gin"
)

// ── Staff ──

func (s *Server) listStaff(c *gin.Context) {
	out, err := s.st.ListStaff(c.Request.Context(), c.Query("all") != "1")
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusOK, out)
}

func (s *Server) createStaff(c *gin.Context) {
	var in store.Staff
	if err := c.ShouldBindJSON(&in); err != nil {
		badRequest(c, "ข้อมูลไม่ถูกต้อง")
		return
	}
	out, err := s.st.CreateStaff(c.Request.Context(), in)
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusCreated, out)
}

func (s *Server) updateStaff(c *gin.Context) {
	id, ok := idParam(c)
	if !ok {
		return
	}
	var in store.Staff
	if err := c.ShouldBindJSON(&in); err != nil {
		badRequest(c, "ข้อมูลไม่ถูกต้อง")
		return
	}
	in.ID = id
	out, err := s.st.UpdateStaff(c.Request.Context(), in)
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusOK, out)
}

// ── Drugs & pack sizes ──

func (s *Server) listDrugs(c *gin.Context) {
	out, err := s.st.ListDrugs(c.Request.Context(), c.Query("all") != "1")
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusOK, out)
}

func (s *Server) createDrug(c *gin.Context) {
	var in store.Drug
	if err := c.ShouldBindJSON(&in); err != nil {
		badRequest(c, "ข้อมูลไม่ถูกต้อง")
		return
	}
	out, err := s.st.CreateDrug(c.Request.Context(), in)
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusCreated, out)
}

func (s *Server) updateDrug(c *gin.Context) {
	id, ok := idParam(c)
	if !ok {
		return
	}
	var in store.Drug
	if err := c.ShouldBindJSON(&in); err != nil {
		badRequest(c, "ข้อมูลไม่ถูกต้อง")
		return
	}
	in.ID = id
	if err := s.st.UpdateDrug(c.Request.Context(), in); err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusOK, gin.H{"ok": true})
}

func (s *Server) addPackSize(c *gin.Context) {
	id, ok := idParam(c)
	if !ok {
		return
	}
	var in store.PackSize
	if err := c.ShouldBindJSON(&in); err != nil {
		badRequest(c, "ข้อมูลไม่ถูกต้อง")
		return
	}
	in.DrugID = id
	out, err := s.st.AddPackSize(c.Request.Context(), in)
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusCreated, out)
}

func (s *Server) updatePackSize(c *gin.Context) {
	id, ok := idParam(c)
	if !ok {
		return
	}
	var in store.PackSize
	if err := c.ShouldBindJSON(&in); err != nil {
		badRequest(c, "ข้อมูลไม่ถูกต้อง")
		return
	}
	in.ID = id
	if err := s.st.UpdatePackSize(c.Request.Context(), in); err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusOK, gin.H{"ok": true})
}

// ── Pack jobs ──

func (s *Server) listJobs(c *gin.Context) {
	f := store.JobFilter{
		From:     c.Query("from"),
		To:       c.Query("to"),
		PackerID: queryInt(c, "packer_id"),
		DrugID:   queryInt(c, "drug_id"),
		Lot:      c.Query("lot"),
		Status:   c.Query("status"),
		Limit:    int(queryInt(c, "limit")),
	}
	if (f.From != "" && !validDate(f.From)) || (f.To != "" && !validDate(f.To)) {
		badRequest(c, "รูปแบบวันที่ต้องเป็น YYYY-MM-DD")
		return
	}
	out, err := s.st.ListJobs(c.Request.Context(), f)
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusOK, out)
}

func (s *Server) createJob(c *gin.Context) {
	var in domain.JobInput
	if err := c.ShouldBindJSON(&in); err != nil {
		badRequest(c, "ข้อมูลไม่ถูกต้อง")
		return
	}
	if in.PackDate == "" {
		in.PackDate = s.today()
	}
	out, err := s.st.CreateJob(c.Request.Context(), in)
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusCreated, out)
}

func (s *Server) getJob(c *gin.Context) {
	id, ok := idParam(c)
	if !ok {
		return
	}
	out, err := s.st.GetJob(c.Request.Context(), id)
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusOK, out)
}

func (s *Server) checkJob(c *gin.Context) {
	id, ok := idParam(c)
	if !ok {
		return
	}
	var in struct {
		CheckerID int64 `json:"checker_id"`
	}
	if err := c.ShouldBindJSON(&in); err != nil || in.CheckerID <= 0 {
		badRequest(c, "กรุณาเลือกผู้ตรวจ")
		return
	}
	out, err := s.st.CheckJob(c.Request.Context(), id, in.CheckerID)
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusOK, out)
}

func (s *Server) cancelJob(c *gin.Context) {
	id, ok := idParam(c)
	if !ok {
		return
	}
	var in struct {
		Reason string `json:"reason"`
	}
	if err := c.ShouldBindJSON(&in); err != nil {
		badRequest(c, "ข้อมูลไม่ถูกต้อง")
		return
	}
	out, err := s.st.CancelJob(c.Request.Context(), id, in.Reason)
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusOK, out)
}

// ── Reports ──

func (s *Server) workload(c *gin.Context) {
	now := time.Now().In(s.loc)
	from := c.DefaultQuery("from", time.Date(now.Year(), now.Month(), 1, 0, 0, 0, 0, s.loc).Format(domain.DateLayout))
	to := c.DefaultQuery("to", now.Format(domain.DateLayout))
	if !validDate(from) || !validDate(to) {
		badRequest(c, "รูปแบบวันที่ต้องเป็น YYYY-MM-DD")
		return
	}
	rows, err := s.st.Workload(c.Request.Context(), from, to)
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusOK, gin.H{"from": from, "to": to, "rows": rows})
}

// ── Settings ──

func (s *Server) getLabelLayout(c *gin.Context) {
	l, err := s.st.GetLabelLayout(c.Request.Context())
	if err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusOK, l)
}

func (s *Server) saveLabelLayout(c *gin.Context) {
	var l domain.LabelLayout
	if err := c.ShouldBindJSON(&l); err != nil {
		badRequest(c, "ข้อมูลไม่ถูกต้อง")
		return
	}
	if err := s.st.SaveLabelLayout(c.Request.Context(), l); err != nil {
		fail(c, err)
		return
	}
	c.JSON(http.StatusOK, l)
}
