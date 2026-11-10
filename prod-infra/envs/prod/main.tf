# env prod — ไฟล์นี้กับของ env อื่นต่างกันแค่ค่าใน locals (โครงเดียวกันมาจาก module)
terraform {
  required_version = ">= 1.10"
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 6.0" }
  }
  # bucket ไม่เขียนในไฟล์ (มีเลขบัญชี) → ส่งตอน init:
  #   terraform init -backend-config="bucket=$(terraform -chdir=../../bootstrap output -raw state_bucket)"
  backend "s3" {
    key          = "prod/terraform.tfstate" # คนละ key = คนละ state · พัง staging ไม่ลาม prod
    region       = "ap-southeast-1"
    encrypt      = true
    use_lockfile = true # สร้างไฟล์ .tflock ใน S3 กันสองคน apply พร้อมกัน (ไม่ต้องใช้ DynamoDB แล้วตั้งแต่ 1.10)
  }
}

provider "aws" {
  region = "ap-southeast-1"
  default_tags { tags = { project = "devsecops-prod", env = "prod", managed_by = "terraform" } }
}

locals {
  env  = "prod"
  cidr = "10.20.0.0/16"
}

module "network" {
  source = "../../modules/network"
  name   = local.env
  cidr   = local.cidr
}

output "vpc_id" { value = module.network.vpc_id }
output "private_subnet_ids" { value = module.network.private_subnet_ids }
output "nat_public_ip" { value = module.network.nat_public_ip }
