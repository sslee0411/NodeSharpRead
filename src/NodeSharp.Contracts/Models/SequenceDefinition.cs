namespace NodeSharp.Contracts.Models;

/// <summary>
/// Flow 캔버스(<see cref="FlowDefinition"/>)와는 별개로 저장·편집되는 "단계형 시퀀스" 정의입니다.
/// 기동/정지 절차, 인터록(Interlock)처럼 순서·타임아웃·실패 분기가 엄격한 산업 절차를 표현하는 데
/// 사용하며, <c>sequences.json</c>으로 저장됩니다.
/// </summary>
/// <remarks>
/// <para>
/// 설계 근거: 02번 설계 문서 11번 탭 카드 5 "Sequence 설계 — 단계형 인터록/기동정지 시퀀스" — Node-RED
/// 스타일 Flow는 "이벤트가 오면 흘려보내는" 모델이라 엄격한 순서·타임아웃·실패 분기를 표현하기
/// 번거로워, <c>lssLib.Sequence</c>(<c>SequenceBuilderBase</c>/<c>SequenceStepBase</c>/
/// <c>ISequenceExecutor</c>) 기반의 별도 모델로 분리했습니다. 실제 실행은
/// <c>NodeSharp.Runtime/Sequence/SequenceExecutor.cs</c>(<c>SQ-01</c>)가 담당합니다.
/// </para>
/// <para>
/// <b>이 Step(<c>CT-03b</c>)의 범위</b>: 이 파일은 순수 데이터 모델만 정의합니다.
/// <c>SequenceExecutor</c>(실행 엔진)는 <c>SQ-01</c> 이후 별도 Step에서 구현합니다.
/// </para>
/// </remarks>
/// <param name="Id">이 시퀀스의 고유 식별자.</param>
/// <param name="Name">시퀀스 편집 창(11번 탭 카드 6)에 표시되는 이름(예: "1호기 펌프 기동 절차").</param>
/// <param name="Steps">순서대로 실행되는 단계 목록(<see cref="SequenceStepDto"/>).</param>
/// <param name="WatchedTagIds">
/// 이 태그들에서 HH 알람이 발생하면 진행 중인 시퀀스를 자동으로 안전정지시킵니다(<c>SequenceExecutor</c>가
/// <c>AlarmRaisedEvent</c>를 구독해 처리 — <c>SQ-01</c>). 기본값은 <see cref="Steps"/>의
/// <see cref="SequenceStepDto.ActionParams"/>에 등장하는 태그 전체입니다.
/// </param>
/// <example>
/// <code>
/// var seq = new SequenceDefinition(
///     Id: "seq-1", Name: "1호기 펌프 기동 절차",
///     Steps: new List&lt;SequenceStepDto&gt;
///     {
///         new(Order: 0, Name: "준비 확인", TriggerExpression: "true",
///             ActionType: "PlcReadStep", ActionParams: new Dictionary&lt;string, object?&gt;(),
///             TimeoutMs: 5000, OnFailStepId: null, OnTimeoutStepId: "step-safe-stop"),
///     },
///     WatchedTagIds: new List&lt;string&gt; { "tag-1" });
/// </code>
/// </example>
public sealed record SequenceDefinition(
    string Id,
    string Name,
    IReadOnlyList<SequenceStepDto> Steps,
    IReadOnlyList<string> WatchedTagIds);

/// <summary>
/// <see cref="SequenceDefinition"/> 안의 단계 하나입니다. 진입 조건(NCalc 표현식)·동작·타임아웃·
/// 실패/타임아웃 시 이동할 단계를 정의합니다.
/// </summary>
/// <param name="Order">이 단계의 실행 순서(0부터 시작).</param>
/// <param name="Name">시퀀스 편집 창의 세로 단계 목록에 표시되는 이름.</param>
/// <param name="TriggerExpression">
/// 이 단계 진입 조건을 나타내는 NCalc 표현식(6번 탭 NCalc 재사용). 예: <c>"prevStep.Done &amp;&amp; tag.Ready==true"</c>.
/// </param>
/// <param name="ActionType">
/// 이 단계의 동작 — <c>lssLib.Sequence.SequenceStepBase</c> 구현체의 타입명(예: <c>"PlcWriteStep"</c>).
/// </param>
/// <param name="ActionParams">이 동작 실행에 필요한 파라미터(키는 파라미터 이름, 값은 동작별로 다른 CLR 타입).</param>
/// <param name="TimeoutMs">이 단계의 제한 시간(밀리초). <c>0</c>이면 무제한. 초과 시 실패 처리 후 <see cref="OnTimeoutStepId"/>로 분기.</param>
/// <param name="OnFailStepId">이 단계가 실패했을 때 이동할 단계의 <see cref="SequenceStepDto.Name"/>(재시도·안전정지 등). 없으면 <c>null</c>.</param>
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
