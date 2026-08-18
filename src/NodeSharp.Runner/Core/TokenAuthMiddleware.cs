using Microsoft.AspNetCore.Http;

namespace NodeSharp.Runner.Core;

/// <summary>
/// Class명 : 토큰 인증 미들웨어
/// 역활 및 기능 : "/hubs/monitor" 요청의 X-NodeSharp-Token 헤더를 RunnerTokenStore와 대조해 일치하지
/// 않으면 401로 즉시 거부하는 ASP.NET Core 미들웨어
///
/// (LK-03) 02번 설계 문서 7번 탭 카드6 <c>TokenAuthMiddleware</c>가 그대로 — SignalR의 협상(negotiate)
/// HTTP 요청과 실제 연결(WebSocket 업그레이드) 요청 모두 이 파이프라인을 거치므로, 여기서 한 번만
/// 검사하면 두 단계 모두 보호됩니다(<c>HubConnectionBuilder.WithUrl(url, opts =&gt;
/// opts.Headers.Add(...))</c>로 붙인 헤더는 SignalR 클라이언트가 보내는 모든 HTTP 요청에 그대로
/// 실립니다 — WebSocket 업그레이드 요청도 예외가 아닙니다). <c>Program.cs</c>가
/// <c>app.UseWhen(path.StartsWithSegments("/hubs/monitor"), ...)</c>로 이 경로에만 적용합니다 —
/// <c>/health</c>는 LK-03 범위 밖(완료 기준이 SignalR 연결만 요구)이라 계속 인증 없이 열려 있습니다.
/// </summary>
public sealed class TokenAuthMiddleware
{
    private const string TokenHeaderName = "X-NodeSharp-Token";

    private readonly RequestDelegate _next;

    /// <summary>ASP.NET Core 미들웨어 표준 생성자 — 다음 미들웨어(<paramref name="next"/>)를 프레임워크가 자동으로 채웁니다.</summary>
    public TokenAuthMiddleware(RequestDelegate next) => _next = next;

    /// <summary>
    /// <paramref name="context"/>의 <c>X-NodeSharp-Token</c> 헤더를 <paramref name="tokenStore"/>로
    /// 검증합니다. 일치하지 않으면(헤더 자체가 없는 경우 포함) 401을 응답하고 파이프라인을 여기서
    /// 끊습니다(<see cref="_next"/>를 호출하지 않음) — SignalR Hub까지 요청이 도달하지 않으므로
    /// <see cref="MonitorHub"/>는 인증 여부를 전혀 신경 쓰지 않아도 됩니다. <paramref name="tokenStore"/>는
    /// ASP.NET Core 미들웨어 관례대로 <c>InvokeAsync</c> 메서드 매개변수로 DI가 주입합니다(생성자가
    /// 아니라 이 메서드에 주입해야 요청마다 최신 값을 정확히 받습니다 — Microsoft 공식 미들웨어 작성
    /// 관례).
    /// </summary>
    public async Task InvokeAsync(HttpContext context, RunnerTokenStore tokenStore)
    {
        var provided = context.Request.Headers[TokenHeaderName].ToString();
        if (!tokenStore.Validate(provided))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await _next(context);
    }
}
