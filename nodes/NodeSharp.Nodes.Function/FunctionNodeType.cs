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
/// 설계 근거: 02번 문서 5번 탭 카드8, 03번 개발 Step맵 FN-01·FN-02·FN-03.
/// </summary>
public static class FunctionNodeType
{
    /// <summary>
    /// Function 노드 타입 디스크립터입니다. <see cref="INodeTypeDescriptor.PropertySchema"/>는 4개
    /// 필드("mode"/"expressionCode"/"csharpCode"/"timeoutSec")입니다 — 모드 전환 시 입력란이 자동으로
    /// 바뀌는 편집 UI(카드8)는 <c>FN-03</c>이 <see cref="PropertyField.VisibleWhenKey"/>로 구현했고,
    /// "timeoutSec"(FN-04)도 같은 방식으로 CSharp 모드에서만 보입니다.
    /// </summary>
    /// <remarks>
    /// <b>(FN-03) "code" 단일 필드 → "expressionCode"/"csharpCode" 분리, 그리고 구버전 호환</b>:
    /// FN-01/FN-02 시점엔 카드8을 재확인하지 않고 단일 "code" 필드로 구현했다가, FN-03 착수 전
    /// 재검토로 카드8 원본 설계(모드별 별도 필드)와 다르다는 것을 발견 — 사용자에게 "단일 필드
    /// 유지" vs "설계대로 분리" 중 확인받아 <b>분리</b>로 결정(<see cref="FunctionNode"/> XML 문서의
    /// FN-03 항목에 상세 근거 기록). <see cref="Factory"/>는 새 스키마("expressionCode"/"csharpCode")를
    /// 우선 읽고, 둘 다 없는데 옛 "code" 키가 있으면(FN-01/FN-02 시절 저장된 flows.json) 그 값을
    /// 저장 당시의 <c>mode</c>에 맞는 새 필드로 옮겨 읽습니다 — 그래프에 저장된 실제 파일을 즉시
    /// 고쳐 쓰지는 않지만(다음에 "완료"로 저장하면 새 키로 자연스럽게 갱신됨), 읽는 시점엔 기존
    /// 데이터가 사라진 것처럼 보이지 않습니다. <c>NodeSharp.Util.Config.Migration.ConfigMigration</c>
    /// (flows.json 전체 스키마 버전 마이그레이션용 인프라)을 쓰지 않은 이유: 이번 변경은 파일
    /// 전체가 아니라 Function 노드 하나의 속성 키 이름 변경에 그치는 국소적 사안이라, 이미 이
    /// 파일이 쓰고 있던 <see cref="ReadString"/>(JsonElement/문자열 겸용 읽기) 패턴을 그대로 확장하는
    /// 쪽이 새 마이그레이션 규칙을 등록하는 것보다 훨씬 가볍고, 다른 파일에 영향도 없습니다.
    /// <para>
    /// <b>(FN-04) "timeoutSec" 필드 신규</b>: 02번 설계 문서 5번 탭 카드7의 "기본 5초, 노드 설정에서
    /// 조정 가능"을 그대로 반영 — <c>Number</c> 타입, 기본값 "5", <see cref="PropertyField.VisibleWhenKey"/>로
    /// CSharp 모드에서만 노출합니다(NCalc 모드는 반복문이 없어 타임아웃이 의미 없음, 카드7 근거).
    /// <see cref="Factory"/>는 신규 <see cref="ReadDouble"/>로 문자열/숫자 <see cref="JsonElement"/> 양쪽을
    /// 파싱해 <c>FunctionNode.TimeoutSeconds</c>에 넘기며, 값이 없거나 파싱 실패·0 이하이면 기본값
    /// 5초로 대체합니다(잘못된 값으로 타임아웃이 사실상 무력화되는 것을 방지).
    /// </para>
    /// </remarks>
    public static readonly INodeTypeDescriptor Descriptor = new FunctionNodeDescriptor();

    private sealed record FunctionNodeDescriptor : INodeTypeDescriptor
    {
        public string TypeName => "function";

        public string Category => "function";

        // (EC-18, ★ 사용자 요청 — "노드앞쪽 아이콘부분이 다르며") 실제 Node-RED의 function 노드
        // 아이콘("ƒ(x)")과 같은 인상을 주는 글리프(InjectNodeType.IconGlyph 항목 참고).
        public string IconGlyph => "ƒ(x)";

        public int DefaultInputs => 1;

        public int DefaultOutputs => 1;

