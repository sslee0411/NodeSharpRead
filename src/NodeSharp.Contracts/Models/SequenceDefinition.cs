namespace NodeSharp.Contracts.Models;

/// <summary>
/// Class명 : 시퀀스 정의
/// 역활 및 기능 : Flow 캔버스와 별개로 저장·편집되는 단계형 시퀀스 정의
///
/// Flow 캔버스(<see cref="FlowDefinition"/>)와는 별개로 저장·편집되는 "단계형 시퀀스" 정의입니다.
/// Node-RED 스타일 Flow는 "이벤트가 오면 흘려보내는" 모델이라 순서·타임아웃·실패 분기가 엄격한
/// 기동/정지 절차, 인터록(Interlock) 같은 산업 시퀀스를 표현하기 번거로워 별도 모델로 분리했습니다.
/// <c>sequences.json</c>으로 저장되며, 실제 실행은 <c>SequenceExecutor</c>(<c>SQ-01</c>)가 담당합니다.
/// 설계 근거: 02번 문서 11번 탭 카드 5.
/// </summary>
/// <param name="Id">이 시퀀스의 고유 식별자.</param>
/// <param name="Name">시퀀스 편집 창에 표시되는 이름(예: "1호기 펌프 기동 절차").</param>
/// <param name="Steps">순서대로 실행되는 단계 목록.</param>
/// <param name="WatchedTagIds">이 태그들에서 HH 알람이 발생하면 진행 중인 시퀀스를 자동으로 안전정지시킵니다(<c>SequenceExecutor</c>가 <c>AlarmRaisedEvent</c>를 구독해 처리).</param>
/// <example>
/// <code>
/// // 2단계 펌프 기동 절차: 밸브 열기 → (조건 충족 시) 펌프 기동, 실패/타임아웃 시 안전정지 단계로 분기
/// var openValve = new SequenceStepDto(
///     Order: 0, Name: "밸브 열기",
///     TriggerExpression: "true", // 시퀀스 시작 즉시 진입
///     ActionType: "PlcWriteStep",
///     ActionParams: new Dictionary&lt;string, object?&gt; { ["tagId"] = "tag-valve1", ["value"] = true },
///     TimeoutMs: 5000,
///     OnFailStepId: "safe-stop", OnTimeoutStepId: "safe-stop");
///
/// var startPump = new SequenceStepDto(
///     Order: 1, Name: "펌프 기동",
///     TriggerExpression: "tag-valve1 == true", // 밸브가 열린 것을 확인한 뒤에만 진입
///     ActionType: "PlcWriteStep",
///     ActionParams: new Dictionary&lt;string, object?&gt; { ["tagId"] = "tag-pump1", ["value"] = true },
///     TimeoutMs: 3000,
///     OnFailStepId: "safe-stop");
///
/// var seq = new SequenceDefinition(
///     Id: "seq-1", Name: "1호기 펌프 기동 절차",
///     Steps: new List&lt;SequenceStepDto&gt; { openValve, startPump },
///     WatchedTagIds: new List&lt;string&gt; { "tag-1" }); // tag-1(토출압력)에서 HH 발생 시 진행 중이어도 자동 안전정지
/// </code>
/// </example>
public sealed record SequenceDefinition(
    string Id,
    string Name,
    IReadOnlyList<SequenceStepDto> Steps,
    IReadOnlyList<string> WatchedTagIds);

/// <summary>
/// Class명 : 시퀀스 단계
/// 역활 및 기능 : SequenceDefinition 안의 단계 하나(진입 조건·동작·타임아웃·실패 시 이동)를 정의하는 모델
///
/// <see cref="SequenceDefinition"/> 안의 단계 하나입니다. 진입 조건(NCalc 표현식)·동작·타임아웃·
/// 실패/타임아웃 시 이동할 단계를 정의합니다.
/// </summary>
/// <param name="Order">이 단계의 실행 순서(0부터 시작).</param>
/// <param name="Name">시퀀스 편집 창의 세로 단계 목록에 표시되는 이름.</param>
/// <param name="TriggerExpression">이 단계 진입 조건을 나타내는 NCalc 표현식(6번 탭 NCalc 재사용).</param>
/// <param name="ActionType">이 단계의 동작 — <c>lssLib.Sequence.SequenceStepBase</c> 구현체의 타입명(예: <c>"PlcWriteStep"</c>).</param>
/// <param name="ActionParams">이 동작 실행에 필요한 파라미터(키는 파라미터 이름, 값은 동작별로 다른 CLR 타입).</param>
/// <param name="TimeoutMs">이 단계의 제한 시간(밀리초). <c>0</c>이면 무제한. 초과 시 실패 처리 후 <see cref="OnTimeoutStepId"/>로 분기.</param>
/// <param name="OnFailStepId">이 단계가 실패했을 때 이동할 단계의 이름(재시도·안전정지 등). 없으면 <c>null</c>.</param>
/// <param name="OnTimeoutStepId">이 단계가 <see cref="TimeoutMs"/>를 초과했을 때 이동할 단계. 없으면 <c>null</c>.</param>
public sealed record SequenceStepDto(
    int Order,
    string Name,
    string TriggerExpression,
    string ActionType,
    IReadOnlyDictionary<string, object?> ActionParams,
    int TimeoutMs = 0,
    string? OnFailStepId = null,
    string? OnTimeoutStepId = null);
