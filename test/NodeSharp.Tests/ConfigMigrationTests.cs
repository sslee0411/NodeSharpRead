using System.Text.Json;
using Newtonsoft.Json.Linq;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Registry;
using NodeSharp.Runtime;
using NodeSharp.Util.Config.Migration;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="ConfigMigration"/>/<see cref="MigrationRule"/>/<see cref="FlowFileHeader"/>(RT-11)의
/// 동작을 검증합니다 — 규칙 등록·체이닝·백업 같은 기본 동작뿐 아니라, 완료 기준(03번 Step맵 RT-11)이
/// 요구하는 "구버전 스키마 샘플 파일을 로드했을 때 최신 스키마로 자동 변환되어 정상 배포까지
/// 이어지는지"를 실제 "v1 레거시 wires 인덱스 배열" 예시 JSON으로 끝까지(FlowEngine.DeployAsync 성공)
/// 직접 검증합니다.
/// </summary>
public class ConfigMigrationTests
{
    /// <summary>
    /// v1(가상의 구버전) → v2(현재 <see cref="Wire"/> 레코드 스키마) 변환 규칙 — MigrationRule.cs
    /// XML 예제와 동일한 로직. "wires": [[0,1]] 처럼 노드 배열 인덱스 쌍으로 저장된 v1 연결선을,
    /// nodes 배열의 실제 id로 찾아 v2의 SourceNodeId/TargetNodeId 문자열 Wire 배열로 바꾼다.
    /// </summary>
    private static readonly MigrationRule V1ToV2Rule = new(FromVersion: 1, ToVersion: 2, Transform: v1 =>
    {
        var nodesV1 = (JArray)v1["nodes"]!;
        var wiresV1 = (JArray)v1["wires"]!;

        var nodesV2 = new JArray();
        foreach (var n in nodesV1)
        {
            nodesV2.Add(new JObject
            {
                ["Id"] = n["id"],
                ["Type"] = n["type"],
                ["Name"] = n["name"],
                ["FlowId"] = v1["id"],
                ["Properties"] = new JObject(),
            });
        }

        var wiresV2 = new JArray();
        foreach (var pair in wiresV1)
        {
            var fromIdx = (int)pair[0]!;
            var toIdx = (int)pair[1]!;
            wiresV2.Add(new JObject
            {
                ["SourceNodeId"] = nodesV1[fromIdx]!["id"],
                ["SourcePort"] = 0,
                ["TargetNodeId"] = nodesV1[toIdx]!["id"],
                ["TargetPort"] = 0,
            });
        }

        return new JObject
        {
            ["Id"] = v1["id"],
            ["Name"] = v1["name"],
            ["Nodes"] = nodesV2,
            ["Wires"] = wiresV2,
        };
    });

    private static ConfigMigration BuildMigration()
    {
        var migration = new ConfigMigration();
        migration.RegisterRule(V1ToV2Rule);
        return migration;
    }

    [Fact]
    public void 등록된_규칙으로_한_단계_마이그레이션하면_스키마가_바뀐다()
    {
        var migration = BuildMigration();
        var v1Json = """{"id":"f1","name":"테스트","nodes":[{"id":"n1","type":"t","name":"A"},{"id":"n2","type":"t","name":"B"}],"wires":[[0,1]]}""";

        var result = migration.Apply(v1Json, fromVersion: 1, toVersion: 2);
        var parsed = JObject.Parse(result);

        Assert.Equal("f1", (string?)parsed["Id"]);
        Assert.Single((JArray)parsed["Wires"]!);
        Assert.Equal("n1", (string?)parsed["Wires"]![0]!["SourceNodeId"]);
        Assert.Equal("n2", (string?)parsed["Wires"]![0]!["TargetNodeId"]);
    }

    [Fact]
    public void 두_버전을_건너뛰면_등록된_규칙_2개를_순서대로_체이닝한다()
    {
        var migration = new ConfigMigration();
        migration.RegisterRule(new MigrationRule(1, 2, v1 =>
        {
            var c = (JObject)v1.DeepClone();
            c["step"] = 2;
            return c;
        }));
        migration.RegisterRule(new MigrationRule(2, 3, v2 =>
        {
            var c = (JObject)v2.DeepClone();
            c["step"] = 3;
            return c;
        }));

        var result = migration.Apply("""{"step":1}""", fromVersion: 1, toVersion: 3);
        var parsed = JObject.Parse(result);

        Assert.Equal(3, (int)parsed["step"]!);
    }

    [Fact]
    public void 같은_버전이면_아무_변환_없이_원본을_그대로_반환한다()
    {
        var migration = BuildMigration();
        var raw = """{"id":"f1"}""";

        var result = migration.Apply(raw, fromVersion: 2, toVersion: 2);

        Assert.Equal(raw, result);
    }

