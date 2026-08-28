using System.Windows.Media;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 노드 카테고리 스타일 카탈로그
/// 역활 및 기능 : INodeTypeDescriptor.Category 문자열로 캔버스 카드의 배경 채움색/테두리색/모서리
/// 반경을 결정하는 정적 조회표 — 실제 Node-RED 에디터의 "카테고리별 파스텔 색 채움 카드" 모양을
/// 재현합니다.
///
/// (v3.33, 사용자 요청 — "노드 모양을 기존 노드레드처럼 깔끔하게 이쁘게 변경") 기존 EC-13 설계는
/// 카테고리마다 테두리 색은 물론 모서리 반경(0/4/10/20 = 각진/기본 둥근/넉넉히 둥근/캡슐형)까지
/// 다르게 그려 카테고리를 "모양"으로도 구분했습니다. 그런데 실제 Node-RED 에디터는 모든 노드가
/// 항상 같은 작은 둥근 사각형(대략 4px 반경)이고, 카테고리 구분은 오직 "옅은 파스텔 배경 채움 +
/// 그보다 진한 테두리색"만으로 합니다 — 사용자가 원한 "기존 노드레드처럼"에 맞추기 위해 모서리
/// 반경을 모든 카테고리 공통 <see cref="UniformCornerRadius"/>(4)로 통일하고, 배경을 테두리색과
/// 같은 계열의 옅은 파스텔로 채워 실제 Node-RED와 동일한 "색으로 구분되는 카드" 느낌을 냈습니다
/// (EC-13의 모서리 반경 다양화 결정을 이번 요청으로 대체 — 카테고리 조회표 자체·"카테고리마다
/// 고정 색상"이라는 큰 방향은 그대로 유지, 근거는 이 클래스 및 FlowCanvasView.RenderNode 주석에
/// 기록). 캔버스에 배치된 카드에만 적용하고(팔레트 카드는 XAML DataTemplate 기반이라 이번 변경
/// 범위 밖으로 남겨둠 — 필요하면 후속 작업으로 별도 진행), 드래그 미리보기(OnCanvasDragEnter)에도
/// 동일하게 반영해 일관된 모양을 보장합니다.
/// 색상은 여전히 테마(라이트/다크, ED-B4)와 무관한 고정 색상입니다 — 카테고리 색상은 "이 노드가
/// 어떤 종류인가"를 나타내는 식별 정보라 테마가 바뀌어도 항상 같은 색으로 보여야 알아보기 쉽다고
/// 판단했습니다(EC-13 도입 시점부터 이어진 결정 유지). 배경이 항상 옅은 파스텔이라 라벨 텍스트도
/// <see cref="TextBrush"/>로 테마와 무관하게 고정된 짙은 회색을 씁니다 — 밝은 파스텔 배경 위에
/// (다크 테마의) 흰 글씨가 걸려 안 보이는 가독성 문제를 피하기 위함입니다.
/// 새 Category가 <see cref="Catalog"/>에 없으면(향후 Phase 13 NR-14~17 등) <see cref="Fallback"/>을
/// 반환해 기존 화면(테마 배경·테마 테두리·테마 텍스트)과 동일하게 보입니다 — 새 카테고리를 팔레트에
/// 추가할 때마다 이 카탈로그에도 한 줄 추가하면 자동으로 색이 입혀집니다.
/// (EC-18, ★ 사용자 요청 — "PLC 부분의 Node는 기존 노드레드와 다름") <c>PlcTagReadNodeType</c>/
/// <c>PlcTagWriteNodeType</c>이 실제로 쓰는 <c>Category</c> 값 <c>"structure"</c>가 이 카탈로그에
/// 없어(위 4종 + Phase 13 예약 4종만 있었음) v3.33 배경 채움 도입 이후 계속 <see cref="Fallback"/>로
/// 빠져 색이 입혀지지 않던 공백을 발견 — <c>"structure"</c> 항목을 신설해 메웠습니다(아래 Catalog
/// 참고, 강철색 계열로 PLC/하드웨어 통신이라는 정체성을 표현).
/// 설계 근거: 03번 개발 Step맵 Phase 6 EC-13(최초 도입) → v3.33(모서리 반경 통일 + 배경 파스텔
/// 채움 추가) → EC-18("structure" 카테고리 신설).
/// </summary>
public static class NodeCategoryStyle
{
    /// <summary>실제 Node-RED와 동일하게, 모든 카테고리가 공유하는 단일 모서리 반경(EC-13의 0/4/10/20 구분을 대체).</summary>
    private const double UniformCornerRadius = 4;

