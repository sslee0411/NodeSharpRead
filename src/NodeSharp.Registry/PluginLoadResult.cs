namespace NodeSharp.Registry;

/// <summary>
/// Class명 : 플러그인 로드 결과
/// 역활 및 기능 : NodeTypeRegistry.LoadPlugins 한 번 호출로 처리한 dll 개수·성공/실패 내역을 담는 요약값
///
/// <see cref="NodeTypeRegistry.LoadPlugins"/> 한 번 호출로 처리한 결과 요약입니다(RG-02a+02b).
/// <see cref="FilesFound"/>는 디렉터리에서 찾은 <c>*.dll</c> 개수(RG-02a — 아직 로딩 전 단계),
/// <see cref="LoadedSuccessfully"/>는 그중 실제로 격리 로드에 성공한 개수(RG-02b),
/// <see cref="DescriptorsRegistered"/>는 새로 등록된 <c>INodeTypeDescriptor</c> 개수(RG-01
/// <c>NodeTypeRegistry.ScanAssembly</c> 결과 누적)입니다. <see cref="Failures"/>는 손상되었거나
/// 로드에 실패한 dll의 파일명과 예외 메시지를 담아, dll 1개의 실패가 나머지 로딩을 막지 않았음을
/// (실패 격리 원칙, <c>PluginLoader.cs</c> XML 주석 참고) 호출 측이 확인할 수 있게 합니다.
/// </summary>
public sealed record PluginLoadResult(
    int FilesFound,
    int LoadedSuccessfully,
    int DescriptorsRegistered,
    IReadOnlyList<string> Failures);
