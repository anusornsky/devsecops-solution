// เฉลย · 2 อย่างที่ทำให้ "ตายตรงไหนก็ได้" ปลอดภัย:
//   1. บันทึกผล + ตั้ง done อยู่ใน transaction เดียว → ตายก่อน commit = ไม่มีทั้งคู่ (ไม่มีผลค้างครึ่งๆ)
//   2. เปิดขึ้นมา: งานที่ค้าง processing คืนเป็น pending → ทำใหม่ได้ปลอดภัย เพราะข้อ 1 รับประกันว่ามันยังไม่มีผล
// ทางที่ไม่เลือก: ลบผลซ้ำทีหลัง (dedupe) — ต้องรู้ว่าแถวไหนคือตัวจริง และระหว่างนั้นคนอื่นเห็นยอดซ้ำ
// ข้อจำกัด: ใช้ได้เมื่อ "งาน" คือการเขียน DB เดียวกัน · ถ้างานเรียกระบบนอก (ส่งอีเมล ตัดบัตร) ต้องใช้ idempotency key แทน
using Microsoft.Data.Sqlite;

var db = new SqliteConnection($"Data Source={args[0]}");
db.Open();
Exec("CREATE TABLE IF NOT EXISTS jobs(id INTEGER PRIMARY KEY, status TEXT NOT NULL DEFAULT 'pending')");
Exec("CREATE TABLE IF NOT EXISTS results(job_id INTEGER NOT NULL, value INTEGER NOT NULL)");
if (Scalar("SELECT count(*) FROM jobs") == 0)
    for (var i = 1; i <= 20; i++) Exec($"INSERT INTO jobs(id) VALUES ({i})");

var back = Exec("UPDATE jobs SET status = 'pending' WHERE status = 'processing'");
if (back > 0) Console.WriteLine($"คืนงานค้าง {back} ชิ้นเข้าคิว");

while (true)
{
    var id = Scalar("SELECT id FROM jobs WHERE status = 'pending' ORDER BY id LIMIT 1");
    if (id == 0) break;
    // จองก่อนทำ: ถ้ามี worker หลายตัว ตัวที่แก้ได้ 1 แถวคือเจ้าของงาน
    if (Exec($"UPDATE jobs SET status = 'processing' WHERE id = {id} AND status = 'pending'") != 1) continue;

    using var tx = db.BeginTransaction();
    Exec($"INSERT INTO results(job_id, value) VALUES ({id}, {id * id})", tx);
    CrashPoint(id);
    Exec($"UPDATE jobs SET status = 'done' WHERE id = {id}", tx);
    tx.Commit();
}
Console.WriteLine($"เสร็จ · done={Scalar("SELECT count(*) FROM jobs WHERE status='done'")}");

int Exec(string sql, SqliteTransaction? tx = null) { using var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = sql; return c.ExecuteNonQuery(); }
long Scalar(string sql) { using var c = db.CreateCommand(); c.CommandText = sql; return Convert.ToInt64(c.ExecuteScalar() ?? 0L); }

static void CrashPoint(long id)
{
    if (Environment.GetEnvironmentVariable("CRASH_AFTER") == id.ToString())
        Environment.FailFast($"จำลองไฟดับหลังทำงาน {id}");
}
