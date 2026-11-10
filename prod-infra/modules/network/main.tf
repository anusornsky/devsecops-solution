# network module: VPC 2 AZ · public subnet (ALB, NAT) · private subnet (แอป, DB) · security group 3 ชั้น
# เรียกซ้ำได้ทุก env — staging กับ prod ต่างกันแค่ cidr/name ที่ส่งเข้ามา

terraform {
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 6.0" }
  }
}

variable "name" {
  description = "ชื่อ env เช่น staging / prod — ใช้นำหน้าชื่อทุกชิ้น"
  type        = string
}

variable "cidr" {
  description = "ช่วง IP ของ VPC เช่น 10.10.0.0/16 (staging กับ prod ห้ามซ้อนกัน เผื่อวันหนึ่งต้องต่อถึงกัน)"
  type        = string
}

variable "app_port" {
  description = "port ที่แอปฟังใน container"
  type        = number
  default     = 8080
}

variable "azs" {
  description = "2 AZ = ศูนย์ข้อมูลหนึ่งดับ อีกที่ยังรับงาน · ระบุตรงๆ แทน data source ที่อาจได้ AZ ใหม่มาเองแล้ว subnet ย้าย"
  type        = list(string)
  default     = ["ap-southeast-1a", "ap-southeast-1b"]
}

locals {
  azs = var.azs
}

resource "aws_vpc" "this" {
  cidr_block           = var.cidr
  enable_dns_support   = true
  enable_dns_hostnames = true # RDS endpoint / service ของ AWS ต้องใช้ชื่อ DNS
  tags                 = { Name = "${var.name}-vpc" }
}

# ปิด security group เริ่มต้นของ VPC (เดิมให้ทุกอย่างใน SG เดียวกันคุยกันได้หมด) — ใครลืมใส่ SG จะเข้าไม่ได้แทนที่จะเปิดโล่ง
resource "aws_default_security_group" "lockdown" {
  vpc_id = aws_vpc.this.id
}

resource "aws_subnet" "public" {
  count             = 2
  vpc_id            = aws_vpc.this.id
  availability_zone = local.azs[count.index]
  cidr_block        = cidrsubnet(var.cidr, 8, count.index) # 10.x.0.0/24, 10.x.1.0/24
  # ไม่แจก public IP อัตโนมัติ — ของที่ต้องมี IP สาธารณะ (ALB, NAT) ขอเอง
  map_public_ip_on_launch = false
  tags                    = { Name = "${var.name}-public-${local.azs[count.index]}", tier = "public" }
}

resource "aws_subnet" "private" {
  count             = 2
  vpc_id            = aws_vpc.this.id
  availability_zone = local.azs[count.index]
  cidr_block        = cidrsubnet(var.cidr, 8, 10 + count.index) # 10.x.10.0/24, 10.x.11.0/24
  tags              = { Name = "${var.name}-private-${local.azs[count.index]}", tier = "private" }
}

resource "aws_internet_gateway" "this" {
  vpc_id = aws_vpc.this.id
  tags   = { Name = "${var.name}-igw" }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.this.id
  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.this.id
  }
  tags = { Name = "${var.name}-public" }
}

resource "aws_route_table_association" "public" {
  count          = 2
  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

# NAT = ทางออกเน็ตทางเดียวของ private subnet (ดึง image, เรียก Secrets Manager) แต่ข้างนอกเข้ามาไม่ได้
# shortcut: NAT ตัวเดียวใน AZ แรก (~0.06 USD/ชม.) ถ้า AZ นั้นดับ แอปใน AZ ที่สองออกเน็ตไม่ได้ · งานจริงที่ต้องทน AZ ดับ = NAT ต่อ AZ (จ่าย 2 เท่า)
resource "aws_eip" "nat" {
  domain = "vpc"
  tags   = { Name = "${var.name}-nat" }
}

resource "aws_nat_gateway" "this" {
  allocation_id = aws_eip.nat.id
  subnet_id     = aws_subnet.public[0].id
  tags          = { Name = "${var.name}-nat" }
  depends_on    = [aws_internet_gateway.this]
}

resource "aws_route_table" "private" {
  vpc_id = aws_vpc.this.id
  route {
    cidr_block     = "0.0.0.0/0"
    nat_gateway_id = aws_nat_gateway.this.id
  }
  tags = { Name = "${var.name}-private" }
}

resource "aws_route_table_association" "private" {
  count          = 2
  subnet_id      = aws_subnet.private[count.index].id
  route_table_id = aws_route_table.private.id
}

# Flow log = บันทึกว่าใครคุยกับใครใน VPC (ใช้ตอนสืบเหตุการณ์ในบท Game day)
resource "aws_cloudwatch_log_group" "flow" {
  #checkov:skip=CKV_AWS_158:ใช้กุญแจเริ่มต้นของ CloudWatch ไม่สร้าง KMS key
  #checkov:skip=CKV_AWS_338:งานฝึกเก็บ 7 วัน (งานจริงตามนโยบายบริษัท มัก 90-365 วัน)
  name              = "/vpc/${var.name}/flow"
  retention_in_days = 7
}

resource "aws_iam_role" "flow" {
  name = "${var.name}-vpc-flow-log"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "vpc-flow-logs.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy" "flow" {
  name = "write-flow-log"
  role = aws_iam_role.flow.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["logs:CreateLogStream", "logs:PutLogEvents", "logs:DescribeLogStreams"]
      Resource = "${aws_cloudwatch_log_group.flow.arn}:*"
    }]
  })
}

