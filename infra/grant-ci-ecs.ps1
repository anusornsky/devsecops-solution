# ให้ role ของ CI (gha-devsecops-deploy) สั่ง ECS ใช้ image ใหม่ได้ — เฉพาะ service stockapi ตัวเดียว
# ใช้: & .\infra\grant-ci-ecs.ps1     (ต้อง aws login --profile lab ก่อน)
$env:AWS_PROFILE = "lab"
$acct = aws sts get-caller-identity --query Account --output text
$policy = @"
{
  "Version": "2012-10-17",
  "Statement": [
    { "Effect": "Allow", "Action": "ecr:GetAuthorizationToken", "Resource": "*" },
    { "Effect": "Allow",
      "Action": ["ecr:BatchCheckLayerAvailability", "ecr:InitiateLayerUpload", "ecr:UploadLayerPart",
                 "ecr:CompleteLayerUpload", "ecr:PutImage", "ecr:BatchGetImage"],
      "Resource": "arn:aws:ecr:ap-southeast-1:$($acct):repository/stockapi-cs" },
    { "Effect": "Allow",
      "Action": ["ecs:UpdateService", "ecs:DescribeServices"],
      "Resource": "arn:aws:ecs:ap-southeast-1:$($acct):service/devsecops-lab/stockapi" }
  ]
}
"@
$file = New-TemporaryFile
Set-Content $file $policy -Encoding utf8NoBOM
aws iam put-role-policy --role-name gha-devsecops-deploy --policy-name ecr-push-stockapi-cs --policy-document "file://$file"
Remove-Item $file
if ($LASTEXITCODE -eq 0) { Write-Host "เพิ่มสิทธิ์ ECS ให้ role ของ CI แล้ว" -ForegroundColor Green }
