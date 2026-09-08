using NodeSharp.Contracts.Enums;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="SequenceCheckpointStore"/>(SQ-05)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 SQ-05,
/// "체크포인트 인프라만 지금 구현" 범위): 저장(SaveAsync) → 조회(TryLoadAsync) → 삭제(ClearAsync)
/// 왕복이 임시 디렉터리에서 정상 동작하는지, 같은 SequenceId로 다시 저장하면 갱신(overwrite)되는지,
/// 여러 SequenceId가 한 파일(sequences.checkpoint.json) 안에서 서로 섞이지 않는지 확인합니다.
/// </summary>
public class SequenceCheckpointStoreTests
{
    private static string NewTempDataDirectory() =>
        Path.Combine(Path.GetTempPath(), "NodeSharpTests_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task 저장한_체크포인트를_같은_SequenceId로_조회하면_그대로_돌아온다()
    {
        var store = new SequenceCheckpointStore();
        var dataDirectory = NewTempDataDirectory();

        try
        {
            var checkpoint = new SequenceCheckpointStore.Checkpoint("seq-1", "2단계", SequenceState.Running, DateTime.UtcNow);
            await store.SaveAsync(checkpoint, dataDirectory);

            var loaded = await store.TryLoadAsync("seq-1", dataDirectory);

            Assert.NotNull(loaded);
            Assert.Equal("seq-1", loaded!.SequenceId);
            Assert.Equal("2단계", loaded.CurrentStepId);
            Assert.Equal(SequenceState.Running, loaded.State);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task 존재하지_않는_SequenceId를_조회하면_null을_반환한다()
    {
        var store = new SequenceCheckpointStore();
        var dataDirectory = NewTempDataDirectory();

        try
        {
            var loaded = await store.TryLoadAsync("없는-시퀀스", dataDirectory);

            Assert.Null(loaded);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task 같은_SequenceId로_다시_저장하면_기존_체크포인트를_덮어쓴다()
    {
        var store = new SequenceCheckpointStore();
        var dataDirectory = NewTempDataDirectory();

        try
        {
            await store.SaveAsync(new SequenceCheckpointStore.Checkpoint("seq-1", "1단계", SequenceState.Running, DateTime.UtcNow), dataDirectory);
            await store.SaveAsync(new SequenceCheckpointStore.Checkpoint("seq-1", "3단계", SequenceState.Completed, DateTime.UtcNow), dataDirectory);

            var loaded = await store.TryLoadAsync("seq-1", dataDirectory);

            Assert.NotNull(loaded);
            Assert.Equal("3단계", loaded!.CurrentStepId);
            Assert.Equal(SequenceState.Completed, loaded.State);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task 서로_다른_SequenceId_체크포인트는_한_파일_안에서도_서로_섞이지_않는다()
    {
        var store = new SequenceCheckpointStore();
        var dataDirectory = NewTempDataDirectory();

        try
        {
            await store.SaveAsync(new SequenceCheckpointStore.Checkpoint("seq-1", "1단계", SequenceState.Running, DateTime.UtcNow), dataDirectory);
            await store.SaveAsync(new SequenceCheckpointStore.Checkpoint("seq-2", "설정단계", SequenceState.Faulted, DateTime.UtcNow), dataDirectory);

            var loaded1 = await store.TryLoadAsync("seq-1", dataDirectory);
            var loaded2 = await store.TryLoadAsync("seq-2", dataDirectory);

            Assert.NotNull(loaded1);
            Assert.NotNull(loaded2);
            Assert.Equal("1단계", loaded1!.CurrentStepId);
            Assert.Equal("설정단계", loaded2!.CurrentStepId);
            Assert.Equal(SequenceState.Faulted, loaded2.State);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ClearAsync로_지운_체크포인트는_다시_조회하면_null이다()
    {
        var store = new SequenceCheckpointStore();
        var dataDirectory = NewTempDataDirectory();

        try
        {
            await store.SaveAsync(new SequenceCheckpointStore.Checkpoint("seq-1", "1단계", SequenceState.Running, DateTime.UtcNow), dataDirectory);
            await store.ClearAsync("seq-1", dataDirectory);

            var loaded = await store.TryLoadAsync("seq-1", dataDirectory);

            Assert.Null(loaded);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task 체크포인트_파일이_아직_없으면_조회는_예외없이_null을_반환한다()
    {
        var store = new SequenceCheckpointStore();
        var dataDirectory = NewTempDataDirectory();

        var loaded = await store.TryLoadAsync("seq-1", dataDirectory);

        Assert.Null(loaded);
    }
}
