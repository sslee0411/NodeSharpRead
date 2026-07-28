namespace NodeSharp.Contracts.Enums;

/// <summary>
/// 한 노드가 여러 출력 와이어를 가질 때(Fan-out), 그 와이어들에 메시지를 어떤 순서로
/// 전달할지 지정합니다.
/// </summary>
/// <remarks>
/// <para>
/// 설계 근거: 02번 설계 문서 5번 탭(동작모델) 카드 1, 2번 탭 카드 10(<c>NodeConfig</c> 정식 선언).
/// <c>NodeConfig.OutputDispatch</c> 필드의 타입입니다.
/// </para>
/// <para>
/// 발견 경위: 이 Enum은 <c>CT-01a</c>(상태/실행 계열)·<c>CT-01b</c>(통신/파라미터 계열)
/// 어디에도 포함되지 않은 채로 02번 문서에만 존재했는데, <c>CT-02b</c>(<c>NodeConfig</c> 구현)를
/// 진행하려는 시점에 <c>NodeConfig</c>가 이 타입을 직접 참조해야 컴파일된다는 것을 확인했습니다.
/// 이미 만들어진 두 Enum Step(CT-01a/b)을 다시 여는 대신, 이 Enum을 실제로 필요로 하는
/// <c>CT-02b</c> 안에서 함께 정의합니다(03번 개발 Step맵.html CT-02b [조치] 참고).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 기본값은 Sequential — 여러 와이어에 순서대로(하나씩 완료 후 다음) 전달
/// var config = new NodeConfig(Id: "n1", Type: "function", Name: "예시", FlowId: "f1",
///     Properties: new Dictionary&lt;string, object?&gt;(), OutputDispatch: DispatchMode.Parallel);
/// </code>
/// </example>
public enum DispatchMode
{
    /// <summary>순차 전달 — 첫 번째 와이어로 전달·완료된 뒤 다음 와이어로 전달합니다(기본값).</summary>
    Sequential,

    /// <summary>병렬 전달 — 모든 와이어에 동시에 전달합니다(더 빠르지만, 각 분기가 독립적으로 동시에 실행되어야 함).</summary>
    Parallel
}
