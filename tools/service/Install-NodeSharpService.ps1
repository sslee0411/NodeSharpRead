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

    Step: RN-06b (03번 개발 Step맵.html, Phase 4 — EventLogWriter + 덤프 보호)
    - 완료 기준 중 "파일 권한이 제한되는지"는 코드가 아니라 배포 시점의 NTFS 폴더 권한 문제라
      판단해 이 설치 스크립트에 추가했다(RN-03b가 서비스 실행 계정을 이미 -ServiceAccount로
      알고 있어 같은 스크립트가 자연스러운 위치). EventLogWriter(RN-06b, NodeSharp.Runner)가
      쓰려면 이벤트 소스가 미리 등록돼 있어야 하므로 그 등록도 함께 추가했다.
    - New-EventLog로 "NodeSharp.Runner" 이벤트 소스를 Application 로그에 등록(이미 등록돼
      있으면 건너뜀 — 재실행해도 오류 나지 않게).
    - $BinaryPath 옆에 crashdumps\ 폴더를 만들고, icacls로 상속을 끊은 뒤 SYSTEM·
      BUILTIN\Administrators·$ServiceAccount 3개만 읽기/쓰기 권한을 갖도록 제한(크래시 덤프에는
      9번 탭 ICredentialStore가 복호화해서 들고 있던 자격증명 평문이 남을 수 있어 credentials.json과
      동급으로 취급, 02번 문서 7번 탭 카드14 근거).
    - 두 단계 모두 서비스 등록 자체(RN-03a/RN-03b 완료 기준)와는 무관한 부가 기능이라, 실패해도
      Write-Warning만 남기고 스크립트 전체를 실패 처리(exit 1)하지는 않는다(기존 완료 기준을
      건드리지 않기 위함).

    Step: RN-07 (03번 개발 Step맵.html, Phase 4 — Windows Service 자동 재기동/Watchdog)
    - 완료 기준: "프로세스를 강제 종료했을 때 자동으로 재기동되는지 확인". Step 설명이 제시한 두
      선택지(sc failure 설정 또는 자체 Watchdog) 중, 02번 문서 10번 탭 카드12("서비스 속성에서
      '실패 시 재시작' 정책 설정 — 프로세스가 예기치 않게 죽어도 OS가 자동으로 다시 띄움")가
      가리키는 sc.exe failure 방식을 선택 — 별도 C# 코드 없이 OS(SCM)가 직접 감시·재시작하는
      쪽이 더 단순하고, 자체 폴링 Watchdog(카드11이 언급만 하고 코드는 없음)을 새로 만들면 그
      Watchdog 자신이 죽는 경우까지 또 대비해야 하는 이중 문제가 생기기 때문. RN-03a/RN-03b와
      완전히 같은 성격의 Step(PowerShell 스크립트, xUnit 대상 아님, 관리자 권한 필요, 자체 확인
      로직 + 사용자 최종 수동 확인)이라 별도 AskUserQuestion 없이 동일한 패턴으로 바로 진행.
    - sc.exe failure로 복구 옵션 설정 — reset= 86400(24시간 동안 추가 실패 없으면 실패 횟수
      리셋), actions= restart/60000을 3회 반복(1~3번째 실패 모두 60초 후 재시작 — sc.exe는
      마지막 action을 그 이후 실패에도 반복 적용). 실패하면(sc.exe 자체 오류) RN-07 완료 기준을
      만족 못 하므로 exit 1로 스크립트를 실패 처리한다(RN-06b의 "부가 기능"과 달리 이건 이 Step의
      본 목적이라 경고만 남기고 넘어가지 않음).
    - sc.exe failureflag $ServiceName 1도 함께 설정 — 기본값(0)은 서비스가 "제어된 방식으로"
      멈춘 경우 복구 액션이 적용되지 않을 수 있는데, .NET 프로세스가 처리되지 않은 예외로 죽는
      경우까지 복구 액션이 적용되도록 켠다. 일부 Windows 버전/구성에서 이 명령 자체가 없을 수
      있어 실패해도 Write-Warning만 남기고 계속 진행(sc failure의 기본 재시작 정책은 이미 걸려
      있으므로 완전히 무력화되지는 않음).
    - 자체 확인 — sc.exe qfailure로 방금 설정한 복구 옵션에 RESTART 동작이 포함됐는지 재확인.
      RN-03a/RN-03b의 sc query/qc 자체 확인과 동일한 방식.
    - 실제 "프로세스를 강제 종료했을 때 재기동되는지"는 Windows Service Control Manager의 실제
      동작이라 이 개발 환경(Linux 샌드박스)은 물론 PowerShell 스크립트 자체 검증만으로도 확인할
      수 없다 — 사용자가 Windows에서 서비스를 시작한 뒤 작업 관리자로 프로세스를 강제 종료해
      약 60초 뒤 다시 떠 있는지 직접 확인해야 한다(RN-03a/RN-03b와 동일하게 xUnit 테스트 없음).
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
}
else {
    Write-Error "서비스 실행 계정 확인에 실패했습니다 — sc qc 결과에서 '$ServiceAccount'를 찾을 수 없습니다."
    Write-Host $qcOutput
    exit 1
}

