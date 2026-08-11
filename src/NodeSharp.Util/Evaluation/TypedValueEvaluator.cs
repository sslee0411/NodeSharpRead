using System.Text.Json;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;

namespace NodeSharp.Util.Evaluation;

/// <summary>
/// Class명 : 타입 값 평가기
/// 역활 및 기능 : TypedValue(출처+값)를 현재 Msg/INodeContext 기준의 실제 런타임 값으로 해석하는 공용 평가기
///
/// <see cref="TypedValue"/>(CT-08)는 값의 "출처"와 "값/경로"만 담을 뿐, 그것을 실제 값으로 바꾸는
/// 로직은 없었습니다(CT-08은 의도적으로 모델만 정의하고 평가는 범위 밖으로 남김). 이 클래스가 그 평가
/// 로직을 <c>NodeSharp.Util</c>에 공용으로 구현해, Switch(NR-04)뿐 아니라 앞으로 같은 입력 방식을 쓰는
/// Change(NR-12a)·Range(NR-12b)도 재사용할 수 있게 합니다(사용자 확인, 2026-08 세션).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="TypedValueSource.Fixed"/> — <see cref="TypedValue.Value"/> 문자열을 그대로 반환.</item>
/// <item><see cref="TypedValueSource.MsgField"/> — <c>"payload"</c>/<c>"payload.temp"</c>처럼 점 표기
/// 경로로 <see cref="Msg"/> 필드(중첩 객체 포함)를 찾습니다.</item>
/// <item><see cref="TypedValueSource.FlowContext"/>/<see cref="TypedValueSource.GlobalContext"/> —
/// <see cref="INodeContext.Flow"/>/<see cref="INodeContext.Global"/>(NR-04에서 신설)에서 키로 조회.</item>
/// <item><see cref="TypedValueSource.EnvVar"/> — 아직 지원하지 않습니다. <c>NodeContext.Env</c>는
/// <c>NR-10b</c>(환경변수 접근)가 완료되기 전까지는 항상 빈 스코프라 지금 연결해도 의미가 없어
/// 사용자 확인(2026-08 세션)에 따라 <c>NR-10b</c>로 이연 — 호출하면 <see cref="NotSupportedException"/>.</item>
/// <item><see cref="TypedValueSource.Expression"/> — <see cref="SimpleExpressionEvaluator"/>(임시,
/// <c>FN-01</c> NCalc 실행기가 완료되면 교체 예정)로 계산.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var msg = new Msg { Payload = 85.0 };
///
/// // 1) Fixed — 저장된 문자열 그대로
/// object? fixedValue = TypedValueEvaluator.Resolve(new TypedValue(TypedValueSource.Fixed, "85"), msg, ctx);
///
/// // 2) MsgField — msg.payload를 직접 읽음
/// object? payload = TypedValueEvaluator.Resolve(new TypedValue(TypedValueSource.MsgField, "payload"), msg, ctx);
///
/// // 3) FlowContext — ctx.Flow에 저장된 값을 읽음(NR-04에서 INodeContext에 신설된 Flow 접근자 사용)
/// ctx.Flow.Set("threshold", 80.0);
/// object? threshold = TypedValueEvaluator.Resolve(new TypedValue(TypedValueSource.FlowContext, "threshold"), msg, ctx);
/// </code>
/// </example>
public static class TypedValueEvaluator
{
    /// <summary><paramref name="typedValue"/>를 <paramref name="msg"/>/<paramref name="ctx"/> 기준으로 해석한 실제 값을 반환합니다.</summary>
    public static object? Resolve(TypedValue typedValue, Msg msg, INodeContext ctx) => typedValue.Source switch
    {
        TypedValueSource.Fixed => typedValue.Value,
        TypedValueSource.MsgField => ResolveMsgFieldPath(typedValue.Value, msg),
        TypedValueSource.FlowContext => ctx.Flow.Get<object>(typedValue.Value),
        TypedValueSource.GlobalContext => ctx.Global.Get<object>(typedValue.Value),
        TypedValueSource.EnvVar => throw new NotSupportedException(
            "TypedValueSource.EnvVar는 아직 지원하지 않습니다 — NR-10b(환경변수 접근, ⏳ 대기)가 " +
            "완료된 뒤 지원할 예정입니다(사용자 확인, 2026-08 세션)."),
        TypedValueSource.Expression => SimpleExpressionEvaluator.Evaluate(typedValue.Value, msg),
        _ => throw new ArgumentOutOfRangeException(nameof(typedValue), typedValue.Source, "알 수 없는 TypedValueSource 값입니다."),
    };

    /// <summary>
    /// <paramref name="path"/>(점 표기, 예: <c>"payload.temp"</c>)를 <paramref name="msg"/>에서 찾아 반환합니다.
    /// 중간 경로가 없으면(필드 자체가 없거나 값이 <c>null</c>) <c>null</c>을 반환합니다(예외를 던지지 않음).
    /// <see cref="SimpleExpressionEvaluator"/>도 식별자를 만나면 이 메서드로 위임합니다(경로 해석 규칙 공유).
    /// </summary>
    internal static object? ResolveMsgFieldPath(string path, Msg msg)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var segments = path.Split('.');
        object? current = msg.Get<object>(segments[0]);
        for (var i = 1; i < segments.Length && current is not null; i++)
        {
            current = ResolveSegment(current, segments[i]);
        }

        return current;
    }

    /// <summary><paramref name="source"/>(중첩 객체) 안에서 <paramref name="key"/> 필드 하나를 찾습니다 — Dictionary/ExpandoObject/JsonElement/일반 POCO(리플렉션) 순으로 시도.</summary>
    private static object? ResolveSegment(object? source, string key)
    {
        switch (source)
        {
            case null:
                return null;
            case IDictionary<string, object?> dict:
                return dict.TryGetValue(key, out var v) ? v : null;
            case JsonElement je when je.ValueKind == JsonValueKind.Object:
                return je.TryGetProperty(key, out var prop) ? UnwrapJsonElement(prop) : null;
            default:
                var propertyInfo = source.GetType().GetProperty(key);
                return propertyInfo?.GetValue(source);
        }
    }

    /// <summary><see cref="JsonElement"/>를 원본에 가까운 CLR 값(숫자는 long/double, 문자열은 string, true/false는 bool)으로 풀어냅니다.</summary>
    private static object? UnwrapJsonElement(JsonElement je) => je.ValueKind switch
    {
        JsonValueKind.String => je.GetString(),
        JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => je,
    };
}
