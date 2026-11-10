# ตัวตรวจ: pwsh -File check.ps1 <โฟลเดอร์ที่มี expand.sql และ contract.sql>   (ต้องมี Docker)
# เรื่อง: แอป v1 ใช้คอลัมน์ name · แอป v2 ใช้ full_name · ระหว่าง deploy ทั้ง 2 รุ่นรันพร้อมกัน
#   ① v1 ยิงต่อเนื่อง → ② รัน expand.sql ระหว่าง v1 ยังยิง → ③ v2 เริ่มยิงด้วย (v1 ยังอยู่) → ④ v1 หยุด → ⑤ รัน contract.sql → ⑥ v2 ยิงต่อ
# ผ่าน = v1 ไม่ล้มเลยจนถึง ④ · v2 ไม่ล้มเลย · ข้อมูลที่ 2 รุ่นเขียน อีกรุ่นอ่านเห็นครบ · หลัง contract เหลือคอลัมน์ full_name อย่างเดียว
param([Parameter(Mandatory)][string]$Dir)
$ErrorActionPreference = 'Stop'
$Dir = Resolve-Path $Dir
$pg = "mig-drill-$([guid]::NewGuid().ToString('N').Substring(0,6))"
function Sql([string]$q) { docker exec $pg psql -U postgres -d postgres -tA -v ON_ERROR_STOP=1 -c $q }
function Count([string]$q) { $v = docker exec $pg psql -U postgres -d postgres -tA -c $q 2>$null; if ($LASTEXITCODE -ne 0) { -1 } else { [int]$v } }   # คอลัมน์หาย = -1 (ไม่ใช่ล้มทั้งตัวตรวจ)
function Loop([string]$name, [int]$n, [string]$q) {
  # วนใน container เอง (เร็วกว่าเรียก docker exec ทีละคำสั่ง) · ล้มแต่ละครั้งเขียน 1 บรรทัดลงไฟล์ .err
  $script = "for i in `$(seq $n); do psql -U postgres -d postgres -qtA -v ON_ERROR_STOP=1 -c `"$q`" >/dev/null 2>>/tmp/$name.log || echo x >> /tmp/$name.err; sleep 0.05; done; touch /tmp/$name.done"
  docker exec -d $pg sh -c $script | Out-Null
}
function Wait([string]$name) { for ($i = 0; $i -lt 600; $i++) { docker exec $pg test -f "/tmp/$name.done"; if ($LASTEXITCODE -eq 0) { return }; Start-Sleep -Milliseconds 200 } throw "$name ไม่จบ" }
function Errs([string]$name) { $e = docker exec $pg sh -c "cat /tmp/$name.err 2>/dev/null | wc -l"; [int]$e }
function Apply([string]$file) {
  docker cp (Join-Path $Dir $file) "${pg}:/tmp/$file" | Out-Null
  $out = docker exec $pg psql -U postgres -d postgres -q -v ON_ERROR_STOP=1 -f "/tmp/$file" 2>&1
  if ($LASTEXITCODE -ne 0) { throw "$file รันไม่ผ่าน: $out" }
}
docker run -d --name $pg -e POSTGRES_PASSWORD=drill postgres:17-alpine | Out-Null
try {
  for ($i = 0; $i -lt 60; $i++) { docker exec $pg pg_isready -U postgres -q; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep 1 }
  Start-Sleep 2   # entrypoint ของ image เปิด-ปิด server 1 รอบตอนเริ่มครั้งแรก
  Sql "CREATE TABLE customers(id serial PRIMARY KEY, name text NOT NULL); INSERT INTO customers(name) VALUES ('เดิม 1'),('เดิม 2'),('เดิม 3');" | Out-Null

  $v1 = "INSERT INTO customers(name) VALUES ('v1-'||clock_timestamp()); SELECT count(name) FROM customers;"
  $v2 = "INSERT INTO customers(full_name) VALUES ('v2-'||clock_timestamp()); SELECT count(full_name) FROM customers;"
  Loop v1 120 $v1                               # ①
  Start-Sleep 2; Apply 'expand.sql'             # ②
  Loop v2 60 $v2                                # ③
  Wait v1; Wait v2                              # ④
  $e1 = Errs v1; $e2a = Errs v2
  $total = [int](Sql "SELECT count(*) FROM customers")
  $nameOk = Count "SELECT count(*) FROM customers WHERE name IS NOT NULL"
  $fullOk = Count "SELECT count(*) FROM customers WHERE full_name IS NOT NULL"
  Apply 'contract.sql'                          # ⑤
  Loop v2b 20 $v2; Wait v2b                     # ⑥
  $e2b = Errs v2b
  $cols = (Sql "SELECT string_agg(column_name, ',' ORDER BY column_name) FROM information_schema.columns WHERE table_name='customers'")
  $firstErr = (docker exec $pg sh -c "head -n 1 /tmp/v1.log /tmp/v2.log /tmp/v2b.log 2>/dev/null | grep -v '^==' | grep -v '^$' | head -n 1")

  $ok = $true
  function Report($cond, $msg) { if ($cond) { Write-Host "✅ $msg" } else { Write-Host "❌ $msg"; $script:ok = $false } }
  Report ($e1 -eq 0)  "v1 ล้ม $e1 ครั้ง ระหว่าง migration (ต้อง 0)"
  Report ($e2a -eq 0) "v2 ล้ม $e2a ครั้ง ตอนรันพร้อม v1 (ต้อง 0)"
  Report ($nameOk -eq $total -and $fullOk -eq $total) "แถวทั้งหมด $total · มี name $nameOk · มี full_name $fullOk (2 รุ่นต้องเห็นข้อมูลกันครบ)"
  Report ($e2b -eq 0) "หลัง contract v2 ล้ม $e2b ครั้ง (ต้อง 0)"
  Report ($cols -eq 'full_name,id') "คอลัมน์สุดท้าย: $cols (ต้องเหลือ full_name,id)"
  if ($firstErr) { Write-Host "error แรกที่เจอ: $firstErr" }
  if ($ok) { Write-Host 'ผ่านทุกข้อ'; exit 0 } else { Write-Host 'ยังไม่ผ่าน'; exit 1 }
} finally { docker rm -f $pg | Out-Null }
