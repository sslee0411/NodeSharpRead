using NodeSharp.Runtime;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="InMemoryContextStore"/>/<see cref="ContextScope"/>(RT-09a, 02번 설계 문서 6번 탭 카드1
/// <c>IContextStore</c>)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 RT-09a): 전역 Context와 Flow
/// 단위 Context에 같은 키로 다른 값을 저장해도 서로 섞이지 않는지 확인. Node 단계(<see cref="ContextScope"/>의
/// "node" scope)는 <c>RT-09b</c> 범위라 이 테스트에서는 다루지 않습니다.
/// </summary>
public class InMemoryContextStoreTests
{
    [Fact]
    public void 같은_key라도_scope가_다르면_값이_섞이지_않는다()
    {
        var store = new InMemoryContextStore();

        store.Set("flow", "f1", "counter", 1);
        store.Set("global", "", "counter", 100);

        Assert.Equal(1, store.Get<int>("flow", "f1", "counter"));
        Assert.Equal(100, store.Get<int>("global", "", "counter"));
    }

    [Fact]
    public void 같은_scope라도_scopeId가_다르면_값이_섞이지_않는다()
    {
        var store = new InMemoryContextStore();

        store.Set("flow", "f1", "counter", 1);
        store.Set("flow", "f2", "counter", 200);

        Assert.Equal(1, store.Get<int>("flow", "f1", "counter"));
        Assert.Equal(200, store.Get<int>("flow", "f2", "counter"));
    }

    [Fact]
    public void 존재하지_않는_키를_읽으면_기본값을_반환하고_예외가_나지_않는다()
    {
        var store = new InMemoryContextStore();

        var ex = Record.Exception(() => store.Get<int>("global", "", "no-such-key"));

        Assert.Null(ex);
        Assert.Equal(0, store.Get<int>("global", "", "no-such-key"));
        Assert.Null(store.Get<string>("global", "", "no-such-key"));
    }

    [Fact]
    public void 저장된_값의_타입이_다르면_기본값을_반환한다()
    {
        var store = new InMemoryContextStore();

        store.Set("global", "", "value", "문자열입니다");

        Assert.Equal(0, store.Get<int>("global", "", "value"));   // string으로 저장했는데 int로 읽음
    }

    [Fact]
    public void Set을_다시_호출하면_기존_값을_덮어쓴다()
    {
        var store = new InMemoryContextStore();

        store.Set("global", "", "counter", 1);
        store.Set("global", "", "counter", 2);

        Assert.Equal(2, store.Get<int>("global", "", "counter"));
    }

    [Fact]
    public void Keys는_해당_scope_scopeId_안의_키만_열거한다()
    {
        var store = new InMemoryContextStore();
        store.Set("flow", "f1", "a", 1);
        store.Set("flow", "f1", "b", 2);
        store.Set("flow", "f2", "c", 3);
        store.Set("global", "", "d", 4);

        var keys = store.Keys("flow", "f1").ToList();

        Assert.Equal(2, keys.Count);
        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
        Assert.DoesNotContain("c", keys);
        Assert.DoesNotContain("d", keys);
    }

    [Fact]
    public void ContextScope로_접근해도_scope끼리_섞이지_않는다()
    {
        var store = new InMemoryContextStore();
        var flowScope = new ContextScope(store, "flow", "f1");
        var globalScope = new ContextScope(store, "global", "");

        flowScope.Set("counter", 1);
        globalScope.Set("counter", 100);

        Assert.Equal(1, flowScope.Get<int>("counter"));
        Assert.Equal(100, globalScope.Get<int>("counter"));

        // ContextScope를 거쳐 저장한 값도 원본 IContextStore로 직접 조회하면 동일하게 보인다
        // (ContextScope는 scope/scopeId를 미리 채워 넣는 얇은 창일 뿐 별도 저장소가 아님).
        Assert.Equal(1, store.Get<int>("flow", "f1", "counter"));
    }

    [Fact]
    public void ContextScope_Keys는_해당_스코프의_키만_반환한다()
    {
        var store = new InMemoryContextStore();
        var flowScope = new ContextScope(store, "flow", "f1");
        flowScope.Set("a", 1);
        flowScope.Set("b", 2);
        store.Set("flow", "f2", "c", 3);

        var keys = flowScope.Keys().ToList();

        Assert.Equal(2, keys.Count);
        Assert.DoesNotContain("c", keys);
    }
}
