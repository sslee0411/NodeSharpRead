using System.Text.Json;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FlowDefinition"/>(CT-02c, 02번 설계 문서 2번 탭 카드 10)에 대한 단위 테스트입니다.
/// 완료 기준: FlowDefinition.Id/Name으로 2개 이상의 인스턴스를 생성해 탭 전환 UI(EC-05)에서
/// 구분 가능한지 확인.
/// </summary>
public class FlowDefinitionTests
{
    [Fact]
    public void 서로_다른_Id와_Name으로_두_인스턴스를_구분할_수_있다()
    {
        // 완료 기준 그대로: 탭 전환 UI(EC-05)가 여러 FlowDefinition을 Id로 구분하고
        // Name을 화면에 표시할 수 있어야 한다.
        var line1 = new FlowDefinition(
            Id: "flow-1", Name: "1호기 라인",
            Nodes: new List<NodeConfig>(), Wires: new List<Wire>());

        var line2 = new FlowDefinition(
            Id: "flow-2", Name: "2호기 라인",
            Nodes: new List<NodeConfig>(), Wires: new List<Wire>());

        Assert.NotEqual(line1.Id, line2.Id);
        Assert.NotEqual(line1.Name, line2.Name);
        Assert.NotEqual(line1, line2); // Nodes/Wires가 둘 다 빈 리스트라도 Id/Name이 달라 record 동등성도 다르다
    }

    [Fact]
    public void Disabled_기본값은_false이다()
    {
        var flow = new FlowDefinition("flow-1", "1호기 라인", new List<NodeConfig>(), new List<Wire>());

        Assert.False(flow.Disabled);
    }

    [Fact]
    public void Nodes와_Wires가_생성자에서_그대로_보관된다()
    {
        var node = new NodeConfig("n1", "inject", "시작", "flow-1", new Dictionary<string, object?>());
        var wire = new Wire("n1", 0, "n2", 0);

        var flow = new FlowDefinition("flow-1", "1호기 라인",
            Nodes: new List<NodeConfig> { node },
            Wires: new List<Wire> { wire });

        Assert.Single(flow.Nodes);
        Assert.Equal(node, flow.Nodes[0]);
        Assert.Single(flow.Wires);
        Assert.Equal(wire, flow.Wires[0]);
    }

    [Fact]
    public void SystemTextJson_왕복_시_Nodes와_Wires_목록이_통째로_보존된다()
    {
        // NodeConfig.Properties(Dictionary<string, object?>)와 달리, Nodes/Wires는
        // List<NodeConfig>/List<Wire>로 각 원소의 정적 타입 정보가 있어 System.Text.Json이
        // JsonElement가 아니라 실제 NodeConfig/Wire 객체로 복원한다(NodeConfigTests의
        // JsonElement 케이스와 대조되는 지점).
        var original = new FlowDefinition(
            Id: "flow-1", Name: "1호기 라인",
            Nodes: new List<NodeConfig>
            {
                new("n1", "inject", "시작", "flow-1", new Dictionary<string, object?>()),
                new("n2", "debug", "출력", "flow-1", new Dictionary<string, object?>()),
            },
            Wires: new List<Wire> { new("n1", 0, "n2", 0) },
            Disabled: true);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<FlowDefinition>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Id, restored!.Id);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Disabled, restored.Disabled);

        Assert.Equal(2, restored.Nodes.Count);
        Assert.Equal("n1", restored.Nodes[0].Id);
        Assert.Equal("inject", restored.Nodes[0].Type);
        Assert.Equal("n2", restored.Nodes[1].Id);

        Assert.Single(restored.Wires);
        Assert.Equal(original.Wires[0], restored.Wires[0]); // Wire는 스칼라 필드뿐이라 record 동등성이 그대로 성립
    }

    [Fact]
    public void EC05_확인_기준__List_FlowDefinition_왕복_시_탭_순서와_내용이_모두_보존된다()
    {
        // (v2.51 신설, ★ 사용자 요청) EC-05부터 flows.json의 최상위 형태가 단일 FlowDefinition이
        // 아니라 목록이다(StartupSequencer/FlowDeployer가 이 형태로 읽음) — List<FlowDefinition> 자체의
        // System.Text.Json 왕복이 탭 순서·개수·내용을 그대로 보존하는지 직접 확인한다.
        var tab1 = new FlowDefinition("f1", "1호기 라인", new List<NodeConfig>
        {
            new("n1", "inject", "시작", "f1", new Dictionary<string, object?>()),
        }, new List<Wire>());
        var tab2 = new FlowDefinition("f2", "2호기 라인(점검 중)", new List<NodeConfig>(), new List<Wire>(), Disabled: true);
        var original = new List<FlowDefinition> { tab1, tab2 };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<List<FlowDefinition>>(json);

        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Count);
        Assert.Equal("f1", restored[0].Id);
        Assert.Equal("1호기 라인", restored[0].Name);
        Assert.False(restored[0].Disabled);
        Assert.Single(restored[0].Nodes);
        Assert.Equal("f2", restored[1].Id);
        Assert.True(restored[1].Disabled);
    }

    [Fact]
    public void Groups_기본값은_null이다()
    {
        // (EC-10 신설) Groups는 선택 매개변수(기본값 null)라 기존 호출부(EC-01b~EC-05, 위 테스트들
        // 포함)가 전부 코드 변경 없이 그대로 컴파일된다는 것도 이 테스트가 함께 보증한다.
        var flow = new FlowDefinition("flow-1", "1호기 라인", new List<NodeConfig>(), new List<Wire>());

        Assert.Null(flow.Groups);
    }

    [Fact]
    public void SystemTextJson_왕복_시_Groups_목록이_통째로_보존된다()
    {
        // (EC-10 신설) Nodes/Wires와 동일하게 Groups도 List<GroupDefinition>이라 System.Text.Json이
        // JsonElement가 아니라 실제 GroupDefinition 객체로 복원한다.
        var original = new FlowDefinition(
            Id: "flow-1", Name: "1호기 라인",
            Nodes: new List<NodeConfig>
            {
                new("n1", "inject", "시작", "flow-1", new Dictionary<string, object?>()),
                new("n2", "function", "변환", "flow-1", new Dictionary<string, object?>()),
                new("n3", "debug", "출력", "flow-1", new Dictionary<string, object?>()),
            },
            Wires: new List<Wire>(),
            Groups: new List<GroupDefinition>
            {
                new(Id: "g1", Name: "전처리", MemberNodeIds: new List<string> { "n1", "n2" }, Collapsed: true),
            });

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<FlowDefinition>(json);

        Assert.NotNull(restored);
        Assert.NotNull(restored!.Groups);
        Assert.Single(restored.Groups!);
        Assert.Equal("g1", restored.Groups![0].Id);
        Assert.Equal("전처리", restored.Groups![0].Name);
        Assert.Equal(2, restored.Groups![0].MemberNodeIds.Count);
        Assert.True(restored.Groups![0].Collapsed);
    }
}
