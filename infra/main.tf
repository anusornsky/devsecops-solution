terraform {
  required_providers {
    aws    = { source = "hashicorp/aws", version = "~> 6.0" }
    random = { source = "hashicorp/random", version = "~> 3.6" }
  }
}

provider "aws" {
  region = "ap-southeast-1" # ไม่มี access_key / secret_key ในไฟล์ — อ่านจาก AWS_PROFILE=lab
}

resource "random_id" "suffix" { byte_length = 4 }

resource "aws_s3_bucket" "lab" {
  #checkov:skip=CKV_AWS_145:งานฝึก ใช้ SSE-S3 (AES256) พอ ไม่สร้าง KMS key ที่เสียเงินรายเดือน
  #checkov:skip=CKV_AWS_18:bucket ฝึกไม่มีข้อมูลจริง ไม่ต้องเก็บ access log (ต้องมี bucket แยกอีกใบ)
  #checkov:skip=CKV_AWS_144:ไม่ต้อง replicate ข้าม region สำหรับงานฝึก (เสียค่า storage 2 เท่า)
  #checkov:skip=CKV2_AWS_61:destroy ทิ้งทุกครั้งที่จบ lab จึงไม่ต้องมี lifecycle rule
  #checkov:skip=CKV2_AWS_62:ไม่มีระบบไหนรอ event จาก bucket นี้
  bucket       = "devsecops-lab-${random_id.suffix.hex}" # ชื่อ bucket ห้ามซ้ำทั้งโลก
  force_destroy = true                                    # งานฝึก: destroy ได้แม้มีไฟล์อยู่
}

resource "aws_s3_bucket_public_access_block" "lab" {
  bucket                  = aws_s3_bucket.lab.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_versioning" "lab" {
  bucket = aws_s3_bucket.lab.id
  versioning_configuration { status = "Enabled" }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "lab" {
  bucket = aws_s3_bucket.lab.id
  rule {
    apply_server_side_encryption_by_default { sse_algorithm = "AES256" }
  }
}

resource "aws_ecr_repository" "lab" {
  #checkov:skip=CKV_AWS_136:ใช้การเข้ารหัสเริ่มต้นของ ECR (AES256) ไม่สร้าง KMS key
  name                = "devsecops-lab-tf" # คนละชื่อกับ stockapi-cs ที่สร้างมือใน Step 7
  image_tag_mutability = "IMMUTABLE"
  force_delete         = true
  image_scanning_configuration { scan_on_push = true }
}
