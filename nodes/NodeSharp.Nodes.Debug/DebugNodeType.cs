using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.Debug;

/// <summary>
/// Class명 : Debug 노드 타입 메타데이터
/// 역활 및 기능 : NodeTypeRegistry.ScanAssembly가 찾아 등록하는 Debug 노드의 INodeTypeDescriptor 정적 필드
///
/// <see cref="DebugNode"/>의 <see cref="INodeTypeDescriptor"/>를 노출합니다. Inject/Switch/Function과
/// 동일한 관례(직접 구현, Registry 빌더 미사용 — 이유는 <c>InjectNodeType</c> XML 문서 참고)로
/// <see cref="INodeTypeDescriptor"/>를 record로 바로 만족시킵니다.
/// 설계 근거: 03번 개발 Step맵 Phase 7 NR-11.
/// </summary>
/// <remarks>
/// <b>Category = "common"</b>: 실제 Node-RED 원본(<c>packages/node_modules/@node-red/nodes/core/
/// common/21-debug.html</c>, WebSearch로 2026-08 세션에 확인)에서 Debug 노드가 <c>category: 'common'</c>로
/// 등록되는 것과 동일하게 맞췄습니다(개발 지침 8번) — Inject/Switch/Function이 각각 "input"/"function"인
/// 것과 달리, Debug는 Node-RED 팔레트에서도 Comment/Complete/Catch/Status/Link 노드와 함께 "common"
/// 그룹에 속합니다.
/// </remarks>
public static class DebugNodeType
{
    /// <summary>
    /// Debug 노드 타입 디스크립터입니다. <see cref="INodeTypeDescriptor.PropertySchema"/>는 "toNext"
    /// (Checkbox, 기본 "false") 1개 필드뿐입니다 — NR-11 desc가 명시한 "다음 노드로도 전달할지" 옵션이
    /// 이 Step의 유일한 편집 가능 속성입니다(Node-RED 원본의 active/tosidebar/console/tostatus/complete 등
    /// 나머지 설정은 이 Step 완료 기준 범위 밖이라 추가하지 않음 — 필요해지면 향후 Step에서 확장).
    /// </summary>
    public static readonly INodeTypeDescriptor Descriptor = new DebugNodeDescriptor();

    private sealed record DebugNodeDescriptor : INodeTypeDescriptor
    {
        public string TypeName => "debug";

        public string Category => "common";

        // (EC-18, ★ 사용자 요청 — "노드앞쪽 아이콘부분이 다르며") 실제 Node-RED의 debug 노드 아이콘이
        // 벌레(ladybug) 모양인 것과 동일한 글리프(InjectNodeType.IconGlyph 항목 참고).
        public string IconGlyph => "🐞";

        public int DefaultInputs => 1;

        public int DefaultOutputs => 1;

        public Func<NodeConfig, IFlowNode> Factory { get; } = cfg => new DebugNode
        {
            Id = cfg.Id,
            Name = cfg.Name,
            ToNext = ReadBool(cfg.Properties, "toNext", false),
        };

        public IReadOnlyList<PropertyField> PropertySchema { get; } = new[]
        {
            new PropertyField(
                Key: "toNext",
                Label: "다음 노드로 전달",
                Type: PropertyFieldType.Checkbox,
                Required: false,
                DefaultValue: "false",
                HelpText: "켜면 디버그 사이드바에 표시(발행)하는 것과 별개로 msg를 0번 출력 포트로도" +
                           " 그대로 전달합니다. 꺼두면(기본값) 사이드바 표시만 하고 msg는 여기서 멈춥니다" +
                           " — Node-RED Debug 노드의 실사용 관례와 동일합니다.",
                Example: "예: false (기본값 — 디버그 전용, 흐름은 여기서 종료), true (디버그도 하면서 다음 노드로 계속 전달)"),
        };

        /// <summary>
        /// <see cref="NodeConfig.Properties"/>에서 불리언 값을 안전하게 읽습니다. System.Text.Json으로
        /// 역직렬화된 값은 원본 CLR <c>bool</c>이 아니라 <see cref="JsonElement"/>로 채워질 수 있어
        /// 두 경우를 모두 처리합니다(Inject/Switch/Function NodeType과 동일한 관례). 키가 없거나 값이
        /// 불리언이 아니면 <paramref name="fallback"/>을 반환합니다.
        /// </summary>
        private static bool ReadBool(IReadOnlyDictionary<string, object?> properties, string key, bool fallback)
        {
            if (!properties.TryGetValue(key, out var raw) || raw is null)
            {
                return fallback;
            }

            if (raw is JsonElement je)
            {
                return je.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String when bool.TryParse(je.GetString(), out var b) => b,
                    _ => fallback,
                };
            }

            return raw switch
            {
                bool b => b,
                string s when bool.TryParse(s, out var b) => b,
                _ => fallback,
            };
        }
    }
}
