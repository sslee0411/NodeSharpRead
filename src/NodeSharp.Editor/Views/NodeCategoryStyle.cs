using System.Windows.Media;

namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 노드 카테고리 스타일 카탈로그
/// 역활 및 기능 : INodeTypeDescriptor.Category 문자열로 캔버스·팔레트 카드의 테두리 색상/모서리 모양을 결정하는 정적 조회표
///
/// Node-RED가 팔레트 카테고리(input/output/function/network 등)마다 고정 색상을 쓰는 것과 동일한
/// 방식을 채택합니다 — 사용자가 노드 인스턴스마다 개별적으로 색상을 바꾸는 것이 아니라, 같은
/// Category에 속한 노드는 항상 같은 색상·모양으로 표시됩니다(EC-13, 사용자 요청 — "지금 있는
/// 노드와 앞으로 추가될 노드들의 색상과 모양을 변경할 수 있도록"에 대한 답으로, Category별 자동
/// 적용 방식을 선택). 색상은 테마(라이트/다크 등, ED-B4)와 무관하게 고정 색상입니다 — 카테고리
/// 색상은 "이 노드가 어떤 종류인가"를 나타내는 식별 정보라 테마가 바뀌어도 항상 같은 색으로
/// 보여야 알아보기 쉽다고 판단했습니다(테마별 라이트/다크 변형을 만들지 않음).
/// "모양"은 카드 테두리 모서리 반경(<see cref="CornerRadius"/>)으로 구분합니다 — Border 컨트롤이
/// 임의 도형(다이아몬드 등)을 지원하지 않아, 각진 사각형(0)·기본 둥근 사각형(4, 기존과 동일)·
/// 넉넉히 둥근 사각형(10)·캡슐형(카드 높이의 절반)의 4단계로 카테고리를 시각적으로 구분합니다.
/// 새 Category가 <see cref="Catalog"/>에 없으면(향후 Phase 13 NR-14~17이 추가할 parser/storage 등
/// 아직 목록에 없는 카테고리 포함) <see cref="Fallback"/>을 반환해 기존 화면과 동일하게 보입니다 —
/// 새 카테고리를 palette에 추가할 때마다 이 카탈로그에도 한 줄 추가하면 자동으로 색상이 입혀집니다.
/// 설계 근거: 03번 개발 Step맵 Phase 6 EC-13.
/// </summary>
public static class NodeCategoryStyle
{
    /// <summary>
    /// Category 문자열(대소문자 구분 없음) → 스타일 조회표입니다. INodeTypeDescriptor.Category가
    /// 현재 실제로 쓰는 값(2번 문서 2번 탭 카드1의 "input"|"output"|"function"|"network" 4종)과
    /// 02번 문서 9번 탭 카드15가 언급하는 Node-RED 표준 팔레트 그룹(Common/Sequence/Parser/Storage)을
    /// 미리 포함해, 해당 Phase(9~13)의 노드 타입이 나중에 추가돼도 코드 수정 없이 바로 색이 입혀지게
    /// 했습니다.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (Color Color, double CornerRadius)> Catalog =
        new Dictionary<string, (Color, double)>(StringComparer.OrdinalIgnoreCase)
        {
            // 파랑, 각진 사각형 — "여기서 흐름이 시작된다"는 인상(Inject 등 트리거형 노드).
            ["input"] = (Color.FromRgb(0x42, 0x85, 0xF4), 0),
            // 초록, 캡슐형(카드 높이의 절반) — "여기서 흐름이 끝난다"는 인상(Debug 등 출력형 노드).
            // 20 = FlowCanvasView.NodeCardHeight(40)의 절반을 상수로 고정(두 파일이 서로 참조하지
            // 않도록, 카드 높이가 바뀌면 이 값도 함께 조정 필요 — 낮은 결합도를 우선한 판단).
            ["output"] = (Color.FromRgb(0x34, 0xA8, 0x53), 20),
            // 주황, 기본 둥근 사각형(4 — EC-02 시점부터 쓰던 값과 동일해 기존 화면과 색만 달라짐).
            ["function"] = (Color.FromRgb(0xF2, 0x99, 0x00), 4),
            // 보라, 넉넉히 둥근 사각형 — 외부 시스템과의 통신을 시각적으로 "부드럽게 열린" 인상으로.
            ["network"] = (Color.FromRgb(0x9C, 0x27, 0xB0), 10),
            // 청록, 기본 둥근 사각형 — Split/Join/Sort/Batch 등 메시지 순서를 다루는 노드(Phase 13 NR-13).
            ["sequence"] = (Color.FromRgb(0x00, 0x96, 0x88), 4),
            // 회청, 기본 둥근 사각형 — CSV/HTML/JSON/XML/YAML 파싱 노드(Phase 13 NR-14).
            ["parser"] = (Color.FromRgb(0x60, 0x7D, 0x8B), 4),
            // 갈색, 기본 둥근 사각형 — 파일 읽기/쓰기/감시 등 저장소 접근 노드(Phase 13 NR-15).
            ["storage"] = (Color.FromRgb(0x79, 0x55, 0x48), 4),
            // 회색, 기본 둥근 사각형 — 여러 팔레트 그룹에 걸치는 공용 노드(Comment/Junction 등).
            ["common"] = (Color.FromRgb(0x9E, 0x9E, 0x9E), 4),
        };

    /// <summary>
    /// <see cref="Catalog"/>에 없는 Category일 때 반환하는 기본값 — <c>BorderBrush</c>는 <c>null</c>로
    /// 둬 호출부(<see cref="Views.FlowCanvasView"/>)가 기존 테마 리소스(<c>BorderBrush</c>)를 그대로
    /// 쓰도록 위임합니다(색상 미지정 = 기존 화면 그대로), 모서리 반경은 EC-02 시점부터 쓰던 기존 값
    /// 4를 그대로 유지합니다.
    /// </summary>
    private static readonly (Brush? BorderBrush, double CornerRadius) Fallback = (null, 4);

    /// <summary>
    /// <paramref name="category"/>(<c>INodeTypeDescriptor.Category</c>, 알 수 없는 타입이면 <c>null</c>)에
    /// 대응하는 스타일을 돌려줍니다. <see cref="Catalog"/>에 없으면 <see cref="Fallback"/>을 반환해
    /// 기존 화면과 똑같이 보이게 합니다(새 Category 추가는 안전하게 점진적으로 가능).
    /// </summary>
    public static (Brush? BorderBrush, double CornerRadius) Resolve(string? category)
    {
        if (category is null || !Catalog.TryGetValue(category, out var entry))
        {
            return Fallback;
        }

        return (new SolidColorBrush(entry.Color), entry.CornerRadius);
    }
}