        public Func<NodeConfig, IFlowNode> Factory { get; } = cfg =>
        {
            var mode = ReadString(cfg.Properties, "mode", "expression") == "csharp"
                ? FunctionMode.CSharp
                : FunctionMode.Expression;

            // (FN-03) 구버전(FN-01/FN-02) 단일 "code" 키 호환 — 새 키가 없을 때만 저장 당시 모드에
            // 맞는 새 필드로 옮겨 읽는다(위 클래스 remarks 참고).
            var legacyCode = ReadString(cfg.Properties, "code", string.Empty);

            return new FunctionNode
            {
                Id = cfg.Id,
                Name = cfg.Name,
                Mode = mode,
                ExpressionCode = ReadString(cfg.Properties, "expressionCode",
                    mode == FunctionMode.Expression ? legacyCode : string.Empty),
                CSharpCode = ReadString(cfg.Properties, "csharpCode",
                    mode == FunctionMode.CSharp ? legacyCode : string.Empty),
                // (FN-04) "timeoutSec" — 값이 없거나 0 이하로 파싱되면 위 클래스 remarks대로 기본 5초 사용.
                TimeoutSeconds = ReadDouble(cfg.Properties, "timeoutSec", fallback: 5.0),
            };
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
                           " Runner는 계속 동작합니다. \"csharp\"은 Roslyn C# 코드 모드로, 반복문·조건문" +
                           " 등 완전한 C# 문법을 쓸 수 있는 대신 최초 배포 시 컴파일이 필요하고, 문법" +
                           " 오류가 있으면 배포 단계에서 이 노드만 실패 처리됩니다(다른 노드는 정상 배포)." +
                           " 아래 입력란은 이 모드에 맞춰 자동으로 바뀌고(FN-03), 두 모드에 입력한 내용은" +
                           " 서로 독립적으로 보존되어 오가도 사라지지 않습니다.",
                Example: "예: \"expression\" (기본값, 현장 엔지니어용), \"csharp\" (복잡한 로직이 필요한 고급 사용자용)"),
            new PropertyField(
                Key: "expressionCode",
                Label: "표현식 (Expression 모드)",
                Type: PropertyFieldType.Code,
                Required: false,
                DefaultValue: "",
                VisibleWhenKey: "mode",
                VisibleWhenValue: "expression",
                HelpText: "NCalc 수식 한 줄입니다 — msg의 모든 필드(payload, topic, 사용자 정의 필드)를" +
                           " 변수처럼 그대로 쓸 수 있고, 계산 결과는 자동으로 msg.payload에 저장됩니다" +
                           "(별도 return 문 불필요).",
                Example: "예: \"(pressure1 - pressure2) * 0.0689\", \"if(val > 0, val, 0)\""),
            new PropertyField(
                Key: "csharpCode",
                Label: "C# 코드 (CSharp 모드)",
                Type: PropertyFieldType.Code,
                Required: false,
                DefaultValue: "",
                VisibleWhenKey: "mode",
                VisibleWhenValue: "csharp",
                HelpText: "완전한 C# 코드입니다 — msg.payload/msg.topic처럼 소문자로 그대로 읽고 쓸 수" +
                           " 있고, 마지막에 반드시 return msg;로 다음 노드에 전달할 메시지를 돌려줘야" +
                           " 합니다(return null;이면 이 메시지는 버려짐, 필터링 용도).",
                Example: "예: \"msg.payload = (double)msg.payload * 2; return msg;\""),
            new PropertyField(
                Key: "timeoutSec",
                Label: "실행 타임아웃(초, CSharp 모드)",
                Type: PropertyFieldType.Number,
                Required: false,
                DefaultValue: "5",
                VisibleWhenKey: "mode",
                VisibleWhenValue: "csharp",
                HelpText: "C# 코드 실행이 이 시간(초)을 넘기면 노드가 타임아웃 에러로 표시되고 다음" +
                           " 입력을 계속 처리합니다(FN-04). await 지점이 전혀 없는 코드(예: 무한 for/while" +
                           " 루프)는 이 시간이 지나도 백그라운드 스레드 자체는 계속 실행될 수 있다는" +
                           " 한계가 있습니다 — 자세한 내용은 RoslynFunctionExecutor·FunctionTimeoutException" +
                           " 코드 문서를 참고하세요. 0 이하이거나 비워두면 기본값 5초가 적용됩니다.",
                Example: "예: \"5\"(기본값), \"1\"(빠른 실패 원함), \"30\"(무거운 계산 허용)"),
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

        /// <summary>
        /// (FN-04) <see cref="NodeConfig.Properties"/>에서 숫자 값을 안전하게 읽습니다. <see cref="ReadString"/>과
        /// 같은 이유(JsonElement/문자열 겸용)로 먼저 문자열로 풀어낸 뒤 <see cref="double.TryParse"/>를
        /// 시도하고, 키가 없거나 파싱에 실패하거나 값이 0 이하이면 <paramref name="fallback"/>을
        /// 반환합니다(위 클래스 remarks의 FN-04 항목 참고 — 잘못된 값으로 타임아웃이 무력화되는 것 방지).
        /// </summary>
        private static double ReadDouble(IReadOnlyDictionary<string, object?> properties, string key, double fallback)
        {
            var text = ReadString(properties, key, string.Empty);
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : fallback;
        }
    }
}
