# สร้างกล่อง (image) ของ C# API — เทียบ Go: go/Dockerfile
# build:  docker build -t stockapi-cs csharp
# รัน:    docker run --rm -p 8091:8080 -v stockcs:/data stockapi-cs

# ── ช่วง 1: build (กล่องที่มี .NET SDK ~900 MB) ─────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# copy ไฟล์โปรเจกต์ก่อนโค้ด → cache ขั้น restore (โหลด NuGet) ไว้ · หลักเดียวกับ go.mod ฝั่ง Go
COPY global.json ./
COPY StockApi/StockApi.csproj StockApi/
RUN dotnet restore StockApi/StockApi.csproj

COPY StockApi/ StockApi/
# publish = build + รวบไฟล์ที่ต้องใช้ตอนรันไว้โฟลเดอร์เดียว · --no-restore เพราะ restore ไปแล้วข้างบน
RUN dotnet publish StockApi/StockApi.csproj -c Release -o /out/app --no-restore \
 && mkdir -p /out/data

# ── ช่วง 2: runtime (มีแค่ .NET runtime · ไม่มี shell · ไม่ใช่ root) ────
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled
WORKDIR /app
COPY --from=build /out/app .
# chiseled ไม่มี mkdir เหมือน distroless → สร้างไว้ช่วง 1 · 1654 = user "app" ที่ image ของ Microsoft เตรียมไว้
COPY --from=build --chown=1654:1654 /out/data /data

# port: image ของ Microsoft ตั้ง ASPNETCORE_HTTP_PORTS=8080 ไว้แล้ว — ห้ามตั้ง ASPNETCORE_URLS ซ้ำ (ได้ warning "Overriding HTTP_PORTS")
ENV DB_PATH=/data/stock.db
EXPOSE 8080
USER 1654
ENTRYPOINT ["dotnet", "StockApi.dll"]
