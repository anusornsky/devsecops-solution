-- เฉลย · expand: เพิ่มของใหม่โดยไม่แตะของเก่า → v1 ไม่รู้สึกอะไรเลย
-- 1) เพิ่มคอลัมน์ใหม่แบบ nullable (ใส่ NOT NULL ตอนนี้ = v1 ที่ยัง insert แค่ name จะล้ม)
-- 2) trigger ก๊อปค่าข้ามกัน 2 ทาง: v1 เขียน name → เติม full_name · v2 เขียน full_name → เติม name (name ยัง NOT NULL อยู่)
-- 3) เติมของเก่าทีหลัง trigger: แถวที่เข้ามาระหว่างนี้ trigger ดูแลแล้ว ไม่มีช่องหลุด
-- งานจริงที่ตารางใหญ่: ข้อ 3 ทำเป็นก้อนเล็กๆ (UPDATE ... WHERE id BETWEEN) กัน lock ทั้งตารางนาน
BEGIN;
ALTER TABLE customers ADD COLUMN full_name text;

CREATE FUNCTION customers_sync_name() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF NEW.full_name IS NULL THEN NEW.full_name := NEW.name; END IF;
  IF NEW.name IS NULL THEN NEW.name := NEW.full_name; END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER customers_sync BEFORE INSERT OR UPDATE ON customers
  FOR EACH ROW EXECUTE FUNCTION customers_sync_name();
COMMIT;

UPDATE customers SET full_name = name WHERE full_name IS NULL;
