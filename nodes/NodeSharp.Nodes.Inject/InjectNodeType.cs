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
    /// 동기화합니다. <see cref="INodeTypeDescriptor.PropertySchema"/>에는 "payload" 필드 1개만
    /// 있습니다(NR-03a 범위 — Trigger 종류 선택은 아직 Manual 하나뿐이라 선택 필드를 추가하지
    /// 않음, NR-03b에서 Interval이 추가될 때 함께 확장 예정). "payload" 값은 노드 인스턴스가 직접
    /// 읽지 않습니다 — <see cref="InjectNode.TriggerAsync"/>가 외부에서 명시적으로 받는 매개변수라,
    /// 이 필드는 사용자가 캔버스에서 설정해둔 "기본 발행 값"을 문서화하는 용도이며 실제 자동
    /// 배선(트리거 호출부가 이 값을 읽어 전달하는 것)은 향후 LK-02(Editor→Runner 클릭 전달) 또는
    /// NR-03b(Interval 스케줄러) 쪽 책임입니다.
    /// </summary>
    public static readonly INodeTypeDescriptor Descriptor = new InjectNodeDescriptor();

    private sealed record InjectNodeDescriptor : INodeTypeDescriptor
    {
        public string TypeName => "inject";

        public string Category => "input";

        public string IconGlyph => string.Empty;

        public int DefaultInputs => 0;

        public int DefaultOutputs => 1;

        public Func<NodeConfig, IFlowNode> Factory { get; } =
            cfg => new InjectNode { Id = cfg.Id, Name = cfg.Name };

        public IReadOnlyList<PropertyField> PropertySchema { get; } = new[]
        {
            new PropertyField(
                Key: "payload",
                Label: "Payload",
                Type: PropertyFieldType.Text,
                Required: false,
                DefaultValue: "",
                HelpText: "노드가 트리거될 때 발행할 메시지 본문(msg.payload)입니다. 비워두면 빈 " +
                           "문자열이 발행됩니다.",
                Example: "예: \"hello\" (고정 문자열), \"42\" (숫자는 지금은 문자열로 저장 — 타입 " +
                         "변환은 향후 TypedValue 지원 Step에서 추가 예정)"),
        };
    }
}
