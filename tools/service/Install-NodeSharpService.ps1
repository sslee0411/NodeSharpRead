<#
    Install-NodeSharpService.ps1
    ====================
    NodeSharp.Runner(헤드리스 실행 프로세스)를 Windows Service로 등록하는 설치 스크립트다
    (02번 설계 문서 10번 탭 카드: "sc.exe create 래핑 PowerShell").

    Step: RN-03a (03번 개발 Step맵.html, Phase 4)
    - 완료 기준: "설치 스크립트 실행 후 서비스 목록(sc query)에 등록되고, 제거 스크립트 실행 후
      목록에서 사라지는지 확인" — 이 스크립트는 sc.exe create 직후 sc.exe query로 실제 등록
      여부를 스스로 확인해 콘솔에 성공/실패를 명확히 출력한다(사용자 확인 부담을 줄이기 위한
      자체 확인 로직, 사용자가 "스크립트 + 자체 확인 로직" 방식으로 확인).
    - xUnit으로 자동 검증할 수 없는 영역이다(PowerShell 스크립트라 .NET 테스트 대상이 아니고,
      실제 서비스 등록도 Windows 관리자 권한이 있어야 함) — 최종 확인은 사용자가 이 스크립트를
      Windows에서 관리자 권한으로 직접 실행해 콘솔 출력을 보고 판단한다. 사용자가 실제로
      관리자 권한으로 실행해 sc query 결과로 등록/제거를 확인함(2026-08-02).
    - 시작 유형(auto)까지만 이 Step에서 설정한다 — 비정상 종료 시 자동 재기동(sc failure 복구
      옵션)은 RN-07(Windows Service 자동 재기동/Watchdog)의 범위다.

    Step: RN-03b (03번 개발 Step맵.html, Phase 4 — 서비스 계정 최소 권한 설정)
    - 완료 기준: "설치 스크립트가 기본값으로 NetworkService 계정을 사용하고, LocalSystem 지정
      시 설치가 거부되는지 확인". RN-03a에서는 sc.exe 기본값(LocalSystem)을 그대로 썼는데,
      LocalSystem은 관리자 권한 전체를 서비스에 주는 과도한 권한이라(v1.23 최소 권한 원칙,
      02번 문서 10번 탭 카드) 이 Step에서 기본 계정을 NT AUTHORITY\NetworkService로 바꾸고,
      LocalSystem을 명시적으로 지정하면 등록 자체를 거부하는 가드를 추가했다.
    - -ServiceAccount 파라미터 추가(기본값 "NT AUTHORITY\NetworkService"). LocalSystem 계열
      이름("LocalSystem"/".\LocalSystem"/"NT AUTHORITY\SYSTEM"/"NT AUTHORITY\LocalSystem", 대소문자
      무시)을 지정하면 등록 전에 즉시 거부하고 이유를 안내한다(관리자 권한 확인 직후, sc create
      호출 전에 막아 애초에 등록되지 않게 함).
    - sc.exe create에 obj= "$ServiceAccount"를 추가로 전달한다. NetworkService/LocalService처럼
      암호가 필요 없는 Windows 내장 가상 계정만 지원 범위로 한정한다(주석에 명시) — 도메인/사용자
      계정처럼 password=가 필요한 계정 지원은 이 Step 범위 밖(필요해지면 별도 Step에서 추가).
    - 자체 확인 로직도 확장: sc.exe qc(설정 조회)로 SERVICE_START_NAME이 실제로 지정한 계정과
      일치하는지 함께 출력해, "기본값으로 NetworkService를 쓰는지" 완료 기준을 사용자가 콘솔
      출력만 보고 바로 판단할 수 있게 했다.

    버그 수정 (2026-08-02, RN-03b 확인 중 발견)
    - 사용자가 실행 시 65번째 줄 근처에서 "예기치 않은 '}' 토큰" ParseException 보고 — 원인은
      코드가 아니라 파일 인코딩이었다. 이 파일이 BOM 없는 UTF-8로 저장돼 있었는데, Windows
      PowerShell 5.1은 BOM이 없으면 스크립트를 시스템 기본 코드페이지(한글 Windows는 cp949,
      2바이트 조합형)로 잘못 해석한다 — 한글 주석의 UTF-8 멀티바이트 시퀀스가 cp949 2바이트
      문자로 잘못 묶이면서 개행 바이트(0x0A)까지 그 조합에 먹혀, 실제 줄 수보다 파서가 인식하는
      줄 수가 줄어들고 중괄호 짝이 어긋난 것처럼 보이게 됐다(중괄호 자체는 원래도 정상 짝이었음,
      grep 기반 중괄호 균형 확인도 계속 9/9로 일치했었다 — 이 검증 도구가 인코딩 문제까지는
      잡아내지 못한다는 것도 이번에 확인). 파일 맨 앞에 UTF-8 BOM(EF BB BF)을 추가해 Windows
      PowerShell 5.1도 UTF-8로 올바르게 읽도록 수정 — 로직·문구는 전혀 바꾸지 않음.
