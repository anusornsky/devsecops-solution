// โจทย์บันได C# ขั้น ③ · worker ที่ AI เขียน: ทำงานครบเมื่อไม่มีอะไรพัง แต่ "ตายกลางงาน" แล้วงานค้างตลอดกาล
// ใช้: dotnet run -- <ไฟล์ db>          (ครั้งแรกสร้างงาน 20 ชิ้นให้เอง)
// งานของคุณ: แก้ไฟล์นี้ให้ pwsh -File ..\check.ps1 starter ผ่าน = ตายตรงไหนก็ได้ เปิดใหม่แล้วทุกงาน "เสร็จ 1 ครั้งพอดี"
// ห้ามย้าย/ลบบรรทัด CrashPoint() — มันคือ "ไฟดับ" ที่ตัวตรวจใช้จำลอง
using Microsoft.Data.Sqlite;

var db = new SqliteConnection($"Data Source={args[0]}");
db.Open();
Exec("CREATE TABLE IF NOT EXISTS jobs(id INTEGER PRIMARY KEY, status TEXT NOT NULL DEFAULT 'pending')");
Exec("CREATE TABLE IF NOT EXISTS results(job_id INTEGER NOT NULL, value INTEGER NOT NULL)");
if (Scalar("SELECT count(*) FROM jobs") == 0)
    for (var i = 1; i <= 20; i++) Exec($"INSERT INTO jobs(id) VALUES ({i})");

while (true)
{
    var id = Scalar("SELECT id FROM jobs WHERE status = 'pending' ORDER BY id LIMIT 1");
    if (id == 0) break;
    Exec($"UPDATE jobs SET status = 'processing' WHERE id = {id}");

    Exec($"INSERT INTO results(job_id, value) VALUES ({id}, {id * id})");   // "ทำงาน" = บันทึกผล
    CrashPoint(id);
    Exec($"UPDATE jobs SET status = 'done' WHERE id = {id}");
}
Console.WriteLine($"เสร็จ · done={Scalar("SELECT count(*) FROM jobs WHERE status='done'")}");

void Exec(string sql) { using var c = db.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
long Scalar(string sql) { using var c = db.CreateCommand(); c.CommandText = sql; return Convert.ToInt64(c.ExecuteScalar() ?? 0L); }

// จำลองไฟดับ: ถ้า CRASH_AFTER = เลขงานนี้ → ตายทันที ไม่มีโอกาส finally/Dispose (เหมือน kill -9)
static void CrashPoint(long id)
{
    if (Environment.GetEnvironmentVariable("CRASH_AFTER") == id.ToString())
        Environment.FailFast($"จำลองไฟดับหลังทำงาน {id}");
}
