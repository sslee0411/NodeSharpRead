using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis.Scripting;
using NodeSharp.Contracts.Models;
using NodeSharp.Nodes.Function;
using Xunit;

namespace NodeSharp.Tests;

/// <summary>
/// <see cref="RoslynFunctionExecutor"/>(FN-02, 03번 개발 Step맵 Phase 7 — Function 노드 Roslyn C# 코드
/// 실행기 + 컴파일 캐시)에 대한 단위 테스트입니다. 완료 기준(03번 Step맵 FN-02): "최초 실행 시 컴파일
/// 지연 후 동일 코드 재실행 시 캐시로 지연이 사라지는지, 문법 오류는 컴파일 에러로 표면화되는지,
/// LL-11a ScaleExtensions를 using 없이 직접 호출 가능한지, 화이트리스트 밖 네임스페이스는 컴파일은
/// 되지만 FN-04 Linter 경고가 뜨는지 확인" — 이 중 "LL-11a ScaleExtensions" 검증은 LL-08a/LL-11a가
/// 아직 ⏳ 대기라 이 Step 범위 밖입니다(대신 NodeSharp.Util 어셈블리 참조 자체가 동작하는지를
/// SemVer로 검증), "FN-04 Linter 경고" 검증도 FN-04가 아직 없어 범위 밖입니다(RoslynFunctionExecutor.cs
/// XML 문서 참고). 이 클래스는 나머지 검증 가능한 항목(컴파일 캐시 재사용, 컴파일 오류 표면화, 실제
/// C# 문법 실행)을 다룹니다.
/// </summary>
public class RoslynFunctionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync는_사칙연산_코드를_계산해_Payload에_저장한다()
    {
        var executor = new RoslynFunctionExecutor();
        executor.Prepare("msg.payload = (double)msg.payload * 2; return msg;");

        var msg = new Msg { Payload = 21.0 };
        var result = await executor.ExecuteAsync(msg, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(42.0, Convert.ToDouble(result!.Payload));
    }

    [Fact]
    public async Task ExecuteAsync는_조건문으로_topic을_변경할_수_있다()
    {
        var executor = new RoslynFunctionExecutor();
        executor.Prepare("if ((double)msg.payload > 100) msg.topic = \"고온 경고\"; return msg;");

        var msg = new Msg { Payload = 150.0 };
        var result = await executor.ExecuteAsync(msg, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("고온 경고", result!.Topic);
    }

    [Fact]
    public async Task ExecuteAsync는_반복문_등_완전한_C샵_문법을_지원한다()
    {
        var executor = new RoslynFunctionExecutor();
        executor.Prepare(
            "double sum = 0; " +
            "for (int i = 1; i <= 5; i++) { sum += i; } " +
            "msg.payload = sum; " +
            "return msg;");

        var result = await executor.ExecuteAsync(new Msg(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(15.0, Convert.ToDouble(result!.Payload)); // 1+2+3+4+5
    }

    [Fact]
    public async Task ExecuteAsync는_return_null이면_결과가_null이라_필터링된다()
    {
        var executor = new RoslynFunctionExecutor();
        executor.Prepare("return null;");

        var result = await executor.ExecuteAsync(new Msg(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteAsync는_NodeSharp_Util_어셈블리의_타입을_참조_화이트리스트로_호출할_수_있다()
    {
        // 참조 화이트리스트(RoslynFunctionExecutor.AllowedReferences)에 NodeSharp.Util 어셈블리가
        // 실제로 포함돼 있어, using 없이도 전체 이름(NodeSharp.Util.SemVer)으로 호출 가능함을 확인
        // — ScaleExtensions(LL-08a)는 아직 없어 이미 존재하는 SemVer로 대신 검증(클래스 XML 문서 참고).
        var executor = new RoslynFunctionExecutor();
        executor.Prepare("msg.payload = NodeSharp.Util.SemVer.IsCompatible(\"1.0.0\", \"1.2.0\"); return msg;");

        var result = await executor.ExecuteAsync(new Msg(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True((bool)result!.Payload!);
    }

    [Fact]
    public void Prepare는_문법_오류_코드면_CompilationErrorException을_던진다()
    {
        var executor = new RoslynFunctionExecutor();
        var invalidCode = $"return msg + ; // 문법 오류, cache-key-{Guid.NewGuid():N}";

        Assert.Throws<CompilationErrorException>(() => executor.Prepare(invalidCode));
    }

    [Fact]
    public void Prepare는_동일_코드로_두_번_호출해도_같은_컴파일_결과를_재사용한다()
    {
        // 완료 기준 "최초 실행 시 컴파일 지연 후 동일 코드 재실행 시 캐시로 지연이 사라지는지"를
        // 시간 측정 대신 캐시 델리게이트의 참조 동일성으로 직접 검증 — 참조가 같다면 두 번째
        // Prepare 호출이 재컴파일하지 않고 캐시를 그대로 재사용했다는 뜻이다. 다른 테스트와 정적
        // 캐시(CompileCache)를 공유해도 충돌하지 않도록 코드 문자열에 매번 새 Guid를 섞는다.
        var uniqueCode = $"return msg; // cache-key-{Guid.NewGuid():N}";
        var cacheField = typeof(RoslynFunctionExecutor).GetField("CompileCache", BindingFlags.NonPublic | BindingFlags.Static)!;
        var cache = (ConcurrentDictionary<string, ScriptRunner<object>>)cacheField.GetValue(null)!;

        var executorA = new RoslynFunctionExecutor();
        executorA.Prepare(uniqueCode);
        Assert.True(cache.ContainsKey(uniqueCode));
        var runnerAfterFirstCompile = cache[uniqueCode];

        var executorB = new RoslynFunctionExecutor(); // 다른 인스턴스지만 같은 코드 문자열
        executorB.Prepare(uniqueCode);

        Assert.Same(runnerAfterFirstCompile, cache[uniqueCode]); // 참조 동일 = 재컴파일되지 않고 캐시가 재사용됨
    }
}
