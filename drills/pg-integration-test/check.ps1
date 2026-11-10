# ตัวตรวจ: pwsh -File check.ps1 <โฟลเดอร์ที่มี PostgresOrderTests.cs>   (ต้องมี Docker + .NET SDK)
# เอา test ของคุณไปรันกับแอป 2 รุ่น บน Postgres จริง:
#   รุ่นแก้แล้ว (โค้ดใน repo ตอนนี้)           → test ต้องเขียว
#   รุ่นมี bug (ถอด execution strategy ออก)   → test ต้องแดง   ← พิสูจน์ว่า test จับ bug ได้จริง
param([Parameter(Mandatory)][string]$Dir)
$ErrorActionPreference = 'Stop'
$test = Join-Path (Resolve-Path $Dir) 'PostgresOrderTests.cs'
$repo = (git -C $PSScriptRoot rev-parse --show-toplevel)
$work = Join-Path ([IO.Path]::GetTempPath()) "pgit-$([guid]::NewGuid().ToString('N').Substring(0,6))"
$pg = "pgit-" + [guid]::NewGuid().ToString('N').Substring(0, 6)
$port = Get-Random -Minimum 55000 -Maximum 56000
New-Item -ItemType Directory $work | Out-Null
try {
  # สำเนาโค้ดจาก commit ล่าสุด (ไม่ยุ่งกับ working tree ของคุณ)
  git -C $repo archive HEAD csharp | tar -x -C $work
  if ($LASTEXITCODE -ne 0) { throw 'คัดลอกโค้ดจาก git ไม่ได้' }
  New-Item -ItemType Directory "$work\csharp\StockApi.Tests\Integration" -Force | Out-Null
  Copy-Item $test "$work\csharp\StockApi.Tests\Integration\PostgresOrderTests.cs"

  docker run -d --name $pg -e POSTGRES_PASSWORD=drill -e POSTGRES_DB=shop -p "127.0.0.1:${port}:5432" postgres:17-alpine | Out-Null
  for ($i = 0; $i -lt 60; $i++) { docker exec $pg pg_isready -U postgres -q; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep 1 }
  Start-Sleep 2
  $env:PG_TEST = "Host=127.0.0.1;Port=$port;Username=postgres;Password=drill;Database=shop"

  function RunTests([string]$label) {
    $out = dotnet test "$work\csharp\StockApi.Tests" --filter "Category=Postgres" 2>&1
    $sum = ($out | Select-String -Pattern 'Passed!|Failed!|error CS' | Select-Object -First 3) -join ' / '
    Write-Host "  $label → $sum"
    if ($out -match 'error CS') { throw 'test ของคุณ build ไม่ผ่าน' }
    if ($out -match 'No test matches') { throw 'ไม่เจอ test ที่มี [Trait("Category", "Postgres")]' }
    return ($LASTEXITCODE -eq 0)
  }
  $greenOnFixed = RunTests 'รุ่นแก้แล้ว'

  $repoFile = "$work\csharp\StockApi\Infrastructure\EfOrderRepository.cs"
  $src = Get-Content $repoFile -Raw
  $buggy = $src.Replace('db.Database.CreateExecutionStrategy().ExecuteAsync(() => FulfilOnceAsync(id, ct));', 'FulfilOnceAsync(id, ct);')
  if ($buggy -eq $src) { throw 'ใส่ bug ไม่ได้ — โค้ดใน repo เปลี่ยนรูปไปแล้ว ตัวตรวจต้องอัปเดต' }
  Set-Content $repoFile $buggy -NoNewline
  $greenOnBuggy = RunTests 'รุ่นมี bug '

  if ($greenOnFixed -and -not $greenOnBuggy) { Write-Host '✅ ผ่าน: test เขียวกับโค้ดที่ถูก และแดงกับโค้ดที่มี bug'; exit 0 }
  if (-not $greenOnFixed) { Write-Host '❌ test แดงตั้งแต่โค้ดที่ถูกแล้ว — test ผิดเอง' }
  if ($greenOnBuggy) { Write-Host '❌ test เขียวทั้งที่โค้ดมี bug — ยังไม่ได้ตรวจสิ่งที่สำคัญ' }
  exit 1
} finally {
  Remove-Item Env:PG_TEST -ErrorAction SilentlyContinue
  docker rm -f $pg 2>$null | Out-Null
  Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
