namespace NodeSharp.Contracts.Enums;

/// <summary>
/// 노드 속성 편집 다이얼로그에서 각 입력 필드가 어떤 UI 컨트롤로 자동 렌더링될지 지정하는
/// 태그입니다. Node-RED의 <c>&lt;node&gt;.html</c> 편집 폼 정의, iiot-system-arch
/// <c>PlcEditorView._RenderParameterForm()</c>의 "ParameterType별 동적 폼" 패턴과 동일한 역할을 합니다.
/// </summary>
/// <remarks>
/// <para>
/// 설계 근거: 02번 설계 문서 9번 탭(Node-RED 기능 보강) 카드 3 — <c>PropertySchema</c>/
/// <c>PropertyField</c>(Label, Type, Required, DefaultValue, Options, HelpText, Example)가
/// 이 Enum 값에 따라 <c>NodePropertyDialog.xaml</c>에서 TextBox/PasswordBox/CheckBox/ComboBox/
/// CodeEditor/CredentialPicker 등으로 자동 렌더링됩니다(CT-07에서 PropertySchema와 함께 구현).
/// </para>
/// <para>
/// 이 Enum은 문서 안에서 두 차례 점진적으로 확장됐습니다(문서의 "표기 안내" 방식과 동일) —
/// 최초 정의는 Text/Number/Password/Checkbox/ComboBox/Code/CredentialRef 7종이었고,
/// 8번 탭(계층형 구조 설정) 작업 중 캔버스 노드가 구조 설정 트리의 태그를 고를 수 있어야 해서
/// <see cref="TagRef"/>가 추가됐고, Change/Range/Switch 등의 노드가 "값을 고정 문자열이 아니라
/// msg 필드/Context/환경변수/수식 중 선택"해서 입력해야 하는 요구가 있는데 그 값을 받을 위젯이
/// 없어 <see cref="TypedValue"/>가 추가됐습니다(v1.31). <b>이 파일이 그 모든 확장을 합친 최종
/// 통합판</b>이며, 앞으로 이 Enum이 필요한 모든 Step은 이 파일을 그대로 참조합니다.
/// </para>
/// </remarks>
/// <example>
/// PropertySchema를 정의할 때 필드별로 타입을 지정하는 예(실제 <c>PropertyField</c> 레코드는 CT-07에서 구현):
/// <code>
/// // 예: PlcTagReadNode의 속성 편집 폼 필드 정의
/// // new PropertyField(Label: "태그", Type: PropertyFieldType.TagRef, Required: true,
/// //                    HelpText: "구조 설정 트리에서 읽어올 태그를 선택합니다.",
/// //                    Example: "예: 1호기PLC/온도센서1");
/// //
/// // new PropertyField(Label: "임계값", Type: PropertyFieldType.TypedValue,
/// //                    HelpText: "고정 숫자뿐 아니라 msg 필드·Context 값·수식으로도 지정할 수 있습니다.",
/// //                    Example: "예: 85.0 또는 msg.threshold");
///
/// var fieldType = PropertyFieldType.CredentialRef;   // 자격증명 저장소(credentials.json)를 가리키는 참조 필드
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

    /// <summary>드롭다운 선택(ComboBox). <c>PropertyField.Options</c>에 선택지 목록을 넣어 사용합니다.</summary>
    ComboBox,

    /// <summary>코드 편집기(CodeEditor). Function 노드의 Roslyn C# 코드, NCalc 수식 등 긴 텍스트 입력에 사용됩니다.</summary>
    Code,

    /// <summary>
    /// 자격증명 참조(CredentialPicker). 실제 비밀값은 <c>credentials.json</c>(DPAPI 암호화)에
    /// 별도로 저장되고, 이 필드에는 그 항목을 가리키는 참조 키만 저장됩니다.
    /// </summary>
    CredentialRef,

    /// <summary>
    /// 태그 참조(구조 설정 트리 팝업 선택). "Tag 선택" 버튼을 누르면 8번 탭 구조 설정 트리가
    /// 팝업으로 열리고, 태그를 클릭 한 번으로 선택합니다(TagId 기반이라 태그 이름 변경에 안전).
    /// </summary>
    TagRef,

    /// <summary>
    /// 값의 "출처"와 "실제 값/경로"를 함께 담는 다중 타입 입력 위젯(Node-RED의 TypedInput —
    /// 파란 라벨 드롭다운 + 값 입력창 조합). Change/Range/Switch 노드처럼 값을 고정 문자열이 아니라
    /// msg 필드/Flow Context/Global Context/환경변수/수식 중에서 선택해 입력해야 할 때 사용합니다.
    /// </summary>
    TypedValue
}
