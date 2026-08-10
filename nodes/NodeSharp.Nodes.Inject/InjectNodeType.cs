using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Nodes.Inject;

/// <summary>
/// Class명 : Inject 노드 타입 메타데이터
/// 역활 및 기능 : NodeTypeRegistry.ScanAssembly가 찾아 등록하는 Inject 노드의 INodeTypeDescriptor 정적 필드
///
/// <see cref="InjectNode"/>의 <see cref="INodeTypeDescriptor"/>를 노출합니다. 관례대로(02번 문서 9번
/// 탭 카드3 <c>HttpRequestNodeType.Descriptor</c> 예시, <c>INodeTypeDescriptor.cs</c> XML 문서의
/// "수집 관례") <c>public static readonly</c> 필드 이름을 <c>Descriptor</c>로 두어
/// <c>NodeTypeRegistry.ScanAssembly</c>(리플렉션 기반)가 자동으로 찾아 등록할 수 있게 합니다.
/// </summary>
/// <remarks>
/// <b>왜 <c>NodeTypeDescriptorBuilder&lt;TNode&gt;</c>(NodeSharp.Registry)를 쓰지 않는가</b>: 02번
/// 문서 1번 탭 폴더 구조가 "nodes\ ← 코어 노드 플러그인, 개별 csproj, Contracts만 참조"라고 명시했는데,
/// 그 빌더는 <c>NodeSharp.Registry</c> 프로젝트 소속이라 참조하면 이 원칙이 깨집니다.
/// <c>NodeTypeDescriptorBuilder</c> 자신의 XML 문서도 "직접 구현하는 대신 ... 쓰면"이라는 표현으로
/// <see cref="INodeTypeDescriptor"/> 직접 구현이 기본 옵션이고 빌더는 선택적 편의 수단일 뿐임을 이미
/// 인정하고 있습니다 — 이 타입은 그 "직접 구현" 경로를 택해 <see cref="INodeTypeDescriptor"/>를
/// record로 바로 만족시킵니다. <c>NodeTypeRegistry.ScanAssembly</c>는 리플렉션으로 "이름이
/// <c>Descriptor</c>인 <c>public static</c> <see cref="INodeTypeDescriptor"/> 필드"만 찾으므로, 빌더로
/// 만들었든 직접 구현했든 스캔 결과는 동일합니다.
/// </remarks>
public static class InjectNodeType
{
    /// <summary>
    /// Inject 노드 타입 디스크립터입니다. <see cref="INodeTypeDescriptor.Factory"/>는
    /// <see cref="InjectNode.Id"/>가 <c>{ get; init; }</c>라 반사(<c>NodeIdBinder</c>) 없이도
    /// 객체 초기화 구문으로 직접 <see cref="NodeConfig.Id"/>/<see cref="NodeConfig.Name"/>을
    /// 동기화합니다. (NR-03a) <see cref="INodeTypeDescriptor.PropertySchema"/>에는 처음엔 "payload"
    /// 필드 1개만 있었습니다(Trigger 종류 선택은 아직 Manual 하나뿐이라 선택 필드를 추가하지 않음).
    /// (NR-03b) "trigger"(ComboBox: manual/interval)·"intervalSeconds"(Number) 2개 필드를 추가했고,
    /// <see cref="Factory"/>가 이 3개 필드 값을 읽어 <see cref="InjectNode.TriggerMode"/>/
    /// <see cref="InjectNode.IntervalSeconds"/>/<see cref="InjectNode.DefaultPayload"/>에 각각
    /// 채웁니다 — "payload" 값을 노드 인스턴스가 실제로 읽어 자동 배선하는 책임이 이 Step에서 비로소
    /// 실현됐습니다(NR-03a 시점엔 "향후 LK-02 또는 NR-03b 쪽 책임"으로 미뤄뒀던 부분). Manual 모드에서는
    /// <see cref="InjectNode.TriggerAsync"/>가 외부에서 명시적으로 받는 payload 매개변수를 그대로 쓰고
    /// (DefaultPayload는 쓰이지 않음), Interval 모드에서는 외부 호출자가 없어 DefaultPayload가 매
    /// 간격마다 자동으로 발행하는 값이 됩니다.
    /// </summary>
    public static readonly INodeTypeDescriptor Descriptor = new InjectNodeDescriptor();

    private sealed record InjectNodeDescriptor : INodeTypeDescriptor
    {
        public string TypeName => "inject";

        public string Category => "input";

