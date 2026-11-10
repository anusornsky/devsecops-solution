# ตัวตรวจ: pwsh -File check.ps1 <โฟลเดอร์โปรเจกต์ เช่น starter>
# รอบ 1: ไฟดับหลังทำงานชิ้นที่ 7 (ก่อนบันทึกว่าเสร็จ) · รอบ 2: ไฟดับอีกครั้งที่ชิ้นที่ 13 · รอบ 3: เปิดปกติ
# ผ่าน = งาน 20 ชิ้น done ครบ และผลแต่ละงานมี 1 แถวพอดี (ไม่หาย ไม่ซ้ำ)
param([Parameter(Mandatory)][string]$Project)
$ErrorActionPreference = 'Stop'
$proj = Resolve-Path $Project
$db = Join-Path ([IO.Path]::GetTempPath()) "crash-drill-$([guid]::NewGuid().ToString('N').Substring(0,8)).db"
dotnet build $proj -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'build ไม่ผ่าน' }
function Run([string]$crash) {
  $env:CRASH_AFTER = $crash
  try { dotnet run --project $proj --no-build -- $db 2>&1 | Out-Null } finally { Remove-Item Env:CRASH_AFTER -ErrorAction SilentlyContinue }
}
Run '7'; Run '13'; Run ''
# อ่านผลด้วยโปรแกรมแยก (checker/) ไม่พึ่งโค้ดของผู้เรียน
$checker = Join-Path $PSScriptRoot 'checker'
$done, $rows, $dup, $stuck = (dotnet run --project $checker -v q -- $db) -split '\|'
if ($LASTEXITCODE -ne 0) { throw 'อ่านผลไม่ได้' }
Remove-Item $db -ErrorAction SilentlyContinue
Write-Host "done $done/20 · แถวผล $rows · งานที่ผลซ้ำ $dup · ค้าง [$stuck]"
if ($done -eq 20 -and $rows -eq 20 -and $dup -eq 0) { Write-Host '✅ ผ่าน: ไฟดับ 2 ครั้ง งานไม่หาย ไม่ซ้ำ'; exit 0 }
Write-Host '❌ ยังไม่ผ่าน'; exit 1
