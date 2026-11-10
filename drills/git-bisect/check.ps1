# ตัวตรวจ: pwsh -File check.ps1 <โฟลเดอร์ repo> <hash ที่คุณหาได้>
# ผ่าน = hash นั้นคือ commit แรกที่ test แดง (commit ก่อนหน้าเขียว) · ตรวจด้วยการรัน test จริง ไม่ใช่เทียบเฉลย
param([Parameter(Mandatory)][string]$Dir, [Parameter(Mandatory)][string]$Hash)
$ErrorActionPreference = 'Stop'
Push-Location $Dir
try {
  if (git status --porcelain) { throw 'repo มีไฟล์ค้างแก้ — git bisect reset / git stash ก่อน' }
  $back = git rev-parse --abbrev-ref HEAD
  if ($back -eq 'HEAD') { throw 'ยังอยู่ระหว่าง bisect — รัน git bisect reset ก่อน' }
  $full = git rev-parse --verify "$Hash^{commit}"
  $red  = { param($c) git checkout -q $c; go test ./... *> $null; $LASTEXITCODE -ne 0 }
  $isRed = & $red $full; $parentGreen = -not (& $red "$full~1")
  git checkout -q $back
  if ($isRed -and $parentGreen) { Write-Host "✅ ผ่าน: $(git log -1 --format='%h %s' $full) คือ commit แรกที่ทำพัง" ; exit 0 }
  Write-Host "❌ ยังไม่ใช่: commit นี้ test $(if($isRed){'แดง'}else{'เขียว'}) · commit ก่อนหน้า $(if($parentGreen){'เขียว'}else{'แดง'})"; exit 1
} finally { Pop-Location }
