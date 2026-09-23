// Package api exposes the PrePack HTTP API and serves the web UI.
package api

import (
	"errors"
	"io/fs"
	"log"
	"net/http"
	"strconv"
	"time"

	"github.com/cilforx/prepack/internal/domain"
	"github.com/cilforx/prepack/internal/store"
	"github.com/gin-gonic/gin"
)

type Server struct {
	st  *store.Store
	loc *time.Location
}

// NewRouter wires API routes and the embedded static UI.
func NewRouter(st *store.Store, web fs.FS, loc *time.Location) *gin.Engine {
	s := &Server{st: st, loc: loc}
	r := gin.New()
	r.Use(gin.Logger(), gin.Recovery())

	a := r.Group("/api")
	a.GET("/health", s.health)

	a.GET("/staff", s.listStaff)
	a.POST("/staff", s.createStaff)
	a.PUT("/staff/:id", s.updateStaff)

	a.GET("/drugs", s.listDrugs)
	a.POST("/drugs", s.createDrug)
	a.PUT("/drugs/:id", s.updateDrug)
	a.POST("/drugs/:id/sizes", s.addPackSize)
	a.PUT("/sizes/:id", s.updatePackSize)

	a.GET("/jobs", s.listJobs)
	a.POST("/jobs", s.createJob)
	a.GET("/jobs/:id", s.getJob)
	a.POST("/jobs/:id/check", s.checkJob)
	a.POST("/jobs/:id/cancel", s.cancelJob)

	a.GET("/reports/workload", s.workload)

	a.GET("/settings/label", s.getLabelLayout)
	a.PUT("/settings/label", s.saveLabelLayout)

	r.NoRoute(gin.WrapH(http.FileServer(http.FS(web))))
	return r
}

func (s *Server) health(c *gin.Context) {
	if err := s.st.DB.PingContext(c.Request.Context()); err != nil {
		c.JSON(http.StatusServiceUnavailable, gin.H{"ok": false, "error": "database unavailable"})
		return
	}
	c.JSON(http.StatusOK, gin.H{"ok": true, "today": s.today()})
}

func (s *Server) today() string { return time.Now().In(s.loc).Format(domain.DateLayout) }

// fail maps store errors to HTTP responses; unexpected errors are logged, not leaked.
func fail(c *gin.Context, err error) {
	var ve store.ValidationError
	switch {
	case errors.As(err, &ve):
		c.JSON(http.StatusBadRequest, gin.H{"error": ve.Msg})
	case errors.Is(err, store.ErrNotFound):
		c.JSON(http.StatusNotFound, gin.H{"error": err.Error()})
	default:
		log.Printf("error: %s %s: %v", c.Request.Method, c.Request.URL.Path, err)
		c.JSON(http.StatusInternalServerError, gin.H{"error": "เกิดข้อผิดพลาดในระบบ"})
	}
}

func badRequest(c *gin.Context, msg string) {
	c.JSON(http.StatusBadRequest, gin.H{"error": msg})
}

func idParam(c *gin.Context) (int64, bool) {
	id, err := strconv.ParseInt(c.Param("id"), 10, 64)
	if err != nil || id <= 0 {
		badRequest(c, "id ไม่ถูกต้อง")
		return 0, false
	}
	return id, true
}

func queryInt(c *gin.Context, key string) int64 {
	v, _ := strconv.ParseInt(c.Query(key), 10, 64)
	return v
}

func validDate(s string) bool {
	_, err := time.Parse(domain.DateLayout, s)
	return err == nil
}
