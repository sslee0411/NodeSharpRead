using NodeSharp.Contracts.Enums;

namespace NodeSharp.Contracts.Models;

/// <summary>
/// Class명 : 태그 런타임 정보
/// 역활 및 기능 : 헤드리스 Runner가 태그 하나를 다루는 데 필요한 모든 정보를 담은 순수 데이터 모델
///
/// 헤드리스 <c>NodeSharp.Runner</c>가 태그 하나를 다루는 데 필요한 모든 정보를 담은 순수
/// 데이터 레코드입니다. Editor 전용 <c>StructureTreeNode</c>(<c>ObservableCollection</c> 기반,
/// WPF 의존)를 Runner가 직접 참조하던 계층 위반을 고치기 위해 도입되었으며, <c>IStructureService</c>
/// (<c>CT-04b</c>)가 이 레코드로 구조 설정(장비→PLC→디바이스맵→태그) 데이터를 노드에 노출합니다.
/// <c>class</c>가 아니라 <c>record</c>라 WPF와 전혀 무관하고, Runtime·Editor 양쪽이 이 레코드만
/// 공유하므로 서로의 클래스는 참조하지 않습니다.
/// 설계 근거: 02번 문서 8번 탭 카드 7.
/// </summary>
/// <param name="Id">이 태그의 고유 식별자.</param>
/// <param name="Name">화면에 표시되는 태그 이름(예: "토출압력").</param>
/// <param name="ParentMapId">이 태그가 속한 디바이스맵의 Id — <c>IStructureService.GetTagsByMap</c>이 이 값으로 태그를 묶습니다.</param>
/// <param name="Offset">PLC 레지스터/버퍼 안에서 이 태그가 시작하는 오프셋(BufSchema 규약을 따름).</param>
/// <param name="BufType">원시 바이트를 엔지니어링 값으로 해석하는 방식(<see cref="BufFieldType"/>).</param>
/// <param name="Scale">Raw-Engineering 스케일 변환 계수. 스케일링이 필요 없는 태그는 <c>null</c>.</param>
/// <param name="Alarm">알람 임계값/비교값(HH/H/L/LL 4단계 + EQ/NE 2종). 알람이 설정되지 않은 태그는 <c>null</c>.</param>
/// <example>
/// <code>
/// // 1) 스케일링 + HH/H 알람이 설정된 태그(토출압력)
/// var pressureTag = new TagRuntimeInfo(
///     Id: "tag-1", Name: "토출압력", ParentMapId: "map-1",
///     Offset: 0, BufType: BufFieldType.FloatLE,
///     Scale: new ScaleRuntimeInfo(RawMin: 0, RawMax: 4095, EngMin: 0, EngMax: 10),
///     Alarm: new AlarmRuntimeInfo(HH: 9.5, H: 8.0, L: null, LL: null));
///
/// // 2) 스케일링·알람 모두 없는 단순 상태 태그(가동 여부 bool)
/// var runningTag = new TagRuntimeInfo(
///     Id: "tag-2", Name: "가동중", ParentMapId: "map-1",
///     Offset: 8, BufType: BufFieldType.Bool,
///     Scale: null, Alarm: null);
///
/// // 3) 특정값 일치/불일치 알람이 설정된 이산 상태 태그(설비 상태 코드, ★ 사용자 요청 v2.50 신설)
/// var statusTag = new TagRuntimeInfo(
///     Id: "tag-3", Name: "설비 상태코드", ParentMapId: "map-1",
///     Offset: 16, BufType: BufFieldType.Int16LE,
///     Scale: null,
///     Alarm: new AlarmRuntimeInfo(HH: null, H: null, L: null, LL: null, EQ: 3, NE: 1));
///
/// // Raw(rawValue) → Engineering 값 변환(선형 스케일) 후 HH 알람 판정
/// double rawValue = 3200;
/// var s = pressureTag.Scale!;
/// double eng = s.EngMin + (rawValue - s.RawMin) / (s.RawMax - s.RawMin) * (s.EngMax - s.EngMin);
/// bool isHH = pressureTag.Alarm?.HH is double hh &amp;&amp; eng >= hh;
///
/// // 특정값 일치(EQ)/불일치(NE) 알람 판정 — 상태코드가 3(고장)이면 EQ, 1(정상)이 아니면 NE
/// double statusValue = 3;
/// bool isEqAlarm = statusTag.Alarm?.EQ is double eq &amp;&amp; statusValue == eq;
/// bool isNeAlarm = statusTag.Alarm?.NE is double ne &amp;&amp; statusValue != ne;
/// </code>
/// </example>
public sealed record TagRuntimeInfo(
    string Id,
    string Name,
    string ParentMapId,
    int Offset,
    BufFieldType BufType,
    ScaleRuntimeInfo? Scale,
    AlarmRuntimeInfo? Alarm);

