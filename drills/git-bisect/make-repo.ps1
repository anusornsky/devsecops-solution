# โจทย์บันได Git ขั้น ③ · สร้าง repo ฝึก 30 commit ที่มี 1 commit ทำ test พัง (ข้อความ commit ไม่บอกว่าตัวไหน)
# ใช้: pwsh -File make-repo.ps1 <โฟลเดอร์ใหม่>
# งานของคุณ: หา commit ที่ทำพังด้วย git bisect (ห้ามไล่อ่านทีละ commit) แล้วรัน check.ps1
param([Parameter(Mandatory)][string]$Dir, [int]$Bad = 19)
$ErrorActionPreference = 'Stop'
if (Test-Path $Dir) { throw "มี $Dir อยู่แล้ว — ใช้ชื่อใหม่" }
New-Item -ItemType Directory $Dir | Out-Null
Push-Location $Dir
try {
  git init -q -b main
  git config user.name 'drill'; git config user.email 'drill@example.invalid'; git config core.autocrlf false   # ไม่งั้น Windows เตือน CRLF ทุก commit
  Set-Content go.mod "module shop`n`ngo 1.26" -Encoding utf8NoBOM
  # ส่วนลด 10% เมื่อซื้อครบ 1000 บาท (รวม 1000 พอดี)
  $code = { param($op, $n) @"
package shop

// Discount คืนส่วนลด (สตางค์) · ครบ 100000 สตางค์ (1000 บาท) ได้ 10%
// revision $n
func Discount(total int) int {
	if total $op 100000 {
		return total / 10
	}
	return 0
}
"@ }
  Set-Content shop_test.go @'
package shop

import "testing"

func TestDiscount(t *testing.T) {
	for _, c := range []struct{ total, want int }{{99999, 0}, {100000, 10000}, {250000, 25000}} {
		if got := Discount(c.total); got != c.want {
			t.Errorf("Discount(%d) = %d · ต้องได้ %d", c.total, got, c.want)
		}
	}
}
'@ -Encoding utf8NoBOM
  $msgs = 'จัดรูปแบบ','ปรับคำอธิบาย','เพิ่มบันทึก','ย้ายบรรทัด','แก้ชื่อตัวแปร','ปรับ comment'
  for ($i = 1; $i -le 30; $i++) {
    $op = if ($i -ge $Bad) { '>' } else { '>=' }   # commit ที่ $Bad เปลี่ยน >= เป็น > เงียบๆ (ปนกับการแก้อื่น)
    Set-Content shop.go (& $code $op $i) -Encoding utf8NoBOM
    git add -A; git commit -q -m "$($msgs[$i % $msgs.Count]) (step $i)"
  }
  Write-Host "สร้างแล้ว: $Dir · 30 commit · test ของ commit ล่าสุดแดง"
} finally { Pop-Location }
