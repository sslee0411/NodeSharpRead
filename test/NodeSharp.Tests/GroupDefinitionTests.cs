using System.Text.Json;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="GroupDefinition"/>(EC-10, 03번 Step맵 EC-10 desc)에 대한 단위 테스트입니다.
/// 완료 기준: 노드 3개를 그룹으로 묶어 저장 후 재로드해도 소속·이름·접힘 상태가 유지되는지 확인
/// (이 테스트는 그중 "모델이 System.Text.Json으로 왕복해도 데이터가 보존되는지"만 담당 — 실제
/// 캔버스 저장/로드·화면 축약 표시는 WPF Editor 몫이라 이 헤드리스 프로젝트 테스트 범위 밖).
/// </summary>
public class GroupDefinitionTests
{
    [Fact]
    public void Collapsed와_Color_기본값이_생성자에서_올바르게_설정된다()
    {
        var group = new GroupDefinition(
            Id: "g1", Name: "고온 감시",
            MemberNodeIds: new List<string> { "n1", "n2", "n3" });

        Assert.Equal("g1", group.Id);
        Assert.Equal("고온 감시", group.Name);
        Assert.Equal(3, group.MemberNodeIds.Count);
        Assert.False(group.Collapsed);
        Assert.Null(group.Color);
    }

    [Fact]
    public void SystemTextJson_왕복_시_모든_필드가_보존된다()
    {
        var original = new GroupDefinition(
            Id: "g1", Name: "고온 감시",
            MemberNodeIds: new List<string> { "n1", "n2", "n3" },
            Collapsed: true,
            Color: "#3B82F6");

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<GroupDefinition>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Id, restored!.Id);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.MemberNodeIds, restored.MemberNodeIds);
        Assert.Equal(original.Collapsed, restored.Collapsed);
        Assert.Equal(original.Color, restored.Color);
    }

    [Fact]
    public void with_식으로_Collapsed만_바꿔도_나머지_필드는_그대로_유지된다()
    {
        // 캔버스에서 접기 버튼을 누르면 FlowCanvasView가 이 패턴(group with { Collapsed = ... })으로
        // 갱신한다 — record 불변성 때문에 항상 새 인스턴스로 교체해야 한다(NodeConfig와 동일한 원칙).
        var expanded = new GroupDefinition(
            Id: "g1", Name: "고온 감시",
            MemberNodeIds: new List<string> { "n1", "n2", "n3" });

        var collapsed = expanded with { Collapsed = true };

        Assert.False(expanded.Collapsed); // 원본은 불변 — 그대로 유지
        Assert.True(collapsed.Collapsed);
        Assert.Equal(expanded.Id, collapsed.Id);
        Assert.Equal(expanded.Name, collapsed.Name);
        Assert.Equal(expanded.MemberNodeIds, collapsed.MemberNodeIds);
    }
}
