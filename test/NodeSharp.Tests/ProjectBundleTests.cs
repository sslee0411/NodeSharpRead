using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="ProjectBundle"/>(CT-03c, 02번 설계 문서 10번 탭 카드 11)에 대한 단위 테스트입니다.
/// 완료 기준: ProjectBundle이 OP-08의 7개 저장 파일 목록을 모두 포함하는지 확인.
/// </summary>
public class ProjectBundleTests
{
    private static readonly string[] ExpectedOp08Files =
    {
        "flows.json",
        "device.json",
        "scale-library.json",
        "alarm-library.json",
        "comm-library.json",
        "sequences.json",
        "dashboard.json"
    };

    [Fact]
    public void Default는_OP08의_7개_저장_파일을_모두_포함한다()
    {
        var bundle = ProjectBundle.Default;

        Assert.Equal(7, bundle.IncludedFileNames.Count);
        foreach (var expected in ExpectedOp08Files)
            Assert.Contains(expected, bundle.IncludedFileNames);
    }

    [Fact]
    public void ExcludedFileName_기본값은_credentials_json이다()
    {
        var bundle = ProjectBundle.Default;

        Assert.Equal("credentials.json", bundle.ExcludedFileName);
        Assert.DoesNotContain("credentials.json", bundle.IncludedFileNames);
    }

    [Fact]
    public void IncludedFileNames를_직접_지정해_커스텀_번들을_만들_수_있다()
    {
        var custom = new ProjectBundle(new List<string> { "flows.json" });

        Assert.Single(custom.IncludedFileNames);
        Assert.Equal("credentials.json", custom.ExcludedFileName); // 기본값 유지
    }
}
