using System.Text.Json;
using NodeSharp.Contracts.Models;
using NodeSharp.Runner;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="StartupSequencer"/>(RN-01a)에 대한 테스트입니다. 완료 기준(03번 Step맵 RN-01a, 사용자
/// 확인으로 좁힌 범위): "device.json→sequences.json→flows.json→dashboard.json 로딩 순서가 항상
/// 고정되는지, 파일 하나가 없거나 손상돼도 해당 단계만 격리되고 나머지 단계는 계속 진행되는지 확인".
/// </summary>
public class StartupSequencerTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nodesharp-startup-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteValidFiles(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "device.json"), "{}");   // RN-01a 범위에서는 존재 여부만 확인
        File.WriteAllText(Path.Combine(dir, "sequences.json"),
            JsonSerializer.Serialize(new List<SequenceDefinition>()));
        // (★ EC-05 확장) flows.json은 단일 FlowDefinition이 아니라 목록(Flow 탭 개수만큼) — StartupSequencer 클래스 주석 참고.
        File.WriteAllText(Path.Combine(dir, "flows.json"),
            JsonSerializer.Serialize(new List<FlowDefinition> { new("f1", "빈 플로우", new List<NodeConfig>(), new List<Wire>()) }));
        File.WriteAllText(Path.Combine(dir, "dashboard.json"),
            JsonSerializer.Serialize(new DashboardDefinition(new List<DashboardTabDto>())));
    }

    [Fact]
    public async Task 완료_기준_직접_검증__4개_파일을_고정된_순서로_로딩한다()
    {
        var dir = NewTempDir();
        try
        {
            WriteValidFiles(dir);
            var sequencer = new StartupSequencer();

            var results = await sequencer.RunAsync(dir, CancellationToken.None);

            Assert.Equal(4, results.Count);
            Assert.Equal("device.json", results[0].FileName);
            Assert.Equal("sequences.json", results[1].FileName);
            Assert.Equal("flows.json", results[2].FileName);
            Assert.Equal("dashboard.json", results[3].FileName);
            Assert.All(results, r => Assert.True(r.Succeeded, $"{r.FileName}: {r.ErrorMessage}"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task 완료_기준_직접_검증__device_json이_없어도_나머지_3개_단계는_격리되어_계속_진행된다()
    {
        var dir = NewTempDir();
        try
        {
            WriteValidFiles(dir);
            File.Delete(Path.Combine(dir, "device.json"));   // 첫 단계를 일부러 없앰
            var sequencer = new StartupSequencer();

            var results = await sequencer.RunAsync(dir, CancellationToken.None);

            Assert.False(results[0].Succeeded);              // device.json 단계만 실패
            Assert.NotNull(results[0].ErrorMessage);
            Assert.True(results[1].Succeeded);                // sequences.json은 계속 진행
            Assert.True(results[2].Succeeded);                // flows.json도 계속 진행
            Assert.True(results[3].Succeeded);                // dashboard.json도 계속 진행
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task 완료_기준_직접_검증__flows_json이_손상돼도_dashboard_json_단계는_계속_시도된다()
    {
        var dir = NewTempDir();
        try
        {
            WriteValidFiles(dir);
            File.WriteAllText(Path.Combine(dir, "flows.json"), "이건 유효한 JSON이 아닙니다");   // 중간 단계 손상
            var sequencer = new StartupSequencer();

            var results = await sequencer.RunAsync(dir, CancellationToken.None);

            Assert.True(results[0].Succeeded);   // device.json
            Assert.True(results[1].Succeeded);   // sequences.json
            Assert.False(results[2].Succeeded);  // flows.json — 손상돼 실패
            Assert.True(results[3].Succeeded);   // dashboard.json — flows.json 실패에 영향받지 않고 계속 시도되어 성공
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task 완료_기준_직접_검증__모든_파일이_없어도_예외_없이_4개_결과가_고정_순서로_반환된다()
    {
        var dir = NewTempDir();   // 빈 디렉터리 — 파일 하나도 없음
        try
        {
            var sequencer = new StartupSequencer();

            var results = await sequencer.RunAsync(dir, CancellationToken.None);

            Assert.Equal(4, results.Count);
            Assert.Equal(new[] { "device.json", "sequences.json", "flows.json", "dashboard.json" },
                results.Select(r => r.FileName));
            Assert.All(results, r => Assert.False(r.Succeeded));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
