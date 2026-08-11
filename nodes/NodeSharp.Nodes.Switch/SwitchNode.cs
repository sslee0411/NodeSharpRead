using System.Text.RegularExpressions;
using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Util.Evaluation;

namespace NodeSharp.Nodes.Switch;

/// <summary>
/// Class명 : Switch 노드
/// 역활 및 기능 : msg의 특정 값을 여러 조건과 비교해 맞는 출력 포트로 라우팅하는 다중 분기 노드
///
/// 03번 개발 Step맵 NR-04의 첫 구현체입니다. <see cref="Property"/>(기본값 <c>msg.payload</c>)의
/// 실제 값을 <see cref="Rules"/> 목록 순서대로 검사해, 조건에 맞는 규칙과 같은 인덱스의 출력 포트로
/// <see cref="Msg"/>를 라우팅합니다(Node-RED <c>10-switch.js</c>와 동일한 "규칙 순서 = 포트 순서"
/// 구조). <see cref="CheckAll"/>이 <c>true</c>(기본값, Node-RED 기본값과 동일 — 조사 결과 재확인)면
/// 맞는 규칙 전부의 포트로 라우팅하고, <c>false</c>면 처음 맞는 규칙 하나에서 멈춥니다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>연산자 범위</b>: 21종 중 17종(<c>eq</c>/<c>neq</c>/<c>lt</c>/<c>lte</c>/<c>gt</c>/<c>gte</c>/
/// <c>btwn</c>/<c>cont</c>/<c>regex</c>/<c>true</c>/<c>false</c>/<c>null</c>/<c>nnull</c>/<c>empty</c>/
/// <c>nempty</c>/<c>istype</c>/<c>else</c>)만 이 Step에서 구현합니다 — <c>head</c>/<c>tail</c>/
/// <c>index</c>(msg.parts 시퀀스 처리)는 그 인프라를 전담하는 <c>NR-13a</c>(Split)/<c>NR-13b</c>(Join)가
/// 아직 <c>⏳ 대기</c>라 함께 이연하고, <c>jsonata_exp</c>는 JSONata 엔진이 이 저장소에 전혀 없어
/// 별도 Step 신설이 필요해 제외했습니다(둘 다 사용자 확인, 2026-08 세션 — <see cref="SwitchRule"/>
/// XML 문서에 동일한 근거 기록).</item>
/// <item><b><c>else</c> 규칙</b>: Node-RED와 동일하게 "다른 규칙 중 하나라도 맞았으면 매치 안 함,
/// 아무것도 안 맞았으면 매치"로 동작합니다 — 다른 규칙들을 먼저 전부(또는 <see cref="CheckAll"/>이
/// <c>false</c>면 첫 매치까지) 평가한 뒤 2차로 판정합니다.</item>
/// <item><b>값 해석</b>: <see cref="Property"/>/<see cref="SwitchRule.CompareValue"/>/
/// <see cref="SwitchRule.CompareValue2"/> 전부 <see cref="TypedValue"/>라 <c>TypedValueEvaluator</c>
/// (<c>NodeSharp.Util.Evaluation</c>, NR-04에서 신설)로 해석합니다 — msg 필드뿐 아니라 Flow/Global
/// Context 값과도 비교 가능(완료 기준의 "FlowContext Source" 요구사항).</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var node = new SwitchNode
/// {
///     Id = "n1",
///     Property = new TypedValue(TypedValueSource.MsgField, "payload"),
///     CheckAll = true,
///     Rules = new[]
///     {
///         new SwitchRule("gte", CompareValue: new TypedValue(TypedValueSource.Fixed, "85")),
///         new SwitchRule("lt", CompareValue: new TypedValue(TypedValueSource.Fixed, "85")),
///     },
///     OutputPorts = new[] { new NodePort(0, "gte 85"), new NodePort(1, "lt 85") },
/// };
///
/// // msg.payload == 90 → 0번 포트로만 라우팅(1번 규칙은 안 맞음)
/// await node.OnInputAsync(new Msg { Payload = 90 }, ctx, CancellationToken.None);
/// </code>
/// </example>
public sealed class SwitchNode : IFlowNode
{
    /// <inheritdoc/>
    public string Id { get; init; } = default!;

    /// <inheritdoc/>
    public string Type => "switch";

    /// <inheritdoc/>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc/>
    public IReadOnlyList<NodePort> InputPorts { get; } = new[] { new NodePort(0, "in") };

    /// <inheritdoc/>
    public IReadOnlyList<NodePort> OutputPorts { get; init; } = new[] { new NodePort(0, "out") };

    /// <summary>비교 대상이 되는 값의 출처/경로입니다(기본값: <c>msg.payload</c>). Node-RED의 <c>node.property</c>/<c>propertyType</c>에 대응.</summary>
    public TypedValue Property { get; init; } = new(TypedValueSource.MsgField, "payload");

    /// <summary>순서대로 검사할 규칙 목록 — 목록 안 인덱스가 곧 <see cref="OutputPorts"/> 인덱스입니다.</summary>
    public IReadOnlyList<SwitchRule> Rules { get; init; } = Array.Empty<SwitchRule>();

    /// <summary><c>true</c>(기본값)면 맞는 규칙 전부의 포트로 라우팅, <c>false</c>면 처음 맞는 규칙 하나에서 멈춥니다(Node-RED <c>checkall</c> 기본값과 동일).</summary>
    public bool CheckAll { get; init; } = true;

