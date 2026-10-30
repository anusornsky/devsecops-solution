# สร้างกล่อง (image) ของ Go API — เทียบ C#: csharp/Dockerfile
# build:  docker build -t stockapi-go go
# รัน:    docker run --rm -p 8090:8080 -v stockgo:/data stockapi-go
#
# multi-stage build = ใช้กล่องใหญ่ compile แล้วหยิบแค่ไฟล์ผลลัพธ์ใส่กล่องเล็ก กล่องใหญ่ทิ้งไป

# ── ช่วง 1: build (กล่องที่มี Go ครบ ~800 MB) ───────────────────────────
FROM golang:1.26 AS build
WORKDIR /src

# copy go.mod/go.sum ก่อนโค้ด → Docker จำผล (cache) ขั้น download ไว้
# แก้โค้ดรอบหน้า ขั้นนี้ไม่ต้องโหลด library ใหม่ (build เร็วขึ้นมาก) · ถ้า copy ทุกอย่างทีเดียว แก้บรรทัดเดียวก็โหลดใหม่หมด
COPY go.mod go.sum ./
RUN go mod download

COPY . .
# CGO_ENABLED=0 = ไม่พึ่ง library C ของเครื่อง → ได้ไฟล์เดียวรันได้เลยในกล่องว่าง (ทำได้เพราะ modernc sqlite เป็น pure Go)
# -ldflags="-s -w" = ตัดข้อมูล debug ออก ไฟล์เล็กลง ~30%
RUN CGO_ENABLED=0 go build -ldflags="-s -w" -o /out/stockapi ./cmd/stockapi \
 && mkdir -p /out/data

# ── ช่วง 2: runtime (กล่องเล็ก ไม่มี shell ไม่มี Go) ─────────────────────
FROM gcr.io/distroless/static-debian12:nonroot
COPY --from=build /out/stockapi /stockapi
# กล่องนี้ไม่มีคำสั่ง mkdir → สร้างโฟลเดอร์ไว้ในช่วง 1 แล้ว copy มา พร้อมให้ user nonroot เขียนได้
COPY --from=build --chown=nonroot:nonroot /out/data /data

# ใน container ต้องฟัง :8080 (ทุก interface) ไม่ใช่ 127.0.0.1 — ไม่งั้นข้างนอกกล่องยิงเข้ามาไม่ถึง
ENV ADDR=:8080 DB_PATH=/data/stock.db
EXPOSE 8080
USER nonroot
ENTRYPOINT ["/stockapi"]
