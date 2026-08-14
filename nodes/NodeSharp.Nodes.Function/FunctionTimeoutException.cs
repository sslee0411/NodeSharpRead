namespace NodeSharp.Nodes.Function;

/// <summary>
/// Class명 : Function 실행 타임아웃 예외
/// 역활 및 기능 : Roslyn C# 코드 실행이 설정된 타임아웃(기본 5초)을 초과했을 때 RoslynFunctionExecutor가 던지는 전용 예외
///
/// <see cref="RoslynFunctionExecutor.ExecuteAsync"/>가 <see cref="RoslynFunctionExecutor.ExecutionTimeoutSeconds"/>
/// 안에 사용자 C# 코드 실행을 끝내지 못했을 때 던지는 전용 예외입니다. 일반 <see cref="OperationCanceledException"/>과
/// 구분되는 별도 타입으로 만든 이유는 (1) 호출자(<c>FunctionNode.OnInputAsync</c>)가 "정상적인 플로우
/// 중지로 인한 취소"와 "사용자 코드 자체가 너무 오래 걸린 타임아웃"을 메시지로 구분해 노드 상태에
/// 표시할 수 있게 하고, (2) 향후 <c>NR-01a</c>(Catch 노드)가 붙으면 타임아웃만 골라 다르게 라우팅할
/// 수 있는 여지를 남기기 위함입니다.
/// 설계 근거: 02번 문서 5번 탭 카드7, 03번 개발 Step맵 Phase 7 FN-04.
/// </summary>
/// <remarks>
/// <b>★ "강제 중단"의 실제 의미(중요 — 코드 사용 전 반드시 확인)</b>: 이 예외가 던져진다는 것은
/// "<see cref="RoslynFunctionExecutor.ExecuteAsync"/> 호출이 타임아웃 시간 안에 <c>FunctionNode</c>에
/// 결과를 반환/보고한다"는 뜻이지, 사용자 C# 코드를 실행 중인 OS 스레드 자체를 강제로 죽인다는
/// 뜻이 아닙니다. .NET(Core 이후, 이 프로젝트의 net8.0 포함)은 <c>Thread.Abort</c>·<c>AppDomain</c>
/// 언로드를 모두 제거해, 관리형 코드에서 "다른 스레드를 강제로 즉시 정지"시킬 방법이 존재하지
/// 않습니다 — <see cref="CancellationToken"/>은 어디까지나 협조적(cooperative) 취소라, 사용자 코드가
/// <c>while(true){}</c>처럼 토큰을 스스로 검사하거나 <c>await</c>하는 지점이 전혀 없으면 토큰을
/// 취소해도 그 스레드는 멈추지 않고 백그라운드에서 계속 실행됩니다(스레드 풀 스레드 1개를 계속
/// 점유). <see cref="RoslynFunctionExecutor"/>는 이런 코드에도 <c>FunctionNode</c>가 무한정 멈추지
/// 않도록 "watchdog" 방식(별도 스레드 풀 스레드에 맡기고, 그 스레드가 끝나길 기다리지 않고 타임아웃만
/// 지나면 이 예외로 즉시 반환)을 씁니다 — 즉 "노드가 멈추지 않고 계속 동작한다"는 완료 기준은
/// 충족하지만, "그 스레드가 실제로 종료된다"는 보장은 하지 않습니다. 진짜 OS 수준 강제 종료가
/// 필요하면 Roslyn 실행을 별도 프로세스로 분리해야 하며, 이는 이 Step 범위를 크게 벗어나는
/// 아키텍처 변경이라 향후 과제로 남깁니다(설계 문서 02번 5번 탭 카드7의 <c>CancellationToken</c>
/// 기반 코드 스니펫도 동일한 한계를 그대로 가짐 — <c>await</c> 지점이 있는 코드에서는 정상적으로
/// 즉시 취소됩니다).
/// </remarks>
public sealed class FunctionTimeoutException : Exception
{
    /// <summary>지정한 메시지로 새 인스턴스를 만듭니다.</summary>
    public FunctionTimeoutException(string message) : base(message)
    {
    }
}