    /// <summary>옅은 파스텔 배경 위에서도 테마와 무관하게 항상 또렷하게 읽히는 고정 짙은 회색 라벨 텍스트색.</summary>
    public static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26));

    /// <summary>
    /// Category 문자열(대소문자 구분 없음) → (배경 파스텔, 테두리 진한색) 조회표입니다.
    /// INodeTypeDescriptor.Category가 현재 실제로 쓰는 값(2번 문서 2번 탭 카드1의
    /// "input"|"output"|"function"|"network" 4종)과 02번 문서 9번 탭 카드15가 언급하는 Node-RED
    /// 표준 팔레트 그룹(Common/Sequence/Parser/Storage)을 미리 포함해, 해당 Phase(9~13)의 노드
    /// 타입이 나중에 추가돼도 코드 수정 없이 바로 색이 입혀지게 했습니다.
    /// </summary>
    // (EC-20, ★ 사용자 요청 — "카드 색상은 색상 파레트에서 선택") private → public 접근 제한자만
    // 바꿨다(그 외 이름·값·순서·Resolve/Fallback 로직 전부 그대로) — ColorPickerDialog가 "카테고리
    // 기본 색상" 스와치를 이 카탈로그에서 직접 읽어 보여줄 수 있도록, 색상값을 ColorPickerDialog에
    // 따로 하드코딩해 두 곳의 값이 어긋날 위험을 만들지 않기 위함이다. IReadOnlyDictionary 타입이라
    // 외부에서 Add/Remove로 변경할 방법은 없다(읽기 전용 유지).
    public static readonly IReadOnlyDictionary<string, (Color Fill, Color Border)> Catalog =
        new Dictionary<string, (Color, Color)>(StringComparer.OrdinalIgnoreCase)
        {
            // 코럴 핑크 — "여기서 흐름이 시작된다"는 인상(Inject 등 트리거형 노드).
            ["input"] = (Color.FromRgb(0xF7, 0xD9, 0xD9), Color.FromRgb(0xD9, 0x8C, 0x8C)),
            // 연두 — "여기서 흐름이 끝난다"는 인상(Debug 등 출력형 노드).
            ["output"] = (Color.FromRgb(0xD7, 0xF0, 0xD7), Color.FromRgb(0x6F, 0xAE, 0x6F)),
            // 호박색(amber) — Node-RED function 계열 노드의 상징색과 같은 색 계열.
            ["function"] = (Color.FromRgb(0xFC, 0xE2, 0xB8), Color.FromRgb(0xE0, 0xA9, 0x4D)),
            // 라벤더 — 외부 시스템과의 통신(network)을 시각적으로 "부드럽게 열린" 인상으로.
            ["network"] = (Color.FromRgb(0xE7, 0xD5, 0xF5), Color.FromRgb(0xB3, 0x7D, 0xD9)),
            // 청록 — Split/Join/Sort/Batch 등 메시지 순서를 다루는 노드(Phase 13 NR-13).
            ["sequence"] = (Color.FromRgb(0xC9, 0xEF, 0xEA), Color.FromRgb(0x4F, 0xA8, 0x9E)),
            // 청회색 — CSV/HTML/JSON/XML/YAML 파싱 노드(Phase 13 NR-14).
            ["parser"] = (Color.FromRgb(0xDC, 0xE3, 0xE8), Color.FromRgb(0x8C, 0xA0, 0xAA)),
            // 갈색 — 파일 읽기/쓰기/감시 등 저장소 접근 노드(Phase 13 NR-15).
            ["storage"] = (Color.FromRgb(0xE8, 0xD7, 0xC9), Color.FromRgb(0xA9, 0x81, 0x6D)),
            // 중립 회색 — 여러 팔레트 그룹에 걸치는 공용 노드(Comment/Junction 등).
            ["common"] = (Color.FromRgb(0xE6, 0xE6, 0xE6), Color.FromRgb(0xA0, 0xA0, 0xA0)),
            // (EC-18) 강철색(steel blue) — PLC/하드웨어 통신 노드(PlcTagRead/PlcTagWrite). 실제
            // Node-RED에는 없는 이 프로젝트 고유 카테고리라, "회로·하드웨어" 인상을 주는 파란-회색
            // 계열을 새로 부여해 network(라벤더)/parser(청회색)와 뚜렷이 구분되게 했다.
            ["structure"] = (Color.FromRgb(0xD2, 0xE3, 0xF3), Color.FromRgb(0x4F, 0x86, 0xB8)),
        };

    /// <summary>
    /// <see cref="Catalog"/>에 없는 Category일 때 반환하는 기본값 — Fill/BorderBrush 모두 <c>null</c>로
    /// 둬 호출부(<see cref="Views.FlowCanvasView"/>)가 기존 테마 리소스(Background/BorderBrush/텍스트색)를
    /// 그대로 쓰도록 위임합니다(색상 미지정 = 기존 화면 그대로), 모서리 반경은 다른 카테고리와 동일하게
    /// <see cref="UniformCornerRadius"/>를 그대로 씁니다(EC-13 시점의 "기존 값 4 유지"와 결과적으로 같음).
    /// </summary>
    private static readonly (Brush? Fill, Brush? BorderBrush) Fallback = (null, null);

    /// <summary>
    /// <paramref name="category"/>(<c>INodeTypeDescriptor.Category</c>, 알 수 없는 타입이면 <c>null</c>)에
    /// 대응하는 스타일을 돌려줍니다. <see cref="Catalog"/>에 없으면 <see cref="Fallback"/>을 반환해
    /// 기존 화면과 똑같이 보이게 합니다(새 Category 추가는 안전하게 점진적으로 가능). 모서리 반경은
    /// 항상 <see cref="UniformCornerRadius"/>로 고정입니다(실제 Node-RED와 동일 — 클래스 주석 참고).
    /// </summary>
    public static (Brush? Fill, Brush? BorderBrush, double CornerRadius) Resolve(string? category)
    {
        if (category is null || !Catalog.TryGetValue(category, out var entry))
        {
            return (Fallback.Fill, Fallback.BorderBrush, UniformCornerRadius);
        }

        return (new SolidColorBrush(entry.Fill), new SolidColorBrush(entry.Border), UniformCornerRadius);
    }
}
