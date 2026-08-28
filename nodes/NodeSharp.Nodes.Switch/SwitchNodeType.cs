using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.Switch;

/// <summary>
/// Class명 : Switch 노드 타입 메타데이터
/// 역활 및 기능 : NodeTypeRegistry.ScanAssembly가 찾아 등록하는 Switch 노드의 INodeTypeDescriptor 정적 필드
///
/// <see cref="SwitchNode"/>의 <see cref="INodeTypeDescriptor"/>를 노출합니다. <c>InjectNodeType</c>과
/// 동일한 관례(직접 구현, Registry의 빌더 미사용 — <c>nodes\*</c>는 Contracts+Util만 참조) 그대로
/// <c>public static readonly</c> <c>Descriptor</c> 필드를 둡니다.
/// </summary>
/// <remarks>
/// <b>"rules" 필드가 <see cref="PropertyFieldType.Code"/>인 이유</b>: <see cref="PropertyField"/>에는
/// "규칙 목록"(반복 그룹) 전용 타입이 없어(조사 결과 확인), <see cref="SwitchRule"/> 목록 전체를 JSON
/// 배열 문자열로 인코딩해 <c>Code</c> 타입 필드 하나에 저장하는 임시 패턴을 씁니다 — WPF 쪽 전용 규칙
/// 편집 UI(추가/삭제/순서 변경)는 이 Step 범위 밖이며, 지금은 JSON 텍스트를 직접 입력하거나 xUnit처럼
/// 코드로 <see cref="NodeConfig.Properties"/>를 채우는 방식으로만 검증합니다.
/// </remarks>
public static class SwitchNodeType
{
    /// <summary>
    /// Switch 노드 타입 디스크립터입니다. <see cref="Factory"/>가 "rules" JSON 문자열을
    /// <see cref="SwitchRule"/> 목록으로 역직렬화해 <see cref="SwitchNode.OutputPorts"/>를 규칙 개수만큼
    /// 만들고, "property"(<see cref="TypedValue"/> JSON)·"checkall"(Checkbox)도 함께 읽어 채웁니다.
    /// </summary>
    public static readonly INodeTypeDescriptor Descriptor = new SwitchNodeDescriptor();

    private sealed record SwitchNodeDescriptor : INodeTypeDescriptor
    {
        public string TypeName => "switch";

        public string Category => "function";

        // (EC-18, ★ 사용자 요청 — "노드앞쪽 아이콘부분이 다르며") 실제 Node-RED의 switch 노드 아이콘
        // (분기/갈래 모양)과 같은 인상을 주는 이모지 글리프(InjectNodeType.IconGlyph 항목 참고).
        public string IconGlyph => "🔀";

        public int DefaultInputs => 1;

        public int DefaultOutputs => 1;

        public Func<NodeConfig, IFlowNode> Factory { get; } = cfg =>
        {
            var rules = ReadRules(cfg.Properties, "rules");
            var outputPorts = rules.Count > 0
                ? rules.Select((r, i) => new NodePort(i, DescribeRule(r))).ToArray()
                : new[] { new NodePort(0, "out") };

            return new SwitchNode
            {
                Id = cfg.Id,
                Name = cfg.Name,
                Property = ReadTypedValue(cfg.Properties, "property", new TypedValue(TypedValueSource.MsgField, "payload")),
                Rules = rules,
                CheckAll = ReadBool(cfg.Properties, "checkall", true),
                OutputPorts = outputPorts,
            };
        };

