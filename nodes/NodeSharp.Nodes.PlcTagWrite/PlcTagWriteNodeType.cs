using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.PlcTagWrite;

/// <summary>
/// Class명 : PLC 태그 쓰기 노드 타입 메타데이터
/// 역활 및 기능 : NodeTypeRegistry.ScanAssembly가 찾아 등록하는 PlcTagWriteNode의 INodeTypeDescriptor 정적 필드
///
/// (ED-D06a) <c>PlcTagReadNodeType</c>(ED-D04)과 동일한 관례(직접 구현, Registry의 빌더 미사용 —
/// <c>nodes\*</c>는 Contracts만 참조)로 PropertySchema에 "tagId"(<see cref="PropertyFieldType.TagRef"/>,
/// Required), "minValue"/"maxValue"(<see cref="PropertyFieldType.Number"/>, 선택) 필드 3개를 노출합니다.
/// </summary>
public static class PlcTagWriteNodeType
{
    /// <summary>PLC 태그 쓰기 노드 타입 디스크립터입니다.</summary>
    public static readonly INodeTypeDescriptor Descriptor = new PlcTagWriteNodeDescriptor();

    private sealed record PlcTagWriteNodeDescriptor : INodeTypeDescriptor
    {
        public string TypeName => "plcTagWrite";

        public string Category => "structure";

        public string IconGlyph => string.Empty;

        public int DefaultInputs => 1;

        public int DefaultOutputs => 1;

        public Func<NodeConfig, IFlowNode> Factory { get; } = cfg => new PlcTagWriteNode
        {
            Id = cfg.Id,
            Name = cfg.Name,
            TagId = ReadString(cfg.Properties, "tagId", string.Empty),
            MinValue = ReadNullableDouble(cfg.Properties, "minValue"),
            MaxValue = ReadNullableDouble(cfg.Properties, "maxValue"),
        };

        public IReadOnlyList<PropertyField> PropertySchema { get; } = new[]
        {
            new PropertyField(
                Key: "tagId",
                Label: "태그",
                Type: PropertyFieldType.TagRef,
                Required: true,
                DefaultValue: "",
                HelpText: "구조 설정 트리(우측 \"구조 설정\" 탭)에서 이 노드가 쓸 태그를 선택합니다. " +
                           "실제 PLC 쓰기는 아직 지원하지 않고(후속 Step 범위), 범위 검사와 동시 쓰기 " +
                           "락(같은 태그 기준)만 증명합니다.",
                Example: "예: \"1호기PLC/출력맵/펌프정지\" 태그를 목록에서 선택"),
            new PropertyField(
                Key: "minValue",
                Label: "최소값(선택)",
                Type: PropertyFieldType.Number,
                Required: false,
                DefaultValue: null,
                HelpText: "이 값보다 작은 쓰기는 거부됩니다. 비워두면 하한 검사를 하지 않습니다.",
                Example: "예: 0"),
            new PropertyField(
                Key: "maxValue",
                Label: "최대값(선택)",
                Type: PropertyFieldType.Number,
                Required: false,
                DefaultValue: null,
                HelpText: "이 값보다 큰 쓰기는 거부됩니다. 비워두면 상한 검사를 하지 않습니다.",
                Example: "예: 100"),
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

        /// <summary>(ED-D06a) NodeConfig.Properties에서 숫자 값을 <c>double?</c>로 안전하게 읽습니다 — 키가 없거나, 값이 비어 있거나, 숫자로 해석되지 않으면 <c>null</c>을 반환합니다(ReadString과 동일한 JsonElement 처리 원칙).</summary>
        private static double? ReadNullableDouble(IReadOnlyDictionary<string, object?> properties, string key)
        {
            if (!properties.TryGetValue(key, out var raw) || raw is null)
            {
                return null;
            }

            if (raw is JsonElement je)
            {
                return je.ValueKind switch
                {
                    JsonValueKind.Number when je.TryGetDouble(out var jn) => jn,
                    JsonValueKind.String when double.TryParse(je.GetString(), out var js) => js,
                    _ => null,
                };
            }

            return raw switch
            {
                double d => d,
                int i => i,
                float f => f,
                string s when double.TryParse(s, out var parsed) => parsed,
                _ => null,
            };
        }
    }
}
