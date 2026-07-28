using System.Text.Json;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="Wire"/>(CT-02b, 02번 설계 문서 2번 탭 카드 2)에 대한 단위 테스트입니다.
/// 완료 기준: flows.json 전체 필드를 포함해 System.Text.Json으로 직렬화/역직렬화 왕복 시
/// 데이터 손실이 없는지 확인.
/// </summary>
public class WireTests
{
    [Fact]
    public void 생성자로_4개_필드가_모두_설정된다()
    {
        var wire = new Wire(SourceNodeId: "n1", SourcePort: 0, TargetNodeId: "n2", TargetPort: 1);

        Assert.Equal("n1", wire.SourceNodeId);
        Assert.Equal(0, wire.SourcePort);
        Assert.Equal("n2", wire.TargetNodeId);
        Assert.Equal(1, wire.TargetPort);
    }

    [Fact]
    public void 모든_필드가_같으면_record_값_동등성으로_같다고_판단한다()
    {
        var a = new Wire("n1", 0, "n2", 1);
        var b = new Wire("n1", 0, "n2", 1);

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void SystemTextJson_왕복_시_모든_필드가_보존된다()
    {
        var original = new Wire(SourceNodeId: "n1", SourcePort: 2, TargetNodeId: "n2", TargetPort: 3);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<Wire>(json);

        Assert.Equal(original, restored);
    }
}
