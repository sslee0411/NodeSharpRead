namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 팔레트 노드 카드 표시 모델
/// 역활 및 기능 : PaletteView의 ItemsControl에 바인딩되는, 노드 타입 하나를 표시하기 위한 최소 정보
///
/// <c>INodeTypeDescriptor</c> 전체를 그대로 바인딩하지 않고 팔레트 카드 표시에 필요한 3개 값
/// (TypeName/Category/IconGlyph)만 뽑아 담습니다 — INodeTypeDescriptor에는 팔레트에 표시할 별도의
/// "표시 이름(DisplayName)" 필드가 없어(TypeName만 존재, 02번 문서 2번 탭 카드1), 검색·표시 모두
/// TypeName을 기준으로 합니다.
/// </summary>
public sealed class PaletteNodeCardViewModel
{
    /// <summary>표시할 카드 하나의 값을 그대로 담습니다(추가 계산 없음).</summary>
    public PaletteNodeCardViewModel(string typeName, string category, string iconGlyph)
    {
        TypeName = typeName;
        Category = category;
        IconGlyph = iconGlyph;
    }

    /// <summary>노드 타입 이름(예: "function"). 검색·최근 사용 추적 키로도 그대로 씁니다.</summary>
    public string TypeName { get; }

    /// <summary>팔레트에서 이 노드가 속하는 분류(예: "input"/"function").</summary>
    public string Category { get; }

    /// <summary>카드에 표시할 아이콘 글리프 이름.</summary>
    public string IconGlyph { get; }
}
