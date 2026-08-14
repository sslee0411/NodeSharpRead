// NodeSharp.Runner 진입점(RN-B0) — Generic Host로 DI 컨테이너만 구성하고 Worker를 백그라운드
// 서비스로 등록한다. 실제 flows.json 로딩 등 기동 로직은 RN-01에서 Worker.ExecuteAsync 안에
// 채워질 예정(02번 문서 3번 탭 카드8). RN-03a(Windows Service 설치)도 이 Generic Host 구조를
// 그대로 재사용한다(별도 재설계 불필요).
// (RN-04a) /health 엔드포인트(02번 문서 7번 탭 카드11)를 노출하기 위해 Host.CreateApplicationBuilder
// 대신 WebApplication.CreateBuilder로 전환했다 — WebApplication은 Generic Host의 상위 호환이라
// AddHostedService<Worker>() 등록은 그대로 유지되고 Kestrel 미니멀 API(app.MapGet)만 추가된
// 것이라 Worker/StartupSequencer/FlowDeployer의 기존 동작은 바뀌지 않는다. NodeSharp.Runner.csproj에
// <FrameworkReference Include="Microsoft.AspNetCore.App" />를 추가해 Sdk=Microsoft.NET.Sdk를
// 유지한 채(Sdk.Web 전환 없이) ASP.NET Core API만 쓸 수 있게 했다.
// 버그 수정(2026-08-02, RN-04a 빌드 확인 중 발견): builder.WebHost.UseUrls(...) 호출에서 CS1061
// ('ConfigureWebHostBuilder'에 UseUrls 정의 없음) 보고 — UseUrls 확장 메서드가
// Microsoft.AspNetCore.Hosting 네임스페이스에 선언돼 있는데 using을 빠뜨린 실수(AddHostedService가
// Microsoft.Extensions.Hosting이 아니라 DependencyInjection 네임스페이스였던 이전 버그와 같은 유형).
// using Microsoft.AspNetCore.Hosting; 한 줄 추가로 수정 — 이 CS1061 때문에 NodeSharp.Runner
// 프로젝트 빌드 전체가 실패해, 이를 참조하는 NodeSharp.Tests에서 NodeSharp.Runner.Health
// 네임스페이스를 못 찾는 CS0234 연쇄 오류도 함께 났었다(Health 폴더 자체는 문제 없었음).
// (RN-06a) CrashDumpCollector.Register()를 0번 단계로 추가 — 처리되지 않은 예외가 나면 덤프를
// 남기고 이벤트 로그에도 기록한다(02번 문서 7번 탭 카드14). 다른 어떤 코드보다 먼저 등록해야
// 그 이후에 나는 모든 예외를 잡을 수 있어 builder 생성보다도 앞에 둔다.
// (LK-02a) builder.Services.AddSignalR()/app.MapHub<MonitorHub>(...) 추가 — 02번 문서 7번 탭
// 카드2의 MonitorHub를 실제 엔드포인트로 노출한다. /health와 같은 Kestrel(같은 포트 47500)에
// 얹으므로 별도 프로세스나 포트가 필요 없다(RN-04a가 이미 확보한 WebApplication 위에 얹는
// 방식, 02번 문서 7번 탭 port 표 "47500 | /health·SignalR 모니터링 스트림").
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NodeSharp.Runner;
using NodeSharp.Runner.Core;
using NodeSharp.Runner.Diagnostics;
using NodeSharp.Runner.Health;

// 0) 크래시 덤프 수집기 등록(RN-06a) — 이 프로그램이 뭘 하기도 전에 가장 먼저 실행돼야
//    이후 어디서 예외가 나도 놓치지 않고 덤프·이벤트 로그를 남길 수 있다.
CrashDumpCollector.Register();

// 1) "설계도"를 만드는 단계 — 아직 서버는 안 켜졌고, 무엇을 켤지 준비만 하는 단계다.
//    builder는 "이 프로그램에 뭘 넣을지"를 계속 등록받는 그릇이라고 보면 된다.
var builder = WebApplication.CreateBuilder(args);

// 2) Worker(백그라운드 작업자, RN-B0/RN-01a/RN-02에서 만든 그 클래스)를 등록.
//    서버가 켜지면 이 Worker.ExecuteAsync()가 자동으로 백그라운드에서 실행된다
//    (flows.json 읽기 → 배포하는 그 로직).
builder.Services.AddHostedService<Worker>();

// 3) RunnerHealthState(가동시간·배포 노드 수 등을 기억하는 상태 저장소, RN-04a)를 등록.
//    "Singleton" = 프로그램이 켜져있는 동안 딱 1개만 만들어서 계속 재사용한다는 뜻.
//    Worker와 /health 둘 다 이 같은 1개의 인스턴스를 나눠 쓰게 된다.
builder.Services.AddSingleton<RunnerHealthState>();

// 3-1) (LK-02a) SignalR 서비스 + StatusBroadcaster(FlowEngine 이벤트를 MonitorHub로 중계하는
//      구독자, DI가 IHubContext<MonitorHub>를 자동으로 만들어 넣어준다) 등록. Worker 생성자가
//      StatusBroadcaster?를 선택적으로 받으므로, 이 등록이 없어도(예: 과거 테스트 환경) Worker는
//      SignalR 없이 그대로 동작한다(하위 호환).
builder.Services.AddSignalR();
builder.Services.AddSingleton<StatusBroadcaster>();

// 4) 이 서버가 어느 주소:포트로 열릴지 지정. localhost(내 컴퓨터 안에서만 접속 가능)로
//    한정해 외부 네트워크에서는 접속할 수 없게 막는다(기본 포트 47500, 02번 문서 7번 탭 카드11).
builder.WebHost.UseUrls("http://localhost:47500");

// 5) 위에서 등록한 내용(builder)을 바탕으로 실제 앱(app)을 조립 — 이 시점부터 app이 진짜 실행 가능한 객체가 된다.
var app = builder.Build();

// 6) "/health"라는 주소로 GET 요청이 들어오면, RunnerHealthState의 현재 상태(Snapshot)를
//    그대로 JSON으로 돌려준다 — 이게 이 파일의 핵심 기능(헬스체크 엔드포인트).
//    예: 브라우저에서 http://localhost:47500/health 접속하면 이 결과가 보인다.
app.MapGet("/health", (RunnerHealthState health) => health.Snapshot());

// 6-1) (LK-02a) "/hubs/monitor" 경로로 SignalR 연결을 받는다 — Editor의 EditorMonitorClient(LK-02b)가
//      이 경로로 접속해 nodeStatus/flowActivity/debugMessage/nodeError 이벤트를 실시간으로 받는다.
//      인증은 아직 없음(로컬호스트 전용 바인딩으로만 보호 — 토큰 인증은 LK-03 몫, MonitorHub 클래스
//      문서 참고).
app.MapHub<MonitorHub>("/hubs/monitor");

// 7) 서버를 실제로 켜고, 종료 신호(Ctrl+C 등)가 올 때까지 계속 대기한다.
//    이 줄이 실행되는 순간부터 Worker도 백그라운드에서 돌고, /health·SignalR도 응답을 시작한다.
await app.RunAsync();
