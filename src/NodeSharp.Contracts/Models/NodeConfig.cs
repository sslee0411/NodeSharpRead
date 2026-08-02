using NodeSharp.Contracts.Enums;

namespace NodeSharp.Contracts.Models;

/// <summary>
/// Class명 : 노드 설정
/// 역활 및 기능 : 캔버스에 배치된 노드 하나의 저장용 설정을 나타내는 모델
///
/// 캔버스에 배치된 노드 하나의 저장용 설정입니다. <c>flows.json</c>에 저장되는 노드 단위
/// 레코드이며, Editor가 이 값을 채워 저장하고 Runner가 읽어 <c>FlowEngine.DeployAsync</c>로
/// 실제 노드 인스턴스를 만드는 데 사용합니다.
/// 설계 근거: 02번 문서 2번 탭 카드 10(여러 탭에서 점진적으로 필요해진 필드를 모두 합친 정식 선언).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>직렬화 방식</b>: <see cref="NodeConfig"/>/<see cref="Wire"/>/<see cref="NodePort"/>/
/// <c>FlowDefinition</c> 같은 정적 스키마 설정은 System.Text.Json을 사용하고, 런타임에 동적으로
/// 확장되는 <see cref="Msg"/>는 Newtonsoft.Json을 사용합니다(02번 문서 2번 탭 카드 3, 의도적인 이원화).</item>
/// <item><b><see cref="Properties"/> 역직렬화 주의</b>: System.Text.Json으로 역직렬화하면 각 값이
/// 원래 CLR 타입(<c>int</c>, <c>bool</c> 등)이 아니라 <see cref="System.Text.Json.JsonElement"/>로
/// 채워집니다. 실제 값을 쓰려면 <c>element.GetString()</c>/<c>GetInt32()</c>/<c>GetBoolean()</c> 등을
/// 거쳐야 합니다(<c>CT-07</c>에서 <c>PropertySchema</c>/<c>PropertyField</c> 구현 시 반영 필요).</item>
/// <item><b>record 동등성 주의</b>: <see cref="Properties"/>(딕셔너리)는 record의 기본 <c>==</c>가
/// 참조를 비교하므로, 내용이 같아도 인스턴스가 다르면 다르다고 판정됩니다. 두 <see cref="NodeConfig"/>가
/// "내용상 같은지" 비교해야 하는 코드(예: 배포 시 변경 여부 판단, 향후 <c>RT-03</c>)는 record의
/// 기본 <c>==</c>에 의존하지 말고 필드 단위로 비교해야 합니다.</item>
/// </list>
/// </remarks>
/// <param name="Id">이 노드의 고유 식별자(플로우 내에서 유일). 캔버스에서 노드를 배치할 때 발급되며 이후 변경되지 않습니다.</param>
/// <param name="Type">노드 타입 이름(예: <c>"inject"</c>, <c>"function"</c>). <c>NodeRegistry</c>가 이 값으로 실제 구현 클래스를 찾습니다.</param>
/// <param name="Name">캔버스에 표시되는 사용자 지정 이름(비워두면 보통 <see cref="Type"/> 기본값을 화면에 표시).</param>
/// <param name="FlowId">이 노드가 속한 Flow 탭의 <c>FlowDefinition.Id</c>.</param>
/// <param name="Properties">노드별 사용자 설정값(속성 편집 폼에서 입력한 값들). 키는 필드 이름, 값은 필드 타입에 따라 다양한 CLR 타입입니다.</param>
/// <param name="OutputDispatch">여러 출력 와이어가 있을 때 순차/병렬 중 어떤 방식으로 전달할지(5번 탭 Fan-out). 기본값은 <see cref="DispatchMode.Sequential"/>.</param>
/// <param name="MaxConcurrency">이 노드가 동시에 처리할 수 있는 최대 메시지 수. 기본값 1(동시 처리 없음, 순차 처리).</param>
/// <param name="CredentialRefId">이 노드가 사용하는 자격증명 항목의 참조 키. 자격증명이 필요 없는 노드는 <c>null</c>.</param>
/// <param name="Disabled">이 노드가 비활성화되어 있는지. <c>true</c>면 배포 시 이 노드는 생성되지 않습니다.</param>
/// <example>
/// <code>
/// // 1) Function 노드 — 순차 처리(기본값), 자격증명 없음
/// var funcNode = new NodeConfig(
///     Id: "n1", Type: "function", Name: "온도 변환", FlowId: "f1",
///     Properties: new Dictionary&lt;string, object?&gt; { ["code"] = "return msg.payload * 1.8 + 32;" },
///     OutputDispatch: DispatchMode.Sequential,
///     MaxConcurrency: 1);
///
/// // 2) 알람 Fan-out 노드 — 여러 출력 와이어를 동시에 처리(병렬), 최대 동시 4건
/// var alarmNode = new NodeConfig(
///     Id: "n2", Type: "alarm-broadcast", Name: "알람 전파", FlowId: "f1",
///     Properties: new Dictionary&lt;string, object?&gt; { ["level"] = "HH" },
///     OutputDispatch: DispatchMode.Parallel,
///     MaxConcurrency: 4);
///
/// // 3) 자격증명이 필요한 MQTT Publish 노드 — CredentialRefId로 credentials.json 항목을 참조
/// var mqttNode = new NodeConfig(
///     Id: "n3", Type: "mqtt-publish", Name: "클라우드 전송", FlowId: "f1",
///     Properties: new Dictionary&lt;string, object?&gt; { ["topic"] = "plant/1/temp" },
///     CredentialRefId: "cred-mqtt-broker-1");
///
/// // Properties 값을 실제로 읽을 때는 JsonElement를 거쳐야 함(위 remarks 참고)
/// if (funcNode.Properties["code"] is System.Text.Json.JsonElement je)
/// {
///     string code = je.GetString() ?? string.Empty;
/// }
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
