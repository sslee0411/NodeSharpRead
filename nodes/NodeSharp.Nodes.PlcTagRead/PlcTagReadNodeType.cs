using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.PlcTagRead;

/// <summary>
/// Class명 : PLC 태그 읽기 노드 타입 메타데이터
/// 역활 및 기능 : NodeTypeRegistry.ScanAssembly가 찾아 등록하는 PlcTagReadNode의 INodeTypeDescriptor 정적 필드
///
/// (ED-D04) <c>InjectNodeType</c>/<c>SwitchNodeType</c>과 동일한 관례(직접 구현, Registry의 빌더
/// 미사용 — <c>nodes\*</c>는 Contracts만 참조)로 PropertySchema에 "tagId"
/// (<see cref="PropertyFieldType.TagRef"/>) 필드 1개를 노출합니다. 실제 태그 선택 목록은 이 프로젝트가
/// 아니라 Editor 쪽(<c>NodeSharp.Editor.Structure.TagCatalog</c> + <c>NodePropertyDialog</c>)이
/// 채웁니다 — 이 Descriptor는 "tagId가 TagRef 타입"이라는 메타데이터만 선언합니다.
/// </summary>
public static class PlcTagReadNodeType
{
    /// <summary>PLC 태그 읽기 노드 타입 디스크립터입니다.</summary>
    public static readonly INodeTypeDescriptor Descriptor = new PlcTagReadNodeDescriptor();

    private sealed record PlcTagReadNodeDescriptor : INodeTypeDescriptor
    {
        public string TypeName => "plcTagRead";

        public string Category => "structure";

        // (EC-18, ★ 사용자 요청 — "PLC 부분의 Node는 기존 노드레드와 다름") 실제 Node-RED에는 대응
        // 아이콘이 없는 이 프로젝트 고유 노드라, "태그를 받아온다(읽기)"는 방향성을 그대로 나타내는
        // 인박스 이모지를 새로 정했다 — 쓰기 노드(PlcTagWriteNodeType, 아웃박스)와 짝을 이룬다.
        public string IconGlyph => "📥";

        public int DefaultInputs => 1;

        public int DefaultOutputs => 1;

        public Func<NodeConfig, IFlowNode> Factory { get; } = cfg => new PlcTagReadNode
        {
            Id = cfg.Id,
            Name = cfg.Name,
            TagId = ReadString(cfg.Properties, "tagId", string.Empty),
        };

        public IReadOnlyList<PropertyField> PropertySchema { get; } = new[]
        {
            new PropertyField(
                Key: "tagId",
                Label: "태그",
                Type: PropertyFieldType.TagRef,
                Required: true,
                DefaultValue: "",
                HelpText: "구조 설정 트리(우측 \"구조 설정\" 탭)에서 이 노드가 읽어올 태그를 선택합니다. " +
                           "선택은 태그의 고유 Id로 저장되므로, 나중에 구조 설정에서 태그 이름만 바꿔도 " +
                           "이 연동은 끊어지지 않습니다. 실제 PLC 값 읽기는 아직 지원하지 않고(후속 " +
                           "Step 범위), 지금은 연동된 태그 Id를 msg.payload로 그대로 전달합니다.",
                Example: "예: \"1호기PLC/온도맵/온도센서1\" 태그를 목록에서 선택"),
        };

        /// <summary>(InjectNodeType.ReadString과 동일한 이유) NodeConfig.Properties에서 문자열 값을 JsonElement/원본 CLR 타입 양쪽 모두에서 안전하게 읽습니다. 키가 없거나 값이 비어 있으면 <paramref name="fallback"/>을 반환합니다.</summary>
        private static string ReadString(IReadOnlyDictionary<string, object?> properties, string key, string fallback)
        {
            if (!properties.TryGetValue(key, out var raw) || raw is null)
            {
                return fallback;
            }

            var text = raw is JsonElement je
                ? (je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString())
                : raw.ToString();
            return string.IsNullOrWhiteSpace(text) ? fallback : text!;
        }
    }
}
