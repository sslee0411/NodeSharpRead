using System.Windows.Media;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : Font Awesome 아이콘 카탈로그
/// 역활 및 기능 : 번들된 Font Awesome Free Solid 웹폰트의 FontFamily 선언과, 아이콘 선택기
/// (<see cref="IconPickerDialog"/>)가 보여줄 산업/플로우 관련 curated 아이콘 목록
///
/// (EC-20, ★ 사용자 요청 — "아이콘의 경우 웹에 공유되어 있는 아이콘을 선택해서 할 수 있도록")
/// EC-19까지는 아이콘을 이모지 등 문자를 직접 타이핑해서만 지정할 수 있었는데, 실제 Node-RED가
/// 아이콘 목록에서 고르는 것처럼(비록 실제로는 Node-RED도 대부분 노드 제작자가 고정한 아이콘을
/// 쓰지만, 인스턴스 오버라이드가 가능한 노드는 미리 정의된 아이콘 세트에서 선택하는 방식) 화면에서
/// 클릭으로 고를 수 있게 하기 위해, 웹에 공개된 아이콘 폰트인 Font Awesome Free(SIL OFL 1.1
/// 라이선스, <c>Assets\Fonts\FontAwesome-LICENSE.txt</c> 참고 — 재배포 허용)의 Solid 스타일
/// 웹폰트(<c>fa-solid-900.ttf</c>)를 <c>NodeSharp.Editor.csproj</c>에 WPF <c>Resource</c>로 번들했다.
/// (EC-21, ★ 사용자 요청 — EC-20 직후 "아이콘을 보다 더 많이 추가 할 수 없을까?") 최초 51종은
/// 통신·저장·계측·경고·자동화 위주로만 골라 화살표/수식/시간/사용자·보안/파일/미디어/UI 상태/날씨·
/// 환경/운송·물류/도형/통신 확장/기타 자동화 카테고리가 비어 있었음 — 같은 Font Awesome Free
/// 6.5.2 <c>metadata/icons.yml</c>에서 이번엔 그 빈 카테고리들 위주로 130종을 추가 curated해
/// 총 <see cref="Icons"/> 181종으로 확장했다(기존 51종은 이름·순서·값 전혀 변경 없이 그대로 유지,
/// 새 130종만 그 뒤에 추가 — 이미 저장된 flows.json의 icon 값·기존 아이콘을 쓰던 카드 모두 영향
/// 없음). 전체 아이콘(2,000종 이상)을 다 넣는 대신 이번에도 curated 방식을 유지한 이유는
/// <see cref="IconPickerDialog"/>의 검색 없이 스크롤만으로 찾기엔 전체 세트가 여전히 너무 많고,
/// curated 목록은 각 아이콘에 이 프로젝트 맥락에 맞는 한글 <see cref="IconEntry.Label"/>을 붙일 수
/// 있어 검색 품질이 더 높기 때문이다(끝없이 늘어나는 요청이 이어지면 그때 "전체 세트 + 영문 라벨"
/// 방식으로 전환하는 것도 후속 검토 대상).
/// </summary>
/// <remarks>
/// <b>왜 이 폰트만(Regular/Brands 제외)</b>: Solid 스타일 하나만으로도 이 프로젝트가 필요로 하는
/// 산업/플로우 관련 아이콘(공장·PLC·통신·타이머·경고 등)을 충분히 커버하고, 3종 폰트를 전부
/// 번들하면 앱 크기가 불필요하게 커진다(Solid 1개 약 420KB, Regular+Brands까지 더하면 약 700KB
/// 추가) — 필요해지면 후속 작업으로 <see cref="FontFamily"/>와 같은 패턴으로 추가 가능.
/// <b>글자 코드로 직접 타이핑한 이모지와의 공존</b>: <see cref="FontFamily"/>는 이 카탈로그에서 고른
/// 아이콘(Private Use Area 문자)에만 명시적으로 적용된다(<c>FlowCanvasView.RenderNode</c>가 아이콘
/// 부분만 별도 <c>Run</c>으로 감싸 이 폰트를 지정) — 사용자가 이전처럼 이모지를 직접 타이핑해도
/// (EC-19) 그 Run 안에서 WPF가 표준 글리프 폴백으로 이모지 폰트를 자동으로 찾아 그대로 표시하므로
/// 두 방식이 서로 충돌하지 않는다.
/// </remarks>
public static class FontAwesomeIconCatalog
{
    /// <summary>
    /// 번들된 Font Awesome 6 Free Solid 웹폰트를 가리키는 <see cref="FontFamily"/>입니다. WPF 표준
    /// 패턴(상대 경로는 폰트 "파일"이 아니라 그 폰트가 들어있는 "폴더"를 가리키고, <c>#</c> 뒤에
    /// 폰트 내부 이름을 붙임 — <c>fa-solid-900.ttf</c>의 내부 이름은 <c>"Font Awesome 6 Free
    /// Solid"</c>, <c>python fontTools</c>로 name 테이블을 직접 읽어 확인)으로 참조합니다.
    /// </summary>
    public static readonly FontFamily FontFamily =
        new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Font Awesome 6 Free Solid");

