#!/usr/bin/env bash
# ตัวตรวจ: ./check.sh <สคริปต์ของคุณ> · รันใน Ubuntu (WSL) หรือ Git Bash · ห้ามแก้ไฟล์นี้
set -u
script=$(realpath "$1")
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/src/data" "$work/src/config" "$work/out"
echo hello > "$work/src/data/a.txt"
echo cfg > "$work/src/config/b.txt"
cd "$work/src"
fail=0
ok()  { echo "✅ $1"; }
bad() { echo "❌ $1"; fail=1; }

# รอบปกติ 4 ครั้ง (ห่าง 1 วิ ชื่อไฟล์จะได้ไม่ชน) → ต้องเหลือ 3 ไฟล์ · ไฟล์ล่าสุดมีครบ 2 ต้นทาง
for i in 1 2 3 4; do
  if ! bash "$script" "$work/out" data config > /dev/null 2>&1; then bad "รอบปกติที่ $i: exit ไม่เป็น 0"; fi
  sleep 1
done
n=$(ls "$work/out"/*.tgz 2>/dev/null | wc -l)
[ "$n" -eq 3 ] && ok "เก็บไว้ 3 ไฟล์ล่าสุด" || bad "เหลือ $n ไฟล์ · ต้องเหลือ 3"
newest=$(ls -t "$work/out"/*.tgz | head -n 1)
if tar tzf "$newest" | grep -q 'data/a.txt' && tar tzf "$newest" | grep -q 'config/b.txt'; then ok "ไฟล์ล่าสุดมีครบทั้ง 2 ต้นทาง"; else bad "ไฟล์ล่าสุดไม่ครบ"; fi

# รอบพัง: ต้นทางหาย
rm -rf config
before=$(ls "$work/out" | sort)
msg=$(bash "$script" "$work/out" data config 2>&1); code=$?
[ $code -ne 0 ] && ok "ต้นทางหาย → exit $code" || bad "ต้นทางหายแต่ exit 0 (คนดูแล cron จะคิดว่าสำเร็จ)"
echo "$msg" | grep -q 'backup ok' && bad "ล้มแต่ยังพิมพ์ 'backup ok'" || ok "ล้มแล้วไม่พิมพ์ว่าสำเร็จ"
after=$(ls "$work/out" | sort)
[ "$before" = "$after" ] && ok "ไม่มีไฟล์ใหม่ค้าง และของดี 3 ไฟล์ยังอยู่ครบ" || bad "ไฟล์ในปลายทางเปลี่ยนหลังรอบที่ล้ม: $(diff <(echo "$before") <(echo "$after") | tr '\n' ' ')"

[ $fail -eq 0 ] && { echo "ผ่านทุกข้อ"; exit 0; } || { echo "ยังไม่ผ่าน"; exit 1; }
