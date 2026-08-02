<#
    Uninstall-NodeSharpService.ps1
    ====================
    Install-NodeSharpService.ps1로 등록한 Windows Service를 제거하는 스크립트다
    (02번 설계 문서 10번 탭 카드: "sc.exe create 래핑 PowerShell"의 짝, 제거 쪽).

    Step: RN-03a (03번 개발 Step맵.html, Phase 4)
    - 완료 기준: "제거 스크립트 실행 후 목록에서 사라지는지 확인" — 이 스크립트는 sc.exe delete
      직후 sc.exe query를 다시 실행해, 서비스가 실제로 더 이상 조회되지 않는지 스스로 확인해
      콘솔에 성공/실패를 명확히 출력한다(Install 스크립트와 동일한 "자체 확인 로직" 방식).
    - xUnit으로 자동 검증할 수 없는 영역이다(PowerShell 스크립트라 .NET 테스트 대상이 아니고,
      실제 제거도 Windows 관리자 권한이 있어야 함) — 최종 확인은 사용자가 이 스크립트를 Windows에서
      관리자 권한으로 직접 실행해 콘솔 출력을 보고 판단한다.
#>

[CmdletBinding()]
param(
    # 제거할 서비스 이름 — Install-NodeSharpService.ps1과 동일한 기본값을 사용
    [string]$ServiceName = "NodeSharpRunner"
)

# 1) 관리자 권한 확인
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "관리자 권한(마우스 우클릭 → '관리자 권한으로 실행')으로 다시 실행해 주세요."
    exit 1
}

# 2) 서비스가 실행 중이면 먼저 정지(실행 중인 서비스는 delete만으로는 완전히 제거되지 않을 수 있음)
Write-Host "[1/2] 서비스를 정지합니다(실행 중이 아니면 건너뜀): $ServiceName"
& sc.exe stop $ServiceName | Out-Null
# stop 실패는 무시한다 — "이미 정지됨"/"애초에 없음" 등도 실패로 보고되므로 delete 단계에서 최종 판단

# 3) sc.exe delete로 서비스 제거
Write-Host "[2/2] 서비스를 제거합니다: sc delete $ServiceName"
& sc.exe delete $ServiceName | Out-Null
$deleteExitCode = $LASTEXITCODE

# 4) 자체 확인(완료 기준 핵심) — sc.exe query가 더 이상 이 서비스를 찾지 못해야 성공
$queryOutput = & sc.exe query $ServiceName 2>&1
$stillRegistered = ($LASTEXITCODE -eq 0) -and (($queryOutput -join "`n") -match [regex]::Escape("SERVICE_NAME: $ServiceName"))

if (-not $stillRegistered) {
    Write-Host "성공 — '$ServiceName' 서비스가 목록에서 사라졌습니다." -ForegroundColor Green
    exit 0
}
else {
    Write-Error "제거 확인에 실패했습니다(delete 종료 코드 $deleteExitCode) — sc query에 아직 서비스가 남아 있습니다."
    Write-Host $queryOutput
    exit 1
}