resource "aws_flow_log" "this" {
  vpc_id          = aws_vpc.this.id
  traffic_type    = "REJECT" # เก็บเฉพาะที่โดนปฏิเสธ = ถูก และเห็นคนที่พยายามเข้าที่ไม่ควรเข้า
  iam_role_arn    = aws_iam_role.flow.arn
  log_destination = aws_cloudwatch_log_group.flow.arn
}

# ---------- security group 3 ชั้น: CloudFront → ALB → แอป → DB ----------
# แต่ละชั้นรับจาก "SG ของชั้นก่อนหน้า" เท่านั้น ไม่ใช่จาก IP — แอปย้ายเครื่อง/IP เปลี่ยน กฎยังถูก

data "aws_ec2_managed_prefix_list" "cloudfront" {
  name = "com.amazonaws.global.cloudfront.origin-facing" # รายการ IP ของ CloudFront ที่ AWS ดูแลให้
}

resource "aws_security_group" "alb" {
  name        = "${var.name}-alb"
  description = "ALB accepts HTTP only from CloudFront"
  vpc_id      = aws_vpc.this.id
  tags        = { Name = "${var.name}-alb" }
}

resource "aws_vpc_security_group_ingress_rule" "alb_from_cloudfront" {
  #checkov:skip=CKV_AWS_260:รับ 80 จาก CloudFront เท่านั้น (prefix list) ไม่ใช่ 0.0.0.0/0 · ไม่มีโดเมนจึงทำ HTTPS ที่ ALB ไม่ได้ ผู้ใช้ได้ HTTPS ที่ CloudFront
  security_group_id = aws_security_group.alb.id
  description       = "HTTP from CloudFront edge only"
  prefix_list_id    = data.aws_ec2_managed_prefix_list.cloudfront.id
  ip_protocol       = "tcp"
  from_port         = 80
  to_port           = 80
}

resource "aws_vpc_security_group_egress_rule" "alb_to_app" {
  security_group_id            = aws_security_group.alb.id
  description                  = "forward to app tasks"
  referenced_security_group_id = aws_security_group.app.id
  ip_protocol                  = "tcp"
  from_port                    = var.app_port
  to_port                      = var.app_port
}

resource "aws_security_group" "app" {
  name        = "${var.name}-app"
  description = "app tasks accept traffic only from the ALB"
  vpc_id      = aws_vpc.this.id
  tags        = { Name = "${var.name}-app" }
}

resource "aws_vpc_security_group_ingress_rule" "app_from_alb" {
  security_group_id            = aws_security_group.app.id
  description                  = "from ALB"
  referenced_security_group_id = aws_security_group.alb.id
  ip_protocol                  = "tcp"
  from_port                    = var.app_port
  to_port                      = var.app_port
}

resource "aws_vpc_security_group_egress_rule" "app_https" {
  security_group_id = aws_security_group.app.id
  description       = "HTTPS out via NAT: ECR, CloudWatch Logs, Secrets Manager"
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443
}

resource "aws_vpc_security_group_egress_rule" "app_to_db" {
  security_group_id            = aws_security_group.app.id
  description                  = "Postgres"
  referenced_security_group_id = aws_security_group.db.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
}

resource "aws_security_group" "db" {
  name        = "${var.name}-db"
  description = "Postgres accepts only from app tasks"
  vpc_id      = aws_vpc.this.id
  tags        = { Name = "${var.name}-db" }
}

resource "aws_vpc_security_group_ingress_rule" "db_from_app" {
  security_group_id            = aws_security_group.db.id
  description                  = "from app tasks"
  referenced_security_group_id = aws_security_group.app.id
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
}

output "vpc_id" { value = aws_vpc.this.id }
output "public_subnet_ids" { value = aws_subnet.public[*].id }
output "private_subnet_ids" { value = aws_subnet.private[*].id }
output "nat_public_ip" { value = aws_eip.nat.public_ip }
output "sg" {
  value = { alb = aws_security_group.alb.id, app = aws_security_group.app.id, db = aws_security_group.db.id }
}