        public IReadOnlyList<PropertyField> PropertySchema { get; } = new[]
        {
            new PropertyField(
                Key: "property",
                Label: "비교할 값",
                Type: PropertyFieldType.TypedValue,
                Required: false,
                DefaultValue: /*lang=json,strict*/ "{\"Source\":1,\"Value\":\"payload\"}",
                HelpText: "규칙들과 비교할 실제 값의 출처입니다. 기본값은 msg.payload이며, msg의 다른 " +
                           "필드·Flow/Global Context 값으로도 바꿀 수 있습니다(TypedValue JSON — " +
                           "Source: 0=Fixed/1=MsgField/2=FlowContext/3=GlobalContext/4=EnvVar(미지원)/" +
                           "5=Expression).",
                Example: "예: {\"Source\":1,\"Value\":\"payload\"} (msg.payload, 기본값), " +
                         "{\"Source\":2,\"Value\":\"threshold\"} (Flow Context의 threshold 키)"),
            new PropertyField(
                Key: "rules",
                Label: "규칙 목록",
                Type: PropertyFieldType.Code,
                Required: false,
                DefaultValue: "[]",
                HelpText: "위 \"비교할 값\"을 검사할 조건 목록입니다(JSON 배열). 목록의 순서가 곧 출력 " +
                           "포트 순서입니다 — 0번째 규칙이 맞으면 0번 포트로, 1번째가 맞으면 1번 포트로 " +
                           "나갑니다. 지원 연산자: eq/neq/lt/lte/gt/gte/btwn/cont/regex/true/false/null/" +
                           "nnull/empty/nempty/istype/else(17종). head/tail/index/jsonata_exp는 아직 " +
                           "지원하지 않습니다(각각 NR-13a/NR-13b, 별도 Step 신설 필요).",
                Example: "예: [{\"Operator\":\"gte\",\"CompareValue\":{\"Source\":0,\"Value\":\"85\"}}," +
                         "{\"Operator\":\"else\"}] (payload가 85 이상이면 0번 포트, 그 외엔 1번 포트)"),
            new PropertyField(
                Key: "checkall",
                Label: "모든 규칙 검사",
                Type: PropertyFieldType.Checkbox,
                Required: false,
                DefaultValue: "true",
                HelpText: "켜면(기본값) 맞는 규칙 전부의 포트로 메시지를 보내고, 끄면 처음 맞는 규칙 " +
                           "하나의 포트로만 보내고 나머지 규칙은 검사하지 않습니다(Node-RED 기본값과 동일).",
                Example: "예: true (기본값, 여러 포트로 동시 라우팅 가능), false (첫 매치에서 멈춤)"),
        };

        /// <summary>규칙 하나를 짧은 사람이 읽기 좋은 문구로 요약합니다 — <see cref="SwitchNode.OutputPorts"/>의 <see cref="NodePort.Label"/>에 사용.</summary>
        private static string DescribeRule(SwitchRule rule) => rule.Operator switch
        {
            "else" => "그 외",
            "true" => "참",
            "false" => "거짓",
            "null" => "null",
            "nnull" => "null 아님",
            "empty" => "비어있음",
            "nempty" => "비어있지 않음",
            _ => rule.CompareValue is not null ? $"{rule.Operator} {rule.CompareValue.Value}" : rule.Operator,
        };

        /// <summary>(NR-04, InjectNodeType의 ReadString/ReadDouble/ReadBool과 동일한 이유) <see cref="NodeConfig.Properties"/>에서 불리언 값을 JsonElement/원본 CLR 타입 양쪽 모두에서 안전하게 읽습니다.</summary>
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

        /// <summary>"rules" 속성값(JSON 배열 문자열 또는 JsonElement)을 <see cref="SwitchRule"/> 목록으로 역직렬화합니다. 없거나 파싱 실패 시 빈 목록을 반환합니다(예외를 던지지 않음).</summary>
        private static IReadOnlyList<SwitchRule> ReadRules(IReadOnlyDictionary<string, object?> properties, string key)
        {
            var json = ExtractJsonText(properties, key);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<SwitchRule>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<SwitchRule>>(json) ?? new List<SwitchRule>();
            }
            catch (JsonException)
            {
                return Array.Empty<SwitchRule>();
            }
        }

        /// <summary>"property" 속성값(TypedValue JSON 문자열 또는 JsonElement)을 <see cref="TypedValue"/>로 역직렬화합니다. 없거나 파싱 실패 시 <paramref name="fallback"/>을 반환합니다.</summary>
        private static TypedValue ReadTypedValue(IReadOnlyDictionary<string, object?> properties, string key, TypedValue fallback)
        {
            var json = ExtractJsonText(properties, key);
            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback;
            }

            try
            {
                return JsonSerializer.Deserialize<TypedValue>(json) ?? fallback;
            }
            catch (JsonException)
            {
                return fallback;
            }
        }

        /// <summary>NodeConfig.cs remarks가 경고한 대로, System.Text.Json 역직렬화 시 값이 원본 문자열이 아니라 JsonElement로 채워질 수 있어 두 경우 모두에서 순수 JSON 텍스트를 뽑아냅니다.</summary>
        private static string? ExtractJsonText(IReadOnlyDictionary<string, object?> properties, string key)
        {
            if (!properties.TryGetValue(key, out var raw) || raw is null)
            {
                return null;
            }

            return raw switch
            {
                JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
                JsonElement je => je.GetRawText(),
                string s => s,
                _ => null,
            };
        }
    }
}
