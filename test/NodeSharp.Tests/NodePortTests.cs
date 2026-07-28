using System.Text.Json;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="NodePort"/>(CT-02b, 02번 설계 문서 2번 탭 카드 2)에 대한 단위 테스트입니다.
/// </summary>
public class NodePortTests
{
    [Fact]
    public void 생성자로_Index와_Label이_설정된다()
    {
        var port = new NodePort(Index: 0, Label: "온도 > 80");

        Assert.Equal(0, port.Index);
        Assert.Equal("온도 > 80", port.Label);
    }

    [Fact]
    public void SystemTextJson_왕복_시_모든_필드가_보존된다()
    {
        var original = new NodePort(Index: 2, Label: "그 외");

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<NodePort>(json);

        Assert.Equal(original, restored);
    }
}
