using System.Reflection;
using NodeSharp.Contracts.Interfaces;

namespace NodeSharp.Registry;

/// <summary>
/// Class명 : 노드 Id 바인더
/// 역활 및 기능 : 반사(reflection)로 IFlowNode 인스턴스의 Id를 NodeConfig.Id와 맞춰주는 아주 작은 도우미
///
/// <see cref="IFlowNode.Id"/>는 인터페이스에 <c>{ get; }</c>만 선언돼 있어(생성 방법을 정의하지 않는다는
/// 02번 문서 2번 탭 카드1 원칙), 인터페이스 타입만으로는 값을 채울 방법이 없습니다. 하지만 실제 구현
/// 클래스들은 관례적으로 <c>{ get; init; }</c>로 선언합니다(<c>IFlowNode</c> XML 주석의
/// <c>PassThroughNode</c> 예제 참고) — <c>init</c> 접근자도 CLR 수준에서는 평범한 setter 메서드라
/// (컴파일러가 "객체 초기화 구문 밖에서 못 쓴다"고 막는 것은 C# 컴파일 시점 검사일 뿐), 반사로는 생성자
/// 밖에서도 호출할 수 있습니다. <see cref="NodeTypeRegistry.CreateInstance"/>(레거시 <c>Type</c> 등록
/// 경로)와 <see cref="NodeTypeDescriptorBuilder{TNode}"/>의 기본 팩토리가 공통으로 이 도우미를 씁니다
/// (RG-01, 오래 미뤄뒀던 "<c>IFlowNode.Id</c>를 <c>NodeConfig.Id</c>와 동기화" 완료 기준).
/// </summary>
/// <remarks>
/// 대상 타입에 <c>Id</c> 프로퍼티가 없거나 setter/init이 없으면(드물게 완전히 읽기 전용으로 만든 타입)
/// 조용히 아무 일도 하지 않습니다 — 이미 확정된 계약(<c>IFlowNode.Id</c>는 <c>{ get; }</c>만 요구)을
/// 넘어서는 강제는 하지 않는다는 원칙입니다.
/// </remarks>
internal static class NodeIdBinder
{
    /// <summary>
    /// <paramref name="node"/>의 <see cref="IFlowNode.Id"/> 프로퍼티에 <paramref name="id"/>를 반사로
    /// 채웁니다. <c>{ get; init; }</c>/<c>{ get; set; }</c> 어느 쪽이든 동작하며, 세터가 없으면 무시합니다.
    /// </summary>
    public static void Bind(IFlowNode node, string id)
    {
        var property = node.GetType().GetProperty(
            nameof(IFlowNode.Id), BindingFlags.Public | BindingFlags.Instance);

        if (property is not null && property.CanWrite)
        {
            property.SetValue(node, id);
        }
    }
}
