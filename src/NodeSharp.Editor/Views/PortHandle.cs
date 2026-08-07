namespace NodeSharp.Editor.Views;

/// <summary>
/// Class명 : 포트 식별자
/// 역활 및 기능 : 캔버스 위 포트 Ellipse 하나가 어느 노드의 몇 번째 입력/출력 포트인지 식별하는 값
///
/// 포트 Ellipse의 <c>Tag</c>에 담아두고, 마우스 이벤트에서 "지금 가리키는 포트가 어느 노드의 몇
/// 번째 입력/출력 포트인지"를 되짚는 용도로만 씁니다(EC-02). <c>record</c>라서 두 핸들이 같은
/// 노드·같은 번호·같은 방향이면 값으로 같다고 판정됩니다(드롭 위치 판정에 사용).
/// </summary>
public sealed record PortHandle(string NodeId, int PortIndex, bool IsOutput);
