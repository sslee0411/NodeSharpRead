using System.Text.Json;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Runner;

/// <summary>
/// Class명 : 기동 순서 조율자
/// 역활 및 기능 : device.json→sequences.json→flows.json→dashboard.json을 고정된 순서로 로딩하고, 파일 하나가 손상돼도 나머지 로딩을 막지 않는 조율자
///
/// (RN-01a) NodeSharp.Runner가 기동할 때 <c>device.json→sequences.json→flows.json→dashboard.json</c>
/// 순서를 항상 고정으로 지키며 로딩합니다(02번 문서 3번 탭 카드8 <c>Program.cs StartupAsync</c>
/// 의사코드 — "참조 대상 → 참조하는 쪽" 순서 원칙). 각 파일은 개별 단계로 분리돼 있어, 파일 하나가
/// 없거나 손상돼도 예외를 밖으로 던지지 않고 <see cref="StartupStageResult.Succeeded"/>를
/// <c>false</c>로 기록한 뒤 다음 단계를 계속 진행합니다(카드8 마지막 줄 "해당 단계만 빈 구조로
/// 시작 + 경고 로그로 격리" 원칙 그대로).
/// </summary>
/// <remarks>
/// <b>RN-01a 범위 한정</b>(사용자 확인): 이 클래스는 파일을 읽어 각 모델로 역직렬화하는 것까지만
/// 다룹니다. 아래 두 가지는 명시적으로 범위 밖입니다.
/// <list type="bullet">
/// <item><b>device.json의 실제 구조 트리 파싱</b> — <c>IStructureService</c> 구현체는 Phase 9
/// (<c>ED-D01</c> 구조 트리, <c>ED-D03</c> device.json 저장)에서야 만들어지므로, 지금은 파일
/// 존재 여부만 확인해 순서상 자리를 지킵니다. "로딩 순서를 바꾸면 TagRef 참조 실패가 재현되는지"
/// 검증은 그 실제 서비스가 생긴 뒤(Phase 9 이후)로 미룹니다.</item>
/// <item><b>flows.json 손상 시 다세대 백업 자동 폴백</b> — <c>OP-09</c>(Phase 14, 다세대 백업/복원)에
/// 의존하므로 <c>RN-01b</c>로 분리했습니다. 이 클래스는 flows.json이 손상됐을 때 해당 단계만
/// 실패로 기록할 뿐, 백업에서 되살리지는 않습니다.</item>
/// </list>
/// flows.json 스키마 마이그레이션(<c>RT-11</c> <c>ConfigMigration</c>)도 실제 저장 포맷(<c>FlowFileHeader</c>
/// 포함 여부)이 RN-02 배포 연동 시점에 확정될 예정이라 이번 Step에서는 연결하지 않았습니다.
/// </remarks>
/// <example>
/// <code>
/// var sequencer = new StartupSequencer();
/// IReadOnlyList&lt;StartupStageResult&gt; results = await sequencer.RunAsync(@"C:\NodeSharpRead\data", ct);
/// // results[0].FileName == "device.json", results[1] == "sequences.json",
/// // results[2] == "flows.json", results[3] == "dashboard.json" — 항상 이 순서
/// // sequences.json이 없어도 flows.json/dashboard.json 단계는 계속 시도됨(격리)
/// </code>
/// </example>
public sealed class StartupSequencer
{
    /// <summary>
    /// <paramref name="baseDirectory"/> 아래의 4개 파일을 고정 순서로 로딩합니다. 각 단계는 실패해도
    /// 예외를 던지지 않고 다음 단계로 넘어갑니다.
    /// </summary>
    /// <returns>4개 단계 각각의 결과를 로딩 순서 그대로 담은 목록.</returns>
    public async Task<IReadOnlyList<StartupStageResult>> RunAsync(string baseDirectory, CancellationToken ct)
    {
        var results = new List<StartupStageResult>
        {
            // 1) 구조 트리 — TagRef가 가리킬 대상. 실제 파싱은 Phase 9(ED-D01/ED-D03) 몫이라
            //    지금은 파일 존재 여부만 확인해 순서상 첫 자리를 지킨다(위 remarks 참고).
            await RunStageAsync("device.json", () =>
            {
                var path = Path.Combine(baseDirectory, "device.json");
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        "device.json이 없습니다(Phase 9 이전에는 실제 구조 트리 파싱을 하지 않으므로, 존재 확인만 실패해도 다음 단계는 계속 진행됩니다).",
                        path);
                }
                return Task.CompletedTask;
            }),

            // 2) Sequence 정의 — 구조 트리(태그)만 참조하고 Flow와는 무관해 Flow보다 먼저 로드 가능.
            await RunStageAsync("sequences.json", async () =>
            {
                var path = Path.Combine(baseDirectory, "sequences.json");
                var json = await File.ReadAllTextAsync(path, ct);
                _ = JsonSerializer.Deserialize<List<SequenceDefinition>>(json)
                    ?? throw new InvalidOperationException("sequences.json 역직렬화 결과가 null입니다.");
            }),

            // 3) Flow 정의 — 구조 트리 + Sequence를 함께 참조할 수 있어 마지막 순번 바로 앞.
            await RunStageAsync("flows.json", async () =>
            {
                var path = Path.Combine(baseDirectory, "flows.json");
                var json = await File.ReadAllTextAsync(path, ct);
                _ = JsonSerializer.Deserialize<FlowDefinition>(json)
                    ?? throw new InvalidOperationException("flows.json 역직렬화 결과가 null입니다.");
            }),

            // 4) Dashboard — Flow/Tag 값을 구독하는 소비자 위치라 항상 가장 마지막.
            await RunStageAsync("dashboard.json", async () =>
            {
                var path = Path.Combine(baseDirectory, "dashboard.json");
                var json = await File.ReadAllTextAsync(path, ct);
                _ = JsonSerializer.Deserialize<DashboardDefinition>(json)
                    ?? throw new InvalidOperationException("dashboard.json 역직렬화 결과가 null입니다.");
            }),
        };

        return results;
    }

    private static async Task<StartupStageResult> RunStageAsync(string fileName, Func<Task> action)
    {
        try
        {
            await action();
            return new StartupStageResult(fileName, Succeeded: true, ErrorMessage: null);
        }
        catch (Exception ex)
        {
            return new StartupStageResult(fileName, Succeeded: false, ErrorMessage: ex.Message);
        }
    }
}