    /// <summary>
    /// 아이콘 선택기 카드 하나 — <paramref name="Name"/>은 Font Awesome 공식 아이콘 이름(영문,
    /// 검색용), <paramref name="Label"/>은 화면에 보여줄 한글 설명(용도 힌트 포함), <paramref name="Glyph"/>는
    /// 실제로 <see cref="NodeConfig.Properties"/>의 <c>"icon"</c> 값으로 저장될 1글자 문자열입니다
    /// (Font Awesome의 Private Use Area 코드포인트 — 이 폰트가 없는 화면에서는 빈 사각형으로 보일 수
    /// 있어 항상 <see cref="FontFamily"/>와 함께 렌더링해야 합니다).
    /// </summary>
    public sealed record IconEntry(string Name, string Label, string Glyph);

    /// <summary>
    /// 산업/IIoT 플로우 편집기에 어울리는 Font Awesome Solid 아이콘 181종을 curated한 목록입니다 —
    /// 전체 아이콘(2,000종 이상)을 다 보여주면 검색 없이는 원하는 아이콘을 찾기 어려워, 통신·저장·
    /// 계측·경고·자동화 등 이 프로젝트의 노드 타입들과 관련 있는 이름 위주로 골랐습니다(선정 근거는
    /// 이 세션의 조사 과정 — Font Awesome Free 6.5.2의 <c>metadata/icons.yml</c>에서 관련 검색어로
    /// 필터링). (EC-21) 최초 51종(위 클래스 주석 EC-20 항목) 이후, 사용자 요청으로 화살표/이동·수식/
    /// 논리·시간/일정·사용자/보안·파일/문서·미디어·UI/상태·날씨/환경·도구/하드웨어·운송/물류·도형/
    /// 표시·통신 확장·자동화/기타 13개 카테고리에서 130종을 추가해 총 181종이 됐습니다(카테고리 경계는
    /// 배열 순서로만 구분되고 별도 그룹 필드는 없음 — 목록이 더 커지면 후속 검토 대상).
    /// <see cref="IconPickerDialog"/>의 검색창은 <see cref="IconEntry.Name"/>/<see cref="IconEntry.Label"/>
    /// 양쪽 다 대상으로 합니다.
    /// </summary>
    public static readonly IReadOnlyList<IconEntry> Icons = new IconEntry[]
    {
        new("bolt", "번개(트리거/전원)", "\uf0e7"),
        new("gear", "기어(설정/기능)", "\uf013"),
        new("industry", "공장(PLC/산업)", "\uf275"),
        new("plug", "플러그(연결/통신)", "\uf1e6"),
        new("wifi", "와이파이(무선)", "\uf1eb"),
        new("satellite", "위성(원격)", "\uf7bf"),
        new("tower-broadcast", "송신탑(방송/신호)", "\uf519"),
        new("signal", "신호 세기", "\uf012"),
        new("wave-square", "파형(신호)", "\uf83e"),
        new("database", "데이터베이스(저장소)", "\uf1c0"),
        new("download", "다운로드(읽기)", "\uf019"),
        new("upload", "업로드(쓰기)", "\uf093"),
        new("clock", "시계(타이머/Inject)", "\uf017"),
        new("code", "코드(Function)", "\uf121"),
        new("bug", "벌레(Debug)", "\uf188"),
        new("triangle-exclamation", "경고 삼각형", "\uf071"),
        new("circle-check", "확인(성공)", "\uf058"),
        new("filter", "필터", "\uf0b0"),
        new("shuffle", "셔플(분기/Switch)", "\uf074"),
        new("code-branch", "코드 분기", "\uf126"),
        new("right-left", "양방향 화살표", "\uf362"),
        new("gauge", "계기판", "\uf624"),
        new("chart-line", "추세 차트", "\uf201"),
        new("chart-bar", "막대 차트", "\uf080"),
        new("folder", "폴더", "\uf07b"),
        new("file", "파일", "\uf15b"),
        new("terminal", "터미널", "\uf120"),
        new("microchip", "칩(PLC/하드웨어)", "\uf2db"),
        new("server", "서버", "\uf233"),
        new("cloud", "클라우드", "\uf0c2"),
        new("envelope", "봉투(알림/메일)", "\uf0e0"),
        new("bell", "종(알람)", "\uf0f3"),
        new("lock", "자물쇠(보안)", "\uf023"),
        new("key", "열쇠(인증)", "\uf084"),
        new("map", "지도(경로)", "\uf279"),
        new("sitemap", "구조도", "\uf0e8"),
        new("diagram-project", "다이어그램", "\uf542"),
        new("power-off", "전원", "\uf011"),
        new("toggle-on", "스위치 켜짐", "\uf205"),
        new("sliders", "슬라이더(조정)", "\uf1de"),
        new("thermometer", "온도계", "\uf491"),
        new("water", "물(유량)", "\uf773"),
        new("fire", "불(경보)", "\uf06d"),
        new("battery-full", "배터리", "\uf240"),
        new("link", "연결 고리", "\uf0c1"),
        new("tag", "태그", "\uf02b"),
        new("tags", "태그 여러 개", "\uf02c"),
        new("table", "표", "\uf0ce"),
        new("robot", "로봇(자동화)", "\uf544"),
        new("hard-drive", "하드디스크", "\uf0a0"),
        new("wrench", "렌치(도구)", "\uf0ad"),

        // (EC-21, ★ 사용자 요청 — "아이콘을 보다 더 많이 추가 할 수 없을까?") 아래부터는 최초 51종
        // 이후 추가된 130종 — 카테고리별로 묶어 순서대로 나열(화살표/이동 → 수식/논리 → 시간/일정 →
        // 사용자/보안 → 파일/문서 → 미디어 → UI/상태 → 날씨/환경 → 도구/하드웨어 → 운송/물류 →
        // 도형/표시 → 통신 확장 → 자동화/기타), 위 51종은 이름·순서·값 전혀 변경 없음(위 클래스
        // 주석 EC-21 항목 참고).
        new("arrow-up", "위쪽 화살표", "\uf062"),
        new("arrow-down", "아래쪽 화살표", "\uf063"),
        new("arrow-left", "왼쪽 화살표", "\uf060"),
        new("arrow-right", "오른쪽 화살표", "\uf061"),
        new("arrow-up-right-from-square", "외부 링크(새 창)", "\uf08e"),
        new("rotate", "회전(새로고침)", "\uf2f1"),
        new("rotate-left", "왼쪽 회전(실행취소)", "\uf2ea"),
        new("rotate-right", "오른쪽 회전(다시실행)", "\uf2f9"),
        new("repeat", "반복(Loop)", "\uf363"),
        new("arrows-rotate", "동기화(새로고침)", "\uf021"),
        new("compress", "축소", "\uf066"),
        new("expand", "확대", "\uf065"),
        new("up-down", "상하 이동", "\uf338"),
        new("up-down-left-right", "이동(드래그)", "\uf0b2"),
        new("circle-arrow-up", "원형 위 화살표", "\uf0aa"),
        new("circle-arrow-down", "원형 아래 화살표", "\uf0ab"),
        new("plus", "더하기", "\u002b"),
        new("minus", "빼기", "\uf068"),
        new("xmark", "닫기(X)", "\uf00d"),
        new("equals", "같음", "\u003d"),
        new("percent", "퍼센트", "\u0025"),
        new("divide", "나누기", "\uf529"),
        new("square-root-variable", "제곱근(수식)", "\uf698"),
        new("calculator", "계산기", "\uf1ec"),
        new("less-than", "작음(<)", "\u003c"),
        new("greater-than", "큼(>)", "\u003e"),
        new("code-compare", "코드 비교", "\ue13a"),
        new("calendar", "달력", "\uf133"),
        new("calendar-days", "일정(날짜)", "\uf073"),
        new("hourglass", "모래시계(대기)", "\uf254"),
        new("hourglass-half", "모래시계 절반(진행 중)", "\uf252"),
        new("stopwatch", "스톱워치(측정)", "\uf2f2"),
        new("clock-rotate-left", "이력(히스토리)", "\uf1da"),
        new("user", "사용자", "\uf007"),
        new("users", "사용자 그룹", "\uf0c0"),
        new("user-gear", "사용자 설정", "\uf4fe"),
        new("user-lock", "사용자 잠금(권한)", "\uf502"),
        new("shield", "방패(보호)", "\uf132"),
        new("shield-halved", "보안(방패 분할)", "\uf3ed"),
        new("fingerprint", "지문(인증)", "\uf577"),
        new("id-badge", "신분증(배지)", "\uf2c1"),
        new("id-card", "신분증 카드", "\uf2c2"),
        new("file-lines", "텍스트 파일", "\uf15c"),
        new("file-code", "코드 파일", "\uf1c9"),
        new("file-csv", "CSV 파일", "\uf6dd"),
        new("file-export", "내보내기", "\uf56e"),
        new("file-import", "가져오기", "\uf56f"),
        new("folder-open", "열린 폴더", "\uf07c"),
        new("floppy-disk", "저장(디스켓)", "\uf0c7"),
        new("copy", "복사", "\uf0c5"),
        new("paste", "붙여넣기", "\uf0ea"),
        new("clipboard", "클립보드", "\uf328"),
        new("clipboard-list", "체크리스트", "\uf46d"),
        new("play", "재생", "\uf04b"),
        new("pause", "일시정지", "\uf04c"),
        new("stop", "정지", "\uf04d"),
        new("forward", "빨리감기", "\uf04e"),
        new("backward", "되감기", "\uf04a"),
        new("volume-high", "소리 켜짐", "\uf028"),
        new("volume-xmark", "음소거", "\uf6a9"),
        new("microphone", "마이크", "\uf130"),
        new("camera", "카메라", "\uf030"),
        new("video", "비디오", "\uf03d"),
        new("image", "이미지", "\uf03e"),
        new("toggle-off", "스위치 꺼짐", "\uf204"),
        new("check", "체크(완료)", "\uf00c"),
        new("circle-xmark", "원형 닫기(오류)", "\uf057"),
        new("circle-info", "정보", "\uf05a"),
        new("circle-question", "물음(도움말)", "\uf059"),
        new("circle-exclamation", "원형 경고", "\uf06a"),
        new("list", "목록", "\uf03a"),
        new("list-check", "체크리스트(목록)", "\uf0ae"),
        new("magnifying-glass", "돋보기(검색)", "\uf002"),
        new("magnifying-glass-plus", "확대 검색", "\uf00e"),
        new("filter-circle-xmark", "필터 해제", "\ue17b"),
        new("sun", "해(주간)", "\uf185"),
        new("moon", "달(야간)", "\uf186"),
        new("cloud-rain", "비(강우)", "\uf73d"),
        new("cloud-bolt", "뇌우", "\uf76c"),
        new("snowflake", "눈(저온)", "\uf2dc"),
        new("wind", "바람", "\uf72e"),
        new("droplet", "물방울(유량)", "\uf043"),
        new("temperature-half", "온도(절반)", "\uf2c9"),
        new("leaf", "친환경", "\uf06c"),
        new("recycle", "재활용", "\uf1b8"),
        new("screwdriver-wrench", "드라이버+렌치(정비)", "\uf7d9"),
        new("hammer", "망치(작업)", "\uf6e3"),
        new("toolbox", "공구함", "\uf552"),
        new("bolt-lightning", "강한 전압", "\ue0b7"),
        new("plug-circle-bolt", "플러그 충전", "\ue55b"),
        new("ethernet", "이더넷", "\uf796"),
        new("sim-card", "SIM 카드", "\uf7c4"),
        new("memory", "메모리(RAM)", "\uf538"),
        new("compact-disc", "디스크(미디어)", "\uf51f"),
        new("network-wired", "유선 네트워크", "\uf6ff"),
        new("gears", "여러 기어(복합 설정)", "\uf085"),
        new("truck", "트럭(운송)", "\uf0d1"),
        new("ship", "선박", "\uf21a"),
        new("plane", "항공기", "\uf072"),
        new("train", "기차", "\uf238"),
        new("warehouse", "창고", "\uf494"),
        new("dolly", "운반 카트", "\uf472"),
        new("box", "상자", "\uf466"),
        new("boxes-stacked", "적재 상자", "\uf468"),
        new("pallet", "팔레트", "\uf482"),
        new("circle", "원(상태 표시)", "\uf111"),
        new("square", "사각형", "\uf0c8"),
        new("star", "별(즐겨찾기)", "\uf005"),
        new("flag", "깃발(표시)", "\uf024"),
        new("thumbtack", "핀 고정", "\uf08d"),
        new("location-dot", "위치 핀", "\uf3c5"),
        new("compass", "나침반(방향)", "\uf14e"),
        new("crosshairs", "조준(타겟)", "\uf05b"),
        new("comment", "말풍선(댓글)", "\uf075"),
        new("comments", "대화(여러 개)", "\uf086"),
        new("phone", "전화", "\uf095"),
        new("envelope-open", "열린 봉투(메일 확인)", "\uf2b6"),
        new("paper-plane", "종이비행기(전송)", "\uf1d8"),
        new("share-nodes", "공유(네트워크)", "\uf1e0"),
        new("rss", "RSS(구독)", "\uf09e"),
        new("satellite-dish", "위성 안테나(수신)", "\uf7c0"),
        new("infinity", "무한 반복", "\uf534"),
        new("wand-magic-sparkles", "자동화(마법)", "\ue2ca"),
        new("brain", "AI/지능", "\uf5dc"),
        new("plug-circle-check", "플러그 확인(연결 성공)", "\ue55c"),
        new("chart-pie", "원형 차트", "\uf200"),
        new("chart-simple", "간단 차트", "\ue473"),
        new("table-cells", "표(셀)", "\uf00a"),
        new("layer-group", "레이어(계층)", "\uf5fd"),
        new("boxes-packing", "포장(박스 정리)", "\ue4c7"),
    };
}