    /// <inheritdoc/>
    public Task OnStartAsync(INodeContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task OnCloseAsync(INodeContext ctx) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task OnInputAsync(Msg msg, INodeContext ctx, CancellationToken ct)
    {
        if (Rules.Count == 0)
        {
            return;
        }

        var propertyValue = TypedValueEvaluator.Resolve(Property, msg, ctx);
        var matched = new bool[Rules.Count];

        for (var i = 0; i < Rules.Count; i++)
        {
            if (Rules[i].Operator == "else")
            {
                continue; // else는 다른 규칙을 전부(또는 첫 매치까지) 평가한 뒤 2차로 판정한다.
            }

            matched[i] = EvaluateRule(Rules[i], propertyValue, msg, ctx);
            if (matched[i] && !CheckAll)
            {
                break;
            }
        }

        var anyMatchedSoFar = matched.Any(m => m);
        for (var i = 0; i < Rules.Count; i++)
        {
            if (Rules[i].Operator == "else")
            {
                matched[i] = !anyMatchedSoFar;
            }
        }

        for (var i = 0; i < Rules.Count; i++)
        {
            if (!matched[i])
            {
                continue;
            }

            await ctx.RouteAsync(Id, i, msg.Clone(), ct);
            if (!CheckAll)
            {
                break;
            }
        }
    }

    /// <summary><paramref name="rule"/> 하나가 <paramref name="propertyValue"/>에 대해 맞는지 판정합니다(<c>else</c>는 <see cref="OnInputAsync"/>가 2차로 따로 처리하므로 여기서는 항상 <c>false</c>).</summary>
    private static bool EvaluateRule(SwitchRule rule, object? propertyValue, Msg msg, INodeContext ctx)
    {
        var compareValue = rule.CompareValue is not null ? TypedValueEvaluator.Resolve(rule.CompareValue, msg, ctx) : null;

        return rule.Operator switch
        {
            "eq" => ValueComparer.LooseEquals(propertyValue, compareValue),
            "neq" => !ValueComparer.LooseEquals(propertyValue, compareValue),
            "lt" => ValueComparer.Compare(propertyValue, compareValue) < 0,
            "lte" => ValueComparer.Compare(propertyValue, compareValue) <= 0,
            "gt" => ValueComparer.Compare(propertyValue, compareValue) > 0,
            "gte" => ValueComparer.Compare(propertyValue, compareValue) >= 0,
            "btwn" => EvaluateBetween(rule, propertyValue, msg, ctx),
            "cont" => Contains(propertyValue, compareValue),
            "regex" => EvaluateRegex(propertyValue, compareValue, rule.CaseSensitive),
            "true" => IsBoolean(propertyValue, expected: true),
            "false" => IsBoolean(propertyValue, expected: false),
            "null" => propertyValue is null,
            "nnull" => propertyValue is not null,
            "empty" => IsEmpty(propertyValue),
            "nempty" => !IsEmpty(propertyValue),
            "istype" => MatchesType(propertyValue, compareValue as string),
            _ => false, // "else" 및 이 Step 범위 밖 연산자(head/tail/index/jsonata_exp)는 매치하지 않음
        };
    }

    private static bool EvaluateBetween(SwitchRule rule, object? propertyValue, Msg msg, INodeContext ctx)
    {
        if (rule.CompareValue is null || rule.CompareValue2 is null)
        {
            return false;
        }

        var low = TypedValueEvaluator.Resolve(rule.CompareValue, msg, ctx);
        var high = TypedValueEvaluator.Resolve(rule.CompareValue2, msg, ctx);
        return ValueComparer.Compare(propertyValue, low) >= 0 && ValueComparer.Compare(propertyValue, high) <= 0;
    }

    private static bool Contains(object? propertyValue, object? compareValue)
    {
        var haystack = propertyValue?.ToString() ?? string.Empty;
        var needle = compareValue?.ToString() ?? string.Empty;
        return haystack.Contains(needle, StringComparison.Ordinal);
    }

    private static bool EvaluateRegex(object? propertyValue, object? compareValue, bool caseSensitive)
    {
        var pattern = compareValue?.ToString();
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        var input = propertyValue?.ToString() ?? string.Empty;
        var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        try
        {
            return Regex.IsMatch(input, pattern, options);
        }
        catch (RegexParseException)
        {
            return false; // 잘못된 정규식 패턴 — 매치하지 않는 것으로 안전하게 처리
        }
    }

    private static bool IsBoolean(object? value, bool expected) => value switch
    {
        bool b => b == expected,
        string s when bool.TryParse(s, out var parsed) => parsed == expected,
        _ => false,
    };

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string s => s.Length == 0,
        System.Collections.ICollection c => c.Count == 0,
        System.Collections.IEnumerable e => !e.Cast<object?>().Any(),
        _ => false,
    };

    /// <summary>istype 연산자 — 값의 실제 CLR 타입을 Node-RED 스타일 타입 이름(string/number/boolean/array/object/null/undefined)과 비교합니다(간이 매핑, buffer/json 등 세부 타입은 지원하지 않음).</summary>
    private static bool MatchesType(object? value, string? typeName) => typeName switch
    {
        "string" => value is string,
        "number" => value is int or long or double or float or decimal,
        "boolean" => value is bool,
        "array" => value is System.Collections.IEnumerable and not string,
        "null" or "undefined" => value is null,
        "object" => value is not null && value is not string && value is not System.Collections.IEnumerable,
        _ => false,
    };
}
