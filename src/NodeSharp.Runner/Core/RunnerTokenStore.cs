using System.IO;

namespace NodeSharp.Runner.Core;

/// <summary>
/// Class명 : 러너 토큰 저장소
/// 역활 및 기능 : Editor↔Runner SignalR 인증에 쓰는 runner.token 값을 최초 기동 시 생성·로컬 파일로
/// 영속화하고, 재발급(Reissue) 요청 시 새 값으로 교체해 이전 토큰을 즉시 무효화하는 DI 싱글턴
///
/// (LK-03) 02번 설계 문서 7번 탭 카드6 <c>RunnerAuthOptions</c>가 그대로 — "같은 파일에 접근 가능 =
/// 신뢰할 수 있는 사용자"라는 단순한 전제로, OAuth 같은 별도 인증 서버 없이 로컬 파일(<c>runner.token</c>,
/// 실행 파일과 같은 폴더, <c>.gitignore</c>에 v1.20부터 이미 등록됨) 기반 토큰 하나로 최소한의 보호를
/// 제공합니다. 이 클래스가 "지금 유효한 토큰이 무엇인지"를 아는 유일한 진실 공급원(source of truth)이며,
/// <see cref="TokenAuthMiddleware"/>가 매 요청마다 <see cref="Validate"/>로 대조합니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>즉시 무효화</b>: <see cref="ReissueAsync"/>가 <see cref="Token"/>을 새 값으로 바꾸는
/// 순간부터, 그 이전 값을 들고 오는 모든 새 연결 시도(재연결 포함)는 <see cref="Validate"/>에서
/// 곧바로 실패합니다 — 별도의 만료 시각·블랙리스트 없이 "현재 값과 다르면 거부"라는 가장 단순한
/// 규칙만으로 완료 기준("재발급 시 기존 토큰 즉시 무효화")을 만족합니다. 재발급 시점에 이미 연결돼
/// 있던 클라이언트(살아있는 SignalR WebSocket)까지 강제로 끊는 것은 <see cref="MonitorHub.ReissueToken"/>
/// 쪽 책임입니다(그 메서드가 재발급 직후 <c>Clients.Others</c>에 알림을 보내 클라이언트가 스스로
/// 연결을 끊고 재인증하도록 유도 — 이 클래스 자신은 SignalR을 몰라도 되도록 분리).</item>
/// <item><b>파일 형식</b>: <c>RunnerProcessManager.runner.path.txt</c>(Editor)와 동일한 관례 —
/// 평문 1줄, 원자적 저장(.tmp→File.Replace)까지는 하지 않습니다. 손상되거나 지워져도 다음 기동/재발급
/// 시 새로 생성되므로 flows.json만큼 엄격하게 다룰 필요가 없다고 판단했습니다.</item>
/// <item><b>동시성</b>: <see cref="Token"/> 읽기/쓰기를 <c>lock</c>으로 감싸, 여러 SignalR 연결이
/// 동시에 <see cref="Validate"/>를 호출하는 중에 <see cref="ReissueAsync"/>가 실행돼도 값이 반쪽만
/// 바뀐 상태로 읽히지 않게 합니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Program.cs
/// var tokenStore = app.Services.GetRequiredService&lt;RunnerTokenStore&gt;();
/// await tokenStore.LoadOrCreateAsync(AppContext.BaseDirectory, CancellationToken.None);
/// // 이후 TokenAuthMiddleware가 tokenStore.Validate(providedToken)으로 매 요청을 검사
/// </code>
/// </example>
public sealed class RunnerTokenStore
{
    private const string TokenFileName = "runner.token";

    private readonly object _lock = new();
    private string? _token;

    /// <summary>현재 유효한 토큰 값입니다 — <see cref="LoadOrCreateAsync"/> 호출 전에는 <c>null</c>입니다.</summary>
    public string? Token
    {
        get { lock (_lock) { return _token; } }
        private set { lock (_lock) { _token = value; } }
    }

    /// <summary>
    /// <paramref name="baseDirectory"/>\runner.token이 있으면 읽어 <see cref="Token"/>을 채웁니다.
    /// 없으면(최초 기동) 새 토큰(GUID, 하이픈 없는 32자 16진수)을 생성해 파일에 저장한 뒤 그 값을
    /// 채웁니다 — Runner를 매번 재기동해도(재발급 전까지는) 같은 토큰을 계속 쓸 수 있어, Editor가
    /// runner.token 파일을 다시 읽어도 값이 안정적입니다.
    /// </summary>
    public async Task LoadOrCreateAsync(string baseDirectory, CancellationToken ct)
    {
        var path = Path.Combine(baseDirectory, TokenFileName);
        if (File.Exists(path))
        {
            var existing = (await File.ReadAllTextAsync(path, ct)).Trim();
            if (!string.IsNullOrEmpty(existing))
            {
                Token = existing;
                return;
            }
        }

        var generated = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(path, generated, ct);
        Token = generated;
    }

    /// <summary>
    /// 새 토큰을 생성해 <paramref name="baseDirectory"/>\runner.token을 덮어쓰고 <see cref="Token"/>을
    /// 그 값으로 교체합니다(교체되는 순간 이전 토큰은 <see cref="Validate"/>에서 즉시 거부됨 — 위 클래스
    /// remarks "즉시 무효화" 항목 참고). Editor의 "토큰 재발급" 메뉴(<see cref="MonitorHub.ReissueToken"/>)가
    /// 호출합니다.
    /// </summary>
    /// <returns>새로 생성된 토큰 값 — 호출자가 자기 자신의 인증 헤더를 즉시 갱신할 수 있도록 반환합니다.</returns>
    public async Task<string> ReissueAsync(string baseDirectory, CancellationToken ct)
    {
        var generated = Guid.NewGuid().ToString("N");
        var path = Path.Combine(baseDirectory, TokenFileName);
        await File.WriteAllTextAsync(path, generated, ct);
        Token = generated;
        return generated;
    }

    /// <summary>
    /// <paramref name="providedToken"/>이 지금 <see cref="Token"/>과 정확히 일치하는지 확인합니다.
    /// <see cref="Token"/>이 아직 없으면(<see cref="LoadOrCreateAsync"/>를 호출하지 않은 비정상 상태)
    /// 무엇을 보내도 거부합니다(안전 기본값 — "아직 모르면 막는다").
    /// </summary>
    public bool Validate(string? providedToken) =>
        !string.IsNullOrEmpty(Token) && string.Equals(providedToken, Token, StringComparison.Ordinal);
}