    [Fact]
    public void 중간_버전에서_규칙을_찾지_못하면_예외를_던진다()
    {
        var migration = new ConfigMigration();

        Assert.Throws<InvalidOperationException>(() => migration.Apply("""{}""", fromVersion: 1, toVersion: 2));
    }

    [Fact]
    public void MigrateIfNeeded는_헤더가_이미_최신이면_원본을_그대로_반환한다()
    {
        var migration = BuildMigration();
        var raw = """{"id":"f1"}""";
        var header = new FlowFileHeader(SchemaVersion: FlowFileHeader.CurrentSchemaVersion, SavedAt: DateTime.Now);

        var result = migration.MigrateIfNeeded(raw, header);

        Assert.Equal(raw, result);
    }

    [Fact]
    public void MigrateIfNeeded는_헤더가_구버전이면_최신_스키마까지_마이그레이션한다()
    {
        var migration = BuildMigration();
        var v1Json = """{"id":"f1","name":"테스트","nodes":[{"id":"n1","type":"t","name":"A"},{"id":"n2","type":"t","name":"B"}],"wires":[[0,1]]}""";
        var header = new FlowFileHeader(SchemaVersion: 1, SavedAt: DateTime.Now);

        var result = migration.MigrateIfNeeded(v1Json, header);
        var parsed = JObject.Parse(result);

        Assert.Single((JArray)parsed["Wires"]!);
    }

    [Fact]
    public void BackupOriginal은_파일이_있으면_타임스탬프_백업본을_만든다()
    {
        var migration = new ConfigMigration();
        var tempPath = Path.Combine(Path.GetTempPath(), $"nodeshaprread-rt11-{Guid.NewGuid():N}.json");
        File.WriteAllText(tempPath, "{}");
        try
        {
            migration.BackupOriginal(tempPath);

            var backups = Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileName(tempPath)}.bak.*");
            Assert.Single(backups);
        }
        finally
        {
            File.Delete(tempPath);
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileName(tempPath)}.bak.*"))
            {
                File.Delete(f);
            }
        }
    }

    [Fact]
    public void BackupOriginal은_파일이_없으면_예외_없이_아무것도_하지_않는다()
    {
        var migration = new ConfigMigration();
        var missingPath = Path.Combine(Path.GetTempPath(), $"nodeshaprread-rt11-missing-{Guid.NewGuid():N}.json");

        var ex = Record.Exception(() => migration.BackupOriginal(missingPath));

        Assert.Null(ex);
    }

    /// <summary>테스트용 최소 IFlowNode — 배포 성공 여부만 확인하면 되므로 아무 동작도 하지 않는다.</summary>
    private sealed class NoOpTestNode : IFlowNode
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Type => "test-node";
        public string Name { get; set; } = string.Empty;
        public IReadOnlyList<NodePort> InputPorts { get; } = Array.Empty<NodePort>();
        public IReadOnlyList<NodePort> OutputPorts { get; } = Array.Empty<NodePort>();
        public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;
    }

    [Fact]
    public async Task 완료_기준_직접_검증__v1_레거시_파일을_마이그레이션하면_최신_스키마로_역직렬화되어_정상_배포까지_이어진다()
    {
        // 완료 기준(03번 Step맵 RT-11): "구버전 스키마 샘플 파일을 로드했을 때 최신 스키마로 자동
        // 변환되어 정상 배포까지 이어지는지 확인" — v1의 인덱스 기반 wires를 실제로 마이그레이션한
        // 결과를 FlowDefinition으로 역직렬화해, FlowEngine.DeployAsync가 예외 없이 성공하고 두
        // 노드가 모두 배포되며 Wire 연결도 살아있는지까지 끝까지 확인한다.
        var migration = BuildMigration();
        var v1Json = """{"id":"flow-1","name":"레거시 플로우","nodes":[{"id":"n1","type":"test-node","name":"A"},{"id":"n2","type":"test-node","name":"B"}],"wires":[[0,1]]}""";
        var header = new FlowFileHeader(SchemaVersion: 1, SavedAt: DateTime.Now);

        var migratedJson = migration.MigrateIfNeeded(v1Json, header);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var flow = JsonSerializer.Deserialize<FlowDefinition>(migratedJson, options);
        Assert.NotNull(flow);
        Assert.Equal(2, flow!.Nodes.Count);
        Assert.Single(flow.Wires);

        var registry = new NodeTypeRegistry(contractsVersion: "1.0.0");
        registry.TryRegister(new PluginManifest("test-node", "1.0.0", RequiredContractsVersion: "1.0.0"), typeof(NoOpTestNode));
        var engine = new FlowEngine(registry);

        await engine.DeployAsync(flow, DeployMode.Full, CancellationToken.None);

        Assert.True(engine.Nodes.ContainsKey("n1"));
        Assert.True(engine.Nodes.ContainsKey("n2"));
        Assert.Empty(engine.FailedNodeIds);
    }
}
