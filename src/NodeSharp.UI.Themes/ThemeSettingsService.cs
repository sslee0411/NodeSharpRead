// ══════════════════════════════════════════════════════════
//  NodeSharp.UI.Themes · ThemeSettingsService.cs
//  역할: 사용자가 선택한 테마를 JSON 파일에 저장하고 복원
//        앱 재시작 후에도 마지막 테마가 유지된다.
//  저장 위치: %AppData%\NodeSharpRead\ui-settings.json
//  출처: D:\lssLib\IIoT\IIoT.Solution\UI\Themes\IIoT.UI.Themes\ThemeSettingsService.cs
//        (ED-B4, "복사 참조" 포팅 — 네임스페이스와 저장 폴더명("IIoT" → "NodeSharpRead")만
//        이 프로젝트에 맞춰 바꿈, 나머지 로직은 원본과 동일)
// ══════════════════════════════════════════════════════════
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NodeSharp.UI.Themes;

// §1 ─ 설정 모델 ─────────────────────────────────────────

/// <summary>UI 설정 저장 모델 (JSON 직렬화)</summary>
internal sealed class UiSettings
{
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = nameof(ThemeKind.DarkNavy);

    [JsonPropertyName("savedAt")]
    public DateTime SavedAt { get; set; } = DateTime.Now;
}

// §2 ─ 서비스 ────────────────────────────────────────────

/// <summary>
/// 테마 설정 영속성 서비스.
/// ThemeManager.ThemeChanged 이벤트를 구독하여 변경 시 자동 저장.
/// </summary>
public sealed class ThemeSettingsService : IDisposable
{
    // §2-1 ─ 경로 설정 ────────────────────────────────────
    private static readonly string SettingsDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NodeSharpRead");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "ui-settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // §2-2 ─ 생성자 ───────────────────────────────────────
    public ThemeSettingsService()
    {
        // 테마 변경 이벤트 구독 → 자동 저장
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    // §2-3 ─ 공개 메서드 ──────────────────────────────────

    /// <summary>
    /// 저장된 테마를 불러와서 적용한다.
    /// 저장 파일이 없으면 DarkNavy(기본값)를 적용한다.
    /// </summary>
    /// <param name="app">적용 대상 Application (null이면 Current 사용)</param>
    /// <returns>실제 적용된 테마</returns>
    public ThemeKind LoadAndApply(System.Windows.Application? app = null)
    {
        var theme = Load();
        ThemeManager.Apply(theme, app);
        return theme;
    }

    /// <summary>저장된 테마 종류를 반환한다 (적용은 하지 않음).</summary>
    public ThemeKind Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return ThemeKind.DarkNavy;  // 기본값

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<UiSettings>(json, JsonOpts);

            if (settings != null &&
                Enum.TryParse<ThemeKind>(settings.Theme, ignoreCase: true, out var kind))
                return kind;
        }
        catch
        {
            // 파일 손상 등 — 기본값 반환
        }

        return ThemeKind.DarkNavy;
    }

    /// <summary>현재 테마를 저장한다.</summary>
    public void Save(ThemeKind theme)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);

            var settings = new UiSettings
            {
                Theme = theme.ToString(),
                SavedAt = DateTime.Now
            };

            var json = JsonSerializer.Serialize(settings, JsonOpts);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // 저장 실패 — 무시 (비필수 기능)
        }
    }

    // §2-4 ─ 이벤트 핸들러 ────────────────────────────────
    private void OnThemeChanged(ThemeKind theme) => Save(theme);

    // §2-5 ─ 해제 ─────────────────────────────────────────
    public void Dispose()
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
    }
}
