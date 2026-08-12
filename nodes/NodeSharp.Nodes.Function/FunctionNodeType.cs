using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.Function;

/// <summary>
/// Class명 : Function 노드 타입 메타데이터
/// 역활 및 기능 : NodeTypeRegistry.ScanAssembly가 찾아 등록하는 Function 노드의 INodeTypeDescriptor 정적 필드
///
/// <see cref="FunctionNode"/>의 <see cref="INodeTypeDescriptor"/>를 노출합니다. Inject/Switch와
/// 동일한 관례(직접 구현, Registry 빌더 미사용 — 이유는 <c>InjectNodeType</c> XML 문서 참고)로
/// <see cref="INodeTypeDescriptor"/>를 record로 바로 만족시킵니다.
/// 설계 근거: 02번 문서 5번 탭 카드8, 03번 개발 Step맵 FN-01.
/// </summary>
public static class FunctionNodeType
{
    /// <summary>
    /// Function 노드 타입 디스크립터입니다. <see cref="INodeTypeDescriptor.PropertySchema"/>는 2개
    /// 필드("mode"/"code")만 있습니다 — 모드 전환 시 입력란이 자동으로 바뀌는 편집 UI(카드8)는
    /// <c>FN-03</c>(⏳ 대기)이 전담하고, 이 Step은 값을 저장/읽는 것까지만 다룹니다.
    /// </summary>
    public static readonly INodeTypeDescriptor Descriptor = new FunctionNodeDescriptor();

    private sealed record FunctionNodeDescriptor : INodeTypeDescriptor
    {
        public string TypeName => "function";

        public string Category => "function";

        public string IconGlyph => string.Empty;

        public int DefaultInputs => 1;

        public int DefaultOutputs => 1;

        public Func<NodeConfig, IFlowNode> Factory { get; } = cfg => new FunctionNode
        {
            Id = cfg.Id,
            Name = cfg.Name,
            Mode = ReadString(cfg.Properties, "mode", "expression") == "csharp"
                ? FunctionMode.CSharp
                : FunctionMode.Expression,
            Code = ReadString(cfg.Properties, "code", string.Empty),
        };

        public IReadOnlyList<PropertyField> PropertySchema { get; } = new[]
        {
            new PropertyField(
                Key: "mode",
                Label: "실행 모드",
                Type: PropertyFieldType.ComboBox,
                Required: false,
                DefaultValue: "expression",
                Options: new[] { "expression", "csharp" },
                HelpText: "\"expression\"은 NCalc 한 줄 수식 모드입니다 — 코드를 몰라도 되고, 문법" +
                           " 오류가 있어도 컴파일 없이 즉시 노드 에러(상태 점 빨강)로만 표시되며" +
                           " Runner는 계속 동작합니다. \"csharp\"은 Roslyn C# 코드 모드로, FN-02가" +
                           " 아직 구현되지 않아 지금 선택하면 배포 시 이 노드만 실패 처리됩니다.",
                Example: "예: \"expression\" (기본값, 현장 엔지니어용), \"csharp\" (FN-02 완료 후 사용 가능)"),
            new PropertyField(
                Key: "code",
                Label: "표현식 / 코드",
                Type: PropertyFieldType.Code,
                Required: false,
                DefaultValue: "",
                HelpText: "mode가 \"expression\"이면 NCalc 수식 한 줄입니다 — msg의 모든 필드" +
                           "(payload, topic, 사용자 정의 필드)를 변수처럼 그대로 쓸 수 있고, 계산" +
                           " 결과는 자동으로 msg.payload에 저장됩니다(별도 return 문 불필요). mode가" +
                           " \"csharp\"이면 FN-02 완료 전까지는 사용할 수 없습니다.",
                Example: "예: \"(pressure1 - pressure2) * 0.0689\", \"if(val > 0, val, 0)\", " +
                         "\"(fahrenheit - 32) * 5 / 9\""),
        };

        /// <summary>
        /// <see cref="NodeConfig.Properties"/>에서 문자열 값을 안전하게 읽습니다. System.Text.Json으로
        /// 역직렬화된 값은 원본 CLR <c>string</c>이 아니라 <see cref="JsonElement"/>로 채워질 수 있어
        /// 두 경우를 모두 처리합니다(Inject/Switch NodeType과 동일한 관례). 키가 없거나 값이 비어
        /// 있으면 <paramref name="fallback"/>을 반환합니다.
        /// </summary>
        private static string ReadString(IReadOnlyDictionary<string, object?> properties, string key, string fallback)
        {
            if (!properties.TryGetValue(key, out var raw) || raw is null)
            {
                return fallback;
            }

            var text = Unwrap(raw)?.ToString();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        /// <summary><see cref="JsonElement"/>로 채워진 값을 원본에 가까운 CLR 값(문자열)으로 풀어냅니다. JsonElement가 아니면 그대로 반환합니다.</summary>
        private static object? Unwrap(object? raw)
        {
            if (raw is not JsonElement je)
            {
                return raw;
            }

            return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
        }
    }
}
