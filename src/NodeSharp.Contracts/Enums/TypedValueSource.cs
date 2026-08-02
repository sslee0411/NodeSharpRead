namespace NodeSharp.Contracts.Enums;

// 한글명: 타입 값 출처
/// <summary>
/// <see cref="Models.TypedValue"/>가 담는 값이 어디서 오는지를 나타냅니다. Node-RED의 TypedInput
/// (파란 라벨 드롭다운)과 동일한 개념으로, Change/Range/Switch 노드처럼 값을 고정 문자열이 아니라
/// 여러 출처 중에서 선택해 입력해야 하는 경우에 사용합니다.
/// 설계 근거: 02번 문서 9번 탭 카드 3(v1.10 신설).
/// </summary>
/// <example>
/// <code>
/// // Editor의 TypedValueEditor.xaml — 좌측 드롭다운(이 Enum 6종) + 우측 입력창이 Source에 따라 전환
/// var fixedValue    = new TypedValue(TypedValueSource.Fixed, "85.0");                 // TextBox
/// var msgField      = new TypedValue(TypedValueSource.MsgField, "payload.temp");      // TextBox(msg 하위 경로)
/// var flowContext   = new TypedValue(TypedValueSource.FlowContext, "lastAlarmLevel");  // ContextKeyPicker
/// var globalContext = new TypedValue(TypedValueSource.GlobalContext, "lineRunning");   // ContextKeyPicker
/// var envVar        = new TypedValue(TypedValueSource.EnvVar, "MAX_TEMP");             // EnvNamePicker
/// var expression    = new TypedValue(TypedValueSource.Expression, "payload * 1.8 + 32"); // CodeEditor(NCalc)
/// </code>
/// </example>
public enum TypedValueSource
{
    /// <summary>고정 리터럴 값. <see cref="Models.TypedValue.Value"/>를 그대로 사용합니다.</summary>
    Fixed,

    /// <summary>현재 msg의 하위 경로(예: <c>"payload.temp"</c>)를 가리킵니다.</summary>
    MsgField,

    /// <summary>Flow 범위 Context(<c>RT-09a</c>)의 키를 가리킵니다.</summary>
    FlowContext,

    /// <summary>Global 범위 Context(<c>RT-09a</c>)의 키를 가리킵니다.</summary>
    GlobalContext,

    /// <summary>환경변수 이름(<c>NR-10b</c>)을 가리킵니다.</summary>
    EnvVar,

    /// <summary>NCalc 수식(<c>FN-01</c> 실행기 재사용)입니다. 결과값이 실제 값으로 평가됩니다.</summary>
    Expression
}
