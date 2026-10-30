// บทที่ 1 — พื้นฐาน Go เทียบ C#   รัน: go run ./01-basics/go   (จากโฟลเดอร์ lessons)
// เปิดคู่กับ ../cs/Program.cs — แต่ละหัวข้อเลขตรงกัน
package main // ทุกไฟล์ต้องบอกว่าอยู่ package ไหน · package main = โปรแกรมที่รันได้ (C#: โปรเจกต์ Exe)

import ( // ดึง package อื่นมาใช้ (C#: using)
	"errors"
	"fmt"
	"strings"
)

// ── 4. struct = กลุ่มข้อมูล (C#: record / class) ─────────────────────────
// ชื่อขึ้นต้นตัวใหญ่ = public · ตัวเล็ก = private (Go ไม่มีคำว่า public/private)
type Product struct {
	SKU  string
	Name string
	Qty  int
}

// method = ฟังก์ชันที่ผูกกับ type · (p Product) ตรงหน้าชื่อ = "receiver" (C#: this)
func (p Product) Label() string {
	return fmt.Sprintf("%s (%s) เหลือ %d", p.Name, p.SKU, p.Qty)
}

// ── 3. ฟังก์ชันคืน 2 ค่า: ผลลัพธ์ + error (C#: คืนค่าเดียว ผิดก็ throw) ──────
func divide(a, b int) (int, error) {
	if b == 0 {
		return 0, errors.New("หารด้วยศูนย์ไม่ได้")
	}
	return a / b, nil // nil = "ไม่มี error" (C#: null)
}

func main() {
	fmt.Println("── 1. ตัวแปร")
	var name string = "ปากกา" // แบบเต็ม: var ชื่อ ชนิด = ค่า (ชนิดอยู่หลังชื่อ · C# อยู่หน้า)
	qty := 10                 // := ประกาศ + เดาชนิดเอง (C#: var qty = 10;) ใช้ได้แค่ในฟังก์ชัน
	var empty int             // ไม่ใส่ค่า = ได้ "ค่าศูนย์" ของชนิดนั้น (int=0, string="", bool=false)
	fmt.Println(name, qty, empty)

	fmt.Println("── 2. if / for (Go มี loop แบบเดียวคือ for)")
	for i := 1; i <= 3; i++ { // ไม่มีวงเล็บรอบเงื่อนไข · ต้องมี { } เสมอ
		if i%2 == 0 {
			fmt.Println(i, "คู่")
		} else {
			fmt.Println(i, "คี่")
		}
	}

	fmt.Println("── 3. คืน 2 ค่า")
	result, err := divide(10, 2)
	fmt.Println(result, err)
	result, err = divide(10, 0) // = (ไม่มี :) เพราะประกาศไปแล้ว
	if err != nil {             // ← บรรทัดนี้จะเจอบ่อยที่สุดใน Go
		fmt.Println("พัง:", err)
	}

	fmt.Println("── 4. struct + method")
	p := Product{SKU: "PEN-001", Name: "ปากกา", Qty: 5}
	fmt.Println(p.Label())
	fmt.Printf("%+v\n", p) // %+v = พิมพ์ทุกช่องพร้อมชื่อ (ใช้ debug บ่อยมาก)

	fmt.Println("── 5. slice = list ที่ยาวเปลี่ยนได้ (C#: List<T>)")
	items := []string{"a", "b"}
	items = append(items, "c") // append คืน slice ใหม่ → ต้องเอามาเก็บทับเสมอ
	fmt.Println(items, len(items))
	for i, s := range items { // range = วนทีละตัว ได้ (ลำดับ, ค่า) · C#: foreach
		fmt.Println(i, s)
	}

	fmt.Println("── 6. map = key → value (C#: Dictionary)")
	stock := map[string]int{"PEN-001": 5}
	stock["CUP-002"] = 3
	n, ok := stock["NOPE"] // ไม่มี key = ได้ค่าศูนย์ + ok=false (ไม่ throw เหมือน C#)
	fmt.Println(stock, n, ok)

	fmt.Println("── 7. pointer: ส่ง \"ที่อยู่\" ให้ฟังก์ชันแก้ของตัวจริงได้")
	addStock(p, 100)   // ส่งสำเนา → ตัวจริงไม่เปลี่ยน
	fmt.Println(p.Qty) // ยังเป็น 5
	addStockPtr(&p, 100)
	fmt.Println(p.Qty) // 105
	fmt.Println(strings.Repeat("=", 20))
}

// รับ Product แบบสำเนา (Go ส่งสำเนาเสมอ ถ้าไม่ใช้ pointer) · C#: struct ทำแบบนี้ แต่ class ส่งตัวจริง
func addStock(p Product, n int) { p.Qty += n }

// *Product = pointer ไปที่ Product · &p = เอาที่อยู่ของ p
func addStockPtr(p *Product, n int) { p.Qty += n }
