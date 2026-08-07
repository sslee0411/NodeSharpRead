// ══════════════════════════════════════════════════════════
//  NodeSharp.UI.Themes · AppTheme.cs
//  (ED-B4로 폐기됨 — 더 이상 사용되지 않음)
// ══════════════════════════════════════════════════════════
//
// (ED-B0) 이 파일은 원래 Dark/Light 2값만 있는 자리표시자 AppTheme enum을 담고 있었다.
// (ED-B4) 사용자 요청으로 D:\lssLib\IIoT\IIoT.Solution\UI\Themes의 실제 테마 시스템을
// 포팅하면서, 이 enum은 ThemeManager.cs에 새로 정의된 7종+NoTheme의 ThemeKind enum으로
// 완전히 대체되었다 — NodeSharp.Editor\App.xaml.cs도 ThemeManager.ApplyTheme(AppTheme.Dark)
// 호출을 ThemeManager.ApplyDefault()로 갱신했다.
//
// 이 개발 환경(Linux 샌드박스)에서는 D:\ 드라이브 마운트의 파일 삭제(rm)가 허용되지 않아
// (Operation not permitted) 이 파일 자체를 지우지 못하고, 대신 타입 정의를 모두 비워둔다 —
// 빈 파일이라 컴파일에는 영향이 없다. 사용자가 Windows에서 편할 때 직접 삭제해도 무방하다.