/// <summary>
/// Class명 : 스케일 런타임 정보
/// 역활 및 기능 : Raw ↔ Engineering 값 사이의 선형 변환 계수를 나타내는 모델
///
/// Raw(PLC에서 읽은 원시 값) ↔ Engineering(사람이 보는 실제 단위 값) 사이의 선형 변환 계수입니다.
/// </summary>
/// <param name="RawMin">원시 값의 최솟값(예: 0~4095 범위 아날로그 입력이면 0).</param>
/// <param name="RawMax">원시 값의 최댓값(예: 4095).</param>
/// <param name="EngMin"><see cref="RawMin"/>에 대응하는 엔지니어링 단위 값(예: 0.0bar).</param>
/// <param name="EngMax"><see cref="RawMax"/>에 대응하는 엔지니어링 단위 값(예: 10.0bar).</param>
public sealed record ScaleRuntimeInfo(double RawMin, double RawMax, double EngMin, double EngMax);

/// <summary>
/// Class명 : 알람 런타임 정보
/// 역활 및 기능 : 태그별 알람 임계값/비교값(HH/H/L/LL 4단계 + EQ/NE 2종)을 나타내는 모델
///
/// 8번 탭 <see cref="AlarmLevel"/>(HH/H/L/LL 4단계 + EQ/NE 2종)에 대응하는 태그별 알람
/// 임계값/비교값입니다. 각 항목은 값이 지정되지 않으면(<c>null</c>) 해당 단계의 알람 감시를
/// 하지 않습니다. HH/H/L/LL은 아날로그 태그(연속값)의 임계값 비교(<c>&gt;=</c>/<c>&lt;=</c>)에,
/// EQ/NE는 디지털/상태 태그(이산값)의 특정값 일치/불일치 비교(<c>==</c>/<c>!=</c>)에 씁니다
/// (★ 사용자 요청으로 v2.50 신설).
/// </summary>
/// <param name="HH">High-High 임계값. 미설정 시 <c>null</c>.</param>
/// <param name="H">High 임계값. 미설정 시 <c>null</c>.</param>
/// <param name="L">Low 임계값. 미설정 시 <c>null</c>.</param>
/// <param name="LL">Low-Low 임계값. 미설정 시 <c>null</c>.</param>
/// <param name="EQ">
/// (v2.50 신설) 특정값 일치 알람의 비교값 — 태그 값이 이 값과 같을 때(<see cref="AlarmLevel.EQ"/>)
/// 알람이 발생합니다. 미설정 시 <c>null</c>(EQ 알람 감시 안 함). 기존 <c>AlarmRuntimeInfo</c>
/// 생성 호출부가 전부 이름 있는 인수를 쓰고 있어(HH/H/L/LL 뒤에 추가된 선택 매개변수), 이 필드를
/// 추가해도 기존 코드는 수정 없이 그대로 컴파일됩니다.
/// </param>
/// <param name="NE">
/// (v2.50 신설) 특정값 불일치 알람의 비교값 — 태그 값이 이 값과 다를 때(그 값 이외의 모든 값,
/// <see cref="AlarmLevel.NE"/>) 알람이 발생합니다. 미설정 시 <c>null</c>(NE 알람 감시 안 함).
/// </param>
public sealed record AlarmRuntimeInfo(double? HH, double? H, double? L, double? LL, double? EQ = null, double? NE = null);
