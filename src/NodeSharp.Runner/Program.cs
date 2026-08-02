// NodeSharp.Runner 진입점(RN-B0) — Generic Host로 DI 컨테이너만 구성하고 Worker를 백그라운드
// 서비스로 등록한다. 실제 flows.json 로딩 등 기동 로직은 RN-01에서 Worker.ExecuteAsync 안에
// 채워질 예정(02번 문서 3번 탭 카드8). RN-03a(Windows Service 설치)도 이 Generic Host 구조를
// 그대로 재사용한다(별도 재설계 불필요).
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeSharp.Runner;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

using var host = builder.Build();
await host.RunAsync();
