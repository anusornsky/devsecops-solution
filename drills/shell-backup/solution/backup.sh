#!/usr/bin/env bash
# เฉลย · 3 หลัก: ล้มให้เร็ว (set -euo pipefail) · เขียนไฟล์ชั่วคราวก่อนแล้วค่อยเปลี่ยนชื่อ (mv ในโฟลเดอร์เดียวกัน = ทีเดียวจบ)
#              · ลบของเก่าหลังจากของใหม่สำเร็จแล้วเท่านั้น
set -euo pipefail
dest=$1
shift
[ $# -gt 0 ] || { echo "ใช้: $0 <ปลายทาง> <ต้นทาง...>" >&2; exit 2; }

final="$dest/backup-$(date +%Y%m%d-%H%M%S).tgz"
tmp=$(mktemp "$dest/.backup.XXXXXX")
trap 'rm -f "$tmp"' EXIT          # ทางไหนก็ตาม (ล้ม/สำเร็จ) ไฟล์ชั่วคราวต้องหาย · สำเร็จแล้ว mv ไปก่อน rm เลยไม่เจอไฟล์

tar czf "$tmp" -- "$@"            # -- กันชื่อไฟล์ที่ขึ้นต้นด้วย - ถูกอ่านเป็นตัวเลือก
mv "$tmp" "$final"

# เก็บ 3 ไฟล์ล่าสุด · ชื่อมีวันเวลาเรียงได้ → sort ชื่อแทน ls -t (ไม่ขึ้นกับเวลาแก้ไฟล์) · -r ของ xargs = ไม่มีอะไรลบก็ไม่รัน rm
find "$dest" -maxdepth 1 -name 'backup-*.tgz' -print | sort -r | tail -n +4 | xargs -r rm --
echo "backup ok: $final"
