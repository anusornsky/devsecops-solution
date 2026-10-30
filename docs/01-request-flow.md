# Step 1 — request 1 ครั้งวิ่งผ่านอะไรบ้าง

ผลจริงจากเครื่อง (2026-10-30) · รันตามหลักสูตร Step 1

```
docker build -t stockapi-cs csharp
docker run -d --name cs -p 127.0.0.1:8091:8080 -v stockcs:/data stockapi-cs
```

## กล่อง port binding: เรียกแอปจากเครื่องเรา

```
curl.exe -v http://127.0.0.1:8091/healthz

> GET /healthz HTTP/1.1
< HTTP/1.1 200 OK
{"status":"ok"}
```

`>` คือสิ่งที่เราส่ง · `<` คือที่ server ตอบ · port 8091 ของเครื่องเราถูกส่งต่อเข้า port 8080 ใน container

## กล่อง process: รันเป็น user ไหน

```
docker top cs
UID     PID      ...  CMD
1654    292879   ...  dotnet StockApi.dll

docker exec cs id
OCI runtime exec failed: ... exec: "id": executable file not found in $PATH
```

UID 1654 = user ธรรมดา (บรรทัด `USER 1654` ใน Dockerfile) ไม่ใช่ root · `exec` ล้มเพราะ image แบบ chiseled ไม่มีแม้แต่คำสั่ง `id` หรือ shell — คนร้ายที่เจาะเข้ามาก็ไม่มีเครื่องมือใช้

## กล่อง ENV: มี secret ไหม

```
docker inspect cs --format "{{json .Config.Env}}"
["PATH=...","APP_UID=1654","ASPNETCORE_HTTP_PORTS=8080",...,"DB_PATH=/data/stock.db"]
```

มีแค่ค่าตั้งค่าทั่วไป ไม่มี password/key · `DB_PATH` ชี้ไปที่ volume `/data`

## error บอกข้อมูลภายในแค่ไหน

```
curl.exe -s http://127.0.0.1:8091/products/NOPE-1
{"error":"ไม่พบสินค้า"}
```

บอกแค่สิ่งที่ผู้ใช้ต้องรู้ ไม่มีชื่อตาราง ไม่มี path ไฟล์ — กันไว้แล้วใน `Api/ErrorHandling.cs`

## สรุปเส้นทาง

browser / curl → `127.0.0.1:8091` (เครื่องเรา) → port 8080 ใน container → process `dotnet StockApi.dll` (UID 1654) → ไฟล์ SQLite `/data/stock.db` ใน volume `stockcs`
