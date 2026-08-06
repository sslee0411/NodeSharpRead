using System.Diagnostics;

namespace NodeSharp.Runner.Diagnostics;

/// <summary>
/// Class명 : 이벤트 로그 기록기
/// 역활 및 기능 : Windows 이벤트 로그(이벤트 뷰어의 "응용 프로그램" 로그)에 중대한 사건만 최소한으로 남기는 정적 클래스
///
/// (RN-06b) Runner를 Windows Service로 등록해도, IT 관리자가 흔히 보는 이벤트 뷰어에는 아무것도
/// 남지 않아 자체 <c>audit.log</c>만 봐야 했던 공백을 메웁니다. 기록 대상은 서비스 시작/정상
/// 종료/크래시(<see cref="CrashDumpCollector"/>가 호출)/치명적 배포 실패뿐입니다 — 평시 상세
/// 로그(디버그 수준)는 여전히 <c>audit.log</c>가 담당하고, 이벤트 로그는 사내 표준 모니터링
/// 도구(SCOM 등)가 감시하는 요약 채널이라는 점에서 서로 대상 독자가 다릅니다. 설계 근거: 02번
/// 문서 7번 탭 카드14.
/// </summary>
/// <remarks>
/// <c>System.Diagnostics.EventLog.WriteEntry</c>는 Windows 전용이라(비-Windows에서는 호출 시
/// 예외) 이 개발 환경(Linux 샌드박스)에서는 실제 기록 여부를 자동 검증할 수 없습니다 — 그래서
/// 이 클래스에는 xUnit 테스트가 없고, "크래시 시 이벤트 로그에 기록이 남는지"는 사용자가 Windows
/// 에서 직접 확인합니다(RN-05a·RN-06a와 동일한 판단). 이벤트 소스(<see cref="Source"/>)는 미리
/// 등록돼 있어야 <c>WriteEntry</c>가 동작하므로, <c>tools\service\Install-NodeSharpService.ps1</c>
/// (RN-03a)에 <c>New-EventLog</c> 등록 단계를 추가했습니다. 소스가 아직 등록되지 않았거나
/// Windows가 아닌 환경에서 호출돼도 이 클래스 자체가 예외를 던져 상위(크래시 처리 흐름)를
/// 막지 않도록 내부에서 예외를 삼킵니다.
/// </remarks>
public static class EventLogWriter
{
    /// <summary>이벤트 뷰어에 표시되는 소스 이름 — 설치 스크립트가 이 이름으로 1회 등록해둔다.</summary>
    private const string Source = "NodeSharp.Runner";

    /// <summary>
    /// 오류 수준으로 1건 기록합니다. <paramref name="detail"/>이 있으면 메시지 아래 줄에 함께
    /// 남깁니다(예: 크래시 덤프 파일 경로). 이벤트 로그 자체를 쓰지 못하는 환경(소스 미등록,
    /// 비-Windows 등)에서도 예외를 밖으로 던지지 않습니다.
    /// </summary>
    public static void WriteError(string message, string? detail = null)
    {
        try
        {
            EventLog.WriteEntry(Source, detail is null ? message : $"{message}\n{detail}", EventLogEntryType.Error);
        }
        catch
        {
            // 이벤트 로그 기록 실패가 호출한 쪽(예: 크래시 처리)까지 막으면 안 되므로 삼킨다.
        }
    }

    /// <summary>정보 수준으로 1건 기록합니다(예: 서비스 시작/정상 종료). 실패해도 예외를 던지지 않습니다.</summary>
    public static void WriteInfo(string message)
    {
        try
        {
            EventLog.WriteEntry(Source, message, EventLogEntryType.Information);
        }
        catch
        {
            // WriteError와 동일한 이유로 삼킨다.
        }
    }
}
