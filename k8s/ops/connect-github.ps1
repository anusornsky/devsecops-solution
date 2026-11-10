# ต่อ cluster เข้ากับ GitHub (repo private) — รันเองครั้งเดียว · ไม่มีความลับในไฟล์นี้
#   1) ghcr-pull (ทุก namespace ของแอป) = ให้ k3s ดึง image จาก GHCR · ต้องเป็น classic token สิทธิ์ read:packages อย่างเดียว
#      (GHCR ยังไม่รับ fine-grained token)
#   2) repo secret ใน argocd = ให้ Argo CD อ่าน repo · fine-grained token: repo นี้ repo เดียว · Contents: Read-only
# token ถูกถามแบบซ่อนตัวอักษร → ไม่ขึ้นจอ ไม่ลง history · ส่งเข้า kubectl ทาง stdin (ไม่เป็น argument ที่ process อื่นเห็น)
# ใช้: pwsh -File k8s/ops/connect-github.ps1
$ErrorActionPreference = 'Stop'
$env:KUBECONFIG = "$HOME\kubeconfig-prod"
$owner = 'anusornsky'
$repoUrl = "https://github.com/$owner/devsecops-solution.git"

function Read-Token([string]$prompt) {
  $s = Read-Host $prompt -AsSecureString
  $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
  try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}
function B64([string]$s) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s)) }

$pull = Read-Token 'classic token (read:packages)'
$repo = Read-Token 'fine-grained token (devsecops-solution · Contents: Read-only)'
if (-not $pull -or -not $repo) { throw 'ต้องใส่ทั้ง 2 token' }

# .dockerconfigjson = รูปแบบที่ kubelet ใช้ login registry
$auth = B64 "${owner}:$pull"
$dockercfg = B64 "{`"auths`":{`"ghcr.io`":{`"auth`":`"$auth`"}}}"
foreach ($ns in 'shop-staging', 'shop-prod') {
  @"
apiVersion: v1
kind: Secret
metadata: { name: ghcr-pull, namespace: $ns }
type: kubernetes.io/dockerconfigjson
data: { .dockerconfigjson: $dockercfg }
"@ | kubectl apply -f -
}

# label secret-type=repository = Argo CD รู้ว่านี่คือรหัสเข้า repo · username อะไรก็ได้ที่ไม่ว่าง (GitHub ดูแค่ token)
@"
apiVersion: v1
kind: Secret
metadata:
  name: repo-devsecops-solution
  namespace: argocd
  labels: { argocd.argoproj.io/secret-type: repository }
type: Opaque
data:
  type: $(B64 'git')
  url: $(B64 $repoUrl)
  username: $(B64 'argocd')
  password: $(B64 $repo)
"@ | kubectl apply -f -

Remove-Variable pull, repo, auth, dockercfg
Write-Host 'เสร็จ · ตรวจ: kubectl -n argocd get secret -l argocd.argoproj.io/secret-type=repository'
