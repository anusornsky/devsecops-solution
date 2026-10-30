# หา URL ของแอปที่รันบน ECS (task ไม่มี load balancer → IP เปลี่ยนทุกครั้งที่ deploy)
# ใช้: & .\infra\url.ps1        (ต้อง aws login --profile lab ก่อน)
$env:AWS_PROFILE = if ($env:AWS_PROFILE) { $env:AWS_PROFILE } else { "lab" }
$task = aws ecs list-tasks --cluster devsecops-lab --service-name stockapi --desired-status RUNNING --query "taskArns[0]" --output text
if (-not $task -or $task -eq "None") { Write-Host "ยังไม่มี task ที่รันอยู่ (desired_count = 0 หรือกำลังเริ่ม รอ 1-2 นาทีแล้วลองใหม่)"; return }
$eni = aws ecs describe-tasks --cluster devsecops-lab --tasks $task --query "tasks[0].attachments[0].details[?name=='networkInterfaceId'].value" --output text
$ip = aws ec2 describe-network-interfaces --network-interface-ids $eni --query "NetworkInterfaces[0].Association.PublicIp" --output text
Write-Output "http://$($ip):8080/healthz"   # Write-Output = เอาไปใช้ต่อในสคริปต์ได้
