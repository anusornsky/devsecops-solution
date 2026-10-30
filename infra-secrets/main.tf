terraform {
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 6.0" }
  }
}

provider "aws" {
  region = "ap-southeast-1"
}

variable "grant" {
  type    = bool
  default = false
}

resource "aws_secretsmanager_secret" "api_key" {
  name                    = "devsecops-lab/api-key"
  recovery_window_in_days = 0
}

resource "aws_ecs_cluster" "lab" {
  name = "sm-lab"
}

resource "aws_cloudwatch_log_group" "lab" {
  name              = "/ecs/sm-lab"
  retention_in_days = 1
}

resource "aws_iam_role" "execution" {
  name = "sm-lab-execution"
  assume_role_policy = jsonencode({
    Version   = "2012-10-17"
    Statement = [{ Effect = "Allow", Principal = { Service = "ecs-tasks.amazonaws.com" }, Action = "sts:AssumeRole" }]
  })
}

resource "aws_iam_role_policy_attachment" "execution" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role_policy" "read_secret" {
  count = var.grant ? 1 : 0
  name  = "read-api-key"
  role  = aws_iam_role.execution.id
  policy = jsonencode({
    Version   = "2012-10-17"
    Statement = [{ Effect = "Allow", Action = "secretsmanager:GetSecretValue", Resource = aws_secretsmanager_secret.api_key.arn }]
  })
}

resource "aws_ecs_task_definition" "lab" {
  family                   = "sm-lab"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.execution.arn
  container_definitions = jsonencode([{
    name      = "probe"
    image     = "public.ecr.aws/docker/library/busybox:latest"
    essential = true
    command   = ["sh", "-c", "echo API_KEY length=$${#API_KEY}"]
    secrets   = [{ name = "API_KEY", valueFrom = aws_secretsmanager_secret.api_key.arn }]
    logConfiguration = {
      logDriver = "awslogs"
      options   = { awslogs-group = aws_cloudwatch_log_group.lab.name, awslogs-region = "ap-southeast-1", awslogs-stream-prefix = "probe" }
    }
  }])
}

output "secret_arn" { value = aws_secretsmanager_secret.api_key.arn }
