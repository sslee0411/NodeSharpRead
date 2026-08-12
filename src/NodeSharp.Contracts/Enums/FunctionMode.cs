namespace NodeSharp.Contracts.Enums;

/// <summary>
/// Class명 : Function 노드 실행 모드
/// 역활 및 기능 : Function 노드가 사용자 코드를 NCalc 표현식과 Roslyn C# 코드 중 어느 쪽으로 실행할지 선택하는 값
///
/// Node-RED의 Function 노드는 Node.js VM에서 사용자 JS를 실행하는 단일 모드만 지원하지만,
/// NodeSharpRead는 코드를 몰라도 되는 <see cref="Expression"/>(NCalc, FN-01)과 완전한 로직이
/// 필요한 <see cref="CSharp"/>(Roslyn, FN-02) 두 모드를 사용자가 캔버스에서 직접 선택하도록
/// 지원합니다 — "노드 하나가 둘 다 지원, 사용자가 상황에 맞게 고름" 설계.
/// 설계 근거: 02번 문서 5번 탭 카드5, 03번 개발 Step맵 Phase 7 FN-01/FN-02.
/// </summary>
/// <remarks>
/// <c>FunctionNode.Mode</c>(nodes\NodeSharp.Nodes.Function)가 이 값에 따라
/// <see cref="Interfaces.IFunctionExecutor"/> 구현체(<c>NCalcFunctionExecutor</c>/
/// <c>RoslynFunctionExecutor</c>)를 선택합니다. <see cref="CSharp"/>은 이 Enum 자체는 FN-01에서
/// 함께 정의하지만, 실제 실행기(<c>RoslynFunctionExecutor</c>)는 FN-02(⏳ 대기)가 구현하기
/// 전까지 없습니다 — FN-01 시점에 <see cref="CSharp"/>을 선택하면 <c>FunctionNode.OnStartAsync</c>가
/// <c>NotSupportedException</c>을 던지고, 이는 <c>FlowEngine.DeployAsync</c>(RT-02b)의 노드별 예외
/// 격리가 잡아 <c>FailedNodeIds</c>에 기록합니다(Inject 노드의 잘못된 cron 표현식과 동일한 기존
/// 메커니즘 재사용 — 새 인프라 불필요).
/// </remarks>
/// <example>
/// <code>
/// var node = new FunctionNode { Mode = FunctionMode.Expression, Code = "(pressure1 - pressure2) * 0.0689" };
/// // Mode == FunctionMode.CSharp이면 현재(FN-02 이전)는 OnStartAsync에서 배포 실패로 처리됨
/// </code>
/// </example>
public enum FunctionMode
{
    /// <summary>NCalc 표현식 모드(FN-01) — 코드를 몰라도 한 줄 수식만 입력하면 되는 기본값. 문법 오류가 있어도 컴파일 없이 즉시 노드 에러로만 표면화됩니다.</summary>
    Expression,

    /// <summary>Roslyn C# 코드 모드(FN-02, 아직 <c>RoslynFunctionExecutor</c> 미구현) — 반복문·조건문 등 완전한 로직이 필요한 고급 사용자용.</summary>
    CSharp
}
