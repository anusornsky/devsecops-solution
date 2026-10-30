// Package httpapi = adapter ขาเข้า: แปลง HTTP request → เรียก app.Service → แปลงผล/ error เป็น JSON + status
// เทียบ C#: StockApi/Api/ProductEndpoints.cs (Minimal API)
package httpapi

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"strconv"

	"github.com/anusornsky/go-csharp-lab/go/internal/app"
	"github.com/anusornsky/go-csharp-lab/go/internal/domain"
)

// Pinger = อะไรก็ได้ที่เช็คว่ายังตอบได้ (ฐานข้อมูล) — ใช้ใน /healthz
type Pinger interface {
	Ping(ctx context.Context) error
}

type createReq struct {
	SKU   string `json:"sku"`
	Name  string `json:"name"`
	Qty   int    `json:"qty"`
	Price int64  `json:"price"` // สตางค์ · ไม่ส่งมา = 0
}

type moveReq struct {
	Qty int `json:"qty"`
}

type orderReq struct {
	Items []domain.OrderItem `json:"items"`
}

// New คืน handler ของทั้ง API — route แบบ "METHOD /path/{param}" ของ Go 1.22+ (≈ app.MapGet/MapPost ของ C#)
func New(svc *app.Service, orders *app.OrderService, db Pinger, log *slog.Logger) http.Handler {
	mux := http.NewServeMux()

	// 202 Accepted = "รับเรื่องแล้ว แต่ยังทำไม่เสร็จ" (ต่างจาก 201 Created ที่ทำเสร็จแล้ว) · ถามสถานะต่อที่ Location
	mux.HandleFunc("POST /orders", func(w http.ResponseWriter, r *http.Request) {
		var req orderReq
		if !decode(w, r, &req) {
			return
		}
		o, err := orders.Place(r.Context(), req.Items)
		if err == nil {
			w.Header().Set("Location", fmt.Sprintf("/orders/%d", o.ID))
		}
		respond(w, log, o, err, http.StatusAccepted)
	})

	mux.HandleFunc("GET /orders/{id}", func(w http.ResponseWriter, r *http.Request) {
		id, err := strconv.ParseInt(r.PathValue("id"), 10, 64) // ข้อความ → ตัวเลข · ไม่ใช่ตัวเลข = ไม่มี order นั้น
		if err != nil {
			respond(w, log, nil, domain.ErrOrderNotFound, 0)
			return
		}
		o, err := orders.Get(r.Context(), id)
		respond(w, log, o, err, http.StatusOK)
	})

	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) {
		if err := db.Ping(r.Context()); err != nil {
			writeJSON(w, http.StatusServiceUnavailable, map[string]string{"status": "db down"})
			return
		}
		writeJSON(w, http.StatusOK, map[string]string{"status": "ok"})
	})

	mux.HandleFunc("GET /products", func(w http.ResponseWriter, r *http.Request) {
		ps, err := svc.List(r.Context())
		respond(w, log, ps, err, http.StatusOK)
	})

	mux.HandleFunc("GET /products/{sku}", func(w http.ResponseWriter, r *http.Request) {
		p, err := svc.Get(r.Context(), r.PathValue("sku"))
		respond(w, log, p, err, http.StatusOK)
	})

	mux.HandleFunc("POST /products", func(w http.ResponseWriter, r *http.Request) {
		var req createReq
		if !decode(w, r, &req) {
			return
		}
		p, err := svc.Create(r.Context(), req.SKU, req.Name, req.Qty, req.Price)
		respond(w, log, p, err, http.StatusCreated)
	})

	mux.HandleFunc("POST /products/{sku}/receive", func(w http.ResponseWriter, r *http.Request) {
		var req moveReq
		if !decode(w, r, &req) {
			return
		}
		p, err := svc.Receive(r.Context(), r.PathValue("sku"), req.Qty)
		respond(w, log, p, err, http.StatusOK)
	})

	mux.HandleFunc("POST /products/{sku}/issue", func(w http.ResponseWriter, r *http.Request) {
		var req moveReq
		if !decode(w, r, &req) {
			return
		}
		p, err := svc.Issue(r.Context(), r.PathValue("sku"), req.Qty)
		respond(w, log, p, err, http.StatusOK)
	})

	return mux
}

// decode อ่าน JSON จาก body · พัง = ตอบ 400 แล้วคืน false ให้ handler หยุด
// จำกัดขนาด 1 MB กันคนส่ง body ใหญ่ผิดปกติ
func decode(w http.ResponseWriter, r *http.Request, dst any) bool {
	r.Body = http.MaxBytesReader(w, r.Body, 1<<20)
	dec := json.NewDecoder(r.Body)
	dec.DisallowUnknownFields() // ส่งช่องที่ไม่รู้จัก = 400 (จับพิมพ์ชื่อช่องผิดได้เร็ว)
	if err := dec.Decode(dst); err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": "JSON ไม่ถูกต้อง: " + err.Error()})
		return false
	}
	return true
}

// respond = จุดเดียวที่แปลง error ของ domain เป็น HTTP status (C#: exception filter / ProblemDetails)
// error ที่ไม่รู้จัก = 500 และไม่ส่งรายละเอียดออกไปให้ client (อยู่ใน log แทน)
func respond(w http.ResponseWriter, log *slog.Logger, v any, err error, okStatus int) {
	switch {
	case err == nil:
		writeJSON(w, okStatus, v)
	case errors.Is(err, domain.ErrInvalid):
		writeJSON(w, http.StatusBadRequest, map[string]string{"error": err.Error()})
	case errors.Is(err, domain.ErrNotFound), errors.Is(err, domain.ErrOrderNotFound):
		writeJSON(w, http.StatusNotFound, map[string]string{"error": err.Error()})
	case errors.Is(err, domain.ErrAlreadyExists), errors.Is(err, domain.ErrInsufficientStock):
		writeJSON(w, http.StatusConflict, map[string]string{"error": err.Error()})
	default:
		log.Error("unexpected", "err", err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": "internal error"})
	}
}

// writeJSON ตั้ง header ก่อนเขียน status แล้วเขียน body
func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}
