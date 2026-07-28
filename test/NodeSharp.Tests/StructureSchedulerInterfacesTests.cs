using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="IStructureService"/>/<see cref="IFlowNodeIndex"/>/<see cref="IScheduler"/>/
/// <see cref="NodeRef"/>(CT-04b, 02번 설계 문서 8번 탭 카드 7·13, 10번 탭 카드 5, 6번 탭 카드 5)에
/// 대한 단위 테스트입니다. 인터페이스 자체는 동작이 없으므로, 여기서는 최소 스텁 구현이 실제로
/// 컴파일·동작하는지와 NodeRef가 두 인터페이스(FindNodesByTagRef/FindNodesBySequenceId)에서
/// 동일하게 재사용되는지를 확인합니다.
/// </summary>
public class StructureSchedulerInterfacesTests
{
    /// <summary>테스트 전용 <see cref="IStructureService"/> 스텁 — 태그 1개(스케일 포함)와 그 태그를 참조하는 노드 목록을 고정으로 보관.</summary>
    private sealed class FakeStructureService : IStructureService
    {
        private readonly TagRuntimeInfo _tag = new(
            Id: "tag-1", Name: "토출압력", ParentMapId: "map-1",
            Offset: 0, BufType: BufFieldType.FloatLE,
            Scale: new ScaleRuntimeInfo(RawMin: 0, RawMax: 4095, EngMin: 0, EngMax: 10),
            Alarm: null);
        private readonly SemaphoreSlim _gate = new(1, 1);
        public bool WriteCalled { get; private set; }

        public TagRuntimeInfo GetTag(string tagId) => _tag;
        public IEnumerable<TagRuntimeInfo> GetTagsByMap(string mapId) => new[] { _tag };
        public Task<byte[]> ReadRawAsync(string tagId, CancellationToken ct) => Task.FromResult(new byte[] { 1, 2, 3, 4 });
        public Task<bool> WriteRawAsync(string tagId, byte[] raw, CancellationToken ct) { WriteCalled = true; return Task.FromResult(true); }
        public object? ApplyScale(TagRuntimeInfo tag, byte[] raw) => 5.0; // 테스트용 고정값(실제 선형 변환은 Runtime 구현체 책임)
        public byte[] ApplyInverseScale(TagRuntimeInfo tag, object? engValue) => new byte[] { 9, 9, 9, 9 };
        public SemaphoreSlim GetWriteGate(string mapId) => _gate;
        public IReadOnlyList<NodeRef> FindNodesByTagRef(string tagId) =>
            new[] { new NodeRef("flow-1", "n1", "PlcTagReadNode1") };
    }

    /// <summary>테스트 전용 <see cref="IFlowNodeIndex"/> 스텁 — 항상 노드 2개를 반환.</summary>
    private sealed class FakeFlowNodeIndex : IFlowNodeIndex
    {
        public IReadOnlyList<NodeRef> FindNodesBySequenceId(string sequenceId) =>
            new[] { new NodeRef("flow-1", "n2", "SequenceTriggerNode1"), new NodeRef("flow-2", "n3", "SequenceTriggerNode2") };
    }

    /// <summary>테스트 전용 <see cref="IScheduler"/> 스텁 — SchedulePeriodic/ScheduleCron/Unschedule 호출 여부와 ownerId를 기록.</summary>
    private sealed class FakeScheduler : IScheduler
    {
        public List<string> Scheduled { get; } = new();
        public List<string> Unscheduled { get; } = new();

        public void SchedulePeriodic(string ownerId, TimeSpan interval, Func<Task> callback) => Scheduled.Add(ownerId);
        public void ScheduleCron(string ownerId, string cronExpression, Func<Task> callback) => Scheduled.Add(ownerId);
        public void Unschedule(string ownerId) => Unscheduled.Add(ownerId);
    }

    [Fact]
    public async Task IStructureService_GetWriteGate로_얻은_락으로_WriteRawAsync를_보호할_수_있다()
    {
        var structure = new FakeStructureService();
        var gate = structure.GetWriteGate("map-1");

        await gate.WaitAsync(CancellationToken.None);
        try
        {
            var bytes = structure.ApplyInverseScale(structure.GetTag("tag-1"), 85.0);
            await structure.WriteRawAsync("tag-1", bytes, CancellationToken.None);
        }
        finally { gate.Release(); }

        Assert.True(structure.WriteCalled);
    }

    [Fact]
    public void IStructureService_FindNodesByTagRef와_IFlowNodeIndex_FindNodesBySequenceId는_같은_NodeRef_타입을_반환한다()
    {
        IStructureService structure = new FakeStructureService();
        IFlowNodeIndex flowNodeIndex = new FakeFlowNodeIndex();

        IReadOnlyList<NodeRef> blockers = structure.FindNodesByTagRef("tag-1");
        IReadOnlyList<NodeRef> callers = flowNodeIndex.FindNodesBySequenceId("seq-1");

        Assert.Single(blockers);
        Assert.Equal(new NodeRef("flow-1", "n1", "PlcTagReadNode1"), blockers[0]);
        Assert.Equal(2, callers.Count);
    }

    [Fact]
    public void NodeRef는_record이므로_필드가_같으면_값이_같다()
    {
        var a = new NodeRef("flow-1", "n1", "PlcTagReadNode1");
        var b = new NodeRef("flow-1", "n1", "PlcTagReadNode1");

        Assert.Equal(a, b);
    }

    [Fact]
    public void IScheduler_SchedulePeriodic로_등록한_ownerId는_Unschedule로_취소_기록된다()
    {
        var scheduler = new FakeScheduler();

        scheduler.SchedulePeriodic("node-1", TimeSpan.FromSeconds(5), () => Task.CompletedTask);
        scheduler.ScheduleCron("node-2", "0 0 * * * *", () => Task.CompletedTask);
        scheduler.Unschedule("node-1");

        Assert.Equal(new[] { "node-1", "node-2" }, scheduler.Scheduled);
        Assert.Equal(new[] { "node-1" }, scheduler.Unscheduled);
    }
}
