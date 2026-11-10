-- เฉลย · contract: v1 หายหมดแล้ว → เก็บกวาดของเก่า และล็อกของใหม่ให้แน่น
BEGIN;
DROP TRIGGER customers_sync ON customers;
DROP FUNCTION customers_sync_name();
ALTER TABLE customers ALTER COLUMN full_name SET NOT NULL;   -- ทุกแถวมีค่าแล้วจากขั้น expand
ALTER TABLE customers DROP COLUMN name;
COMMIT;
