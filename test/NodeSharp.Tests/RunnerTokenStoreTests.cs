using NodeSharp.Runner.Core;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// (LK-03) <see cref="RunnerTokenStore"/>에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 LK-03)의
/// "재발급 메뉴 실행 시 기존 토큰이 즉시 무효화되는지"를 이 클래스가 실제로 만족하는지 검증합니다:
/// ① 최초 기동 시 runner.token 파일이 없으면 새로 생성되는지 ② 이미 있으면 그 값을 그대로 읽는지
/// ③ <see cref="RunnerTokenStore.ReissueAsync"/> 이후 <see cref="RunnerTokenStore.Validate"/>가 옛
/// 토큰을 거부하고 새 토큰만 통과시키는지 ④ 재발급 결과가 파일에도 반영돼 있는지. 각 테스트는 임시
/// 디렉터리를 쓰고 끝나면 지웁니다(<see cref="FileContextStoreTests"/>와 동일한 관례).
/// </summary>
public class RunnerTokenStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "NodeSharpTests_" + Guid.NewGuid().ToString("N"));

    public RunnerTokenStoreTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadOrCreateAsync는_파일이_없으면_새_토큰을_생성해_저장한다()
    {
        var store = new RunnerTokenStore();

        await store.LoadOrCreateAsync(_tempDir, CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(store.Token));
        var savedPath = Path.Combine(_tempDir, "runner.token");
        Assert.True(File.Exists(savedPath));
        Assert.Equal(store.Token, (await File.ReadAllTextAsync(savedPath)).Trim());
    }

    [Fact]
    public async Task LoadOrCreateAsync는_파일이_이미_있으면_그_값을_그대로_읽는다()
    {
        var path = Path.Combine(_tempDir, "runner.token");
        await File.WriteAllTextAsync(path, "기존토큰값");
        var store = new RunnerTokenStore();

        await store.LoadOrCreateAsync(_tempDir, CancellationToken.None);

        Assert.Equal("기존토큰값", store.Token);
    }

    [Fact]
    public async Task Validate는_현재_토큰과_정확히_일치할_때만_true를_반환한다()
    {
        var store = new RunnerTokenStore();
        await store.LoadOrCreateAsync(_tempDir, CancellationToken.None);

        Assert.True(store.Validate(store.Token));
        Assert.False(store.Validate("전혀다른값"));
        Assert.False(store.Validate(null));
        Assert.False(store.Validate(string.Empty));
    }

    [Fact]
    public async Task ReissueAsync_이후에는_이전_토큰이_즉시_거부되고_새_토큰만_통과한다()
    {
        var store = new RunnerTokenStore();
        await store.LoadOrCreateAsync(_tempDir, CancellationToken.None);
        var oldToken = store.Token;

        var newToken = await store.ReissueAsync(_tempDir, CancellationToken.None);

        Assert.NotEqual(oldToken, newToken);
        Assert.False(store.Validate(oldToken));   // 완료 기준: 기존 토큰 즉시 무효화
        Assert.True(store.Validate(newToken));
    }

    [Fact]
    public async Task ReissueAsync는_파일도_새_값으로_덮어쓴다()
    {
        var store = new RunnerTokenStore();
        await store.LoadOrCreateAsync(_tempDir, CancellationToken.None);

        var newToken = await store.ReissueAsync(_tempDir, CancellationToken.None);

        var savedPath = Path.Combine(_tempDir, "runner.token");
        Assert.Equal(newToken, (await File.ReadAllTextAsync(savedPath)).Trim());
    }
}
