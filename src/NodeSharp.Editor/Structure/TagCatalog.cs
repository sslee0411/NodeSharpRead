namespace NodeSharp.Editor.Structure;

/// <summary>
/// Class명 : 태그 카탈로그
/// 역활 및 기능 : 현재 구조 설정 트리(<see cref="StructureView"/>.Devices)에 있는 모든 태그(TagNode)를
/// "Id + 표시 경로" 형태로 평탄화해 캔버스 쪽(NodePropertyDialog)이 참조할 수 있게 공유하는 정적 홀더
///
/// (ED-D04) 캔버스 노드의 <see cref="Contracts.Enums.PropertyFieldType.TagRef"/> 필드가 실제 태그
/// 선택 목록을 채우려면 구조 설정 트리(<see cref="StructureView"/> 소유)의 데이터가 필요한데, 두
/// View(FlowCanvasView/StructureView)는 <c>MainWindow.xaml</c>에 이름만 붙여 나란히 배치된 독립
/// UserControl 인스턴스라 생성자 주입 경로가 없습니다(EC-03의 <c>NodePropertyDialog</c>도
/// FlowCanvasView가 직접 <c>new</c>로 만듭니다). <c>NodeSharp.Runner.CurrentEngineHolder</c>(현재
/// 배포된 FlowEngine을 정적 필드로 공유해 MonitorHub가 참조하는 것)와 동일한 관례로, 여기서도
/// "현재 트리 상태를 정적으로 공유"하는 가장 단순한 방법을 택했습니다. <see cref="StructureView"/>의
/// <c>RenderTree()</c>가 트리가 바뀔 때마다(추가/삭제/이름변경/속성편집 — 사실상 항상) <see cref="Update"/>를
/// 호출해 최신 상태로 갱신하므로, <c>NodePropertyDialog</c>가 열릴 때마다 <see cref="CurrentTags"/>는
/// 항상 그 시점의 최신 태그 목록입니다.
/// </summary>
/// <remarks>
/// <b>왜 Id 기준인가(완료 기준 "태그 이름 변경해도 연동이 끊기지 않는지")</b>: <see cref="TagCatalogEntry.Id"/>는
/// <see cref="StructureTreeNode.Id"/>(GUID, 생성 시 고정)를 그대로 쓰고, <see cref="TagCatalogEntry.DisplayPath"/>만
/// 이름이 바뀔 때마다 달라집니다. 캔버스 노드(PlcTagReadNode 등)의 <c>NodeConfig.Properties["tagId"]</c>에는
/// 항상 <see cref="TagCatalogEntry.Id"/>만 저장되므로, 구조 설정에서 태그 이름을 바꿔도(Id는 그대로) 그
/// 저장된 값은 여전히 같은 태그를 가리킵니다 — 다음에 속성 편집 창을 열면 <see cref="CurrentTags"/>가
/// 새 이름으로 갱신돼 있어 ComboBox에는 바뀐 이름이 표시되지만, 선택된 항목(Id 매칭)은 그대로 유지됩니다.
/// </remarks>
public static class TagCatalog
{
    /// <summary>현재 구조 설정 트리에 있는 모든 태그 — <see cref="StructureView"/>의 RenderTree()가 호출될 때마다 최신으로 갱신됩니다.</summary>
    public static IReadOnlyList<TagCatalogEntry> CurrentTags { get; private set; } = Array.Empty<TagCatalogEntry>();

    /// <summary><paramref name="tags"/>로 <see cref="CurrentTags"/>를 완전히 교체합니다.</summary>
    public static void Update(IReadOnlyList<TagCatalogEntry> tags) => CurrentTags = tags;
}

/// <summary>
/// Class명 : 태그 카탈로그 항목
/// 역활 및 기능 : 태그 하나의 Id(불변)와 사람이 읽기 좋은 표시 경로(트리 위치, 이름 변경 시 갱신됨)를 담는 값
/// </summary>
/// <param name="Id">이 태그(TagNode)의 <see cref="StructureTreeNode.Id"/> — TagRef 필드가 실제로 저장하는 값.</param>
/// <param name="DisplayPath">"장비/PLC/디바이스맵/태그" 형태의 표시용 경로 — ComboBox에 보이는 문자열.</param>
public sealed record TagCatalogEntry(string Id, string DisplayPath)
{
    /// <summary>WPF ComboBox가 DataTemplate 없이 항목을 그릴 때 기본적으로 이 값을 표시하도록 재정의합니다.</summary>
    public override string ToString() => DisplayPath;
}
