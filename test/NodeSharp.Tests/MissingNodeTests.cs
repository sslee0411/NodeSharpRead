using NodeSharp.Contracts.Models;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="MissingNode"/>(RT-02a, 02번 설계 문서 2번 탭 카드4·3번 탭 카드6)에 대한 단위 테스트입니다.
/// </summary>
public class MissingNodeTests
{
    [Fact]
    public void MissingNode는_원본_Id와_Type을_보존한다()
    {
        var node = new MissingNode("n2", "mqtt-in-legacy");

        Assert.Equal("n2", node.Id);
        Assert.Equal("mqtt-in-legacy", node.Type);
    }

    [Fact]
    public void MissingNode는_Name에_알_수_없는_타입임을_표시한다()
    {
        var node = new MissingNode("n2", "mqtt-in-legacy");

        Assert.Contains("mqtt-in-legacy", node.Name);
    }

    [Fact]
    public void MissingNode는_입력포트와_출력포트가_비어있다()
    {
        var node = new MissingNode("n2", "no-such-type");

        Assert.Empty(node.InputPorts);
        Assert.Empty(node.OutputPorts);
    }

    [Fact]
    public async Task MissingNode는_OnInputAsync_호출_시_예외_없이_입력을_그냥_버린다()
    {
        var node = new MissingNode("n2", "no-such-type");

        var ex = await Record.ExceptionAsync(() => node.OnInputAsync(new Msg(), null!, CancellationToken.None));

        Assert.Null(ex);
    }
}
