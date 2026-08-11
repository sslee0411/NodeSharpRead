using NodeSharp.Contracts.Enums;
using NodeSharp.Contracts.Interfaces;
using NodeSharp.Contracts.Models;
using NodeSharp.Runtime;
using NodeSharp.Util.Messaging;
using NodeSharp.Util.Evaluation;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="TypedValueEvaluator"/>(NR-04, NodeSharp.Util.Evaluation)에 대한 단위 테스트입니다.
/// <see cref="TypedValueSource"/> 6종 중 5종(EnvVar 제외 — NR-10b로 이연)의 해석이 올바른지, msg 필드의
/// 중첩 경로(<c>"payload.temp"</c>)까지 찾아내는지 확인합니다. NR-04 완료 기준의 "비교값을 TypedValue의
/// MsgField/FlowContext Source로 설정해도 정상 비교되는지"를 이 클래스가 실질적으로 증명합니다.
/// </summary>
public class TypedValueEvaluatorTests
{
    /// <summary>실제 <see cref="NodeContext"/>(RT-09b 구현체)를 만들어 Flow/Global Context가 실제로 동작하는 상태로 테스트합니다.</summary>
    private static INodeContext BuildRealContext()
    {
        var store = new InMemoryContextStore();
        var eventBus = new EventBusAdapter(new EventBus());
        var registry = new NodeSharp.Registry.NodeTypeRegistry(contractsVersion: "1.0.0");
        var engine = new FlowEngine(registry, store, eventBus);
        return new NodeContext(engine, eventBus, store, flowId: "f1", nodeId: "n1");
    }

    [Fact]
    public void Fixed_Source는_저장된_문자열을_그대로_반환한다()
    {
        var ctx = BuildRealContext();
        var msg = new Msg();
        var result = TypedValueEvaluator.Resolve(new TypedValue(TypedValueSource.Fixed, "85"), msg, ctx);
        Assert.Equal("85", result);
    }

    [Fact]
    public void MsgField_Source는_최상위_필드를_읽는다()
    {
        var ctx = BuildRealContext();
        var msg = new Msg { Payload = 42 };
        var result = TypedValueEvaluator.Resolve(new TypedValue(TypedValueSource.MsgField, "payload"), msg, ctx);
        Assert.Equal(42, result);
    }

    [Fact]
    public void MsgField_Source는_점_표기로_중첩_필드도_읽는다()
    {
        var ctx = BuildRealContext();
        var msg = new Msg { Payload = new Dictionary<string, object?> { ["temp"] = 21.5 } };
        var result = TypedValueEvaluator.Resolve(new TypedValue(TypedValueSource.MsgField, "payload.temp"), msg, ctx);
        Assert.Equal(21.5, result);
    }

    [Fact]
    public void MsgField_Source는_없는_필드는_null을_반환한다()
    {
        var ctx = BuildRealContext();
        var msg = new Msg();
        var result = TypedValueEvaluator.Resolve(new TypedValue(TypedValueSource.MsgField, "doesNotExist"), msg, ctx);
        Assert.Null(result);
    }

    [Fact]
    public void FlowContext_Source는_ctx_Flow에_저장된_값을_읽는다()
    {
        var ctx = BuildRealContext();
        ctx.Flow.Set("threshold", 80.0);
        var msg = new Msg();
        var result = TypedValueEvaluator.Resolve(new TypedValue(TypedValueSource.FlowContext, "threshold"), msg, ctx);
        Assert.Equal(80.0, result);
    }

    [Fact]
    public void GlobalContext_Source는_ctx_Global에_저장된_값을_읽는다()
    {
        var ctx = BuildRealContext();
        ctx.Global.Set("siteId", "P1");
        var msg = new Msg();
        var result = TypedValueEvaluator.Resolve(new TypedValue(TypedValueSource.GlobalContext, "siteId"), msg, ctx);
        Assert.Equal("P1", result);
    }

    [Fact]
    public void Expression_Source는_SimpleExpressionEvaluator로_계산한다()
    {
        var ctx = BuildRealContext();
        var msg = new Msg { Payload = 10.0 };
        var result = TypedValueEvaluator.Resolve(new TypedValue(TypedValueSource.Expression, "payload + 5"), msg, ctx);
        Assert.Equal(15.0, result);
    }

    [Fact]
    public void EnvVar_Source는_NR_10b_전까지_NotSupportedException을_던진다()
    {
        var ctx = BuildRealContext();
        var msg = new Msg();
        Assert.Throws<NotSupportedException>(() =>
            TypedValueEvaluator.Resolve(new TypedValue(TypedValueSource.EnvVar, "MAX_TEMP"), msg, ctx));
    }
}
