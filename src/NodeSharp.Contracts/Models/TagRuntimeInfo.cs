using NodeSharp.Contracts.Enums;

namespace NodeSharp.Contracts.Models;

// 한글명: 태그 런타임 정보
/// <summary>
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
/// <param name="Alarm">4단계 알람 임계값(HH/H/L/LL). 알람이 설정되지 않은 태그는 <c>null</c>.</param>
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
/// // Raw(rawValue) → Engineering 값 변환(선형 스케일) 후 HH 알람 판정
/// double rawValue = 3200;
/// var s = pressureTag.Scale!;
/// double eng = s.EngMin + (rawValue - s.RawMin) / (s.RawMax - s.RawMin) * (s.EngMax - s.EngMin);
/// bool isHH = pressureTag.Alarm?.HH is double hh &amp;&amp; eng >= hh;
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

// 한글명: 스케일 런타임 정보
/// <summary>
/// Raw(PLC에서 읽은 원시 값) ↔ Engineering(사람이 보는 실제 단위 값) 사이의 선형 변환 계수입니다.
/// </summary>
/// <param name="RawMin">원시 값의 최솟값(예: 0~4095 범위 아날로그 입력이면 0).</param>
/// <param name="RawMax">원시 값의 최댓값(예: 4095).</param>
/// <param name="EngMin"><see cref="RawMin"/>에 대응하는 엔지니어링 단위 값(예: 0.0bar).</param>
/// <param name="EngMax"><see cref="RawMax"/>에 대응하는 엔지니어링 단위 값(예: 10.0bar).</param>
public sealed record ScaleRuntimeInfo(double RawMin, double RawMax, double EngMin, double EngMax);

// 한글명: 알람 런타임 정보
/// <summary>
/// 8번 탭 <see cref="AlarmLevel"/>(HH/H/L/LL) 4단계에 대응하는 태그별 알람 임계값입니다.
/// 각 단계는 값이 지정되지 않으면(<c>null</c>) 해당 단계의 알람 감시를 하지 않습니다.
/// </summary>
/// <param name="HH">High-High 임계값. 미설정 시 <c>null</c>.</param>
/// <param name="H">High 임계값. 미설정 시 <c>null</c>.</param>
/// <param name="L">Low 임계값. 미설정 시 <c>null</c>.</param>
/// <param name="LL">Low-Low 임계값. 미설정 시 <c>null</c>.</param>
public sealed record AlarmRuntimeInfo(double? HH, double? H, double? L, double? LL);
