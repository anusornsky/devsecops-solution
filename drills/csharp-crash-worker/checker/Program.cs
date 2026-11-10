// ส่วนหนึ่งของตัวตรวจ (ห้ามแก้) · อ่านไฟล์ db แล้วพิมพ์: done แถวผล งานที่ผลซ้ำ งานที่ค้าง
using Microsoft.Data.Sqlite;
using var c = new SqliteConnection($"Data Source={args[0]};Mode=ReadOnly");
c.Open();
string Q(string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToString(cmd.ExecuteScalar()) ?? ""; }
Console.WriteLine(string.Join("|",
    Q("SELECT count(*) FROM jobs WHERE status='done'"),
    Q("SELECT count(*) FROM results"),
    Q("SELECT count(*) FROM (SELECT job_id FROM results GROUP BY job_id HAVING count(*) > 1)"),
    Q("SELECT ifnull(group_concat(id), '') FROM jobs WHERE status <> 'done'")));
