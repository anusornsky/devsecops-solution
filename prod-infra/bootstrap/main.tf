# bootstrap = ของที่ต้องมีก่อน Terraform ของทีมจะทำงานได้ (bucket เก็บ state + budget กันบิลบาน)
# state ของโฟลเดอร์นี้เก็บในเครื่อง (local) ตั้งใจ — bucket เก็บ state ยังไม่มีตอนรันครั้งแรก (ไก่กับไข่)
terraform {
  required_version = ">= 1.10" # use_lockfile ของ S3 backend ต้อง 1.10 ขึ้นไป
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 6.0" }
  }
}

provider "aws" {
  region = "ap-southeast-1"
  default_tags { tags = { project = "devsecops-prod", managed_by = "terraform" } }
}

variable "alert_email" {
  description = "อีเมลที่รับแจ้งเตือนเมื่อค่าใช้จ่ายใกล้งบ · เว้นว่าง = ไม่สร้าง budget (แผน Free ตัดจากเครดิต ดูยอดด้วย aws freetier get-account-plan-state)"
  type        = string
  default     = ""
}

variable "monthly_budget_usd" {
  description = "งบต่อเดือน (USD) — เกิน 80% ของที่คาด หรือ 100% ของจริง = ส่งอีเมล"
  type        = number
  default     = 30
}

data "aws_caller_identity" "me" {}

resource "aws_s3_bucket" "state" {
  #checkov:skip=CKV_AWS_145:SSE-S3 พอสำหรับ state ของงานฝึก ไม่สร้าง KMS key (1 USD/เดือน/key)
  #checkov:skip=CKV_AWS_18:ไม่ต้องเก็บ access log ของ bucket state (ต้องมี bucket อีกใบ)
  #checkov:skip=CKV_AWS_144:ไม่ replicate ข้าม region ในงานฝึก
  #checkov:skip=CKV2_AWS_62:ไม่มีระบบไหนรอ event จาก bucket state
  # เลขบัญชีในชื่อ = ไม่ชนกับคนอื่นทั้งโลก และรู้ทันทีว่าเป็นของบัญชีไหน
  bucket = "devsecops-tfstate-${data.aws_caller_identity.me.account_id}"
  # force_destroy ปิดไว้ตั้งใจ: state หาย = Terraform ลืมว่าสร้างอะไรไว้ (ต้องลบ envs ก่อน แล้วค่อยล้าง bucket เอง)
}

resource "aws_s3_bucket_versioning" "state" {
  bucket = aws_s3_bucket.state.id
  versioning_configuration { status = "Enabled" } # state พังหรือโดนเขียนทับ → ย้อนไปเวอร์ชันก่อนได้
}

resource "aws_s3_bucket_server_side_encryption_configuration" "state" {
  bucket = aws_s3_bucket.state.id
  rule {
    apply_server_side_encryption_by_default { sse_algorithm = "AES256" }
  }
}

resource "aws_s3_bucket_public_access_block" "state" {
  bucket                  = aws_s3_bucket.state.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_lifecycle_configuration" "state" {
  bucket = aws_s3_bucket.state.id
  rule {
    id     = "expire-old-state-versions"
    status = "Enabled"
    filter {}
    noncurrent_version_expiration { noncurrent_days = 90 }
    abort_incomplete_multipart_upload { days_after_initiation = 7 }
  }
}

# บังคับ HTTPS: state มีค่าลับปน (เช่น ARN ของ secret) ห้ามวิ่งผ่าน HTTP
resource "aws_s3_bucket_policy" "state" {
  bucket = aws_s3_bucket.state.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid       = "DenyInsecureTransport"
      Effect    = "Deny"
      Principal = "*"
      Action    = "s3:*"
      Resource  = [aws_s3_bucket.state.arn, "${aws_s3_bucket.state.arn}/*"]
      Condition = { Bool = { "aws:SecureTransport" = "false" } }
    }]
  })
}

resource "aws_budgets_budget" "monthly" {
  count        = var.alert_email == "" ? 0 : 1
  name         = "devsecops-prod-monthly"
  budget_type  = "COST"
  limit_amount = tostring(var.monthly_budget_usd)
  limit_unit   = "USD"
  time_unit    = "MONTHLY"

  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 80
    threshold_type             = "PERCENTAGE"
    notification_type          = "FORECASTED" # คาดว่าจะเกิน = เตือนก่อนเกินจริง
    subscriber_email_addresses = [var.alert_email]
  }
  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 100
    threshold_type             = "PERCENTAGE"
    notification_type          = "ACTUAL"
    subscriber_email_addresses = [var.alert_email]
  }
}

output "state_bucket" {
  description = "ใส่ชื่อนี้ใน backend ของ envs/staging และ envs/prod"
  value       = aws_s3_bucket.state.bucket
}
