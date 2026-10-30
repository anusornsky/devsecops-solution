// stockapi = composition root: อ่าน config → ต่อชิ้นส่วนทุกชั้นเข้าด้วยกัน → เปิด HTTP server
// เทียบ C#: StockApi/Program.cs
package main

import (
	"context"
	"errors"
	"fmt"
	"io"
	"log/slog"
	"net"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/anusornsky/go-csharp-lab/go/internal/app"
	"github.com/anusornsky/go-csharp-lab/go/internal/httpapi"
	"github.com/anusornsky/go-csharp-lab/go/internal/store"
	"github.com/anusornsky/go-csharp-lab/go/internal/worker"
)

// main บางที่สุด: งานจริงอยู่ใน run เพื่อให้เทสต์ส่ง env / stdout ปลอมเข้าไปได้
func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	if err := run(ctx, os.Getenv, os.Stdout); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

// run ทำงานจน ctx ถูกยกเลิก (Ctrl+C / docker stop ส่ง SIGTERM) แล้วปิดอย่างนุ่มนวล
func run(ctx context.Context, getenv func(string) string, stdout io.Writer) error {
	log := slog.New(slog.NewJSONHandler(stdout, nil)) // log เป็น JSON ทีละบรรทัด = ระบบเก็บ log (CloudWatch ฯลฯ) อ่านต่อได้เลย

	dbPath := envOr(getenv, "DB_PATH", "stock.db")
	addr := envOr(getenv, "ADDR", "127.0.0.1:8082") // default ฟังแค่เครื่องตัวเอง (8080 มักชนกับ Docker/โปรแกรมอื่น) · C# ใช้ 8081

	db, err := store.Open(ctx, dbPath)
	if err != nil {
		return err
	}
	defer db.Close()

	// งานที่ค้าง processing จากรอบก่อน (โปรแกรมตายกลางงาน) → คืนเข้าคิวก่อนเริ่ม worker
	orders := app.NewOrderService(db, log)
	if n, err := db.RecoverStuck(ctx); err != nil {
		return fmt.Errorf("คืนงานค้าง: %w", err)
	} else if n > 0 {
		log.Warn("คืนงานค้างเข้าคิว", "orders", n)
	}

	// worker วิ่งใน goroutine แยก · ตอนปิด: สั่งหยุด → รอจนหยุดจริง → แล้ว defer db.Close() ข้างบนถึงทำงาน
	// defer ทำงานย้อนลำดับ (ประกาศทีหลัง ทำก่อน) → บรรทัดนี้รันก่อน db.Close เสมอ
	// ถ้าปิด DB ก่อน worker หยุด = worker ที่กำลังตัดสต็อกจะพังกลางทาง
	workerCtx, stopWorker := context.WithCancel(ctx)
	workerDone := make(chan struct{})
	go func() {
		worker.Run(workerCtx, orders, 500*time.Millisecond, log)
		close(workerDone) // close channel = ส่งสัญญาณ "จบแล้ว" ให้ทุกคนที่รออยู่
	}()
	defer func() {
		stopWorker()
		<-workerDone
	}()

	srv := &http.Server{
		Addr:    addr,
		Handler: httpapi.New(app.NewService(db, log), orders, db, log),
		// timeout กัน client ช้า/ค้างกิน connection ไปเรื่อยๆ (ค่า default ของ Go = ไม่มี timeout เลย)
		ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout:       10 * time.Second,
		WriteTimeout:      10 * time.Second,
		IdleTimeout:       60 * time.Second,
	}

	// จอง port ก่อน แล้วค่อย log — ถ้า log ก่อน (แบบ ListenAndServe) port ชนจะเห็น "listening" ทั้งที่โปรแกรมตายไปแล้ว
	ln, err := net.Listen("tcp", addr)
	if err != nil {
		return fmt.Errorf("เปิด port %s: %w", addr, err)
	}
	log.Info("listening", "addr", ln.Addr().String(), "db", dbPath)

	errCh := make(chan error, 1)
	go func() { errCh <- srv.Serve(ln) }() // go func(){...}() = รันฟังก์ชันนี้แยกไปอีกเส้น (goroutine) ไม่รอให้จบ

	// select = รอว่าช่องไหนมีของมาก่อน: server พังเอง หรือ ได้สัญญาณให้ปิด
	select {
	case err := <-errCh:
		return err
	case <-ctx.Done():
	}

	// graceful shutdown: หยุดรับคำขอใหม่ แต่รอคำขอที่ค้างอยู่ให้จบ ไม่เกิน 10 วินาที
	log.Info("shutting down")
	shutdownCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := srv.Shutdown(shutdownCtx); err != nil {
		return err
	}
	if err := <-errCh; !errors.Is(err, http.ErrServerClosed) {
		return err
	}
	log.Info("stopped")
	return nil
}

func envOr(getenv func(string) string, key, def string) string {
	if v := getenv(key); v != "" {
		return v
	}
	return def
}
