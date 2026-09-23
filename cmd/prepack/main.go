// Command prepack runs the PrePack web server.
package main

import (
	"context"
	"errors"
	"flag"
	"log"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"
	_ "time/tzdata" // Windows servers have no system tz database

	"github.com/cilforx/prepack/internal/api"
	"github.com/cilforx/prepack/internal/config"
	"github.com/cilforx/prepack/internal/db"
	"github.com/cilforx/prepack/internal/store"
	"github.com/cilforx/prepack/web"
	"github.com/gin-gonic/gin"
)

func main() {
	envFile := flag.String("env", ".env", "path to .env file")
	migrateOnly := flag.Bool("migrate", false, "apply database migrations and exit")
	flag.Parse()

	cfg, err := config.Load(*envFile)
	if err != nil {
		log.Fatal(err)
	}
	dsn, err := cfg.DSN()
	if err != nil {
		log.Fatal(err)
	}
	loc, _ := time.LoadLocation(cfg.TimeZone)

	ctx := context.Background()
	conn, err := db.Open(ctx, dsn)
	if err != nil {
		log.Fatal(err)
	}
	defer conn.Close()

	if err := db.Migrate(ctx, conn); err != nil {
		log.Fatalf("migrate: %v", err)
	}
	if *migrateOnly {
		log.Println("migrations applied")
		return
	}

	if os.Getenv("GIN_MODE") == "" {
		gin.SetMode(gin.ReleaseMode)
	}
	srv := &http.Server{
		Addr:              cfg.Addr,
		Handler:           api.NewRouter(store.New(conn), web.FS, loc),
		ReadHeaderTimeout: 10 * time.Second,
	}

	go func() {
		log.Printf("PrePack listening on %s", cfg.Addr)
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			log.Fatal(err)
		}
	}()

	stop := make(chan os.Signal, 1)
	signal.Notify(stop, os.Interrupt, syscall.SIGTERM)
	<-stop
	shutdownCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	_ = srv.Shutdown(shutdownCtx)
}
