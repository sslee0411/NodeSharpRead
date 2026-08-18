using Microsoft.AspNetCore.Http;
using NodeSharp.Runner.Core;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// (LK-03) <see cref="TokenAuthMiddleware"/>에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 LK-03)의
/// "잘못된 토큰으로 연결 시도 시 거부되는지"를 이 미들웨어가 실제로 만족하는지 검증합니다: ① 올바른
/// <c>X-NodeSharp-Token</c> 헤더면 다음 미들웨어(<c>next</c>)로 통과시키는지 ② 틀린 값이면 401을
/// 응답하고 <c>next</c>를 호출하지 않는지 ③ 헤더 자체가 없어도 마찬가지로 401인지. <c>LK-02a</c>가
/// <c>StatusBroadcasterTests.cs</c>에서 손으로 만든 것과 동일하게, 실제 Kestrel/TestServer 없이
/// <see cref="DefaultHttpContext"/>만으로 미들웨어 로직 자체를 직접 검증합니다 — 실제 SignalR
/// 협상·WebSocket 업그레이드 요청까지 이 미들웨어를 실제로 거치는지는 LK-02a가 이미 명시해둔 "실제
/// 실행 확인 영역"과 동일하게 범위 밖입니다(사용자 로컬에서 잘못된 토큰으로 Editor를 연결해 거부되는지
/// 실물로 확인 필요).
/// </summary>
public class TokenAuthMiddlewareTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "NodeSharpTests_" + Guid.NewGuid().ToString("N"));

    public TokenAuthMiddlewareTests()
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

    private async Task<RunnerTokenStore> NewLoadedStoreAsync()
    {
        var store = new RunnerTokenStore();
        await store.LoadOrCreateAsync(_tempDir, CancellationToken.None);
        return store;
    }

    [Fact]
    public async Task 올바른_토큰이면_다음_미들웨어로_통과시킨다()
    {
        var store = await NewLoadedStoreAsync();
        var nextCalled = false;
        var middleware = new TokenAuthMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Headers["X-NodeSharp-Token"] = store.Token;

        await middleware.InvokeAsync(context, store);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task 틀린_토큰이면_401을_응답하고_다음_미들웨어를_호출하지_않는다()
    {
        var store = await NewLoadedStoreAsync();
        var nextCalled = false;
        var middleware = new TokenAuthMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Headers["X-NodeSharp-Token"] = "전혀다른값";

        await middleware.InvokeAsync(context, store);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task 헤더가_아예_없어도_401을_응답한다()
    {
        var store = await NewLoadedStoreAsync();
        var nextCalled = false;
        var middleware = new TokenAuthMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        // (헤더를 아예 설정하지 않음)

        await middleware.InvokeAsync(context, store);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }
}
