# Step 5 — image ของเราดีแค่ไหน (ผลจริง 2026-10-30)

แอปเดียวกันทุกตัวอักษร ต่างกันแค่ Dockerfile

| | Dockerfile จริง (multi-stage + chiseled) | Dockerfile.unsafe (stage เดียว · SDK ทั้งก้อน) |
|---|---|---|
| ขนาด | ~231 MB | ~1.56 GB |
| ช่องโหว่ (Grype) | 7 แถว: Medium 6 · Negligible 1 | 410 แถว: Critical 20 · High 112 · Medium 105 · Low 13 |
| รันเป็น | UID 1654 (user ธรรมดา) | root (UID 0) |
| มี shell | ไม่มี (`docker exec ... id` ล้ม) | มี · เขียนไฟล์ลง `/etc` ได้ |

ฝั่ง Go: distroless ~21.5 MB เทียบ `golang:1.26` ทั้งก้อน ~1.89 GB

## ที่ได้เรียน

- CVE ส่วนใหญ่มาจาก **base image** ไม่ใช่โค้ดเรา → แก้ด้วยการเปลี่ยน base ไม่ใช่แก้โค้ด
- image ที่ไม่มี shell = คนร้ายที่เจาะเข้ามาไม่มีเครื่องมือใช้ · debug ต้องใช้ `docker debug` / ดู log แทน
- SBOM ของทุก run เก็บเป็น artifact ใน job `sbom` ของ `security.yml`