        public string IconGlyph => string.Empty;

        public int DefaultInputs => 0;

        public int DefaultOutputs => 1;

        public Func<NodeConfig, IFlowNode> Factory { get; } = cfg => new InjectNode
        {
            Id = cfg.Id,
            Name = cfg.Name,
            TriggerMode = ReadString(cfg.Properties, "trigger", "manual"),
            IntervalSeconds = ReadDouble(cfg.Properties, "intervalSeconds", 0),
            DefaultPayload = cfg.Properties.TryGetValue("payload", out var payload) ? Unwrap(payload) : null,
        };

        public IReadOnlyList<PropertyField> PropertySchema { get; } = new[]
        {
            new PropertyField(
                Key: "payload",
                Label: "Payload",
                Type: PropertyFieldType.Text,
                Required: false,
                DefaultValue: "",
                HelpText: "노드가 트리거될 때 발행할 메시지 본문(msg.payload)입니다. 비워두면 빈 " +
                           "문자열이 발행됩니다. Interval 모드에서는 매 간격마다 이 값이 그대로 발행됩니다.",
                Example: "예: \"hello\" (고정 문자열), \"42\" (숫자는 지금은 문자열로 저장 — 타입 " +
                         "변환은 향후 TypedValue 지원 Step에서 추가 예정)"),
            new PropertyField(
                Key: "trigger",
                Label: "Trigger",
                Type: PropertyFieldType.ComboBox,
                Required: false,
                DefaultValue: "manual",
                Options: new[] { "manual", "interval" },
                HelpText: "언제 발행할지 선택합니다. manual은 (지금은 xUnit, 향후 LK-02가 붙으면 캔버스" +
                           " 클릭이) TriggerAsync를 직접 호출할 때만 1회 발행하고, interval은 배포되는" +
                           " 즉시 intervalSeconds 간격으로 자동 반복 발행합니다.",
                Example: "예: \"manual\" (버튼 클릭 시에만), \"interval\" (배포 후 자동 반복 — Cron/" +
                         "OnDeploy는 NR-03c·NR-03d에서 추가 예정)"),
            new PropertyField(
                Key: "intervalSeconds",
                Label: "간격(초)",
                Type: PropertyFieldType.Number,
                Required: false,
                DefaultValue: "5",
                HelpText: "trigger가 \"interval\"일 때 자동 발행 간격(초)입니다. trigger가 " +
                           "\"manual\"이면 이 값은 무시됩니다. 0 이하로 설정하면 자동 발행이 시작되지 " +
                           "않습니다.",
                Example: "예: 5 (5초마다), 60 (1분마다)"),
        };

        /// <summary>
        /// (NR-03b) <see cref="NodeConfig.Properties"/>에서 문자열 값을 안전하게 읽습니다.
        /// NodeConfig.cs remarks가 경고한 대로, System.Text.Json으로 역직렬화된 값은 원본 CLR
        /// <c>string</c>이 아니라 <see cref="JsonElement"/>로 채워질 수 있어 두 경우를 모두 처리합니다.
        /// 키가 없거나 값이 비어 있으면 <paramref name="fallback"/>을 반환합니다.
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

        /// <summary>
        /// (NR-03b) <see cref="ReadString"/>과 동일한 이유로, <see cref="NodeConfig.Properties"/>에서
        /// 숫자 값을 <see cref="JsonElement"/>/원본 CLR 타입 양쪽 모두에서 안전하게 읽습니다. 파싱에
        /// 실패하면(값이 없거나 숫자가 아니면) <paramref name="fallback"/>을 반환합니다.
        /// </summary>
        private static double ReadDouble(IReadOnlyDictionary<string, object?> properties, string key, double fallback)
        {
            if (!properties.TryGetValue(key, out var raw) || raw is null)
            {
                return fallback;
            }

            if (raw is JsonElement je)
            {
                return je.ValueKind switch
                {
                    JsonValueKind.Number when je.TryGetDouble(out var n) => n,
                    JsonValueKind.String when double.TryParse(je.GetString(), out var n) => n,
                    _ => fallback,
                };
            }

            return raw switch
            {
                double d => d,
                int i => i,
                string s when double.TryParse(s, out var n) => n,
                _ => fallback,
            };
        }

        /// <summary>(NR-03b) <see cref="JsonElement"/>로 채워진 값을 원본에 가까운 CLR 값(문자열)으로 풀어냅니다. JsonElement가 아니면 그대로 반환합니다.</summary>
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
