# Step 7 — CI คุยกับ AWS แบบไม่มี key

## ภาพรวม

GitHub Actions ขอ token (OIDC) → AWS ตรวจ trust policy → ให้กุญแจชั่วคราว → push image ขึ้น ECR → สั่ง ECS ใช้ image ใหม่
ใน GitHub **ไม่มี AWS access key เก็บไว้เลย** มีแค่ Variable `AWS_ROLE_ARN`

## trust policy (เลขบัญชีและ ID ลบออกแล้ว)

```json
{
  "Effect": "Allow",
  "Principal": { "Federated": "arn:aws:iam::<ACCOUNT_ID>:oidc-provider/token.actions.githubusercontent.com" },
  "Action": "sts:AssumeRoleWithWebIdentity",
  "Condition": {
    "StringEquals": {
      "token.actions.githubusercontent.com:aud": "sts.amazonaws.com",
      "token.actions.githubusercontent.com:sub": "repo:anusornsky@<OWNER_ID>/devsecops-lab@<REPO_ID>:ref:refs/heads/main"
    }
  }
}
```

- `sub` จำกัดแค่ **repo นี้ + branch main** — branch อื่น / fork / PR assume role ไม่ได้
- repo ที่สร้างหลัง 15 ก.ค. 2026 ใช้รูปแบบ `owner@ID/repo@ID` (ชื่อเปลี่ยนแล้ว ID ไม่เปลี่ยน กันคนตั้งชื่อ repo ซ้ำมาสวมรอย)

## สิทธิ์ของ role (แคบที่สุดเท่าที่ใช้จริง)

- ECR: push ได้เฉพาะ repo `stockapi-cs`
- ECS: `UpdateService` + `DescribeServices` เฉพาะ service `devsecops-lab/stockapi`

## ผลจริง (2026-10-30)

```
arn:aws:sts::************:assumed-role/gha-devsecops-deploy/GitHubActions
```

image ขึ้น ECR ด้วย tag = commit SHA และ `latest` · ECS เปลี่ยนไปใช้ image ใหม่ทุกครั้งที่ push main

## บนเครื่องเราก็ไม่มี key ถาวร

`aws login --profile lab` (IAM user + MFA) → `aws configure list --profile lab` ช่อง TYPE = `login`
