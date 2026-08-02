using NodeSharp.Contracts.Interfaces;
using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="FileContextStore"/>(RT-09c, <see cref="IContextStore"/>의 파일 기반 구현체 — 02번 문서
/// 6번 탭 카드1 다이어그램의 "파일 — lssLib JsonWriteService" 플러그인 슬롯)에 대한 단위 테스트입니다.
/// 완료 기준(03번 Step맵 RT-09c): 메모리 구현(<see cref="InMemoryContextStore"/>)을 이 파일 구현으로 DI
/// 교체해도 <see cref="IContextStore"/>를 사용하는 코드(<see cref="ContextScope"/>/<c>NodeContext</c>)가
/// 전혀 바뀌지 않고 그대로 동작하는지 확인. 각 테스트는 임시 디렉터리에 파일을 만들고 끝나면 지운다
/// (테스트끼리 파일이 섞이지 않도록 매번 새 임시 경로 사용).
/// </summary>
public class FileContextStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "NodeSharpTests_" + Guid.NewGuid().ToString("N"));

    private string NewFilePath() => Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void 존재하지_않는_파일로_생성해도_예외_없이_빈_상태로_시작한다()
    {
        var path = NewFilePath();

        var ex = Record.Exception(() => new FileContextStore(path));

        Assert.Null(ex);
        Assert.False(File.Exists(path));   // Set을 한 번도 안 했으면 파일 자체가 아직 없어야 함
    }

    [Fact]
    public void Set_직후_같은_인스턴스에서_Get하면_원본_타입_그대로_반환한다()
    {
        var store = new FileContextStore(NewFilePath());

        store.Set("global", "", "count", 42);

        Assert.Equal(42, store.Get<int>("global", "", "count"));
    }

    [Fact]
    public void Set을_호출하면_즉시_파일에_저장된다()
    {
        var path = NewFilePath();
        var store = new FileContextStore(path);

        store.Set("flow", "f1", "name", "값");

        Assert.True(File.Exists(path));
        Assert.Contains("name", File.ReadAllText(path));
    }

    [Fact]
    public void 새_인스턴스로_같은_파일을_다시_읽으면_저장된_값이_복원된다()
    {
        var path = NewFilePath();
        var first = new FileContextStore(path);
        first.Set("global", "", "count", 42);
        first.Set("global", "", "name", "테스트");

        var reloaded = new FileContextStore(path);

        Assert.Equal(42, reloaded.Get<int>("global", "", "count"));
        Assert.Equal("테스트", reloaded.Get<string>("global", "", "name"));
    }

    [Fact]
    public void 같은_key라도_scope_scopeId가_다르면_섞이지_않는다()
    {
        var store = new FileContextStore(NewFilePath());

        store.Set("flow", "f1", "counter", 1);
        store.Set("flow", "f2", "counter", 2);
        store.Set("global", "", "counter", 100);

        Assert.Equal(1, store.Get<int>("flow", "f1", "counter"));
        Assert.Equal(2, store.Get<int>("flow", "f2", "counter"));
        Assert.Equal(100, store.Get<int>("global", "", "counter"));
    }

    [Fact]
    public void Keys는_재로드_후에도_해당_scope_scopeId_안의_키만_열거한다()
    {
        var path = NewFilePath();
        var first = new FileContextStore(path);
        first.Set("flow", "f1", "a", 1);
        first.Set("flow", "f1", "b", 2);
        first.Set("flow", "f2", "c", 3);

        var reloaded = new FileContextStore(path);
        var keys = reloaded.Keys("flow", "f1").ToList();

        Assert.Equal(2, keys.Count);
        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
        Assert.DoesNotContain("c", keys);
    }

    [Fact]
    public void 존재하지_않는_키를_읽으면_예외_없이_기본값을_반환한다()
    {
        var store = new FileContextStore(NewFilePath());

        var ex = Record.Exception(() => store.Get<int>("global", "", "no-such-key"));

        Assert.Null(ex);
        Assert.Equal(0, store.Get<int>("global", "", "no-such-key"));
        Assert.Null(store.Get<string>("global", "", "no-such-key"));
    }

    [Fact]
    public void IContextStore를_ContextScope로_감싸도_FileContextStore로_DI_교체만으로_동일하게_동작한다()
    {
        // 완료 기준 직접 검증: InMemoryContextStore 자리에 FileContextStore를 넣어도 ContextScope
        // 코드는 한 글자도 바뀌지 않는다 — 아래는 RT-09a InMemoryContextStoreTests의 동일 시나리오를
        // IContextStore 하나만 교체해 그대로 재현한 것.
        IContextStore store = new FileContextStore(NewFilePath());
        var flowScope = new ContextScope(store, "flow", "f1");
        var globalScope = new ContextScope(store, "global", "");

        flowScope.Set("counter", 1);
        globalScope.Set("counter", 100);

        Assert.Equal(1, flowScope.Get<int>("counter"));
        Assert.Equal(100, globalScope.Get<int>("counter"));
    }

    [Fact]
    public void Set을_다시_호출하면_파일에서도_기존_값을_덮어쓴다()
    {
        var path = NewFilePath();
        var store = new FileContextStore(path);
        store.Set("global", "", "counter", 1);
        store.Set("global", "", "counter", 2);

        var reloaded = new FileContextStore(path);

        Assert.Equal(2, reloaded.Get<int>("global", "", "counter"));
    }
}