#>

[CmdletBinding()]
param(
    # 등록할 서비스 이름(내부 식별자, sc query에 표시되는 SERVICE_NAME)
    [string]$ServiceName = "NodeSharpRunner",

    # NodeSharp.Runner.exe의 전체 경로(필수) — dotnet publish 결과물 위치를 지정한다
    [Parameter(Mandatory = $true)]
    [string]$BinaryPath,

    # 서비스 관리자(services.msc)에 표시되는 이름
    [string]$DisplayName = "NodeSharp Runner",

    # 서비스 설명
    [string]$Description = "NodeSharp 헤드리스 런타임(flows.json 배포·실행) — Node-RED 클론 프로젝트",

    # (RN-03b) 서비스 실행 계정 — 기본값은 최소 권한 원칙(v1.23)에 따라 NetworkService.
    # LocalSystem은 아래 가드에서 거부되므로 이 파라미터로 지정해도 등록되지 않는다.
    [string]$ServiceAccount = "NT AUTHORITY\NetworkService"
)

# 1) 관리자 권한 확인 — sc create는 관리자 권한 없이는 실패한다(공통 규칙: 실패 원인을 앞에서 명확히 알림)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "관리자 권한(마우스 우클릭 → '관리자 권한으로 실행')으로 다시 실행해 주세요."
    exit 1
}

# 2) (RN-03b) LocalSystem 지정 거부 — 최소 권한 원칙(v1.23) 가드. 등록 전에 미리 막는다.
$localSystemAliases = @("LocalSystem", ".\LocalSystem", "NT AUTHORITY\SYSTEM", "NT AUTHORITY\LocalSystem")
if ($localSystemAliases -contains $ServiceAccount) {
    Write-Error "ServiceAccount로 LocalSystem은 지정할 수 없습니다(최소 권한 원칙, v1.23) — 기본값 NT AUTHORITY\NetworkService를 사용하거나, PLC 통신·파일 쓰기에 필요한 권한만 가진 전용 계정을 지정해 주세요."
    exit 1
}

# 3) 실행 파일 존재 확인 — 없는 경로로 서비스를 등록하면 나중에 "시작"할 때만 실패해 원인 파악이 늦어짐
if (-not (Test-Path -LiteralPath $BinaryPath)) {
    Write-Error "BinaryPath에 해당하는 파일이 없습니다: $BinaryPath"
    exit 1
}

# 4) sc.exe create로 서비스 등록(등호 뒤 공백은 sc.exe 문법상 필수)
#    obj=는 NetworkService/LocalService 같은 암호 불필요 내장 계정만 지원(RN-03b 범위)
Write-Host "[1/3] 서비스를 등록합니다: $ServiceName (계정: $ServiceAccount)"
& sc.exe create $ServiceName binPath= "`"$BinaryPath`"" DisplayName= "$DisplayName" start= auto obj= "$ServiceAccount" | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "sc.exe create 실패(종료 코드 $LASTEXITCODE) — 이미 같은 이름의 서비스가 등록돼 있을 수 있습니다."
    exit 1
}

# 5) 서비스 설명 추가(선택 정보라 실패해도 치명적이지 않지만, 실패 시 경고는 남긴다)
& sc.exe description $ServiceName "$Description" | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Warning "sc.exe description 설정에 실패했습니다(서비스 등록 자체는 완료됨)."
}

# 6) 자체 확인(완료 기준 핵심) — sc.exe query로 방금 등록한 서비스가 실제로 목록에 있는지 재확인
Write-Host "[2/3] 등록 결과를 확인합니다: sc query $ServiceName"
$queryOutput = & sc.exe query $ServiceName 2>&1
if ($LASTEXITCODE -eq 0 -and ($queryOutput -join "`n") -match [regex]::Escape("SERVICE_NAME: $ServiceName")) {
    Write-Host "성공 — '$ServiceName' 서비스가 등록되었습니다." -ForegroundColor Green
    Write-Host $queryOutput
}
else {
    Write-Error "등록 확인에 실패했습니다 — sc query 결과에서 서비스를 찾을 수 없습니다."
    Write-Host $queryOutput
    exit 1
}

# 7) (RN-03b) 자체 확인 — sc.exe qc로 실제 등록된 실행 계정(SERVICE_START_NAME)이 의도한 값인지 재확인
Write-Host "[3/3] 서비스 실행 계정을 확인합니다: sc qc $ServiceName"
$qcOutput = & sc.exe qc $ServiceName 2>&1
if (($qcOutput -join "`n") -match [regex]::Escape($ServiceAccount)) {
    Write-Host "성공 — 서비스 실행 계정이 '$ServiceAccount'(으)로 등록되었습니다." -ForegroundColor Green
    exit 0
}
else {
    Write-Error "서비스 실행 계정 확인에 실패했습니다 — sc qc 결과에서 '$ServiceAccount'를 찾을 수 없습니다."
    Write-Host $qcOutput
    exit 1
}
