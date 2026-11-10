-- โจทย์บันได Data ขั้น ③ · migration ที่ AI เขียน: "เปลี่ยนชื่อคอลัมน์ name เป็น full_name"
-- ถูกต้องถ้าปิดระบบก่อน แต่ตอน deploy จริง v1 (ใช้ name) กับ v2 (ใช้ full_name) รันพร้อมกันอยู่ช่วงหนึ่ง
-- งานของคุณ: เขียน expand.sql (รันตอน v1 ยังอยู่) + contract.sql (รันหลัง v1 หายหมดแล้ว) ให้ check.ps1 ผ่าน
ALTER TABLE customers RENAME COLUMN name TO full_name;
