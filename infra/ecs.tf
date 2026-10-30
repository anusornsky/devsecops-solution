# รันแอปจริงบน AWS: ECS Fargate 1 task · public IP · ไม่มี load balancer (ประหยัด ~$9/เดือน)
# ปิดเมื่อไม่ใช้: terraform apply -var desired_count=0   (task หยุด = ไม่เสียค่า Fargate)
# ⚠️ SQLite อยู่ใน container — ข้อมูลหายทุกครั้งที่ deploy ใหม่ (งานฝึกเท่านั้น)

variable "desired_count" {
  description = "จำนวน task ที่รัน — 0 = ปิดแอป ไม่เสียเงิน"
  type        = number
  default     = 1
}

variable "allowed_cidr" {
  description = "IP ที่เปิดแอปได้ — ค่าเริ่มต้นทุกคน · ใส่ IP ตัวเอง/32 ถ้าอยากให้เห็นคนเดียว"
  type        = string
  default     = "0.0.0.0/0"
}

# ใช้ VPC เริ่มต้นของบัญชี — ไม่ต้องสร้าง network เอง
data "aws_vpc" "default" {
  default = true
}

data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }
}

# ECR ที่สร้างไว้แล้วใน Step 7 (deploy.yml push image เข้าที่นี่)
data "aws_ecr_repository" "app" {
  name = "stockapi-cs"
}

resource "aws_ecs_cluster" "app" {
  name = "devsecops-lab"
  setting {
    name  = "containerInsights"
    value = "enabled"
  }
}

resource "aws_cloudwatch_log_group" "app" {
  #checkov:skip=CKV_AWS_158:งานฝึก ใช้กุญแจเข้ารหัสเริ่มต้นของ CloudWatch พอ ไม่สร้าง KMS key (เสียเงินรายเดือน)
  #checkov:skip=CKV_AWS_338:งานฝึก เก็บ log 7 วันพอ ไม่ต้อง 1 ปี
  name              = "/ecs/stockapi"
  retention_in_days = 7
}

# role ที่ ECS ใช้ดึง image จาก ECR และเขียน log — ไม่ใช่สิทธิ์ของตัวแอป
resource "aws_iam_role" "execution" {
  name = "devsecops-lab-ecs-execution"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ecs-tasks.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "execution" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_security_group" "app" {
  #checkov:skip=CKV_AWS_260:เปิด 8080 ให้เรียกแอปได้โดยตรงเพราะไม่มี load balancer (ไม่ใช่ port 80)
  name        = "devsecops-lab-app"
  description = "allow HTTP 8080 to the stock API task"
  vpc_id      = data.aws_vpc.default.id

  ingress {
    description = "stock API"
    from_port   = 8080
    to_port     = 8080
    protocol    = "tcp"
    cidr_blocks = [var.allowed_cidr]
  }

  egress {
    description = "pull image from ECR + write logs"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_ecs_task_definition" "app" {
  family                   = "stockapi"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.execution.arn

  container_definitions = jsonencode([{
    name      = "stockapi"
    image     = "${data.aws_ecr_repository.app.repository_url}:latest"
    essential = true
    portMappings = [{
      containerPort = 8080
      protocol      = "tcp"
    }]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.app.name
        awslogs-region        = "ap-southeast-1"
        awslogs-stream-prefix = "app"
      }
    }
  }])
}

resource "aws_ecs_service" "app" {
  #checkov:skip=CKV_AWS_333:ไม่มี load balancer จึงต้องใช้ public IP เพื่อเปิดแอปจากข้างนอก
  name            = "stockapi"
  cluster         = aws_ecs_cluster.app.id
  task_definition = aws_ecs_task_definition.app.arn
  desired_count   = var.desired_count
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = data.aws_subnets.default.ids
    security_groups  = [aws_security_group.app.id]
    assign_public_ip = true
  }
}

output "find_url" {
  description = "คำสั่งหา URL ของแอป (IP เปลี่ยนทุกครั้งที่ task เริ่มใหม่)"
  value       = "aws ecs list-tasks --cluster devsecops-lab --service-name stockapi → describe-tasks → ENI → public IP · หรือใช้สคริปต์ infra/url.ps1"
}
