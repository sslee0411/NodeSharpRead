using System.Collections.Concurrent;

namespace NodeSharp.Drivers.Modbus;

/// <summary>
/// Class명 : 인메모리 양방향 스트림
/// 역활 및 기능 : 메모리 큐 2개로 만든 양방향 Stream 페어 — 실제 소켓/시리얼 포트 없이 바이트를 주고받는 두 끝을 제공
///
/// (PD-01c) <see cref="CreatePair"/>가 반환하는 두 <see cref="Stream"/> 중 한쪽에 쓰면 반대쪽이 그대로
/// 읽습니다. <see cref="VirtualModbusSlave"/>가 <see cref="ModbusDriver"/>와 실제 TCP 소켓 없이 대화하는
/// 통로로 씁니다(PD-01b <c>ModbusDriverRtuTests.cs</c>가 테스트 전용으로 먼저 구현했던 것과 동일한
/// 구조를 여기로 승격 — 이제 프로덕션·테스트 양쪽에서 재사용).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>스레드 사용</b>: <see cref="BlockingCollection{T}"/>의 블로킹 대기를 <see cref="Task.Run(Func{Task}, CancellationToken)"/>으로
/// 스레드풀 스레드에서 수행합니다 — 실제 네트워크 I/O가 아니라 인메모리 시뮬레이터용이라 처리량보다
/// 단순성·정확성을 우선한 선택입니다(성능이 중요해지면 <c>System.Threading.Channels</c> 등으로 교체
/// 가능하다는 점을 여기 남겨둡니다).</item>
/// <item><b>부분 읽기 허용</b>: 실제 <see cref="System.Net.Sockets.NetworkStream"/>/<see cref="System.IO.Ports.SerialPort.BaseStream"/>과
/// 마찬가지로 <see cref="ReadAsync(Memory{byte}, CancellationToken)"/>는 요청한 바이트 수를 다 채우지 않고도
/// 반환할 수 있습니다 — 호출 측이 <c>ReadExactAsync</c> 류로 반복 호출해야 합니다.</item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// var (a, b) = InMemoryDuplexStream.CreatePair();
/// await a.WriteAsync(new byte[] { 1, 2, 3 });
/// var buf = new byte[3];
/// await b.ReadAsync(buf); // { 1, 2, 3 } — a가 쓴 것을 b가 읽음
/// </code>
/// </example>
internal sealed class InMemoryDuplexStream : Stream
{
    private readonly BlockingCollection<byte> _readQueue;
    private readonly BlockingCollection<byte> _writeQueue;

    private InMemoryDuplexStream(BlockingCollection<byte> readQueue, BlockingCollection<byte> writeQueue)
    {
        _readQueue = readQueue;
        _writeQueue = writeQueue;
    }

    /// <summary>새 양방향 스트림 페어를 만듭니다 — A에 쓰면 B가 읽고, B에 쓰면 A가 읽습니다.</summary>
    public static (Stream A, Stream B) CreatePair()
    {
        var aToB = new BlockingCollection<byte>();
        var bToA = new BlockingCollection<byte>();
        Stream a = new InMemoryDuplexStream(readQueue: bToA, writeQueue: aToB);
        Stream b = new InMemoryDuplexStream(readQueue: aToB, writeQueue: bToA);
        return (a, b);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        var first = await Task.Run(() => _readQueue.Take(cancellationToken), cancellationToken).ConfigureAwait(false);
        buffer.Span[0] = first;
        var read = 1;
        while (read < buffer.Length && _readQueue.TryTake(out var next))
        {
            buffer.Span[read] = next;
            read++;
        }

        return read;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        foreach (var b in buffer.Span)
        {
            _writeQueue.Add(b, cancellationToken);
        }

        return ValueTask.CompletedTask;
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
