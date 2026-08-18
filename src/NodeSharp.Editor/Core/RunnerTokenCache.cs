using System.IO;

namespace NodeSharp.Editor.Core;

/// <summary>
/// Class명 : 러너 토큰 캐시
/// 역활 및 기능 : Editor가 Runner의 SignalR Hub에 접속할 때 쓸 runner.token 값을 "같은 PC면 Runner
/// 폴더의 runner.token 파일을 직접 읽기, 원격 PC면 사용자가 입력한 값을 로컬에 기억해두기" 두 경로로
/// 알아내는 정적 도우미
///
/// (LK-03) 02번 설계 문서 7번 탭 카드6 "Editor는 이 파일을 읽어(같은 PC) 또는 사용자가 직접 입력해
/// (원격 PC) 접속한다"를 그대로 구현합니다. "같은 PC"인지는 <see cref="RunnerProcessManager.RunnerExecutablePath"/>가
/// 가리키는 폴더에 실제로 <c>runner.token</c> 파일이 있는지로 판단합니다 — Editor가 이미 "Runner
/// 실행(배포)" 메뉴(LK-02b 후속)를 위해 그 경로를 알고 있으므로 별도 설정 없이 재사용할 수 있습니다.
/// 그 파일이 없으면(원격 PC, 또는 Runner 실행 파일 경로를 아직 모름) Editor 자신의 데이터 폴더
/// (<c>FlowCanvasView.DataDirectory</c>, <see cref="RunnerProcessManager"/>가 <c>runner.path.txt</c>를
/// 저장하는 곳과 동일)에 <c>runner.token.cache</c>로 저장해둔 마지막 입력값을 대신 씁니다(최초 1회는
/// 사용자가 직접 입력해야 함 — <c>MainWindow.OnEnterTokenClick</c>/<c>Views.TokenInputDialog</c> 참고).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>같은 PC 우선</b>: 두 경로 중 <c>runner.token</c>(Runner 폴더)이 있으면 항상 그 값을
/// 우선합니다 — Runner 쪽에서 재발급이 일어나도(예: 다른 Editor 인스턴스가 재발급) 이 값을 다시
/// 읽기만 하면 항상 최신이기 때문입니다. 반대로 <c>runner.token.cache</c>(원격 PC 캐시)는 이
/// Editor가 마지막으로 알고 있던 값일 뿐이라 Runner 쪽에서 재발급되면(다른 사용자가 재발급) 다음
/// 연결 시도부터 401로 거부되고, 사용자가 새 토큰을 다시 입력해야 합니다(원격 PC의 근본적 한계 —
/// 파일 시스템을 공유하지 않으므로 자동으로 알 방법이 없음, 02번 문서 설계 그대로).</item>
/// <item><b>민감 파일</b>: <c>runner.token.cache</c>는 <c>runner.token</c>과 마찬가지로 인증
/// 비밀값을 담으므로 <c>.gitignore</c>에 추가했습니다(credentials.json/runner.token과 동일한
/// 분류 — 02번 문서 8번 탭 카드3 "커밋해도 안전한 파일과 안 되는 파일을 물리적으로 분리" 원칙).</item>
/// </list>
/// </remarks>
public static class RunnerTokenCache
{
    private const string CacheFileName = "runner.token.cache";
    private const string RunnerSideFileName = "runner.token";

    /// <summary>
    /// <paramref name="runnerExecutablePath"/>가 가리키는 폴더에 <c>runner.token</c>이 있으면 그
    /// 값을(같은 PC), 없으면 <paramref name="dataDirectory"/>\runner.token.cache에 저장된 마지막
    /// 값을(원격 PC 또는 아직 실행 파일 경로를 모름) 읽습니다. 둘 다 없으면 <c>null</c>을 반환해
    /// 호출자(<c>MainWindow</c>)가 사용자에게 직접 입력을 요청하게 합니다.
    /// </summary>
    public static async Task<string?> ResolveAsync(string? runnerExecutablePath, string dataDirectory, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(runnerExecutablePath))
        {
            var runnerDirectory = Path.GetDirectoryName(runnerExecutablePath);
            if (!string.IsNullOrEmpty(runnerDirectory))
            {
                var sameMachinePath = Path.Combine(runnerDirectory, RunnerSideFileName);
                if (File.Exists(sameMachinePath))
                {
                    var value = (await File.ReadAllTextAsync(sameMachinePath, ct)).Trim();
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }
            }
        }

        var cachePath = Path.Combine(dataDirectory, CacheFileName);
        if (File.Exists(cachePath))
        {
            var cached = (await File.ReadAllTextAsync(cachePath, ct)).Trim();
            return string.IsNullOrEmpty(cached) ? null : cached;
        }

        return null;
    }

    /// <summary>
    /// <paramref name="token"/>을 <paramref name="dataDirectory"/>\runner.token.cache에 저장합니다
    /// (사용자가 직접 입력했거나, 재발급으로 새로 받은 값 — <c>RunnerProcessManager.SavePathAsync</c>와
    /// 동일하게 평문 1줄, 원자적 저장 없음).
    /// </summary>
    public static async Task SaveAsync(string dataDirectory, string token, CancellationToken ct = default)
    {
        var cachePath = Path.Combine(dataDirectory, CacheFileName);
        await File.WriteAllTextAsync(cachePath, token, ct);
    }
}