# 8) (RN-06b) 이벤트 소스 등록 — EventLogWriter(NodeSharp.Runner)가 쓰려면 미리 등록돼 있어야 함.
#    이미 등록돼 있으면 New-EventLog가 오류를 내므로 먼저 존재 여부를 확인하고 없을 때만 등록한다.
#    이 부가 기능이 실패해도(예: 권한 부족) 서비스 등록 자체(완료 기준)는 이미 끝났으므로 경고만 남긴다.
$eventSource = "NodeSharp.Runner"
Write-Host "[부가] 이벤트 로그 소스를 등록합니다: $eventSource"
try {
    if (-not [System.Diagnostics.EventLog]::SourceExists($eventSource)) {
        New-EventLog -LogName Application -Source $eventSource
        Write-Host "성공 — 이벤트 소스 '$eventSource'를 Application 로그에 등록했습니다." -ForegroundColor Green
    }
    else {
        Write-Host "이미 등록돼 있어 건너뜁니다 — 이벤트 소스 '$eventSource'."
    }
}
catch {
    Write-Warning "이벤트 소스 등록에 실패했습니다(서비스 등록 자체는 완료됨): $($_.Exception.Message)"
}

# 9) (RN-06b) 크래시 덤프 폴더 접근 권한 제한 — 덤프에는 복호화된 자격증명이 남을 수 있어
#    credentials.json과 동급으로 취급(02번 문서 7번 탭 카드14). $BinaryPath와 같은 폴더에
#    crashdumps\를 만들고, 상속을 끊은 뒤 SYSTEM·Administrators·서비스 계정만 접근 가능하게 한다.
try {
    $dumpDirectory = Join-Path -Path (Split-Path -Path $BinaryPath -Parent) -ChildPath "crashdumps"
    if (-not (Test-Path -LiteralPath $dumpDirectory)) {
        New-Item -ItemType Directory -Path $dumpDirectory | Out-Null
    }
    Write-Host "[부가] 크래시 덤프 폴더 접근 권한을 제한합니다: $dumpDirectory"
    & icacls.exe "$dumpDirectory" /inheritance:r | Out-Null
    & icacls.exe "$dumpDirectory" /grant:r "SYSTEM:(OI)(CI)F" "BUILTIN\Administrators:(OI)(CI)F" "${ServiceAccount}:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "성공 — '$dumpDirectory' 접근 권한을 SYSTEM/Administrators/$ServiceAccount로 제한했습니다." -ForegroundColor Green
    }
    else {
        Write-Warning "icacls 권한 설정이 실패했습니다(종료 코드 $LASTEXITCODE) — 서비스 등록 자체는 완료됨."
    }
}
catch {
    Write-Warning "크래시 덤프 폴더 권한 제한 중 오류가 발생했습니다(서비스 등록 자체는 완료됨): $($_.Exception.Message)"
}

# 10) (RN-07) 실패 시 자동 재시작 정책 설정 — 이 Step의 완료 기준 본체이므로 실패하면 스크립트도 실패 처리한다.
Write-Host "[RN-07 1/2] 실패 시 자동 재시작 정책을 설정합니다: sc failure $ServiceName"
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error "sc.exe failure 설정에 실패했습니다(종료 코드 $LASTEXITCODE) — RN-07 완료 기준(강제 종료 시 자동 재기동)을 만족하지 못합니다."
    exit 1
}

# 11) (RN-07) 처리되지 않은 예외로 죽는 경우까지 복구 액션이 적용되도록 failureflag를 켠다.
#     일부 Windows 버전/구성에는 이 명령 자체가 없을 수 있어, 실패해도 경고만 남기고 계속 진행한다
#     (기본 재시작 정책은 위 10)에서 이미 걸려 있어 완전히 무력화되지는 않음).
Write-Host "[RN-07 2/2] 처리되지 않은 예외에도 복구 액션이 적용되도록 설정합니다: sc failureflag $ServiceName 1"
& sc.exe failureflag $ServiceName 1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Warning "sc.exe failureflag 설정에 실패했습니다 — 이 Windows 버전/구성에는 없는 명령일 수 있습니다(기본 재시작 정책은 여전히 적용됨)."
}

# 12) (RN-07) 자체 확인 — sc.exe qfailure로 방금 설정한 복구 옵션에 RESTART 동작이 실제로 반영됐는지 재확인
Write-Host "[RN-07 확인] 복구 옵션을 확인합니다: sc qfailure $ServiceName"
$qfailureOutput = & sc.exe qfailure $ServiceName 2>&1
if (($qfailureOutput -join "`n") -match "RESTART") {
    Write-Host "성공 — 실패 시 자동 재시작(RESTART) 복구 정책이 등록되었습니다." -ForegroundColor Green
}
else {
    Write-Error "복구 옵션 확인에 실패했습니다 — sc qfailure 결과에서 RESTART 동작을 찾을 수 없습니다."
    Write-Host $qfailureOutput
    exit 1
}

exit 0
