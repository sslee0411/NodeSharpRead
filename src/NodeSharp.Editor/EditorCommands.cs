using System.Windows.Input;

namespace NodeSharp.Editor;

/// <summary>
/// Class명 : 에디터 전역 명령
/// 역활 및 기능 : WPF ApplicationCommands에 없는 캔버스 전용 단축키(그룹 묶기/해제)를 위한 RoutedCommand 모음
///
/// WPF의 <see cref="ApplicationCommands"/>(Save/Copy/Paste/Undo/Redo 등, <c>EC-04</c>~<c>EC-07</c>이
/// 이미 사용 중)에는 "그룹으로 묶기"/"그룹 해제"에 대응하는 표준 명령이 없어, <c>MainWindow</c>가
/// <c>Window.InputBindings</c>/<c>CommandBindings</c>에서 참조할 수 있는 <see cref="RoutedCommand"/>
/// 2개를 이 클래스에 직접 선언합니다(<see cref="ApplicationCommands"/>와 동일한 사용 패턴).
/// 설계 근거: 03번 Step맵 <c>EC-10</c> desc(그룹핑, Node-RED 1.1+ 표준 단축키 Ctrl+G/Ctrl+Shift+G 관례).
/// </summary>
/// <example>
/// <code>
/// // MainWindow.xaml — xmlns:local="clr-namespace:NodeSharp.Editor" 선언 후
/// // &lt;KeyBinding Key="G" Modifiers="Control" Command="{x:Static local:EditorCommands.GroupNodes}" /&gt;
/// // &lt;CommandBinding Command="{x:Static local:EditorCommands.GroupNodes}" Executed="OnGroupNodesClick" /&gt;
/// </code>
/// </example>
public static class EditorCommands
{
    /// <summary>선택된 노드 2개 이상을 그룹으로 묶습니다(Ctrl+G) — <c>FlowCanvasView.GroupSelectedNodes()</c>를 호출합니다.</summary>
    public static readonly RoutedCommand GroupNodes = new(nameof(GroupNodes), typeof(EditorCommands));

    /// <summary>선택된 노드가 속한 그룹을 해제합니다(Ctrl+Shift+G) — <c>FlowCanvasView.UngroupSelectedGroup()</c>를 호출합니다.</summary>
    public static readonly RoutedCommand UngroupNodes = new(nameof(UngroupNodes), typeof(EditorCommands));
}
