namespace NodeSharp.Runner;

/// <summary>
/// Class명 : 기동 단계 결과
/// 역활 및 기능 : StartupSequencer가 파일 하나를 로딩한 결과(성공 여부·오류 메시지)를 담는 값
///
/// <see cref="StartupSequencer"/>가 파일 하나(예: <c>device.json</c>)를 로딩한 결과입니다(RN-01a).
/// <see cref="Succeeded"/>가 <c>false</c>여도 <see cref="StartupSequencer.RunAsync"/>는 예외를 던지지
/// 않고 다음 단계로 계속 진행합니다 — "파일 하나가 손상/누락된 경우 해당 단계만 격리하고 다음 단계는
/// 계속 진행한다"는 02번 문서 3번 탭 카드8 원칙 그대로입니다.
/// </summary>
public sealed record StartupStageResult(string FileName, bool Succeeded, string? ErrorMessage);
