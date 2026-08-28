namespace NodeSharp.Contracts.Enums;

/// <summary>
/// Class명 : 속성 필드 타입
/// 역활 및 기능 : 노드 속성 편집 다이얼로그의 입력 필드가 어떤 UI 컨트롤로 렌더링될지 지정하는 타입 태그
///
/// 노드 속성 편집 다이얼로그에서 각 입력 필드가 어떤 UI 컨트롤로 자동 렌더링될지 지정하는
/// 태그입니다. Node-RED의 <c>&lt;node&gt;.html</c> 편집 폼 정의와 동일한 역할을 하며,
/// <c>PropertySchema</c>/<c>PropertyField</c>가 이 값에 따라 <c>NodePropertyDialog.xaml</c>에서
/// TextBox/PasswordBox/CheckBox/ComboBox/CodeEditor 등으로 자동 렌더링됩니다.
/// 설계 근거: 02번 문서 9번 탭 카드 3(여러 번 확장된 최종 통합판 — 이 파일이 기준).
/// </summary>
/// <example>
/// <code>
/// // PlcTagReadNode의 속성 편집 폼 필드 정의 예(PropertyField 레코드는 CT-07에서 구현)
/// var fields = new[]
/// {
///     new PropertyField(Label: "태그", Type: PropertyFieldType.TagRef, Required: true,
///         HelpText: "구조 설정 트리에서 읽어올 태그를 선택합니다.", Example: "1호기PLC/온도센서1"),
///
///     new PropertyField(Label: "임계값", Type: PropertyFieldType.TypedValue,
///         HelpText: "고정 숫자뿐 아니라 msg 필드·Context 값·수식으로도 지정할 수 있습니다.",
///         Example: "85.0 또는 msg.threshold"),
///
///     new PropertyField(Label: "API 키", Type: PropertyFieldType.CredentialRef,
///         HelpText: "자격증명 저장소(credentials.json)를 가리키는 참조 필드입니다."),
/// };
/// </code>
/// </example>
public enum PropertyFieldType
{
    /// <summary>일반 텍스트 입력(TextBox).</summary>
    Text,

    /// <summary>숫자 입력(Number). 범위 검사 등 숫자 전용 유효성 검사와 함께 사용됩니다.</summary>
    Number,

    /// <summary>비밀번호 입력(PasswordBox). 화면에 값이 노출되지 않으며, 저장 시 암호화 대상일 수 있습니다.</summary>
    Password,

    /// <summary>체크박스(불리언 On/Off).</summary>
    Checkbox,

    /// <summary>드롭다운 선택(ComboBox). <c>PropertyField.Options</c>에 넣은 선택지 목록에서 고릅니다.</summary>
    ComboBox,

    /// <summary>코드 편집기(CodeEditor). Function 노드의 Roslyn/NCalc 코드 등 긴 텍스트 입력에 사용됩니다.</summary>
    Code,

    /// <summary>자격증명 참조(CredentialPicker). 실제 비밀값은 <c>credentials.json</c>(DPAPI 암호화)에 저장되고, 이 필드에는 참조 키만 저장됩니다.</summary>
    CredentialRef,

    /// <summary>태그 참조. "Tag 선택" 버튼으로 구조 설정 트리를 팝업으로 열어 TagId 기반으로 선택합니다(태그 이름 변경에 안전).</summary>
    TagRef,

    /// <summary>
    /// 값의 "출처"와 "실제 값"을 함께 담는 다중 타입 입력 위젯(Node-RED TypedInput). Change/Range/Switch
    /// 노드처럼 값을 고정 문자열이 아니라 msg 필드/Context/환경변수/수식 중에서 선택해 입력할 때 사용합니다.
    /// </summary>
    TypedValue,

    /// <summary>
    /// (EC-19/EC-20) 16진 색상 코드(#RRGGBB) 입력. <c>NodePropertyDialog</c>는 TextBox 옆에 색상
    /// 팔레트 선택 버튼(<c>ColorPickerDialog</c>)을 함께 그립니다 — 팔레트에서 고르면 그 값이
    /// TextBox에 채워지고, 원하면 TextBox에 직접 16진수를 입력해도 됩니다(두 방식 모두 최종적으로
    /// 같은 문자열 값을 <c>NodeConfig.Properties</c>에 저장).
    /// </summary>
    Color,

    /// <summary>
    /// (EC-19/EC-20) 아이콘 글리프 입력. <c>NodePropertyDialog</c>는 TextBox 옆에 아이콘 선택 버튼
    /// (<c>IconPickerDialog</c>, Font Awesome 아이콘 세트에서 클릭으로 선택)을 함께 그립니다 —
    /// <see cref="Color"/>와 동일하게 팔레트 선택과 직접 타이핑(이모지 등) 둘 다 지원합니다.
    /// </summary>
    Icon
}
