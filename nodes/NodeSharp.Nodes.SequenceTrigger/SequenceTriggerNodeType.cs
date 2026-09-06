using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.SequenceTrigger;

/// <summary>
/// Class명 : 시퀀스 트리거 노드 타입 메타데이터
/// 역활 및 기능 : NodeTypeRegistry.ScanAssembly가 찾아 등록하는 SequenceTriggerNode의 INodeTypeDescriptor 정적 필드
///
/// (SQ-03) <c>PlcTagReadNodeType</c>과 동일한 관례(직접 구현, Registry의 빌더 미사용)로 PropertySchema에
/// "sequenceId"(<see cref="PropertyFieldType.SequenceRef"/>) 필드 1개를 노출합니다.
/// </summary>
public static class SequenceTriggerNodeType
{
    /// <summary>시퀀스 트리거 노드 타입 디스크립터입니다.</summary>
    public static readonly INodeTypeDescriptor Descriptor = new SequenceTriggerNodeDescriptor();

    private sealed record SequenceTriggerNodeDescriptor : INodeTypeDescriptor
    {
        public string TypeName => "sequenceTrigger";

        public string Category => "sequence";

        // Node-RED에는 대응 노드가 없는 이 프로젝트 고유 노드라(EC-18과 동일한 판단), "정해진 절차를
        // 순서대로 진행한다"는 의미의 이모지를 골랐다.
        public string IconGlyph => "🧭";

        public int DefaultInputs => 1;

        public int DefaultOutputs => 1;

        public Func<NodeConfig, IFlowNode> Factory { get; } = cfg => new SequenceTriggerNode
        {
            Id = cfg.Id,
            Name = cfg.Name,
            SequenceId = ReadString(cfg.Properties, "sequenceId", string.Empty),
        };

        public IReadOnlyList<PropertyField> PropertySchema { get; } = new[]
        {
            new PropertyField(
                Key: "sequenceId",
                Label: "시퀀스",
                Type: PropertyFieldType.SequenceRef,
                Required: true,
                DefaultValue: "",
                HelpText: "이 노드가 가리키는 시퀀스의 Id를 입력합니다(SQ-04 이전이라 아직 목록에서 " +
                           "선택하는 대신 직접 입력 — PropertyFieldType.SequenceRef XML 문서 참고). " +
                           "캔버스에서 이 노드를 더블클릭하면 Sequence Editor 창이 열립니다.",
                Example: "예: \"seq-1\""),
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
