using NodeSharp.Contracts.Enums;

namespace NodeSharp.Contracts.Models;

/// <summary>
/// 캔버스에 배치된 노드 하나의 저장용 설정입니다. <c>flows.json</c>에 저장되는 노드 단위
/// 레코드이며, Editor가 이 값을 채워 저장하고 Runner가 읽어 <c>FlowEngine.DeployAsync</c>로
/// 실제 노드 인스턴스를 만드는 데 사용합니다.
/// </summary>
/// <remarks>
/// <para>
/// 설계 근거: 02번 설계 문서 2번 탭 카드 10 "NodeConfig / FlowDefinition 완전 정의" — 이전에
/// 여러 탭에서 <c>NodeConfig(...)</c>처럼 생략 표기로만 등장하던 것을 전체 필드로 완성한
/// 정식 선언입니다(5번 탭 <c>OutputDispatch</c>, 9번 탭 <c>CredentialRefId</c>/<c>Disabled</c> 등
/// 여러 탭에서 점진적으로 필요해진 필드를 모두 합침).
/// </para>
/// <para>
/// <b>직렬화 방식(중요 — <see cref="Models.Msg"/>와 다름)</b>: <c>flows.json</c>에 저장되는
/// 노드/플로우 정의 계열(<see cref="NodeConfig"/>, <see cref="Wire"/>, <see cref="NodePort"/>,
/// <c>FlowDefinition</c>)은 <b>System.Text.Json</b>을 사용합니다. 반면 노드 사이를 오가는
/// 메시지 데이터(<see cref="Models.Msg"/>)는 <b>Newtonsoft.Json</b>을 사용합니다 — 02번 문서
/// 2번 탭 카드 3 표에 명시된 방침이며, "정적 스키마를 가진 설정"과 "런타임에 동적으로
/// 확장되는 메시지"를 서로 다른 직렬화 전략으로 다루는 것이 의도적인 설계입니다.
/// </para>
/// <para>
/// <b><see cref="Properties"/> 역직렬화 시 주의</b>: <see cref="Properties"/>는
/// <c>IReadOnlyDictionary&lt;string, object?&gt;</c>이므로, System.Text.Json으로 역직렬화하면
/// 각 값은 원래의 CLR 타입(예: <c>int</c>, <c>bool</c>)이 아니라
/// <see cref="System.Text.Json.JsonElement"/>로 채워집니다(System.Text.Json이 <c>object</c>
/// 타입 멤버를 다룰 때의 기본 동작 — Newtonsoft.Json이 <see cref="Models.Msg"/>에서 원래 CLR
/// 타입으로 복원해주는 것과 다릅니다). 값을 실제로 사용하려면
/// <c>element.GetString()</c>/<c>element.GetInt32()</c>/<c>element.GetBoolean()</c> 등
/// <see cref="System.Text.Json.JsonElement"/>의 접근자를 거쳐야 합니다 — CT-07에서
/// <c>PropertySchema</c>/<c>PropertyField</c>를 구현할 때 이 점을 반영해야 합니다.
/// </para>
/// <para>
/// <b>record 동등성(<c>==</c>/<see cref="Equals(object)"/>) 관련 주의</b>: <see cref="NodeConfig"/>는
/// <c>record</c>이므로 필드 값 기반 동등성을 자동 생성하지만, <see cref="Properties"/>
/// (<c>IReadOnlyDictionary&lt;string, object?&gt;</c>)는 컬렉션 타입이라 <c>Dictionary</c>가
/// 값 동등성을 오버라이드하지 않는 한 <b>내용이 같아도 참조가 다르면 다른 값으로 판정</b>됩니다.
/// 두 <see cref="NodeConfig"/>가 "내용상 같은지"를 비교해야 하는 코드(예: 배포 시 변경 여부
/// 판단)는 record의 기본 <c>==</c>에 의존하지 말고 필드 단위 비교를 해야 합니다.
/// </para>
/// </remarks>
/// <param name="Id">이 노드의 고유 식별자(플로우 내에서 유일). 캔버스에서 노드를 배치할 때 발급되며 이후 변경되지 않습니다.</param>
/// <param name="Type">노드 타입 이름(예: <c>"inject"</c>, <c>"function"</c>). <c>NodeRegistry</c>가 이 값으로 실제 구현 클래스를 찾습니다.</param>
/// <param name="Name">캔버스에 표시되는 사용자 지정 이름(비워두면 보통 <see cref="Type"/> 기본값을 화면에 표시).</param>
/// <param name="FlowId">이 노드가 속한 Flow 탭의 <c>FlowDefinition.Id</c>.</param>
/// <param name="Properties">
/// 노드별 사용자 설정값(9번 탭 <c>PropertySchema</c>/<c>PropertyField</c> 기반 속성 편집 폼에서 입력한 값들).
/// 키는 필드 이름, 값은 필드 타입에 따라 다양한 CLR 타입(문자열/숫자/불리언 등)입니다.
/// </param>
/// <param name="OutputDispatch">여러 출력 와이어가 있을 때 순차/병렬 중 어떤 방식으로 전달할지(5번 탭 Fan-out). 기본값은 <see cref="DispatchMode.Sequential"/>.</param>
/// <param name="MaxConcurrency">이 노드가 동시에 처리할 수 있는 최대 메시지 수(5번 탭, SemaphoreSlim 기반 동시성 제한). 기본값 1(동시 처리 없음, 순차 처리).</param>
/// <param name="CredentialRefId">이 노드가 사용하는 자격증명 항목의 참조 키(9번 탭 <c>CredentialRef</c>). 자격증명이 필요 없는 노드는 <c>null</c>.</param>
/// <param name="Disabled">이 노드가 비활성화되어 있는지(9번 탭 Enable-Disable). <c>true</c>면 배포 시 이 노드는 생성되지 않습니다.</param>
/// <example>
/// <code>
/// var config = new NodeConfig(
///     Id: "n1", Type: "function", Name: "온도 변환", FlowId: "f1",
///     Properties: new Dictionary&lt;string, object?&gt; { ["code"] = "return msg.payload * 1.8 + 32;" },
///     OutputDispatch: DispatchMode.Sequential,
///     MaxConcurrency: 1,
///     CredentialRefId: null,
///     Disabled: false);
/// </code>
/// </example>
public sealed record NodeConfig(
    string Id,
    string Type,
    string Name,
    string FlowId,
    IReadOnlyDictionary<string, object?> Properties,
    DispatchMode OutputDispatch = DispatchMode.Sequential,
    int MaxConcurrency = 1,
    string? CredentialRefId = null,
    bool Disabled = false);
